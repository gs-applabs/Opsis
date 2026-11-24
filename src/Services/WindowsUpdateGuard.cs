using System;
using System.IO;
using System.Text.Json;

namespace MotionDesk.Services;

/// <summary>
/// Cheap registry-fingerprint check before any optional shell integrations start.
/// On changed Windows build, puts invasive Opsis shell preferences into an explicit
/// review hold and captures user configuration. Does not patch Windows system files.
/// </summary>
public static class WindowsUpdateGuard
{
    public sealed class State
    {
        public int Schema { get; set; } = 1;
        public string LastApprovedFingerprint { get; set; } = "";
        public string PendingFingerprint { get; set; } = "";
        public DateTime? DetectedUtc { get; set; }
        public string LastSnapshotId { get; set; } = "";
    }

    private static readonly object Gate = new();
    private static string StateFile => Path.Combine(OpsisRecoveryHub.Root, "update-guard.json");

    public static State Read()
    {
        lock (Gate)
        {
            if (!File.Exists(StateFile)) return new State();
            if (new FileInfo(StateFile).Length > 32 * 1024)
                throw new InvalidDataException("Update Guard state too large.");
            var saved = JsonSerializer.Deserialize<State>(File.ReadAllText(StateFile))
                ?? throw new InvalidDataException("Invalid Update Guard state.");
            if (saved.Schema != 1) throw new InvalidDataException("Unknown Update Guard schema.");
            return saved;
        }
    }

    private static void Save(State state)
    {
        Directory.CreateDirectory(OpsisRecoveryHub.Root);
        AtomicFile.WriteAllText(StateFile, JsonSerializer.Serialize(state));
    }

    public static bool RequiresReview
    {
        get
        {
            var s = Read();
            return !string.IsNullOrWhiteSpace(s.PendingFingerprint);
        }
    }

    // An initial baseline and each changed Windows build trigger one asynchronous
    // read-only inventory of six known shell files. Idle sessions do not hash
    // system files repeatedly, avoiding extra SSD traffic or startup latency.
    private static void ScheduleShellInventory()
    {
        if (AppSettings.IsIsolatedForTests()) return;
        _ = System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                var impact = OpsisRecoveryHub.InspectUpdateImpact();
                RotatingLogger.Write("Windows shell inventory: " + impact);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
            { RotatingLogger.Write("Windows shell inventory failed: " + e.Message); }
        });
    }

    /// <summary>
    /// Called on startup before plugins and shell replacements. No full-disk scan;
    /// writes only on first install or when a Windows update changes the fingerprint.
    /// </summary>
    public static bool EvaluateStartup()
    {
        lock (Gate)
        {
            var fingerprint = OpsisRecoveryHub.WindowsFingerprint();
            var state = Read();
            if (string.IsNullOrWhiteSpace(state.LastApprovedFingerprint))
            {
                state.LastApprovedFingerprint = fingerprint;
                Save(state);
                ScheduleShellInventory();
                return false;
            }
            if (state.LastApprovedFingerprint == fingerprint && string.IsNullOrEmpty(state.PendingFingerprint))
                return false;
            if (state.PendingFingerprint == fingerprint)
                return true;
            // One snapshot per detected Windows build. Failure must not block boot,
            // but pending review must still be persisted whenever possible.
            try { state.LastSnapshotId = OpsisRecoveryHub.Snapshot("Before Windows shell compatibility review").Id; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
            { RotatingLogger.Write("Update guard backup warning: " + e.Message); }
            state.PendingFingerprint = fingerprint;
            state.DetectedUtc = DateTime.UtcNow;
            Save(state);
            ScheduleShellInventory();
            return true;
        }
    }

    /// <summary>Only the user can acknowledge the new Windows build after reviewing.</summary>
    public static void ApproveCurrentBuild()
    {
        lock (Gate)
        {
            var state = Read();
            state.LastApprovedFingerprint = OpsisRecoveryHub.WindowsFingerprint();
            state.PendingFingerprint = "";
            state.DetectedUtc = null;
            Save(state);
        }
    }
}
