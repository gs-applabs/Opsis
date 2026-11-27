using System.Text.Json;
using MotionDesk.Widgets;

namespace MotionDesk.Services;

public sealed class WorkspaceProfile
{
    public string Name { get; set; } = "Default";
    public AppSettings AppSettings { get; set; } = new();
    public WallpaperSettingsSnapshot Wallpaper { get; set; } = new();
    public List<WidgetSnapshot> Widgets { get; set; } = new();
    public List<ProfileMonitor> Monitors { get; set; } = new();
}

public sealed class ProfileMonitor
{
    public string Id { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public int Width { get; set; }
    public int Height { get; set; }
}

public sealed class WallpaperSettingsSnapshot
{
    public string VideoPath { get; set; } = string.Empty;
    public string PerformanceMode { get; set; } = "Balanced";
    public bool Enabled { get; set; }
}

public static class WorkspaceProfileService
{
    /// <summary>Raised only after the full transaction (including widgets and wallpaper) succeeded.</summary>
    public static event Action<string>? ProfileActivated;
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

    internal static bool MayRestoreSession(bool safeMode, bool restoreEnabled, bool exists) =>
        !safeMode && restoreEnabled && exists;

    internal static bool MaySaveLastSession(bool safeMode, bool alreadySaved) =>
        !safeMode && !alreadySaved;

    internal static void RestoreStartupSession()
    {
        if (SafeModeService.IsSafeMode) return;
        var saved = AppSettings.Load();
        if (MayRestoreSession(false, saved.RestoreLastSession, File.Exists(Path.Combine(Root, "Last Session.json"))))
        {
            try { Load("Last Session"); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
        }
        else if (saved.ShowSystemMonitor && !WidgetHostEngine.Instance.GetActiveWidgets().Any(w => w.WidgetId == "sysmon"))
            WidgetHostEngine.Instance.SpawnWidget("sysmon", 100, 100, 300, 200);
    }

    internal static void SaveLastSessionOnce()
    {
        if (!MaySaveLastSession(SafeModeService.IsSafeMode, ExitSaveDone)) return;
        Save("Last Session");
        ExitSaveDone = true;
    }

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
            Widgets = WidgetHostEngine.Instance.GetSnapshots().ToList(),
            Monitors = MonitorTopologyService.Current().Select(x => new ProfileMonitor
            { Id=x.Id, DeviceName=x.DeviceName, Width=x.Bounds.Width, Height=x.Bounds.Height }).ToList()
        };

        MotionDesk.Services.AtomicFile.WriteAllText(Path.Combine(Root, name + ".json"), JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static bool Load(string name)
    {
        var path = Path.Combine(Root, Sanitize(name) + ".json");
        if (!File.Exists(path))
        {
            VerifiedRollbackService.NoteProfileFailed(name);
            return false;
        }
        WorkspaceProfile? profile;
        try
        {
            // A damaged profile may be restored from .bak by AtomicFile.ReadAllText.
            if (new FileInfo(path).Length > 16L * 1024 * 1024)
            {
                VerifiedRollbackService.NoteProfileFailed(name);
                return false;
            }
            profile = JsonSerializer.Deserialize<WorkspaceProfile>(AtomicFile.ReadAllText(path));
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            VerifiedRollbackService.NoteProfileFailed(name);
            return false;
        }
        if (profile?.AppSettings == null || profile.Wallpaper == null || profile.Widgets == null)
        {
            VerifiedRollbackService.NoteProfileFailed(name);
            return false;
        }

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
            UiResourceAudit.Mark("profile-activated", name);
            try { ProfileActivated?.Invoke(name); }
            catch (Exception ex) { AttachLog.Write("Profile accessibility announcement failed: " + ex.GetType().Name); }
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
            VerifiedRollbackService.NoteProfileFailed(name);
            return false;
        }
    }

    /// <summary>Export the selected saved profile. Data is never overwritten without SaveFileDialog confirmation.</summary>
    public static void ExportPortable(string name, string destination)
    {
        var source = Path.Combine(Root, Sanitize(name) + ".json");
        if (!File.Exists(source)) throw new FileNotFoundException("Profile not found", source);
        File.Copy(source, destination, true);
    }

    /// <summary>Import into local profile storage, remapping only monitor identities. Does not activate the profile.</summary>
    public static string ImportPortable(string sourcePath, IReadOnlyDictionary<string,string> map)
    {
        var source = new FileInfo(sourcePath);
        if (!source.Exists || source.Length is < 2 or > 8 * 1024 * 1024) throw new InvalidDataException("Invalid profile size");
        var profile = JsonSerializer.Deserialize<WorkspaceProfile>(File.ReadAllText(sourcePath))
            ?? throw new InvalidDataException("Missing profile data");
        if (profile.AppSettings == null || profile.Wallpaper == null || profile.Widgets == null || profile.Widgets.Count > 120)
            throw new InvalidDataException("Invalid profile content");
        var connected = MonitorTopologyService.Current();
        foreach (var widget in profile.Widgets)
        {
            if (widget.Width is < 1 or > 2400 || widget.Height is < 1 or > 1600) throw new InvalidDataException("Invalid widget dimensions");
            if (widget.MonitorId is { Length: > 0 } old && map.TryGetValue(old, out var assigned))
            {
                var match = connected.FirstOrDefault(x=>x.Id.Equals(assigned,StringComparison.OrdinalIgnoreCase));
                if (match == null) throw new InvalidDataException("Monitor mapping is not connected");
                widget.MonitorId=match.Id; widget.MonitorDevice=match.DeviceName;
                widget.MonitorWidth=match.Bounds.Width; widget.MonitorHeight=match.Bounds.Height;
            }
        }
        profile.Monitors=connected.Select(x=>new ProfileMonitor
            { Id=x.Id,DeviceName=x.DeviceName,Width=x.Bounds.Width,Height=x.Bounds.Height }).ToList();
        var basename=Sanitize(profile.Name);
        if (basename.StartsWith("__opsis",StringComparison.OrdinalIgnoreCase)) basename="Imported";
        basename=basename.Length>70 ? basename[..70] : basename;
        // Never silently overwrite an import made in the same second.
        var suffix=DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8];
        var name=$"{basename}-import-{suffix}";
        Directory.CreateDirectory(Root);
        AtomicFile.WriteAllText(Path.Combine(Root,name+".json"),JsonSerializer.Serialize(profile));
        return name;
    }

    public static IReadOnlyList<ProfileMonitor> PreviewPortableMonitors(string sourcePath)
    {
        var source = new FileInfo(sourcePath);
        if (!source.Exists || source.Length is < 2 or > 8 * 1024 * 1024) throw new InvalidDataException("Invalid profile size");
        return (JsonSerializer.Deserialize<WorkspaceProfile>(File.ReadAllText(sourcePath))?.Monitors ?? new()).Take(16).ToArray();
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
