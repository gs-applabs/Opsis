using System.Drawing.Drawing2D;
using MotionDesk.Widgets;

namespace MotionDesk.UI;

/// <summary>Lightweight simultaneous atmospheric colour preview; independent of rendering loop.</summary>
internal sealed class WeatherTriptychForm : Form
{
    private readonly string[] names;
    internal WeatherTriptychForm()
    {
        Text = MainWindow.StudioText("Dawn · noon · night preview", "Προεπισκόπηση χαραυγής · μεσημεριού · νύχτας");
        StartPosition = FormStartPosition.CenterParent; ClientSize = new Size(960, 360); MinimumSize = new Size(690, 300);
        BackColor = UiTheme.PageBackground;
        names = MainWindow.StudioGreek ? ["Χαραυγή", "Μεσημέρι", "Νύχτα"] : ["Dawn", "Noon", "Night"];
        DoubleBuffered = true;
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var colorSets = new [] { (Color.FromArgb(35,55,118), Color.FromArgb(250,147,111)),
            (Color.FromArgb(34,107,199),Color.FromArgb(185,223,248)),
            (Color.FromArgb(5,9,25),Color.FromArgb(20,29,66)) };
        var pad = 14; var width = Math.Max(1,(ClientSize.Width - pad*4)/3); var height = Math.Max(1,ClientSize.Height-2*pad);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        for (int i=0;i<3;i++)
        {
            var rect = new Rectangle(pad+i*(width+pad),pad,width,height);
            using var gradient = new LinearGradientBrush(rect, colorSets[i].Item1,colorSets[i].Item2,LinearGradientMode.Vertical);
            e.Graphics.FillRectangle(gradient,rect);
            var circleX = rect.Left+rect.Width*(i==0 ? .26f : i==1 ? .52f : .72f);
            var circleY = rect.Top+rect.Height*(i==0 ? .67f : i==1 ? .30f : .29f);
            using var circle = new SolidBrush(i==2 ? Color.FromArgb(229,232,246) : Color.FromArgb(255,236,184));
            e.Graphics.FillEllipse(circle,circleX-19,circleY-19,38,38);
            var pts = new[] {new Point(rect.Left,rect.Bottom),new Point(rect.Left,(int)(rect.Top+height*.82)),
                new Point(rect.Left+width/3,(int)(rect.Top+height*.79)),new Point(rect.Left+width*2/3,(int)(rect.Top+height*.86)),
                new Point(rect.Right,(int)(rect.Top+height*.77)),new Point(rect.Right,rect.Bottom)};
            using var silhouette = new SolidBrush(Color.FromArgb(i==2 ? 9:37,i==2 ? 15:44,i==2 ? 32:70));
            e.Graphics.FillPolygon(silhouette,pts);
            using var font = new Font("Segoe UI",12,FontStyle.Bold);
            using var ink = new SolidBrush(Color.White);
            e.Graphics.DrawString(names[i],font,ink,rect.Left+16,rect.Top+15);
        }
    }
}
