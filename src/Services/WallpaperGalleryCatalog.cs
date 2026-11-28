using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using MotionDesk.Widgets;

namespace MotionDesk.Services;

/// <summary>Reads installed, declarative wallpaper manifests. Never executes a page during discovery.</summary>
public static class WallpaperGalleryCatalog
{
    public sealed record Entry(string Id, string Name, string NameEl, string Category,
        string IndexPath, string? PosterPath, string Description, string DescriptionEl);

    public static readonly string[] Categories = [
        "Abstract", "Nature", "Tech", "Space", "City / Night", "Minimal", "Weather",
        "Neon", "Fantasy", "Greek Landmarks", "European Capitals", "Historical Cities"
    ];

    private static readonly Regex ValidId = new("^[a-z0-9][a-z0-9_-]{0,79}$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static bool SafeExisting(string path, bool directory)
    {
        try
        {
            if (directory ? !Directory.Exists(path) : !File.Exists(path)) return false;
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        { return false; }
    }

    public static IReadOnlyList<Entry> Discover(string? baseDirectory = null)
    {
        string baseRoot = Path.GetFullPath(baseDirectory ?? AppContext.BaseDirectory);
        string wallpapers = Path.Combine(baseRoot, "wallpapers");
        string packs = Path.Combine(wallpapers, "packs");
        if (!SafeExisting(wallpapers, true)) return Array.Empty<Entry>();
        var roots = new List<string> { Path.Combine(wallpapers, "samples") };
        if (SafeExisting(packs, true)) roots.Add(Path.Combine(packs, "opsis-100"));
        var found = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

        foreach (string root in roots)
        {
            if (!SafeExisting(root, true)) continue;
            try
            {
                foreach (string directory in Directory.EnumerateDirectories(root).Take(1000)
                    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                {
                    try
                    {
                        if (!SafeExisting(directory, true)) continue;
                        string index = Path.Combine(directory, "index.html");
                        string manifestPath = Path.Combine(directory, WallpaperManifest.FileName);
                        if (!SafeExisting(index, false) || !SafeExisting(manifestPath, false)) continue;
                        if (new FileInfo(index).Length is <= 0 or > 512 * 1024) continue;
                        var manifest = WallpaperManifest.LoadFor(index);
                        if (manifest is null ||
                            !string.Equals(manifest.Collection, "OPSIS 100 3D Wallpapers", StringComparison.OrdinalIgnoreCase) ||
                            !ValidId.IsMatch(manifest.WallpaperId ?? "")) continue;
                        string id = manifest.WallpaperId.ToLowerInvariant();
                        // Built-in Ancient Athens is never installed a second time as an addon.
                        string normalized = id.Replace("-", "").Replace("_", "");
                        if (normalized is "ancientathens" or "athens" or "ancientathenscore") continue;
                        string category = Categories.FirstOrDefault(c =>
                            string.Equals(c, manifest.Category, StringComparison.OrdinalIgnoreCase)) ?? "Abstract";
                        string? poster = new[] { "poster.png", "poster.jpg", "poster.webp" }
                            .Select(file => Path.Combine(directory, file))
                            .FirstOrDefault(file => SafeExisting(file, false) && new FileInfo(file).Length is > 0 and <= 16 * 1024 * 1024);
                        if (poster is null) continue;
                        string name = string.IsNullOrWhiteSpace(manifest.Name) ? id : manifest.Name;
                        // Installed pack is processed last. Its manifest supersedes the same legacy ID.
                        if (id == "opsis-glass-signature" && found.ContainsKey(id)) continue;
                        found[id] = new Entry(id, name, manifest.NameEl ?? "", category, index,
                            poster, manifest.Description ?? "", manifest.DescriptionEl ?? "");
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
                    { /* One damaged scene must never hide its neighbors. */ }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            { /* The optional pack can be absent or removed. */ }
        }
        return found.Values.OrderBy(x => Array.IndexOf(Categories, x.Category))
            .ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(x => x.Id, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
