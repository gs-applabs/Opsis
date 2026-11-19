using System.Reflection;
using MotionDesk.UI;
using MotionDesk.Widgets;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        CheckDesktopMenu();
        var path = Path.Combine(AppContext.BaseDirectory, "widgets", "wallpaper", "gallery");
        Directory.CreateDirectory(path);
        using (var poster = new Bitmap(480, 270))
        {
            using (var g = Graphics.FromImage(poster)) g.Clear(Color.DarkMagenta);
            poster.Save(Path.Combine(path, "aurora.jpg"), System.Drawing.Imaging.ImageFormat.Jpeg);
        }
        using (var strip = new Bitmap(1440,648))
        {
            using (var g = Graphics.FromImage(strip)) g.Clear(Color.MediumBlue);
            strip.Save(Path.Combine(path, "aurora.preview.jpg"), System.Drawing.Imaging.ImageFormat.Jpeg);
        }

        using var window = new MainWindow { ClientSize = new Size(1020, 760) };
        window.OpenGallery();
        window.Show();
        Pump(130);
        Check(window.ActivePage == "Wallpaper", "Wallpaper page opens");
        Button Tile(string asset) => window.Controls.Find("wallpaper-tile-" + asset, true).OfType<Button>().Single();
        Check(StudioWallpaperCatalog.Scenes.All(scene => Tile(scene.Asset).Visible), "All thirty preview tiles are mounted");
        var first = Tile("aurora");
        var second = Tile("nebula");
        Check(first.Top == second.Top && first.Right <= second.Left, "Tiles are laid out horizontally without overlap");
        var hover = first.GetType().GetMethod("OnMouseEnter", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var leave = first.GetType().GetMethod("OnMouseLeave", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var frame = first.GetType().GetField("_frame", BindingFlags.NonPublic | BindingFlags.Instance)!;
        Cursor.Position = first.PointToScreen(new Point(40, 40));
        hover.Invoke(first,[EventArgs.Empty]);
        Pump(350);
        Check((int)frame.GetValue(first)! > 0, "Hover preview advances frames");
        leave.Invoke(first,[EventArgs.Empty]);
        first.PerformClick();
        Check(WallpaperSettings.Load().Mode == "Studio Aurora" && WallpaperHostEngine.Instance.IsEnabled, "A scene button applies its wallpaper mode");
        Button Control(string name) => All(window).OfType<Button>().Single(button => button.Text == name);
        Control("Παύση").PerformClick();
        Check(WallpaperHostEngine.UserPaused, "Pause action updates the gallery");
        Control("Αναπαραγωγή").PerformClick();
        Check(!WallpaperHostEngine.UserPaused, "Resume action updates the gallery");
        Control("Επόμενο").PerformClick();
        Check(WallpaperSettings.Load().Mode == "Studio Nebula", "Next uses the wallpaper transport");
        Control("Προηγούμενο").PerformClick();
        Check(WallpaperSettings.Load().Mode == "Studio Aurora", "Previous returns to the original wallpaper");
        Control("Διακοπή").PerformClick();
        Check(!WallpaperHostEngine.Instance.IsEnabled, "Stop changes the gallery power state");
        foreach (string asset in new[] { "particles", "waves" })
        {
            var tile = Tile(asset);
            var fi = tile.GetType().GetField("_builtIn", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var preview = (BuiltInWallpaperPreview)fi.GetValue(tile)!;
            using var a = new Bitmap(360, 203);
            using var b = new Bitmap(360, 203);
            using (var g = Graphics.FromImage(a)) preview.Draw(g, new Rectangle(0, 0, a.Width, a.Height), 1.0);
            using (var g = Graphics.FromImage(b)) preview.Draw(g, new Rectangle(0, 0, b.Width, b.Height), 5.0);
            int diff = Enumerable.Range(0, 203).Sum(y => Enumerable.Range(0, 360).Count(x => a.GetPixel(x, y) != b.GetPixel(x, y)));
            Check(diff > 50, asset + " preview genuinely moves");
        }
        var desktop = (BuiltInWallpaperPreview)Tile("desktop").GetType().GetField("_builtIn", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(Tile("desktop"))!;
        using (var b = new Bitmap(300, 170))
        {
            using var g = Graphics.FromImage(b);
            desktop.Draw(g, new Rectangle(0, 0, 300, 170), 0);
            Check(b.GetPixel(150, 85).ToArgb() == ColorTranslator.FromHtml("#102942").ToArgb(), "Windows solid background preview uses system colour");
        }
        window.ClientSize = new Size(730, 640);
        Pump(100);
        Check(first.Width < 345 && first.Right <= second.Left, "Gallery adapts to a narrower window");
        Tile("weather").PerformClick();
        Check(WallpaperSettings.Load().Mode == "Weather", "Weather is reachable from the gallery");
        Tile("image").PerformClick();
        Check(WallpaperSettings.Load().Mode == "Image" && window.DetailsOpened, "Media opens its settings");
    }

    static IEnumerable<Control> All(Control control) =>
        control.Controls.Cast<Control>().SelectMany(child => new[] { child }.Concat(All(child)));

    static void CheckDesktopMenu()
    {
        using var root = Microsoft.Win32.Registry.CurrentUser;
        const string path = @"Software\Classes\DesktopBackground\Shell\OpsisStudio";
        try
        {
            MotionDesk.Services.DesktopMenu.Register(Environment.ProcessPath!);
            using var menu = root.OpenSubKey(path)!;
            Check((string?)menu.GetValue("MUIVerb") == "Opsis", "Explorer cascade is registered for the current user");
            using var verbs = menu.OpenSubKey(@"shell\02Wallpaper\shell")!;
            Check(verbs.GetSubKeyNames().Length == 4, "Explorer has all four wallpaper actions");
            foreach (var key in verbs.GetSubKeyNames())
            {
                using var cmd = verbs.OpenSubKey(key + @"\command")!;
                string quoted = (string)cmd.GetValue("")!;
                Check(quoted.StartsWith("\"" + Environment.ProcessPath + "\" --desktop-action "), "Verb points at the quoted installed executable");
            }
            MotionDesk.Services.DesktopMenu.Run("new-container");
            Check(MotionDesk.Containers.ContainerHost.Current.Count == 1, "Desktop action calls the normal container host");
        }
        finally { MotionDesk.Services.DesktopMenu.Unregister(); }
        Check(root.OpenSubKey(path) == null, "Explorer cascade unregisters cleanly");
    }

    static void Pump(int ms)
    {
        var end = Environment.TickCount64 + ms;
        while (Environment.TickCount64 < end) { Application.DoEvents(); Thread.Sleep(10); }
    }

    static void Check(bool result, string title)
    {
        if (!result) throw new InvalidOperationException(title);
        Console.WriteLine("PASS " + title);
    }
}
