using System.Runtime.InteropServices;
using System.Text;
using MotionDesk.Services;
using MotionDesk.UI;

namespace MotionDesk.Widgets;

internal sealed class ReplacementTaskbar : Form
{
    private static readonly Dictionary<string, ReplacementTaskbar> Bars = new(StringComparer.OrdinalIgnoreCase);
    private static System.Windows.Forms.Timer? _timer;
    internal static bool ActivityDemand => _timer != null;
    private static HotkeyManager? _shortcut;
    private static long _windowsTrayUntil;
    private static WinApi.AppWindow[] _windows = [];
    private static IReadOnlyList<MonitorTopologyService.Display> _displayIdentities = Array.Empty<MonitorTopologyService.Display>();
    private static long _identityAt;
    private static string _displaySignature = "";
    private readonly FlowLayoutPanel _apps = new() { Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true };
    private readonly Panel _root = new() { Dock = DockStyle.Fill };
    private readonly Button _start = new();
    private string _startArtStyle = "Opsis Glass";
    private readonly System.Drawing.Icon? _opsisIcon = LoadOpsisIcon();

    private static System.Drawing.Icon? LoadOpsisIcon()
    {
        try { return System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
        catch { return null; }
    }

    internal static void RefreshStartButtonArt()
    {
        foreach (var bar in Bars.Values)
        {
            bar._content = null;
            bar._start.Invalidate();
        }
        if (_timer != null) RefreshBars();
    }
    private readonly Button _taskView = new();
    private readonly Button _clock = new();
    private string? _content;
    private bool _vertical;
    private OpsisTaskbarDesigner.Profile _visual = OpsisTaskbarDesigner.Default;
    private readonly System.Windows.Forms.Timer _pulse = new() { Interval = 300 };
    private Button? _activeItem;
    private bool _pulseBright;
    private bool SignatureLook => AppSettings.Peek().ThemeMode == "SignatureGlass" && !SystemInformation.HighContrast;
    private bool CanPulse => !IsDisposed && !Disposing && _visual.ActiveButtonAnimation && Visible &&
        _activeItem is { IsDisposed: false } && Opsis.UI.OpsisMotion.Enabled &&
        !SystemInformation.HighContrast && !AppActivity.IsPausedFor(Screen.FromControl(this));

    private void SyncPulse()
    {
        if (IsDisposed || Disposing) return;
        bool animate = CanPulse;
        _pulse.Interval = Math.Max(1, _visual.AnimationIntervalMs);
        _pulse.Enabled = animate;
        if (!animate && _activeItem is { IsDisposed: false } item)
        {
            _pulseBright = false;
            item.FlatAppearance.BorderSize = 1;
            item.FlatAppearance.BorderColor = SystemInformation.HighContrast ? SystemColors.WindowText : UiTheme.AccentCyan;
        }
    }

    internal static bool AllowsReplacement(bool saved, bool permitted, bool review, bool held) => saved && permitted && !review && !held;
    private static bool Admission => AllowsReplacement(AppSettings.Peek().ReplaceWindowsTaskbar,
        RuntimeSessionPolicy.IsPermitted(SessionBackgroundService.ReplacementTaskbar), WindowsUpdateGuard.RequiresReview, WindowsUpdateGuard.RuntimeHold);

    internal static void RefreshSettings()
    {
        if (!Admission) { Stop(); return; }
        // A single taskbar owner is essential. StartAllBack/ExplorerPatcher can
        // already own the Windows shell, so Opsis does NOT stack a second
        // overlay over them. This is read-only; user/vendor files are untouched.
        try
        {
            var external = OpsisModCompatibility.ScanCached();
            if (!OpsisIntegrationAudit.TaskbarOverlayAllowed(true, external, false))
            {
                RotatingLogger.Write("Opsis taskbar held: competing shell replacement detected: " +
                    string.Join(", ", external.Where(OpsisModCompatibility.BlocksTaskbarOverlay).Select(x => x.Product)));
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
            AppActivity.RequestRefresh();
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
        TaskbarWindowPreview.CloseCurrent();
        OpsisVolumeFlyout.CloseCurrent();
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
        _displayIdentities = Array.Empty<MonitorTopologyService.Display>();
        _displaySignature = ""; _identityAt = 0; _windowsTrayUntil = 0;
        AppActivity.RequestRefresh();
    }

    private static void MoveToActiveWorkspace()
    {
        foreach (var bar in Bars.Values) bar.Dispose();
        Bars.Clear();
        RefreshBars();
    }

    internal static void YieldToDeskStrip(Rectangle reservation)
    {
        if (_timer == null || reservation.IsEmpty) return;
        foreach (var bar in Bars.Values)
        {
            if (!bar.Visible || !bar.Bounds.IntersectsWith(reservation)) continue;
            bar.Hide();
            TaskbarWindowPreview.CloseCurrent();
            OpsisVolumeFlyout.CloseCurrent();
        }
    }

    private static void RefreshBars()
    {
        if (!Admission) { Stop(); return; }
        if (_timer == null) return; // a stale queued callback cannot create another owner
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
        OpsisTaskbarDesigner.Profile profile;
        try { profile = OpsisTaskbarDesigner.Read(); }
        catch (Exception e) { RotatingLogger.Write("Taskbar visual profile: " + e.Message); profile = OpsisTaskbarDesigner.Default; }
        IReadOnlyDictionary<string, OpsisTaskbarDesigner.Profile> overrides;
        try { overrides = OpsisTaskbarDesigner.ReadMonitorProfiles(); }
        catch (Exception e) { RotatingLogger.Write("Monitor profile: " + e.Message); overrides = new Dictionary<string, OpsisTaskbarDesigner.Profile>(); }
        if (overrides.Count > 0)
        {
            var signature = string.Join("|", screens.OrderBy(x => x.Key).Select(x => x.Key + ":" + x.Value.Bounds));
            if (signature != _displaySignature || Environment.TickCount64 - _identityAt >= 15000)
            {
                _displayIdentities = MonitorTopologyService.Current();
                _displaySignature = signature; _identityAt = Environment.TickCount64;
            }
        }
        var dockReservations = DeskStripHostEngine.TaskbarReservations();
        foreach (var (id, screen) in screens)
        {
            var effective = profile;
            if (overrides.Count > 0)
            {
                var display = _displayIdentities.FirstOrDefault(x => x.DeviceName == id);
                if (display != null && overrides.TryGetValue(OpsisTaskbarDesigner.MonitorKey(display, _displayIdentities), out var local))
                    effective = local;
            }
            if (!Bars.TryGetValue(id, out var bar)) Bars[id] = bar = new ReplacementTaskbar();
            var native = shellBounds.FirstOrDefault(bounds => Screen.FromRectangle(bounds).DeviceName == id);
            if (native.IsEmpty && !screen.Primary && !OpsisTaskbarDesigner.IsDetached(effective)) { bar.Hide(); continue; }
            var localWindows = _windows.Where(window => Screen.FromHandle(window.Handle).DeviceName == id).ToArray();
            var bounds = OpsisTaskbarDesigner.CalculateBounds(effective, screen.Bounds, screen.WorkingArea, native,
                Math.Min(14, pins.Count) + localWindows.Length);
            bounds = OpsisTaskbarDesigner.AvoidDeskStrip(effective, bounds, screen.WorkingArea, dockReservations);
            if (bounds.Width < 24 || bounds.Height < 24) { bar.Hide(); continue; }
            if (bar.Bounds != bounds) bar.Bounds = bounds;
            if (AppActivity.IsFullscreenOn(screen)) { bar.Hide(); TaskbarWindowPreview.CloseCurrent(); OpsisVolumeFlyout.CloseCurrent(); continue; }
            if (!bar.Visible) bar.Show();
            bar.ApplyState(pins, localWindows, active, effective);
            // Keep the overlay above Explorer without raising it over an open Start panel or another app.
            var visited = new HashSet<IntPtr>();
            for (IntPtr preceding = GetWindow(bar.Handle, 3); preceding != IntPtr.Zero && visited.Count < 1024 && visited.Add(preceding); preceding = GetWindow(preceding, 3))
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
        _start.Text = "";
        _start.Paint += (_, e) => OpsisStartButtonArt.PaintButton(_start, e, _startArtStyle, _opsisIcon);
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
        menu.Items.Add("Windows Notification Center / Ειδοποιήσεις", null, (_, _) => RunGesture("Notifications"));
        menu.Items.Add("Windows Quick Settings / Γρήγορες ρυθμίσεις", null, (_, _) => RunGesture("Quick settings"));
        menu.Items.Add("Volume / Ένταση", null, (_, _) => RunGesture("Volume"));
        menu.Items.Add("Notification settings", null, (_, _) => OpenSettings("ms-settings:notifications"));
        menu.Items.Add("Sound settings", null, (_, _) => OpenSettings("ms-settings:sound"));
        menu.Items.Add(MainWindow.StudioText("Restore Windows taskbar", "Επαναφορά γραμμής εργασιών Windows"), null, (_, _) =>
        {
            var settings = AppSettings.Load(); settings.ReplaceWindowsTaskbar = false; settings.Save();
            BeginInvoke(Stop);
        });
        ContextMenuStrip = menu;
        foreach (var surface in new Control[] { _root, _apps })
        {
            surface.MouseUp += (_, e) => { if (e.Button == MouseButtons.Middle) RunGesture(_visual.EmptyMiddleClick); };
            surface.MouseDoubleClick += (_, e) => { if (e.Button == MouseButtons.Left) RunGesture(_visual.EmptyDoubleClick); };
        }
        Disposed += (_, _) => { _pulse.Stop(); _pulse.Dispose(); menu.Dispose(); _opsisIcon?.Dispose(); };
        _pulse.Tick += (_, _) =>
        {
            if (!CanPulse || _activeItem is not { IsDisposed: false } active) { SyncPulse(); return; }
            _pulseBright = !_pulseBright;
            active.FlatAppearance.BorderSize = _pulseBright ? 2 : 1;
            active.FlatAppearance.BorderColor = _pulseBright
                ? UiTheme.AccentCyan : UiTheme.TextSecondary;
        };
        VisibleChanged += (_, _) =>
        {
            SyncPulse();
        };
        _root.Controls.AddRange([_apps, _clock, _taskView, _start]);
        Controls.Add(_root);
    }

    private static void OpenSettings(string uri)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri) { UseShellExecute = true }); }
        catch (Exception ex) { RotatingLogger.Write("Shell settings: " + ex.Message); }
    }

    private void RunGesture(string action)
    {
        if (!OpsisTaskbarDesigner.ValidGesture(action) || WindowsUpdateGuard.RequiresReview || WindowsUpdateGuard.RuntimeHold || SafeModeService.IsSafeMode) return;
        switch (action)
        {
            case "Task view": VirtualWorkspaces.OpenTaskView(); break;
            case "DeskFlip": DeskFlipEngine.Show(); break;
            case "Notifications": VirtualWorkspaces.OpenNotifications(); break;
            case "Quick settings": VirtualWorkspaces.OpenQuickSettings(); break;
            case "Volume":
                if (_visual.VolumeStyle == "Windows") VirtualWorkspaces.OpenQuickSettings();
                else OpsisVolumeFlyout.Open(Bounds, _visual.VolumeStyle);
                break;
        }
    }

    private void ApplyState(IReadOnlyList<string> pins, IEnumerable<WinApi.AppWindow> windows, IntPtr active, OpsisTaskbarDesigner.Profile profile)
    {
        bool vertical = Height > Width;
        var list = windows.Where(window => window.Handle != Handle).ToArray();
        // These are Opsis-owned visuals; native Windows taskbar remains unchanged.
        _visual = profile;
        // Color-key transparency creates real gaps between existing controls.
        // No second taskbar window, no continuous animation and no native HWND edits.
        var gapColor = Color.FromArgb(255, 0, 254);
        bool contrast = SystemInformation.HighContrast;
        bool segmented = _visual.Segmented && !contrast;
        var background = contrast ? SystemColors.Window : segmented ? gapColor : UiTheme.NavBackground;
        TransparencyKey = segmented ? gapColor : Color.Empty;
        BackColor = _root.BackColor = _apps.BackColor = background;
        Opacity = !SignatureLook && !contrast && _visual.Style == "Glass" && !segmented ? _visual.OpacityPercent / 100.0 : 1.0;
        try { _startArtStyle = StartStudioPreferences.Read().StartButtonStyle; }
        catch (Exception ex) { RotatingLogger.Write("Opsis Start design: " + ex.Message); _startArtStyle = "Opsis Glass"; }
        var clockText = DateTime.Now.ToString(vertical ? "HH:mm" : "ddd  HH:mm");
        if (_clock.Text != clockText) _clock.Text = clockText;
        string key = $"{_startArtStyle}|{SignatureLook}|{contrast}|{DeviceDpi}|{vertical}|{Width}|{Height}|{_visual.Segmented}|{_visual.ThumbnailStyle}|{active}|{_visual.Style}|{_visual.Alignment}|{_visual.ButtonWidth}|{_visual.ButtonGap}|{_visual.ShowTaskView}|{_visual.ShowClock}|{_visual.ActiveButtonAnimation}|{_visual.AnimationIntervalMs}|{UiTheme.TextPrimary.ToArgb()}|{UiTheme.Surface.ToArgb()}|" +
            string.Join('|', pins) + "|" + string.Join('|', list.Select(window => $"{window.Handle}:{window.Title}"));
        if (_content == key)
        {
            SyncPulse();
            return;
        }
        _content = key;
        _start.Invalidate();
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
        foreach (var fixedButton in new[] { _start, _taskView, _clock }) StyleButton(fixedButton, false);
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
                }, window.Handle == active, window.Handle);
            }
        }
        finally
        {
            _apps.ResumeLayout();
            SyncPulse();
        }
    }

    private void AddItem(string label, Action click, bool active, IntPtr window = default)
    {
        var item = new Button { Text = label, AutoEllipsis = true, AccessibleName = label,
            Width = _vertical ? Math.Max(24, Width - 2 * Math.Max(3, _visual.ButtonGap) - 2) : _visual.ButtonWidth,
            Height = OpsisTaskbarDesigner.ButtonHeight(_visual, _vertical, Height),
            Margin = new Padding(_visual.Segmented ? Math.Max(3, _visual.ButtonGap) : _visual.ButtonGap), FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand,
            BackColor = active ? UiTheme.SurfaceHover : UiTheme.Surface, ForeColor = active ? UiTheme.AccentCyan : UiTheme.TextPrimary };
        StyleButton(item, active);
        if (active) _activeItem = item;
        item.Click += (_, _) => click();
        if (window != IntPtr.Zero && _visual.ThumbnailStyle != "Off")
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("Window preview / Προεπισκόπηση", null, (_, _) =>
                TaskbarWindowPreview.Open(window, label, item.RectangleToScreen(item.ClientRectangle), _visual.ThumbnailStyle));
            item.ContextMenuStrip = menu;
            item.Disposed += (_, _) => menu.Dispose();
        }
        _apps.Controls.Add(item);
    }

    private void StyleButton(Button button, bool active)
    {
        bool contrast = SystemInformation.HighContrast;
        button.FlatStyle = FlatStyle.Flat;
        button.UseVisualStyleBackColor = false;
        button.BackColor = contrast ? (active ? SystemColors.Highlight : SystemColors.Control) : active ? UiTheme.SurfaceHover : UiTheme.Surface;
        button.ForeColor = contrast ? (active ? SystemColors.HighlightText : SystemColors.ControlText) : active ? UiTheme.AccentCyan : UiTheme.TextPrimary;
        button.FlatAppearance.BorderSize = active || SignatureLook ? 1 : 0;
        button.FlatAppearance.BorderColor = contrast ? SystemColors.WindowText : active ? UiTheme.AccentCyan : UiTheme.Border;
        button.FlatAppearance.MouseOverBackColor = contrast ? SystemColors.Control : UiTheme.SurfaceHover;
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
