// Lifecycle + orchestration of the Windows-appearance engines inside Opsis 2.6.0.
// Low-end friendly: no thread; a 60 s timer only while an automatic theme or the slideshow is on; the window hook only while a window effect is on.
using System.Windows.Forms;

namespace MotionDesk.Appearance;

public sealed class AppearanceService
{
    private static readonly Lazy<AppearanceService> _instance = new(() => new AppearanceService());
    public static AppearanceService Instance => _instance.Value;

    private readonly AppearanceStore _store = new();
    private readonly AppearanceEngine _engine;
    private AppearanceSettings _settings = new();
    private System.Windows.Forms.Timer? _timer, _slideTimer, _barTimer;
    private readonly WindowEffectsEngine _effects;
    private ThemeLibrary _themes = new(ThemeLibrary.DefaultRoot);
    private readonly ThemeApplier _applier;
    private WinEventWatcher? _watcher;
    private readonly IWallpaperApi _wallpaperApi;
    private readonly TaskbarLookEngine _taskbarLook;
    private readonly TaskbarShapeEngine _taskbarShape;
    private readonly IRestorePointService _restorePoints;
    private readonly BackupVault _vault;
    private bool _started;
    private readonly bool _noSystem;
    private readonly InstanceGuard _guard = new();

    /// <summary>False when an old stand-alone Opsis Appearance is still running and owns the engine: then nothing is applied from here (read-only page).</summary>
    public bool EngineActive => _guard.IsOwner;

    /// <summary>Raised on the UI thread after the engine changed something the page shows (theme flipped, slideshow advanced, theme applied).</summary>
    public event Action? Changed;

    /// <summary>True when Opsis runs with an isolated data folder (tests, UI audit, CI): then NOTHING of the real Windows is read or changed.</summary>
    public static bool IsolatedRun => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPSIS_APPDATA"));

    public AppearanceService() : this(IsolatedRun ? new MemoryRegistry() : new CurrentUserRegistry(), IsolatedRun ? new NullDwm() : null, IsolatedRun ? new NullTaskbarControl() : null,
        IsolatedRun ? new MemoryWallpaper() : null, null, IsolatedRun ? new NullTaskbarSurface() : null, IsolatedRun ? new NullRestorePoints() : null, null, noSystem: IsolatedRun) { }

    internal AppearanceService(IRegistryStore registry, IDwmWindows? dwm = null, ITaskbarControl? taskbar = null, IWallpaperApi? wallpaper = null, string? themesRoot = null,
        ITaskbarSurface? surface = null, IRestorePointService? restorePoints = null, string? vaultRoot = null, bool noSystem = false)
    {
        _noSystem = noSystem;
        var tbSurface = surface ?? new RealTaskbarSurface();
        _taskbarLook = new TaskbarLookEngine(tbSurface);
        _taskbarShape = new TaskbarShapeEngine(tbSurface);
        _restorePoints = restorePoints ?? new RealRestorePointService();
        _vault = new BackupVault(vaultRoot ?? BackupVault.DefaultRoot);
        if (themesRoot != null) _themes = new ThemeLibrary(themesRoot);
        _wallpaperApi = wallpaper ?? new RealWallpaper();
        _engine = new AppearanceEngine(registry, noSystem ? () => { } : null);
        _effects = new WindowEffectsEngine(dwm ?? new RealDwmWindows(() => SystemInformation.HighContrast), taskbar ?? new RealTaskbar(), () => _engine.IsDark);
        _applier = new ThemeApplier(_engine, fx => { _settings.Effects = fx; ApplyEffects(); }, _wallpaperApi, _themes);
    }

    public AppearanceEngine Engine => _engine;
    public WindowEffectsEngine Effects => _effects;
    public ThemeLibrary Themes => _themes;
    public ThemeApplier Applier => _applier;
    public AppearanceStore Store => _store;
    public AppearanceSettings Settings => _settings;
    public BackupVault Vault => _vault;
    public bool IsAdministrator => _restorePoints.IsAdministrator;
    public bool Started => _started;

    private void RaiseChanged() { try { Changed?.Invoke(); } catch (Exception) { } }

    /// <summary>Called once when Opsis starts. Applies what was already on (it does NOT ask for a new restore point on every start).</summary>
    public void Start()
    {
        if (_started) return;
        _started = true;
        _settings = _store.Load();
        if (!_guard.TryAcquire()) return;   // the old separate app applies the effects; this one only shows the settings
        ApplySchedule();
        UpdateTimer();
        UpdateSlideTimer();
        ApplyEffects();
        ApplyTaskbar(askForRestorePoint: false);
    }

    /// <summary>Called when Opsis exits: the taskbar and the window effects ALWAYS go back to Windows.</summary>
    public void Stop()
    {
        _started = false;
        _guard.Release();
        _barTimer?.Stop(); _barTimer?.Dispose(); _barTimer = null;
        try { _taskbarLook.RestoreAll(); _taskbarShape.RestoreAll(); } catch (Exception) { }
        _timer?.Stop(); _timer?.Dispose(); _timer = null;
        _slideTimer?.Stop(); _slideTimer?.Dispose(); _slideTimer = null;
        _watcher?.Dispose(); _watcher = null;
        try { _effects.RestoreAll(); } catch (Exception) { }
    }

    public void Pause() { _timer?.Stop(); _slideTimer?.Stop(); _barTimer?.Stop(); }
    public void Resume() { UpdateTimer(); UpdateSlideTimer(); UpdateBarTimer(); ApplySchedule(); }

    /// <summary>Re-reads the settings file (for example after another change) and re-applies the schedule.</summary>
    public void Reload() { _settings = _store.Load(); ApplySchedule(); UpdateTimer(); UpdateSlideTimer(); RaiseChanged(); }

    /// <summary>Applies a full theme (the user's original values are saved first) and keeps the effects it carries.</summary>
    public ThemeApplier.Report ApplyTheme(OpsisTheme theme)
    {
        Backup();
        var report = _applier.Apply(theme);
        if (theme.Effects != null) _store.Save(_settings);
        RaiseChanged();
        return report;
    }

    /// <summary>The window hook lives ONLY while a window effect is on (no polling).</summary>
    public void ApplyEffects()
    {
        if (_started && !EngineActive) return;
        try
        {
            _effects.AccentRgb = _engine.ReadAccent() ?? 0x0078D4;
            _effects.Apply(_settings.Effects);
            bool need = _settings.Effects.AnyWindowEffect;
            if (need && _watcher == null) _watcher = new WinEventWatcher(h => _effects.OnForegroundChanged(h), h => _effects.ApplyToWindow(h));
            else if (!need && _watcher != null) { _watcher.Dispose(); _watcher = null; }
        }
        catch (Exception) { /* an effect that fails must never take Opsis down */ }
    }

    /// <summary>Saves the user's original values once, before anything is changed.</summary>
    public void Backup()
    {
        if (!_store.HasBackup) _store.SaveBackupOnce(_engine.Snapshot());
    }

    /// <summary>Puts one category back to its defaults: 0 theme/colour, 1 taskbar, 2 windows, 4 wallpaper/slideshow.</summary>
    public void ResetCategory(int category)
    {
        var d = new AppearanceSettings();
        switch (category)
        {
            case 0: _settings.Mode = d.Mode; _settings.Latitude = d.Latitude; _settings.Longitude = d.Longitude; _settings.LightAt = d.LightAt; _settings.DarkAt = d.DarkAt; _settings.SunOffsetMinutes = d.SunOffsetMinutes; break;
            case 1: _settings.TaskbarLook = d.TaskbarLook; _settings.TaskbarShape = d.TaskbarShape; break;
            case 2: _settings.Effects = d.Effects; break;
            case 4: _settings.Slideshow = d.Slideshow; break;
            default: return;
        }
        SaveSettings();
        try { if (category == 2) ApplyEffects(); if (category == 1) ApplyTaskbar(askForRestorePoint: false); } catch (Exception) { }
    }

    public void SaveSettings() { _store.Save(_settings); UpdateTimer(); UpdateSlideTimer(); ApplySchedule(); }

    // ---- taskbar look / shape + safety ---------------------------------------------------------------------------

    /// <summary>The FIRST time something invasive is switched on a system restore point is requested (needs administrator, at most once a day, never blocks).</summary>
    public RestorePointResult? ApplyTaskbar(bool askForRestorePoint)
    {
        RestorePointResult? rp = null;
        try
        {
            bool wantsAny = _settings.TaskbarLook.Enabled || _settings.TaskbarShape.Enabled;
            if (wantsAny && askForRestorePoint)
            {
                rp = InvasiveGuard.BeforeInvasiveChange(_settings.Safety, _restorePoints, "Opsis: taskbar look", DateTime.UtcNow);
                _store.Save(_settings);
            }
            _taskbarLook.Apply(_settings.TaskbarLook);
            _taskbarShape.Apply(_settings.TaskbarShape);
        }
        catch (Exception) { /* a taskbar that does not answer must not take Opsis down */ }
        UpdateBarTimer();
        return rp;
    }

    private void UpdateBarTimer()
    {
        bool need = _taskbarLook.NeedsTimer || _settings.TaskbarShape.Enabled || _settings.TaskbarLook.Enabled;
        if (!need) { _barTimer?.Stop(); return; }
        if (_barTimer == null)
        {
            _barTimer = new System.Windows.Forms.Timer { Interval = 800 };
            _barTimer.Tick += (_, _) => { try { _taskbarLook.Tick(); _taskbarShape.Tick(); } catch (Exception) { } };
        }
        _barTimer.Start();
    }

    public RestorePointResult CreateRestorePointNow()
    {
        var r = InvasiveGuard.BeforeInvasiveChange(_settings.Safety, _restorePoints, "Opsis: manual restore point", DateTime.UtcNow, force: true)!;
        _store.Save(_settings);
        return r;
    }

    // ---- wallpaper slideshow -------------------------------------------------------------------------------------

    private void UpdateSlideTimer()
    {
        if (!_settings.Slideshow.Enabled) { _slideTimer?.Stop(); return; }
        if (_slideTimer == null)
        {
            _slideTimer = new System.Windows.Forms.Timer { Interval = 60_000 };      // one cheap check a minute, only while the slideshow is on
            _slideTimer.Tick += (_, _) => SlideshowTick(DateTime.UtcNow, force: false);
        }
        _slideTimer.Start();
        SlideshowTick(DateTime.UtcNow, force: false);
    }

    /// <summary>Changes the wallpaper when the interval passed (always with <paramref name="force"/>). True when it changed.</summary>
    public bool SlideshowTick(DateTime utcNow, bool force)
    {
        var s = _settings.Slideshow;
        try
        {
            if (!force && !WallpaperSlideshow.Due(s, utcNow)) return false;
            var images = WallpaperSlideshow.Images(s.Folder);
            var next = WallpaperSlideshow.Pick(images, s.LastFile, s.Shuffle);
            if (next == null) return false;
            if (s.OriginalPath == null)
            {
                var cur = _wallpaperApi.Current();
                s.OriginalPath = cur.Path ?? ""; s.OriginalStyle = cur.Style;
            }
            _wallpaperApi.Set(next, s.Style);
            s.LastFile = next; s.LastChangeUtc = utcNow;
            _store.Save(_settings);
            RaiseChanged();
            return true;
        }
        catch (Exception) { return false; }     // a missing or locked picture must not stop anything; the next tick tries again
    }

    /// <summary>Stops the slideshow and puts the wallpaper back that was there before it started.</summary>
    public void StopSlideshow(bool restoreWallpaper)
    {
        var s = _settings.Slideshow;
        s.Enabled = false;
        try { if (restoreWallpaper && !string.IsNullOrEmpty(s.OriginalPath) && File.Exists(s.OriginalPath)) _wallpaperApi.Set(s.OriginalPath, s.OriginalStyle); } catch (Exception) { }
        s.OriginalPath = null;
        SaveSettings();
    }

    /// <summary>"Restore my original settings": taskbar look/shape and window effects go back to Windows and stay OFF; saved original values are written back.</summary>
    public void RestoreOriginal()
    {
        try
        {
            _settings.TaskbarLook = new TaskbarLookSettings(); _settings.TaskbarShape = new TaskbarShapeSettings();
            _taskbarLook.RestoreAll(); _taskbarShape.RestoreAll();
            _store.Save(_settings); UpdateBarTimer();
        }
        catch (Exception) { }
        var backup = _store.LoadBackup();
        if (backup != null) _engine.Restore(backup);   // Restore skips values it cannot write instead of throwing
    }

    public void ApplySchedule()
    {
        if (_settings.Mode == ThemeMode.Manual) return;
        try
        {
            bool wantDark = AppearanceEngine.ShouldBeDark(_settings, DateTime.Now);
            if (wantDark == _engine.IsDark) return;
            Backup();
            _engine.SetDark(wantDark);
            if (!_noSystem) Broadcaster.SettingsChanged();
            RaiseChanged();
        }
        catch (Exception)
        {
            // A locked-down PC (policy, no registry write access) must never make Opsis fail: the page shows the current state and the manual buttons report the error.
        }
    }

    private void UpdateTimer()
    {
        bool need = _settings.Mode != ThemeMode.Manual;
        if (!need) { _timer?.Stop(); return; }
        if (_timer == null)
        {
            _timer = new System.Windows.Forms.Timer { Interval = 60_000 };
            _timer.Tick += (_, _) => ApplySchedule();
        }
        _timer.Start();
    }
}
