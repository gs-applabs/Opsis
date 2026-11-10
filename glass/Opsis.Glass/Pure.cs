using System.Drawing;

namespace Opsis.Glass;

/// <summary>The performance tier, same meaning as in the rest of Opsis (High / Balanced / Low).</summary>
public enum GlassTier { Low = 0, Balanced = 1, High = 2 }

/// <summary>What is true about the machine right now. The glass layer asks <see cref="GlassPolicy.Evaluate"/>; nothing else decides about work.</summary>
public sealed record GlassConditions
{
    public GlassTier Tier { get; init; } = GlassTier.High;
    public bool LiveBackdrop { get; init; }          // an animated wallpaper is running behind
    public bool WindowMovingBehind { get; init; }    // something behind a pane changes for a moment (a window drag, a resize)
    public bool Idle { get; init; }                  // no input for a while and nothing animating
    public bool Locked { get; init; }
    public bool RemoteSession { get; init; }
    public bool FullscreenApp { get; init; }
    public bool BatterySaver { get; init; }
    public bool AllMinimizedOrHidden { get; init; }
    public bool SafeMode { get; init; }
    public bool DuplicationAvailable { get; init; } = true;
    /// <summary>Keep the panes as they are (no capture, no render): for screenshots of the glass and for tests.</summary>
    public bool Frozen { get; init; }
}

public enum SuspendReason { None, Locked, RemoteSession, FullscreenApp, AllHidden, SafeMode }

/// <summary>The decision: what to do this moment. <see cref="CaptureNow"/> false and <see cref="Static"/> true = keep the last frame, no GPU work.</summary>
public sealed record GlassDecision(bool Suspended, SuspendReason Reason, bool CaptureNow, int MaxFps, bool Refraction, bool Blur, bool Static);

public static class GlassPolicy
{
    /// <summary>
    /// The resource rules of the new core in one place (design doc 6.3): capture only when something changes behind; High = refraction up to 60 fps,
    /// Balanced = blur only, 30 fps; Low = a static frosted tint (no capture); suspend on lock, remote session, fullscreen app, everything hidden and safe mode;
    /// battery saver and idle lower the rate; no duplication available (remote, secure desktop) = static tint.
    /// </summary>
    public static GlassDecision Evaluate(GlassConditions c)
    {
        if (c.Frozen) return new(false, SuspendReason.None, false, 0, false, false, false);
        if (c.SafeMode) return new(true, SuspendReason.SafeMode, false, 0, false, false, true);
        if (c.Locked) return new(true, SuspendReason.Locked, false, 0, false, false, true);
        if (c.RemoteSession) return new(true, SuspendReason.RemoteSession, false, 0, false, false, true);
        if (c.FullscreenApp) return new(true, SuspendReason.FullscreenApp, false, 0, false, false, true);
        if (c.AllMinimizedOrHidden) return new(true, SuspendReason.AllHidden, false, 0, false, false, true);

        if (c.Tier == GlassTier.Low || !c.DuplicationAvailable)
            return new(false, SuspendReason.None, false, 0, false, false, true);            // static frosted tint, painted once

        // Capture is CHANGE-DRIVEN by the compositor (a duplication frame only arrives when something behind changed), so "capture allowed" costs nothing while
        // the background is still: the loop just times out. The rate only caps how often a CHANGED frame may be rendered.
        var changing = c.LiveBackdrop || c.WindowMovingBehind;
        var fps = c.Tier == GlassTier.High ? 60 : 30;
        if (!changing) fps = c.Idle ? 4 : 15;                                                // static wallpaper: just watch for windows moving behind
        if (c.BatterySaver) fps = Math.Min(fps, 20);
        return new(false, SuspendReason.None, true, fps, c.Tier == GlassTier.High, true, false);
    }
}

/// <summary>Integer rectangle maths of the shared capture (ONE capture for the union of all panes of a monitor).</summary>
public static class RectMath
{
    public static Rectangle Union(IEnumerable<Rectangle> rects)
    {
        var any = false; int l = 0, t = 0, r = 0, b = 0;
        foreach (var x in rects)
        {
            if (x.Width <= 0 || x.Height <= 0) continue;
            if (!any) { l = x.Left; t = x.Top; r = x.Right; b = x.Bottom; any = true; continue; }
            l = Math.Min(l, x.Left); t = Math.Min(t, x.Top); r = Math.Max(r, x.Right); b = Math.Max(b, x.Bottom);
        }
        return any ? Rectangle.FromLTRB(l, t, r, b) : Rectangle.Empty;
    }

    /// <summary>Where a pane's backdrop starts inside the union texture (clamped so a pane partly off the monitor never reads outside it).</summary>
    public static Point OffsetIn(Rectangle union, Rectangle pane) => new(pane.X - union.X, pane.Y - union.Y);

    /// <summary>The part of <paramref name="r"/> that lies on <paramref name="monitor"/> (empty when none).</summary>
    public static Rectangle ClipTo(Rectangle monitor, Rectangle r) => Rectangle.Intersect(monitor, r);

    /// <summary>The union texture is only re-created when it must grow, or shrinks a lot (hysteresis: no churn while a pane is dragged).</summary>
    public static bool NeedsNewTexture(Size current, Size wanted) =>
        wanted.Width > current.Width || wanted.Height > current.Height || (current.Width > wanted.Width * 2 && current.Height > wanted.Height * 2);
}

/// <summary>A small thread-safe LRU with a hard limit; GPU objects stored in it are disposed when evicted (plan 0.4d: no cache without a maximum).</summary>
public sealed class BoundedLru<TKey, TValue> : IDisposable where TKey : notnull
{
    private readonly object gate = new();
    private readonly Dictionary<TKey, LinkedListNode<(TKey Key, TValue Value)>> map = new();
    private readonly LinkedList<(TKey Key, TValue Value)> order = new();

    public BoundedLru(int capacity) { if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity)); Capacity = capacity; }

    public int Capacity { get; }
    public int Count { get { lock (gate) return map.Count; } }

    public bool TryGet(TKey key, out TValue value)
    {
        lock (gate)
        {
            if (map.TryGetValue(key, out var n)) { order.Remove(n); order.AddFirst(n); value = n.Value.Value; return true; }
        }
        value = default!; return false;
    }

    public TValue GetOrCreate(TKey key, Func<TKey, TValue> create)
    {
        if (TryGet(key, out var v)) return v;
        var made = create(key);
        lock (gate)
        {
            if (map.TryGetValue(key, out var ex)) { (made as IDisposable)?.Dispose(); order.Remove(ex); order.AddFirst(ex); return ex.Value.Value; }
            map[key] = order.AddFirst((key, made));
            while (map.Count > Capacity && order.Last is { } last) { order.RemoveLast(); map.Remove(last.Value.Key); (last.Value.Value as IDisposable)?.Dispose(); }
        }
        return made;
    }

    public void Clear() { lock (gate) { foreach (var e in order) (e.Value as IDisposable)?.Dispose(); order.Clear(); map.Clear(); } }
    public void Dispose() => Clear();
}

/// <summary>How a glass pane looks. Plain data; the host maps its settings (Opsis.UI.GlassSettings) onto it.</summary>
public sealed record GlassStyle
{
    public Color Tint { get; init; } = Color.FromArgb(0x99, 0xCC, 0xFF);
    public float TintAlpha { get; init; } = 0.22f;       // 0..1
    public float BlurSigma { get; init; } = 5f;           // DIPs; 0 = no blur
    public int CornerRadius { get; init; } = 16;
    public float Gloss { get; init; } = 0.55f;            // 0..1: rim brightness, specular highlight, refraction strength
    public float Rim { get; init; } = 0.5f;               // 0..1

    /// <summary>Refraction strength in pixels at the bezel, from the glossy intensity.</summary>
    public float RefractionPx => 12f + 24f * Math.Clamp(Gloss, 0f, 1f);

    /// <summary>Width of the refracting bezel in pixels.</summary>
    public int BezelPx => Math.Max(8, (int)(CornerRadius * 1.6f));

    /// <summary>Key of the caches (everything that changes the pre-computed maps).</summary>
    public (int W, int H, int Radius, int Gloss100, int Dpi) MapKey(int w, int h, int dpi) => (w, h, CornerRadius, (int)Math.Round(Gloss * 100), dpi);
}

/// <summary>The displacement map of a pane (R,G = offset around 0.5), built ONCE per (size, radius, gloss, DPI) and cached.</summary>
public static class DisplacementMapBuilder
{
    /// <summary>BGRA bytes: B = 128, G = y offset, R = x offset; 128 = no displacement. Zero in the middle, strongest at the bezel, inward.</summary>
    public static byte[] Build(int w, int h, int radius, int bezel)
    {
        var px = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var d = DistanceInside(x, y, w, h, radius);
                float ox = 0, oy = 0;
                if (d >= 0 && d < bezel)
                {
                    var t = 1f - d / bezel; var k = t * t;
                    ox = (w / 2f - x) / (w / 2f) * k; oy = (h / 2f - y) / (h / 2f) * k;
                }
                var o = (y * w + x) * 4;
                px[o] = 128; px[o + 1] = (byte)Math.Clamp(128 + oy * 127, 0, 255); px[o + 2] = (byte)Math.Clamp(128 + ox * 127, 0, 255); px[o + 3] = 255;
            }
        return px;
    }

    /// <summary>Distance from the edge of a rounded rectangle, negative outside (the transparent corners).</summary>
    public static float DistanceInside(int x, int y, int w, int h, float radius)
    {
        var dx = Math.Min(x, w - 1 - x); var dy = Math.Min(y, h - 1 - y);
        if (dx < radius && dy < radius) { var cx = radius - dx; var cy = radius - dy; return radius - MathF.Sqrt(cx * cx + cy * cy); }
        return Math.Min(dx, dy);
    }
}
