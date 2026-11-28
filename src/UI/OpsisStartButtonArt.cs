using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MotionDesk.UI;

/// <summary>Reusable, GDI+ rendered Opsis Start emblems. No shell hooks, DLL injection or bitmap assets.</summary>
public static class OpsisStartButtonArt
{
    public static readonly string[] Styles = { "Opsis Glass", "Crystal", "Aurora", "Obsidian", "Minimal", "Classic", "Win11 Fluent", "Win11 Glass", "Win11 Dark", "Win11 Sharp", "Opsis Square" };
    public static bool IsValid(string? style) => Array.IndexOf(Styles, style) >= 0;

    public static void Draw(Graphics graphics, Rectangle bounds, string? style, Icon? applicationIcon = null)
    {
        if (bounds.Width < 8 || bounds.Height < 8) return;
        style = style != null && IsValid(style) ? style : Styles[0];
        var g = graphics;
        var state = g.Save();
        try
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            var side = Math.Min(bounds.Width - 4, bounds.Height - 4);
            var rect = new Rectangle(bounds.X + (bounds.Width - side) / 2, bounds.Y + (bounds.Height - side) / 2, side, side);
            if (style == "Opsis Glass" && applicationIcon != null)
            {
                g.DrawIcon(applicationIcon, rect);
                return;
            }
            if (style.StartsWith("Win11 ", StringComparison.Ordinal) || style == "Opsis Square")
            {
                var sharp = style == "Win11 Sharp";
                var useLogo = style == "Opsis Square";
                var inset = Math.Max(2, side / 12);
                var square = Rectangle.Inflate(rect, -inset, -inset);
                var radius = sharp ? Math.Max(2, side / 14f) : Math.Max(4, side / 5f);
                using var path = RoundedSquare(square, radius);
                var squareTop = style switch
                {
                    "Win11 Dark" => Color.FromArgb(246, 52, 62, 86),
                    "Win11 Fluent" => Color.FromArgb(250, 73, 165, 237),
                    "Win11 Glass" => Color.FromArgb(230, 191, 235, 255),
                    "Win11 Sharp" => Color.FromArgb(248, 58, 116, 201),
                    _ => Color.FromArgb(238, 135, 222, 255)
                };
                var squareBottom = style switch
                {
                    "Win11 Dark" => Color.FromArgb(252, 13, 17, 31),
                    "Win11 Glass" => Color.FromArgb(237, 31, 100, 181),
                    _ => Color.FromArgb(248, 20, 53, 138)
                };
                using var fill = new LinearGradientBrush(square, squareTop, squareBottom, 87f);
                using var border = new Pen(Color.FromArgb(195, 219, 244, 255), Math.Max(1f, side / 36f));
                g.FillPath(fill, path);
                g.DrawPath(border, path);
                // Flat Windows 11-style surface: no curved shine, ellipse or top highlight.
                // Keep the subtle linear gradient and border for depth.
                DrawMark(g, square, Color.White, useLogo, applicationIcon);
                return;
            }
            if (style == "Minimal")
            {
                using var pen = new Pen(Color.FromArgb(235, 211, 245, 255), Math.Max(1.4f, side / 18f));
                g.DrawEllipse(pen, Rectangle.Inflate(rect, -3, -3));
                DrawMark(g, rect, Color.White, false, applicationIcon);
                return;
            }
            Color top, bottom, edge;
            switch (style)
            {
                case "Crystal": top = Color.FromArgb(230, 220, 250, 255); bottom = Color.FromArgb(235, 33, 115, 183); edge = Color.LightCyan; break;
                case "Aurora": top = Color.FromArgb(238, 96, 225, 189); bottom = Color.FromArgb(240, 39, 66, 159); edge = Color.FromArgb(230, 137, 255, 222); break;
                case "Obsidian": top = Color.FromArgb(244, 66, 75, 106); bottom = Color.FromArgb(251, 9, 13, 32); edge = Color.FromArgb(220, 145, 155, 210); break;
                case "Classic": top = Color.FromArgb(245, 48, 141, 213); bottom = Color.FromArgb(250, 11, 54, 130); edge = Color.White; break;
                default: top = Color.FromArgb(222, 161, 232, 255); bottom = Color.FromArgb(231, 22, 94, 178); edge = Color.FromArgb(243, 225, 249, 255); break;
            }
            var sphere = Rectangle.Inflate(rect, -2, -2);
            using var background = new LinearGradientBrush(sphere, top, bottom, 72f);
            using var outline = new Pen(edge, Math.Max(1.1f, side / 24f));
            g.FillEllipse(background, sphere);
            g.DrawEllipse(outline, sphere);
            var highlight = new Rectangle(sphere.X + side / 7, sphere.Y + side / 10, sphere.Width - side / 3, Math.Max(4, side / 3));
            using var glass = new LinearGradientBrush(highlight, Color.FromArgb(155, Color.White), Color.FromArgb(8, Color.White), 90f);
            g.FillEllipse(glass, highlight);
            using var rim = new Pen(Color.FromArgb(75, 255, 255, 255), 1);
            g.DrawArc(rim, sphere.X + 4, sphere.Y + 4, sphere.Width - 8, sphere.Height - 8, 20, 140);
            DrawMark(g, rect, Color.White, style == "Opsis Glass", applicationIcon);
        }
        finally { g.Restore(state); }
    }

    private static GraphicsPath RoundedSquare(Rectangle r, float radius)
    {
        var p = new GraphicsPath();
        var d = Math.Min(Math.Min(r.Width, r.Height), radius * 2);
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    private static void DrawMark(Graphics g, Rectangle r, Color color, bool useLogo, Icon? applicationIcon)
    {
        if (useLogo && applicationIcon != null)
        {
            var size = Math.Max(9, (int)(r.Width * .53));
            var box = new Rectangle(r.X + (r.Width - size) / 2, r.Y + (r.Height - size) / 2, size, size);
            g.DrawIcon(applicationIcon, box);
            return;
        }
        // Four bright panes forming an original, clean Opsis emblem.
        var middleX = r.X + r.Width / 2f;
        var middleY = r.Y + r.Height / 2f;
        var unit = r.Width * .19f;
        var gap = Math.Max(1f, r.Width * .045f);
        using var brush = new SolidBrush(color);
        foreach (var x in new[] { middleX - unit - gap / 2, middleX + gap / 2 })
            foreach (var y in new[] { middleY - unit - gap / 2, middleY + gap / 2 })
                g.FillRectangle(brush, x, y, unit, unit);
    }

    public static void PaintButton(object? sender, PaintEventArgs e, string style, Icon? icon = null)
    {
        if (sender is not Control control) return;
        e.Graphics.Clear(control.BackColor);
        Draw(e.Graphics, control.ClientRectangle, style, icon);
    }
}
