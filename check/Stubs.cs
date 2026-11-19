using System.Drawing.Drawing2D;
namespace MotionDesk.Services
{
    public static class LocalizationManager { public static string CurrentLanguage { get; set; } = "el"; }
    public static class RotatingLogger { public static void Write(string message) => Console.WriteLine(message); }
    public static class ThemeService { public static bool IsLightTheme() => false; }
    public static class DesktopWallpaperReader
    {
        public sealed record Info(string? Uri, string Style, string Color);
        public static Info Read() => new(null, "Fill", "#102942");
    }
}
namespace Opsis.Containers
{
    using System.Text.Json.Nodes;
    public interface IContainersApi { JsonNode? Handle(string cmd, JsonObject args); }
    public sealed class PipeException(string code, string description) : System.Exception(code + " " + description);
    public static class PipeClient
    {
        public static JsonObject Send(string pipe, string action, JsonObject? body, int timeout) => new() { ["ok"] = true };
    }
}
namespace MotionDesk.Containers
{
    public sealed class ContainerHost
    {
        public static ContainerHost Current { get; } = new();
        public int Count { get; private set; }
        public void NewContainerAtCursor(string kind = "Data", string? title = null) => Count++;
    }
}
namespace MotionDesk.Widgets
{
    public sealed class WallpaperSettings
    {
        public string Mode { get; set; } = "Waves";
        public string WaveStyle { get; set; } = "Ribbons";
        public string PaletteName { get; set; } = "";
        public string ThemeMode { get; set; } = "Dark";
        public double WaveSpeed { get; set; } = 1;
        public double GlowIntensity { get; set; } = 1;
        public double LineThickness { get; set; } = 1;
        public const string AthensMode = "Ancient Athens";
        private static readonly WallpaperSettings Settings = new();
        public static WallpaperSettings Load() => Settings;
    }
    public static class WavePalettes
    {
        public sealed record Palette(string Background, string WaveColor, string PeakColor, string GlowColor);
        public static Palette Resolve(string name, bool light) => new("#0a1436", "#2461b0", "#50d4ff", "#abe8fa");
    }
    public sealed class WallpaperHostEngine
    {
        public static WallpaperHostEngine Instance { get; } = new();
        public bool IsEnabled { get; private set; }
        public static bool UserPaused { get; private set; }
        public event Action? StateChanged;
        public void TogglePause() { UserPaused = !UserPaused; StateChanged?.Invoke(); }
        public void PlayOrPauseWallpaper() { if (IsEnabled) TogglePause(); else Enable(); }
        public void StopWallpaper() { UserPaused = false; Disable(); }
        public void StepVideo(bool forward) => StepWallpaper(forward);
        public void StepWallpaper(bool forward)
        {
            var modes = StudioWallpaperCatalog.Scenes.Select(s => s.Mode).ToArray();
            int index = Array.IndexOf(modes, WallpaperSettings.Load().Mode);
            SetMode(modes[(index + modes.Length + (forward ? 1 : -1)) % modes.Length]);
        }

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
        private static FlowLayoutPanel AddCardSection(Control parent, string text, int width = 760)
        {
            var panel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            AddHeading(panel, text);
            parent.Controls.Add(panel);
            return panel;
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
