using System;
using System.IO;
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
        public int ButtonWidth { get; set; } = 128;
        public int ButtonGap { get; set; } = 2;
        public int OpacityPercent { get; set; } = 100;
        public bool ShowTaskView { get; set; } = true;
        public bool ShowClock { get; set; } = true;
        public bool ActiveButtonAnimation { get; set; } = false;
        public int AnimationIntervalMs { get; set; } = 300;
    }
    private static readonly object Gate = new();
    private static string FilePath => Path.Combine(AppPaths.AppDataFolder, "taskbar-designer.json");
    private static Profile? _cached;
    private static string? _cachedPath;
    private static long _checkedAt;
    public static Profile Default => new();
    private static Profile Copy(Profile p) => new()
    {
        Schema = p.Schema, Name = p.Name, Style = p.Style, Alignment = p.Alignment,
        ButtonWidth = p.ButtonWidth, ButtonGap = p.ButtonGap,
        OpacityPercent = p.OpacityPercent, ShowTaskView = p.ShowTaskView,
        ShowClock = p.ShowClock, ActiveButtonAnimation = p.ActiveButtonAnimation,
        AnimationIntervalMs = p.AnimationIntervalMs
    };

    public static void Validate(Profile? p)
    {
        if (p == null || p.Schema != 1 || string.IsNullOrWhiteSpace(p.Name) || p.Name.Length > 60 ||
            p.Style is not ("Classic" or "Compact" or "Glass") ||
            p.Alignment is not ("Left" or "Center") ||
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
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            OpsisRecoveryHub.Snapshot("Before Opsis taskbar visual changes");
            AtomicFile.WriteAllText(FilePath, JsonSerializer.Serialize(profile,
                new JsonSerializerOptions { WriteIndented = true }));
            _cached = null; _cachedPath = null; _checkedAt = 0;
        }
    }

    public static int ButtonHeight(Profile p, bool vertical, int barThickness)
    {
        Validate(p);
        return vertical ? (p.Style == "Compact" ? 34 : 44)
            : Math.Max(23, barThickness - (p.Style == "Compact" ? 12 : 6));
    }
}
