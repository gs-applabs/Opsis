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
    private const string Handler = @"Software\Classes\DesktopBackground\shellex\ContextMenuHandlers\Opsis";
    private const string ClassicId = "{7F45B246-0BA9-4C86-AB71-09F969610F62}";
    private const string ComClass = @"Software\Classes\CLSID\" + ClassicId;
    private static readonly string[] OldVerbs = ["OpsisStudio", "OpsisStudioNewContainer", "OpsisStudioNextWallpaper"];

    public static string ExePath => Environment.ProcessPath ?? "";
    public static string PipeName => "opsis-studio-control-v1-" + Environment.UserName.ToLowerInvariant();
    public static string ContainerPipeName => "opsis-studio-containers-v1-" + Environment.UserName.ToLowerInvariant();

    public static readonly HashSet<string> Allowed = new(StringComparer.Ordinal)
    {
        "new-container", "prev-wallpaper", "next-wallpaper", "toggle-wallpaper", "stop-wallpaper",
        "prev-video", "next-video", "toggle-video", "ping"
    };

    // The classic Explorer uses IContextMenu. The signed identity package uses IExplorerCommand.
    public static void Register(string exe, bool? greekOverride = null)
    {
        if (!Path.IsPathFullyQualified(exe) || exe.Contains('"') || !File.Exists(exe))
            throw new ArgumentException("Invalid Opsis executable path.", nameof(exe));

        var extension = Path.Combine(Path.GetDirectoryName(exe)!, "OpsisExplorer.dll");
        if (!File.Exists(extension))
        {
            RegisterClassicCommands(exe, greekOverride ?? LocalizationManager.CurrentLanguage == "el");
            return;
        }

        using (var server = Registry.CurrentUser.CreateSubKey(ComClass + @"\InprocServer32"))
        {
            server.SetValue("", extension, RegistryValueKind.String);
            server.SetValue("ThreadingModel", "Apartment", RegistryValueKind.String);
        }
        using (var handler = Registry.CurrentUser.CreateSubKey(Handler))
        {
            handler.SetValue("", ClassicId, RegistryValueKind.String);
            handler.SetValue("Executable", exe, RegistryValueKind.String);
            handler.SetValue("Greek", (greekOverride ?? LocalizationManager.CurrentLanguage == "el") ? 1 : 0, RegistryValueKind.DWord);
        }
        RemoveOldVerbs();
        NotifyExplorer();
    }

    private static void RegisterClassicCommands(string exe, bool greek)
    {
        Registry.CurrentUser.DeleteSubKeyTree(Handler, false);
        Registry.CurrentUser.DeleteSubKeyTree(ComClass, false);
        RemoveOldVerbs();
        using var menu = Registry.CurrentUser.CreateSubKey(Root + "Opsis");
        menu.SetValue("MUIVerb", "Opsis");
        menu.SetValue("Icon", exe);
        menu.SetValue("SubCommands", "");
        (string Action, string Caption)[] commands =
        [
            ("new-container", greek ? "Νέο DeskContainer" : "New DeskContainer"),
            ("prev-wallpaper", greek ? "Προηγούμενο φόντο" : "Previous wallpaper"),
            ("next-wallpaper", greek ? "Επόμενο φόντο" : "Next wallpaper"),
            ("toggle-wallpaper", greek ? "Παύση / συνέχιση" : "Pause / resume"),
            ("stop-wallpaper", greek ? "Διακοπή" : "Stop wallpaper")
        ];
        for (int i = 0; i < commands.Length; i++)
        {
            using var item = menu.CreateSubKey($@"shell\{i + 1}-{commands[i].Action}");
            item.SetValue("MUIVerb", commands[i].Caption);
            using var command = item.CreateSubKey("command");
            command.SetValue("", $"\"{exe}\" --desktop-action {commands[i].Action}");
        }
        NotifyExplorer();
    }

    private static void RemoveOldVerbs()
    {
        foreach (var name in OldVerbs) Registry.CurrentUser.DeleteSubKeyTree(Root + name, false);
    }

    public static void Unregister()
    {
        Registry.CurrentUser.DeleteSubKeyTree(Handler, false);
        Registry.CurrentUser.DeleteSubKeyTree(ComClass, false);
        RemoveOldVerbs();
        NotifyExplorer();
    }

    public static bool IsRegistered()
    {
        try
        {
            using var menu = Registry.CurrentUser.OpenSubKey(Root + @"Opsis\shell\1-new-container\command");
            if (Equals(menu?.GetValue(""), $"\"{ExePath}\" --desktop-action new-container")) return true;
            using var key = Registry.CurrentUser.OpenSubKey(Handler);
            using var server = Registry.CurrentUser.OpenSubKey(ComClass + @"\InprocServer32");
            return Equals(key?.GetValue(""), ClassicId) && server?.GetValue("") is string dll && File.Exists(dll);
        }
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
