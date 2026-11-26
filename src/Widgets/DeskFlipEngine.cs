using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using MotionDesk.Services;

namespace MotionDesk.Widgets;

public sealed class DeskFlipOverlay : Form
{
    private const int MaximumWindows = 12;
    private const uint WmNull = 0;
    private const uint SmtoAbortIfHung = 2;
    private const uint PwRenderFullContent = 2;
    private const int DwmwaCloaked = 14;
    private readonly List<Entry> _entries = new();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 16 };
    private readonly Bitmap? _desktop;
    private readonly string _style;
    private readonly PointF[] _texturePoints = new PointF[3];
    private int _selection;
    private float _position, _transitionFrom, _target;
    private long _transitionStarted;
    private bool _closing;
    private static readonly System.Threading.SemaphoreSlim CaptureGate = new(1, 1);

    private sealed record Entry(IntPtr Handle, string Title, Bitmap? Snapshot);

    public DeskFlipOverlay() : this(DeskFlipEngine.Style) { }

    internal DeskFlipOverlay(string style)
    {
        _style = SystemInformation.HighContrast ? "Compact list" : style; // high contrast: the plain list (no desktop capture, readable)
        var foreground = GetForegroundWindow();
        Bounds = Screen.FromHandle(foreground).Bounds;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        DoubleBuffered = true;
        KeyPreview = true;
        BackColor = Color.FromArgb(20, 35, 49);
        _desktop = _style == "Compact list" ? null : CaptureDesktop(Bounds);
        foreach (var window in CandidateWindows())
            _entries.Add(new Entry(window, GetTitle(window), null));
        int front = _entries.FindIndex(e => e.Handle == foreground);
        _selection = _entries.Count > 1 ? (Math.Max(front, 0) + 1) % _entries.Count : 0;
        _position = _target = _selection;
        _timer.Tick += (_, _) => Animate();
    }

    internal bool HasWindows => _entries.Count > 0;

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Activate();
        Focus();
        if (_style != "Compact list") _ = PopulateSnapshotsAsync();
    }

    // PrintWindow can block even after WM_NULL succeeds. Keep at most one
    // capture worker globally; a hung third-party window never blocks Opsis UI
    // or causes additional capture workers to accumulate.
    private async System.Threading.Tasks.Task PopulateSnapshotsAsync()
    {
        if (!CaptureGate.Wait(0)) return;
        try
        {
            for (int i = 0; i < _entries.Count && !_closing && !IsDisposed; i++)
            {
                var handle = _entries[i].Handle;
                var image = await System.Threading.Tasks.Task.Run(() => CaptureWindow(handle));
                if (_closing || IsDisposed || i >= _entries.Count)
                {
                    image?.Dispose();
                    return;
                }
                if (image != null)
                {
                    _entries[i] = _entries[i] with { Snapshot = image };
                    Invalidate();
                }
            }
        }
        catch (Exception ex) { RotatingLogger.Write("DeskFlip snapshot worker: " + ex.Message); }
        finally { CaptureGate.Release(); }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Escape: Close(); return true;
            case Keys.Enter:
            case Keys.Space: SwitchTo(_selection); return true;
            case Keys.Left:
            case Keys.Up:
            case Keys.Tab | Keys.Shift: MoveSelection(-1); return true;
            case Keys.Right:
            case Keys.Down:
            case Keys.Tab: MoveSelection(1); return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (e.Delta != 0) MoveSelection(e.Delta < 0 ? 1 : -1);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        if (_style != "Flip 3D")
        {
            for (int i = 0; i < _entries.Count; i++)
                if (FlatBounds(i).Contains(e.Location)) { SwitchTo(i); return; }
            return;
        }
        foreach (var (index, depth) in VisibleCards().OrderBy(card => card.Depth))
        {
            using var polygon = new GraphicsPath();
            polygon.AddPolygon(Project(_entries[index], depth));
            if (!polygon.IsVisible(e.Location)) continue;
            SwitchTo(index);
            return;
        }
    }

    internal void MoveSelection(int direction)
    {
        if (_entries.Count < 2) return;
        if (_style != "Flip 3D")
        {
            _selection = Wrap(_selection + direction);
            Invalidate();
            return;
        }
        _target += direction;
        _selection = Wrap((int)_target);
        _transitionFrom = _position;
        _transitionStarted = Environment.TickCount64;
        _timer.Start();
    }

    private int Wrap(int index) => ((index % _entries.Count) + _entries.Count) % _entries.Count;

    private void Animate()
    {
        var progress = Math.Clamp((Environment.TickCount64 - _transitionStarted) / 195f, 0, 1);
        var ease = 1 - MathF.Pow(1 - progress, 3);
        _position = _transitionFrom + (_target - _transitionFrom) * ease;
        if (progress >= 1) { _position = _target = _selection; _timer.Stop(); }
        Invalidate();
    }

    private void SwitchTo(int index)
    {
        if (_closing || index < 0 || index >= _entries.Count) return;
        _closing = true;
        var handle = _entries[index].Handle;
        Close();
        if (IsIconic(handle)) ShowWindow(handle, 9);
        SetForegroundWindow(handle);
    }

    private IEnumerable<(int Index, float Depth)> VisibleCards()
    {
        if (_entries.Count == 0) yield break;
        int head = (int)MathF.Ceiling(_position);
        float fraction = head - _position;
        int cardCount = fraction > .001f ? _entries.Count - 1 : _entries.Count;
        for (int i = 0; i < cardCount; i++)
        {
            float depth = fraction + i;
            if (depth > 8.8f) break;
            yield return (Wrap(head + i), depth);
        }

        // The card leaving the front slides down and right until the next one takes its place.
        if (fraction > .001f) yield return (Wrap(head - 1), fraction - 1);
    }

    private PointF[] Project(Entry entry, float depth)
    {
        float scale = MathF.Pow(.895f, depth);
        float aspect = entry.Snapshot is { } image ? (float)image.Width / image.Height : 1.58f;
        float width = Math.Min(Width * .55f, Height * .64f * aspect) * scale;
        float height = width / aspect;
        float centerX = Width * .625f - Width * .095f * depth / (1 + Math.Abs(depth) * .115f);
        float centerY = Height * .585f - Height * .067f * depth / (1 + Math.Abs(depth) * .15f);
        float left = centerX - width * .56f, right = centerX + width * .44f;
        return
        [
            new(left, centerY - height * .39f), new(right, centerY - height * .515f),
            new(right, centerY + height * .49f), new(left, centerY + height * .305f)
        ];
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        if (_desktop != null) graphics.DrawImage(_desktop, ClientRectangle);
        using var tint = new SolidBrush(Color.FromArgb(70, 5, 19, 30));
        graphics.FillRectangle(tint, ClientRectangle);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
        graphics.PixelOffsetMode = PixelOffsetMode.Half;
        if (_style != "Flip 3D")
        {
            for (int i = 0; i < _entries.Count; i++) DrawFlat(graphics, i);
            return;
        }
        foreach (var (index, depth) in VisibleCards().OrderByDescending(card => card.Depth))
            DrawWindow(graphics, _entries[index], depth);
    }

    private Rectangle FlatBounds(int index)
    {
        int columns = _style == "Compact list" ? Math.Min(2, Math.Max(1, (_entries.Count + 5) / 6))
            : Math.Min(4, Math.Max(1, _entries.Count));
        int rows = Math.Max(1, (_entries.Count + columns - 1) / columns);
        int pad = Math.Max(8, Math.Min(Width, Height) / 60);
        int w = Math.Max(1, (Width - 4 * pad) / columns - pad);
        int h = Math.Max(1, Math.Min((Height - 4 * pad) / rows - pad, _style == "Compact list" ? 64 : Height / 3));
        int left = (Width - columns * (w + pad) + pad) / 2;
        int top = (Height - rows * (h + pad) + pad) / 2;
        return new Rectangle(left + index % columns * (w + pad), top + index / columns * (h + pad), w, h);
    }

    private void DrawFlat(Graphics graphics, int index)
    {
        var box = FlatBounds(index);
        var entry = _entries[index];
        bool active = index == _selection;
        using var fill = new SolidBrush(active ? Color.FromArgb(235, 39, 81, 112) : Color.FromArgb(230, 26, 43, 58));
        using var rim = new Pen(active ? Color.White : Color.FromArgb(130, 170, 195), active ? 2f : 1f);
        graphics.FillRectangle(fill, box);
        graphics.DrawRectangle(rim, box);
        int labelHeight = Math.Min(32, box.Height);
        if (entry.Snapshot is { } image && box.Height > labelHeight + 8 && box.Width > 8)
        {
            float scale = Math.Min((box.Width - 8f) / image.Width, (box.Height - labelHeight - 8f) / image.Height);
            int w = Math.Max(1, (int)(image.Width * scale)), h = Math.Max(1, (int)(image.Height * scale));
            graphics.DrawImage(image, new Rectangle(box.X + (box.Width - w) / 2, box.Y + 4, w, h));
        }
        var label = _style == "Compact list" ? box : new Rectangle(box.X, box.Bottom - labelHeight, box.Width, labelHeight);
        TextRenderer.DrawText(graphics, entry.Title, SystemFonts.MessageBoxFont, label, Color.White,
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
    }

    private void DrawWindow(Graphics graphics, Entry entry, float depth)
    {
        var p = Project(entry, depth);
        var shadow = p.Select(point => new PointF(point.X + 5, point.Y + 8)).ToArray();
        using var shadowFill = new SolidBrush(Color.FromArgb(95, Color.Black));
        graphics.FillPolygon(shadowFill, shadow);

        using var backing = new SolidBrush(Color.FromArgb(240, 30, 53, 73));
        graphics.FillPolygon(backing, p);
        if (entry.Snapshot is { } image)
        {
            int strips = depth < 3 ? 18 : 10;
            for (int i = 0; i < strips; i++)
            {
                float left = (float)i / strips, right = Math.Min(1f, (i + 1.13f) / strips);
                // Homogeneous depth makes the nearer edge wider while preserving straight window borders.
                float u = left / (1.32f - .32f * left), v = right / (1.32f - .32f * right);
                _texturePoints[0] = Lerp(p[0], p[1], u);
                _texturePoints[1] = Lerp(p[0], p[1], v);
                _texturePoints[2] = Lerp(p[3], p[2], u);
                graphics.DrawImage(image, _texturePoints,
                    new RectangleF(left * image.Width, 0, (right - left) * image.Width, image.Height), GraphicsUnit.Pixel);
            }
        }
        else
        {
            var left = (int)p[0].X + 14;
            var top = (int)p[0].Y + 14;
            TextRenderer.DrawText(graphics, entry.Title, SystemFonts.MessageBoxFont,
                new Rectangle(left, top, Math.Max(16, (int)p[1].X - left - 20), 32),
                Color.White, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        if (depth > .25f)
        {
            using var distance = new SolidBrush(Color.FromArgb((int)Math.Clamp(depth * 9, 0, 76), 5, 17, 28));
            graphics.FillPolygon(distance, p);
        }
        using var rim = new Pen(depth < .45f ? Color.FromArgb(227, 218, 242, 250) : Color.FromArgb(150, 166, 204, 222),
            depth < .45f ? 1.7f : 1.15f) { LineJoin = LineJoin.Round };
        graphics.DrawPolygon(rim, p);
    }

    private static PointF Lerp(PointF a, PointF b, float t) =>
        new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

    private static Bitmap? CaptureDesktop(Rectangle area)
    {
        try
        {
            using var original = new Bitmap(area.Width, area.Height);
            using (var graphics = Graphics.FromImage(original))
                graphics.CopyFromScreen(area.Location, Point.Empty, area.Size);
            return new Bitmap(original, Math.Max(1, area.Width / 3), Math.Max(1, area.Height / 3));
        }
        catch (Exception ex) when (ex is ExternalException or ArgumentException) { return null; }
    }

    private static Bitmap? CaptureWindow(IntPtr handle)
    {
        if (IsIconic(handle) || SendMessageTimeout(handle, WmNull, UIntPtr.Zero, IntPtr.Zero,
                SmtoAbortIfHung, 70, out _) == IntPtr.Zero || !GetWindowRect(handle, out var area)) return null;
        int width = area.Right - area.Left, height = area.Bottom - area.Top;
        if (width is <= 0 or > 7680 || height is <= 0 or > 4320) return null;
        try
        {
            using var source = new Bitmap(width, height, PixelFormat.Format32bppRgb);
            bool captured;
            using (var graphics = Graphics.FromImage(source))
            {
                var dc = graphics.GetHdc();
                try { captured = PrintWindow(handle, dc, PwRenderFullContent); }
                finally { graphics.ReleaseHdc(dc); }
            }
            if (!captured) return null;
            float scale = Math.Min(1, 1024f / Math.Max(width, height));
            return new Bitmap(source, Math.Max(1, (int)(width * scale)), Math.Max(1, (int)(height * scale)));
        }
        catch (Exception ex) when (ex is ExternalException or ArgumentException) { return null; }
    }

    private static IReadOnlyList<IntPtr> CandidateWindows()
    {
        var windows = new List<IntPtr>();
        EnumWindows((handle, _) =>
        {
            if (!IsWindowVisible(handle) || GetWindowTextLength(handle) == 0 || GetWindow(handle, 4) != IntPtr.Zero ||
                (GetWindowLong(handle, -20) & 0x80) != 0) return true;
            if (DwmGetWindowAttribute(handle, DwmwaCloaked, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return true;
            windows.Add(handle);
            return windows.Count < MaximumWindows;
        }, IntPtr.Zero);
        return windows;
    }

    private static string GetTitle(IntPtr handle)
    {
        var title = new StringBuilder(Math.Clamp(GetWindowTextLength(handle) + 1, 1, 512));
        GetWindowText(handle, title, title.Capacity);
        return title.ToString();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _closing = true;
            _timer.Dispose();
            foreach (var entry in _entries) entry.Snapshot?.Dispose();
            _entries.Clear();
            _desktop?.Dispose();
        }
        base.Dispose(disposing);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowRect { public int Left, Top, Right, Bottom; }
    private delegate bool EnumWindowsProc(IntPtr handle, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr handle);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextLength(IntPtr handle);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr handle, StringBuilder title, int count);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowLong(IntPtr handle, int index);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr handle, uint command);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr handle);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr handle, out WindowRect area);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr handle, int attribute, out int value, int bytes);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr handle);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr handle, int command);
    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr handle, IntPtr dc, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessageTimeout(
        IntPtr handle, uint message, UIntPtr wParam, IntPtr lParam, uint flags, uint timeout, out UIntPtr result);
}

public static class DeskFlipEngine
{
    private static DeskFlipOverlay? _overlay;
    private const string ModuleId = "desktop.window-switcher";
    public static bool IsValidStyle(string style) => style is "Flip 3D" or "Thumbnail grid" or "Compact list";
    public static string Style
    {
        get
        {
            try
            {
                var state = OpsisModCore.Read();
                if (state.Options.TryGetValue(ModuleId, out var options) &&
                    options.TryGetValue("style", out var style) && IsValidStyle(style)) return style;
            }
            catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or UnauthorizedAccessException)
            { RotatingLogger.Write("DeskFlip preferences: " + ex.Message); }
            return "Flip 3D";
        }
    }

    public static void SetStyle(string style)
    {
        if (!IsValidStyle(style)) throw new ArgumentOutOfRangeException(nameof(style));
        OpsisModCore.SetOption(ModuleId, "style", style);
    }

    public static void Show()
    {
        if (_overlay is { IsDisposed: false } shown)
        {
            shown.Activate();
            shown.MoveSelection(1);
            return;
        }
        var overlay = new DeskFlipOverlay();
        if (!overlay.HasWindows) { overlay.Dispose(); return; }
        _overlay = overlay;
        overlay.FormClosed += (_, _) => _overlay = null;
        overlay.Show();
    }
}
