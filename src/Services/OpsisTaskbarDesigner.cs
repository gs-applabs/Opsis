using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Drawing;
using System.Text.Json;

namespace MotionDesk.Services;

/// <summary>
/// Opsis-owned taskbar rendering profile, independent of the native Windows taskbar.
/// Does not alter Explorer, DWM, protected binaries, registry, or shell placement.
/// </summary>
public static class OpsisTaskbarDesigner
{
    public sealed class Profile
    {
        public int Schema { get; set; } = 1;
        public string Name { get; set; } = "Default";
        public string Style { get; set; } = "Classic"; // Classic | Compact | Glass
        public string Alignment { get; set; } = "Left"; // Left | Center
        public string Edge { get; set; } = "Native"; // Native | Top | Bottom | Left | Right
        public bool Floating { get; set; }
        public bool Segmented { get; set; }
        public bool DynamicSize { get; set; }
        public int EdgeMargin { get; set; } = 10;
        public int LengthPercent { get; set; } = 80;
        public int Thickness { get; set; } = 48;
        public int ButtonWidth { get; set; } = 128;
        public int ButtonGap { get; set; } = 2;
        public int OpacityPercent { get; set; } = 100;
        public bool ShowTaskView { get; set; } = true;
        public bool ShowClock { get; set; } = true;
        public string EmptyMiddleClick { get; set; } = "None";
        public string EmptyDoubleClick { get; set; } = "None";
        public string VolumeStyle { get; set; } = "Windows";
        public string ThumbnailStyle { get; set; } = "Off"; // Off | Compact | Large
        public static bool IsThumbnailStyle(string? value) => value is "Off" or "Compact" or "Large";
        public bool ActiveButtonAnimation { get; set; } = false;
        public int AnimationIntervalMs { get; set; } = 300;
    }
    public static IReadOnlyList<string> Gestures { get; } = Array.AsReadOnly(new[] { "None", "Task view", "DeskFlip", "Notifications", "Quick settings", "Volume" });
    public static bool ValidGesture(string? action) => action != null && Gestures.Contains(action);
    public static bool ValidVolumeStyle(string? style) => style is "Windows" or "Compact" or "Large";
    private static readonly object Gate = new();
    private static string FilePath => Path.Combine(AppPaths.AppDataFolder, "taskbar-designer.json");
    private static Profile? _cached;
    private static string? _cachedPath;
    private static long _checkedAt;
    public static Profile Default => new();

    // Original Opsis presets; use only the already shipped geometry/rendering
    // and sound owners. Never invoke an external shell mod or activate a bar.
    public static IReadOnlyList<string> BuiltInPresets { get; } =
        Array.AsReadOnly(new[] { "Classic", "Compact", "Floating Glass", "Segmented Dock", "Vertical Studio", "Minimal" });

    public static Profile CreatePreset(string name)
    {
        var p = name switch
        {
            "Classic" => new Profile(),
            "Compact" => new Profile { Style = "Compact", ButtonWidth = 84, ButtonGap = 1, ThumbnailStyle = "Compact", VolumeStyle = "Compact" },
            "Floating Glass" => new Profile { Style = "Glass", Alignment = "Center", Edge = "Bottom", Floating = true,
                DynamicSize = true, LengthPercent = 80, Thickness = 56, ButtonWidth = 100, OpacityPercent = 88, VolumeStyle = "Large", ThumbnailStyle = "Large" },
            "Segmented Dock" => new Profile { Style = "Glass", Edge = "Bottom", Alignment = "Center", Segmented = true,
                Floating = true, DynamicSize = true, EdgeMargin = 12, ButtonGap = 6, ButtonWidth = 88, VolumeStyle = "Compact" },
            "Vertical Studio" => new Profile { Style = "Compact", Edge = "Left", Thickness = 72, Floating = true,
                LengthPercent = 85, ButtonWidth = 84, ButtonGap = 3, ThumbnailStyle = "Compact", VolumeStyle = "Compact" },
            "Minimal" => new Profile { Style = "Compact", ButtonWidth = 76, ButtonGap = 0, ShowTaskView = false,
                ShowClock = true, ThumbnailStyle = "Off", VolumeStyle = "Compact" },
            _ => throw new ArgumentOutOfRangeException(nameof(name))
        };
        p.Name = name;
        Validate(p);
        return p;
    }
    private static Profile Copy(Profile p) => new()
    {
        Schema = p.Schema, Name = p.Name, Style = p.Style, Alignment = p.Alignment,
        ButtonWidth = p.ButtonWidth, ButtonGap = p.ButtonGap,
        OpacityPercent = p.OpacityPercent, ShowTaskView = p.ShowTaskView,
        ShowClock = p.ShowClock, ActiveButtonAnimation = p.ActiveButtonAnimation,
        AnimationIntervalMs = p.AnimationIntervalMs, ThumbnailStyle = p.ThumbnailStyle,
        EmptyMiddleClick = p.EmptyMiddleClick, EmptyDoubleClick = p.EmptyDoubleClick, VolumeStyle = p.VolumeStyle,
        Edge = p.Edge, Floating = p.Floating, Segmented = p.Segmented, DynamicSize = p.DynamicSize,
        EdgeMargin = p.EdgeMargin, LengthPercent = p.LengthPercent, Thickness = p.Thickness
    };

    public static void Validate(Profile? p)
    {
        if (p == null || p.Schema != 1 || string.IsNullOrWhiteSpace(p.Name) || p.Name.Length > 60 ||
            p.Style is not ("Classic" or "Compact" or "Glass") ||
            !Profile.IsThumbnailStyle(p.ThumbnailStyle) || !ValidGesture(p.EmptyMiddleClick) || !ValidGesture(p.EmptyDoubleClick) || !ValidVolumeStyle(p.VolumeStyle) ||
            p.Alignment is not ("Left" or "Center") ||
            p.Edge is not ("Native" or "Top" or "Bottom" or "Left" or "Right") ||
            p.EdgeMargin is < 0 or > 32 || p.LengthPercent is < 40 or > 100 || p.Thickness is < 36 or > 112 ||
            p.ButtonWidth is < 72 or > 240 || p.ButtonGap is < 0 or > 20 ||
            p.OpacityPercent is < 60 or > 100 ||
            p.AnimationIntervalMs is < 180 or > 800)
            throw new InvalidDataException("Invalid Opsis taskbar visual profile.");
    }

    public static Profile Read()
    {
        lock (Gate)
        {
            var path = FilePath;
            if (_cached != null && string.Equals(_cachedPath, path, StringComparison.OrdinalIgnoreCase) &&
                Environment.TickCount64 - _checkedAt < 5000)
                return Copy(_cached);
            if (!File.Exists(path))
            {
                _cached = Default; _cachedPath = path; _checkedAt = Environment.TickCount64;
                return Copy(_cached);
            }
            if (new FileInfo(path).Length > 32 * 1024)
                throw new InvalidDataException("Taskbar profile is oversized.");
            var p = JsonSerializer.Deserialize<Profile>(AtomicFile.ReadAllText(path))
                ?? throw new InvalidDataException("Taskbar profile not readable.");
            Validate(p);
            _cached = p; _cachedPath = path; _checkedAt = Environment.TickCount64;
            return Copy(p);
        }
    }

    public static void Save(Profile profile)
    {
        Validate(profile);
        lock (Gate)
        {
            if (Equivalent(Read(), profile)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            OpsisRecoveryHub.Snapshot("Before Opsis taskbar visual changes");
            AtomicFile.WriteAllText(FilePath, JsonSerializer.Serialize(profile,
                new JsonSerializerOptions { WriteIndented = true }));
            _cached = null; _cachedPath = null; _checkedAt = 0;
        }
    }

    private static string MonitorFile => Path.Combine(AppPaths.AppDataFolder, "taskbar-monitor-profiles.json");
    private static Dictionary<string, Profile>? _monitorCache;
    private static string? _monitorCachePath;
    private static long _monitorCheckedAt;

    public static bool ValidMonitorKey(string? key) => !string.IsNullOrWhiteSpace(key) && key.Length <= 512 && !key.Any(char.IsControl);
    internal static string MonitorKey(MonitorTopologyService.Display display, IReadOnlyList<MonitorTopologyService.Display> displays) =>
        displays.Count(x => string.Equals(x.Id, display.Id, StringComparison.OrdinalIgnoreCase)) == 1 ?
            "id:" + display.Id : "device:" + display.DeviceName;

    public static IReadOnlyDictionary<string, Profile> ReadMonitorProfiles()
    {
        lock (Gate)
        {
            var path = MonitorFile;
            if (_monitorCache == null || _monitorCachePath != path || Environment.TickCount64 - _monitorCheckedAt >= 5000)
            {
                var values = new Dictionary<string, Profile>(StringComparer.OrdinalIgnoreCase);
                if (File.Exists(path))
                {
                    if (new FileInfo(path).Length > 128 * 1024) throw new InvalidDataException("Monitor profiles too large.");
                    var loaded = JsonSerializer.Deserialize<Dictionary<string, Profile>>(AtomicFile.ReadAllText(path))
                        ?? throw new InvalidDataException("Invalid monitor profiles.");
                    if (loaded.Count > 32) throw new InvalidDataException("Too many monitor profiles.");
                    foreach (var pair in loaded)
                    {
                        Validate(pair.Value);
                        if (!ValidMonitorKey(pair.Key) || !values.TryAdd(pair.Key, pair.Value))
                            throw new InvalidDataException("Invalid or duplicate monitor profile identity.");
                    }
                }
                _monitorCache = values; _monitorCachePath = path; _monitorCheckedAt = Environment.TickCount64;
            }
            return _monitorCache.ToDictionary(x => x.Key, x => Copy(x.Value), StringComparer.OrdinalIgnoreCase);
        }
    }

    public static void SaveMonitorProfile(string key, Profile? profile)
    {
        if (!ValidMonitorKey(key)) throw new ArgumentException("Invalid monitor identity.", nameof(key));
        if (profile != null) Validate(profile);
        lock (Gate)
        {
            var values = ReadMonitorProfiles().ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
            if (profile == null)
            {
                if (!values.Remove(key)) return;
            }
            else
            {
                if (values.TryGetValue(key, out var old) && Equivalent(old, profile)) return;
                if (!values.ContainsKey(key) && values.Count >= 32) throw new InvalidDataException("Too many monitor profiles.");
                values[key] = Copy(profile);
            }
            OpsisRecoveryHub.Snapshot("Before per-monitor Opsis taskbar changes");
            AtomicFile.WriteAllText(MonitorFile, JsonSerializer.Serialize(values));
            _monitorCache = null;
        }
    }

    public static bool Equivalent(Profile? a, Profile? b) =>
        a == null || b == null ? a == b : JsonSerializer.Serialize(a) == JsonSerializer.Serialize(b);

    // Explicit placements are Opsis companion bars in the existing work area,
    // not edits to Explorer's native taskbar or its reserved work area.
    public static bool IsDetached(Profile p) => p.Edge != "Native" || p.Floating || p.Segmented || p.DynamicSize;

    public static Rectangle CalculateBounds(Profile p, Rectangle screen, Rectangle work, Rectangle native, int items)
    {
        Validate(p);
        if (!IsDetached(p))
            return native.IsEmpty ? new Rectangle(screen.Left, screen.Bottom - 44, screen.Width, 44)
                                  : Rectangle.Intersect(native, screen);
        var area = Rectangle.Intersect(screen, work);
        if (area.Width < 100 || area.Height < 100) return Rectangle.Empty;
        var edge = p.Edge == "Native" ?
            (native.IsEmpty ? "Bottom" : native.Height > native.Width ?
                (native.Left <= screen.Left + screen.Width / 2 ? "Left" : "Right") :
                (native.Top <= screen.Top + screen.Height / 2 ? "Top" : "Bottom")) : p.Edge;
        bool vertical = edge is "Left" or "Right";
        int margin = p.Floating || p.Segmented ? Math.Min(p.EdgeMargin, Math.Min(area.Width, area.Height) / 8) : 0;
        int available = (vertical ? area.Height : area.Width) - margin * 2;
        int across = (vertical ? area.Width : area.Height) - margin * 2;
        if (available < 100 || across < 24) return Rectangle.Empty;
        int thickness = Math.Min(p.Thickness, across);
        int length = p.Floating || p.Segmented ? available * p.LengthPercent / 100 : available;
        if (p.DynamicSize)
        {
            int fixedSize = vertical ? 38 + (p.ShowTaskView ? 36 : 0) + (p.ShowClock ? 42 : 0)
                : 66 + (p.ShowTaskView ? 42 : 0) + (p.ShowClock ? 104 : 0);
            int desired = fixedSize + Math.Clamp(items, 0, 100) *
                ((vertical ? 44 : p.ButtonWidth) + p.ButtonGap * 2) + 16;
            length = Math.Min(length, Math.Max(Math.Min(available, 240), desired));
        }
        length = Math.Clamp(length, Math.Min(available, 100), available);
        int offset = p.Alignment == "Center" || p.Floating || p.Segmented ? (available - length) / 2 : 0;
        int x = vertical ? (edge == "Left" ? area.Left + margin : area.Right - margin - thickness)
            : area.Left + margin + offset;
        int y = vertical ? area.Top + margin + offset
            : (edge == "Top" ? area.Top + margin : area.Bottom - margin - thickness);
        return new Rectangle(x, y, vertical ? thickness : length, vertical ? length : thickness);
    }

    // DeskStrip owns its chosen edge and reserves its reveal footprint even
    // while auto-hidden. Move only the Opsis companion bar, never Windows'
    // taskbar or saved geometry. Fail closed if there is not enough room.
    internal static Rectangle AvoidDeskStrip(Profile profile, Rectangle desired, Rectangle work,
        IReadOnlyList<Rectangle> reservations)
    {
        if (desired.IsEmpty || !reservations.Any(r => r.IntersectsWith(desired))) return desired;
        if (!IsDetached(profile)) return Rectangle.Empty;
        if (!work.Contains(desired)) return Rectangle.Empty;
        bool vertical = desired.Height > desired.Width;
        bool nearStart = vertical ? desired.Left + desired.Width / 2 <= work.Left + work.Width / 2
            : desired.Top + desired.Height / 2 <= work.Top + work.Height / 2;
        const int clearance = 8;
        var candidate = desired;
        for (int step = 0; step < reservations.Count; step++)
        {
            var collisions = reservations.Where(r => r.IntersectsWith(candidate)).ToArray();
            if (collisions.Length == 0) return candidate;
            if (vertical)
                candidate.X = nearStart ? collisions.Max(r => r.Right) + clearance
                    : collisions.Min(r => r.Left) - clearance - candidate.Width;
            else
                candidate.Y = nearStart ? collisions.Max(r => r.Bottom) + clearance
                    : collisions.Min(r => r.Top) - clearance - candidate.Height;
            if (!work.Contains(candidate)) return Rectangle.Empty;
        }
        return reservations.Any(r => r.IntersectsWith(candidate)) ? Rectangle.Empty : candidate;
    }

    public static int ButtonHeight(Profile p, bool vertical, int barThickness)
    {
        Validate(p);
        return vertical ? (p.Style == "Compact" ? 34 : 44)
            : Math.Max(23, barThickness - (p.Style == "Compact" ? 12 : 6));
    }
}
