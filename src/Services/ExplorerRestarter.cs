using System.Diagnostics;

namespace MotionDesk.Services
{
    /// <summary>
    /// "Restart Explorer": ends explorer.exe, waits up to 3 s for Windows to bring a new one back and, when it does not, starts it explicitly from %WINDIR% (never from PATH).
    /// The steps are injected so the decision logic is tested without touching the real shell.
    /// </summary>
    public static class ExplorerRestarter
    {
        /// <returns>True when an explorer.exe is running at the end; false when it could not be (re)started.</returns>
        public static bool Restart(Action kill, Func<bool> isRunning, Action start, Action<int> sleepMs, int waitMs = 3000, int stepMs = 250)
        {
            kill();
            for (int waited = 0; waited < waitMs; waited += stepMs) { sleepMs(stepMs); if (isRunning()) return true; }
            try { start(); } catch (Exception) { return false; }
            for (int waited = 0; waited < 2000; waited += stepMs) { sleepMs(stepMs); if (isRunning()) return true; }
            return isRunning();
        }

        public static bool RestartReal()
        {
            return Restart(
                kill: () => { using var k = Process.Start(new ProcessStartInfo(MotionDesk.Appearance.TrustedExe.System32("taskkill.exe"), "/f /im explorer.exe") { CreateNoWindow = true, UseShellExecute = false }); k?.WaitForExit(5000); },
                isRunning: () => { var p = Process.GetProcessesByName("explorer"); try { return p.Length > 0; } finally { foreach (var x in p) x.Dispose(); } },
                start: () => { using var _ = Process.Start(new ProcessStartInfo(MotionDesk.Appearance.TrustedExe.Windows("explorer.exe")) { UseShellExecute = true }); },
                sleepMs: Thread.Sleep);
        }
    }
}
