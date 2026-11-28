using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Linq;
using System.Text.Json;

namespace MotionDesk.Services;

/// <summary>
/// Unified read-only catalog of declarative plugin contributions. Wallpapers and
/// widgets use the same vocabulary as all built-in Opsis assets. No DLLs, scripts
/// or untrusted code are executed while cataloguing contributions.
/// </summary>
public static class OpsisEcosystemGallery
{
    private static readonly HashSet<string> Types = new(StringComparer.OrdinalIgnoreCase)
        { "wallpaper", "widget", "theme", "icon-pack", "cursor-pack", "automation-template", "layout" };

    public sealed class Contribution
    {
        public string Id { get; set; } = "";
        public string Type { get; set; } = "";
        public string Category { get; set; } = "";
        public string Title { get; set; } = "";
        public string Asset { get; set; } = "";
        public string Author { get; set; } = "";
        public string Description { get; set; } = "";
    }

    public sealed class Manifest
    {
        public int Schema { get; set; } = 1;
        public string PluginId { get; set; } = "";
        public string Name { get; set; } = "";
        public string Version { get; set; } = "0.0.0";
        public List<Contribution> Contributions { get; set; } = new();
    }

    public sealed record Item(
        string PluginId, string Id, string Type, string Category, string Title,
        string Author, string Description, string AssetPath, string Origin, string Version = "0.0.0");

    private static bool SafeToken(string? value) =>
        value != null && value.Length is >= 1 and <= 80 &&
        value.All(x => char.IsAsciiLetterOrDigit(x) || x is '.' or '_' or '-');

    private static bool SafeRelative(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 220 || Path.IsPathRooted(value) ||
            value.Contains(':') || value.Contains('\\')) return false;
        var parts = value.Split('/');
        return parts.All(x => x.Length > 0 && x != "." && x != "..") &&
            value.All(x => x >= 32);
    }

    // The plugin host is the authoritative directory discovery mechanism,
    // including HKCU registered plugin installers, user storage, and protected
    // install roots. Previously the Gallery scanned only two roots and missed
    // installed plugins registered under PluginPaths.
    private static IEnumerable<(string Dir, string Origin)> ContributorDirs()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (dir, trustedRoot) in PluginHost.EnumerateCandidateFolders().Take(400))
        {
            if (seen.Add(Path.GetFullPath(dir)))
                yield return (dir, trustedRoot ? "installed" : "registered-plugin");
        }
        var gallery = Path.Combine(AppPaths.AppDataFolder, "gallery-packs");
        if (!Directory.Exists(gallery) ||
            (File.GetAttributes(gallery) & FileAttributes.ReparsePoint) != 0)
            yield break;
        foreach (var dir in Directory.EnumerateDirectories(gallery).Take(200))
        {
            if (seen.Add(Path.GetFullPath(dir)))
                yield return (dir, "gallery-pack");
        }
    }

    private static IEnumerable<(Manifest Data, string Dir, string Origin)> Manifests()
    {
        foreach (var (dir, origin) in ContributorDirs())
        {
            Manifest? manifest = null;
            try
            {
                if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0) continue;
                var path = Path.Combine(dir, "opsis-contributions.json");
                if (!File.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 ||
                    new FileInfo(path).Length > (origin == "gallery-pack" ? MaxManifestBytes : 128 * 1024)) continue;
                manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(path),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (manifest == null || manifest.Schema != 1 || !SafeToken(manifest.PluginId) ||
                    manifest.Contributions == null || manifest.Contributions.Count > (origin == "gallery-pack" ? MaxPackImages : 100))
                    continue;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or
                                      JsonException or ArgumentException or NotSupportedException)
            {
                continue; // isolate broken or unapproved *metadata*; never load its DLL.
            }
            if (manifest != null) yield return (manifest, dir, origin);
        }
    }

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp" };

    private static bool HasLinkedPathComponents(string root, string file)
    {
        var relative = Path.GetRelativePath(root, file);
        var current = root;
        foreach (var part in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            current = Path.Combine(current, part);
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return true;
        }
        return false;
    }

    /// <summary>
    /// Load a declarative Clock/Date/Text widget from an approved contribution.
    /// Never interpret HTML/JS, load executable modules or run arbitrary data sources.
    /// </summary>
    public static OpsisWidgetDesigner.Layout ReadWidgetContribution(Item item)
    {
        var extension = Path.GetExtension(item.AssetPath).ToLowerInvariant();
        if (!string.Equals(item.Type, "widget", StringComparison.OrdinalIgnoreCase) ||
            extension is not (".json" or ".ini"))
            throw new InvalidDataException("Only native JSON or supported Rainmeter INI widget metadata is permitted.");
        var selected = Discover().FirstOrDefault(x => x.Id == item.Id &&
            string.Equals(x.PluginId, item.PluginId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.AssetPath, item.AssetPath, StringComparison.OrdinalIgnoreCase));
        if (selected == null || new FileInfo(selected.AssetPath).Length > 32 * 1024)
            throw new InvalidDataException("Widget contribution missing or too large.");
        if (string.Equals(extension, ".ini", StringComparison.OrdinalIgnoreCase))
        {
            var imported = OpsisRainmeterSkinImporter.PreviewFile(selected.AssetPath);
            var data = new OpsisWidgetDesigner.Layout
            {
                Name = imported.Name, Binding = imported.Binding, Text = imported.Text,
                AccentHex = imported.AccentHex, FontSize = imported.FontSize,
                Width = imported.Width, Height = imported.Height, Rows = imported.Rows
            };
            OpsisWidgetDesigner.Validate(data);
            return data;
        }
        return OpsisWidgetDesigner.ReadPortable(selected.AssetPath);
    }

    /// <summary>
    /// Install an explicitly chosen declarative theme with SHA-256 integrity checks;
    /// hashes are NOT a digital publisher signature. Does not auto-apply settings.
    /// </summary>
    public static string InstallThemeContribution(Item item)
    {
        if (!string.Equals(item.Type, "theme", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetExtension(item.AssetPath), ".opsis-theme", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("This Gallery item is not an Opsis theme.");
        var selected = Discover().FirstOrDefault(x => x.Id == item.Id &&
            string.Equals(x.PluginId, item.PluginId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.AssetPath, item.AssetPath, StringComparison.OrdinalIgnoreCase));
        if (selected == null || new FileInfo(selected.AssetPath).Length > 1024 * 1024)
            throw new InvalidDataException("Theme contribution missing or too large.");
        return OpsisThemeBundle.Import(selected.AssetPath);
    }

    private static readonly object PackGate = new();
    private static readonly HashSet<string> PackImageExtensions = new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".webp" };
    public const int MaxPackImages = 200;
    private const long MaxPackBytes = 1024L * 1024 * 1024;
    private const int MaxManifestBytes = 512 * 1024;

    /// <summary>Install a data-only static wallpaper pack into the existing gallery.
    /// No scripts, DLLs, archives within archives or auto-application. Publish only
    /// after the complete manifest, bounded extraction and image checks succeed.</summary>
    internal static string ResolveShellPack(Item item)
    {
        if (item.Type is not ("icon-pack" or "cursor-pack") ||
            !Path.GetExtension(item.AssetPath).Equals(".json", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Select an icon or cursor pack.");
        var selected = Discover().FirstOrDefault(x => x == item);
        if (selected == null) throw new InvalidDataException("Gallery pack changed or is no longer present.");
        _ = ShellAssetPack.Preview(selected.AssetPath, item.Type == "cursor-pack");
        return selected.AssetPath;
    }

    public static int ImportWallpaperPack(string zipPath) => ImportDataPack(zipPath, allowWidgets: false);
    public static int ImportGalleryPack(string zipPath) => ImportDataPack(zipPath, allowWidgets: true);

    private static bool ValidPackAsset(Contribution item, bool allowWidgets)
    {
        if (!SafeRelative(item.Asset) || !SafeToken(Path.GetFileName(item.Asset))) return false;
        if (allowWidgets && item.Type is "icon-pack" or "cursor-pack")
        {
            var parts = item.Asset.Split('/');
            return parts.Length == 3 && parts[0] == "shell" && SafeToken(parts[1]) &&
                   parts[1] is not ("." or "..") && !parts[1].EndsWith('.') && parts[2] == "pack.json";
        }
        if (item.Asset.Count(c => c == '/') != 1) return false;
        string extension = Path.GetExtension(item.Asset);
        return item.Type == "wallpaper" && item.Asset.StartsWith("images/", StringComparison.Ordinal) &&
                   PackImageExtensions.Contains(extension) ||
               allowWidgets && item.Type == "widget" && item.Asset.StartsWith("widgets/", StringComparison.Ordinal) &&
                   extension.Equals(".json", StringComparison.OrdinalIgnoreCase);
    }

    private static int ImportDataPack(string zipPath, bool allowWidgets)
    {
        if (!Path.GetExtension(zipPath).Equals(".zip", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Expected a ZIP gallery pack.");
        if (!System.Threading.Monitor.TryEnter(PackGate))
            throw new InvalidOperationException("Another Gallery import is running.");
        try
        {
            using var archive = ZipFile.OpenRead(zipPath);
            if (archive.Entries.Count < 2 || archive.Entries.Count > MaxPackImages * (allowWidgets ? 7 : 1) + 1) throw new InvalidDataException("A gallery pack must contain one manifest and 1–200 supported assets.");
            var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
            long total = 0;
            foreach (var entry in archive.Entries)
            {
                var name = entry.FullName;
                if (!SafeRelative(name) || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000 ||
                    (entry.ExternalAttributes & 0x400) != 0 || !entries.TryAdd(name, entry))
                    throw new InvalidDataException("Unsafe or duplicate gallery ZIP entry.");
                if (entry.Length <= 0 || entry.Length > 32L * 1024 * 1024 || entry.Length > MaxPackBytes - total)
                    throw new InvalidDataException("Gallery ZIP size quota exceeded.");
                total += entry.Length;
            }
            if (!entries.TryGetValue("opsis-contributions.json", out var manifestEntry) || manifestEntry.Length > MaxManifestBytes)
                throw new InvalidDataException("Gallery manifest missing or too large.");
            byte[] manifestBytes = ReadEntry(manifestEntry);
            var manifest = JsonSerializer.Deserialize<Manifest>(manifestBytes, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidDataException("Invalid gallery manifest.");
            if (manifest.Schema != 1 || !SafeToken(manifest.PluginId) || manifest.PluginId is "." or ".." ||
                manifest.PluginId.EndsWith('.') || manifest.Contributions == null || (manifest.Contributions.Count < 1 || manifest.Contributions.Count > MaxPackImages) ||
                !Version.TryParse(manifest.Version, out _))
                throw new InvalidDataException("Invalid gallery pack identity or count.");
            // Use a fixed, generated folder name rather than trusting a manifest path.
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var assets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var expectedEntries = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "opsis-contributions.json" };
            foreach (var item in manifest.Contributions)
            {
                if (item == null || !SafeToken(item.Id) || !ids.Add(item.Id) || !ValidPackAsset(item, allowWidgets) ||
                    !assets.Add(item.Asset) || !entries.ContainsKey(item.Asset) ||
                    (item.Type == "widget" && entries[item.Asset].Length > 32768) ||
                    string.IsNullOrWhiteSpace(item.Title) || item.Title.Length > 120 ||
                    string.IsNullOrWhiteSpace(item.Category) || item.Category.Length > 60 ||
                    (item.Author?.Length ?? 0) > 120 || (item.Description?.Length ?? 0) > 1024)
                    throw new InvalidDataException("Invalid data-only Gallery contribution.");
                if (!expectedEntries.Add(item.Asset)) throw new InvalidDataException("Duplicate contribution path.");
                if (item.Type is "icon-pack" or "cursor-pack")
                {
                    if (entries[item.Asset].Length > 16384) throw new InvalidDataException("Shell manifest too large.");
                    var roles = ShellAssetPack.ParseRoles(ReadEntry(entries[item.Asset]), item.Type == "cursor-pack");
                    string prefix = item.Asset[..(item.Asset.LastIndexOf('/') + 1)];
                    foreach (var name in roles.Values.Distinct(StringComparer.OrdinalIgnoreCase))
                    {
                        string path = prefix + name;
                        if (!entries.TryGetValue(path, out var roleEntry) || roleEntry.Length > 8L * 1024 * 1024 ||
                            !expectedEntries.Add(path)) throw new InvalidDataException("Missing, oversized or shared shell pack asset.");
                    }
                }
            }
            if (expectedEntries.Count != entries.Count) throw new InvalidDataException("Gallery contains undeclared files.");
            if (Manifests().Any(x => string.Equals(x.Data.PluginId, manifest.PluginId, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("A gallery pack with this identity is already installed.");
            string root = Path.Combine(AppPaths.AppDataFolder, "gallery-packs");
            Directory.CreateDirectory(root);
            if (!PackagePaths.IsExistingUnlinkedPath(AppPaths.AppDataFolder, root)) throw new InvalidDataException("Linked gallery storage is not supported.");
            if (Directory.EnumerateDirectories(root).Take(200).Count() >= 200)
                throw new InvalidDataException("Gallery pack quota reached.");
            // Staging is a sibling, so Gallery cannot discover a partial manifest.
            string stage = Path.Combine(AppPaths.AppDataFolder, ".gallery-stage-" + Guid.NewGuid().ToString("N"));
            string destination = Path.Combine(root, "pack-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stage);
            try
            {
                Directory.CreateDirectory(Path.Combine(stage, "images"));
                if (allowWidgets) Directory.CreateDirectory(Path.Combine(stage, "widgets"));
                var hashes = new HashSet<string>(StringComparer.Ordinal);
                foreach (var item in manifest.Contributions)
                {
                    byte[] data = ReadEntry(entries[item.Asset]);
                    string hash = Convert.ToHexString(SHA256.HashData(data));
                    if (!hashes.Add(hash)) throw new InvalidDataException("Duplicate asset contents in the pack.");
                    string extension = Path.GetExtension(item.Asset).ToLowerInvariant();
                    if (item.Type == "widget")
                    {
                        var layout = JsonSerializer.Deserialize<OpsisWidgetDesigner.Layout>(data)
                            ?? throw new InvalidDataException("Invalid widget contribution.");
                        OpsisWidgetDesigner.Validate(layout);
                        string name = "widget-" + hashes.Count.ToString("D3") + ".json";
                        File.WriteAllBytes(Path.Combine(stage, "widgets", name), data);
                        item.Asset = "widgets/" + name;
                    }
                    else if (item.Type is "icon-pack" or "cursor-pack")
                    {
                        bool cursor = item.Type == "cursor-pack";
                        var roles = ShellAssetPack.ParseRoles(data, cursor);
                        string prefix = item.Asset[..(item.Asset.LastIndexOf('/') + 1)];
                        string relative = "shell/pack-" + hashes.Count.ToString("D3");
                        string target = Path.Combine(stage, relative.Replace('/', Path.DirectorySeparatorChar));
                        Directory.CreateDirectory(target);
                        var rewritten = new Dictionary<string,string>(StringComparer.Ordinal);
                        var copied = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var role in roles)
                        {
                            if (!copied.TryGetValue(role.Value, out string? name))
                            {
                                byte[] image = ReadEntry(entries[prefix + role.Value]);
                                string roleExtension = Path.GetExtension(role.Value).ToLowerInvariant();
                                ShellAssetPack.ValidateAssetBytes(image, roleExtension, cursor, allowAnimated: cursor);
                                name = "asset-" + (copied.Count + 1).ToString("D2") + roleExtension;
                                File.WriteAllBytes(Path.Combine(target, name), image);
                                copied.Add(role.Value, name);
                            }
                            rewritten.Add(role.Key, name);
                        }
                        File.WriteAllText(Path.Combine(target, "pack.json"), JsonSerializer.Serialize(new { schema = 1, kind = item.Type, roles = rewritten }));
                        item.Asset = relative + "/pack.json";
                    }
                    else
                    {
                        using (var decoded = WallpaperPackImage.Decode(data, extension)) { }
                        string name = "image-" + hashes.Count.ToString("D3") + (extension == ".jpeg" ? ".jpg" : extension);
                        File.WriteAllBytes(Path.Combine(stage, "images", name), data);
                        item.Asset = "images/" + name;
                    }
                }
                File.WriteAllText(Path.Combine(stage, "opsis-contributions.json"), JsonSerializer.Serialize(manifest));
                Directory.Move(stage, destination);
                return manifest.Contributions.Count;
            }
            finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
        }
        finally { System.Threading.Monitor.Exit(PackGate); }
    }

    private static byte[] ReadEntry(ZipArchiveEntry entry)
    {
        using var input = entry.Open();
        using var output = new MemoryStream((int)entry.Length);
        var buffer = new byte[81920];
        int count;
        while ((count = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (count > entry.Length - output.Length) throw new InvalidDataException("Gallery ZIP entry exceeds declared size.");
            output.Write(buffer, 0, count);
        }
        if (output.Length != entry.Length) throw new InvalidDataException("Gallery ZIP entry length mismatch.");
        return output.ToArray();
    }

    /// <summary>Apply only a selected, verified static image contribution. Never execute plugin code.</summary>
    public static void ApplyStaticWallpaper(Item item)
    {
        if (!string.Equals(item.Type, "wallpaper", StringComparison.OrdinalIgnoreCase) ||
            !ImageExtensions.Contains(Path.GetExtension(item.AssetPath)))
            throw new InvalidDataException("Only static image wallpaper contributions can be applied here.");
        var discovered = Discover().FirstOrDefault(x => x.Id == item.Id && x.PluginId == item.PluginId &&
            string.Equals(x.AssetPath, item.AssetPath, StringComparison.OrdinalIgnoreCase));
        if (discovered == null) throw new InvalidDataException("Wallpaper contribution no longer exists.");
        if (new FileInfo(discovered.AssetPath).Length > 64L * 1024 * 1024)
            throw new InvalidDataException("Wallpaper image too large.");
        OpsisRecoveryHub.Snapshot("Before applying Gallery wallpaper " + item.Id);
        var wallpaper = MotionDesk.Widgets.WallpaperSettings.Load();
        wallpaper.Mode = "Image";
        wallpaper.ImagePath = discovered.AssetPath;
        wallpaper.Save();
        MotionDesk.Widgets.WallpaperHostEngine.Instance.Enable();
        MotionDesk.Widgets.WallpaperHostEngine.Instance.RefreshAll();
    }

    /// <summary>Raw source identities for duplicate-conflict auditing before Gallery de-duplication.</summary>
    public static IReadOnlyList<(string PluginId, string ItemId, string Origin)> ContributionOrigins()
    {
        var results = new List<(string PluginId, string ItemId, string Origin)>();
        foreach (var (manifest, dir, origin) in Manifests())
        {
            foreach (var entry in manifest.Contributions)
                if (entry != null && SafeToken(entry.Id) && Types.Contains(entry.Type) &&
                    SafeRelative(entry.Asset))
                    results.Add((manifest.PluginId, entry.Id, origin + ":" + Path.GetFileName(dir)));
        }
        return results.Take(20000).ToArray();
    }

    private static bool IsNewer(string candidate, string existing) =>
        Version.TryParse(candidate, out var newVersion) &&
        (!Version.TryParse(existing, out var current) || newVersion > current);

    public static IReadOnlyList<Item> Discover()
    {
        var results = new Dictionary<string, Item>(StringComparer.OrdinalIgnoreCase);
        foreach (var (manifest, dir, origin) in Manifests())
        {
            foreach (var item in manifest.Contributions)
            {
                if (item == null || !SafeToken(item.Id) || !Types.Contains(item.Type) ||
                    !SafeRelative(item.Asset) || string.IsNullOrWhiteSpace(item.Title) ||
                    item.Title.Length > 120 || string.IsNullOrWhiteSpace(item.Category) ||
                    item.Category.Length > 60 || (item.Author?.Length ?? 0) > 120 ||
                    (item.Description?.Length ?? 0) > 1024) continue;
                try
                {
                    var filename = Path.GetFullPath(Path.Combine(
                        dir, item.Asset.Replace('/', Path.DirectorySeparatorChar)));
                    var basePath = Path.GetFullPath(dir) + Path.DirectorySeparatorChar;
                    if (!filename.StartsWith(basePath, StringComparison.OrdinalIgnoreCase) ||
                        !File.Exists(filename) || HasLinkedPathComponents(dir, filename) ||
                        new FileInfo(filename).Length > 500L * 1024 * 1024) continue;
                    var key = manifest.PluginId + ":" + item.Id;
                    var incoming = new Item(manifest.PluginId, item.Id, item.Type, item.Category,
                        item.Title, item.Author ?? "", item.Description ?? "", filename,
                        origin, manifest.Version ?? "0.0.0");
                    if (results.TryGetValue(key, out var existing) &&
                        !IsNewer(incoming.Version, existing.Version)) continue;
                    results[key] = incoming;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or
                                          ArgumentException or NotSupportedException)
                {
                    // Broken individual asset does not affect other plugins.
                }
            }
        }
        return results.Values.OrderBy(x => x.Type).ThenBy(x => x.Category)
            .ThenBy(x => x.Title).ToArray();
    }
}
