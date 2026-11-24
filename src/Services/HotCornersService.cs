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
        private const int Edge = 5;
        private const int DwellMs = 700;
        public HotCornersService(Action openOpsis)
        {
            _openOpsis = openOpsis;
            _timer.Tick += (_, _) => Tick();
        }
        public void Start() => _timer.Start();
        public void Stop() => _timer.Stop();
        public void Dispose() { _timer.Stop(); _timer.Dispose(); }

        private void Tick()
        {
            var settings = AppSettings.Peek();
            if (!settings.HotCornersEnabled || SystemInformation.TerminalServerSession)
            {
                Reset();
                return;
            }
            var p = Cursor.Position;
            var screen = Screen.AllScreens.FirstOrDefault(s => s.Bounds.Contains(p));
            if (screen == null) { Reset(); return; }
            var b = screen.Bounds;
            var top = p.Y - b.Top < Edge;
            var bottom = b.Bottom - 1 - p.Y < Edge;
            var left = p.X - b.Left < Edge;
            var right = b.Right - 1 - p.X < Edge;
            var corner = top && left ? "TopLeft" : top && right ? "TopRight"
                : bottom && left ? "BottomLeft" : bottom && right ? "BottomRight" : "";
            if (corner != _candidate)
            {
                _candidate = corner;
                _enteredAt = Environment.TickCount64;
                _fired = false;
            }
            if (corner == "" || _fired || Environment.TickCount64 - _enteredAt < DwellMs) return;
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
