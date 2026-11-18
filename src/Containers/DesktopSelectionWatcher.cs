using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using MotionDesk.Services;
using MotionDesk.Widgets;

namespace MotionDesk.Containers
{
    /// <summary>
    /// Rubber-band to container (user request, plan 22-A-6): after the user drags a selection rectangle over desktop icons and selects two or more, a small button appears next to the pointer
    /// ("Make a container from N selected icons"). Only a passive low-level mouse hook that looks at button down / up over the desktop list; nothing is read from other windows and nothing is changed
    /// until the button is pressed. It never swallows an event.
    /// </summary>
    internal sealed class DesktopSelectionWatcher : IDisposable
    {
        private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] private static extern IntPtr SetWindowsHookEx(int id, HookProc cb, IntPtr module, uint thread);
        [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll")] private static extern IntPtr GetModuleHandle(string? name);
        [StructLayout(LayoutKind.Sequential)] private struct MsLlHook { public int X, Y; public uint Data, Flags, Time; public IntPtr Extra; }

        private readonly ContainerHost _host; private readonly HookProc _proc; private IntPtr _hook;
        private bool _downOnDesktop; private Point _down; private readonly System.Windows.Forms.Timer _check = new() { Interval = 220 };
        private Form? _offer;

        public DesktopSelectionWatcher(ContainerHost host)
        {
            _host = host; _proc = Callback; _check.Tick += (_, _) => { _check.Stop(); Evaluate(); };
            _hook = SetWindowsHookEx(14 /* WH_MOUSE_LL */, _proc, GetModuleHandle(null), 0);
        }

        private IntPtr Callback(int code, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (code >= 0)
                {
                    var m = Marshal.PtrToStructure<MsLlHook>(lParam); int msg = wParam.ToInt32();
                    if (msg == 0x0201) { _down = new Point(m.X, m.Y); var inOffer = _offer != null && _offer.Bounds.Contains(_down); _downOnDesktop = !inOffer && AppSettings.Peek().OfferContainerFromSelection && DesktopIconVisibilityEngine.IsDesktopListAt(_down); if (!inOffer) CloseOffer(); }
                    else if (msg == 0x0202 && _downOnDesktop) { _downOnDesktop = false; if (Math.Abs(m.X - _down.X) + Math.Abs(m.Y - _down.Y) > 16) { _check.Stop(); _check.Start(); } }
                }
            }
            catch (Exception) { }
            return CallNextHookEx(_hook, code, wParam, lParam);
        }

        private void Evaluate()
        {
            var paths = DesktopIconVisibilityEngine.SelectedDesktopPaths(); if (paths.Count < 2) return;
            CloseOffer();
            var at = Cursor.Position;
            var b = new Button { Text = string.Format(LocalizationManager.T("DCM.FromSelection"), paths.Count), AutoSize = true, FlatStyle = FlatStyle.Flat, Padding = new Padding(8, 4, 8, 4), BackColor = Color.FromArgb(32, 36, 52), ForeColor = Color.White, Cursor = Cursors.Hand };
            b.FlatAppearance.BorderColor = Color.FromArgb(90, 130, 255);
            _offer = new Form { FormBorderStyle = FormBorderStyle.None, ShowInTaskbar = false, TopMost = true, StartPosition = FormStartPosition.Manual, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Location = new Point(at.X + 12, at.Y + 12), BackColor = Color.FromArgb(90, 130, 255), Padding = new Padding(1) };
            _offer.Controls.Add(b);
            b.Click += (_, _) => { var f = _offer; CloseOffer(); _host.CreateFromDesktopSelection(paths, at); };
            _offer.Deactivate += (_, _) => CloseOffer();
            var life = new System.Windows.Forms.Timer { Interval = 7000 }; life.Tick += (_, _) => { life.Stop(); life.Dispose(); CloseOffer(); }; life.Start();
            _offer.Show();
        }

        private void CloseOffer() { var f = _offer; _offer = null; if (f != null && !f.IsDisposed) { try { f.Close(); f.Dispose(); } catch (Exception) { } } }

        public void Dispose() { _check.Dispose(); CloseOffer(); if (_hook != IntPtr.Zero) { UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero; } }
    }
}
