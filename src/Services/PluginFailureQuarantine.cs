using System.Text.Json;

namespace MotionDesk.Services;

/// <summary>
/// Isolates a plugin after three unexpected failures in ten minutes, for thirty minutes.
/// A failure never disables unrelated plugins. State is persisted atomically and
/// bounded to prevent unbounded disk writes or hostile plugin IDs becoming file paths.
/// </summary>
internal static class PluginFailureQuarantine
{
    private sealed class FailureState
    {
        public List<DateTime> FailuresUtc { get; set; } = new();
        public DateTime QuarantinedUntilUtc { get; set; }
    }

    private static readonly object Gate = new();
    private static readonly Dictionary<string, FailureState> Entries = Read();
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan Quarantine = TimeSpan.FromMinutes(30);
    private static string StorePath => Path.Combine(AppPaths.AppDataFolder, "plugin-quarantine.json");

    private static Dictionary<string, FailureState> Read()
    {
        try
        {
            var path = StorePath;
            if (!File.Exists(path) || new FileInfo(path).Length > 64 * 1024) return new(StringComparer.OrdinalIgnoreCase);
            var result = JsonSerializer.Deserialize<Dictionary<string, FailureState>>(File.ReadAllText(path));
            return result is null ? new(StringComparer.OrdinalIgnoreCase) :
                new Dictionary<string, FailureState>(result.Take(128).Where(e => e.Value != null), StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { return new(StringComparer.OrdinalIgnoreCase); }
    }

    internal static bool IsQuarantined(string id)
    {
        lock (Gate) return Entries.TryGetValue(id, out var e) && e.QuarantinedUntilUtc > DateTime.UtcNow;
    }

    /// <summary>Returns true only if this failure put the plugin into quarantine.</summary>
    internal static bool RecordFailure(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 180) return false;
        lock (Gate)
        {
            if (!Entries.TryGetValue(id, out var state)) Entries[id] = state = new FailureState();
            var now = DateTime.UtcNow;
            state.FailuresUtc.RemoveAll(t => t <= now - Window || t > now.AddMinutes(1));
            state.FailuresUtc.Add(now);
            bool isolated = state.FailuresUtc.Count >= 3 && state.QuarantinedUntilUtc <= now;
            if (isolated)
            {
                state.QuarantinedUntilUtc = now + Quarantine;
                state.FailuresUtc.Clear();
            }
            // Keep only the newest 128 identities, never writing a file per plugin call.
            if (Entries.Count > 128)
                foreach (var key in Entries.Keys.Where(k => !string.Equals(k, id, StringComparison.OrdinalIgnoreCase)).Take(Entries.Count - 128).ToList()) Entries.Remove(key);
            Persist();
            return isolated;
        }
    }

    internal static void Clear(string id)
    {
        lock (Gate) { if (Entries.Remove(id)) Persist(); }
    }

    private static void Persist()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
            AtomicFile.WriteAllText(StorePath, JsonSerializer.Serialize(Entries));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
