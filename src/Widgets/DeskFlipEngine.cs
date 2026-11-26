using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using MotionDesk.Services;

namespace MotionDesk.Widgets;

public enum DeskFlipLayout { Vista, Grid, List }

public sealed class DeskFlipOverlay : Form
{
    private const int MaximumWindows = 12;
    private const uint WmNull = 0;
    private const uint SmtoAbortIfHung = 2;
    private const uint PwRenderFullContent = 2;
    private const int DwmwaCloaked = 14;
    private readonly List<Entry> _entries = new();
    private readonly DeskFlipLayout _layout;
    internal DeskFlipLayout LayoutMode => _layout;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 16 };
    private readonly Bitmap? _desktop;
    private readonly PointF[] _texturePoints = new PointF[3];
    private int _selection;
    private float _position, _transitionFrom, _target;
    private long _transitionStarted;
    private bool _closing;

    private sealed record Entry(IntPtr Handle, string Title, Bitmap? Snapshot);

    public DeskFlipOverlay() : this(DeskFlipLayout.Vista) { }

    public DeskFlipOverlay(DeskFlipLayout layout)
    {
        _layout = SystemInformation.HighContrast ? DeskFlipLayout.List : layout;
        var foreground = GetForegroundWindow();
        Bounds = Screen.FromHandle(foreground).Bounds;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        DoubleBuffered = true;
        KeyPreview = true;
        BackColor = Color.FromArgb(20, 35, 49);
        _desktop = _layout == DeskFlipLayout.Vista ? CaptureDesktop(Bounds) : null;
        foreach (var window in CandidateWindows())
            _entries.Add(new Entry(window, GetTitle(window), _layout == DeskFlipLayout.List ? null : CaptureWindow(window)));
        AccessibleName = "DeskFlip window switcher";
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
        if (_layout != DeskFlipLayout.Vista)
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
        _target += direction;
        _selection = Wrap((int)_target);
        if (_layout != DeskFlipLayout.Vista || !Opsis.UI.OpsisMotion.Enabled || SystemInformation.HighContrast)
        { _timer.Stop(); _position = _target = _selection; Invalidate(); return; }
        _transitionFrom = _position;
        _transitionStarted = Environment.TickCount64;
        _timer.Start();
    }

    private int Wrap(int index) => ((index % _entries.Count) + _entries.Count) % _entries.Count;

    private void Animate()
    {
        var progress = !Opsis.UI.OpsisMotion.Enabled || SystemInformation.HighContrast ? 1f : Math.Clamp((Environment.TickCount64 - _transitionStarted) / 195f, 0, 1);
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
        if (!IsWindow(handle)) return;
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
        if (_layout != DeskFlipLayout.Vista) { PaintFlat(graphics); return; }
        if (_desktop != null) graphics.DrawImage(_desktop, ClientRectangle);
        using var tint = new SolidBrush(Color.FromArgb(70, 5, 19, 30));
        graphics.FillRectangle(tint, ClientRectangle);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
        graphics.PixelOffsetMode = PixelOffsetMode.Half;
        foreach (var (index, depth) in VisibleCards().OrderByDescending(card => card.Depth))
            DrawWindow(graphics, _entries[index], depth);
    }

    private Rectangle FlatBounds(int index)
    {
        int pad = Math.Clamp(Width / 24, 12, 48);
        int top = Math.Clamp(Height / 12, 30, 72);
        int areaHeight = Math.Max(1, Height - top - pad);
        if (_layout == DeskFlipLayout.List)
        {
            int w = Math.Min(Math.Max(1, Width - pad * 2), 900);
            int h = Math.Max(1, Math.Min(52, areaHeight / Math.Max(1, _entries.Count)));
            return new Rectangle((Width - w) / 2, top + index * h, w, Math.Max(1,h - 2));
        }
        int cols = Math.Min(4, Math.Max(1,(int)Math.Ceiling(Math.Sqrt(_entries.Count))));
        int rows = Math.Max(1, (_entries.Count + cols - 1) / cols);
        int cellW = Math.Max(1, (Width - pad * 2) / cols);
        int cellH = Math.Max(1, areaHeight / rows);
        int gap = Math.Min(12, Math.Max(0, Math.Min(cellW,cellH) / 8));
        return new Rectangle(pad + index % cols * cellW + gap / 2, top + index / cols * cellH + gap / 2,
            Math.Max(1,cellW-gap),Math.Max(1,cellH-gap));
    }

    private void PaintFlat(Graphics g)
    {
        bool hc = SystemInformation.HighContrast;
        var t = Opsis.UI.OpsisTheme.Current;
        var back = hc ? SystemColors.Window : t.Background;
        var text = hc ? SystemColors.WindowText : t.Text;
        var selected = hc ? SystemColors.Highlight : t.Accent;
        var onSelected = hc ? SystemColors.HighlightText :
            Opsis.UI.Contrast.Ratio(Color.Black,selected) >= Opsis.UI.Contrast.Ratio(Color.White,selected) ? Color.Black : Color.White;
        g.Clear(back);
        TextRenderer.DrawText(g, $"DeskFlip  ·  {_layout}   |   {Math.Min(_entries.Count,_selection+1)}/{_entries.Count}",
            SystemFonts.MessageBoxFont,new Rectangle(12,4,Math.Max(1,Width-24),Math.Max(1,Math.Clamp(Height/12,30,72)-8)),
            text,TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        for (int i=0; i<_entries.Count; i++)
        {
            var rect=FlatBounds(i);
            bool active=i==_selection;
            using (var fill=new SolidBrush(active ? selected : hc ? back : t.Surface)) g.FillRectangle(fill,rect);
            var color=active ? onSelected : text;
            var flags=TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
            int titleHeight=Math.Min(30,rect.Height);
            if (_layout==DeskFlipLayout.Grid && _entries[i].Snapshot is { } img && rect.Width>12 && rect.Height>titleHeight+12)
            {
                var area=new Rectangle(rect.X+6,rect.Y+6,rect.Width-12,rect.Height-titleHeight-12);
                double scale=Math.Min(area.Width/(double)img.Width,area.Height/(double)img.Height);
                int w=Math.Max(1,(int)(img.Width*scale)), h=Math.Max(1,(int)(img.Height*scale));
                g.DrawImage(img,new Rectangle(area.X+(area.Width-w)/2,area.Y+(area.Height-h)/2,w,h));
            }
            var label=_layout==DeskFlipLayout.List ? Rectangle.Inflate(rect,-6,0) :
                new Rectangle(rect.X+4,rect.Bottom-titleHeight,Math.Max(1,rect.Width-8),titleHeight);
            if (label.Width>0 && label.Height>0) TextRenderer.DrawText(g,_entries[i].Title,SystemFonts.MessageBoxFont,label,color,flags);
            if (active) ControlPaint.DrawFocusRectangle(g,Rectangle.Inflate(rect,-1,-1),color,selected);
        }
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
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr handle);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr handle);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr handle, int command);
    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr handle, IntPtr dc, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessageTimeout(
        IntPtr handle, uint message, UIntPtr wParam, IntPtr lParam, uint flags, uint timeout, out UIntPtr result);
}

public static class DeskFlipEngine
{
    private static DeskFlipOverlay? _overlay;

    public static void Show() => Show(DeskFlipLayout.Vista);

    public static void Show(DeskFlipLayout layout)
    {
        if (SystemInformation.HighContrast) layout = DeskFlipLayout.List;
        if (_overlay is { IsDisposed: false } shown)
        {
            if (shown.LayoutMode == layout) { shown.Activate(); shown.MoveSelection(1); return; }
            shown.Close();
        }
        var overlay = new DeskFlipOverlay(layout);
        if (!overlay.HasWindows) { overlay.Dispose(); return; }
        _overlay = overlay;
        overlay.FormClosed += (_, _) => _overlay = null;
        overlay.Show();
    }
}
