using System.Drawing.Drawing2D;
namespace MotionDesk.UI;
internal static partial class UiTheme
{
        public static GraphicsPath RoundedPath(Rectangle bounds, int radius)
        {
            var path = new GraphicsPath();
            if (bounds.Width <= 0 || bounds.Height <= 0) return path;
            radius = Math.Clamp(radius, 0, Math.Min(bounds.Width, bounds.Height) / 2);
            if (radius == 0) { path.AddRectangle(bounds); return path; }
            int d = radius * 2;
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

}
