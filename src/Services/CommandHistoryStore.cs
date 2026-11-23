using System.Text.Json;

namespace MotionDesk.Services;

/// <summary>
/// The command palette's opt-out-free, LOCAL recently-used commands. Stores only
/// command identifiers (never command arguments, filenames or target paths), keeps
/// at most 30 entries and lets users remove any single entry from the palette.
/// </summary>
internal static class CommandHistoryStore
{
    private sealed record Record(string Id, DateTime UsedUtc);
    private static readonly object Gate = new();
    private static string PathOnDisk => Path.Combine(AppPaths.AppDataFolder, "command-history.json");
    private const int MaxEntries = 30;

    private static List<Record> Read()
    {
        try
        {
            if (!File.Exists(PathOnDisk)) return new();
            var entries = JsonSerializer.Deserialize<List<Record>>(AtomicFile.ReadAllText(PathOnDisk));
            return (entries ?? new()).Where(e => e.Id.Length is > 0 and < 200)
                .DistinctBy(e => e.Id, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(e => e.UsedUtc).Take(MaxEntries).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }

    public static IReadOnlyList<string> RecentIds()
    {
        lock (Gate) return Read().Select(x => x.Id).ToArray();
    }

    public static void RecordUse(string id)
    {
        // An app name is not an acceptable value here: only built-in and plugin
        // commands are recorded. Applications launched outside Opsis are not tracked.
        if (string.IsNullOrWhiteSpace(id) || id.StartsWith("app:", StringComparison.OrdinalIgnoreCase) || id.Length > 160) return;
        lock (Gate)
        {
            var entries = Read();
            entries.RemoveAll(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            entries.Insert(0, new Record(id, DateTime.UtcNow));
            AtomicFile.WriteAllText(PathOnDisk, JsonSerializer.Serialize(entries.Take(MaxEntries).ToArray()));
        }
    }

    public static void Remove(string id)
    {
        lock (Gate)
        {
            var entries = Read();
            if (entries.RemoveAll(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) == 0) return;
            AtomicFile.WriteAllText(PathOnDisk, JsonSerializer.Serialize(entries));
        }
    }
}
