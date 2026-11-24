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
        public List<Contribution> Contributions { get; set; } = new();
    }

    public sealed record Item(
        string PluginId, string Id, string Type, string Category, string Title,
        string Author, string Description, string AssetPath, string Origin);

    private static bool SafeToken(string value) =>
        value.Length is >= 1 and <= 80 &&
        value.All(x => char.IsAsciiLetterOrDigit(x) || x is '.' or '_' or '-');

    private static bool SafeRelative(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 220 || Path.IsPathRooted(value) ||
            value.Contains(':') || value.Contains('\\')) return false;
        var parts = value.Split('/');
        return parts.All(x => x.Length > 0 && x != "." && x != "..") &&
            value.All(x => x >= 32);
    }

    private static IEnumerable<(string Root, string Origin)> Roots()
    {
        yield return (Path.Combine(AppContext.BaseDirectory, "plugins"), "installed");
        yield return (Path.Combine(AppPaths.AppDataFolder, "plugins"), "user");
        yield return (Path.Combine(AppPaths.AppDataFolder, "gallery-packs"), "gallery-pack");
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

    public static IReadOnlyList<Item> Discover()
    {
        var results = new Dictionary<string, Item>(StringComparer.OrdinalIgnoreCase);
        foreach (var (root, origin) in Roots())
        {
            if (!Directory.Exists(root) || (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) continue;
            foreach (var dir in Directory.EnumerateDirectories(root).Take(200))
            {
                try
                {
                    if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0) continue;
                    var manifestPath = Path.Combine(dir, "opsis-contributions.json");
                    if (!File.Exists(manifestPath) || new FileInfo(manifestPath).Length > 128 * 1024) continue;
                    var manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(manifestPath),
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (manifest == null || manifest.Schema != 1 || !SafeToken(manifest.PluginId) ||
                        manifest.Contributions?.Count > 100) continue;
                    foreach (var item in manifest.Contributions ?? new List<Contribution>())
                    {
                        if (!SafeToken(item.Id) || !Types.Contains(item.Type) ||
                            !SafeRelative(item.Asset) || item.Title.Length is < 1 or > 120 ||
                            item.Category.Length is < 1 or > 60) continue;
                        var filename = Path.GetFullPath(Path.Combine(dir, item.Asset.Replace('/', Path.DirectorySeparatorChar)));
                        var basePath = Path.GetFullPath(dir) + Path.DirectorySeparatorChar;
                        if (!filename.StartsWith(basePath, StringComparison.OrdinalIgnoreCase) || !File.Exists(filename)) continue;
                        if (HasLinkedPathComponents(dir, filename) ||
                            new FileInfo(filename).Length > 500L * 1024 * 1024) continue;
                        var key = manifest.PluginId + ":" + item.Id;
                        if (results.ContainsKey(key)) continue;
                        results.Add(key, new Item(manifest.PluginId, item.Id, item.Type, item.Category,
                            item.Title, item.Author, item.Description, filename, origin));
                    }
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
                {
                    // Broken plugin metadata is isolated; other contributors remain visible.
                }
            }
        }
        return results.Values.OrderBy(x => x.Type).ThenBy(x => x.Category).ThenBy(x => x.Title).ToArray();
    }
}
