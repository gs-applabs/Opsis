using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Forms;

namespace MotionDesk.Services;

/// <summary>
/// P3 Window Rules: foreground-only, opt-in, without global hooks, DLL injection
/// or enumerating every application on every tick. Keeps in-memory window positions
/// for user-directed restore and restores on exit.
/// </summary>
public sealed class WindowRulesEngine : IDisposable
{
    public sealed class Rule
    {
        public string Process { get; set; } = "";
        public bool Enabled { get; set; }
        public bool MoveAndResize { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; } = 960;
        public int Height { get; set; } = 640;
        public bool AlwaysOnTop { get; set; }
    }
    private sealed record WindowState(IntPtr Handle, int ProcessId, NativeRect Bounds, bool WasTopMost);
    private const int SwpNoActivate = 0x0010;
    private const int SwpNoSize = 0x0001;
    private const int SwpNoMove = 0x0002;
    private const int SwpNoZOrder = 0x0004;
    private const int GwlExStyle = -20;
    private const long WsExTopMost = 0x00000008;
    private static readonly IntPtr HwndTopMost = new(-1), HwndNotTopMost = new(-2);
    private static readonly HashSet<string> Protected = new(StringComparer.OrdinalIgnoreCase)
    {
        "Opsis","explorer","dwm","winlogon","consent","lsass","taskmgr",
        "sihost","ShellExperienceHost","StartMenuExperienceHost","System","SearchHost"
    };
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    private readonly Dictionary<IntPtr, WindowState> _applied = new();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1500 };
    private static readonly object Gate = new();
    private static List<Rule>? Cached;
    private static string ConfigPath => Path.Combine(AppPaths.AppDataFolder, "window-rules.json");
    public static WindowRulesEngine Instance { get; } = new();

    private WindowRulesEngine() => _timer.Tick += (_, _) => Evaluate();
    public void Start() => _timer.Start();
    public void Stop() => _timer.Stop();

    private static void Validate(Rule? rule)
    {
        if (rule == null || string.IsNullOrWhiteSpace(rule.Process) || rule.Process.Length > 60 ||
            rule.Process.Any(x => !char.IsAsciiLetterOrDigit(x) && x is not ('-' or '_' or '.')) ||
            Protected.Contains(rule.Process) ||
            rule.Width is < 300 or > 10000 || rule.Height is < 240 or > 10000 ||
            rule.X is < -20000 or > 20000 || rule.Y is < -20000 or > 20000)
            throw new InvalidDataException("Unsupported window target or geometry.");
    }
    public static IReadOnlyList<Rule> Read()
    {
        lock (Gate)
        {
            if (Cached != null) return Cached.Select(Copy).ToArray();
            if (!File.Exists(ConfigPath)) { Cached = new List<Rule>(); return Array.Empty<Rule>(); }
            if (new FileInfo(ConfigPath).Length > 128 * 1024) throw new InvalidDataException("Window rules exceed size limit.");
            var rules = JsonSerializer.Deserialize<List<Rule>>(AtomicFile.ReadAllText(ConfigPath))
                ?? throw new InvalidDataException("Invalid Window Rules file.");
            if (rules.Count > 100) throw new InvalidDataException("Excessive Window Rules.");
            foreach (var rule in rules) Validate(rule);
            if (rules.GroupBy(x => x.Process, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
                throw new InvalidDataException("Duplicate Window Rules.");
            Cached = rules;
            return rules.Select(Copy).ToArray();
        }
    }
    private static Rule Copy(Rule r) => new()
    {
        Process = r.Process, Enabled = r.Enabled, MoveAndResize = r.MoveAndResize,
        X = r.X, Y = r.Y, Width = r.Width, Height = r.Height, AlwaysOnTop = r.AlwaysOnTop
    };

    public static void Save(IReadOnlyList<Rule> rules)
    {
        if (rules.Count > 100) throw new InvalidDataException("Too many window rules.");
        foreach (var rule in rules) Validate(rule);
        if (rules.GroupBy(x => x.Process, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
            throw new InvalidDataException("Duplicate window rules.");
        lock (Gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
            AtomicFile.WriteAllText(ConfigPath, JsonSerializer.Serialize(rules, new JsonSerializerOptions { WriteIndented = true }));
            Cached = rules.Select(Copy).ToList();
        }
    }

    private void Evaluate()
    {
        if (!AppSettings.Peek().WindowRulesEnabled || WindowsUpdateGuard.RequiresReview || SafeModeService.IsSafeMode) return;
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero || !IsWindowVisible(hwnd) || IsIconic(hwnd)) return;
            if (GetWindowThreadProcessId(hwnd, out var pid) == 0 || pid == 0) return;
            using var process = Process.GetProcessById((int)pid);
            if (Protected.Contains(process.ProcessName)) return;
            var rule = Read().FirstOrDefault(x => x.Enabled &&
                string.Equals(x.Process, process.ProcessName, StringComparison.OrdinalIgnoreCase));
            if (rule == null || (!rule.MoveAndResize && !rule.AlwaysOnTop) || _applied.ContainsKey(hwnd)) return;
            if (!GetWindowRect(hwnd, out var rect)) return;
            var originalTopMost = (GetWindowLongPtr(hwnd, GwlExStyle).ToInt64() & WsExTopMost) != 0;
            var flags = SwpNoActivate | (rule.MoveAndResize ? 0 : (SwpNoMove | SwpNoSize)) |
                (rule.AlwaysOnTop ? 0 : SwpNoZOrder);
            if (!SetWindowPos(hwnd, rule.AlwaysOnTop ? HwndTopMost : IntPtr.Zero,
                rule.X, rule.Y, rule.Width, rule.Height, flags)) return;
            _applied[hwnd] = new WindowState(hwnd, (int)pid, rect, originalTopMost);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception or ArgumentException)
        { RotatingLogger.Write("Window rule skipped: " + e.Message); }
    }

    public void Restore()
    {
        foreach (var state in _applied.Values.ToArray())
        {
            try
            {
                if (!IsWindow(state.Handle) || GetWindowThreadProcessId(state.Handle, out var pid) == 0 ||
                    pid != state.ProcessId) continue;
                var b = state.Bounds;
                SetWindowPos(state.Handle, state.WasTopMost ? HwndTopMost : HwndNotTopMost,
                    b.Left, b.Top, b.Right - b.Left, b.Bottom - b.Top, SwpNoActivate);
            }
            catch (Exception e) { RotatingLogger.Write("Window restore: " + e.Message); }
        }
        _applied.Clear();
    }
    public void Dispose() { _timer.Stop(); Restore(); _timer.Dispose(); }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect bounds);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr order, int x, int y, int width, int height, int flags);
    [DllImport("user32.dll", EntryPoint="GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);
}
