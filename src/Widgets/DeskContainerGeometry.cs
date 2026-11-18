namespace MotionDesk.Widgets;

// Ported from PecoFence v0.0.1, crates/core/src/geometry.rs (MIT).
// The upstream copyright and licence are in THIRD_PARTY_NOTICES.md.
internal sealed record DeskContainerGeometry(string Monitor, Rectangle Bounds, Rectangle WorkArea)
{
    public Rectangle Place(Rectangle workArea)
    {
        int width = Math.Clamp(Bounds.Width, 1, workArea.Width);
        int height = Math.Clamp(Bounds.Height, 1, workArea.Height);
        int x = PlaceAxis(Bounds.Left - WorkArea.Left, Bounds.Width, WorkArea.Width, width, workArea.Width);
        int y = PlaceAxis(Bounds.Top - WorkArea.Top, Bounds.Height, WorkArea.Height, height, workArea.Height);
        return new Rectangle(workArea.Left + x, workArea.Top + y, width, height);
    }

    private static int PlaceAxis(int start, int size, int oldExtent, int newSize, int newExtent)
    {
        int end = oldExtent - (start + size);
        double center = start + size / 2.0;
        double offset = Math.Abs(center - oldExtent / 2.0);
        double position = offset < Math.Min(start, end) * 0.5
            ? center / Math.Max(1, oldExtent) * newExtent - newSize / 2.0
            : start <= end ? start : newExtent - end - newSize;
        return (int)Math.Clamp(Math.Round(position), 0, Math.Max(0, newExtent - newSize));
    }
}
