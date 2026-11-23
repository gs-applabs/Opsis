using System.Runtime.InteropServices;
using MotionDesk.Widgets;

namespace MotionDesk.Services;

internal static class AutoTileService
{
    private static System.Windows.Forms.Timer? _timer;
    private static readonly Dictionary<string, string> Previous = new(StringComparer.OrdinalIgnoreCase);

    internal static void RefreshSettings()
    {
        _timer?.Dispose();
        _timer = null;
        Previous.Clear();
        if (!AppSettings.Load().AutoTileEnabled || SafeModeService.IsSafeMode) return;
        _timer = new System.Windows.Forms.Timer { Interval = 1300 };
        _timer.Tick += (_, _) => Apply();
        _timer.Start();
    }

    internal static void Stop()
    {
        _timer?.Dispose();
        _timer = null;
        Previous.Clear();
    }

    internal static void Apply(bool force = false)
    {
        var settings = AppSettings.Peek();
        var exclusions = new HashSet<string>(settings.ZoneExcludedApps, StringComparer.OrdinalIgnoreCase);
        var monitors = Screen.AllScreens;
        var windows = WinApi.EnumerateAppWindows().Where(window =>
            !string.Equals(window.ExePath, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase) &&
            !exclusions.Contains(Path.GetFileNameWithoutExtension(window.ExePath)) &&
            !IsIconic(window.Handle) && !IsZoomed(window.Handle)).ToArray();
        foreach (var screen in monitors)
        {
            if (AppActivity.IsFullscreenOn(screen)) { Previous.Remove(screen.DeviceName); continue; }
            var area = DesktopToolbar.WorkAreaForTiling(screen);
            var selected = windows.Where(window => Screen.FromHandle(window.Handle).DeviceName == screen.DeviceName)
                .OrderBy(window => window.Handle.ToInt64()).Take(12).ToArray();
            var signature = settings.AutoTileMode + "|" + settings.AutoTileGap + "|" + area + "|" + string.Join(",", selected.Select(window => window.Handle));
            if (!force && Previous.TryGetValue(screen.DeviceName, out var old) && old == signature) continue;
            Previous[screen.DeviceName] = signature;
            var slots = AutoTileLayout.Arrange(area, selected.Length, settings.AutoTileMode, settings.AutoTileGap);
            for (int i = 0; i < slots.Length; i++)
            {
                var slot = slots[i];
                SetWindowPos(selected[i].Handle, IntPtr.Zero, slot.X, slot.Y, slot.Width, slot.Height, 0x0010 | 0x0004 | 0x0200 | 0x4000);
            }
        }
        foreach (var removed in Previous.Keys.Where(id => !monitors.Any(screen => screen.DeviceName == id)).ToArray())
            Previous.Remove(removed);
    }

    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int w, int h, uint flags);
}
