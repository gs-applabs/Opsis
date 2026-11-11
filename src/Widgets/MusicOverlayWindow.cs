using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;
using MotionDesk.Services;

namespace MotionDesk.Widgets
{
    /// <summary>
    /// Music overlay (plan 2.1.6): a small glass card on the desktop with the cover, title and artist of what ANY player is playing (Windows media session, <see cref="NowPlayingService"/>).
    /// Layered, click-through, not activating. It exists only while enabled; it is visible only while something plays, and never over a fullscreen app / locked screen.
    /// The glass is the GPU pane when "GPU glass" is on; otherwise a translucent dark card of its own. One repaint per track change - nothing runs per frame.
    /// </summary>
    internal sealed class MusicOverlayWindow : Form
    {
        private readonly NowPlayingService _now;
        private readonly LayeredSurface _surface = new();
        private readonly GlassPaneBinder _pane;
        private OverlayAnchor _anchor;

        public MusicOverlayWindow(NowPlayingService now, OverlayAnchor anchor)
        {
            _now = now; _anchor = anchor;
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual; TopMost = false;
            _pane = new GlassPaneBinder(this, 18, () => GlassService.Enabled);
            _now.Changed += Refresh;
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplay;
        }

        protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ExStyle |= 0x00080000 | 0x08000000 | 0x80 | 0x20; return cp; } }   // layered, no-activate, tool window, transparent (click-through)
        protected override bool ShowWithoutActivation => true;

        private float Scale => DeviceDpi / 96f;
        private bool GlassOn => GlassService.Enabled;

        public void SetAnchor(OverlayAnchor a) { _anchor = a; Refresh(); }

        private void OnDisplay(object? s, EventArgs e) { if (IsHandleCreated && !IsDisposed) BeginInvoke(new Action(Refresh)); }

        /// <summary>Show / hide and redraw from the current state of the media session.</summary>
        public new void Refresh()
        {
            if (IsDisposed) return;
            var n = _now.Current;
            var primary = Screen.PrimaryScreen;
            bool show = n != null && n.Playing && !string.IsNullOrWhiteSpace(n.Title) && !AppActivity.IsPausedFor(primary);
            if (!show) { if (Visible) Hide(); return; }
            var size = new Size((int)(340 * Scale), (int)(88 * Scale));
            var wa = (primary ?? Screen.AllScreens[0]).WorkingArea;
            var loc = OverlayPlacement.Compute(wa, size, _anchor, (int)(24 * Scale));
            if (!IsHandleCreated) CreateHandle();
            SetBounds(loc.X, loc.Y, size.Width, size.Height, BoundsSpecified.All);
            using (var g = _surface.Begin(size.Width, size.Height)) Paint(g, size, n!, _now.Art);
            _surface.Present(Handle, loc);
            if (!Visible) Show();
        }

        /// <summary>Draws the card for the given track into a bitmap (used by the check that renders it without any desktop content).</summary>
        public Bitmap RenderPreview(NowPlaying n, Bitmap? art)
        {
            var size = new Size((int)(340 * Scale), (int)(88 * Scale));
            var bmp = new Bitmap(size.Width, size.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using var g = Graphics.FromImage(bmp); Paint(g, size, n, art);
            return bmp;
        }

        private void Paint(Graphics g, Size size, NowPlaying n, Bitmap? art)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit; g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            float s = Scale; var card = new Rectangle(0, 0, size.Width - 1, size.Height - 1);
            using (var path = MotionDesk.UI.UiTheme.RoundedPath(card, (int)(18 * s)))
            {
                if (!GlassOn) { using var fill = new SolidBrush(Color.FromArgb(170, 22, 26, 36)); g.FillPath(fill, path); }
                using var rim = new Pen(Color.FromArgb(GlassOn ? 90 : 60, 255, 255, 255), 1f); g.DrawPath(rim, path);
            }
            // cover (or a glossy note glyph when the player gives none)
            var cover = new Rectangle((int)(12 * s), (int)(12 * s), (int)(64 * s), (int)(64 * s));
            using (var cp = MotionDesk.UI.UiTheme.RoundedPath(cover, (int)(12 * s)))
            {
                if (art != null) { g.SetClip(cp); g.DrawImage(art, cover); g.ResetClip(); }
                else
                {
                    using var gb = new LinearGradientBrush(cover, Color.FromArgb(150, 120, 220, 255), Color.FromArgb(150, 90, 110, 230), 90f); g.FillPath(gb, cp);
                    using var nf = new Font("Segoe UI Symbol", 26f * s, FontStyle.Regular, GraphicsUnit.Pixel); using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    g.DrawString("♪", nf, Brushes.White, cover, sf);
                }
                using var cr = new Pen(Color.FromArgb(110, 255, 255, 255), 1f); g.DrawPath(cr, cp);
            }
            var textX = cover.Right + (int)(14 * s); var textW = size.Width - textX - (int)(14 * s);
            using var tf = new Font("Segoe UI Semibold", 15f * s, FontStyle.Bold, GraphicsUnit.Pixel);
            using var af = new Font("Segoe UI", 12.5f * s, FontStyle.Regular, GraphicsUnit.Pixel);
            using var fmt = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
            using var shadow = new SolidBrush(Color.FromArgb(140, 0, 0, 0));
            void Text(string t, Font f, Brush b, float y) { g.DrawString(t, f, shadow, new RectangleF(textX + 1, y + 1, textW, 24 * s), fmt); g.DrawString(t, f, b, new RectangleF(textX, y, textW, 24 * s), fmt); }
            Text(n.Title, tf, Brushes.White, 18 * s);
            if (!string.IsNullOrWhiteSpace(n.Artist)) { using var ab = new SolidBrush(Color.FromArgb(215, 235, 240, 250)); Text(n.Artist, af, ab, 42 * s); }
            if (n.Duration is { } d && d.TotalSeconds > 1) { using var db = new SolidBrush(Color.FromArgb(170, 220, 230, 245)); Text(d.TotalHours >= 1 ? d.ToString(@"h\:mm\:ss") : d.ToString(@"m\:ss"), af, db, 60 * s); }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplay; _now.Changed -= Refresh; _pane.Dispose(); _surface.Dispose(); }
            base.Dispose(disposing);
        }
    }

    /// <summary>Starts / stops the music overlay from the settings (UI thread). Nothing is created while the setting is off.</summary>
    internal static class MusicOverlay
    {
        private static NowPlayingService? _svc; private static MusicOverlayWindow? _win;

        public static void Apply()
        {
            var s = AppSettings.Load();
            if (!s.MusicOverlayEnabled) { Stop(); return; }
            var anchor = Enum.TryParse<OverlayAnchor>(s.MusicOverlayAnchor, true, out var a) ? a : OverlayAnchor.BottomRight;
            if (_win != null) { _win.SetAnchor(anchor); return; }
            _svc = new NowPlayingService(); _win = new MusicOverlayWindow(_svc, anchor);
            _ = _svc.StartAsync();
        }

        public static void Stop() { _win?.Close(); _win?.Dispose(); _win = null; _svc?.Dispose(); _svc = null; }
    }
}
