using MotionDesk.Widgets;

namespace MotionDesk.Services;

/// <summary>
/// Discovers installed offline wallpaper packs. The Add-On installer only writes
/// self-contained directories under wallpapers/packs/opsis-100; the host requires no
/// code change or registration step after a pack is installed.
/// </summary>
public static class WallpaperGalleryCatalog
{
    public static readonly string[] Categories =
    ["Abstract", "Nature", "Tech", "Space", "City / Night",
     "Minimal", "Weather", "Neon", "Fantasy", "Greek Landmarks"];

    public sealed record Entry(string Id, string Name, string Category,
        string IndexPath, string? PosterPath, string Description);

    public static IReadOnlyList<Entry> Discover(string? baseDirectory = null)
    {
        string baseRoot = baseDirectory ?? AppContext.BaseDirectory;
        string[] roots =
        [
            Path.Combine(baseRoot, "wallpapers", "samples"),
            // Separate install/uninstall scope for the optional 100-wallpaper add-on.
            Path.Combine(baseRoot, "wallpapers", "packs", "opsis-100")
        ];
        var found = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        foreach (string root in roots)
        {
            if (!Directory.Exists(root)) continue;
            try
            {
                foreach (var directory in Directory.EnumerateDirectories(root).Take(1000))
                {
                    try
                    {
                        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) continue;
                        string index = Path.Combine(directory, "index.html");
                        if (!File.Exists(index) || new FileInfo(index).Length > 512 * 1024) continue;
                        var manifest = WallpaperManifest.LoadFor(index);
                        if (manifest == null || manifest.Collection != "OPSIS 100 3D Wallpapers"
                            || string.IsNullOrWhiteSpace(manifest.WallpaperId)) continue;
                        string category = Categories.FirstOrDefault(c => c.Equals(manifest.Category,
                            StringComparison.OrdinalIgnoreCase)) ?? "Abstract";
                        string poster = Path.Combine(directory, "poster.png");
                        // The add-on is scanned last, overriding a legacy sample of
                        // the same wallpaper ID without touching its original files.
                        found[manifest.WallpaperId] = new Entry(manifest.WallpaperId, manifest.Name,
                            category, index, File.Exists(poster) ? poster : null, manifest.Description);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                        or ArgumentException or System.Text.Json.JsonException) { }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return found.Values.OrderBy(x => Array.IndexOf(Categories, x.Category))
            .ThenBy(x => x.Id, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
