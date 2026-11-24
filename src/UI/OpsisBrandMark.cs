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
            using var body = new LinearGradientBrush(new Rectangle(0,0,100,100),
                Color.FromArgb(66,106,140), Color.FromArgb(6,22,37), 65f);
            g.FillPath(body,disc);
            using var blue = Blue();
            using var ribbon = Ribbon();
            using var shadow = new SolidBrush(Color.FromArgb(175,0,16,34));
            var shadowState = g.Save();
            g.TranslateTransform(0,1.5f);
            g.FillPath(shadow,blue);
            g.Restore(shadowState);
            using var blueBrush = new LinearGradientBrush(new Rectangle(0,0,100,100),
                Color.FromArgb(199,247,255), Color.FromArgb(64,200,255), 65f);
            blueBrush.InterpolationColors = new ColorBlend {
                Colors = new[]{Color.FromArgb(199,247,255), Color.FromArgb(56,199,255),
                    Color.FromArgb(0,142,232), Color.FromArgb(7,85,190),
                    Color.FromArgb(0,142,232), Color.FromArgb(64,200,255)},
                Positions = new[]{0f,.12f,.35f,.64f,.84f,1f}
            };
            g.FillPath(blueBrush,blue);
            using var bevel = new Pen(Color.FromArgb(100,109,215,255),.55f) { LineJoin=LineJoin.Round };
            g.DrawPath(bevel,blue);
            shadowState = g.Save();
            g.TranslateTransform(1.1f,1.5f);
            g.FillPath(shadow,ribbon);
            g.Restore(shadowState);
            using var whiteBrush = new LinearGradientBrush(new Rectangle(0,0,100,100),
                Color.White, Color.FromArgb(144,184,213), 65f);
            whiteBrush.InterpolationColors = new ColorBlend {
                Colors = new[]{Color.White,Color.FromArgb(248,253,255),Color.FromArgb(210,230,243),
                    Color.White,Color.FromArgb(144,184,213)},
                Positions = new[]{0f,.3f,.55f,.73f,1f}
            };
            g.FillPath(whiteBrush,ribbon);
            using var ribbonEdge = new Pen(Color.FromArgb(190,242,251,255),.38f) { LineJoin=LineJoin.Round };
            g.DrawPath(ribbonEdge,ribbon);
            using var specular = new GraphicsPath();
            specular.AddBezier(6,36,10,16,29,5,50,4);
            specular.AddBezier(50,4,70,4,87,15,94,32);
            specular.AddBezier(94,32,74,24,55,26,39,33);
            specular.AddBezier(39,33,25,39,14,40,6,36); specular.CloseFigure();
            using var gloss = new LinearGradientBrush(new Rectangle(0,0,100,42),
                Color.FromArgb(100,255,255,255),Color.FromArgb(0,188,240,255),90f);
            g.FillPath(gloss,specular);
            using var rim = new Pen(Color.FromArgb(165,170,231,255),.9f);
            g.DrawEllipse(rim,3.8f,3.8f,92.4f,92.4f);
            using var shineArc = new Pen(Color.FromArgb(195,235,251,255),.75f) { StartCap=LineCap.Round,EndCap=LineCap.Round };
            g.DrawArc(shineArc,6,6,88,88,213,56);
            using var lowerArc = new Pen(Color.FromArgb(170,90,226,255),.75f) { StartCap=LineCap.Round,EndCap=LineCap.Round };
            g.DrawArc(lowerArc,4,4,92,92,20,50);
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
