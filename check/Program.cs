using System.Reflection;
using MotionDesk.UI;
using MotionDesk.Widgets;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
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
        Check(StudioWallpaperCatalog.Scenes.All(scene => Tile(scene.Asset).Visible), "All ten preview tiles are mounted");
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
        window.ClientSize = new Size(730, 640);
        Pump(100);
        Check(first.Width < 345 && first.Right <= second.Left, "Gallery adapts to a narrower window");
        Tile("weather").PerformClick();
        Check(WallpaperSettings.Load().Mode == "Weather", "Weather is reachable from the gallery");
        Tile("image").PerformClick();
        Check(WallpaperSettings.Load().Mode == "Image" && window.DetailsOpened, "Media opens its settings");
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
