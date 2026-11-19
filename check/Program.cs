using Microsoft.Win32;
using MotionDesk.Services;
using MotionDesk.Widgets;

internal static class Check
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        const string path = @"Software\Classes\DesktopBackground\Shell\OpsisStudio";
        try
        {
            using (var old = Registry.CurrentUser.CreateSubKey(path)) old.SetValue("SubCommands", "");
            DesktopMenu.ApplyInstallerChoice(true, true);
            using var root = Registry.CurrentUser.OpenSubKey(path) ?? throw new Exception("Missing context menu");
            Require(root.GetValue("SubCommands") == null, "Old submenu registration removed");
            using var sub = root.OpenSubKey(@"ExtendedSubCommandsKey\shell");
            using var newContainer = sub?.OpenSubKey("01NewContainer");
            Require((string?)newContainer?.GetValue("MUIVerb") == "Νέο DeskContainer", "New DeskContainer is localized and registered");
            using var context = sub?.OpenSubKey("02Wallpaper");
            Require((string?)context?.GetValue("MUIVerb") == "Κινούμενο wallpaper", "Wallpaper title is localized and registered");
            using var commands = context?.OpenSubKey(@"ExtendedSubCommandsKey\shell");
            Require(commands?.GetSubKeyNames().Length == 4, "All four Explorer verbs are attached");
            string[] actions = ["prev-wallpaper", "next-wallpaper", "toggle-wallpaper", "stop-wallpaper"];
            var executable = Environment.ProcessPath;
            foreach (var action in actions)
            {
                Require(commands!.GetSubKeyNames().Any(key =>
                {
                    using var cmd = commands.OpenSubKey(key+@"\command");
                    return (string?)cmd?.GetValue("") == $"\"{executable}\" --desktop-action {action}";
                }), "Explorer dispatches " + action);
                DesktopMenu.Run(action);
            }
            Require(WallpaperHostEngine.Instance.Calls.SequenceEqual(new[]{"previous","next","pause","stop"}), "Four commands dispatch correctly");
            DesktopMenu.Run("new-container");
            Require(MotionDesk.Containers.ContainerHost.Current.Count == 1, "Container creation dispatches through real host method");
        }
        finally { DesktopMenu.ApplyInstallerChoice(false, true); }
        Require(!DesktopMenu.IsRegistered(), "Opt-out removes Explorer key");
        using var main = new Form { Text = "DeskFlip front", Width = 480, Height = 300 };
        using var secondary = new Form { Text = "DeskFlip behind", Width = 460, Height = 280, BackColor = Color.RoyalBlue };
        main.Show(); secondary.Show();
        Application.DoEvents();
        using var overlay = new DeskFlipOverlay();
        Require(overlay.HasWindows, "DeskFlip enumerates live top-level windows");
        overlay.Show();
        overlay.MoveSelection(1);
        var end = Environment.TickCount64 + 450;
        while (Environment.TickCount64 < end) { Application.DoEvents(); Thread.Sleep(10); }
        var timer = (System.Windows.Forms.Timer)typeof(DeskFlipOverlay).GetField("_timer", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(overlay)!;
        Require(!timer.Enabled, "Flip animation reaches its selection and stops its timer");
        overlay.Close();
    }

    private static void Require(bool condition, string title)
    {
        if (!condition) throw new InvalidOperationException(title);
        Console.WriteLine("PASS " + title);
    }
}
