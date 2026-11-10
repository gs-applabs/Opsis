using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.Direct2D1;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DirectComposition;
using Vortice.DXGI;
using Vortice.Mathematics;
using Size = System.Drawing.Size;
using Point = System.Drawing.Point;
using Rectangle = System.Drawing.Rectangle;
using D2D = Vortice.Direct2D1;
using DC = Vortice.DCommon;

namespace Opsis.Glass;

/// <summary>Counters of the glass layer (for the baseline, the budgets and the tests of the host).</summary>
public sealed record GlassStats(long Captures, long Renders, long StaticRenders, long SkippedNoChange, long DeviceResets, int Panes, string LastError);

/// <summary>
/// The glass layer: owns the GPU devices and ONE thread that runs the message loop of the glass windows and the render loop.
/// Panes are real per-pixel-alpha windows (DirectComposition, premultiplied alpha, excluded from capture, click-through option).
/// Work is decided only by <see cref="GlassPolicy"/> (design doc 6.3): capture when something changes behind, one capture per monitor for the union of its panes,
/// everything on the GPU, bounded caches, no per-frame allocation in the render loop, suspend on lock / remote / fullscreen / hidden / safe mode.
/// </summary>
public sealed class GlassManager : IDisposable
{
    private readonly ConcurrentQueue<Action> commands = new();
    private readonly AutoResetEvent wake = new(false);
    private readonly Thread thread;
    private readonly Dictionary<int, Pane> panes = new();
    private readonly ManualResetEventSlim started = new();
    private volatile bool stop;
    private int nextId = 1;
    private GlassConditions conditions = new();
    private volatile bool backdropChanged;
    private string lastError = "";
    private long captures, renders, staticRenders, skipped, resets;

    private Devices? gpu;
    private readonly Dictionary<int, MonitorCapture> monitors = new();
    private readonly BoundedLru<(int W, int H, int Radius, int Gloss100, int Dpi), ID2D1Bitmap1> maps = new(16);

    /// <summary>False: panes are visible in screenshots and recordings, but then they also see themselves in the capture (not supported). Default true.</summary>
    public bool ExcludeFromCapture { get; init; } = true;

    public GlassManager()
    {
        thread = new Thread(Run) { IsBackground = true, Name = "OpsisGlass" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        started.Wait(5000);
    }

    // ------------------------------------------------------------------ public API (any thread)
    public GlassStats Stats => new(Interlocked.Read(ref captures), Interlocked.Read(ref renders), Interlocked.Read(ref staticRenders), Interlocked.Read(ref skipped), Interlocked.Read(ref resets), PaneCount, lastError);
    private int paneCount; public int PaneCount => Volatile.Read(ref paneCount);

    /// <summary>Creates a pane (a glass window) at the given desktop bounds. Returns its id at once; the window appears on the glass thread.</summary>
    public int AddPane(Rectangle bounds, GlassStyle style, bool clickThrough = false)
    {
        var id = Interlocked.Increment(ref nextId);
        Post(() => { try { panes[id] = new Pane(id, bounds, style, clickThrough, ExcludeFromCapture); paneCount = panes.Count; } catch (Exception ex) { lastError = ex.Message; } });
        return id;
    }

    public void MovePane(int id, Point location) => Post(() => { if (panes.TryGetValue(id, out var p)) p.MoveTo(location); backdropChanged = true; });
    public void ResizePane(int id, Size size) => Post(() => { if (panes.TryGetValue(id, out var p) && gpu != null) p.Resize(gpu, size); backdropChanged = true; });
    public void SetStyle(int id, GlassStyle style) => Post(() => { if (panes.TryGetValue(id, out var p) && gpu != null) p.SetStyle(gpu.D2D, style); backdropChanged = true; });
    /// <summary>For screenshots and screen recordings: while on, the panes are visible to capture tools (and see themselves, so use it only for a moment).</summary>
    public void SetVisibleToCaptureTools(bool on) => Post(() => { foreach (var p in panes.Values) p.SetExcluded(!on); });
    /// <summary>Keeps pane <paramref name="id"/> directly below the window <paramref name="hwnd"/> in the z-order (the host window draws the content, the pane the glass).</summary>
    public void PlaceBelow(int id, IntPtr hwnd) => Post(() => { if (panes.TryGetValue(id, out var p)) p.PlaceBelow(hwnd); });
    public void SetPaneVisible(int id, bool visible) => Post(() => { if (panes.TryGetValue(id, out var p)) p.SetVisible(visible); });
    /// <summary>True when the GPU devices were created (false = the host should keep its own non-GPU look).</summary>
    public bool IsGpuReady => gpu != null && !lastError.StartsWith("GPU init", StringComparison.Ordinal);
    public void RemovePane(int id) => Post(() => { if (panes.Remove(id, out var p)) { p.Dispose(); paneCount = panes.Count; } });

    /// <summary>The host tells what is true now (tier, live wallpaper, lock, fullscreen ...). Cheap; call it whenever something changes.</summary>
    public void SetConditions(GlassConditions c) { Volatile.Write(ref conditions, c); wake.Set(); }

    /// <summary>Something behind a pane changed (a window moved, the wallpaper changed): capture at least once more.</summary>
    public void NotifyBackdropChanged() { backdropChanged = true; wake.Set(); }

    private void Post(Action a) { commands.Enqueue(a); wake.Set(); }

    // ------------------------------------------------------------------ the glass thread
    private void Run()
    {
        try { gpu = Devices.Create(); }
        catch (Exception ex) { lastError = "GPU init: " + ex.Message; }
        started.Set();
        var frame = Stopwatch.StartNew();
        long nextFrameAt = 0;

        while (!stop)
        {
            Native.Pump();
            while (commands.TryDequeue(out var cmd)) { try { cmd(); } catch (Exception ex) { lastError = ex.Message; } }
            if (gpu == null) { wake.WaitOne(250); continue; }

            var c = Volatile.Read(ref conditions);
            var decision = GlassPolicy.Evaluate(c);
            try
            {
                if (decision.Suspended)
                {
                    foreach (var p in panes.Values) p.SetVisible(false);
                    wake.WaitOne(250); continue;
                }
                foreach (var p in panes.Values) p.SetVisible(true);

                if (decision.Static || !decision.CaptureNow && !backdropChanged)
                {
                    // nothing changes behind (or the Low tier): paint panes that are dirty ONCE with the static tint, then sleep. No capture, no GPU work at idle.
                    foreach (var p in panes.Values) if (p.Dirty) { p.RenderStatic(gpu); Interlocked.Increment(ref staticRenders); }
                    Interlocked.Increment(ref skipped);
                    wake.WaitOne(decision.Static ? 500 : 100);
                    continue;
                }

                // pace to the allowed rate (60 / 30 / 20 fps)
                var fps = Math.Max(1, decision.MaxFps == 0 ? 30 : decision.MaxFps);
                var now = frame.ElapsedMilliseconds;
                if (now < nextFrameAt) { wake.WaitOne((int)(nextFrameAt - now)); continue; }
                nextFrameAt = frame.ElapsedMilliseconds + 1000 / fps;

                RenderLive(decision);
                backdropChanged = decision.CaptureNow ? backdropChanged && false : false;
            }
            catch (Exception ex) when (IsDeviceLost(ex))
            {
                Interlocked.Increment(ref resets); lastError = "device lost: " + ex.Message;
                Recover();
            }
            catch (Exception ex) { lastError = ex.Message; wake.WaitOne(250); }
        }
        foreach (var p in panes.Values) p.Dispose();
        panes.Clear();
        foreach (var m in monitors.Values) m.Dispose();
        maps.Dispose();
        gpu?.Dispose();
    }

    private void RenderLive(GlassDecision d)
    {
        // group panes by monitor: ONE duplication and ONE union texture per monitor
        foreach (var group in panes.Values.GroupBy(p => Devices.MonitorOf(gpu!, p.Bounds)))
        {
            var mon = GetMonitor(group.Key);
            if (mon == null) continue;
            var union = RectMath.Union(group.Select(p => p.Bounds));
            if (!mon.TryCapture(gpu!, union, 1000 / Math.Max(1, d.MaxFps), out var texBitmap)) { Interlocked.Increment(ref skipped); continue; }
            Interlocked.Increment(ref captures);
            foreach (var p in group) { p.RenderLive(gpu!, texBitmap!, RectMath.OffsetIn(mon.UnionRect, p.Bounds), d, maps); Interlocked.Increment(ref renders); }
        }
    }

    private MonitorCapture? GetMonitor(int index)
    {
        if (monitors.TryGetValue(index, out var m)) return m;
        m = MonitorCapture.Create(gpu!, index);
        if (m != null) monitors[index] = m;
        return m;
    }

    private static bool IsDeviceLost(Exception ex) => ex is SharpGen.Runtime.SharpGenException sg && (sg.ResultCode == Vortice.DXGI.ResultCode.DeviceRemoved || sg.ResultCode == Vortice.DXGI.ResultCode.DeviceReset);

    private void Recover()
    {
        foreach (var m in monitors.Values) m.Dispose();
        monitors.Clear(); maps.Clear();
        foreach (var p in panes.Values) p.ReleaseGpu();
        gpu?.Dispose(); gpu = null;
        try { gpu = Devices.Create(); foreach (var p in panes.Values) p.RebuildGpu(gpu); }
        catch (Exception ex) { lastError = "recover: " + ex.Message; Thread.Sleep(500); }
    }

    public void Dispose()
    {
        stop = true; wake.Set();
        thread.Join(3000);
        wake.Dispose(); started.Dispose();
    }

    // ================================================================== GPU objects
    internal sealed class Devices : IDisposable
    {
        public ID3D11Device D3D = null!; public ID3D11DeviceContext Ctx = null!; public IDXGIDevice Dxgi = null!; public IDXGIAdapter Adapter = null!;
        public ID2D1Factory1 Factory = null!; public ID2D1Device D2DDevice = null!; public ID2D1DeviceContext D2D = null!;
        public IDCompositionDevice Comp = null!; public IDXGIFactory2 Factory2 = null!;

        public static Devices Create()
        {
            var d = new Devices();
            D3D11.D3D11CreateDevice((IDXGIAdapter?)null, DriverType.Hardware, DeviceCreationFlags.BgraSupport, new[] { Vortice.Direct3D.FeatureLevel.Level_11_1, Vortice.Direct3D.FeatureLevel.Level_11_0 }, out ID3D11Device? dev, out ID3D11DeviceContext? ctx).CheckError();
            d.D3D = dev!; d.Ctx = ctx!;
            d.Dxgi = d.D3D.QueryInterface<IDXGIDevice>();
            d.Adapter = d.Dxgi.GetAdapter();
            d.Factory = D2D1.D2D1CreateFactory<ID2D1Factory1>();
            d.D2DDevice = d.Factory.CreateDevice(d.Dxgi);
            d.D2D = d.D2DDevice.CreateDeviceContext(DeviceContextOptions.None);
            d.Comp = DComp.DCompositionCreateDevice<IDCompositionDevice>(d.Dxgi);
            d.Factory2 = d.Adapter.GetParent<IDXGIFactory2>();
            return d;
        }

        /// <summary>Index of the output (monitor) whose desktop rectangle contains the centre of <paramref name="r"/> (0 when none).</summary>
        public static int MonitorOf(Devices g, Rectangle r)
        {
            var c = new Point(r.X + r.Width / 2, r.Y + r.Height / 2);
            for (uint i = 0; g.Adapter.EnumOutputs(i, out var o).Success; i++)
            {
                using (o) { var b = o!.Description.DesktopCoordinates; if (c.X >= b.Left && c.X < b.Right && c.Y >= b.Top && c.Y < b.Bottom) return (int)i; }
            }
            return 0;
        }

        public void Dispose()
        {
            Factory2?.Dispose(); Comp?.Dispose(); D2D?.Dispose(); D2DDevice?.Dispose(); Factory?.Dispose(); Adapter?.Dispose(); Dxgi?.Dispose(); Ctx?.Dispose(); D3D?.Dispose();
        }
    }

    /// <summary>One Desktop Duplication per monitor and ONE persistent texture for the union of that monitor's panes (re-created only when it must grow).</summary>
    internal sealed class MonitorCapture : IDisposable
    {
        private IDXGIOutputDuplication? dup;
        private ID3D11Texture2D? union; private ID2D1Bitmap1? bitmap;
        private Size unionSize;
        private readonly RectI desktop;
        public Rectangle UnionRect { get; private set; }
        private readonly int index;

        private MonitorCapture(int index, RectI desktop) { this.index = index; this.desktop = desktop; }

        public static MonitorCapture? Create(Devices g, int index)
        {
            if (g.Adapter.EnumOutputs((uint)index, out var o).Failure) return null;
            using (o)
            {
                var m = new MonitorCapture(index, o!.Description.DesktopCoordinates);
                using var o1 = o.QueryInterface<IDXGIOutput1>();
                m.dup = o1.DuplicateOutput(g.D3D);
                return m;
            }
        }

        /// <summary>Waits up to one frame for a CHANGE; copies the union region (clipped to this monitor) into the shared texture. False = nothing changed (no work).</summary>
        public bool TryCapture(Devices g, Rectangle wanted, int timeoutMs, out ID2D1Bitmap1? result)
        {
            result = null;
            var local = Rectangle.Intersect(new Rectangle(desktop.Left, desktop.Top, desktop.Right - desktop.Left, desktop.Bottom - desktop.Top), wanted);
            if (local.IsEmpty) return false;
            if (dup == null) { dup = Reopen(g); if (dup == null) return false; }
            var hr = dup.AcquireNextFrame((uint)Math.Max(1, timeoutMs), out _, out var res);
            if (hr.Failure)
            {
                if (hr == Vortice.DXGI.ResultCode.WaitTimeout) return false;
                if (hr == Vortice.DXGI.ResultCode.AccessLost) { dup.Dispose(); dup = null; return false; }   // secure desktop / mode change: reopen next time
                hr.CheckError();
            }
            try
            {
                EnsureTexture(g, local.Size);
                using var tex = res!.QueryInterface<ID3D11Texture2D>();
                var sx = local.X - desktop.Left; var sy = local.Y - desktop.Top;
                g.Ctx.CopySubresourceRegion(union!, 0, 0, 0, 0, tex, 0, new Box(sx, sy, 0, sx + local.Width, sy + local.Height, 1));
                UnionRect = local;
            }
            finally { res?.Dispose(); dup.ReleaseFrame(); }
            result = bitmap; return true;
        }

        private IDXGIOutputDuplication? Reopen(Devices g)
        {
            try { if (g.Adapter.EnumOutputs((uint)index, out var o).Failure) return null; using (o) { using var o1 = o!.QueryInterface<IDXGIOutput1>(); return o1.DuplicateOutput(g.D3D); } }
            catch (Exception) { return null; }
        }

        private void EnsureTexture(Devices g, Size wanted)
        {
            if (union != null && !RectMath.NeedsNewTexture(unionSize, wanted)) return;
            bitmap?.Dispose(); union?.Dispose();
            union = g.D3D.CreateTexture2D(new Texture2DDescription { Width = (uint)wanted.Width, Height = (uint)wanted.Height, MipLevels = 1, ArraySize = 1, Format = Format.B8G8R8A8_UNorm, SampleDescription = new SampleDescription(1, 0), Usage = ResourceUsage.Default, BindFlags = BindFlags.ShaderResource });
            using var surf = union.QueryInterface<IDXGISurface>();
            bitmap = g.D2D.CreateBitmapFromDxgiSurface(surf, new BitmapProperties1(new DC.PixelFormat(Format.B8G8R8A8_UNorm, DC.AlphaMode.Ignore), 96, 96, BitmapOptions.None));
            unionSize = wanted;
        }

        public void Dispose() { bitmap?.Dispose(); union?.Dispose(); dup?.Dispose(); }
    }

    /// <summary>One glass window: a DirectComposition swap chain with premultiplied alpha. All GPU objects are created once; the render path allocates nothing of its own.</summary>
    internal sealed class Pane : IDisposable
    {
        private static readonly Native.WndProc Proc = (h, m, wp, lp) => Native.DefWindowProc(h, m, wp, lp);
        private static bool registered;
        public int Id { get; }
        public Rectangle Bounds { get; private set; }
        public bool Dirty { get; private set; } = true;
        private readonly IntPtr hwnd; private GlassStyle style;
        private IDXGISwapChain1? swap; private IDCompositionTarget? target; private IDCompositionVisual? visual;
        private ID2D1Effect? blur, displace; private ID2D1Image? displaceOut; private ID2D1RoundedRectangleGeometry? clip; private ID2D1SolidColorBrush? tint, rim; private ID2D1Layer? layer;
        private ID2D1LinearGradientBrush? sheen; private ID2D1GradientStopCollection? sheenStops;
        private ID2D1Bitmap1? target2d; private bool visible = true;

        public Pane(int id, Rectangle bounds, GlassStyle style, bool clickThrough, bool exclude)
        {
            Id = id; Bounds = bounds; this.style = style;
            if (!registered)
            {
                var cls = new Native.WNDCLASS { lpfnWndProc = Marshal.GetFunctionPointerForDelegate(Proc), hInstance = Native.GetModuleHandle(null), lpszClassName = "OpsisGlassPane", hCursor = Native.LoadCursor(IntPtr.Zero, 32512) };
                Native.RegisterClass(ref cls); registered = true;
            }
            var ex = Native.WS_EX_TOPMOST | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_NOREDIRECTIONBITMAP | (clickThrough ? Native.WS_EX_TRANSPARENT : 0);
            hwnd = Native.CreateWindowEx(ex, "OpsisGlassPane", "Opsis glass", Native.WS_POPUP, bounds.X, bounds.Y, bounds.Width, bounds.Height, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (exclude) Native.SetWindowDisplayAffinity(hwnd, Native.WDA_EXCLUDEFROMCAPTURE);
        }

        public void RebuildGpu(Devices g)
        {
            ReleaseGpu();
            swap = g.Factory2.CreateSwapChainForComposition(g.D3D, new SwapChainDescription1 { Width = (uint)Bounds.Width, Height = (uint)Bounds.Height, Format = Format.B8G8R8A8_UNorm, SampleDescription = new SampleDescription(1, 0), BufferUsage = Usage.RenderTargetOutput, BufferCount = 2, Scaling = Scaling.Stretch, SwapEffect = SwapEffect.FlipSequential, AlphaMode = Vortice.DXGI.AlphaMode.Premultiplied }, null);
            visual = g.Comp.CreateVisual(); visual.SetContent(swap);
            g.Comp.CreateTargetForHwnd(hwnd, true, out target!); target.SetRoot(visual); g.Comp.Commit();
            Native.ShowWindow(hwnd, 4);
            BuildEffects(g.D2D);
            Dirty = true;
        }

        private void BuildEffects(ID2D1DeviceContext dc)
        {
            DisposeEffects();
            blur = new ID2D1Effect(dc.CreateEffect(EffectGuids.GaussianBlur));
            blur.SetValue((int)GaussianBlurProperties.StandardDeviation, Math.Max(0.01f, style.BlurSigma));
            displace = new ID2D1Effect(dc.CreateEffect(EffectGuids.DisplacementMap));
            displace.SetValue((int)DisplacementMapProperties.Scale, style.RefractionPx);
            displaceOut = displace.Output;
            using var f = dc.Factory;
            clip = f.CreateRoundedRectangleGeometry(new RoundedRectangle { Rect = new Rect(0.5f, 0.5f, Bounds.Width - 1, Bounds.Height - 1), RadiusX = style.CornerRadius, RadiusY = style.CornerRadius });
            tint = dc.CreateSolidColorBrush(new Color4(style.Tint.R / 255f, style.Tint.G / 255f, style.Tint.B / 255f, style.TintAlpha));
            rim = dc.CreateSolidColorBrush(new Color4(1f, 1f, 1f, Math.Clamp(style.Rim * (0.35f + 0.4f * style.Gloss) + 0.05f, 0f, 1f)));
            layer = dc.CreateLayer(null);
            // specular sheen: a soft white band at the top, strength from the glossy intensity
            sheenStops = dc.CreateGradientStopCollection(new[] { new GradientStop { Position = 0f, Color = new Color4(1f, 1f, 1f, 0.28f * style.Gloss) }, new GradientStop { Position = 0.45f, Color = new Color4(1f, 1f, 1f, 0f) } }, Gamma.StandardRgb, ExtendMode.Clamp);
            sheen = dc.CreateLinearGradientBrush(new LinearGradientBrushProperties { StartPoint = new Vector2(0, 0), EndPoint = new Vector2(0, Bounds.Height) }, sheenStops);
        }

        public void SetStyle(ID2D1DeviceContext dc, GlassStyle s) { style = s; if (swap != null) BuildEffects(dc); Dirty = true; }
        public void MoveTo(Point p) { Bounds = new Rectangle(p, Bounds.Size); Native.SetWindowPos(hwnd, IntPtr.Zero, p.X, p.Y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE); }
        public void SetExcluded(bool exclude) => Native.SetWindowDisplayAffinity(hwnd, exclude ? Native.WDA_EXCLUDEFROMCAPTURE : Native.WDA_NONE);
        public void PlaceBelow(IntPtr above) => Native.SetWindowPos(hwnd, above, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        public void SetVisible(bool v) { if (v == visible) return; visible = v; Native.ShowWindow(hwnd, v ? 4 : 0); if (v) Dirty = true; }

        public void Resize(Devices g, Size size)
        {
            Bounds = new Rectangle(Bounds.Location, size);
            Native.SetWindowPos(hwnd, IntPtr.Zero, 0, 0, size.Width, size.Height, Native.SWP_NOMOVE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
            target2d?.Dispose(); target2d = null;
            swap?.ResizeBuffers(0, (uint)size.Width, (uint)size.Height, Format.Unknown, SwapChainFlags.None);
            BuildEffects(g.D2D); Dirty = true;
        }

        private void BeginTarget(ID2D1DeviceContext dc)
        {
            using var surface = swap!.GetBuffer<IDXGISurface>(0);
            target2d?.Dispose();
            target2d = dc.CreateBitmapFromDxgiSurface(surface, new BitmapProperties1(new DC.PixelFormat(Format.B8G8R8A8_UNorm, DC.AlphaMode.Premultiplied), 96, 96, BitmapOptions.Target | BitmapOptions.CannotDraw));
            dc.Target = target2d;
        }

        /// <summary>Live: refraction (High) or blur only (Balanced) of the shared union bitmap, tint, specular sheen, rim.</summary>
        public void RenderLive(Devices g, ID2D1Bitmap1 union, Point sourceOffset, GlassDecision d, BoundedLru<(int, int, int, int, int), ID2D1Bitmap1> maps)
        {
            if (swap == null) RebuildGpu(g);
            var dc = g.D2D; BeginTarget(dc);
            dc.BeginDraw(); dc.Clear(new Color4(0, 0, 0, 0));
            blur!.SetInput(0, union, true);
            dc.PushLayer(new LayerParameters1 { ContentBounds = new Rect(0, 0, Bounds.Width, Bounds.Height), GeometricMask = clip, MaskAntialiasMode = AntialiasMode.PerPrimitive, Opacity = 1f, MaskTransform = Matrix3x2.Identity }, layer!);
            if (d.Refraction)
            {
                var map = maps.GetOrCreate(style.MapKey(Bounds.Width, Bounds.Height, 96), _ => CreateMap(dc));
                displace!.SetInputEffect(0, blur, true); displace.SetInput(1, map, true);
                dc.DrawImage(displaceOut!, new Vector2(0, 0), new Rect(sourceOffset.X, sourceOffset.Y, Bounds.Width, Bounds.Height), D2D.InterpolationMode.Linear, D2D.CompositeMode.SourceOver);
            }
            else
            {
                dc.DrawImage(blur.Output, new Vector2(0, 0), new Rect(sourceOffset.X, sourceOffset.Y, Bounds.Width, Bounds.Height), D2D.InterpolationMode.Linear, D2D.CompositeMode.SourceOver);
            }
            dc.FillRectangle(new Rect(0, 0, Bounds.Width, Bounds.Height), tint!);
            if (style.Gloss > 0.01f) dc.FillRectangle(new Rect(0, 0, Bounds.Width, Bounds.Height), sheen!);
            dc.PopLayer();
            dc.DrawGeometry(clip!, rim!, 1f);
            dc.EndDraw(); dc.Target = null;
            swap!.Present(0, PresentFlags.None);
            Dirty = false;
        }

        /// <summary>Static frosted tint (Low tier, nothing changing, no duplication): painted once, no backdrop.</summary>
        public void RenderStatic(Devices g)
        {
            if (swap == null) RebuildGpu(g);
            var dc = g.D2D; BeginTarget(dc);
            dc.BeginDraw(); dc.Clear(new Color4(0, 0, 0, 0));
            dc.PushLayer(new LayerParameters1 { ContentBounds = new Rect(0, 0, Bounds.Width, Bounds.Height), GeometricMask = clip, MaskAntialiasMode = AntialiasMode.PerPrimitive, Opacity = 1f, MaskTransform = Matrix3x2.Identity }, layer!);
            dc.FillRectangle(new Rect(0, 0, Bounds.Width, Bounds.Height), tint!);
            dc.PopLayer();
            dc.DrawGeometry(clip!, rim!, 1f);
            dc.EndDraw(); dc.Target = null;
            swap!.Present(0, PresentFlags.None);
            Dirty = false;
        }

        private unsafe ID2D1Bitmap1 CreateMap(ID2D1DeviceContext dc)
        {
            var px = DisplacementMapBuilder.Build(Bounds.Width, Bounds.Height, style.CornerRadius, style.BezelPx);
            fixed (byte* p = px) return dc.CreateBitmap(new SizeI(Bounds.Width, Bounds.Height), (IntPtr)p, (uint)(Bounds.Width * 4), new BitmapProperties1(new DC.PixelFormat(Format.B8G8R8A8_UNorm, DC.AlphaMode.Premultiplied), 96, 96, BitmapOptions.None));
        }

        private void DisposeEffects() { sheen?.Dispose(); sheenStops?.Dispose(); displaceOut?.Dispose(); layer?.Dispose(); rim?.Dispose(); tint?.Dispose(); clip?.Dispose(); displace?.Dispose(); blur?.Dispose(); sheen = null; sheenStops = null; displaceOut = null; layer = null; rim = null; tint = null; clip = null; displace = null; blur = null; }

        public void ReleaseGpu() { target2d?.Dispose(); target2d = null; DisposeEffects(); target?.Dispose(); visual?.Dispose(); swap?.Dispose(); target = null; visual = null; swap = null; }
        public void Dispose() { ReleaseGpu(); Native.DestroyWindow(hwnd); }
    }
}
