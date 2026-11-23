using System.Drawing;

namespace MotionDesk.UI;

internal readonly record struct DesktopPreviewGeometry(Rectangle Screen, Rectangle Canvas)
{
    internal Rectangle Fit => FitInside(Screen.Size, Canvas);

    internal Rectangle ToPreview(Rectangle bounds)
    {
        var fit = Fit;
        if (fit.IsEmpty) return Rectangle.Empty;
        double x = (double)fit.Width / Screen.Width, y = (double)fit.Height / Screen.Height;
        return new Rectangle(fit.X + (int)Math.Round((bounds.X - Screen.X) * x),
            fit.Y + (int)Math.Round((bounds.Y - Screen.Y) * y),
            Math.Max(1, (int)Math.Round(bounds.Width * x)), Math.Max(1, (int)Math.Round(bounds.Height * y)));
    }

    internal Point Move(Rectangle original, Point previewDelta)
    {
        var fit = Fit;
        if (fit.IsEmpty) return original.Location;
        var destination = new Point(original.X + (int)Math.Round(previewDelta.X * (double)Screen.Width / fit.Width),
            original.Y + (int)Math.Round(previewDelta.Y * (double)Screen.Height / fit.Height));
        return new Point(Math.Clamp(destination.X, Screen.Left, Math.Max(Screen.Left, Screen.Right - original.Width)),
            Math.Clamp(destination.Y, Screen.Top, Math.Max(Screen.Top, Screen.Bottom - original.Height)));
    }

    internal static Rectangle FitInside(Size image, Rectangle frame)
    {
        if (image.Width < 1 || image.Height < 1 || frame.Width < 1 || frame.Height < 1) return Rectangle.Empty;
        double scale = Math.Min((double)frame.Width / image.Width, (double)frame.Height / image.Height);
        var size = new Size(Math.Max(1, (int)Math.Round(image.Width * scale)), Math.Max(1, (int)Math.Round(image.Height * scale)));
        return new Rectangle(frame.X + (frame.Width - size.Width) / 2, frame.Y + (frame.Height - size.Height) / 2, size.Width, size.Height);
    }
}
