using System.Text.Json.Nodes;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using MotionDesk.Containers;
using MotionDesk.Widgets;
using Opsis.Containers;

namespace MotionDesk.Services;

public static class DesktopMenu
{
    private const string Root = @"Software\Classes\DesktopBackground\Shell\";
    private const string MenuKey = Root + "OpsisStudio";
    private const string Submenu = @"ExtendedSubCommandsKey\shell";
    private static readonly string[] LegacyVerbs = ["OpsisStudioNewContainer", "OpsisStudioNextWallpaper"];
    private static readonly (string Key, string English, string Greek, string Action)[] Playback =
    [
        ("01Previous", "Previous", "Προηγούμενο", "prev-wallpaper"),
        ("02Next", "Next", "Επόμενο", "next-wallpaper"),
        ("03Pause", "Pause / resume", "Παύση / συνέχιση", "toggle-wallpaper"),
        ("04Stop", "Stop", "Διακοπή", "stop-wallpaper")
    ];

    public static string ExePath => Environment.ProcessPath ?? "";
    public static string PipeName => "opsis-studio-control-v1-" + Environment.UserName.ToLowerInvariant();
    public static string ContainerPipeName => "opsis-studio-containers-v1-" + Environment.UserName.ToLowerInvariant();

    public static readonly HashSet<string> Allowed = new(StringComparer.Ordinal)
    {
        "new-container", "prev-wallpaper", "next-wallpaper", "toggle-wallpaper", "stop-wallpaper",
        "prev-video", "next-video", "toggle-video", "ping"
    };

    // Static cascades are shown in the Windows classic desktop menu, including Show more options on Windows 11.
    public static void Register(string exe, bool? greekOverride = null)
    {
        if (!Path.IsPathFullyQualified(exe) || exe.Contains('"') || !File.Exists(exe))
            throw new ArgumentException("Invalid Opsis executable path.", nameof(exe));

        bool greek = greekOverride ?? (LocalizationManager.CurrentLanguage == "el");
        using var menu = Registry.CurrentUser.CreateSubKey(MenuKey);
        menu.SetValue("MUIVerb", "Opsis");
        menu.SetValue("Icon", "\"" + exe + "\"");
        menu.DeleteValue("SubCommands", false);
        menu.DeleteSubKeyTree("shell", false);
        using var shell = menu.CreateSubKey(Submenu);
        WriteVerb(shell, "01NewContainer", greek ? "Νέο DeskContainer" : "New DeskContainer", "new-container", exe);
        using var animation = shell.CreateSubKey("02Wallpaper");
        animation.SetValue("MUIVerb", greek ? "Κινούμενο wallpaper" : "Animated wallpaper");
        using var commands = animation.CreateSubKey(Submenu);
        foreach (var (key, english, greekText, action) in Playback)
            WriteVerb(commands, key, greek ? greekText : english, action, exe);
        RemoveLegacyVerbs();
        NotifyExplorer();
    }

    private static void WriteVerb(RegistryKey parent, string key, string title, string action, string exe)
    {
        using var verb = parent.CreateSubKey(key);
        verb.SetValue("MUIVerb", title);
        using var command = verb.CreateSubKey("command");
        command.SetValue("", "\"" + exe + "\" --desktop-action " + action);
    }

    private static void RemoveLegacyVerbs()
    {
        foreach (var key in LegacyVerbs) Registry.CurrentUser.DeleteSubKeyTree(Root + key, false);
    }

    public static void Unregister()
    {
        Registry.CurrentUser.DeleteSubKeyTree(MenuKey, false);
        RemoveLegacyVerbs();
        NotifyExplorer();
    }

    public static bool IsRegistered()
    {
        try { using var key = Registry.CurrentUser.OpenSubKey(MenuKey + "\\" + Submenu + @"\02Wallpaper"); return key != null; }
        catch (Exception) { return false; }
    }

    public static void Apply(bool enabled)
    {
        if (enabled) Register(ExePath); else Unregister();
    }

    internal static void ApplyInstallerChoice(bool enabled, bool greek)
    {
        if (enabled) Register(ExePath, greek); else Unregister();
        var settings = AppSettings.Load();
        settings.DesktopMenuEnabled = enabled;
        settings.Save();
    }

    private static void NotifyExplorer() => SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);

    public static bool Send(string action)
    {
        try { return PipeClient.Send(PipeName, action, null, 700)["ok"]?.GetValue<bool>() == true; }
        catch (Exception) { return false; }
    }

    internal static void Run(string action)
    {
        try
        {
            var wallpaper = WallpaperHostEngine.Instance;
            switch (action)
            {
                case "next-wallpaper": wallpaper.StepWallpaper(true); break;
                case "prev-wallpaper": wallpaper.StepWallpaper(false); break;
                case "toggle-wallpaper": wallpaper.PlayOrPauseWallpaper(); break;
                case "stop-wallpaper": wallpaper.StopWallpaper(); break;
                case "next-video": wallpaper.StepVideo(true); break;
                case "prev-video": wallpaper.StepVideo(false); break;
                case "toggle-video": wallpaper.TogglePause(); break;
                case "new-container":
                    ContainerHost.Current?.NewContainerAtCursor(title: LocalizationManager.CurrentLanguage == "el" ? "Νέο DeskContainer" : "New DeskContainer");
                    break;
            }
        }
        catch (Exception ex) { RotatingLogger.Write($"Desktop action '{action}' failed: {ex.Message}"); }
    }

    public sealed class Api : IContainersApi
    {
        private readonly SynchronizationContext _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();

        public JsonNode? Handle(string cmd, JsonObject args)
        {
            if (!Allowed.Contains(cmd)) throw new PipeException("unknown-command", "not allowed");
            if (cmd != "ping") _ui.Post(_ => Run(cmd), null);
            return new JsonObject { ["accepted"] = cmd };
        }
    }
}
