// When Opsis runs with an isolated data folder (OPSIS_APPDATA: tests, the UI click audit, CI) the Appearance engines must NEVER touch the real Windows of the
// person running it (theme, accent, taskbar, file extensions, wallpaper, restore points). These stand-ins keep everything in memory.
using Microsoft.Win32;

namespace MotionDesk.Appearance;

internal sealed class MemoryRegistry : IRegistryStore
{
    private readonly Dictionary<(string, string), object> _d = new();
    public object? Get(string subKey, string name) => _d.TryGetValue((subKey, name), out var v) ? v : null;
    public void Set(string subKey, string name, object value, RegistryValueKind kind) => _d[(subKey, name)] = value;
    public void Delete(string subKey, string name) => _d.Remove((subKey, name));
}

internal sealed class NullDwm : IDwmWindows
{
    public IReadOnlyList<IntPtr> TopLevelWindows() => Array.Empty<IntPtr>();
    public bool IsEligible(IntPtr hwnd) => false;
    public bool IsAlive(IntPtr hwnd) => false;
    public void SetCorner(IntPtr hwnd, int preference) { }
    public void SetBorderColor(IntPtr hwnd, int colorRef) { }
    public void SetDarkTitle(IntPtr hwnd, bool dark) { }
    public void SetBackdrop(IntPtr hwnd, int type) { }
    public IntPtr Foreground => IntPtr.Zero;
}

internal sealed class NullTaskbarControl : ITaskbarControl
{
    private bool _auto;
    public bool IsAutoHide => _auto;
    public void SetAutoHide(bool on) => _auto = on;
}

internal sealed class MemoryWallpaper : IWallpaperApi
{
    private (string? Path, WallpaperStyle Style) _cur = (null, WallpaperStyle.Fill);
    public (string? Path, WallpaperStyle Style) Current() => _cur;
    public void Set(string path, WallpaperStyle style) => _cur = (path, style);
}

internal sealed class NullTaskbarSurface : ITaskbarSurface
{
    public IReadOnlyList<IntPtr> Taskbars() => Array.Empty<IntPtr>();
    public bool IsAlive(IntPtr taskbar) => false;
    public void SetAccent(IntPtr taskbar, int accentState, int gradientColorAbgr) { }
    public bool AnyMaximizedWindow() => false;
    public bool StartOrSearchActive() => false;
    public ScreenRect GetRect(IntPtr taskbar) => new(0, 0, 0, 0);
    public void SetRegion(IntPtr taskbar, ScreenRect? regionInWindowCoords, int radius) { }
}

internal sealed class NullRestorePoints : IRestorePointService
{
    public bool IsAdministrator => false;
    public RestorePointResult Create(string description) => new(RestorePointStatus.Skipped, "isolated run: no restore point");
}
