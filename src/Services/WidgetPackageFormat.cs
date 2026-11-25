using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MotionDesk.Services;

internal sealed record WidgetManifest(string Id, string Name, string Version, string Description, string Entry, int Width = 330, int Height = 220, int Api = 1);

internal static class WidgetPackageFormat
{
    private const long MaxBytes = 40 * 1024 * 1024;
    internal const string ManifestName = "widget.json";
    private static readonly Regex Slug = new(@"\A[a-z][a-z0-9-]{1,47}\z", RegexOptions.CultureInvariant);
    private static readonly Regex PathPart = new(@"\A[A-Za-z0-9_][A-Za-z0-9_.-]{0,80}\z", RegexOptions.CultureInvariant);
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private static readonly Dictionary<string, string> Types = new(StringComparer.OrdinalIgnoreCase)
    {
        [".html"] = "text/html; charset=utf-8", [".css"] = "text/css; charset=utf-8",
        [".js"] = "text/javascript; charset=utf-8", [".json"] = "application/json; charset=utf-8",
        [".png"] = "image/png", [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg",
        [".webp"] = "image/webp", [".gif"] = "image/gif", [".woff2"] = "font/woff2"
    };

    internal static string? BrowserType(string path) => Types.GetValueOrDefault(Path.GetExtension(path));

    internal static string? LocalFile(string root, string name)
    {
        if (name.Length is 0 or > 240 || name.Contains('\\')) return null;
        string[] parts = name.Split('/');
        if (parts.Length > 8) return null;
        foreach (string part in parts)
        {
            if (!PathPart.IsMatch(part) || part.EndsWith('.') || part.EndsWith(' ')) return null;
            string stem = part.Split('.')[0].ToUpperInvariant();
            if (stem is "CON" or "PRN" or "AUX" or "NUL" || stem.Length == 4 && stem[3] is >= '0' and <= '9' &&
                (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal))) return null;
        }
        return Path.Combine([root, .. parts]);
    }

    internal static string? ExistingResource(string directory, string name) =>
        LocalFile(directory, name) is { } path && PackagePaths.IsExistingUnlinkedPath(directory, path) && File.Exists(path) ? path : null;

    internal static WidgetManifest Read(string directory)
    {
        var path = ExistingResource(directory, ManifestName);
        if (path == null || new FileInfo(path).Length > 16384) throw new InvalidDataException("Invalid widget.json.");
        using var file = File.OpenRead(path);
        var manifest = JsonSerializer.Deserialize<WidgetManifest>(file, Json);
        if (manifest == null || manifest.Api != 1 || manifest.Id == null || !Slug.IsMatch(manifest.Id) ||
            manifest.Name is not { Length: > 0 and <= 90 } || manifest.Entry == null ||
            manifest.Version is not { Length: > 0 and <= 32 } ||
            !string.Equals(Path.GetExtension(manifest.Entry), ".html", StringComparison.OrdinalIgnoreCase) ||
            ExistingResource(directory, manifest.Entry) == null)
            throw new InvalidDataException("The widget needs a valid id, name, version, entry HTML, and SDK version 1.");
        return manifest with { Width = Math.Clamp(manifest.Width, 160, 900), Height = Math.Clamp(manifest.Height, 100, 700) };
    }

    internal static WidgetManifest Extract(string archive, string directory)
    {
        using var zip = ZipFile.OpenRead(archive);
        if (zip.Entries.Count is 0 or > 300) throw new InvalidDataException("A widget must contain 1–300 files.");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long bytes = 0;
        foreach (var entry in zip.Entries)
        {
            bool directoryEntry = entry.FullName.EndsWith('/');
            string path = entry.FullName.TrimEnd('/');
            if ((entry.ExternalAttributes >> 16 & 0xF000) == 0xA000 ||
                LocalFile(directory, path) is not { } output || !seen.Add(path))
                throw new InvalidDataException("The widget archive contains an invalid or duplicate path.");
            if (directoryEntry) continue;
            if (BrowserType(path) == null) throw new InvalidDataException("Widget packages only contain web assets.");
            if (entry.Length > MaxBytes - bytes) throw new InvalidDataException("The widget exceeds 40 MB uncompressed.");
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            using var input = entry.Open();
            using var target = File.Open(output, FileMode.CreateNew);
            var buffer = new byte[32768];
            int read;
            while ((read = input.Read(buffer)) > 0)
            {
                if (bytes + read > MaxBytes) throw new InvalidDataException("The widget exceeds 40 MB uncompressed.");
                target.Write(buffer, 0, read);
                bytes += read;
            }
        }
        return Read(directory);
    }
}
