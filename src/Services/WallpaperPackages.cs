using System.IO.Compression;

namespace MotionDesk.Services;

/// <summary>A read-only inspection report. Inspecting never creates or runs package content.</summary>
public sealed class WallpaperPackagePreview
{
    public IReadOnlyList<string> Files { get; init; } = Array.Empty<string>();
    public long UncompressedBytes { get; init; }
    public string EntryPoint { get; init; } = "index.html";
    public int ScriptCount { get; init; }
}

/// <summary>
/// Local wallpaper ZIPs are untrusted executable web content. Validate *every* ZIP entry before
/// showing a preview or extracting; extract into a private staging folder with bounded streams;
/// only publish the package directory after the entire archive has passed verification.
/// </summary>
public static class WallpaperPackages
{
    public const long MaxUncompressedBytes = 400L * 1024 * 1024;
    public const int MaxEntries = 4000;

    public static string Root => Path.Combine(AppPaths.AppDataFolder, "wallpapers");

    private sealed class EntryPlan
    {
        public required ZipArchiveEntry Entry { get; init; }
        public required string RelativePath { get; init; }
        public required bool IsDirectory { get; init; }
    }

    private sealed class PackagePlan
    {
        public required string Prefix { get; init; }
        public required List<EntryPlan> Entries { get; init; }
        public long TotalBytes { get; init; }
    }

    /// <summary>Read-only preflight: lists the files that would actually be installed.</summary>
    public static WallpaperPackagePreview Inspect(string zipPath)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var plan = Validate(zip);
        return new WallpaperPackagePreview
        {
            Files = plan.Entries.Where(e => !e.IsDirectory).Select(e => e.RelativePath).ToArray(),
            UncompressedBytes = plan.TotalBytes,
            EntryPoint = plan.Prefix + "index.html",
            ScriptCount = plan.Entries.Count(e => !e.IsDirectory &&
                (e.RelativePath.EndsWith(".js", StringComparison.OrdinalIgnoreCase) ||
                 e.RelativePath.EndsWith(".html", StringComparison.OrdinalIgnoreCase)))
        };
    }

    /// <summary>Returns the path of the installed index.html; failures leave no partial package.</summary>
    public static string Import(string zipPath, string? destinationRoot = null)
    {
        destinationRoot ??= Root;
        using var zip = ZipFile.OpenRead(zipPath);
        // Repeat preflight at import: preview is not a security authorization and the ZIP can change.
        var plan = Validate(zip);
        Directory.CreateDirectory(destinationRoot);
        string staging = Path.Combine(destinationRoot, ".opsis-import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        string stageRoot = Path.GetFullPath(staging) + Path.DirectorySeparatorChar;
        bool committed = false;
        try
        {
            long totalWritten = 0;
            foreach (var item in plan.Entries)
            {
                string dest = Path.GetFullPath(Path.Combine(stageRoot, item.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
                if (!dest.StartsWith(stageRoot, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Unsafe wallpaper package path.");
                if (item.IsDirectory) { Directory.CreateDirectory(dest); continue; }

                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                using var source = item.Entry.Open();
                using var output = new FileStream(dest, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                byte[] buffer = new byte[81920];
                long fileWritten = 0;
                int count;
                while ((count = source.Read(buffer, 0, buffer.Length)) > 0)
                {
                    // Check BEFORE addition so a hostile ZIP cannot overflow or bypass the limit.
                    if (count > item.Entry.Length - fileWritten || count > MaxUncompressedBytes - totalWritten)
                        throw new InvalidDataException("Wallpaper package exceeded its declared/allowed size.");
                    output.Write(buffer, 0, count);
                    fileWritten += count;
                    totalWritten += count;
                }
                if (fileWritten != item.Entry.Length)
                    throw new InvalidDataException("Wallpaper package entry size mismatch.");
            }

            string name = SafeName(Path.GetFileNameWithoutExtension(zipPath));
            string finalFolder = UniqueFolder(destinationRoot, name);
            Directory.Move(staging, finalFolder); // atomic publish on the same filesystem
            committed = true;
            return Path.Combine(finalFolder, "index.html");
        }
        finally
        {
            if (!committed)
            {
                try { Directory.Delete(staging, true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    private static PackagePlan Validate(ZipArchive zip)
    {
        if (zip.Entries.Count == 0 || zip.Entries.Count > MaxEntries)
            throw new InvalidDataException("Wallpaper package contains no files or too many entries.");

        var all = new List<(ZipArchiveEntry Entry, string Normalized, bool Directory)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var entry in zip.Entries)
        {
            string path = entry.FullName.Replace('\\', '/');
            bool directory = path.EndsWith("/", StringComparison.Ordinal);
            string trimmed = directory ? path.TrimEnd('/') : path;
            string[] parts = trimmed.Split('/');
            if (parts.Length == 0 || parts.Any(UnsafeSegment) || path.StartsWith("/", StringComparison.Ordinal) ||
                !seen.Add(trimmed))
                throw new InvalidDataException("Wallpaper package has an unsafe or duplicate path: " + path);

            // UNIX symlink entries cannot become files or directories in a trusted package.
            int unixFileType = (entry.ExternalAttributes >> 16) & 0xF000;
            if (unixFileType == 0xA000 || (entry.ExternalAttributes & 0x400) != 0) // symlink or DOS reparse point
                throw new InvalidDataException("Wallpaper package contains a link: " + path);
            if (directory && entry.Length != 0)
                throw new InvalidDataException("Wallpaper package has an invalid directory entry.");
            if (entry.Length < 0 || entry.Length > MaxUncompressedBytes - total)
                throw new InvalidDataException("Wallpaper package exceeds the size limit.");
            total += entry.Length;
            all.Add((entry, trimmed, directory));
        }

        // Only a root index.html or a single top-level wrapper folder is supported.
        string? prefix = all.Where(e => !e.Directory && e.Normalized.EndsWith("index.html", StringComparison.OrdinalIgnoreCase))
            .Select(e => e.Normalized)
            .Where(p => p.Equals("index.html", StringComparison.OrdinalIgnoreCase) ||
                        (p.Split('/').Length == 2 && p.EndsWith("/index.html", StringComparison.OrdinalIgnoreCase)))
            .OrderBy(p => p.Count(c => c == '/'))
            .Select(p => p == "index.html" ? "" : p[..(p.LastIndexOf('/') + 1)])
            .FirstOrDefault();
        if (prefix == null) throw new InvalidDataException("Wallpaper package must contain index.html at its root or in one folder.");

        var selected = new List<EntryPlan>();
        long selectedBytes = 0;
        foreach (var item in all)
        {
            if (!item.Normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            string rel = item.Normalized[prefix.Length..];
            if (rel.Length == 0) continue;
            selected.Add(new EntryPlan { Entry = item.Entry, RelativePath = rel, IsDirectory = item.Directory });
            selectedBytes += item.Entry.Length;
        }
        if (!selected.Any(e => !e.IsDirectory && e.RelativePath.Equals("index.html", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Wallpaper package entry point is missing.");
        return new PackagePlan { Prefix = prefix, Entries = selected, TotalBytes = selectedBytes };
    }

    private static bool UnsafeSegment(string segment)
    {
        if (string.IsNullOrEmpty(segment) || segment is "." or ".." ||
            segment.EndsWith(' ') || segment.EndsWith('.') ||
            segment.Any(c => char.IsControl(c) || "<>:\"|?*".Contains(c))) return true;
        string stem = segment.Split('.')[0];
        return stem.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
               stem.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
               stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
               stem.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
               (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
                                     stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) && stem[3] is >= '1' and <= '9');
    }

    /// <summary>Export is transactional: never destroy the old archive before a complete new one exists.</summary>
    public static void Export(string indexHtml, string zipPath)
    {
        string dir = Path.GetDirectoryName(indexHtml) ?? throw new ArgumentException("No folder", nameof(indexHtml));
        string output = Path.GetFullPath(zipPath);
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dir)) + Path.DirectorySeparatorChar;
        if (output.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Choose an output folder outside the wallpaper folder.");
        string temporary = Path.Combine(Path.GetDirectoryName(output)!, Guid.NewGuid() + ".tmp");
        try
        {
            ZipFile.CreateFromDirectory(dir, temporary, CompressionLevel.Optimal, includeBaseDirectory: false);
            File.Move(temporary, output, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static string SafeName(string n)
    {
        foreach (char c in Path.GetInvalidFileNameChars()) n = n.Replace(c, '_');
        n = n.Trim().Trim('.');
        return n.Length == 0 ? "wallpaper" : n.Length > 60 ? n[..60] : n;
    }

    private static string UniqueFolder(string root, string name)
    {
        string p = Path.Combine(root, name);
        for (int i = 2; Directory.Exists(p) || File.Exists(p); i++) p = Path.Combine(root, $"{name} ({i})");
        return p;
    }
}
