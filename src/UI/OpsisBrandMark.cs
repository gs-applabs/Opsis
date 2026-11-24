using System.Drawing;
using System.Drawing.Drawing2D;

namespace MotionDesk.UI;

/// <summary>Opsis 3.0 blue-white circular ribbon; fixed geometry with a subtle travelling highlight.</summary>
internal static class OpsisBrandMark
{
    private static GraphicsPath Blue()
    {
        var p = new GraphicsPath();
        p.StartFigure(); p.AddBezier(41,3,19,5,5,23,3,43); p.AddBezier(3,43,1,54,12,60,23,58);
        p.AddBezier(23,58,29,57,33,53,28,48); p.AddBezier(28,48,20,41,20,34,29,32);
        p.AddLine(29,32,41,32); p.AddBezier(41,32,46,31,49,27,48,23);
        p.AddBezier(48,23,46,19,37,19,32,19); p.AddBezier(32,19,38,13,41,7,41,3); p.CloseFigure();
        p.StartFigure(); p.AddBezier(3,55,9,66,21,67,31,61); p.AddLine(31,61,45,46);
        p.AddBezier(45,46,38,65,26,80,17,85); p.AddBezier(17,85,8,76,4,65,3,55); p.CloseFigure();
        p.StartFigure(); p.AddBezier(95,30,104,51,96,78,78,89); p.AddBezier(78,89,68,96,54,98,41,95);
        p.AddBezier(41,95,47,81,58,72,72,65); p.AddBezier(72,65,85,60,94,55,95,30); p.CloseFigure();
        return p;
    }

    private static GraphicsPath Ribbon()
    {
        var p = new GraphicsPath();
        p.AddBezier(47,3,66,5,79,13,80,26); p.AddBezier(80,26,81,39,68,46,57,55);
        p.AddBezier(57,55,52,60,49,64,47,69); p.AddBezier(47,69,63,64,79,60,92,59);
        p.AddBezier(92,59,80,65,66,70,54,75); p.AddBezier(54,75,42,80,36,87,35,96);
        p.AddBezier(35,96,27,94,20,90,14,84); p.AddBezier(14,84,27,75,31,62,39,51);
        p.AddBezier(39,51,49,38,62,32,63,22); p.AddBezier(63,22,64,13,56,7,47,3); p.CloseFigure();
        return p;
    }

    internal static void Draw(Graphics g, RectangleF bounds, double seconds = 0, bool animate = false)
    {
        float size = Math.Min(bounds.Width, bounds.Height);
        if (size <= 0) return;
        var state = g.Save();
        try
        {
            g.TranslateTransform(bounds.X + (bounds.Width-size)/2, bounds.Y + (bounds.Height-size)/2);
            g.ScaleTransform(size/100f,size/100f);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var disc = new GraphicsPath();
            disc.AddEllipse(3,3,94,94);
            g.SetClip(disc, CombineMode.Intersect);
            using var dark = new SolidBrush(Color.FromArgb(7,27,45));
            g.FillPath(dark,disc);
            using var blue = Blue();
            using var blueBrush = new LinearGradientBrush(new Rectangle(0,0,100,100),
                Color.FromArgb(19,185,255), Color.FromArgb(0,103,219), 57f);
            g.FillPath(blueBrush,blue);
            using var ribbon = Ribbon();
            using var whiteBrush = new LinearGradientBrush(new Rectangle(0,0,100,100),
                Color.White, Color.FromArgb(217,242,255), 57f);
            g.FillPath(whiteBrush,ribbon);
            if (animate)
            {
                float position = (float)(0.5-0.5*Math.Cos(seconds*0.9));
                var shineState=g.Save();
                g.SetClip(blue,CombineMode.Intersect);
                float y=-30+position*135;
                using var shine=new LinearGradientBrush(new RectangleF(0,y,100,30),Color.Transparent,Color.Transparent,90f);
                shine.InterpolationColors=new ColorBlend {
                    Colors=new[]{Color.Transparent,Color.FromArgb(90,165,237,255),Color.Transparent},
                    Positions=new[]{0f,.5f,1f}
                };
                g.FillRectangle(shine,0,y,100,30);
                g.Restore(shineState);
            }
        }
        finally { g.Restore(state); }
    }
}
