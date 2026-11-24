using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using MotionDesk.Services;

namespace MotionDesk.UI;

/// <summary>
/// Optional cursor halo/click ripple. Uses a tiny transparent WinForms window
/// following the pointer; no global mouse hook, DLL injection or shell patch.
/// It is paused for fullscreen windows and battery operation when requested.
/// </summary>
public static class OpsisCursorFx
{
    private static CursorFxOverlay? _overlay;
    public static bool Running => _overlay is { IsDisposed: false };

    public static void Start()
    {
        if (Running || SafeModeService.IsSafeMode || WindowsUpdateGuard.RequiresReview) return;
        _overlay = new CursorFxOverlay();
        _overlay.Show();
    }

    public static void Stop()
    {
        var overlay = _overlay;
        _overlay = null;
        if (overlay == null || overlay.IsDisposed) return;
        overlay.Close();
        overlay.Dispose();
    }
}

internal sealed class CursorFxOverlay : Form
{
    private const int ExTransparent = 0x20;
    private const int ExToolWindow = 0x80;
    private const int ExNoActivate = 0x08000000;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 50 };
    private AppSettings? _settings;
    private bool _compatibilityHold;
    private DateTime _settingsAt = DateTime.MinValue;
    private Point _lastPosition = new(int.MinValue, int.MinValue);
    private readonly int _diameter = 142;
    private DateTime _lastClickUtc = DateTime.MinValue;
    private bool _wasDown;
    private string _currentStyle = "Both";
    private int _intensity = 70;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);

    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= ExTransparent | ExToolWindow | ExNoActivate;
            return cp;
        }
    }

    internal CursorFxOverlay()
    {
        Text = "Opsis Cursor FX";
        ShowInTaskbar = false;
        ControlBox = false;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        BackColor = Color.Magenta;
        TransparencyKey = Color.Magenta;
        ClientSize = new Size(_diameter, _diameter);
        DoubleBuffered = true;
        _timer.Tick += (_, _) => Tick();
        Shown += (_, _) => _timer.Start();
        FormClosed += (_, _) => { _timer.Stop(); _timer.Dispose(); };
    }

    private static bool FullscreenForeground()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero || hwnd == GetShellWindow() || IsIconic(hwnd)) return false;
        if (!GetWindowRect(hwnd, out var r)) return false;
        var monitor = Screen.FromHandle(hwnd).Bounds;
        // Maximized applications may also be suppressed: safety over effects.
        return r.Left <= monitor.Left && r.Top <= monitor.Top &&
            r.Right >= monitor.Right && r.Bottom >= monitor.Bottom;
    }

    private void Tick()
    {
        if (IsDisposed) return;
        try
        {
            var now = DateTime.UtcNow;
            if (_settings == null || (now - _settingsAt).TotalSeconds >= 5)
            {
                _settings = AppSettings.Peek();
                _compatibilityHold = SafeModeService.IsSafeMode || WindowsUpdateGuard.RequiresReview;
                _settingsAt = now;
            }
            var settings = _settings;
            if (!settings.CursorFxEnabled || _compatibilityHold || FullscreenForeground() ||
                (settings.PauseOnBatterySaver &&
                 SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Offline))
            {
                if (Visible) Hide();
                return;
            }
            _currentStyle = settings.CursorFxStyle is "Halo" or "Ripple" or "Both"
                ? settings.CursorFxStyle : "Both";
            _intensity = Math.Clamp(settings.CursorFxIntensity, 20, 100);
            var cursor = Cursor.Position;
            var next = new Point(cursor.X - Width / 2, cursor.Y - Height / 2);
            var moved = next != _lastPosition;
            if (moved) { Location = next; _lastPosition = next; }
            var down = (Control.MouseButtons & MouseButtons.Left) != 0;
            if (down && !_wasDown) _lastClickUtc = DateTime.UtcNow;
            _wasDown = down;
            if (!Visible) Show();
            var rippleActive = _lastClickUtc != DateTime.MinValue &&
                (now - _lastClickUtc).TotalSeconds < 0.55;
            if (moved || rippleActive) Invalidate();
        }
        catch (Exception ex)
        {
            _timer.Stop();
            Hide();
            RotatingLogger.Write("Cursor FX disabled after overlay failure: " + ex.Message);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var c = _diameter / 2f;
        var weight = _intensity / 100f;
        if (_currentStyle is "Halo" or "Both")
        {
            using var halo = new Pen(Color.FromArgb((int)(165 * weight), 70, 207, 238), 2.5f);
            using var soft = new Pen(Color.FromArgb((int)(45 * weight), 70, 207, 238), 9f);
            e.Graphics.DrawEllipse(soft, c - 16, c - 16, 32, 32);
            e.Graphics.DrawEllipse(halo, c - 15, c - 15, 30, 30);
        }
        if ((_currentStyle is "Ripple" or "Both") && _lastClickUtc != DateTime.MinValue)
        {
            var elapsed = (DateTime.UtcNow - _lastClickUtc).TotalSeconds;
            if (elapsed < 0.55)
            {
                var t = Math.Clamp(elapsed / 0.55, 0, 1);
                var radius = 15 + (float)t * 48;
                var alpha = (int)((1 - t) * 210 * weight);
                using var ripple = new Pen(Color.FromArgb(Math.Clamp(alpha, 0, 255), 95, 220, 255), 3);
                e.Graphics.DrawEllipse(ripple, c - radius, c - radius, radius * 2, radius * 2);
            }
        }
    }
}
