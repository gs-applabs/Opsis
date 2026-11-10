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

        /// <summary>The device names of the monitors whose desktop is covered right now.</summary>
        public static HashSet<string> Compute()
        {
            var screens = Screen.AllScreens;
            var covered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var remaining = screens.ToDictionary(s => s.DeviceName, s => s.WorkingArea, StringComparer.OrdinalIgnoreCase);
            var pid = (uint)Environment.ProcessId;

            EnumWindows((h, _) =>
            {
                if (remaining.Count == 0) return false;
                if (!IsWindowVisible(h) || IsIconic(h)) return true;
                GetWindowThreadProcessId(h, out var wpid);
                if (wpid == pid) return true;
                var ex = GetWindowLong(h, -20);
                if ((ex & (0x80 /*TOOLWINDOW*/ | 0x20 /*TRANSPARENT*/ | 0x80000 /*LAYERED*/)) != 0) return true;
                if (DwmGetWindowAttribute(h, 14 /*CLOAKED*/, out int cloaked, 4) == 0 && cloaked != 0) return true;
                var cls = new System.Text.StringBuilder(64); GetClassName(h, cls, 64);
                if (Array.IndexOf(ShellClasses, cls.ToString()) >= 0) return true;
                if (DwmGetWindowAttribute(h, 9 /*EXTENDED_FRAME_BOUNDS*/, out RECT r, Marshal.SizeOf<RECT>()) != 0 && !GetWindowRect(h, out r)) return true;
                var rect = Rectangle.FromLTRB(r.l, r.t, r.r, r.b);
                foreach (var kv in remaining.ToArray())
                    if (Covers(rect, kv.Value)) { covered.Add(kv.Key); remaining.Remove(kv.Key); }
                return true;
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
    }
}
