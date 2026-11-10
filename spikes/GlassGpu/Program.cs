// Phase 1 proof (plan 2.4.C, option A): the GPU path of Opsis.Glass.
//   DXGI Desktop Duplication (change-driven, ONE capture for the union of all glass windows, glass windows excluded from capture)
//   -> Direct2D effects on the GPU (blur + displacement/refraction + tint + rim) -> DirectComposition swap chains with PREMULTIPLIED alpha (true per-pixel transparency).
// Throw-away measurement tool, not product code.
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Vortice.Direct2D1;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DirectComposition;
using Vortice.DXGI;
using Vortice.Mathematics;
using D2D = Vortice.Direct2D1;

namespace GlassGpu;

internal static unsafe class Program
{
    private static string _out = "results-gpu.txt";
    private static void Log(string s) { var line = DateTime.Now.ToString("HH:mm:ss.fff") + " " + s; Console.WriteLine(line); File.AppendAllText(_out, line + Environment.NewLine); }

    private static int Main(string[] args)
    {
        _out = args.Length > 0 ? args[0] : _out;
        var shots = args.Length > 1 ? args[1] : Path.GetDirectoryName(Path.GetFullPath(_out))!;
        File.WriteAllText(_out, "");
        Nat.SetProcessDpiAwarenessContext(new IntPtr(-4));
        var cores = Environment.ProcessorCount;
        var sw0 = Nat.GetSystemMetrics(0); var sh0 = Nat.GetSystemMetrics(1);
        Log($"machine: {sw0}x{sh0}, {cores} cores, build {Environment.OSVersion.Version.Build}; label={Environment.GetEnvironmentVariable("SPIKE_LABEL") ?? "-"}");

        var page = Environment.GetEnvironmentVariable("SPIKE_STAGE_PAGE");
        if (!string.IsNullOrEmpty(page) && File.Exists(page))
        {
            var ready = new ManualResetEventSlim(); Stage.Start(page, ready);
            Log(ready.Wait(15000) ? "stage: Opsis wallpaper page loaded in a 1700x900 stand-in window" : "stage: page did not load");
            Thread.Sleep(2500);
        }

        // ---------------- devices
        D3D11.D3D11CreateDevice(null, DriverType.Hardware, DeviceCreationFlags.BgraSupport, new[] { Vortice.Direct3D.FeatureLevel.Level_11_1, Vortice.Direct3D.FeatureLevel.Level_11_0 }, out ID3D11Device? dev, out ID3D11DeviceContext? ctx).CheckError();
        using var d3d = dev!; using var d3dCtx = ctx!;
        using var dxgiDev = d3d.QueryInterface<IDXGIDevice>();
        using var adapter = dxgiDev.GetAdapter();
        Log($"adapter: {adapter.Description.Description}");
        adapter.EnumOutputs(0, out var output0).CheckError(); using var output = output0!;
        using var output1 = output.QueryInterface<IDXGIOutput1>();
        using var dup = output1.DuplicateOutput(d3d);
        using var d2dFactory = D2D1.D2D1CreateFactory<ID2D1Factory1>();
        using var d2dDev = d2dFactory.CreateDevice(dxgiDev);
        using var dc = d2dDev.CreateDeviceContext(DeviceContextOptions.None);
        using var dcomp = DComp.DCompositionCreateDevice<IDCompositionDevice>(dxgiDev);
        using var factory2 = adapter.GetParent<IDXGIFactory2>();

        // ---------------- glass windows (DirectComposition, premultiplied alpha, excluded from capture)
        const int count = 4;
        var wins = new List<GpuGlassWin>();
        for (var i = 0; i < count; i++) wins.Add(new GpuGlassWin(d3d, dc, dcomp, factory2, 120 + i * 330, 200, 320, 240));
        foreach (var w in wins) w.SetExclude(true);
        int ux = wins.Min(w => w.X), uy = wins.Min(w => w.Y), uw = wins.Max(w => w.X + w.W) - ux, uh = wins.Max(w => w.Y + w.H) - uy;

        // ONE persistent texture for the union region (VRAM: uw*uh*4 bytes, allocated once)
        using var union = d3d.CreateTexture2D(new Texture2DDescription { Width = (uint)uw, Height = (uint)uh, MipLevels = 1, ArraySize = 1, Format = Format.B8G8R8A8_UNorm, SampleDescription = new SampleDescription(1, 0), Usage = ResourceUsage.Default, BindFlags = BindFlags.ShaderResource });
        using var unionSurface = union.QueryInterface<IDXGISurface>();
        using var unionBitmap = dc.CreateBitmapFromDxgiSurface(unionSurface, new BitmapProperties1(new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Ignore), 96, 96, BitmapOptions.None));
        Log($"union region {ux},{uy} {uw}x{uh} = {uw * uh * 4 / 1048576.0:0.0} MB VRAM (once)");

        var proc = Process.GetCurrentProcess();
        double Cpu(Action a, out double secs) { proc.Refresh(); var c0 = proc.TotalProcessorTime; var sw = Stopwatch.StartNew(); a(); sw.Stop(); proc.Refresh(); secs = sw.Elapsed.TotalSeconds; return (proc.TotalProcessorTime - c0).TotalSeconds / secs / cores * 100; }

        // ---------------- run: change-driven duplication, render all glass windows each captured frame
        long gpuTicks = 0, frames = 0, idleTimeouts = 0; double acquireMs = 0, renderMs = 0;
        void Pump(int ms)
        {
            var end = Stopwatch.StartNew();
            while (end.ElapsedMilliseconds < ms)
            {
                var t0 = Stopwatch.GetTimestamp();
                var hr = dup.AcquireNextFrame(33, out var info, out var res);
                var t1 = Stopwatch.GetTimestamp();
                if (hr.Failure) { idleTimeouts++; continue; } // nothing changed behind: NO capture, NO render (zero cost at idle)
                using (res)
                using (var tex = res!.QueryInterface<ID3D11Texture2D>())
                    d3dCtx.CopySubresourceRegion(union, 0, 0, 0, 0, tex, 0, new Box(ux, uy, 0, ux + uw, uy + uh, 1));
                dup.ReleaseFrame();
                var t2 = Stopwatch.GetTimestamp();
                foreach (var w in wins) w.Render(dc, unionBitmap, w.X - ux, w.Y - uy);
                var t3 = Stopwatch.GetTimestamp();
                acquireMs += (t1 - t0) * 1000.0 / Stopwatch.Frequency; renderMs += (t3 - t2) * 1000.0 / Stopwatch.Frequency; frames++;
            }
        }

        Pump(1500); frames = 0; acquireMs = renderMs = 0; idleTimeouts = 0;
        var cpuLive = Cpu(() => Pump(8000), out var s1);
        Log($"GPU path, {count} glass windows 320x240 over a LIVE background: {frames / s1:0.0} rendered frames/s, acquire {acquireMs / Math.Max(1, frames):0.00} ms, GPU-side render submit {renderMs / Math.Max(1, frames):0.00} ms per frame (all {count} windows), our process CPU {cpuLive:0.0}%");
        Log($"   idle timeouts (no change behind -> no work): {idleTimeouts}");

        // screenshot of the composed result (affinity off just for the shot)
        foreach (var w in wins) w.SetExclude(false);
        Thread.Sleep(500);
        Shot(shots, "gpu-glass-4", ux - 40, uy - 40, uw + 80, uh + 80);
        foreach (var w in wins) w.SetExclude(true);

        // proof that the duplicated union region does not contain our own windows
        {
            DumpTexture(d3d, d3dCtx, union, Path.Combine(shots, "gpu-captured-union.png"), uw, uh);
            Log("   gpu-captured-union.png = what the glass sees (must show the background only, no glass panes)");
        }

        foreach (var w in wins) w.Dispose();
        Log("done");
        return 0;
    }

    private static void DumpTexture(ID3D11Device dev, ID3D11DeviceContext ctx, ID3D11Texture2D src, string path, int w, int h)
    {
        using var staging = dev.CreateTexture2D(new Texture2DDescription { Width = (uint)w, Height = (uint)h, MipLevels = 1, ArraySize = 1, Format = Format.B8G8R8A8_UNorm, SampleDescription = new SampleDescription(1, 0), Usage = ResourceUsage.Staging, CPUAccessFlags = CpuAccessFlags.Read });
        ctx.CopyResource(staging, src);
        var map = ctx.Map(staging, 0, MapMode.Read);
        using var bmp = new Bitmap(w, h, PixelFormat.Format32bppRgb);
        var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
        for (var y = 0; y < h; y++) Buffer.MemoryCopy((byte*)map.DataPointer + y * map.RowPitch, (byte*)data.Scan0 + y * data.Stride, w * 4, w * 4);
        bmp.UnlockBits(data); ctx.Unmap(staging, 0);
        bmp.Save(path, ImageFormat.Png);
    }

    private static void Shot(string dir, string name, int x, int y, int w, int h)
    {
        x = Math.Max(0, x); y = Math.Max(0, y);
        using var bmp = new Bitmap(w, h, PixelFormat.Format32bppRgb);
        using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(x, y, 0, 0, new System.Drawing.Size(w, h));
        bmp.Save(Path.Combine(dir, name + ".png"), ImageFormat.Png);
        Log($"   screenshot {name}.png (region {x},{y} {w}x{h})");
    }
}

internal sealed unsafe class GpuGlassWin : IDisposable
{
    private static readonly Nat.WndProc Proc = (h, m, wp, lp) => Nat.DefWindowProc(h, m, wp, lp);
    private static bool _registered;
    public IntPtr Hwnd { get; }
    public int X, Y, W, H;
    private readonly IDXGISwapChain1 _swap;
    private readonly IDCompositionTarget _target;
    private readonly IDCompositionVisual _visual;
    private readonly ID2D1Bitmap1 _disp;            // displacement map: built once per size/DPI, then cached (bounded: one per window here)
    private readonly ID2D1Effect _blur, _displace;
    private readonly ID2D1Image _displaceOut;
    private readonly ID2D1RoundedRectangleGeometry _clip;
    private readonly ID2D1SolidColorBrush _tint, _rim;
    private readonly ID2D1Layer _layer;

    public GpuGlassWin(ID3D11Device d3d, ID2D1DeviceContext dc, IDCompositionDevice dcomp, IDXGIFactory2 factory2, int x, int y, int w, int h)
    {
        X = x; Y = y; W = w; H = h;
        if (!_registered)
        {
            var cls = new Nat.WNDCLASS { lpfnWndProc = Marshal.GetFunctionPointerForDelegate(Proc), hInstance = Nat.GetModuleHandle(null), lpszClassName = "GlassGpuWnd", hCursor = Nat.LoadCursor(IntPtr.Zero, 32512) };
            Nat.RegisterClass(ref cls); _registered = true;
        }
        // WS_EX_NOREDIRECTIONBITMAP (0x00200000): no GDI surface; the content comes from the composition swap chain
        Hwnd = Nat.CreateWindowEx(0x00000080 | 0x08000000 | 0x00000008 | 0x00200000, "GlassGpuWnd", "g", 0x80000000, x, y, w, h, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        _swap = factory2.CreateSwapChainForComposition(d3d, new SwapChainDescription1 { Width = (uint)w, Height = (uint)h, Format = Format.B8G8R8A8_UNorm, SampleDescription = new SampleDescription(1, 0), BufferUsage = Usage.RenderTargetOutput, BufferCount = 2, Scaling = Scaling.Stretch, SwapEffect = SwapEffect.FlipSequential, AlphaMode = Vortice.DXGI.AlphaMode.Premultiplied }, null);
        _visual = dcomp.CreateVisual(); _visual.SetContent(_swap);
        dcomp.CreateTargetForHwnd(Hwnd, true, out _target!); _target.SetRoot(_visual); dcomp.Commit();
        Nat.ShowWindow(Hwnd, 4);

        // displacement map (R,G = offset 0..1 around 0.5): stronger near the rounded bezel, built once
        var px = new byte[w * h * 4]; const float radius = 16f, bezel = 26f;
        for (var yy = 0; yy < h; yy++)
            for (var xx = 0; xx < w; xx++)
            {
                var dx = Math.Min(xx, w - 1 - xx); var dy = Math.Min(yy, h - 1 - yy);
                float d; if (dx < radius && dy < radius) { var cx = radius - dx; var cy = radius - dy; d = radius - MathF.Sqrt(cx * cx + cy * cy); } else d = Math.Min(dx, dy);
                float ox = 0, oy = 0;
                if (d >= 0 && d < bezel) { var t = 1f - d / bezel; var k = t * t; ox = (w / 2f - xx) / (w / 2f) * k; oy = (h / 2f - yy) / (h / 2f) * k; }
                var o = (yy * w + xx) * 4;
                px[o] = 128; px[o + 1] = (byte)Math.Clamp(128 + oy * 127, 0, 255); px[o + 2] = (byte)Math.Clamp(128 + ox * 127, 0, 255); px[o + 3] = 255;
            }
        fixed (byte* p = px) _disp = dc.CreateBitmap(new Vortice.Mathematics.SizeI(w, h), (IntPtr)p, (uint)(w * 4), new BitmapProperties1(new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied), 96, 96, BitmapOptions.None));
        _blur = new ID2D1Effect(dc.CreateEffect(EffectGuids.GaussianBlur)); _blur.SetValue((int)GaussianBlurProperties.StandardDeviation, 5.0f);
        _displace = new ID2D1Effect(dc.CreateEffect(EffectGuids.DisplacementMap)); _displaceOut = _displace.Output; _displace.SetValue((int)DisplacementMapProperties.Scale, 28.0f);
        using var f = dc.Factory;
        _clip = f.CreateRoundedRectangleGeometry(new RoundedRectangle { Rect = new Rect(0.5f, 0.5f, w - 1, h - 1), RadiusX = 16, RadiusY = 16 });
        _tint = dc.CreateSolidColorBrush(new Color4(0.6f, 0.8f, 1f, 0.30f));
        _rim = dc.CreateSolidColorBrush(new Color4(1f, 1f, 1f, 0.45f));
        _layer = dc.CreateLayer(null);
    }

    public void SetExclude(bool on) => Nat.SetWindowDisplayAffinity(Hwnd, on ? 0x11u : 0u);

    /// <summary>Source = the shared union bitmap; (sx, sy) = where this window's backdrop starts inside it. No per-frame allocation: effects, brushes, geometry and the displacement map are created once.</summary>
    public void Render(ID2D1DeviceContext dc, ID2D1Bitmap1 union, int sx, int sy)
    {
        using var surface = _swap.GetBuffer<IDXGISurface>(0);
        using var target = dc.CreateBitmapFromDxgiSurface(surface, new BitmapProperties1(new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied), 96, 96, BitmapOptions.Target | BitmapOptions.CannotDraw));
        dc.Target = target;
        dc.BeginDraw();
        dc.Clear(new Color4(0, 0, 0, 0));
        // backdrop crop -> displacement (refraction) with the cached map -> blur a little (frosted part)
        _blur.SetInput(0, union, true);
        _displace.SetInputEffect(0, _blur, true);
        _displace.SetInput(1, _disp, true);
        dc.PushLayer(new LayerParameters1 { ContentBounds = new Rect(0, 0, W, H), GeometricMask = _clip, MaskAntialiasMode = AntialiasMode.PerPrimitive, Opacity = 1f, MaskTransform = System.Numerics.Matrix3x2.Identity }, _layer);
        dc.DrawImage(_displaceOut, new System.Numerics.Vector2(0, 0), new Rect(sx, sy, W, H), D2D.InterpolationMode.Linear, D2D.CompositeMode.SourceOver);
        dc.FillRectangle(new Rect(0, 0, W, H), _tint);
        dc.PopLayer();
        dc.DrawGeometry(_clip, _rim, 1f);
        dc.EndDraw();
        dc.Target = null;
        _swap.Present(0, PresentFlags.None);
    }

    public void Dispose()
    {
        _displaceOut.Dispose(); _layer.Dispose(); _rim.Dispose(); _tint.Dispose(); _clip.Dispose(); _displace.Dispose(); _blur.Dispose(); _disp.Dispose();
        _target.Dispose(); _visual.Dispose(); _swap.Dispose(); Nat.DestroyWindow(Hwnd);
    }
}
