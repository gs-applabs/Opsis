namespace MotionDesk.UI;

/// <summary>
/// A small live progress card at the bottom of the sidebar for a long task that runs in the background (a package download / install):
/// title, detail line and a progress bar (a moving bar while the size is not known). It takes no space while nothing runs, and shows only
/// the bar when the sidebar is collapsed. Colours come from the active theme tokens; nothing animates while idle.
/// </summary>
internal sealed class SidebarTaskPanel : Control
{
    private string _title = "", _detail = "";
    private double _fraction = -1;            // <0: unknown size (moving bar)
    private int _phase;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 60 };

    public SidebarTaskPanel()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
        Dock = DockStyle.Fill;
        Visible = false;
        Height = 64;
        _timer.Tick += (_, _) => { _phase = (_phase + 6) % 200; if (Visible && _fraction < 0) Invalidate(); };
    }

    /// <summary>Compact layout (collapsed sidebar): the bar only; the text moves to the tooltip.</summary>
    public bool Compact { get; set; }

    public void Update(string title, string detail, double fraction)
    {
        _title = title; _detail = detail; _fraction = fraction;
        AccessibleName = title + " " + detail;
        if (!Visible) { Visible = true; _timer.Start(); }
        Invalidate();
    }

    public void Finish() { _timer.Stop(); Visible = false; }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        int pad = 2, barH = 6;
        int barY = Compact ? Height / 2 - barH / 2 : Height - barH - 8;
        var bar = new Rectangle(pad, barY, Math.Max(8, Width - pad * 2), barH);
        if (!Compact)
        {
            using var titleFont = new Font("Segoe UI", 8.5f, FontStyle.Bold);
            using var detailFont = new Font("Segoe UI", 8f);
            TextRenderer.DrawText(g, _title, titleFont, new Rectangle(pad, 4, Width - pad * 2, 18), UiTheme.TextPrimary,
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.Left);
            TextRenderer.DrawText(g, _detail, detailFont, new Rectangle(pad, 22, Width - pad * 2, 16), UiTheme.TextSecondary,
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.Left);
        }
        using (var back = new SolidBrush(UiTheme.Border)) g.FillRectangle(back, bar);
        using var fill = new SolidBrush(UiTheme.AccentCyan);
        if (_fraction >= 0) g.FillRectangle(fill, new Rectangle(bar.X, bar.Y, (int)(bar.Width * Math.Clamp(_fraction, 0, 1)), bar.Height));
        else
        {
            int w = bar.Width / 3, x = bar.X + (int)((bar.Width + w) * (_phase / 200.0)) - w;
            var clip = Rectangle.Intersect(new Rectangle(x, bar.Y, w, bar.Height), bar);
            if (clip.Width > 0) g.FillRectangle(fill, clip);
        }
    }

    protected override void Dispose(bool disposing) { if (disposing) _timer.Dispose(); base.Dispose(disposing); }
}
