using System.Drawing;

namespace Opsis.Containers;

/// <summary>
/// The window BEHAVIOUR of a container as pure functions (phase 6): snapping while moving, resizing limits, roll-up, keeping a container reachable after a monitor change.
/// No windows here: the host (layered window + Opsis.Glass pane) calls these and applies the result, so the rules are unit-tested without a desktop.
/// </summary>
public static class ContainerBehavior
{
    public const int MinWidth = 96, MinHeight = 64;

    /// <summary>
    /// Moves <paramref name="moving"/> so that its edges snap to the edges of the work area and of the other containers when closer than <paramref name="threshold"/> pixels
    /// (like the old fork and AquaSnap: edge to edge and edge to the same edge). Each axis snaps independently, to the nearest candidate.
    /// </summary>
    public static Rectangle Snap(Rectangle moving, IEnumerable<Rectangle> others, Rectangle workArea, int threshold)
    {
        if (threshold <= 0) return moving;
        var xs = new List<int> { workArea.Left, workArea.Right }; var ys = new List<int> { workArea.Top, workArea.Bottom };
        foreach (var o in others) { xs.Add(o.Left); xs.Add(o.Right); ys.Add(o.Top); ys.Add(o.Bottom); }
        var dx = Best(moving.Left, moving.Right, xs, threshold);
        var dy = Best(moving.Top, moving.Bottom, ys, threshold);
        return new Rectangle(moving.X + dx, moving.Y + dy, moving.Width, moving.Height);
    }

    /// <summary>The smallest shift (|d| &lt;= threshold) that puts the near or the far edge on one of the candidates; 0 when nothing is close.</summary>
    private static int Best(int near, int far, List<int> candidates, int threshold)
    {
        var best = 0; var bestAbs = int.MaxValue;
        foreach (var c in candidates)
        {
            foreach (var d in new[] { c - near, c - far })
            {
                var a = Math.Abs(d);
                if (a <= threshold && a < bestAbs) { best = d; bestAbs = a; }
            }
        }
        return best;
    }

    /// <summary>Resize by dragging an edge/corner: never smaller than the minimum, never off the work area.</summary>
    public static Rectangle Resize(Rectangle start, Size wanted, Rectangle workArea)
    {
        var w = Math.Clamp(wanted.Width, MinWidth, Math.Max(MinWidth, workArea.Right - start.X));
        var h = Math.Clamp(wanted.Height, MinHeight, Math.Max(MinHeight, workArea.Bottom - start.Y));
        return new Rectangle(start.Location, new Size(w, h));
    }

    /// <summary>Roll-up: the container shrinks to its title bar (the height of the title bar of THIS container) and remembers its full height.</summary>
    public static (Rectangle Bounds, int UnrolledHeight) RollUp(Rectangle full, int titleBarHeight) =>
        (new Rectangle(full.X, full.Y, full.Width, Math.Max(16, titleBarHeight)), full.Height);

    public static Rectangle Unroll(Rectangle rolled, int unrolledHeight, Rectangle workArea)
    {
        var h = Math.Clamp(unrolledHeight > 0 ? unrolledHeight : 240, MinHeight, workArea.Height);
        var y = Math.Min(rolled.Y, workArea.Bottom - h);                      // opening near the bottom edge grows upwards instead of leaving the screen
        return new Rectangle(rolled.X, Math.Max(workArea.Top, y), rolled.Width, h);
    }

    /// <summary>A container whose monitor changed or went away must stay reachable: at least <paramref name="reach"/> pixels of its title bar inside some work area.</summary>
    public static Rectangle KeepReachable(Rectangle bounds, IReadOnlyList<Rectangle> workAreas, int reach = 48)
    {
        if (workAreas.Count == 0) return bounds;
        foreach (var w in workAreas)
        {
            var title = new Rectangle(bounds.X, bounds.Y, bounds.Width, 24);
            var visible = Rectangle.Intersect(title, w);
            if (visible.Width >= Math.Min(reach, bounds.Width) && visible.Height >= 8) return bounds;
        }
        var primary = workAreas[0];
        return new Rectangle(Math.Clamp(bounds.X, primary.Left, Math.Max(primary.Left, primary.Right - bounds.Width)), Math.Clamp(bounds.Y, primary.Top, Math.Max(primary.Top, primary.Bottom - 24)), bounds.Width, bounds.Height);
    }

    /// <summary>A locked container ignores move and resize requests (the lock glyph in the title bar).</summary>
    public static Rectangle ApplyLock(bool locked, Rectangle current, Rectangle requested) => locked ? current : requested;

    /// <summary>Which container is hit by a point, top-most first (later in the list = on top); a rolled-up container only answers on its title bar.</summary>
    public static int HitTest(IReadOnlyList<(Rectangle Bounds, bool Rolled, bool Hidden)> windows, Point p)
    {
        for (var i = windows.Count - 1; i >= 0; i--)
            if (!windows[i].Hidden && windows[i].Bounds.Contains(p)) return i;
        return -1;
    }
}
