using System.Drawing.Drawing2D;
namespace MotionDesk.UI;
public sealed partial class MainWindow : Form
{
        private sealed class IconButton : Button
        {
            private const TextFormatFlags CaptionFlags = TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding |
                TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl;
            private static readonly Font CaptionFont = new("Segoe UI", 10f);
            private readonly string _iconKey;
            private bool _hovered;

            public IconButton(string iconKey, string text, int width)
            {
                _iconKey = iconKey;
                FlatStyle = FlatStyle.Flat;
                FlatAppearance.BorderSize = 0;
                UseVisualStyleBackColor = false;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                    ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw, true);
                BackColor = Color.Transparent;
                Font = CaptionFont;
                Text = text;
                Cursor = Cursors.Hand;
                AccessibleRole = AccessibleRole.PushButton;
                UseMnemonic = false;
                MinimumSize = new Size(width, 44);
                MaximumSize = new Size(width, 0);
                Margin = new Padding(0, 3, 8, 3);
                AutoSizeMode = AutoSizeMode.GrowAndShrink;
                AutoSize = true;
            }

            private int Px(int value) => LogicalToDeviceUnits(value);

            public override Size GetPreferredSize(Size proposedSize)
            {
                int width = MinimumSize.Width > 0 ? MinimumSize.Width : Px(350);
                int textWidth = Math.Max(1, width - Px(62));
                int textHeight = TextRenderer.MeasureText(Text, Font, new Size(textWidth, int.MaxValue), CaptionFlags).Height;
                return new Size(width, Math.Max(MinimumSize.Height, textHeight + Px(14)));
            }

            protected override void OnMouseEnter(EventArgs e)
            {
                base.OnMouseEnter(e);
                _hovered = true;
                Invalidate();
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                base.OnMouseLeave(e);
                _hovered = false;
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                if (Width < 8 || Height < 8) return;
                var g = e.Graphics;
                var previous = g.SmoothingMode;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using var outline = UiTheme.RoundedPath(ClientRectangle, Math.Min(Width, Height) / 2);
                var colors = (FindForm() as MainWindow)?.CurrentSkinColors ?? ColorsFromTheme(ThemeManager.Current);
                using var background = new SolidBrush(Enabled && _hovered ? colors.Hover : colors.Surface);
                g.FillPath(background, outline);

                int iconSize = Px(22);
                IconRenderer.Draw(g, _iconKey, new RectangleF(Px(14), (Height - iconSize) / 2f, iconSize, iconSize),
                    Enabled ? colors.Accent : colors.Muted);
                if (Focused && ShowFocusCues)
                {
                    var bounds = Rectangle.Inflate(ClientRectangle, -3, -3);
                    using var focus = UiTheme.RoundedPath(bounds, Math.Min(bounds.Width, bounds.Height) / 2);
                    using var pen = new Pen(colors.Accent);
                    g.DrawPath(pen, focus);
                }

                g.SmoothingMode = previous;
                var caption = new Rectangle(Px(46), 0, Math.Max(1, Width - Px(62)), Height);
                TextRenderer.DrawText(g, Text, Font, caption, Enabled ? colors.Text : colors.Muted,
                    CaptionFlags | TextFormatFlags.VerticalCenter | TextFormatFlags.PreserveGraphicsClipping);
            }
        }

        private sealed class HoverButton : Button
        {
            private bool _hovered;
            public Color BaseColor { get; set; } = UiTheme.Surface;
            public Color HoverBackColor { get; set; } = UiTheme.SurfaceHover;

            public HoverButton(bool keyboard = false)
            {
                FlatStyle = FlatStyle.Flat;
                FlatAppearance.BorderSize = 0;
                UseVisualStyleBackColor = false;
                UseMnemonic = false;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                SetStyle(ControlStyles.Selectable, keyboard);
                TabStop = keyboard;
                MouseEnter += (_, _) => { _hovered = true; Invalidate(); };
                MouseLeave += (_, _) => { _hovered = false; Invalidate(); };
            }

            protected override void OnPaintBackground(PaintEventArgs e)
            {
                using var backdrop = new SolidBrush(Parent?.BackColor ?? UiTheme.PageBackground);
                e.Graphics.FillRectangle(backdrop, e.ClipRectangle);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var colors = (FindForm() as MainWindow)?.CurrentSkinColors ?? ColorsFromTheme(ThemeManager.Current);
                PaintActionButton(this, e.Graphics, _hovered ? HoverBackColor : BackColor,
                    Enabled ? ForeColor : colors.Muted, colors.Accent);
            }
        }

        private static void PaintActionButton(Button button, Graphics g, Color fill, Color text, Color accent)
        {
            if (button.Width < 8 || button.Height < 8) return;
            var bounds = button.ClientRectangle;
            var state = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var shape = UiTheme.RoundedPath(bounds, Math.Min(bounds.Width, bounds.Height) / 2);
            using var brush = new SolidBrush(fill);
            g.FillPath(brush, shape);
            if (button.Focused && button.TabStop)
            {
                var inset = Rectangle.Inflate(bounds, -3, -3);
                using var focus = UiTheme.RoundedPath(inset, Math.Min(inset.Width, inset.Height) / 2);
                using var pen = new Pen(fill == accent ? text : accent);
                g.DrawPath(pen, focus);
            }
            g.Restore(state);

            int padding = Math.Max(button.Padding.Horizontal / 2, button.LogicalToDeviceUnits(14));
            var caption = Rectangle.Inflate(bounds, -Math.Min(padding, (bounds.Width - 1) / 2), -2);
            TextRenderer.DrawText(g, button.Text, button.Font, caption, text, ActionCaptionFlags |
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.PreserveGraphicsClipping);
        }

        private const TextFormatFlags ActionCaptionFlags = TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding |
            TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl;

        private static int CaptionHeight(string text, int width, int horizontalInset, Font font, int minimum = 40) =>
            Math.Max(minimum, TextRenderer.MeasureText(text, font, new Size(Math.Max(1, width - horizontalInset), int.MaxValue),
                ActionCaptionFlags).Height + 14);

}
