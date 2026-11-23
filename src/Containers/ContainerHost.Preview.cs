using System.Runtime.InteropServices;
using MotionDesk.Services;

namespace MotionDesk.Containers;

internal sealed partial class ContainerHost
{
    private readonly System.Windows.Forms.Timer _peekTimer = new() { Interval = 30 };
    private readonly System.Windows.Forms.Timer _monitorTimer = new() { Interval = 550 };
    private readonly List<PeekShade> _peekShades = [];
    private bool _peeking;
    private IntPtr _peekForeground;
    private long _peekStarted;
    private string? _previousTopology;

    private sealed class PeekShade : Form
    {
        internal PeekShade(Rectangle bounds)
        {
            Bounds = bounds;
            StartPosition = FormStartPosition.Manual;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            BackColor = Color.Black;
            Opacity = 0.01;
            TopMost = true;
        }

        protected override bool ShowWithoutActivation => true;
        protected override CreateParams CreateParams
        {
            get { var parameters = base.CreateParams; parameters.ExStyle |= 0x08000000; return parameters; }
        }
    }

    private static string MonitorTopology() => string.Join("|", Screen.AllScreens.OrderBy(screen => screen.DeviceName, StringComparer.Ordinal)
        .Select(screen => FormattableString.Invariant($"{screen.DeviceName}:{screen.Bounds.X},{screen.Bounds.Y},{screen.Bounds.Width},{screen.Bounds.Height},{screen.Primary}")));

    internal string? LayoutForCurrentMonitors() =>
        AppSettings.Load().ContainerMonitorLayouts.TryGetValue(MonitorTopology(), out string? name) ? name : null;

    internal void BindLayoutToCurrentMonitors(string? name)
    {
        var settings = AppSettings.Load();
        var topology = MonitorTopology();
        if (name == null) settings.ContainerMonitorLayouts.Remove(topology);
        else settings.ContainerMonitorLayouts[topology] = name;
        settings.Save();
    }

    private void ApplyMonitorLayout()
    {
        _monitorTimer.Stop();
        string topology = MonitorTopology();
        if (topology == _previousTopology) return;
        _previousTopology = topology;
        StopPeek();
        if (LayoutForCurrentMonitors() is { } layout) LoadSnapshot(layout);
        var monitors = Monitors();
        foreach (var window in _windows.Values) window.SetPlacement(Place(window.Model, monitors));
    }

    internal void TogglePeek()
    {
        if (_peeking) { StopPeek(); return; }
        if (!_windows.Values.Any(window => window.Visible)) return;
        _peekForeground = GetForegroundWindow();
        _peekStarted = Environment.TickCount64;
        if (AppSettings.Load().ContainerPeekDim)
        {
            foreach (var screen in Screen.AllScreens)
            {
                var shade = new PeekShade(screen.Bounds);
                shade.MouseDown += (_, _) => StopPeek();
                _peekShades.Add(shade);
                shade.Show();
            }
        }
        _peeking = true;
        ReseatZOrder();
        _peekTimer.Start();
    }

    private void UpdatePeek()
    {
        long elapsed = Environment.TickCount64 - _peekStarted;
        var foreground = GetForegroundWindow();
        if (_windows.Count == 0 || (GetAsyncKeyState((int)Keys.Escape) & 0x8000) != 0 ||
            (elapsed > 250 && foreground != IntPtr.Zero && foreground != _peekForeground &&
                !_windows.Values.Any(window => window.IsHandleCreated && window.Handle == foreground)))
        {
            StopPeek();
            return;
        }
        double opacity = Math.Clamp(elapsed / 150d, 0.01, 1) * 0.36;
        foreach (var shade in _peekShades)
            if (Math.Abs(shade.Opacity - opacity) > 0.001) shade.Opacity = opacity;
    }

    private void StopPeek()
    {
        if (!_peeking) return;
        _peekTimer.Stop();
        _peeking = false;
        foreach (var shade in _peekShades) shade.Dispose();
        _peekShades.Clear();
        foreach (var window in _windows.Values) window.TopMost = false;
        ReseatZOrder();
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
}
