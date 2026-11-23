using System.Drawing.Drawing2D;
using MotionDesk.Containers;
using MotionDesk.Services;
using MotionDesk.Widgets;
using Opsis.Containers;

namespace MotionDesk.UI;

internal sealed class ContainerWorkspacePreview : Control
{
    private readonly ContainerHost _host;
    private readonly bool _single;
    private readonly Func<WindowSkinColors> _colors;
    private readonly ContainerIcons _icons = new();
    private readonly Image? _wallpaper;
    private IReadOnlyList<Container> _models = [];
    private readonly List<(Container Model, Rectangle Screen, Rectangle Preview)> _hitRegions = [];
    private Point? _dragStart;
    private string? _dragId;
    private Rectangle _dragBounds;
    private Point _delta;
    internal Screen Monitor { get; set; } = Screen.PrimaryScreen ?? Screen.AllScreens[0];
    internal string? SelectedId { get; private set; }
    internal event Action<string>? ContainerSelected;

    internal ContainerWorkspacePreview(ContainerHost host, bool single, Func<WindowSkinColors> colors)
    {
        _host = host;
        _single = single;
        _colors = colors;
        DoubleBuffered = true;
        ResizeRedraw = true;
        BackColor = UiTheme.PageBackground;
        AccessibleName = MainWindow.StudioText("Desktop layout preview", "Προεπισκόπηση διάταξης επιφάνειας");
        _icons.Loaded += Invalidate;
        _wallpaper = LoadBackdrop();
    }

    internal void UpdateModels(IReadOnlyList<Container> models, string? selected)
    {
        _models = models;
        SelectedId = selected;
        Invalidate();
    }

    private DesktopPreviewGeometry Geometry => new(Monitor.WorkingArea, Rectangle.Inflate(ClientRectangle, -5, -5));

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var palette = _colors();
        g.Clear(palette.Background);
        var frame = _single ? Rectangle.Inflate(ClientRectangle, -5, -5) : Geometry.Fit;
        _hitRegions.Clear();
        if (frame.Width < 10 || frame.Height < 10) return;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var mask = UiTheme.RoundedPath(frame, 12);
        var clip = g.Save();
        g.SetClip(mask);
        using (var bg = new LinearGradientBrush(frame, palette.Navigation, Blend(palette.Surface, palette.Accent, .26f), 42))
            g.FillRectangle(bg, frame);
        if (_wallpaper != null && !SystemInformation.HighContrast)
        {
            double scale = Math.Max((double)frame.Width / _wallpaper.Width, (double)frame.Height / _wallpaper.Height);
            var width = (int)Math.Ceiling(_wallpaper.Width * scale);
            var height = (int)Math.Ceiling(_wallpaper.Height * scale);
            g.DrawImage(_wallpaper, new Rectangle(frame.X + (frame.Width - width) / 2, frame.Y + (frame.Height - height) / 2, width, height));
        }
        var monitors = Screen.AllScreens.Select(s => new MonitorInfo(s.DeviceName, s.Bounds, s.Primary)).ToList();
        var models = _single ? _models.Where(c => c.Id == SelectedId) : _models.OrderBy(c => c.Id == SelectedId);
        foreach (var model in models)
        {
            var location = LegacyImporter.Denormalize(model.Geometry, monitors);
            var actual = new Rectangle(location.X, location.Y, Math.Max(80, location.Width), Math.Max(60, location.Height));
            if (!_single && !actual.IntersectsWith(Monitor.WorkingArea)) continue;
            var placed = actual;
            if (model.Id == _dragId)
                placed.Location = Geometry.Move(actual, _delta);
            Rectangle preview = _single
                ? DesktopPreviewGeometry.FitInside(actual.Size, Rectangle.Inflate(frame, -Math.Min(28, frame.Width / 6), -Math.Min(26, frame.Height / 8)))
                : Geometry.ToPreview(placed);
            if (preview.Width < 2 || preview.Height < 2) continue;
            _hitRegions.Add((model, actual, Rectangle.Intersect(frame, preview)));
            PaintContainer(g, model, preview, Math.Clamp((float)preview.Width / actual.Width, .30f, 1.25f));
        }
        if (_single && _hitRegions.Count == 0)
            TextRenderer.DrawText(g, MainWindow.StudioText("Select a container", "Επίλεξε container"), UiTheme.FontBody,
                frame, palette.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        g.Restore(clip);
        using var outline = new Pen(palette.Border);
        g.DrawPath(outline, mask);
    }

    private void PaintContainer(Graphics g, Container model, Rectangle bounds, float scale)
    {
        var palette = _colors();
        bool selected = model.Id == SelectedId;
        using var shape = UiTheme.RoundedPath(bounds, 8);
        Color surface = ContainerRenderer.ParseColor(model.Appearance.FrameColor, palette.Surface);
        if (SystemInformation.HighContrast) surface = SystemColors.Window;
        using var fill = new SolidBrush(Color.FromArgb(model.Hidden && !_single ? 145 : 236, surface));
        using var border = new Pen(selected ? palette.Accent : palette.Border, selected ? 2 : 1)
        { DashStyle = model.Hidden && !_single ? DashStyle.Dash : DashStyle.Solid };
        g.FillPath(fill, shape);
        g.DrawPath(border, shape);
        if (bounds.Width < 22 || bounds.Height < 19) return;
        var oldClip = g.Save();
        g.SetClip(shape, CombineMode.Intersect);
        int titleHeight = Math.Clamp((int)Math.Round(model.TitleBar.Height * scale), 15, bounds.Height);
        Color tint = ContainerRenderer.ParseColor(model.TitleBar.Color, Blend(surface, palette.Accent, .25f));
        if (SystemInformation.HighContrast) tint = SystemColors.Window;
        using var titleBrush = new SolidBrush(Color.FromArgb(Math.Clamp(model.TitleBar.Opacity * 255 / 100, 0, 255), tint));
        g.FillRectangle(titleBrush, bounds.X, bounds.Y, bounds.Width, titleHeight);
        Color heading = ContainerRenderer.ParseColor(model.TitleBar.TextColor,
            tint.GetBrightness() > .63f ? Color.FromArgb(22, 27, 34) : Color.White);
        if (SystemInformation.HighContrast) heading = SystemColors.WindowText;
        using var titleFont = new Font(model.TitleBar.FontFamily ?? UiTheme.FontBody.FontFamily.Name,
            Math.Clamp(model.TitleBar.FontSize * scale, 7f, 23f), model.TitleBar.Bold ? FontStyle.Bold : FontStyle.Regular);
        TextRenderer.DrawText(g, model.Title, titleFont,
            new Rectangle(bounds.Left + 5, bounds.Top, Math.Max(1, bounds.Width - 10), titleHeight), heading,
            TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        if ((_single || !model.RolledUp) && bounds.Height > titleHeight + 7 && bounds.Width > 14)
        {
            if (model.Kind == ContainerKind.Note)
                TextRenderer.DrawText(g, model.Source ?? "", UiTheme.FontBody, Rectangle.FromLTRB(bounds.Left + 6, bounds.Top + titleHeight + 3, bounds.Right - 6, bounds.Bottom - 3),
                    surface.GetBrightness() > .63f ? Color.Black : Color.White, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            else
            {
                var live = _host.Windows.FirstOrDefault(w => w.Model.Id == model.Id);
                var items = live?.Items ?? (model.TabsEnabled && model.Tabs.Count > 0
                    ? model.Tabs[Math.Clamp(model.CurrentTab, 0, model.Tabs.Count - 1)].Items : model.Items);
                if (model.Kind == ContainerKind.Portal && live == null && !string.IsNullOrWhiteSpace(model.Source))
                    TextRenderer.DrawText(g, model.Source, UiTheme.FontBody, Rectangle.FromLTRB(bounds.Left + 6, bounds.Top + titleHeight + 3, bounds.Right - 6, bounds.Bottom - 3),
                        palette.Text, TextFormatFlags.EndEllipsis);
                int size = ContainerItemLayout.ViewOf(model) is "list" or "details" ? 20 : model.Appearance.IconSize;
                int icon = Math.Clamp((int)Math.Round((size > 0 ? size : 48) * scale), 12, 62);
                int columns = ContainerItemLayout.ViewOf(model) is "list" or "details" ? 1 : Math.Max(1, (bounds.Width - 12) / (icon + 12));
                int rows = Math.Max(0, (bounds.Height - titleHeight - 6) / (icon + 6));
                for (int i = 0; i < Math.Min(items.Count, Math.Min(36, columns * rows)); i++)
                {
                    var entry = items[i];
                    int x = bounds.Left + 6 + i % columns * (icon + 12);
                    int y = bounds.Top + titleHeight + 5 + i / columns * (icon + 6);
                    g.DrawImage(_icons.Get(entry.Path, icon), new Rectangle(x, y, icon, icon));
                    if (columns == 1 && bounds.Width > icon + 49)
                        TextRenderer.DrawText(g, string.IsNullOrEmpty(entry.DisplayName) ? Path.GetFileName(entry.Path) : entry.DisplayName, UiTheme.FontBody,
                            new Rectangle(x + icon + 5, y, bounds.Right - x - icon - 10, icon), palette.Text,
                            TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                }
            }
        }
        g.Restore(oldClip);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (_single || e.Button != MouseButtons.Left) return;
        var hit = _hitRegions.FindLast(item => item.Preview.Contains(e.Location));
        if (hit.Model is not { } model) return;
        ContainerSelected?.Invoke(model.Id);
        if (model.Locked) return;
        _dragId = model.Id;
        _dragBounds = hit.Screen;
        _dragStart = e.Location;
        Capture = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragStart is not Point start) return;
        _delta = new Point(e.X - start.X, e.Y - start.Y);
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_dragId is not string id) return;
        var moved = Geometry.Move(_dragBounds, _delta);
        bool dragged = Math.Abs(_delta.X) >= 3 || Math.Abs(_delta.Y) >= 3;
        CancelDrag();
        if (dragged && moved != _dragBounds.Location) _host.MoveFromPreview(id, moved);
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (!Capture) CancelDrag();
    }

    private void CancelDrag()
    {
        _dragStart = null;
        _dragId = null;
        _delta = Point.Empty;
        if (Capture) Capture = false;
        Invalidate();
    }

    private static Image? LoadBackdrop()
    {
        if (SystemInformation.HighContrast) return null;
        try
        {
            var settings = WallpaperSettings.Load();
            string? path = WallpaperHostEngine.Instance.IsEnabled ? settings.Mode switch
            {
                "Image" => settings.ImagePath,
                WallpaperSettings.AthensMode => Path.Combine(AppContext.BaseDirectory, "widgets", "wallpaper", "ancient-athens", "day.jpg"),
                _ => null
            } : null;
            if (string.IsNullOrWhiteSpace(path) && Uri.TryCreate(DesktopWallpaperReader.Read().Uri, UriKind.Absolute, out var uri) && uri.IsFile)
                path = uri.LocalPath;
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            using var source = Image.FromFile(path);
            var thumbnail = DesktopPreviewGeometry.FitInside(source.Size, new Rectangle(0, 0, 1280, 800));
            return new Bitmap(source, thumbnail.Size);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Runtime.InteropServices.ExternalException)
        {
            return null;
        }
    }

    private static Color Blend(Color first, Color second, float weight) => Color.FromArgb(
        (int)(first.R + (second.R - first.R) * weight), (int)(first.G + (second.G - first.G) * weight), (int)(first.B + (second.B - first.B) * weight));

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _wallpaper?.Dispose(); _icons.Dispose(); }
        base.Dispose(disposing);
    }
}
