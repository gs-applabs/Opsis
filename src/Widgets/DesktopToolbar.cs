using MotionDesk.Services;
using MotionDesk.UI;

namespace MotionDesk.Widgets;

internal sealed class DesktopToolbar : Form
{
    private static readonly Dictionary<string, DesktopToolbar> Bars = new(StringComparer.OrdinalIgnoreCase);
    private static System.Windows.Forms.Timer? _timer;
    private const int BarHeight = 38;
    private readonly Label _clock = new() { AutoSize = false, TextAlign = ContentAlignment.MiddleRight, Dock = DockStyle.Right, Width = 182 };
    private readonly FlowLayoutPanel _actions = new() { Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true, Padding = new Padding(9, 3, 0, 0) };
    private readonly GlassPaneBinder _glass;

    internal static void RefreshSettings()
    {
        if (SafeModeService.IsSafeMode || !AppSettings.Load().DesktopToolbarEnabled) { Stop(); return; }
        if (_timer == null)
        {
            _timer = new System.Windows.Forms.Timer { Interval = 1000 };
            _timer.Tick += (_, _) => Sync();
            ThemeManager.Changed += RefreshColors;
            LocalizationManager.Changed += Rebuild;
            _timer.Start();
        }
        Sync();
    }

    private static void Rebuild()
    {
        Stop();
        RefreshSettings();
    }

    internal static Rectangle WorkAreaForTiling(Screen screen)
    {
        var area = screen.WorkingArea;
        if (!Bars.TryGetValue(screen.DeviceName, out var bar) || !bar.Visible) return area;
        return Rectangle.FromLTRB(area.Left, Math.Min(area.Bottom, bar.Bottom), area.Right, area.Bottom);
    }

    internal static void Stop()
    {
        if (_timer == null) return;
        _timer.Dispose();
        _timer = null;
        ThemeManager.Changed -= RefreshColors;
        LocalizationManager.Changed -= Rebuild;
        foreach (var bar in Bars.Values) bar.Dispose();
        Bars.Clear();
    }

    private static void RefreshColors()
    {
        foreach (var bar in Bars.Values) bar.ApplyColors();
    }

    private static void Sync()
    {
        var settings = AppSettings.Peek();
        var screens = Screen.AllScreens.Where(screen => screen.Primary || settings.DesktopToolbarAllMonitors)
            .ToDictionary(screen => screen.DeviceName, StringComparer.OrdinalIgnoreCase);
        foreach (var name in Bars.Keys.Where(id => !screens.ContainsKey(id)).ToArray())
        {
            Bars[name].Dispose();
            Bars.Remove(name);
        }
        foreach (var (id, screen) in screens)
        {
            if (!Bars.TryGetValue(id, out var bar)) Bars[id] = bar = new DesktopToolbar();
            var area = screen.WorkingArea;
            int height = BarHeight * bar.DeviceDpi / 96;
            var bounds = new Rectangle(area.Left, area.Top, area.Width, height);
            if (bar.Bounds != bounds) bar.Bounds = bounds;
            bool show = !AppActivity.IsFullscreenOn(screen);
            if (bar.Visible != show) bar.Visible = show;
            string time = DateTime.Now.ToString("ddd d MMM   HH:mm");
            if (bar._clock.Text != time) bar._clock.Text = time;
        }
    }

    private DesktopToolbar()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        Padding = new Padding(0, 0, 12, 0);
        Add(MainWindow.StudioText("Search", "Αναζήτηση"), DesktopLauncher.Open, 100);
        Add("Opsis", () => TrayApplicationContext.OpenPage?.Invoke("Dashboard"), 77);
        Add(LocalizationManager.T("Widgets.QuickSettings"), () => OpenWidget("quicksettings", 380, 336), 163);
        Add(MainWindow.StudioText("Media", "Πολυμέσα"), () => OpenWidget("media", 372, 216), 105);
        Add(MainWindow.StudioText("Windows", "Παράθυρα"), () => DeskFlipEngine.Show(), 105);
        Add(MainWindow.StudioText("Calendar", "Ημερολόγιο"), () => OpenWidget("calendar", 365, 284), 112);
        Add(MainWindow.StudioText("Tile windows", "Τακτοποίηση"), () => AutoTileService.Apply(force: true), 125);
        Controls.AddRange([_actions, _clock]);
        _glass = new GlassPaneBinder(this, 8, () => GlassService.Enabled, DesktopSurface.DeskStrip);
        ApplyColors();
    }

    private void Add(string name, Action open, int width)
    {
        var button = new Button { Text = name, AccessibleName = name, AutoEllipsis = true, Font = UiTheme.FontBody,
            Width = width, Height = 28, FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand, Margin = new Padding(0, 0, 4, 0) };
        button.FlatAppearance.BorderSize = 0;
        button.Click += (_, _) => open();
        _actions.Controls.Add(button);
    }

    private void ApplyColors()
    {
        BackColor = UiTheme.NavBackground;
        _clock.ForeColor = UiTheme.TextSecondary;
        foreach (Control action in _actions.Controls)
        {
            action.BackColor = UiTheme.Surface;
            action.ForeColor = UiTheme.TextPrimary;
        }
    }

    private void OpenWidget(string id, int width, int height)
    {
        var widget = WidgetHostEngine.Instance.GetActiveWidgets().FirstOrDefault(item => item.WidgetId == id && !item.IsDisposed);
        if (widget != null) { widget.Show(); widget.BringToFront(); return; }
        WidgetHostEngine.Instance.SpawnWidget(id, Left + 18, Bottom + 12, width, height);
    }

    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get { var parameters = base.CreateParams; parameters.ExStyle |= 0x80; return parameters; }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _glass?.Dispose();
        base.Dispose(disposing);
    }
}
