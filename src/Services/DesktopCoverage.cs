using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MotionDesk.Services
{
    /// <summary>
    /// 2.6.0 resource saving without any quality loss: which monitors have their desktop FULLY covered by an opaque window (a maximized or fullscreen window, a game,
    /// a full-screen video). The wallpaper behind it cannot be seen, so its animation is paused there and resumes at once when the desktop shows again.
    /// Only a window that covers the whole work area of the monitor counts; several tiled windows never do (the wallpaper keeps running, which is the safe choice).
    /// Windows of Opsis itself, the shell (desktop, taskbar), minimized, cloaked, tool, layered/transparent and click-through windows are ignored.
    /// </summary>
    internal static class DesktopCoverage
    {
        /// <summary>True when <paramref name="window"/> covers <paramref name="workArea"/> (a few pixels of tolerance for borders and rounding).</summary>
        internal static bool Covers(Rectangle window, Rectangle workArea, int tolerance = 3) =>
            workArea.Width > 0 && workArea.Height > 0 &&
            window.Left <= workArea.Left + tolerance && window.Top <= workArea.Top + tolerance &&
            window.Right >= workArea.Right - tolerance && window.Bottom >= workArea.Bottom - tolerance;

        private static readonly string[] ShellClasses = { "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "NotifyIconOverflowWindow", "TopLevelWindowForOverflowXamlIsland", "XamlExplorerHostIslandWindow", "Windows.UI.Core.CoreWindow" };

        /// <summary>What the covering decision needs to know about one top-level window.</summary>
        internal readonly record struct WinInfo(Rectangle Rect, uint Pid, bool Visible, bool Minimized, bool Cloaked, long ExStyle, string Class);

        /// <summary>
        /// The pure decision: windows in z-order from the top; a monitor is covered when an opaque, visible, not-ours window covers its whole work area.
        /// (Windows above it do not matter: if the covering window is below a small popup, the desktop is still not visible.)
        /// </summary>
        internal static HashSet<string> Evaluate(IEnumerable<WinInfo> topToBottom, IReadOnlyDictionary<string, Rectangle> workAreas, uint ownPid)
        {
            var covered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var w in topToBottom)
            {
                if (!w.Visible || w.Minimized || w.Cloaked || w.Pid == ownPid || ExcludedStyle(w.ExStyle)) continue;
                if (Array.IndexOf(ShellClasses, w.Class) >= 0) continue;
                MarkCovered(w.Rect, workAreas, covered);
                if (covered.Count == workAreas.Count) break;
            }
            return covered;
        }

        private static bool ExcludedStyle(long style) => (style & (0x80 | 0x20 | 0x80000)) != 0;

        private static void MarkCovered(Rectangle rect, IReadOnlyDictionary<string, Rectangle> areas, HashSet<string> covered)
        {
            foreach (var area in areas)
                if (Covers(rect, area.Value)) covered.Add(area.Key);
        }

        /// <summary>The device names of the monitors whose desktop is covered right now.</summary>
        public static HashSet<string> Compute()
        {
            var areas = Screen.AllScreens.ToDictionary(s => s.DeviceName, s => s.WorkingArea, StringComparer.OrdinalIgnoreCase);
            var covered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var name = new System.Text.StringBuilder(64);
            uint ownPid = (uint)Environment.ProcessId;
            EnumWindows((h, _) =>
            {
                if (!IsWindowVisible(h) || IsIconic(h) || ExcludedStyle(GetWindowLong(h, -20))) return true;
                GetWindowThreadProcessId(h, out uint pid);
                if (pid == ownPid) return true;
                if (DwmGetWindowAttribute(h, 9, out RECT r, Marshal.SizeOf<RECT>()) != 0 && !GetWindowRect(h, out r)) return true;
                var rect = Rectangle.FromLTRB(r.l, r.t, r.r, r.b);
                if (!areas.Any(area => !covered.Contains(area.Key) && Covers(rect, area.Value))) return true;
                name.Clear();
                GetClassName(h, name, name.Capacity);
                if (Array.IndexOf(ShellClasses, name.ToString()) >= 0) return true;
                if (DwmGetWindowAttribute(h, 14, out int cloaked, 4) == 0 && cloaked != 0) return true;
                MarkCovered(rect, areas, covered);
                return covered.Count < areas.Count;
            }, IntPtr.Zero);
            return covered;
        }

        [StructLayout(LayoutKind.Sequential)] private struct RECT { public int l, t, r, b; }
        private delegate bool EnumProc(IntPtr h, IntPtr p);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc cb, IntPtr p);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr h, int index);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr h, System.Text.StringBuilder sb, int max);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int value, int size);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr h, int attr, out RECT value, int size);
    }
}

namespace MotionDesk.Services
{
    /// <summary>Test hook (the class itself is internal to the app).</summary>
    public static class DesktopCoverageTestAccess
    {
        public static bool Covers(System.Drawing.Rectangle window, System.Drawing.Rectangle workArea) => DesktopCoverage.Covers(window, workArea);

        public static string[] Evaluate(IEnumerable<(System.Drawing.Rectangle Rect, uint Pid, bool Minimized, bool Cloaked, long ExStyle, string Class)> topToBottom, IDictionary<string, System.Drawing.Rectangle> workAreas, uint ownPid) =>
            DesktopCoverage.Evaluate(topToBottom.Select(w => new DesktopCoverage.WinInfo(w.Rect, w.Pid, true, w.Minimized, w.Cloaked, w.ExStyle, w.Class)), new Dictionary<string, System.Drawing.Rectangle>(workAreas), ownPid).OrderBy(x => x).ToArray();
    }
}
