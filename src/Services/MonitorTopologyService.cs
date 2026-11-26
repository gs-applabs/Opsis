using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;
using MotionDesk.Widgets;

namespace MotionDesk.Services;

/// <summary>Docking-aware display identity and user-approved cross-PC monitor mapping.</summary>
internal static class MonitorTopologyService
{
    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
    private struct DISPLAY_DEVICE
    {
        public uint cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=128)] public string DeviceString;
        public uint StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=128)] public string DeviceKey;
    }
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern bool EnumDisplayDevices(string? lpDevice, uint deviceNum, ref DISPLAY_DEVICE device, uint flags);

    internal sealed record Display(string Id, string DeviceName, Rectangle Bounds, Rectangle WorkingArea, int Dpi, bool Primary);
    internal sealed record Layout(string Fingerprint, DateTime TimestampUtc, List<Display> Screens);
    internal static IReadOnlyList<Display> Current()
    {
        var screens = new List<Display>();
        foreach (var s in Screen.AllScreens)
        {
            var stable = s.DeviceName;
            try
            {
                var device = new DISPLAY_DEVICE { cb=(uint)Marshal.SizeOf<DISPLAY_DEVICE>() };
                if (EnumDisplayDevices(s.DeviceName, 0, ref device, 0) && !string.IsNullOrWhiteSpace(device.DeviceID))
                    stable = device.DeviceID.Trim(); // PnP identity survives most DISPLAY1 renumberings
            }
            catch { }
            screens.Add(new Display(stable, s.DeviceName, s.Bounds, s.WorkingArea, DpiOf(s.Bounds), s.Primary));
        }
        return screens;
    }
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(Point point, uint flags);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);

    /// <summary>The effective DPI of THAT monitor (mixed-DPI desktops have a different value per screen); 96 when Windows cannot tell.</summary>
    private static int DpiOf(Rectangle bounds)
    {
        try
        {
            var centre = new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
            var monitor = MonitorFromPoint(centre, 2 /* MONITOR_DEFAULTTONEAREST */);
            if (monitor != IntPtr.Zero && GetDpiForMonitor(monitor, 0 /* MDT_EFFECTIVE_DPI */, out var x, out _) == 0 && x is >= 48 and <= 960) return (int)x;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException) { }
        return 96;
    }

    internal static Display? ForDevice(string? device) => Current().FirstOrDefault(s => s.DeviceName == device);
    internal static Display? Match(string? id, string? device = null, int width = 0, int height = 0)
    {
        var current = Current();
        var found = current.FirstOrDefault(s => !string.IsNullOrEmpty(id) && s.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (found != null) return found;
        found = current.FirstOrDefault(s => !string.IsNullOrEmpty(device) && s.DeviceName.Equals(device, StringComparison.OrdinalIgnoreCase));
        if (found != null) return found;
        return current.OrderBy(s => Math.Abs(s.Bounds.Width - width) + Math.Abs(s.Bounds.Height - height))
            .ThenBy(s => s.Primary ? 0 : 1).FirstOrDefault();
    }
    internal static string Fingerprint(IReadOnlyList<Display>? items = null)
    {
        items ??= Current();
        return string.Join("|", items.OrderBy(m => m.Id, StringComparer.OrdinalIgnoreCase)
            .Select(m => $"{m.Id}:{m.Bounds.Width}x{m.Bounds.Height}:{m.Bounds.X},{m.Bounds.Y}:{m.Dpi}"));
    }

    private static string HistoryFile => Path.Combine(AppPaths.AppDataFolder, "monitor-topologies.json");
    private static readonly object Gate = new();
    /// <summary>Writes only when display topology changes, not during frames.</summary>
    internal static void Remember()
    {
        lock (Gate)
        {
            var current=Current(); if (current.Count==0) return;
            var key=Fingerprint(current);
            try
            {
                var layouts=File.Exists(HistoryFile) ? JsonSerializer.Deserialize<List<Layout>>(AtomicFile.ReadAllText(HistoryFile)) ?? new() : new List<Layout>();
                if (layouts.Any(a => a.Fingerprint == key)) return;
                layouts.Insert(0,new Layout(key,DateTime.UtcNow,current.ToList()));
                MotionDesk.Services.AtomicFile.WriteAllText(HistoryFile,JsonSerializer.Serialize(layouts.Take(12).ToArray()));
            }
            catch(Exception e) when(e is IOException or UnauthorizedAccessException or JsonException) { }
        }
    }
    internal static IReadOnlyList<Layout> Known()
    {
        try { return JsonSerializer.Deserialize<List<Layout>>(AtomicFile.ReadAllText(HistoryFile)) ?? new(); }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or JsonException) { return []; }
    }
}

/// <summary>Optional, conservative window return after a physically disconnected monitor reconnects.</summary>
internal sealed class DockWindowRestorer : IDisposable
{
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd,out RECT rect);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int width,int height,uint flags);
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left,Top,Right,Bottom; }
    private sealed record Placement(IntPtr Window, string MonitorId, Rectangle Rect);
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 12000 };
    private Dictionary<IntPtr,Placement> remembered = new();
    private HashSet<string> missing = new(StringComparer.OrdinalIgnoreCase);
    private string topology = string.Empty;
    internal DockWindowRestorer()
    {
        topology = MonitorTopologyService.Fingerprint(); MonitorTopologyService.Remember();
        timer.Tick += (_,_) => Tick(); timer.Start();
    }
    private void Tick()
    {
        var current=MonitorTopologyService.Current();
        var key=MonitorTopologyService.Fingerprint(current);
        if (key != topology)
        {
            var currentIds=current.Select(s=>s.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach(var entry in remembered.Values)
                if(!currentIds.Contains(entry.MonitorId)) missing.Add(entry.MonitorId);
            if(AppSettings.Load().RestoreWindowsAfterDocking && missing.Count>0)
            {
                foreach(var placement in remembered.Values.Where(x=>missing.Contains(x.MonitorId)))
                {
                    var monitor=current.FirstOrDefault(s=>s.Id.Equals(placement.MonitorId,StringComparison.OrdinalIgnoreCase));
                    if(monitor==null || !IsWindow(placement.Window) || IsIconic(placement.Window)) continue;
                    var area=monitor.WorkingArea;
                    var r=placement.Rect;
                    var x=Math.Clamp(r.X,area.Left,Math.Max(area.Left,area.Right-r.Width));
                    var y=Math.Clamp(r.Y,area.Top,Math.Max(area.Top,area.Bottom-r.Height));
                    SetWindowPos(placement.Window,IntPtr.Zero,x,y,r.Width,r.Height,0x0010|0x0004|0x0200|0x4000);
                }
                missing.RemoveWhere(m=>currentIds.Contains(m));
            }
            topology=key; MonitorTopologyService.Remember();
        }
        // Do not enumerate unrelated application windows in the background unless
        // docking restore was explicitly enabled (battery/idle budget and privacy).
        if (!AppSettings.Peek().RestoreWindowsAfterDocking)
        {
            remembered.Clear(); missing.Clear(); return;
        }
        // Only refresh when all displays are present. A window moved by Windows to the primary
        // during a dock disconnect must NOT overwrite its original remembered location.
        if(missing.Count>0) return;
        remembered=WinApi.EnumerateAppWindows().Take(120)
            .Where(w=>w.Handle!=IntPtr.Zero && IsWindow(w.Handle) && !IsIconic(w.Handle) &&
                !string.Equals(w.ExePath,Environment.ProcessPath,StringComparison.OrdinalIgnoreCase))
            .Select(w=> (window:w, monitor:current.FirstOrDefault(s=>s.Bounds.Contains(Screen.FromHandle(w.Handle).Bounds.Location))))
            .Where(x=>x.monitor!=null && GetWindowRect(x.window.Handle,out _))
            .Select(x=> { GetWindowRect(x.window.Handle,out var bounds);
                return new Placement(x.window.Handle,x.monitor!.Id,Rectangle.FromLTRB(bounds.Left,bounds.Top,bounds.Right,bounds.Bottom)); })
            .Where(x=>x.Rect.Width>100 && x.Rect.Height>75)
            .ToDictionary(x=>x.Window);
    }
    public void Dispose()=>timer.Dispose();
}
