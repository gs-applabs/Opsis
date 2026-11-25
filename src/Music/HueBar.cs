using System.Drawing.Drawing2D;

namespace OpsisMusicOverlay;

internal sealed class HueBar : Control
{
    private static readonly Color[] Stops = [Color.Red, Color.Yellow, Color.Lime, Color.Cyan, Color.Blue, Color.Magenta, Color.Red];
    private int _hue;
    internal event Action<Color>? ColorSelected;

    internal HueBar()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        SetStyle(ControlStyles.Selectable, true);
        TabStop = true;
        AccessibleRole = AccessibleRole.Slider;
        Cursor = Cursors.Hand;
    }

    internal void SetColor(Color color)
    {
        int hue = color.GetSaturation() == 0 ? _hue : (int)Math.Round(color.GetHue());
        if (hue == _hue || (hue == 0 && _hue == 360)) return;
        _hue = hue;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var frame = Rectangle.Inflate(ClientRectangle, -3, -4);
        if (frame.Width < 2 || frame.Height < 2) return;
        using var spectrum = new LinearGradientBrush(frame, Color.Red, Color.Red, LinearGradientMode.Horizontal);
        spectrum.InterpolationColors = new ColorBlend { Colors = Stops, Positions = [0, 1f / 6, 2f / 6, .5f, 4f / 6, 5f / 6, 1] };
        e.Graphics.FillRectangle(spectrum, frame);
        int x = frame.X + (int)Math.Round(_hue / 360d * (frame.Width - 1));
        using var dark = new Pen(Color.Black, 4);
        using var light = new Pen(Color.White, 2);
        e.Graphics.DrawLine(dark, x, frame.Top, x, frame.Bottom - 1);
        e.Graphics.DrawLine(light, x, frame.Top, x, frame.Bottom - 1);
        if (Focused) ControlPaint.DrawFocusRectangle(e.Graphics, ClientRectangle);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || Width <= 7) return;
        Focus();
        SelectHue((int)Math.Round(360d * (e.X - 3) / (Width - 7)));
    }

    protected override bool IsInputKey(Keys keyData) =>
        (keyData & Keys.KeyCode) is Keys.Left or Keys.Right or Keys.Home or Keys.End || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        int? next = e.KeyCode switch { Keys.Left => _hue - 5, Keys.Right => _hue + 5, Keys.Home => 0, Keys.End => 360, _ => null };
        if (next == null) return;
        e.SuppressKeyPress = true;
        SelectHue(next.Value);
    }

    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

    private void SelectHue(int hue)
    {
        _hue = Math.Clamp(hue, 0, 360);
        if (_hue == 360) ColorSelected?.Invoke(Stops[^1]);
        else
        {
            int i = _hue / 60;
            double ratio = (_hue % 60) / 60d;
            var a = Stops[i];
            var b = Stops[i + 1];
            ColorSelected?.Invoke(Color.FromArgb(Mix(a.R, b.R), Mix(a.G, b.G), Mix(a.B, b.B)));
            int Mix(int x, int y) => (int)Math.Round(x + (y - x) * ratio);
        }
        Invalidate();
    }
}
