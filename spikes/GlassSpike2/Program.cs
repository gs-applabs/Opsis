// Spike v2 (Phase 0A): TRUE per-pixel-alpha windows (Win32 layered windows, UpdateLayeredWindow) over the live desktop, with a LIVE backdrop for refraction.
// Throw-away measurement tool. The glass maths run on the CPU here; the GPU (Direct2D / DXGI duplication) version is NOT built (stated in the design doc).
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

namespace GlassSpike2;

internal static unsafe class Program
{
    private static readonly StringBuilder Report = new();
    private static string _out = "results2.txt";
    private static void Log(string s) { var line = DateTime.Now.ToString("HH:mm:ss.fff") + " " + s; Console.WriteLine(line); File.AppendAllText(_out, line + Environment.NewLine); }

    private static int Main(string[] args)
    {
        _out = args.Length > 0 ? args[0] : _out;
        var shots = args.Length > 1 ? args[1] : Path.GetDirectoryName(Path.GetFullPath(_out))!;
        File.WriteAllText(_out, "");
        Nat.SetProcessDpiAwarenessContext(new IntPtr(-4)); // per-monitor v2
        var cores = Environment.ProcessPath is null ? 1 : Environment.ProcessorCount;
        var scr = new Rectangle(0, 0, Nat.GetSystemMetrics(0), Nat.GetSystemMetrics(1));
        Log($"machine: {scr.Width}x{scr.Height} (1 screen), {cores} cores, build {Environment.OSVersion.Version.Build}; label={Environment.GetEnvironmentVariable("SPIKE_LABEL") ?? "-"}");

        // ---- stage: a borderless topmost window that plays Opsis' OWN live wallpaper page (src/Widgets/wallpaper/index.html in its procedural "Waves" mode), standing in for the
        // wallpaper window (which sits behind the icons). The glass windows are composed above it by DWM exactly as they would be above the real one. It also keeps the
        // screenshots free of whatever is open on the user's desktop.
        var page = Environment.GetEnvironmentVariable("SPIKE_STAGE_PAGE");
        if (!string.IsNullOrEmpty(page) && File.Exists(page))
        {
            var ready = new ManualResetEventSlim();
            Stage.Start(page, ready);
            Log(ready.Wait(15000) ? "stage: Opsis wallpaper page loaded in a 1700x900 stand-in window" : "stage: page did not finish loading (continuing)");
            Thread.Sleep(2500);
        }

        // ---- P0: is a window with WDA_EXCLUDEFROMCAPTURE really absent from a BitBlt capture? (needed for a live backdrop without feedback)
        {
            var w = new GlassWin(300, 300, 200, 150, exclude: true);
            w.PaintSolid(255, 0, 0);       // opaque red
            Thread.Sleep(300);
            var px = Capture(300 + 100, 300 + 75, 1, 1)[2]; // red channel at the window centre
            var withoutAffinity = false;
            w.SetExclude(false); Thread.Sleep(200);
            var px2 = Capture(300 + 100, 300 + 75, 1, 1)[2];
            withoutAffinity = px2 > 200;
            Log($"P0 exclude-from-capture: captured red channel behind the window with affinity={px}, without affinity={px2} -> {(px < 200 && withoutAffinity ? "EXCLUDED OK (BitBlt does not see the window)" : "NOT excluded")}");
            w.Dispose();
        }

        // ---- P1/P2: transparent windows (true alpha), idle and drag
        var wins = new List<GlassWin>();
        for (var i = 0; i < 6; i++) wins.Add(new GlassWin(120 + (i % 3) * 460, 140 + (i / 3) * 360, 420, 300, exclude: true));
        foreach (var w in wins) w.PaintTransparent();
        Thread.Sleep(300);
        var proc = Process.GetCurrentProcess();
        double Cpu(Action a, out double secs)
        {
            proc.Refresh(); var c0 = proc.TotalProcessorTime; var sw = Stopwatch.StartNew(); a(); sw.Stop(); proc.Refresh();
            secs = sw.Elapsed.TotalSeconds; return (proc.TotalProcessorTime - c0).TotalSeconds / secs / cores * 100;
        }
        var idle = Cpu(() => Thread.Sleep(3000), out var s1);
        Log($"P1 idle, 6 transparent layered windows 420x300: CPU {idle:0.00}%");
        var frames = 0;
        var drag = Cpu(() =>
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 3000)
            {
                var t = sw.Elapsed.TotalSeconds;
                wins[0].MoveTo((int)(150 + 700 * (0.5 + 0.5 * Math.Sin(t * 2))), (int)(100 + 300 * (0.5 + 0.5 * Math.Cos(t * 1.7))));
                frames++; Thread.Sleep(8);
            }
        }, out var s2);
        Log($"P2 drag one transparent window (SetWindowPos, bitmap untouched): {frames / s2:0} moves/s, CPU {drag:0.00}%");
        Shot(shots, "v1-transparent", wins, scr);

        // ---- P3: live refraction (capture behind the window, glass maths, UpdateLayeredWindow)
        foreach (var (w, h, label) in new[] { (420, 300, "100%"), (630, 450, "150%"), (840, 600, "200%") })
        {
            var g = new GlassWin(200, 150, w, h, exclude: true);
            var tc = 0.0; var tr = 0.0; var tu = 0.0; var n = 0;
            var cpu = Cpu(() =>
            {
                var sw = Stopwatch.StartNew();
                while (sw.ElapsedMilliseconds < 3000)
                {
                    var t0 = Stopwatch.GetTimestamp();
                    var back = Capture(g.X, g.Y, w, h);
                    var t1 = Stopwatch.GetTimestamp();
                    var bmp = GlassMath.Render(back, w, h);
                    var t2 = Stopwatch.GetTimestamp();
                    g.Present(bmp);
                    var t3 = Stopwatch.GetTimestamp();
                    tc += Ms(t1 - t0); tr += Ms(t2 - t1); tu += Ms(t3 - t2); n++;
                }
            }, out var s3);
            Log($"P3 live refraction {w}x{h} (like {label} DPI): {n / s3:0.0} fps flat out, per frame capture {tc / n:0.0} ms + glass {tr / n:0.0} ms + present {tu / n:0.0} ms, CPU {cpu:0.0}%");
            if (label == "100%") Shot(shots, "v3-refraction", new List<GlassWin> { g }, scr);
            g.Dispose();
        }

        // ---- P4: 4 live glass windows at a 30 fps budget
        {
            var gs = Enumerable.Range(0, 4).Select(i => new GlassWin(120 + i * 330, 200, 320, 240, exclude: true)).ToList();
            var n = 0;
            var cpu = Cpu(() =>
            {
                var sw = Stopwatch.StartNew();
                while (sw.ElapsedMilliseconds < 4000)
                {
                    var frameStart = sw.ElapsedMilliseconds;
                    // ONE capture of the union rectangle per frame, cropped per window (the capture cost is per call, ~one display frame, not per pixel)
                    int ux = gs.Min(g => g.X), uy = gs.Min(g => g.Y), uw = gs.Max(g => g.X + g.W) - ux, uh = gs.Max(g => g.Y + g.H) - uy;
                    var all = Capture(ux, uy, uw, uh);
                    foreach (var g in gs) g.Present(GlassMath.Render(Crop(all, uw, g.X - ux, g.Y - uy, g.W, g.H), g.W, g.H));
                    n++;
                    var wait = 33 - (sw.ElapsedMilliseconds - frameStart);
                    if (wait > 0) Thread.Sleep((int)wait);
                }
            }, out var s4);
            Log($"P4 4 live glass windows 320x240, ONE union capture per frame, 30 fps budget: reached {n / s4:0.0} fps, CPU {cpu:0.0}%");
            Shot(shots, "v3-refraction-4", gs, scr);
            foreach (var g in gs) g.Dispose();
        }

        // ---- P5: DWM system backdrop (Acrylic) on a normal popup: the live blur is done by the compositor, not by us
        {
            var a = new GlassWin(300, 200, 420, 300, exclude: false, dwmBackdrop: true);
            Thread.Sleep(600);
            var cpu = Cpu(() => Thread.Sleep(3000), out var s5);
            Log($"P5 DWM Acrylic window 420x300 (live blur by DWM): our process CPU {cpu:0.00}% (the GPU work is the compositor's)");
            Shot(shots, "v2-dwm-acrylic", new List<GlassWin> { a }, scr);
            a.Dispose();
        }

        foreach (var w in wins) w.Dispose();
        Log("done");
        return 0;
    }

    private static byte[] Crop(byte[] src, int srcW, int x, int y, int w, int h)
    {
        var dst = new byte[w * h * 4];
        for (var row = 0; row < h; row++) Buffer.BlockCopy(src, ((y + row) * srcW + x) * 4, dst, row * w * 4, w * 4);
        return dst;
    }

    private static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

    // a BGRA copy of a screen rectangle as the compositor shows it (windows with WDA_EXCLUDEFROMCAPTURE are not in it)
    internal static byte[] Capture(int x, int y, int w, int h)
    {
        var screen = Nat.GetDC(IntPtr.Zero);
        var mem = Nat.CreateCompatibleDC(screen);
        var bi = new Nat.BITMAPINFO { biSize = 40, biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
        var dib = Nat.CreateDIBSection(mem, ref bi, 0, out var bits, IntPtr.Zero, 0);
        var old = Nat.SelectObject(mem, dib);
        Nat.BitBlt(mem, 0, 0, w, h, screen, x, y, 0x00CC0020 | 0x40000000); // SRCCOPY | CAPTUREBLT
        var bytes = new byte[w * h * 4];
        Marshal.Copy(bits, bytes, 0, bytes.Length);
        Nat.SelectObject(mem, old); Nat.DeleteObject(dib); Nat.DeleteDC(mem); Nat.ReleaseDC(IntPtr.Zero, screen);
        return bytes;
    }

    private static void Shot(string dir, string name, List<GlassWin> wins, Rectangle scr)
    {
        foreach (var w in wins) w.SetExclude(false);
        Thread.Sleep(400);
        var r = Rectangle.Union(Rectangle.Empty, new Rectangle(wins.Min(w => w.X) - 40, wins.Min(w => w.Y) - 40, 0, 0));
        var x0 = Math.Max(0, wins.Min(w => w.X) - 40); var y0 = Math.Max(0, wins.Min(w => w.Y) - 40);
        var x1 = Math.Min(scr.Width, wins.Max(w => w.X + w.W) + 40); var y1 = Math.Min(scr.Height, wins.Max(w => w.Y + w.H) + 40);
        var bytes = Capture(x0, y0, x1 - x0, y1 - y0);
        using var bmp = new Bitmap(x1 - x0, y1 - y0, PixelFormat.Format32bppRgb);
        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
        Marshal.Copy(bytes, 0, data.Scan0, bytes.Length); bmp.UnlockBits(data);
        bmp.Save(Path.Combine(dir, name + ".png"), ImageFormat.Png);
        foreach (var w in wins) w.SetExclude(true);
        Log($"   screenshot {name}.png (region {x0},{y0} {x1 - x0}x{y1 - y0})");
    }
}

internal static class GlassMath
{
    /// <summary>Refraction near the rounded bezel + tint + rim over a captured backdrop; premultiplied BGRA, transparent corners.</summary>
    public static byte[] Render(byte[] back, int w, int h)
    {
        var stride = w * 4; var outp = new byte[stride * h];
        const float radius = 16f, bezel = 26f;
        // a light 1/4 blur of the backdrop for the frosted part
        var sw = Math.Max(2, w / 4); var sh = Math.Max(2, h / 4); var small = new int[sw * sh];
        Parallel.For(0, sh, y => { for (var x = 0; x < sw; x++) { int r = 0, g = 0, b = 0, n = 0; for (var dy = 0; dy < 4; dy++) for (var dx = 0; dx < 4; dx++) { var sx = Math.Min(w - 1, x * 4 + dx); var sy = Math.Min(h - 1, y * 4 + dy); var i = sy * stride + sx * 4; b += back[i]; g += back[i + 1]; r += back[i + 2]; n++; } small[y * sw + x] = ((r / n) << 16) | ((g / n) << 8) | (b / n); } });
        Parallel.For(0, h, y =>
        {
            for (var x = 0; x < w; x++)
            {
                var dx = Math.Min(x, w - 1 - x); var dy = Math.Min(y, h - 1 - y);
                float d;
                if (dx < radius && dy < radius) { var cx = radius - dx; var cy = radius - dy; d = radius - MathF.Sqrt(cx * cx + cy * cy); } else d = Math.Min(dx, dy);
                var o = y * stride + x * 4;
                if (d < 0) continue;
                float sx = x, sy = y;
                if (d < bezel) { var t = 1f - d / bezel; var k = t * t * 14f; sx += (w / 2f - x) / (w / 2f) * k; sy += (h / 2f - y) / (h / 2f) * k; }
                var xi = Math.Clamp((int)sx, 0, w - 1); var yi = Math.Clamp((int)sy, 0, h - 1);
                var bi = yi * stride + xi * 4;
                int b0 = back[bi], g0 = back[bi + 1], r0 = back[bi + 2];
                var c = small[Math.Clamp((int)(sy / 4), 0, sh - 1) * sw + Math.Clamp((int)(sx / 4), 0, sw - 1)];
                // sharp refracted pixel mixed with the blurred one (frosted), then a light tint
                int r = (r0 * 6 + ((c >> 16) & 255) * 4) / 10, g = (g0 * 6 + ((c >> 8) & 255) * 4) / 10, b = (b0 * 6 + (c & 255) * 4) / 10;
                r += (255 - r) * 10 / 100; g += (255 - g) * 10 / 100; b += (255 - b) * 10 / 100;
                if (d < 1.6f) { r += (255 - r) * 35 / 100; g += (255 - g) * 35 / 100; b += (255 - b) * 35 / 100; }
                outp[o] = (byte)b; outp[o + 1] = (byte)g; outp[o + 2] = (byte)r; outp[o + 3] = 255;
            }
        });
        return outp;
    }
}

internal sealed unsafe class GlassWin : IDisposable
{
    private static readonly Nat.WndProc Proc = (h, m, wp, lp) => Nat.DefWindowProc(h, m, wp, lp);
    private static bool _registered;
    public IntPtr Hwnd { get; }
    public int X, Y, W, H;
    private readonly IntPtr _memDc, _dib, _bits;
    private readonly bool _layered;

    public GlassWin(int x, int y, int w, int h, bool exclude, bool dwmBackdrop = false)
    {
        X = x; Y = y; W = w; H = h; _layered = !dwmBackdrop;
        if (!_registered)
        {
            var cls = new Nat.WNDCLASS { lpfnWndProc = Marshal.GetFunctionPointerForDelegate(Proc), hInstance = Nat.GetModuleHandle(null), lpszClassName = "GlassSpike2Wnd", hCursor = Nat.LoadCursor(IntPtr.Zero, 32512) };
            Nat.RegisterClass(ref cls); _registered = true;
        }
        var ex = 0x00000080 /*TOOLWINDOW*/ | 0x08000000 /*NOACTIVATE*/ | 0x00000008 /*TOPMOST*/ | (_layered ? 0x00080000 : 0);
        Hwnd = Nat.CreateWindowEx(ex, "GlassSpike2Wnd", "g", 0x80000000 /*POPUP*/, x, y, w, h, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        var screen = Nat.GetDC(IntPtr.Zero);
        _memDc = Nat.CreateCompatibleDC(screen);
        var bi = new Nat.BITMAPINFO { biSize = 40, biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
        _dib = Nat.CreateDIBSection(_memDc, ref bi, 0, out _bits, IntPtr.Zero, 0);
        Nat.SelectObject(_memDc, _dib);
        Nat.ReleaseDC(IntPtr.Zero, screen);
        if (dwmBackdrop)
        {
            int dark = 1, type = 3, round = 2; // 3 = Acrylic (transient window), rounded corners
            Nat.DwmSetWindowAttribute(Hwnd, 20, ref dark, 4);
            Nat.DwmSetWindowAttribute(Hwnd, 33, ref round, 4);
            Nat.DwmSetWindowAttribute(Hwnd, 38, ref type, 4);
            var m = new Nat.MARGINS { l = -1, r = -1, t = -1, b = -1 };
            Nat.DwmExtendFrameIntoClientArea(Hwnd, ref m);
            Nat.ShowWindow(Hwnd, 4);
            Nat.SetWindowPos(Hwnd, new IntPtr(-1), x, y, w, h, 0x0010);
            Pump();
        }
        else
        {
            PaintTransparent();
            Nat.ShowWindow(Hwnd, 4);
        }
        if (exclude) SetExclude(true);
    }

    public void SetExclude(bool on) => Nat.SetWindowDisplayAffinity(Hwnd, on ? 0x11u : 0u);

    private static void Pump() { while (Nat.PeekMessage(out var m, IntPtr.Zero, 0, 0, 1)) { Nat.TranslateMessage(ref m); Nat.DispatchMessage(ref m); } }

    public void MoveTo(int x, int y) { X = x; Y = y; Nat.SetWindowPos(Hwnd, IntPtr.Zero, x, y, 0, 0, 0x0001 | 0x0010 | 0x0004); }

    /// <summary>Solid opaque colour (used only to prove the capture exclusion).</summary>
    public void PaintSolid(byte r, byte g, byte b)
    {
        var p = (byte*)_bits; for (var i = 0; i < W * H; i++) { p[i * 4] = b; p[i * 4 + 1] = g; p[i * 4 + 2] = r; p[i * 4 + 3] = 255; }
        Update();
    }

    /// <summary>A mostly transparent glass pane: premultiplied white at ~14 % alpha, a brighter 1 px rim, transparent rounded corners.</summary>
    public void PaintTransparent()
    {
        var p = (byte*)_bits; const float radius = 16f;
        for (var y = 0; y < H; y++)
            for (var x = 0; x < W; x++)
            {
                var dx = Math.Min(x, W - 1 - x); var dy = Math.Min(y, H - 1 - y);
                float d; if (dx < radius && dy < radius) { var cx = radius - dx; var cy = radius - dy; d = radius - MathF.Sqrt(cx * cx + cy * cy); } else d = Math.Min(dx, dy);
                var o = (y * W + x) * 4;
                if (d < 0) { p[o] = p[o + 1] = p[o + 2] = p[o + 3] = 0; continue; }
                var a = d < 1.6f ? 110 : 36; var v = (byte)a; // premultiplied white
                p[o] = p[o + 1] = p[o + 2] = v; p[o + 3] = (byte)a;
            }
        Update();
    }

    public void Present(byte[] premultipliedBgra) { Marshal.Copy(premultipliedBgra, 0, _bits, premultipliedBgra.Length); Update(); }

    private void Update()
    {
        var screen = Nat.GetDC(IntPtr.Zero);
        var size = new Nat.SIZE { cx = W, cy = H }; var src = new Nat.POINT(); var dst = new Nat.POINT { x = X, y = Y };
        var blend = new Nat.BLENDFUNCTION { BlendOp = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
        Nat.UpdateLayeredWindow(Hwnd, screen, ref dst, ref size, _memDc, ref src, 0, ref blend, 2);
        Nat.ReleaseDC(IntPtr.Zero, screen);
    }

    public void Dispose() { Nat.DestroyWindow(Hwnd); Nat.DeleteObject(_dib); Nat.DeleteDC(_memDc); }
}

internal static class Nat
{
    public delegate IntPtr WndProc(IntPtr h, uint m, IntPtr wp, IntPtr lp);
    [StructLayout(LayoutKind.Sequential)] public struct WNDCLASS { public uint style; public IntPtr lpfnWndProc; public int cbClsExtra, cbWndExtra; public IntPtr hInstance, hIcon, hCursor, hbrBackground; [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName; [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName; }
    [StructLayout(LayoutKind.Sequential)] public struct BITMAPINFO { public int biSize, biWidth, biHeight; public short biPlanes, biBitCount; public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant; }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int x, y; }
    [StructLayout(LayoutKind.Sequential)] public struct SIZE { public int cx, cy; }
    [StructLayout(LayoutKind.Sequential)] public struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
    [StructLayout(LayoutKind.Sequential)] public struct MARGINS { public int l, r, t, b; }
    [StructLayout(LayoutKind.Sequential)] public struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public POINT pt; }
    [DllImport("user32.dll")] public static extern IntPtr DefWindowProc(IntPtr h, uint m, IntPtr wp, IntPtr lp);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern ushort RegisterClass(ref WNDCLASS c);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr CreateWindowEx(int ex, string cls, string name, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
    [DllImport("user32.dll")] public static extern bool DestroyWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int cx, uint flags);
    [DllImport("user32.dll")] public static extern bool UpdateLayeredWindow(IntPtr h, IntPtr dst, ref POINT pptDst, ref SIZE psize, IntPtr src, ref POINT pptSrc, int key, ref BLENDFUNCTION blend, int flags);
    [DllImport("user32.dll")] public static extern bool SetWindowDisplayAffinity(IntPtr h, uint affinity);
    [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int i);
    [DllImport("user32.dll")] public static extern IntPtr LoadCursor(IntPtr inst, int id);
    [DllImport("user32.dll")] public static extern bool PeekMessage(out MSG m, IntPtr h, uint a, uint b, uint remove);
    [DllImport("user32.dll")] public static extern bool TranslateMessage(ref MSG m);
    [DllImport("user32.dll")] public static extern IntPtr DispatchMessage(ref MSG m);
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr c);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr GetModuleHandle(string? n);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFO bi, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr o);
    [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] public static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, int rop);
    [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr h, int a, ref int v, int size);
    [DllImport("dwmapi.dll")] public static extern int DwmExtendFrameIntoClientArea(IntPtr h, ref MARGINS m);
}
