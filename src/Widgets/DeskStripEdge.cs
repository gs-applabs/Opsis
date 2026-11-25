using System.Drawing;

namespace MotionDesk.Widgets;

public enum DockPosition { Bottom, Top, Left, Right }

internal static class DeskStripEdge
{
    internal static Rectangle RevealArea(Rectangle workArea, Rectangle dock, DockPosition position)
    {
        const int depth = 4;
        var edge = position switch
        {
            DockPosition.Bottom => new Rectangle(dock.X, workArea.Bottom - depth, dock.Width, depth),
            DockPosition.Top => new Rectangle(dock.X, workArea.Top, dock.Width, depth),
            DockPosition.Left => new Rectangle(workArea.Left, dock.Y, depth, dock.Height),
            _ => new Rectangle(workArea.Right - depth, dock.Y, depth, dock.Height)
        };
        return Rectangle.Intersect(workArea, edge);
    }
}
