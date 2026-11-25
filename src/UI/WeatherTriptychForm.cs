using System.Drawing.Drawing2D;
using MotionDesk.Widgets;

namespace MotionDesk.UI;

/// <summary>Lightweight simultaneous atmospheric colour preview; independent of rendering loop.</summary>
internal sealed class WeatherTriptychForm : Form
{
    private readonly string[] names;
    private readonly Bitmap? previewPhoto;
    internal WeatherTriptychForm()
    {
        Text = MainWindow.StudioText("Dawn · noon · night preview", "Προεπισκόπηση χαραυγής · μεσημεριού · νύχτας");
        StartPosition = FormStartPosition.CenterParent; ClientSize = new Size(960, 360); MinimumSize = new Size(690, 300);
        BackColor = UiTheme.PageBackground;
        names = MainWindow.StudioGreek ? ["Χαραυγή", "Μεσημέρι", "Νύχτα"] : ["Dawn", "Noon", "Night"];
        DoubleBuffered = true;
        try
        {
            var path = WallpaperSettings.Load().ImagePath; // user-selected real photo, when available
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path) &&
                new[] { ".jpg", ".jpeg", ".png", ".webp", ".bmp" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            {
                // Open only the active photo; don't synthesize or replace landmarks.
                using var raw = Image.FromFile(path);
                int width = Math.Min(1200, raw.Width);
                int height = Math.Max(1, (int)Math.Round(raw.Height * (double)width / raw.Width));
                previewPhoto = new Bitmap(width, height);
                using var g = Graphics.FromImage(previewPhoto);
                g.DrawImage(raw, new Rectangle(0, 0, width, height));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException || ex is OutOfMemoryException)
        { previewPhoto?.Dispose(); }
        FormClosed += (_, _) => previewPhoto?.Dispose();
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
            if (previewPhoto is { } photo)
            {
                float sourceAspect = (float)photo.Width / photo.Height;
                float targetAspect = (float)rect.Width / rect.Height;
                RectangleF source = sourceAspect > targetAspect
                    ? new RectangleF((photo.Width - photo.Height * targetAspect) / 2, 0, photo.Height * targetAspect, photo.Height)
                    : new RectangleF(0, (photo.Height - photo.Width / targetAspect) / 2, photo.Width, photo.Width / targetAspect);
                e.Graphics.DrawImage(photo, rect, source.X, source.Y, source.Width, source.Height, GraphicsUnit.Pixel);
                using var grade = new SolidBrush(Color.FromArgb(i == 0 ? 95 : i == 1 ? 20 : 175,
                    i == 0 ? 226 : i == 1 ? 210 : 8,
                    i == 0 ? 116 : i == 1 ? 222 : 14,
                    i == 0 ? 87 : i == 1 ? 245 : 45));
                e.Graphics.FillRectangle(grade, rect);
            }
            var circleX = rect.Left+rect.Width*(i==0 ? .26f : i==1 ? .52f : .72f);
            var circleY = rect.Top+rect.Height*(i==0 ? .67f : i==1 ? .30f : .29f);
            using var circle = new SolidBrush(i==2 ? Color.FromArgb(229,232,246) : Color.FromArgb(255,236,184));
            e.Graphics.FillEllipse(circle,circleX-19,circleY-19,38,38);
            var pts = new[] {new Point(rect.Left,rect.Bottom),new Point(rect.Left,(int)(rect.Top+height*.82)),
                new Point(rect.Left+width/3,(int)(rect.Top+height*.79)),new Point(rect.Left+width*2/3,(int)(rect.Top+height*.86)),
                new Point(rect.Right,(int)(rect.Top+height*.77)),new Point(rect.Right,rect.Bottom)};
            using var silhouette = new SolidBrush(Color.FromArgb(i==2 ? 9:37,i==2 ? 15:44,i==2 ? 32:70));
            if (previewPhoto == null) e.Graphics.FillPolygon(silhouette,pts);
            using var font = new Font("Segoe UI",12,FontStyle.Bold);
            using var ink = new SolidBrush(Color.White);
            e.Graphics.DrawString(names[i],font,ink,rect.Left+16,rect.Top+15);
        }
    }
}
