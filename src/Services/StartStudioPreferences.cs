using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MotionDesk.Services;

/// <summary>Per-user Start Studio pinned shortcuts. Never stores executable blobs.</summary>
public static class StartStudioPreferences
{
    public sealed class Data
    {
        public int Schema { get; set; } = 1;
        public List<string> PinnedShortcuts { get; set; } = new();
        // Native Opsis launcher presentation; does not replace Windows Start.
        public string DisplayStyle { get; set; } = "List"; // List | Tiles | Compact
        public bool GroupByFolder { get; set; } = true;
        public string[] PowerActions { get; set; } = new[] { "Lock", "Sign out", "Restart", "Shut down" };
        public string StartButtonStyle { get; set; } = "Opsis Glass"; // Safe, Opsis-owned taskbar only
    }
    public static IReadOnlyList<string> AvailablePowerActions { get; } = Array.AsReadOnly(new[] { "Lock", "Sign out", "Restart", "Shut down" });
    public static bool ValidPowerActions(string[]? actions) => actions != null && actions.Length <= AvailablePowerActions.Count &&
        actions.Distinct(StringComparer.Ordinal).Count() == actions.Length && actions.All(AvailablePowerActions.Contains);
    public static void SetPowerActions(string[] actions)
    {
        if (!ValidPowerActions(actions)) throw new InvalidDataException("Unsupported power action.");
        lock (Gate)
        {
            var data = Read();
            if (data.PowerActions.SequenceEqual(actions)) return;
            data.PowerActions = actions.ToArray();
            Directory.CreateDirectory(Path.GetDirectoryName(Config)!);
            AtomicFile.WriteAllText(Config, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    internal sealed record Shortcut(string Name, string Path, string Group);
    private static int _scanBusy;
    private static string[] Roots() => new[]
    {
        Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu)
    }.Where(root => !string.IsNullOrWhiteSpace(root)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    // Single shared, bounded scan for Studio and the actual Opsis Start launcher.
    // No persistent index, watchers or background refresh when neither is open.
    internal static Shortcut[] ScanShortcuts(System.Threading.CancellationToken token)
    {
        if (System.Threading.Interlocked.CompareExchange(ref _scanBusy, 1, 0) != 0)
            throw new InvalidOperationException("A Start shortcut scan is already running. Try again after it completes.");
        try
        {
            var items = new List<Shortcut>();
            int visited = 0;
            foreach (var root in Roots())
            {
                if (!Directory.Exists(root) || !PackagePaths.IsExistingUnlinkedPath(root, root)) continue;
                var pending = new Stack<(string Folder, int Depth)>();
                pending.Push((root, 0));
                while (pending.Count > 0 && items.Count < 3000 && visited < 20000)
                {
                    token.ThrowIfCancellationRequested();
                    var (folder, depth) = pending.Pop();
                    try
                    {
                        foreach (var entry in Directory.EnumerateFileSystemEntries(folder))
                        {
                            token.ThrowIfCancellationRequested();
                            if (++visited > 20000 || items.Count >= 3000) break;
                            var attributes = File.GetAttributes(entry);
                            if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                            if ((attributes & FileAttributes.Directory) != 0)
                            {
                                if (depth < 6) pending.Push((entry, depth + 1));
                                continue;
                            }
                            if (!entry.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) continue;
                            string group = Path.GetRelativePath(root, folder);
                            items.Add(new Shortcut(Path.GetFileNameWithoutExtension(entry), entry, group == "." ? "General" : group));
                        }
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                }
            }
            return items.DistinctBy(x => x.Path, StringComparer.OrdinalIgnoreCase).ToArray();
        }
        finally { System.Threading.Volatile.Write(ref _scanBusy, 0); }
    }

    internal static bool CanLaunchShortcut(string path) => SafePath(path) &&
        Roots().Any(root => PackagePaths.IsExistingUnlinkedPath(root, path)) && SafeLaunch.IsAllowed(path);

    private static readonly object Gate = new();
    private static string Config => Path.Combine(AppPaths.AppDataFolder, "start-studio.json");

    private static bool SafePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 2048 ||
            !Path.IsPathFullyQualified(path) || !path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            return false;
        var candidate = Path.GetFullPath(path);
        var roots = Roots();
        return roots.Any(root => candidate.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) +
            Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
    }

    public static Data Read()
    {
        lock (Gate)
        {
            if (!File.Exists(Config)) return new Data();
            if (new FileInfo(Config).Length > 128 * 1024)
                throw new InvalidDataException("Start Studio settings too large.");
            var data = JsonSerializer.Deserialize<Data>(AtomicFile.ReadAllText(Config))
                ?? throw new InvalidDataException("Invalid Start Studio settings.");
            if (data.Schema != 1 || data.PinnedShortcuts?.Count > 250 ||
                data.PinnedShortcuts == null || data.PinnedShortcuts.Any(p => !SafePath(p)) ||
                data.DisplayStyle is not ("List" or "Tiles" or "Compact") ||
                !ValidPowerActions(data.PowerActions) || !MotionDesk.UI.OpsisStartButtonArt.IsValid(data.StartButtonStyle))
                throw new InvalidDataException("Invalid pinned shortcut metadata.");
            return data;
        }
    }

    internal static System.Windows.Forms.View LauncherView(string style) => style switch
    {
        "Tiles" => System.Windows.Forms.View.Tile,
        "Compact" => System.Windows.Forms.View.List,
        _ => System.Windows.Forms.View.Details
    };

    internal static bool MovePin(List<string> pins, string path, int direction)
    {
        if (direction is not (-1 or 1)) throw new ArgumentOutOfRangeException(nameof(direction));
        int index = pins.FindIndex(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        int target = index + direction;
        if (index < 0 || target < 0 || target >= pins.Count) return false;
        (pins[index], pins[target]) = (pins[target], pins[index]);
        return true;
    }

    public static bool MovePinned(string path, int direction)
    {
        lock (Gate)
        {
            var data = Read();
            if (!MovePin(data.PinnedShortcuts, path, direction)) return false;
            AtomicFile.WriteAllText(Config, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
            return true;
        }
    }

    public static bool IsPinned(string path) => Read().PinnedShortcuts.Contains(path, StringComparer.OrdinalIgnoreCase);

    public static void SetDisplay(string style, bool groupByFolder)
    {
        if (style is not ("List" or "Tiles" or "Compact"))
            throw new ArgumentOutOfRangeException(nameof(style));
        lock (Gate)
        {
            var data = Read();
            if (data.DisplayStyle == style && data.GroupByFolder == groupByFolder) return;
            data.DisplayStyle = style;
            data.GroupByFolder = groupByFolder;
            Directory.CreateDirectory(Path.GetDirectoryName(Config)!);
            AtomicFile.WriteAllText(Config,
                JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    public static void SetStartButton(string style)
    {
        if (!MotionDesk.UI.OpsisStartButtonArt.IsValid(style))
            throw new ArgumentOutOfRangeException(nameof(style));
        lock (Gate)
        {
            var data = Read();
            if (data.StartButtonStyle == style) return; // Avoid needless SSD writes.
            data.StartButtonStyle = style;
            Directory.CreateDirectory(Path.GetDirectoryName(Config)!);
            AtomicFile.WriteAllText(Config, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    public static void SetPinned(string path, bool pinned)
    {
        if (!SafePath(path) || (pinned && !CanLaunchShortcut(path))) throw new InvalidOperationException("Shortcut is not in the Windows Start Menu.");
        lock (Gate)
        {
            var d = Read();
            if (d.PinnedShortcuts.Contains(path, StringComparer.OrdinalIgnoreCase) == pinned) return;
            d.PinnedShortcuts.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
            if (pinned) d.PinnedShortcuts.Add(path);
            if (d.PinnedShortcuts.Count > 250) throw new InvalidOperationException("Maximum 250 pinned shortcuts.");
            Directory.CreateDirectory(Path.GetDirectoryName(Config)!);
            AtomicFile.WriteAllText(Config, JsonSerializer.Serialize(d, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
