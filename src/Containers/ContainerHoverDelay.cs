using System.Drawing;

namespace MotionDesk.Containers;

internal sealed class ContainerHoverDelay
{
    private bool? _target;
    private long _since;

    internal bool? Update(bool collapsed, bool pointerInside, bool busy, int delay, long now)
    {
        bool target = !pointerInside;
        if (busy || collapsed == target)
        {
            Reset();
            return null;
        }
        if (_target != target)
        {
            _target = target;
            _since = now;
            return null;
        }
        if (now - _since < Math.Clamp(delay, 50, 3000)) return null;
        Reset();
        return target;
    }

    internal static Rectangle TitleBounds(Rectangle expanded, int titleHeight) =>
        new(expanded.Location, new Size(expanded.Width, Math.Clamp(titleHeight + 2, 1, expanded.Height)));

    internal void Reset() => _target = null;
}
