using System.Text.Json;
using MotionDesk.Widgets;

namespace MotionDesk.Services;

public sealed class WorkspaceProfile
{
    public string Name { get; set; } = "Default";
    public AppSettings AppSettings { get; set; } = new();
    public WallpaperSettingsSnapshot Wallpaper { get; set; } = new();
    public List<WidgetSnapshot> Widgets { get; set; } = new();
}

public sealed class WallpaperSettingsSnapshot
{
    public string VideoPath { get; set; } = string.Empty;
    public string PerformanceMode { get; set; } = "Balanced";
    public bool Enabled { get; set; }
}

public static class WorkspaceProfileService
{
    private static string Root => Path.Combine(AppPaths.AppDataFolder, "profiles");

    public static IReadOnlyList<string> ListProfiles()
    {
        Directory.CreateDirectory(Root);
        return Directory.GetFiles(Root, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .OrderBy(x => x)
            .ToArray()!;
    }

    // True αφού αποθηκεύτηκε το "Last Session" στην έξοδο (tray Exit) ΠΡΙΝ κλείσουν τα παράθυρα. Το
    // Application.Exit() κλείνει τα Forms με τυχαία σειρά — αν τα widgets έκλειναν πρώτα, το αποθηκευμένο
    // "Last Session" του MainWindow.FormClosed ήταν ΑΔΕΙΟ και τα widgets/containers χάνονταν στην επόμενη εκκίνηση.
    public static bool ExitSaveDone { get; set; }

    public static void Save(string name)
    {
        name = Sanitize(name);
        Directory.CreateDirectory(Root);
        var wallpaper = WallpaperSettings.Load();
        var profile = new WorkspaceProfile
        {
            Name = name,
            AppSettings = AppSettings.Load(),
            Wallpaper = new WallpaperSettingsSnapshot
            {
                VideoPath = wallpaper.VideoPath,
                PerformanceMode = wallpaper.PerformanceMode,
                Enabled = WallpaperHostEngine.Instance.IsEnabled
            },
            Widgets = WidgetHostEngine.Instance.GetSnapshots().ToList()
        };

        MotionDesk.Services.AtomicFile.WriteAllText(Path.Combine(Root, name + ".json"), JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static bool Load(string name)
    {
        var path = Path.Combine(Root, Sanitize(name) + ".json");
        if (!File.Exists(path)) return false;
        WorkspaceProfile? profile;
        try
        {
            // A damaged profile may be restored from .bak by AtomicFile.ReadAllText.
            if (new FileInfo(path).Length > 16L * 1024 * 1024) return false;
            profile = JsonSerializer.Deserialize<WorkspaceProfile>(AtomicFile.ReadAllText(path));
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { return false; }
        if (profile?.AppSettings == null || profile.Wallpaper == null || profile.Widgets == null) return false;

        // Snapshot the live state before writing ANY new settings. If a widget/plugin throws
        // midway through a profile switch, restore both disk settings and the visible workspace.
        AppSettings currentApp;
        WallpaperSettings wallpaper;
        IReadOnlyList<WidgetSnapshot> previousWidgets;
        bool previousEnabled;
        try
        {
            currentApp = AppSettings.Load();
            wallpaper = WallpaperSettings.Load();
            previousWidgets = WidgetHostEngine.Instance.GetSnapshots().ToArray();
            previousEnabled = WallpaperHostEngine.Instance.IsEnabled;
        }
        catch (Exception ex) { AttachLog.Write("Profile snapshot failed: " + ex.GetType().Name); return false; }

        bool oldGridSnap = currentApp.GridSnap;
        int oldThreshold = currentApp.SnapThreshold, oldGridSize = currentApp.GridSize;
        string oldVideo = wallpaper.VideoPath, oldPerformance = wallpaper.PerformanceMode;
        try
        {
            // Only workspace-specific values are imported: language, theme and global user
            // settings must not silently revert to the values saved by an older profile.
            currentApp.GridSnap = profile.AppSettings.GridSnap;
            currentApp.SnapThreshold = Math.Clamp(profile.AppSettings.SnapThreshold, 5, 50);
            currentApp.GridSize = Math.Clamp(profile.AppSettings.GridSize, 5, 100);
            currentApp.Save();

            wallpaper.VideoPath = profile.Wallpaper.VideoPath ?? string.Empty;
            wallpaper.PerformanceMode = profile.Wallpaper.PerformanceMode ?? "Balanced";
            wallpaper.Save();

            WidgetSnapEngine.EnableGridSnap = currentApp.GridSnap;
            WidgetSnapEngine.SnapThreshold = currentApp.SnapThreshold;
            WidgetSnapEngine.GridSize = currentApp.GridSize;
            WidgetHostEngine.Instance.RestoreSnapshots(profile.Widgets);
            WallpaperHostEngine.Instance.SetPerformanceMode(wallpaper.PerformanceMode);
            if (profile.Wallpaper.Enabled) WallpaperHostEngine.Instance.Enable();
            else WallpaperHostEngine.Instance.Disable();
            return true;
        }
        catch (Exception ex)
        {
            AttachLog.Write("Profile switch rolled back: " + ex.GetType().Name);
            try
            {
                currentApp.GridSnap = oldGridSnap;
                currentApp.SnapThreshold = oldThreshold;
                currentApp.GridSize = oldGridSize;
                currentApp.Save();
                wallpaper.VideoPath = oldVideo;
                wallpaper.PerformanceMode = oldPerformance;
                wallpaper.Save();
                WidgetSnapEngine.EnableGridSnap = oldGridSnap;
                WidgetSnapEngine.SnapThreshold = oldThreshold;
                WidgetSnapEngine.GridSize = oldGridSize;
                WidgetHostEngine.Instance.RestoreSnapshots(previousWidgets);
                WallpaperHostEngine.Instance.SetPerformanceMode(oldPerformance);
                if (previousEnabled) WallpaperHostEngine.Instance.Enable();
                else WallpaperHostEngine.Instance.Disable();
            }
            catch (Exception restoreEx)
            {
                AttachLog.Write("Profile rollback needs safe-mode recovery: " + restoreEx.GetType().Name);
            }
            return false;
        }
    }

    public static void Delete(string name)
    {
        var path = Path.Combine(Root, Sanitize(name) + ".json");
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string((value ?? "Default").Where(c => !invalid.Contains(c)).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(clean) ? "Default" : clean;
    }
}
