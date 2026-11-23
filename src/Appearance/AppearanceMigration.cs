// Read-only report about what an older separate appearance app/plugin left on this PC and what Opsis 2.6.0 uses from it.
// Nothing is copied, changed or deleted: the built-in Appearance reads the SAME files (appearance.json, appearance-original.json, themes\, appearance\backups).
using System.Text.Json;

namespace MotionDesk.Appearance;

public static class AppearanceMigration
{
    public sealed record Item(string What, string Path, bool Present, bool Usable, string Note);

    public sealed record Report(IReadOnlyList<Item> Items, IReadOnlyList<string> NotMigrated)
    {
        public bool AnythingFound => Items.Any(i => i.Present);
    }

    /// <summary>Files that belong to the old stand-alone shell, the welcome guide and the family window: not used any more.</summary>
    public static readonly string[] NotMigratedFiles = { @"appearance\suite.json", @"appearance\suite-apps.json" };

    public static Report Scan(string dataDir)
    {
        var items = new List<Item>();
        string settings = Path.Combine(dataDir, "appearance.json");
        items.Add(new Item("Settings", settings, File.Exists(settings), Parses<AppearanceSettings>(settings), "used as is"));
        string original = Path.Combine(dataDir, "appearance-original.json");
        items.Add(new Item("Original Windows values (restore)", original, File.Exists(original), File.Exists(original) && Parses<List<AppearanceEngine.SnapshotEntry>>(original), "used as is; Restore still works"));
        string themes = Path.Combine(dataDir, "themes");
        int themeCount = Directory.Exists(themes) ? Directory.GetDirectories(themes).Count(d => File.Exists(Path.Combine(d, "theme.json"))) : 0;
        items.Add(new Item("Saved themes (" + themeCount + ")", themes, themeCount > 0, true, "used as is (.opsistheme import/export unchanged)"));
        string vault = Path.Combine(dataDir, "appearance", "backups");
        items.Add(new Item("File backups (BackupVault)", vault, Directory.Exists(vault), true, "used as is"));
        var skipped = NotMigratedFiles.Where(f => File.Exists(Path.Combine(dataDir, f))).ToList();
        return new Report(items, skipped.Count == 0 ? Array.Empty<string>() : skipped);
    }

    private static bool Parses<T>(string file)
    {
        try { return File.Exists(file) && JsonSerializer.Deserialize<T>(File.ReadAllText(file)) != null; }
        catch (Exception) { return false; }
    }

    /// <summary>
    /// A safety copy of the old settings BEFORE anything reads them for the report: appearance.json, appearance-original.json and the themes folder go to
    /// appearance\migration-backup\yyyyMMdd-HHmmss. The originals are never changed or deleted. Returns the folder, or null when there was nothing to copy.
    /// </summary>
    public static string? Backup(string dataDir, DateTime? now = null)
    {
        try
        {
            var sources = new[] { "appearance.json", "appearance-original.json" }.Select(f => Path.Combine(dataDir, f)).Where(File.Exists).ToList();
            string themes = Path.Combine(dataDir, "themes");
            if (sources.Count == 0 && !Directory.Exists(themes)) return null;
            string dest = Path.Combine(dataDir, "appearance", "migration-backup", (now ?? DateTime.Now).ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(dest);
            foreach (var f in sources) File.Copy(f, Path.Combine(dest, Path.GetFileName(f)), overwrite: false);
            if (Directory.Exists(themes))
                foreach (var file in Directory.EnumerateFiles(themes, "*", SearchOption.AllDirectories))
                {
                    string rel = Path.GetRelativePath(dataDir, file);
                    string target = Path.Combine(dest, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(file, target, overwrite: false);
                }
            return dest;
        }
        catch (Exception) { return null; }
    }

    /// <summary>The folder names (under the plugin roots) of an older separate appearance plugin: Opsis ignores them and never deletes them.</summary>
    public static IReadOnlyList<string> FindOldPluginFolders(IEnumerable<string> pluginRoots)
    {
        var found = new List<string>();
        foreach (var root in pluginRoots)
        {
            try { if (Directory.Exists(root)) found.AddRange(Directory.EnumerateDirectories(root).Where(d => File.Exists(Path.Combine(d, "OpsisAppearancePlugin.dll")))); }
            catch (Exception) { }
        }
        return found;
    }

    /// <summary>One plain line per item for the log or a message box.</summary>
    public static string Describe(Report r) =>
        string.Join(Environment.NewLine, r.Items.Select(i => $"{(i.Present ? (i.Usable ? "OK" : "UNREADABLE") : "none")}  {i.What} - {i.Note}")
            .Concat(r.NotMigrated.Select(n => "not migrated: " + n)));
}
