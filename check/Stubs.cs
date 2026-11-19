using System.Drawing.Drawing2D;
namespace MotionDesk.Services { }
namespace MotionDesk.Widgets
{
    public sealed class WallpaperSettings
    {
        public string Mode { get; set; } = "Waves";
        public const string AthensMode = "Ancient Athens";
        private static readonly WallpaperSettings Settings = new();
        public static WallpaperSettings Load() => Settings;
    }
    public sealed class WallpaperHostEngine
    {
        public static WallpaperHostEngine Instance { get; } = new();
        public bool IsEnabled { get; private set; }
        public event Action? StateChanged;
        public void Enable() { IsEnabled = true; StateChanged?.Invoke(); }
        public void Disable() { IsEnabled = false; StateChanged?.Invoke(); }
        public void SetMode(string mode) { WallpaperSettings.Load().Mode = mode; Enable(); }
    }
}
namespace MotionDesk.UI
{
    public sealed partial class MainWindow : Form
    {
        private string? _currentPageKey;
        public string? ActivePage => _currentPageKey;
        public bool DetailsOpened { get; private set; }
        private static bool StudioGreek => true;
        private static string WallpaperModeLabel(string value) => value;
        private static string StudioText(string english, string greek) => greek;
        private static FlowLayoutPanel CreatePagePanel() => new() { FlowDirection = FlowDirection.TopDown,
            WrapContents = false, Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(4) };
        private static Button StudioButton(string text, Action action, int width)
        {
            var button = new Button { Text = text, Width = width };
            button.Click += (_,_)=>action();
            return button;
        }
        private static void AddHeading(Control parent, string text) => parent.Controls.Add(new Label { Text = text, AutoSize = true });
        private static void AddSection(Control parent, string text) => AddHeading(parent,text);
        private void ShowWallpaperDetails() => DetailsOpened = true;
        private void SetPage(string key, Control control) => Controls.Add(control);
        public void OpenGallery() => ShowWallpaper();
    }
    internal static class UiTheme
    {
        internal static Color PageBackground => Color.FromArgb(15, 24, 38);
        internal static Color Surface => Color.FromArgb(20, 32, 53);
        internal static Color SurfaceHover => Color.FromArgb(30, 47, 68);
        internal static Color TextPrimary => Color.White;
        internal static Color TextSecondary => Color.Silver;
        internal static Color Border => Color.DimGray;
        internal static Color AccentCyan => Color.Cyan;
        internal static Font FontBody => SystemFonts.MessageBoxFont!;
        internal static Font FontSubheading => SystemFonts.MessageBoxFont!;
        internal static GraphicsPath RoundedPath(Rectangle box, int radius)
        {
            var path = new GraphicsPath(); path.AddRectangle(box); return path;
        }
    }
}
