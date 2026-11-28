using System;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MotionDesk.Services
{
    /// <summary>Opt-in hot corners. UI thread timer, no system hooks or permanent OS changes.</summary>
    public sealed class HotCornersService : IDisposable
    {
        private readonly System.Windows.Forms.Timer _timer = new() { Interval = 100 };
        private readonly Action _openOpsis;
        private string _candidate = "";
        private long _enteredAt;
        private bool _fired;
        public HotCornersService(Action openOpsis)
        {
            _openOpsis = openOpsis;
            _timer.Tick += (_, _) => Tick();
        }
        internal static bool MayRun(bool saved, bool permitted, bool review, bool hold, bool remote) => saved && permitted && !review && !hold && !remote;
        private static bool Admission => MayRun(AppSettings.Peek().HotCornersEnabled,
            RuntimeSessionPolicy.IsPermitted(SessionBackgroundService.HotCorners), WindowsUpdateGuard.RequiresReview, WindowsUpdateGuard.RuntimeHold, SystemInformation.TerminalServerSession);
        public void Start() { if (Admission) _timer.Start(); else Stop(); }
        public void Stop() { _timer.Stop(); Reset(); }
        internal void ReconcileSession() { if (Admission) Start(); else Stop(); }
        public void Dispose() { _timer.Stop(); _timer.Dispose(); }

        private void Tick()
        {
            var settings = AppSettings.Peek();
            if (!Admission)
            {
                Stop();
                return;
            }
            if (settings.HotCornersPauseOnFullscreen && IsForegroundFullscreen()) { Reset(); return; }
            var p = Cursor.Position;
            var edge = Math.Clamp(settings.HotCornerEdgePixels, 2, 20);
            var dwell = Math.Clamp(settings.HotCornerDwellMilliseconds, 300, 2500);
            var screen = Screen.AllScreens.FirstOrDefault(s => s.Bounds.Contains(p));
            if (screen == null) { Reset(); return; }
            var b = screen.Bounds;
            var top = p.Y - b.Top < edge;
            var bottom = b.Bottom - 1 - p.Y < edge;
            var left = p.X - b.Left < edge;
            var right = b.Right - 1 - p.X < edge;
            var corner = top && left ? "TopLeft" : top && right ? "TopRight"
                : bottom && left ? "BottomLeft" : bottom && right ? "BottomRight" : "";
            if (corner != _candidate)
            {
                _candidate = corner;
                _enteredAt = Environment.TickCount64;
                _fired = false;
            }
            if (corner == "" || _fired || Environment.TickCount64 - _enteredAt < dwell) return;
            _fired = true;
            var action = corner switch
            {
                "TopLeft" => settings.HotCornerTopLeft,
                "TopRight" => settings.HotCornerTopRight,
                "BottomLeft" => settings.HotCornerBottomLeft,
                _ => settings.HotCornerBottomRight
            };
            switch (action)
            {
                case "OpenOpsis": _openOpsis(); break;
                case "ShowDesktop": SendWindowsShortcut(0x44); break; // Win+D
                case "TaskView": SendWindowsShortcut(0x09); break; // Win+Tab
            }
        }

        // Avoid intrusive corner activation while a fullscreen app or game is in focus.
        private static bool IsForegroundFullscreen()
        {
            var foreground = GetForegroundWindow();
            if (foreground == IntPtr.Zero || foreground == GetShellWindow()) return false;
            if (!GetWindowRect(foreground, out var rect)) return false;
            foreach (var screen in Screen.AllScreens)
            {
                var b = screen.Bounds;
                if (rect.Left <= b.Left && rect.Top <= b.Top &&
                    rect.Right >= b.Right && rect.Bottom >= b.Bottom)
                    return true;
            }
            return false;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WindowRectangle { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr handle, out WindowRectangle rect);

        private void Reset() { _candidate = ""; _fired = false; }
        private static void SendWindowsShortcut(byte key)
        {
            // Key-up in finally prevents a stuck Windows key if the send fails.
            keybd_event(0x5B, 0, 0, UIntPtr.Zero);
            try
            {
                keybd_event(key, 0, 0, UIntPtr.Zero);
                keybd_event(key, 0, 2, UIntPtr.Zero);
            }
            finally { keybd_event(0x5B, 0, 2, UIntPtr.Zero); }
        }
        [DllImport("user32.dll")] private static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extraInfo);
    }
}
