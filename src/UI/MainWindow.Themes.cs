using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using MotionDesk.Services;

namespace MotionDesk.UI
{
    // Theme page: the palettes imported from GearWin (colours only), each as a dark and a light preview that applies live when clicked.
    public sealed partial class MainWindow
    {
        private sealed class PaletteStrip : Control
        {
            private readonly UiPalette _p; private readonly bool _selected;
            public PaletteStrip(UiPalette p, bool selected, string tip, Action onClick)
            {
                _p = p; _selected = selected; Size = new Size(190, 34); Cursor = Cursors.Hand; Margin = new Padding(0, 2, 12, 2);
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
                var tooltip = new ToolTip();
                tooltip.SetToolTip(this, tip);
                Disposed += (_, _) => tooltip.Dispose();
                SetStyle(ControlStyles.Selectable, true);
                TabStop = true;
                Click += (_, _) => onClick();
                KeyDown += (_, e) =>
                {
                    if (e.KeyCode is not (Keys.Enter or Keys.Space)) return;
                    e.SuppressKeyPress = true;
                    onClick();
                };
                GotFocus += (_, _) => Invalidate();
                LostFocus += (_, _) => Invalidate();
                AccessibleName = tip; AccessibleRole = AccessibleRole.PushButton;
            }
            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics; g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                var r = new Rectangle(1, 1, Width - 3, Height - 3);
                using (var path = UiTheme.RoundedPath(r, 8)) { using var bg = new SolidBrush(_p.Background); g.FillPath(bg, path); using var pen = new Pen(_selected || Focused ? _p.AccentCyan : _p.Border, _selected || Focused ? 2.5f : 1f); g.DrawPath(pen, path); }
                var chips = new[] { _p.Sidebar, _p.Surface, _p.SurfaceHover, _p.AccentCyan, _p.TextPrimary };
                int x = 10, w = (Width - 20 - 4 * 6) / 5;
                foreach (var c in chips) { using var b = new SolidBrush(c); g.FillRectangle(b, x, 9, w, Height - 20); using var bp = new Pen(Color.FromArgb(90, _p.TextSecondary)); g.DrawRectangle(bp, x, 9, w, Height - 20); x += w + 6; }
            }
        }

        private void BuildGearThemeCards(Control panel)
        {
            var current = AppSettings.Load().ThemeMode;
            var card = AddCardSection(panel, Lx("Παλέτες GearWin (σκούρο και φωτεινό)", "GearWin palettes (dark and light)"));
            AddText(card, Lx("Μόνο χρώματα: χωρίς κινούμενο φόντο. Κάνε κλικ σε σκούρο ή φωτεινό δείγμα για άμεση εφαρμογή.", "Colours only: no animated background. Click a dark or light sample to apply it at once."));
            foreach (var t in GearWinThemes.All)
            {
                var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 2) };
                row.Controls.Add(new Label { Text = t.Name, AutoSize = false, Width = 230, Height = 34, TextAlign = ContentAlignment.MiddleLeft, ForeColor = UiTheme.TextPrimary, Font = UiTheme.FontBody });
                foreach (var dark in new[] { true, false })
                {
                    var mode = GearWinThemes.Mode(t.Name, dark); var name = t.Name + " - " + (dark ? Lx("σκούρο", "dark") : Lx("φωτεινό", "light"));
                    row.Controls.Add(new PaletteStrip(dark ? t.Dark() : t.Light(), current == mode, name, () => ThemeManager.SetMode(mode)));
                }
                card.Controls.Add(row);
            }
        }
    }
}
