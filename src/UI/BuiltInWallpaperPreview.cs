using System.Drawing.Drawing2D;
using MotionDesk.Services;
using MotionDesk.Widgets;

namespace MotionDesk.UI;

internal sealed class BuiltInWallpaperPreview : IDisposable
{
    private static readonly (double X, double Y, double Size, double Phase)[] Particles = BuildParticles();
    private readonly string _mode;
    private Color _background, _wave, _peak, _glow;
    private Image? _desktop;
    private string _style = "";
    private double _speed = 1, _intensity = 1, _thickness = 1;

    internal bool Animated => _mode != "Desktop";
    internal static BuiltInWallpaperPreview? Create(string mode) =>
        mode is "Waves" or "Particles" or "Desktop" ? new BuiltInWallpaperPreview(mode) : null;

    private BuiltInWallpaperPreview(string mode)
    {
        _mode = mode;
        if (Animated) { RefreshPalette(); return; }

        var info = DesktopWallpaperReader.Read();
        _background = ColorTranslator.FromHtml(info.Color);
        _style = info.Style;
        if (string.IsNullOrEmpty(info.Uri)) return;
        try
        {
            using var file = new FileStream(new Uri(info.Uri).LocalPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var source = Image.FromStream(file);
            var scale = Math.Min(1d, 720d / Math.Max(source.Width, source.Height));
            _desktop = new Bitmap(source, Math.Max(1, (int)(source.Width * scale)), Math.Max(1, (int)(source.Height * scale)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Runtime.InteropServices.ExternalException)
        {
            // A Windows slideshow can replace the file while the gallery is opening.
        }
    }

    internal void RefreshPalette()
    {
        if (!Animated) return;
        var settings = WallpaperSettings.Load();
        _style = settings.WaveStyle;
        var palette = WavePalettes.Resolve(settings.PaletteName, settings.ThemeMode == "Light" ||
            (settings.ThemeMode != "Dark" && ThemeService.IsLightTheme()));
        _background = ColorTranslator.FromHtml(palette.Background);
        _wave = ColorTranslator.FromHtml(palette.WaveColor);
        _peak = ColorTranslator.FromHtml(palette.PeakColor);
        _glow = ColorTranslator.FromHtml(palette.GlowColor);
        _speed = settings.WaveSpeed;
        _intensity = settings.GlowIntensity;
        _thickness = settings.LineThickness;
    }

    internal void Draw(Graphics g, Rectangle bounds, double time)
    {
        using var bg = new SolidBrush(_background);
        g.FillRectangle(bg, bounds);
        if (_mode == "Desktop") { DrawDesktop(g, bounds); return; }

        time *= _speed;
        if (_mode == "Particles")
        {
            using var halo = new SolidBrush(Color.FromArgb(35, _glow));
            using var dot = new SolidBrush(_peak);
            foreach (var p in Particles)
            {
                float x = bounds.Left + (float)(p.X + Math.Sin(time * .7 + p.Phase) * .018) * bounds.Width;
                float y = bounds.Top + (float)((p.Y - time * (.025 + p.Size * .004) % 1 + 1) % 1) * bounds.Height;
                float size = (float)p.Size * bounds.Width / 245;
                halo.Color = Color.FromArgb((int)Math.Clamp(42 * _intensity, 0, 125), _glow);
                dot.Color = Color.FromArgb((int)(155 + 85 * Math.Sin(time * 1.6 + p.Phase)), _peak);
                g.FillEllipse(halo, x - size * 2, y - size * 2, size * 4, size * 4);
                g.FillEllipse(dot, x - size * .5f, y - size * .5f, size, size);
            }
            return;
        }

        if (_style == "TechGrid") DrawGrid(g, bounds, time);
        else DrawRibbons(g, bounds, time);
    }

    private void DrawRibbons(Graphics g, Rectangle b, double time)
    {
        using var fill = new LinearGradientBrush(b, Color.FromArgb(175, _wave), Color.FromArgb(15, _background), 90f);
        using var edge = new Pen(_peak, (float)Math.Clamp(_thickness * 1.35, .6, 4));
        using var glow = new Pen(Color.FromArgb((int)Math.Clamp(55 * _intensity, 0, 150), _glow), 4);
        var points = new PointF[81];
        for (int i = 0; i < 4; i++)
        {
            double phase = time * (.67 + i * .25);
            edge.Color = Color.FromArgb(205 - i * 24, i % 2 == 0 ? _peak : _glow);
            for (int j = 0; j < points.Length; j++)
            {
                double x = (double)j / (points.Length - 1);
                double wave = .56 * Math.Sin(x * 7.4 + phase) + .29 * Math.Sin(x * 13.68 - phase * 1.3) + .15 * Math.Sin(x * 3.26 + phase * .6 + i);
                double baseline = _style == "Aurora" ? .32 + i * .105 : .45 + i * .092;
                points[j] = new PointF(b.Left + (float)x * b.Width, b.Top + (float)(baseline - wave * (.072 + i * .028)) * b.Height);
            }
            using var path = new GraphicsPath();
            path.AddLines(points);
            path.AddLine(points[^1], new PointF(b.Right, b.Bottom));
            path.AddLine(b.Right, b.Bottom, b.Left, b.Bottom);
            path.CloseFigure();
            g.FillPath(fill, path);
            g.DrawLines(glow, points);
            g.DrawLines(edge, points);
        }
    }

    private void DrawGrid(Graphics g, Rectangle bounds, double time)
    {
        float horizon = bounds.Top + bounds.Height * .49f;
        float center = bounds.Left + bounds.Width * .5f;
        using var guide = new Pen(Color.FromArgb(150, _glow));
        for (int i = -8; i <= 8; i++) g.DrawLine(guide, center + i * 3, horizon, center + i * bounds.Width / 7f, bounds.Bottom);
        for (int i = 0; i < 14; i++)
        {
            float y = horizon + (float)Math.Pow((i + time % 1) / 14, 2.25) * (bounds.Bottom - horizon);
            g.DrawLine(guide, bounds.Left, y, bounds.Right, y);
        }
    }

    private void DrawDesktop(Graphics g, Rectangle b)
    {
        if (_desktop == null) return;
        float fill = Math.Max((float)b.Width / _desktop.Width, (float)b.Height / _desktop.Height);
        float fit = Math.Min((float)b.Width / _desktop.Width, (float)b.Height / _desktop.Height);
        switch (_style)
        {
            case "Stretch":
                g.DrawImage(_desktop, b);
                break;
            case "Tile":
                float scale = Math.Min(1, fit * .65f);
                for (float x = b.Left; x < b.Right; x += Math.Max(1, _desktop.Width * scale))
                    for (float y = b.Top; y < b.Bottom; y += Math.Max(1, _desktop.Height * scale))
                        g.DrawImage(_desktop, x, y, _desktop.Width * scale, _desktop.Height * scale);
                break;
            default:
                float zoom = _style is "Fit" or "Center" ? fit : fill;
                float w = _desktop.Width * zoom, h = _desktop.Height * zoom;
                g.DrawImage(_desktop, b.Left + (b.Width - w) / 2, b.Top + (b.Height - h) / 2, w, h);
                break;
        }
    }

    private static (double, double, double, double)[] BuildParticles()
    {
        var random = new Random(2510);
        return Enumerable.Range(0, 70).Select(_ =>
            (random.NextDouble(), random.NextDouble(), 1 + random.NextDouble() * 2, random.NextDouble() * Math.Tau)).ToArray();
    }

    public void Dispose() => _desktop?.Dispose();
}
