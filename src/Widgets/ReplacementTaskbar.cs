using System.Runtime.InteropServices;
using System.Text;
using MotionDesk.Services;
using MotionDesk.UI;

namespace MotionDesk.Widgets;

internal sealed class ReplacementTaskbar : Form
{
    private static readonly Dictionary<string, ReplacementTaskbar> Bars = new(StringComparer.OrdinalIgnoreCase);
    private static System.Windows.Forms.Timer? _timer;
    private static HotkeyManager? _shortcut;
    private static long _windowsTrayUntil;
    private static WinApi.AppWindow[] _windows = [];
    private readonly FlowLayoutPanel _apps = new() { Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true };
    private readonly Panel _root = new() { Dock = DockStyle.Fill };
    private readonly Button _start = new();
    private readonly Button _taskView = new();
    private readonly Button _clock = new();
    private string? _content;
    private bool _vertical;
    private OpsisTaskbarDesigner.Profile _visual = OpsisTaskbarDesigner.Default;
    private readonly System.Windows.Forms.Timer _pulse = new() { Interval = 300 };
    private Button? _activeItem;
    private bool _pulseBright;

    internal static void RefreshSettings()
    {
        if (SafeModeService.IsSafeMode || WindowsUpdateGuard.RequiresReview ||
            !AppSettings.Load().ReplaceWindowsTaskbar) { Stop(); return; }
        // A single taskbar owner is essential. StartAllBack/ExplorerPatcher can
        // already own the Windows shell, so Opsis does NOT stack a second
        // overlay over them. This is read-only; user/vendor files are untouched.
        try
        {
            var external = OpsisModCompatibility.Scan();
            if (!OpsisIntegrationAudit.TaskbarOverlayAllowed(true, external, false))
            {
                RotatingLogger.Write("Opsis taskbar held: competing shell replacement detected: " +
                    string.Join(", ", external.Where(x => x.Risk == "High").Select(x => x.Product)));
                Stop();
                return;
            }
        }
        catch (Exception ex)
        {
            RotatingLogger.Write("Opsis taskbar compatibility scan unavailable: " + ex.Message);
            Stop(); // fail closed, leave the normal Windows taskbar running.
            return;
        }
        if (_timer == null)
        {
            _timer = new System.Windows.Forms.Timer { Interval = 950 };
            _timer.Tick += (_, _) => RefreshBars();
            _timer.Start();
            ThemeManager.Changed += RefreshBars;
            VirtualWorkspaces.Changed += MoveToActiveWorkspace;
            _shortcut = new HotkeyManager();
            _shortcut.RegisterCtrl(Keys.Escape, () =>
            {
                var screen = Screen.FromPoint(Cursor.Position);
                if (Bars.TryGetValue(screen.DeviceName, out var bar)) DesktopLauncher.OpenStart(bar.Bounds);
            });
        }
        RefreshBars();
    }

    internal static void Stop()
    {
        if (_timer == null) return;
        _timer.Dispose();
        _timer = null;
        _shortcut?.Dispose();
        _shortcut = null;
        ThemeManager.Changed -= RefreshBars;
        VirtualWorkspaces.Changed -= MoveToActiveWorkspace;
        foreach (var bar in Bars.Values) bar.Dispose();
        Bars.Clear();
        _windows = [];
    }

    private static void MoveToActiveWorkspace()
    {
        foreach (var bar in Bars.Values) bar.Dispose();
        Bars.Clear();
        RefreshBars();
    }

    private static void RefreshBars()
    {
        if (Environment.TickCount64 < _windowsTrayUntil) return;
        var screens = Screen.AllScreens.ToDictionary(screen => screen.DeviceName, StringComparer.OrdinalIgnoreCase);
        foreach (var stale in Bars.Keys.Where(id => !screens.ContainsKey(id)).ToArray())
        {
            Bars[stale].Dispose();
            Bars.Remove(stale);
        }
        _windows = WinApi.EnumerateAppWindows().ToArray();
        var shellBounds = NativeTaskbars();
        var active = WinApi.GetForegroundWindow();
        var pins = DeskStripHostEngine.Instance.GetPinnedApps();
        foreach (var (id, screen) in screens)
        {
            if (!Bars.TryGetValue(id, out var bar)) Bars[id] = bar = new ReplacementTaskbar();
            var native = shellBounds.FirstOrDefault(bounds => Screen.FromRectangle(bounds).DeviceName == id);
            if (native.IsEmpty && !screen.Primary) { bar.Hide(); continue; }
            var bounds = native.IsEmpty ? new Rectangle(screen.Bounds.Left, screen.Bounds.Bottom - 44, screen.Bounds.Width, 44)
                : Rectangle.Intersect(native, screen.Bounds);
            if (bounds.Width < 24 || bounds.Height < 24) { bar.Hide(); continue; }
            if (bar.Bounds != bounds) bar.Bounds = bounds;
            if (AppActivity.IsFullscreenOn(screen)) { bar.Hide(); continue; }
            if (!bar.Visible) bar.Show();
            bar.ApplyState(pins, _windows.Where(window => Screen.FromHandle(window.Handle).DeviceName == id), active);
            // Keep the overlay above Explorer without raising it over an open Start panel or another app.
            for (IntPtr preceding = GetWindow(bar.Handle, 3); preceding != IntPtr.Zero; preceding = GetWindow(preceding, 3))
            {
                if (!IsTray(preceding) || !GetWindowRect(preceding, out var rect) ||
                    !bounds.IntersectsWith(Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom))) continue;
                SetWindowPos(bar.Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0013);
                break;
            }
        }
    }

    private ReplacementTaskbar()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        _start.Text = "Opsis";
        _start.AccessibleName = MainWindow.StudioText("Start", "Έναρξη");
        _start.Click += (_, _) => DesktopLauncher.OpenStart(Bounds);
        _taskView.Text = "▣";
        _taskView.AccessibleName = MainWindow.StudioText("Task View", "Προβολή εργασιών");
        _taskView.Click += (_, _) => VirtualWorkspaces.OpenTaskView();
        _clock.AccessibleName = MainWindow.StudioText("Clock and calendar", "Ρολόι και ημερολόγιο");
        _clock.Click += (_, _) => TrayApplicationContext.OpenPage?.Invoke("Widgets");
        var menu = new ContextMenuStrip();
        menu.Items.Add(MainWindow.StudioText("DeskStrip settings", "Ρυθμίσεις DeskStrip"), null, (_, _) => TrayApplicationContext.OpenPage?.Invoke("DeskStrip"));
        menu.Items.Add(MainWindow.StudioText("Open Windows notification area", "Περιοχή ειδοποιήσεων Windows"), null, (_, _) =>
        {
            _windowsTrayUntil = Environment.TickCount64 + 10000;
            foreach (var bar in Bars.Values) bar.Hide();
            VirtualWorkspaces.OpenSystemTray();
        });
        menu.Items.Add(MainWindow.StudioText("Restore Windows taskbar", "Επαναφορά γραμμής εργασιών Windows"), null, (_, _) =>
        {
            var settings = AppSettings.Load(); settings.ReplaceWindowsTaskbar = false; settings.Save();
            BeginInvoke(Stop);
        });
        ContextMenuStrip = menu;
        Disposed += (_, _) => { _pulse.Stop(); _pulse.Dispose(); menu.Dispose(); };
        _pulse.Tick += (_, _) =>
        {
            if (_activeItem == null || _activeItem.IsDisposed || !Visible) return;
            _pulseBright = !_pulseBright;
            _activeItem.FlatAppearance.BorderSize = _pulseBright ? 2 : 1;
            _activeItem.FlatAppearance.BorderColor = _pulseBright
                ? UiTheme.AccentCyan : UiTheme.TextSecondary;
        };
        VisibleChanged += (_, _) =>
        {
            _pulse.Enabled = Visible && _visual.ActiveButtonAnimation && _activeItem != null;
        };
        _root.Controls.AddRange([_apps, _clock, _taskView, _start]);
        Controls.Add(_root);
    }

    private void ApplyState(IReadOnlyList<string> pins, IEnumerable<WinApi.AppWindow> windows, IntPtr active)
    {
        bool vertical = Height > Width;
        var list = windows.Where(window => window.Handle != Handle).ToArray();
        // These are Opsis-owned visuals; native Windows taskbar remains unchanged.
        try { _visual = OpsisTaskbarDesigner.Read(); }
        catch (Exception e) { RotatingLogger.Write("Taskbar visual profile: " + e.Message); _visual = OpsisTaskbarDesigner.Default; }
        BackColor = UiTheme.NavBackground;
        Opacity = _visual.Style == "Glass" ? _visual.OpacityPercent / 100.0 : 1.0;
        _start.BackColor = UiTheme.Surface;
        _start.ForeColor = UiTheme.TextPrimary;
        _clock.Text = DateTime.Now.ToString(vertical ? "HH:mm" : "ddd  HH:mm");
        _clock.ForeColor = UiTheme.TextPrimary;
        _clock.BackColor = UiTheme.Surface;
        _taskView.ForeColor = UiTheme.TextPrimary;
        _taskView.BackColor = UiTheme.Surface;
        string key = $"{vertical}|{active}|{_visual.Style}|{_visual.Alignment}|{_visual.ButtonWidth}|{_visual.ButtonGap}|{_visual.ShowTaskView}|{_visual.ShowClock}|{_visual.ActiveButtonAnimation}|{_visual.AnimationIntervalMs}|{UiTheme.TextPrimary.ToArgb()}|{UiTheme.Surface.ToArgb()}|" +
            string.Join('|', pins) + "|" + string.Join('|', list.Select(window => $"{window.Handle}:{window.Title}"));
        if (_content == key)
        {
            _pulse.Interval = _visual.AnimationIntervalMs;
            _pulse.Enabled = _visual.ActiveButtonAnimation && Visible && _activeItem != null;
            return;
        }
        _content = key;
        _vertical = vertical;
        _start.Dock = _taskView.Dock = vertical ? DockStyle.Top : DockStyle.Left;
        _clock.Dock = vertical ? DockStyle.Bottom : DockStyle.Right;
        _taskView.Visible = _visual.ShowTaskView;
        _clock.Visible = _visual.ShowClock;
        _apps.FlowDirection = vertical ? FlowDirection.TopDown : FlowDirection.LeftToRight;
        _apps.Padding = (!vertical && _visual.Alignment == "Center") ? new Padding(Math.Min(80, Width / 10), 0, 0, 0) : Padding.Empty;
        _start.Size = new Size(_visual.Style == "Compact" ? 60 : 66, _visual.Style == "Compact" ? 32 : 38);
        _taskView.Size = new Size(_visual.Style == "Compact" ? 35 : 42, _visual.Style == "Compact" ? 32 : 36);
        _clock.Size = new Size(104, _visual.Style == "Compact" ? 35 : 42);
        foreach (var fixedButton in new[] { _start, _taskView, _clock }) fixedButton.FlatStyle = FlatStyle.Flat;
        _apps.FlowDirection = vertical ? FlowDirection.TopDown : FlowDirection.LeftToRight;
        _apps.SuspendLayout();
        try
        {
            _pulse.Stop();
            _activeItem = null;
            foreach (Control old in _apps.Controls.Cast<Control>().ToArray()) old.Dispose();
            foreach (var file in pins.Take(14))
            {
                AddItem(Path.GetFileNameWithoutExtension(file), ActionLaunch, false);
                void ActionLaunch()
                {
                    if (SafeLaunch.IsAllowed(file))
                    {
                        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(file) { UseShellExecute = true }); }
                        catch (System.ComponentModel.Win32Exception) { }
                    }
                }
            }
            foreach (var window in list)
            {
                AddItem(window.Title, () =>
                {
                    if (WinApi.GetForegroundWindow() == window.Handle) WinApi.Minimize(window.Handle);
                    else WinApi.BringToFront(window.Handle);
                }, window.Handle == active);
            }
        }
        finally
        {
            _apps.ResumeLayout();
            _pulse.Interval = _visual.AnimationIntervalMs;
            _pulse.Enabled = _visual.ActiveButtonAnimation && Visible && _activeItem != null;
        }
    }

    private void AddItem(string label, Action click, bool active)
    {
        var item = new Button { Text = label, AutoEllipsis = true, AccessibleName = label,
            Width = _vertical ? Math.Max(40, Width - 7) : _visual.ButtonWidth,
            Height = OpsisTaskbarDesigner.ButtonHeight(_visual, _vertical, Height),
            Margin = new Padding(_visual.ButtonGap), FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand,
            BackColor = active ? UiTheme.SurfaceHover : UiTheme.Surface, ForeColor = active ? UiTheme.AccentCyan : UiTheme.TextPrimary };
        item.FlatAppearance.BorderSize = active ? 1 : 0;
        if (active) _activeItem = item;
        item.Click += (_, _) => click();
        _apps.Controls.Add(item);
    }

    private static Rectangle[] NativeTaskbars()
    {
        var bounds = new List<Rectangle>();
        EnumWindows((hwnd, _) =>
        {
            if (IsTray(hwnd) && GetWindowRect(hwnd, out var rect)) bounds.Add(Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom));
            return true;
        }, IntPtr.Zero);
        return bounds.ToArray();
    }

    private static bool IsTray(IntPtr hwnd)
    {
        var name = new StringBuilder(48);
        GetClassName(hwnd, name, name.Capacity);
        return name.ToString() is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";
    }

    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get { var parameters = base.CreateParams; parameters.ExStyle |= 0x80 | 0x08000000; return parameters; }
    }

    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    private delegate bool Visitor(IntPtr hwnd, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(Visitor visitor, IntPtr parameter);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hwnd, uint command);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int capacity);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int w, int h, uint flags);
}
