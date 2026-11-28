using System.Runtime.InteropServices;
using MotionDesk.Widgets;

namespace MotionDesk.Services;

internal static class AutoTileService
{
    private static System.Windows.Forms.Timer? _timer;
    internal static bool ActivityDemand => _timer != null;
    private static readonly Dictionary<string, string> Previous = new(StringComparer.OrdinalIgnoreCase);
    private static Dictionary<IntPtr, Rectangle>? _undoManual;

    internal sealed record TilePreview(string Title, IntPtr Window, Rectangle Before, Rectangle After, string Monitor);

    private static bool IsTilingCandidate(IntPtr hwnd)
    {
        if (!IsWindow(hwnd) || IsIconic(hwnd) || IsZoomed(hwnd)) return false;
        long style = GetWindowLongPtr(hwnd, -16).ToInt64();
        long extended = GetWindowLongPtr(hwnd, -20).ToInt64();
        // Dialogs (including resizable dialogs), owned windows, tool palettes and
        // temporary popups must never be tiled with their parent application.
        if (GetWindow(hwnd, 4) != IntPtr.Zero) return false; // GW_OWNER
        var cls = new System.Text.StringBuilder(128);
        if (GetClassName(hwnd, cls, cls.Capacity) > 0 &&
            (cls.ToString().Equals("#32770", StringComparison.OrdinalIgnoreCase) ||
             cls.ToString().Equals("tooltips_class32", StringComparison.OrdinalIgnoreCase))) return false;
        const long WS_THICKFRAME = 0x00040000L, WS_CHILD = 0x40000000L, WS_POPUP = 0x80000000L;
        const long WS_EX_TOOLWINDOW = 0x00000080L, WS_EX_DLGMODALFRAME = 0x00000001L;
        const long WS_EX_NOACTIVATE = 0x08000000L;
        return (style & WS_THICKFRAME) != 0 && (style & WS_CHILD) == 0 &&
            (extended & (WS_EX_TOOLWINDOW | WS_EX_DLGMODALFRAME | WS_EX_NOACTIVATE)) == 0 &&
            ((style & WS_POPUP) == 0 || (style & 0x00C00000L) != 0); // allow only captioned popups
    }

    private static Rectangle ResolveRuleRect(WinApi.AppWindow window, Screen screen, Rectangle fallback, AppSettings settings)
    {
        var program = Path.GetFileNameWithoutExtension(window.ExePath);
        foreach (var rule in settings.ZoneRules ?? new())
        {
            if (rule == null || rule.ZoneIndex<0 || rule.ZoneIndex>47 ||
                !program.Equals(Path.GetFileNameWithoutExtension(rule.Process),StringComparison.OrdinalIgnoreCase)) continue;
            if (!string.IsNullOrWhiteSpace(rule.Extension))
            {
                var extension=rule.Extension.StartsWith('.') ? rule.Extension : "."+rule.Extension;
                // Explicit extension is matched as a token in the window title. Unknown document type
                // is NOT guessed from the process name (notepad can open anything).
                if (!System.Text.RegularExpressions.Regex.IsMatch(window.Title,
                    @"(?<![a-zA-Z0-9])"+System.Text.RegularExpressions.Regex.Escape(extension)+@"(?=\b|[^a-zA-Z0-9]|$)",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase,System.TimeSpan.FromMilliseconds(50))) continue;
            }
            var layout=ZoneLayoutStore.GetLayout(screen.DeviceName);
            if (rule.ZoneIndex>=layout.Zones.Count) continue;
            var area=DesktopToolbar.WorkAreaForTiling(screen);
            var rect=layout.Zones[rule.ZoneIndex].ToAbsolute(area);
            if(rect.Width>=120 && rect.Height>=90) return rect;
        }
        return fallback;
    }

    // #44: one-shot temporary span; automatic tile enforcement is suspended while
    // a span is active, then the user can restore the original rectangle explicitly.
    private static readonly Dictionary<IntPtr, Rectangle> SpanUndo = new();
    private static bool RestoreAutoTileAfterSpans;

    internal static bool TrySpanWindow(IntPtr window, Screen monitor, ZoneLayoutData layout, int a, int b)
    {
        if (!IsTilingCandidate(window) || a < 0 || b < 0 || a >= layout.Zones.Count || b >= layout.Zones.Count ||
            !ZoneLayoutStore.TryJoinAdjacent(layout.Zones[a], layout.Zones[b], out var merged) ||
            Screen.FromHandle(window).DeviceName != monitor.DeviceName || !GetWindowRect(window, out var before)) return false;
        var bounds = merged.ToAbsolute(DesktopToolbar.WorkAreaForTiling(monitor));
        if (bounds.Width < 120 || bounds.Height < 90) return false;
        // Keep the FIRST position for Undo, even if the user changes the joined zones.
        bool firstSpan = SpanUndo.Count == 0;
        var s = AppSettings.Load();
        if (firstSpan) RestoreAutoTileAfterSpans = s.AutoTileEnabled;
        if (s.AutoTileEnabled) { s.AutoTileEnabled = false; s.Save(); RefreshSettings(); }
        bool moved = SetWindowPos(window, IntPtr.Zero,bounds.X,bounds.Y,bounds.Width,bounds.Height,
            0x0010 | 0x0004 | 0x0200 | 0x4000);
        if (moved)
            SpanUndo.TryAdd(window, Rectangle.FromLTRB(before.Left,before.Top,before.Right,before.Bottom));
        else if (firstSpan) ResumeAutomaticTilingAfterSpans();
        return moved;
    }

    internal static bool RestoreSpan(IntPtr hwnd)
    {
        if (!SpanUndo.Remove(hwnd, out var before)) return false;
        bool restored = IsWindow(hwnd) && SetWindowPos(hwnd,IntPtr.Zero,before.X,before.Y,before.Width,before.Height,
            0x0010 | 0x0004 | 0x0200 | 0x4000);
        if (SpanUndo.Count == 0) ResumeAutomaticTilingAfterSpans();
        return restored;
    }

    private static void ResumeAutomaticTilingAfterSpans()
    {
        if (!RestoreAutoTileAfterSpans) return;
        RestoreAutoTileAfterSpans = false;
        var settings = AppSettings.Load();
        settings.AutoTileEnabled = true;
        settings.Save();
        RefreshSettings();
    }

    internal static IReadOnlyList<TilePreview> Preview()
    {
        var settings = AppSettings.Peek();
        var exclusions = new HashSet<string>(settings.ZoneExcludedApps, StringComparer.OrdinalIgnoreCase);
        var windows = WinApi.EnumerateAppWindows().Where(window =>
            !string.Equals(window.ExePath, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase) &&
            !exclusions.Contains(Path.GetFileNameWithoutExtension(window.ExePath)) &&
            IsTilingCandidate(window.Handle)).ToArray();
        var result = new List<TilePreview>();
        foreach (var screen in Screen.AllScreens)
        {
            if (AppActivity.IsFullscreenOn(screen)) continue;
            var area = DesktopToolbar.WorkAreaForTiling(screen);
            var selected = windows.Where(window => Screen.FromHandle(window.Handle).DeviceName == screen.DeviceName)
                .OrderBy(window => window.Handle.ToInt64()).Take(12).ToArray();
            var slots = AutoTileLayout.Arrange(area, selected.Length, settings.AutoTileMode, settings.AutoTileGap);
            for (int i = 0; i < slots.Length; i++)
            {
                if (!GetWindowRect(selected[i].Handle, out var original)) continue;
                result.Add(new TilePreview(selected[i].Title, selected[i].Handle,
                    Rectangle.FromLTRB(original.Left, original.Top, original.Right, original.Bottom),
                    ResolveRuleRect(selected[i],screen,slots[i],settings), screen.DeviceName));
            }
        }
        return result;
    }

    /// <summary>
    /// Apply the EXACT positions that the user approved in Preview(). Never enumerate
    /// windows again or substitute a different tiling plan after the confirmation.
    /// A window closed, moved to a different display, changed size or turned into a
    /// dialog meanwhile is skipped. Only successfully moved windows enter Undo.
    /// </summary>
    internal static int ApplyApprovedPreview(IReadOnlyList<TilePreview> approved)
    {
        if (approved == null || approved.Count == 0) return 0;
        var screens = Screen.AllScreens.ToDictionary(x => x.DeviceName, StringComparer.OrdinalIgnoreCase);
        var restore = new Dictionary<IntPtr, Rectangle>();
        foreach (var tile in approved.Take(48))
        {
            if (!screens.TryGetValue(tile.Monitor, out var screen) ||
                !IsTilingCandidate(tile.Window) ||
                Screen.FromHandle(tile.Window).DeviceName != screen.DeviceName ||
                !GetWindowRect(tile.Window, out var current)) continue;
            var before = Rectangle.FromLTRB(current.Left, current.Top, current.Right, current.Bottom);
            if (before != tile.Before || tile.After.Width < 120 || tile.After.Height < 90) continue;
            var work = DesktopToolbar.WorkAreaForTiling(screen);
            if (!work.IntersectsWith(tile.After) ||
                tile.After.Left < work.Left || tile.After.Top < work.Top ||
                tile.After.Right > work.Right || tile.After.Bottom > work.Bottom) continue;
            if (SetWindowPos(tile.Window, IntPtr.Zero, tile.After.X, tile.After.Y,
                    tile.After.Width, tile.After.Height, 0x0010 | 0x0004 | 0x0200 | 0x4000))
                restore.TryAdd(tile.Window, before);
        }
        if (restore.Count > 0) { _undoManual = restore; Previous.Clear(); }
        return restore.Count;
    }

    /// <summary>Restore the original rectangles of the last manual tile action.</summary>
    internal static bool UndoLastManual()
    {
        if (_undoManual is not { Count: > 0 } undo) return false;
        _undoManual = null;
        // Disable the automatic 1.3-second enforcement: otherwise the undo is immediately undone.
        var settings = AppSettings.Load();
        if (settings.AutoTileEnabled)
        {
            settings.AutoTileEnabled = false; settings.Save(); RefreshSettings();
        }
        bool restored = false;
        foreach (var (hwnd, rectangle) in undo)
            if (IsWindow(hwnd) && rectangle.Width >= 1 && rectangle.Height >= 1)
                restored |= SetWindowPos(hwnd, IntPtr.Zero, rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height,
                    0x0010 | 0x0004 | 0x0200 | 0x4000);
        Previous.Clear();
        return restored;
    }

    internal static void RefreshSettings()
    {
        _timer?.Dispose();
        _timer = null;
        Previous.Clear();
        if (!AppSettings.Load().AutoTileEnabled || SafeModeService.IsSafeMode || WindowsUpdateGuard.RequiresReview) { AppActivity.RequestRefresh(); return; }
        _timer = new System.Windows.Forms.Timer { Interval = 1300 };
        _timer.Tick += (_, _) => Apply();
        _timer.Start();
        AppActivity.RequestRefresh();
    }

    internal static void Stop()
    {
        _timer?.Dispose();
        _timer = null;
        Previous.Clear();
        AppActivity.RequestRefresh();
    }

    internal static void Apply(bool force = false)
    {
        if (WindowsUpdateGuard.RuntimeHold || (force && WindowsUpdateGuard.RequiresReview)) return;
        if (force)
        {
            // One undo stack per explicit user action; automatic tiling is never stacked.
            _undoManual = Preview().GroupBy(x => x.Window).ToDictionary(g => g.Key, g => g.First().Before);
        }
        var settings = AppSettings.Peek();
        var exclusions = new HashSet<string>(settings.ZoneExcludedApps, StringComparer.OrdinalIgnoreCase);
        var monitors = Screen.AllScreens;
        var windows = WinApi.EnumerateAppWindows().Where(window =>
            !string.Equals(window.ExePath, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase) &&
            !exclusions.Contains(Path.GetFileNameWithoutExtension(window.ExePath)) &&
            IsTilingCandidate(window.Handle)).ToArray();
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
                var slot = ResolveRuleRect(selected[i],screen,slots[i],settings);
                SetWindowPos(selected[i].Handle, IntPtr.Zero, slot.X, slot.Y, slot.Width, slot.Height, 0x0010 | 0x0004 | 0x0200 | 0x4000);
            }
        }
        foreach (var removed in Previous.Keys.Where(id => !monitors.Any(screen => screen.DeviceName == id)).ToArray())
            Previous.Remove(removed);
    }

    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr GetWindow(IntPtr hwnd, uint command);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder name, int maxCount);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT bounds);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int w, int h, uint flags);
}
