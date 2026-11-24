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
    public static Profile Default => new();

    public static void Validate(Profile? p)
    {
        if (p == null || p.Schema != 1 || p.Name.Length is < 1 or > 60 ||
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
            if (!File.Exists(FilePath)) return Default;
            if (new FileInfo(FilePath).Length > 32 * 1024)
                throw new InvalidDataException("Taskbar profile is oversized.");
            var p = JsonSerializer.Deserialize<Profile>(AtomicFile.ReadAllText(FilePath))
                ?? throw new InvalidDataException("Taskbar profile not readable.");
            Validate(p);
            return p;
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
        }
    }

    public static int ButtonHeight(Profile p, bool vertical, int barThickness)
    {
        Validate(p);
        return vertical ? (p.Style == "Compact" ? 34 : 44)
            : Math.Max(23, barThickness - (p.Style == "Compact" ? 12 : 6));
    }
}
