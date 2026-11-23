using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;
using MotionDesk.Widgets;

namespace MotionDesk.Services;

internal static class VirtualWorkspaces
{
    private const string Root = @"Software\Microsoft\Windows\CurrentVersion\Explorer\VirtualDesktops";
    private static readonly string Store = Path.Combine(AppPaths.AppDataFolder, "workspaces.json");
    private static System.Windows.Forms.Timer? _timer, _switchTimer;
    private static State _state = new();
    private static Guid _applied;
    private static Guid[] _knownDesktops = [];
    private static IVirtualDesktopManager? _manager;
    internal static event Action? Changed;

    private sealed class State
    {
        public string? Fallback { get; set; }
        public Dictionary<Guid, string> Wallpapers { get; set; } = new();
        public Dictionary<Guid, string> Names { get; set; } = new();
    }

    public static bool OverridesSchedule => _applied != Guid.Empty && _state.Wallpapers.ContainsKey(_applied);

    internal static void Start()
    {
        if (_timer != null) return;
        try
        {
            if (File.Exists(Store)) _state = JsonSerializer.Deserialize<State>(AtomicFile.ReadAllText(Store)) ?? new();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        { RotatingLogger.Write("Workspaces: " + ex.Message); }
        _timer = new System.Windows.Forms.Timer { Interval = 950 };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
        Tick();
    }

    internal static void Stop()
    {
        _timer?.Dispose();
        _timer = null;
        _switchTimer?.Dispose();
        _switchTimer = null;
        _applied = Guid.Empty;
        _knownDesktops = [];
        if (_manager != null) Marshal.FinalReleaseComObject(_manager);
        _manager = null;
    }

    internal static Guid[] DesktopIds()
    {
        using var key = Registry.CurrentUser.OpenSubKey(Root);
        if (key?.GetValue("VirtualDesktopIDs") is not byte[] bytes || bytes.Length % 16 != 0) return [];
        return Enumerable.Range(0, bytes.Length / 16).Select(i => new Guid(bytes.AsSpan(i * 16, 16)))
            .Where(id => id != Guid.Empty).Distinct().ToArray();
    }

    internal static Guid CurrentDesktop()
    {
        using var process = Process.GetCurrentProcess();
        foreach (string path in new[] { $@"Software\Microsoft\Windows\CurrentVersion\Explorer\SessionInfo\{process.SessionId}\VirtualDesktops", Root })
        {
            using var key = Registry.CurrentUser.OpenSubKey(path);
            if (key?.GetValue("CurrentVirtualDesktop") is byte[] { Length: 16 } bytes)
            {
                var id = new Guid(bytes);
                if (id != Guid.Empty) return id;
            }
        }

        try
        {
            var type = Type.GetTypeFromCLSID(new Guid("AA509086-5CA9-4C25-8F95-589D3C07B48A"));
            _manager ??= type == null ? null : (IVirtualDesktopManager?)Activator.CreateInstance(type);
            var hwnd = WinApi.GetForegroundWindow();
            if (_manager != null && hwnd != IntPtr.Zero && _manager.IsWindowOnCurrentVirtualDesktop(hwnd, out bool current) == 0 && current &&
                _manager.GetWindowDesktopId(hwnd, out Guid id) == 0 && id != Guid.Empty)
                return id;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException) { }
        var desktops = DesktopIds();
        return desktops.Length == 1 ? desktops[0] : Guid.Empty;
    }

    internal static string? Name(Guid id) =>
        _state.Names.TryGetValue(id, out string? name) ? name : null;
    internal static bool HasWallpaper(Guid id) => _state.Wallpapers.ContainsKey(id);

    internal static void SaveDefault()
    {
        _state.Fallback = JsonSerializer.Serialize(WallpaperSettings.Load());
        Persist();
    }

    internal static bool BindCurrent(string name)
    {
        var id = CurrentDesktop();
        if (id == Guid.Empty) return false;
        var wallpaper = JsonSerializer.Serialize(WallpaperSettings.Load());
        _state.Fallback ??= wallpaper;
        _state.Wallpapers[id] = wallpaper;
        if (name.Trim().Length > 0) _state.Names[id] = name.Trim();
        _applied = id;
        Persist();
        Changed?.Invoke();
        return true;
    }

    internal static void Forget(Guid id)
    {
        if (!_state.Wallpapers.Remove(id)) return;
        _state.Names.Remove(id);
        Persist();
        if (_applied == id && _state.Fallback is { } fallback) ApplyWallpaper(fallback);
        Changed?.Invoke();
    }

    internal static void OpenTaskView() => DesktopKey(0x09, ctrl: false);
    internal static void OpenSystemTray() => DesktopKey(0x42, ctrl: false);
    internal static void CreateDesktop() => DesktopKey(0x44);
    internal static void CloseDesktop() => DesktopKey(0x73);
    internal static void Switch(bool next) => DesktopKey(next ? (byte)0x27 : (byte)0x25);

    internal static void SwitchTo(Guid target)
    {
        _switchTimer?.Dispose();
        var timer = _switchTimer = new System.Windows.Forms.Timer { Interval = 340 };
        int attempts = 0;
        Guid previous = Guid.Empty;
        long requestedAt = 0;
        void Step()
        {
            var ordered = DesktopIds();
            var current = CurrentDesktop();
            int index = Array.IndexOf(ordered, current), destination = Array.IndexOf(ordered, target);
            bool awaitingSwitch = previous == current && Environment.TickCount64 - requestedAt < 1500;
            if (index >= 0 && destination >= 0 && index != destination && awaitingSwitch) return;
            if (index < 0 || destination < 0 || index == destination || previous == current || attempts++ >= ordered.Length)
            {
                timer.Dispose();
                if (ReferenceEquals(_switchTimer, timer)) _switchTimer = null;
                return;
            }
            previous = current;
            requestedAt = Environment.TickCount64;
            Switch(next: destination > index);
        }
        timer.Tick += (_, _) => Step();
        timer.Start();
        Step();
    }

    private static void Tick()
    {
        try
        {
            var desktops = DesktopIds();
            bool topologyChanged = !desktops.SequenceEqual(_knownDesktops);
            var id = CurrentDesktop();
            if (id != Guid.Empty && id != _applied)
            {
                var previous = _applied;
                _applied = id;
                try
                {
                    if (_state.Wallpapers.TryGetValue(id, out var wallpaper)) ApplyWallpaper(wallpaper);
                    else if (_state.Fallback is { } fallback && (previous == Guid.Empty || _state.Wallpapers.ContainsKey(previous)))
                        ApplyWallpaper(fallback);
                }
                catch
                {
                    // Allow the next tick to retry if the wallpaper settings could not be written.
                    _applied = previous;
                    throw;
                }
                topologyChanged = true;
            }
            if (!topologyChanged) return;
            _knownDesktops = desktops;
            Changed?.Invoke();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or COMException)
        { RotatingLogger.Write("Workspaces: " + ex.Message); }
    }

    private static void ApplyWallpaper(string serialized)
    {
        if (JsonSerializer.Deserialize<WallpaperSettings>(serialized) is not { } snapshot) return;
        var current = WallpaperSettings.Load();
        current.Mode = WallpaperSettings.NormalizeMode(snapshot.Mode);
        current.PageSource = snapshot.PageSource;
        current.ImagePath = snapshot.ImagePath;
        current.ImageFit = snapshot.ImageFit;
        current.AthensPhase = snapshot.AthensPhase;
        current.WeatherLandmarkId = snapshot.WeatherLandmarkId;
        current.WeatherLandmarks = snapshot.WeatherLandmarks;
        current.WeatherSimulation = snapshot.WeatherSimulation;
        current.TimeSimulation = snapshot.TimeSimulation;
        current.WindSimulation = snapshot.WindSimulation;
        current.WaveStyle = snapshot.WaveStyle;
        current.PaletteName = snapshot.PaletteName;
        if (current.Mode == "Video" && snapshot.CurrentPlaylistFile() is { } video)
        {
            current.AddVideoFiles([video]);
            int index = current.VideoPaths.FindIndex(path => string.Equals(path, video, StringComparison.OrdinalIgnoreCase));
            if (index >= 0) current.CurrentVideoIndex = index;
        }
        current.Save();
        WallpaperHostEngine.Instance.RefreshAll();
    }

    private static void Persist()
    {
        Directory.CreateDirectory(AppPaths.AppDataFolder);
        AtomicFile.WriteAllText(Store, JsonSerializer.Serialize(_state));
    }

    private static void DesktopKey(byte key, bool ctrl = true)
    {
        const byte win = 0x5B, control = 0x11;
        keybd_event(win, 0, 0, UIntPtr.Zero);
        if (ctrl) keybd_event(control, 0, 0, UIntPtr.Zero);
        keybd_event(key, 0, 0, UIntPtr.Zero);
        keybd_event(key, 0, 2, UIntPtr.Zero);
        if (ctrl) keybd_event(control, 0, 2, UIntPtr.Zero);
        keybd_event(win, 0, 2, UIntPtr.Zero);
    }

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);

    [ComImport, Guid("A5CD92FF-29BE-454C-8D04-D82879FB3F1B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IVirtualDesktopManager
    {
        [PreserveSig] int IsWindowOnCurrentVirtualDesktop(IntPtr window, [MarshalAs(UnmanagedType.Bool)] out bool current);
        [PreserveSig] int GetWindowDesktopId(IntPtr window, out Guid id);
        [PreserveSig] int MoveWindowToDesktop(IntPtr window, in Guid id);
    }
}
