using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using MotionDesk.Services;

namespace MotionDesk.UI;

/// <summary>A non-interactive graphical mockup of a preset. Never changes the Windows desktop.</summary>
public sealed class AppearancePreviewForm : Form
{
    private readonly AppearancePresets.Preset _preset;
    public AppearancePreviewForm(AppearancePresets.Preset preset)
    {
        _preset = preset;
        Text = "Opsis — Appearance Preview / Προεπισκόπηση";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(870, 570);
        MinimumSize = new Size(570, 420);
        Controls.Add(new DesktopCanvas(preset) { Dock = DockStyle.Fill });
        Controls.Add(new Label
        {
            Text = "Visual simulation only — your Windows settings are unchanged. / Ενδεικτική απεικόνιση — καμία αλλαγή συστήματος.",
            Dock = DockStyle.Bottom, Height = 38, TextAlign = ContentAlignment.MiddleCenter
        });
    }

    private sealed class DesktopCanvas : Control
    {
        private readonly AppearancePresets.Preset _p;
        public DesktopCanvas(AppearancePresets.Preset p)
        {
            _p = p;
            DoubleBuffered = true;
            ResizeRedraw = true;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var b = ClientRectangle;
            if (b.Width < 200 || b.Height < 180) return;
            using var bg = new LinearGradientBrush(b, Color.FromArgb(25, 51, 88), Color.FromArgb(12, 15, 36), 65f);
            g.FillRectangle(bg, b);

            using var heading = new Font("Segoe UI", 18, FontStyle.Bold);
            using var small = new Font("Segoe UI", 10);
            using var text = new SolidBrush(Color.White);
            using var secondary = new SolidBrush(Color.FromArgb(211, 220, 241));
            g.DrawString(_p.Name, heading, text, 30, 24);
            g.DrawString("Windows 11 / Start Studio — indicative layout", small, secondary, 32, 63);

            // Draw a Start-like launcher mockup, intentionally separate from the actual shell.
            var menu = new Rectangle(30, 110, Math.Min(400, b.Width - 80), Math.Max(160, b.Height - 208));
            using (var panel = new SolidBrush(Color.FromArgb(226, 234, 247))) g.FillRoundedRectangle(panel, menu, 18);
            using var dark = new SolidBrush(Color.FromArgb(38, 48, 72));
            g.DrawString("OPSIS START STUDIO", small, dark, menu.Left + 22, menu.Top + 18);
            using (var searchBrush = new SolidBrush(Color.White))
                g.FillRoundedRectangle(searchBrush, new Rectangle(menu.Left + 20, menu.Top + 53, menu.Width - 40, 34), 7);
            g.DrawString("Search applications…", small, dark, menu.Left + 32, menu.Top + 61);
            for (var i = 0; i < 8 && menu.Top + 117 + i * 39 < menu.Bottom - 22; i++)
            {
                using var iconBrush = new SolidBrush(Color.FromArgb(70 + i * 15, 112 + i * 9, 189));
                g.FillRoundedRectangle(iconBrush, new Rectangle(menu.Left + 26, menu.Top + 112 + i * 39, 24, 24), 5);
                g.DrawString(i % 2 == 0 ? "Application" : "Folder group", small, dark,
                    menu.Left + 62, menu.Top + 113 + i * 39);
            }

            // Taskbar placement and search/Widgets/Task View visibility reflect selected P2 values.
            var task = new Rectangle(0, b.Bottom - 64, b.Width, 64);
            using (var bar = new SolidBrush(Color.FromArgb(238, 243, 250)))
                g.FillRectangle(bar, task);
            int count = 3 + (_p.TaskbarWidgets != 0 ? 1 : 0) +
                (_p.TaskbarTaskView != 0 ? 1 : 0) + (_p.TaskbarSearch != 0 ? 1 : 0);
            int startX = _p.TaskbarAlignment == 0 ? 26 : Math.Max(26, (b.Width - count * 45) / 2);
            for (var i = 0; i < count; i++)
            {
                using var tile = new SolidBrush(i == 0 ? Color.FromArgb(28, 107, 195) : Color.FromArgb(96, 127, 175));
                g.FillRoundedRectangle(tile, new Rectangle(startX + 45 * i, task.Top + 18, 27, 27), 6);
            }
            g.DrawString("Explorer: " + (_p.CompactMode == 1 ? "compact" : "regular") +
                " · Extensions: " + (_p.HideFileExt == 0 ? "shown" : "hidden"), small,
                secondary, Math.Min(b.Width - 290, 460), 115);
            g.DrawString("Hot Corners: " + (_p.HotCornersEnabled ? "on" : "off"), small,
                secondary, Math.Min(b.Width - 290, 460), 143);
        }
    }
}

internal static class RoundedCanvas
{
    internal static void FillRoundedRectangle(this Graphics graphics, Brush brush, Rectangle rect, int radius)
    {
        using var path = new GraphicsPath();
        var d = Math.Min(radius * 2, Math.Min(rect.Width, rect.Height));
        path.AddArc(rect.Left, rect.Top, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        graphics.FillPath(brush, path);
    }
}
