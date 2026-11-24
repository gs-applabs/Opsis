using System;
using System.Collections.Generic;
using System.IO;
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
                    new FileInfo(path).Length > 128 * 1024) continue;
                manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(path),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (manifest == null || manifest.Schema != 1 || !SafeToken(manifest.PluginId) ||
                    manifest.Contributions == null || manifest.Contributions.Count > 100)
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
        var extension = Path.GetExtension(item.AssetPath);
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
                Width = imported.Width, Height = imported.Height
            };
            OpsisWidgetDesigner.Validate(data);
            return data;
        }
        var widget = JsonSerializer.Deserialize<OpsisWidgetDesigner.Layout>(
            File.ReadAllText(selected.AssetPath))
            ?? throw new InvalidDataException("Widget contribution not valid JSON.");
        OpsisWidgetDesigner.Validate(widget);
        return widget;
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
