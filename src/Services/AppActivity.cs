using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using MotionDesk.Widgets;

namespace MotionDesk.Services;

/// <summary>
/// Κεντρική γνώση του αν το desktop πρέπει να "ησυχάσει": εφαρμογή πλήρους οθόνης (παιχνίδι, video player),
/// κλειδωμένη συνεδρία, απομακρυσμένη (RDP) συνεδρία ή Battery Saver. Πριν, μόνο το wallpaper σταματούσε σε
/// fullscreen — όλα τα widgets (ρολόι, system monitor, audio visualizer στα 25Hz) συνέχιζαν να τρέχουν timers και
/// να ζωγραφίζουν κάτω από το παιχνίδι, κλέβοντας CPU/GPU ακριβώς όταν χρειάζεται όλη η ισχύς. Τώρα υπάρχει ΕΝΑ
/// shared state: τα widgets ελέγχουν <see cref="IsFullscreenAppActive"/> (= οποιοσδήποτε λόγος παύσης, το όνομα
/// μένει για συμβατότητα) και το wallpaper εγγράφεται στο <see cref="FullscreenChanged"/> και ρωτά
/// <see cref="IsPausedFor"/> ανά οθόνη. Πρέπει να δημιουργηθεί/ξεκινήσει από το UI thread.
/// Fullscreen: 1s polling, 2 συνεχόμενα θετικά δείγματα για είσοδο (όχι αναλαμπές), άμεση έξοδος.
/// </summary>
public static class AppActivity
{
    private static System.Windows.Forms.Timer? _timer;
    private static SynchronizationContext? _ui;
    private static Func<bool>? _containerDemand;
    private static bool _diagnostics, _sessionLocked, _detailed;
    private static int _refreshQueued;
    internal readonly record struct Demand(bool Detailed, bool FullscreenOnly, bool Coverage)
    {
        public bool Any => Detailed || FullscreenOnly || Coverage;
    }

    internal static Demand SelectDemand(bool wallpaper, bool widgets, bool taskbar, bool glass,
        bool discord, bool containerRules, bool toolbar, bool autoTile, bool dock, bool diagnostics) =>
        new(wallpaper || widgets || taskbar || glass || discord || containerRules || diagnostics,
            toolbar || autoTile || dock, wallpaper || taskbar || diagnostics);

    private static Demand CurrentDemand() => SelectDemand(
        WallpaperHostEngine.HasActiveScene, WidgetHostEngine.HasVisibleWidgets,
        ReplacementTaskbar.ActivityDemand, GlassService.ExistingManager != null,
        DiscordPresence.Instance.ActivityDemand, _containerDemand?.Invoke() == true,
        DesktopToolbar.ActivityDemand, AutoTileService.ActivityDemand,
        DeskStripHostEngine.ActivityDemand, _diagnostics);
    private static int _fullscreenHits;
    private static int _slowTick;
    private static bool _fullscreen, _locked, _remote, _batterySaver, _screensaver, _highLoad, _awake;
    private static int _loadHits;
    private static string? _fullscreenDevice;
    // 2.6.0: monitors whose desktop is fully covered by an opaque window (see DesktopCoverage): the wallpaper there is invisible, so it is paused (no quality loss).
    private static HashSet<string> _covered = new(StringComparer.OrdinalIgnoreCase);
    private static string _coveredKey = "";
    private static HashSet<string> _previousCovered = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>True όταν υπάρχει οποιοσδήποτε λόγος παύσης (fullscreen, lock, RDP, battery saver).</summary>
    public static bool IsFullscreenAppActive { get; private set; }

    /// <summary>2.6.0: the individual reasons, for the glass layer (it suspends on lock / remote / fullscreen and lowers its rate on battery saver).</summary>
    public readonly record struct ActivitySnapshot(bool Locked, bool Remote, bool Fullscreen, bool BatterySaver);
    public static ActivitySnapshot Snapshot => new(_locked, _remote, _fullscreen, _batterySaver);
    public static event Action<bool>? FullscreenChanged;

    public static string Describe() =>
        $"fullscreen={_fullscreen}({_fullscreenDevice ?? "-"}) locked={_locked} remote={_remote} batterySaver={_batterySaver} screensaver={_screensaver} highLoad={_highLoad}";

    /// <summary>Αν το rendering μιας συγκεκριμένης οθόνης πρέπει να είναι σταματημένο. Fullscreen σε άλλη οθόνη ΔΕΝ παύει αυτή.</summary>
    public static bool IsFullscreenOn(Screen screen)
    {
        // Explicit one-shot actions (manual tiling, newly opening a shell surface)
        // must not use a stale "not fullscreen" result while the sampler is off.
        if (_ui != null && _timer == null && IsForegroundWindowFullscreen(out var device))
            return device == null || string.Equals(device, screen.DeviceName, StringComparison.OrdinalIgnoreCase);
        return _fullscreen && (_fullscreenDevice == null ||
            string.Equals(_fullscreenDevice, screen.DeviceName, StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsPausedFor(Screen? screen, bool includeDesktopCoverage = true)
    {
        if (_locked || _remote || _batterySaver || _screensaver || _highLoad) return true;
        if (includeDesktopCoverage && screen != null && _covered.Contains(screen.DeviceName)) return true;
        if (!_fullscreen) return false;
        return _fullscreenDevice == null || screen == null || string.Equals(_fullscreenDevice, screen.DeviceName, StringComparison.OrdinalIgnoreCase);
    }

    public static void Start(Func<bool>? containerDemand = null, bool diagnostics = false)
    {
        if (_ui != null) return;
        _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        _containerDemand = containerDemand;
        _diagnostics = diagnostics;
        // The lock subscription is the minimal safety service, independent of polling.
        SystemEvents.SessionSwitch += OnSessionSwitch;
        RefreshDemand();
    }

    // All owners report activation/deactivation; coalesce changes from settings and
    // form lifecycle without invoking activity subscribers reentrantly.
    internal static void RequestRefresh()
    {
        var ui = _ui;
        if (ui == null || Interlocked.Exchange(ref _refreshQueued, 1) != 0) return;
        try
        {
            ui.Post(_ =>
            {
                Interlocked.Exchange(ref _refreshQueued, 0);
                if (ReferenceEquals(_ui, ui)) RefreshDemand();
            }, null);
        }
        catch (Exception ex) { Interlocked.Exchange(ref _refreshQueued, 0); System.Diagnostics.Debug.WriteLine(ex); }
    }

    private static void StopSampler()
    {
        _timer?.Stop(); _timer?.Dispose(); _timer = null;
        _fullscreen = _remote = _batterySaver = _screensaver = _highLoad = _detailed = false;
        _fullscreenDevice = null; _fullscreenHits = _loadHits = _slowTick = 0;
        _covered.Clear(); _previousCovered.Clear(); _coveredKey = "";
        if (_awake) SetThreadExecutionState(ES_CONTINUOUS);
        _awake = false;
    }

    private static void RefreshDemand()
    {
        if (_ui == null) return;
        var demand = CurrentDemand();
        var settings = AppSettings.Peek();
        _locked = _sessionLocked && settings.PauseOnLock;
        if (!demand.Any)
        {
            StopSampler();
            Publish();
            return;
        }
        if (_timer == null)
        {
            _timer = new System.Windows.Forms.Timer { Interval = 1000 };
            _timer.Tick += (_, _) => Poll();
            _timer.Start();
            Poll();
        }
        else
        {
            // Only a new detailed consumer needs a fresh slow sample right away.
            if (demand.Detailed && !_detailed) _slowTick = 0;
            if (!demand.Detailed) ClearDetailed();
            if (!demand.Coverage || !settings.PauseWhenDesktopCovered) ClearCoverage();
            Publish();
            UpdateKeepAwake(settings);
        }
    }

    // UI thread owns both timer and execution-state request.
    public static void Stop()
    {
        if (_ui == null) return;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        _ui = null; _containerDemand = null; _diagnostics = false;
        Interlocked.Exchange(ref _refreshQueued, 0);
        StopSampler();
        _sessionLocked = _locked = false;
        Publish();
    }

    private static void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        var ui = _ui;
        ui?.Post(_ =>
        {
            if (!ReferenceEquals(_ui, ui)) return;
            if (e.Reason == SessionSwitchReason.SessionLock) _sessionLocked = true;
            else if (e.Reason == SessionSwitchReason.SessionUnlock) _sessionLocked = false;
            else return;
            var settings = AppSettings.Peek();
            _locked = _sessionLocked && settings.PauseOnLock;
            Publish();
            UpdateKeepAwake(settings);
        }, null);
    }

    private static void ClearDetailed()
    {
        _remote = _batterySaver = _screensaver = _highLoad = _detailed = false;
        _loadHits = _slowTick = 0;
    }

    private static void ClearCoverage()
    {
        _covered.Clear(); _previousCovered.Clear(); _coveredKey = "";
    }

    private static void Poll()
    {
        try
        {
            var demand = CurrentDemand();
            var settings = AppSettings.Peek();
            _locked = _sessionLocked && settings.PauseOnLock;
            if (!demand.Any)
            {
                StopSampler(); Publish(); return;
            }
            bool fsNow = IsForegroundWindowFullscreen(out string? device);
            _fullscreenHits = fsNow ? Math.Min(2, _fullscreenHits + 1) : 0;
            bool fullscreen = _fullscreenHits >= 2 || (_fullscreen && fsNow);
            _fullscreenDevice = fullscreen ? device : null;

            // Dock, toolbar and automatic tiling only consume fullscreen state.
            // Avoid expensive sensor queries unless a detailed consumer exists.
            if (demand.Detailed)
            {
                if (!_detailed) _slowTick = 0;
                _detailed = true;
                if (_slowTick == 0)
                {
                    _remote = settings.PauseOnRemoteSession && GetSystemMetrics(SM_REMOTESESSION) != 0;
                    _batterySaver = settings.PauseOnBatterySaver && IsBatterySaverOn();
                    _screensaver = settings.PauseOnScreensaver && IsScreensaverRunning();
                    UpdateLoad(settings);
                }
                _slowTick = (_slowTick + 1) % 5;
            }
            else ClearDetailed();
            UpdateCovered(settings, demand.Coverage);
            _fullscreen = fullscreen;
            Publish();
            UpdateKeepAwake(settings);
        }
        catch (Exception ex) { RotatingLogger.WriteRecurring(RotatingLogger.RecurringSource.Activity, "AppActivity: " + ex.Message); }
    }

    private static string _lastCoveredKey = "";

    // 2.6.0: needs two equal samples (2 s) to START pausing a covered monitor and one sample to resume (the desktop shows again at once).
    private static void UpdateCovered(AppSettings settings, bool coverageDemand)
    {
        if (!coverageDemand || !settings.PauseWhenDesktopCovered ||
            _locked || _remote || _screensaver || _batterySaver || _highLoad)
        {
            ClearCoverage();
            return;
        }
        var now = DesktopCoverage.Compute();
        _covered.IntersectWith(now);
        foreach (var device in now)
            if (_previousCovered.Contains(device)) _covered.Add(device);
        _previousCovered = now;
        _coveredKey = string.Join("|", _covered.OrderBy(x => x));
    }

    private static bool _lastPublished;
    private static string? _lastDevice;
    private static bool _lastLocked, _lastRemote, _lastSaver, _lastScreensaver, _lastLoad;

    private static void Publish()
    {
        bool now = _fullscreen || _locked || _remote || _batterySaver || _screensaver || _highLoad;
        bool changed = now != _lastPublished || _fullscreenDevice != _lastDevice
                       || _locked != _lastLocked || _remote != _lastRemote || _batterySaver != _lastSaver
                       || _screensaver != _lastScreensaver || _highLoad != _lastLoad;
        var coveredChanged = _coveredKey != _lastCoveredKey;
        if (!changed && !coveredChanged) return;
        _lastCoveredKey = _coveredKey;
        _lastPublished = now; _lastDevice = _fullscreenDevice;
        _lastLocked = _locked; _lastRemote = _remote; _lastSaver = _batterySaver; _lastScreensaver = _screensaver; _lastLoad = _highLoad;
        IsFullscreenAppActive = now;
        FullscreenChanged?.Invoke(now);
    }

    // CPU / GPU / memory threshold with hysteresis: in after two checks (10 s) above the limit, out when 10 points below it.
    private static void UpdateLoad(AppSettings s)
    {
        if (s.PauseCpuAbove <= 0 && s.PauseGpuAbove <= 0 && s.PauseRamAbove <= 0) { _highLoad = false; _loadHits = 0; return; }
        var m = SystemMonitorService.Instance.GetSnapshot();
        double ram = m.TotalMemoryMb > 0 ? (m.TotalMemoryMb - m.AvailableMemoryMb) / m.TotalMemoryMb * 100 : 0;
        double gpu = s.PauseGpuAbove > 0 ? GpuMonitorService.Instance.GetSnapshot().LoadPercent ?? 0 : 0;
        int margin = _highLoad ? 10 : 0;
        bool over = (s.PauseCpuAbove > 0 && m.CpuPercent > s.PauseCpuAbove - margin)
                 || (s.PauseGpuAbove > 0 && gpu > s.PauseGpuAbove - margin)
                 || (s.PauseRamAbove > 0 && ram > s.PauseRamAbove - margin);
        if (over) { _loadHits = Math.Min(2, _loadHits + 1); if (_loadHits >= 2) _highLoad = true; }
        else { _loadHits = 0; _highLoad = false; }
    }

    // Keeps the PC and the display awake while the wallpaper is running and not paused. Called from the UI-thread timer only
    // (SetThreadExecutionState belongs to the calling thread).
    private static void UpdateKeepAwake(AppSettings s)
    {
        bool want = s.KeepPcAwake && !IsFullscreenAppActive && MotionDesk.Widgets.WallpaperHostEngine.IsRunningIfCreated;
        if (want == _awake) return;
        _awake = want;
        SetThreadExecutionState(want ? (ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED) : ES_CONTINUOUS);
    }

    private static bool IsScreensaverRunning() => SystemParametersInfo(SPI_GETSCREENSAVERRUNNING, 0, out int running, 0) && running != 0;

    private static bool IsBatterySaverOn()
    {
        return GetSystemPowerStatus(out var s) && (s.SystemStatusFlag & 1) != 0;
    }

    private static bool IsForegroundWindowFullscreen(out string? device)
    {
        device = null;
        // Exclusive D3D fullscreen / παρουσίαση: ο shell το ξέρει με ακρίβεια (το rect-test δεν πιάνει exclusive mode σε κάποιες περιπτώσεις).
        bool shellSaysFullscreen = SHQueryUserNotificationState(out int qun) == 0 && (qun == 3 || qun == 4);

        IntPtr hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return shellSaysFullscreen;
        if (!GetWindowRect(hwnd, out var rect)) return shellSaysFullscreen;

        var sb = new StringBuilder(256);
        GetClassName(hwnd, sb, sb.Capacity);
        string cls = sb.ToString();
        if (cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return false;

        var screen = Screen.FromRectangle(new Rectangle(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top));
        var b = screen.Bounds;
        bool covers = rect.Left <= b.Left && rect.Top <= b.Top && rect.Right >= b.Right && rect.Bottom >= b.Bottom;
        if (covers || shellSaysFullscreen) device = screen.DeviceName;
        return covers || shellSaysFullscreen;
    }

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS { public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag; public int BatteryLifeTime, BatteryFullLifeTime; }
    private const int SM_REMOTESESSION = 0x1000, SPI_GETSCREENSAVERRUNNING = 0x0072;
    private const uint ES_CONTINUOUS = 0x80000000, ES_SYSTEM_REQUIRED = 0x1, ES_DISPLAY_REQUIRED = 0x2;
    [DllImport("user32.dll")] private static extern bool SystemParametersInfo(int uiAction, int uiParam, out int pvParam, int fWinIni);
    [DllImport("kernel32.dll")] private static extern uint SetThreadExecutionState(uint esFlags);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int nIndex);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
    [DllImport("kernel32.dll")] private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS lpSystemPowerStatus);
    [DllImport("shell32.dll")] private static extern int SHQueryUserNotificationState(out int pquns);
}
