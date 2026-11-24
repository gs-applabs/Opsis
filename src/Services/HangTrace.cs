namespace MotionDesk.Services;

/// <summary>Hang hunting: with OPSIS_TRACE=&lt;file&gt; steps of suspicious code paths are appended to that file (the last line shows where the UI thread stopped).</summary>
internal static class HangTrace
{
    private static readonly string? Path_ = Environment.GetEnvironmentVariable("OPSIS_TRACE");
    public static void Mark(string s) { if (Path_ == null) return; try { File.AppendAllText(Path_, DateTime.UtcNow.ToString("HH:mm:ss.fff") + " [" + Environment.CurrentManagedThreadId + "] " + s + "\n"); } catch (Exception) { } }
}
