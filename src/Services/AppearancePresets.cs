using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MotionDesk.Services;

/// <summary>
/// P2 appearance presets: portable, data-only snapshots of supported native Explorer/Taskbar
/// options and Opsis Hot Corners. Never loads executable code from a preset.
/// </summary>
public static class AppearancePresets
{
    public sealed class Preset
    {
        public int Schema { get; set; } = 1;
        public string Name { get; set; } = "";
        public int HideFileExt { get; set; }
        public int Hidden { get; set; } = 2;
        public int CompactMode { get; set; }
        public int ExplorerLaunchTo { get; set; } = 2;
        public int TaskbarAlignment { get; set; } = 1;
        public int TaskbarWidgets { get; set; } = 1;
        public int TaskbarTaskView { get; set; } = 1;
        public int TaskbarSearch { get; set; } = 1;
        public bool HotCornersEnabled { get; set; }
        public string TopLeft { get; set; } = "None";
        public string TopRight { get; set; } = "None";
        public string BottomLeft { get; set; } = "None";
        public string BottomRight { get; set; } = "None";
        public int DwellMs { get; set; } = 700;
        public int EdgePixels { get; set; } = 5;
        public bool PauseOnFullscreen { get; set; } = true;
    }

    private static string Folder => Path.Combine(AppPaths.AppDataFolder, "appearance-presets");
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private static string SafeName(string name)
    {
        name = name.Trim();
        if (name.Length is < 1 or > 50 || name is "." or ".." ||
            name.Any(ch => !char.IsLetterOrDigit(ch) && ch != ' ' && ch != '-' && ch != '_'))
            throw new ArgumentException("Preset names may contain only letters, numbers, spaces, hyphens and underscores.");
        return name;
    }

    private static string FilePath(string name) => Path.Combine(Folder, SafeName(name) + ".json");

    public static IReadOnlyList<string> List()
    {
        if (!Directory.Exists(Folder)) return Array.Empty<string>();
        return Directory.EnumerateFiles(Folder, "*.json", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileNameWithoutExtension)
            .Where(x => !string.IsNullOrEmpty(x))
            .Cast<string>().OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    public static Preset Capture(string name)
    {
        var s = AppSettings.Load();
        return new Preset
        {
            Name = SafeName(name),
            HideFileExt = ExplorerViewProfiles.ReadOption("HideFileExt"),
            Hidden = ExplorerViewProfiles.ReadOption("Hidden"),
            CompactMode = ExplorerViewProfiles.ReadOption("UseCompactMode"),
            ExplorerLaunchTo = ExplorerViewProfiles.ReadOption("LaunchTo"),
            TaskbarAlignment = TaskbarViewProfiles.IsWindows11 ? TaskbarViewProfiles.ReadOption("TaskbarAl") : 1,
            TaskbarWidgets = TaskbarViewProfiles.IsWindows11 ? TaskbarViewProfiles.ReadOption("TaskbarDa") : 1,
            TaskbarTaskView = TaskbarViewProfiles.IsWindows11 ? TaskbarViewProfiles.ReadOption("ShowTaskViewButton") : 1,
            TaskbarSearch = TaskbarViewProfiles.IsWindows11 ? TaskbarViewProfiles.ReadOption("SearchboxTaskbarMode") : 1,
            HotCornersEnabled = s.HotCornersEnabled,
            TopLeft = s.HotCornerTopLeft, TopRight = s.HotCornerTopRight,
            BottomLeft = s.HotCornerBottomLeft, BottomRight = s.HotCornerBottomRight,
            DwellMs = s.HotCornerDwellMilliseconds, EdgePixels = s.HotCornerEdgePixels,
            PauseOnFullscreen = s.HotCornersPauseOnFullscreen
        };
    }

    public static void SaveCurrent(string name)
    {
        var p = Capture(name);
        Directory.CreateDirectory(Folder);
        // No silent overwrites; the caller can delete a preset explicitly.
        var path = FilePath(name);
        if (File.Exists(path)) throw new IOException("A preset with this name already exists.");
        AtomicFile.WriteAllText(path, JsonSerializer.Serialize(p, Json));
    }

    /// <summary>Import validated, data-only preset without applying system settings.</summary>
    public static void SavePortable(Preset preset)
    {
        Validate(preset);
        Directory.CreateDirectory(Folder);
        var path = FilePath(preset.Name);
        if (File.Exists(path)) throw new IOException("Appearance preset already exists.");
        AtomicFile.WriteAllText(path, JsonSerializer.Serialize(preset, Json));
    }

    /// <summary>Export validated, data-only JSON without changing the desktop.</summary>
    public static void ExportPortable(string name, string destination)
    {
        var preset = Read(name);
        if (!string.Equals(Path.GetExtension(destination), ".json", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Appearance exports must use .json.", nameof(destination));
        if (File.Exists(destination))
            throw new IOException("The destination already exists.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
        AtomicFile.WriteAllText(destination, JsonSerializer.Serialize(preset, Json));
    }

    /// <summary>Import untrusted data only after schema/size/value validation. Never apply it automatically.</summary>
    public static string ImportPortable(string source)
    {
        if (!string.Equals(Path.GetExtension(source), ".json", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Appearance imports must use .json.", nameof(source));
        if (new FileInfo(source).Length > 32 * 1024)
            throw new InvalidDataException("Appearance preset exceeds 32 KB.");
        var preset = JsonSerializer.Deserialize<Preset>(AtomicFile.ReadAllText(source))
            ?? throw new InvalidDataException("Invalid appearance preset JSON.");
        Validate(preset);
        SavePortable(preset);
        return preset.Name;
    }

    public static Preset Read(string name)
    {
        var path = FilePath(name);
        if (new FileInfo(path).Length > 32 * 1024) throw new InvalidDataException("Preset file too large.");
        var p = JsonSerializer.Deserialize<Preset>(AtomicFile.ReadAllText(path))
                ?? throw new InvalidDataException("Invalid appearance preset.");
        Validate(p);
        return p;
    }

    public static void Delete(string name) => File.Delete(FilePath(name));

    public static string Preview(string name)
    {
        var p = Read(name);
        var s = AppSettings.Load();
        var lines = new List<string> { "Appearance preset: " + p.Name };
        void Show(string label, int before, int after) => lines.Add(label + ": " + before + " → " + after + (before == after ? " (unchanged)" : ""));
        Show("Explorer extensions", ExplorerViewProfiles.ReadOption("HideFileExt"), p.HideFileExt);
        Show("Explorer hidden files", ExplorerViewProfiles.ReadOption("Hidden"), p.Hidden);
        Show("Explorer compact view", ExplorerViewProfiles.ReadOption("UseCompactMode"), p.CompactMode);
        Show("Explorer launch location", ExplorerViewProfiles.ReadOption("LaunchTo"), p.ExplorerLaunchTo);
        if (TaskbarViewProfiles.IsWindows11)
        {
            Show("Taskbar alignment", TaskbarViewProfiles.ReadOption("TaskbarAl"), p.TaskbarAlignment);
            Show("Taskbar widgets", TaskbarViewProfiles.ReadOption("TaskbarDa"), p.TaskbarWidgets);
            Show("Taskbar task view", TaskbarViewProfiles.ReadOption("ShowTaskViewButton"), p.TaskbarTaskView);
            Show("Taskbar search", TaskbarViewProfiles.ReadOption("SearchboxTaskbarMode"), p.TaskbarSearch);
        }
        lines.Add("Hot Corners: " + (s.HotCornersEnabled ? "on" : "off") + " → " + (p.HotCornersEnabled ? "on" : "off"));
        lines.Add("Dwell: " + s.HotCornerDwellMilliseconds + "ms → " + p.DwellMs + "ms");
        lines.Add("Corner mappings: " + p.TopLeft + ", " + p.TopRight + ", " + p.BottomLeft + ", " + p.BottomRight);
        lines.Add("Preview only — no system settings have changed.");
        return string.Join(Environment.NewLine, lines);
    }

    public static void Apply(string name)
    {
        var target = Read(name);
        var before = Capture("Rollback");
        // Validate the entire preset before touching any Windows or Opsis settings.
        Validate(target);
        if (Equivalent(before, target)) return; // No writes for an unchanged profile.
        OpsisRecoveryHub.Snapshot("Before appearance preset: " + target.Name);
        try { ApplyCore(target); }
        catch
        {
            try { ApplyCore(before); }
            catch (Exception rollbackError)
            {
                throw new InvalidOperationException("Appearance preset failed; automatic rollback also failed. Use Explorer and Taskbar Restore actions.", rollbackError);
            }
            throw;
        }
    }

    /// <summary>Side-effect-free value comparison; preset names are metadata only.</summary>
    public static bool Equivalent(Preset a, Preset b) =>
        a.HideFileExt == b.HideFileExt && a.Hidden == b.Hidden &&
        a.CompactMode == b.CompactMode && a.ExplorerLaunchTo == b.ExplorerLaunchTo &&
        (!TaskbarViewProfiles.IsWindows11 ||
            (a.TaskbarAlignment == b.TaskbarAlignment && a.TaskbarWidgets == b.TaskbarWidgets &&
             a.TaskbarTaskView == b.TaskbarTaskView && a.TaskbarSearch == b.TaskbarSearch)) &&
        a.HotCornersEnabled == b.HotCornersEnabled && a.TopLeft == b.TopLeft &&
        a.TopRight == b.TopRight && a.BottomLeft == b.BottomLeft &&
        a.BottomRight == b.BottomRight && a.DwellMs == b.DwellMs &&
        a.EdgePixels == b.EdgePixels && a.PauseOnFullscreen == b.PauseOnFullscreen;

    private static void ApplyCore(Preset p)
    {
        // Only changed options are written; no needless SSD or registry activity.
        if (ExplorerViewProfiles.ReadOption("HideFileExt") != p.HideFileExt)
            ExplorerViewProfiles.ApplyOption("HideFileExt", p.HideFileExt);
        if (ExplorerViewProfiles.ReadOption("Hidden") != p.Hidden)
            ExplorerViewProfiles.ApplyOption("Hidden", p.Hidden);
        if (ExplorerViewProfiles.ReadOption("UseCompactMode") != p.CompactMode)
            ExplorerViewProfiles.ApplyOption("UseCompactMode", p.CompactMode);
        if (ExplorerViewProfiles.ReadOption("LaunchTo") != p.ExplorerLaunchTo)
            ExplorerViewProfiles.ApplyOption("LaunchTo", p.ExplorerLaunchTo);
        if (TaskbarViewProfiles.IsWindows11)
        {
            if (TaskbarViewProfiles.ReadOption("TaskbarAl") != p.TaskbarAlignment)
                TaskbarViewProfiles.ApplyOption("TaskbarAl", p.TaskbarAlignment);
            if (TaskbarViewProfiles.ReadOption("TaskbarDa") != p.TaskbarWidgets)
                TaskbarViewProfiles.ApplyOption("TaskbarDa", p.TaskbarWidgets);
            if (TaskbarViewProfiles.ReadOption("ShowTaskViewButton") != p.TaskbarTaskView)
                TaskbarViewProfiles.ApplyOption("ShowTaskViewButton", p.TaskbarTaskView);
            if (TaskbarViewProfiles.ReadOption("SearchboxTaskbarMode") != p.TaskbarSearch)
                TaskbarViewProfiles.ApplyOption("SearchboxTaskbarMode", p.TaskbarSearch);
        }
        var s = AppSettings.Load();
        bool changed = s.HotCornersEnabled != p.HotCornersEnabled ||
            s.HotCornerTopLeft != p.TopLeft || s.HotCornerTopRight != p.TopRight ||
            s.HotCornerBottomLeft != p.BottomLeft || s.HotCornerBottomRight != p.BottomRight ||
            s.HotCornerDwellMilliseconds != p.DwellMs ||
            s.HotCornerEdgePixels != p.EdgePixels ||
            s.HotCornersPauseOnFullscreen != p.PauseOnFullscreen;
        if (!changed) return;
        s.HotCornersEnabled = p.HotCornersEnabled;
        s.HotCornerTopLeft = p.TopLeft; s.HotCornerTopRight = p.TopRight;
        s.HotCornerBottomLeft = p.BottomLeft; s.HotCornerBottomRight = p.BottomRight;
        s.HotCornerDwellMilliseconds = p.DwellMs;
        s.HotCornerEdgePixels = p.EdgePixels;
        s.HotCornersPauseOnFullscreen = p.PauseOnFullscreen;
        s.Save();
    }

    private static void Validate(Preset p)
    {
        if (p.Schema != 1 || string.IsNullOrWhiteSpace(p.Name)) throw new InvalidDataException("Unsupported preset version.");
        SafeName(p.Name);
        if (p.HideFileExt is not (0 or 1) || p.Hidden is not (1 or 2) ||
            p.CompactMode is not (0 or 1) || p.ExplorerLaunchTo is not (1 or 2) ||
            p.TaskbarAlignment is not (0 or 1) || p.TaskbarWidgets is not (0 or 1) ||
            p.TaskbarTaskView is not (0 or 1) || p.TaskbarSearch < 0 || p.TaskbarSearch > 3 ||
            p.DwellMs < 300 || p.DwellMs > 2500 || p.EdgePixels < 2 || p.EdgePixels > 20)
            throw new InvalidDataException("Preset values out of range.");
        static bool ActionOk(string? action) => action is "None" or "OpenOpsis" or "ShowDesktop" or "TaskView";
        if (!ActionOk(p.TopLeft) || !ActionOk(p.TopRight) || !ActionOk(p.BottomLeft) || !ActionOk(p.BottomRight))
            throw new InvalidDataException("Unrecognized Hot Corner action.");
    }
}
