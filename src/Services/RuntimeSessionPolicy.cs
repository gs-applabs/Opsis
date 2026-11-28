using System.Threading;

namespace MotionDesk.Services;

// Session-only admission gate. This is not a second settings store or a
// scheduler. Each existing service remains responsible for its lifecycle.
[Flags]
internal enum SessionBackgroundService
{
    None = 0,
    DockRestore = 1,
    UsageAlerts = 2,
    DiscordPresence = 4,
    ScheduledAutomations = 8,
    PeriodicWallpaperAccent = 16,
    AnimatedWallpaper = 32,
    DeskStrip = 64,
    Widgets = 128,
    MusicVisualizer = 256,
    AppearanceAutomatic = 512,
    AutoTile = 1024,
    AdaptiveSampling = 2048,
    ReplacementTaskbar = 4096,
    ZoneSnap = 8192,
    HotCorners = 16384,
    WindowRules = 32768,
    CursorEffects = 65536,
    DesktopHotkeys = 131072,
    DesktopToolbar = 262144,
    VirtualWorkspaceWallpaper = 524288,
    Background = DockRestore | UsageAlerts | DiscordPresence | ScheduledAutomations | PeriodicWallpaperAccent,
    All = Background | AnimatedWallpaper | DeskStrip | Widgets | MusicVisualizer | AppearanceAutomatic | AutoTile | AdaptiveSampling | ReplacementTaskbar | ZoneSnap | HotCorners | WindowRules | CursorEffects | DesktopHotkeys | DesktopToolbar | VirtualWorkspaceWallpaper
}

internal static class RuntimeSessionPolicy
{
    private static int _allowed = (int)SessionBackgroundService.All;
    internal static SessionBackgroundService Allowed => (SessionBackgroundService)Volatile.Read(ref _allowed);

    // Reject undefined/zero service requests rather than accidentally
    // granting a capability that has no registered owner.
    internal static bool Allows(SessionBackgroundService allowed, SessionBackgroundService service, bool savedEnabled, bool safeMode)
        => savedEnabled && !safeMode && service != SessionBackgroundService.None &&
           (service & ~SessionBackgroundService.All) == 0 && (allowed & service) == service;

    internal static bool Allows(SessionBackgroundService service, bool savedEnabled, bool safeMode)
        => Allows(Allowed, service, savedEnabled, safeMode);

    internal static bool IsPermitted(SessionBackgroundService service) =>
        Allows(Allowed, service, true, SafeModeService.IsSafeMode);

    internal static SessionBackgroundService ReplaceBackground(SessionBackgroundService current, bool allow)
        => allow ? current | SessionBackgroundService.Background : current & ~SessionBackgroundService.Background;

    internal static void SetAllowed(SessionBackgroundService mask)
        => Volatile.Write(ref _allowed, (int)(mask & SessionBackgroundService.All));

    // The only writer is the existing tray UI thread.
    internal static void SetAllowed(SessionBackgroundService service, bool allowed)
        => SetAllowed(allowed ? Allowed | service : Allowed & ~service);
}
