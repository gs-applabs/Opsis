// Phase 0A spike (plan 2.4.C): can a TOP-LEVEL WPF window (AllowsTransparency) carry a pre-rendered "liquid glass" bitmap cheaply enough?
// This is a throw-away measurement tool, not product code. The glass bitmap is made on the CPU here (blurred backdrop + refraction at the bezel + rim light);
// the Direct2D / Vortice version is NOT measured (see docs/CONTAINERS_NATIVE_DESIGN.md).
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using PixelFormat = System.Drawing.Imaging.PixelFormat;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace GlassSpike;

internal static class Program
{
    private static readonly StringBuilder Report = new();
    private static string? _out;
    private static void Log(string s) { Console.WriteLine(s); Report.AppendLine(s); if (_out != null) File.AppendAllText(_out, s + Environment.NewLine); }

    [STAThread]
    private static int Main(string[] args)
    {
        var outFile = args.Length > 0 ? args[0] : "spike-results.txt"; _out = outFile; File.WriteAllText(outFile, "");
        var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var cores = Environment.ProcessorCount;
        var screens = System.Windows.Forms.Screen.AllScreens;
        Log($"machine: {screens.Length} screen(s): " + string.Join(", ", screens.Select(s => $"{s.Bounds.Width}x{s.Bounds.Height}")) + $"; {cores} logical cores; pid {Environment.ProcessId}");

        // ---- P1: cost of one glass bitmap
        var rng = new Random(1);
        foreach (var (w, h, label) in new[] { (420, 300, "420x300 (100%)"), (630, 450, "630x450 (150%)"), (840, 600, "840x600 (200%)"), (1260, 900, "1260x900 (300%)") })
        {
            var back = Snapshot(w, h);
            Glass.Render(back, w, h); // warm up
            var sw = Stopwatch.StartNew();
            const int runs = 20;
            for (var i = 0; i < runs; i++) Glass.Render(back, w, h);
            Log($"P1 glass render {label}: {sw.Elapsed.TotalMilliseconds / runs:0.0} ms per bitmap (parallel rows)");
        }
        {
            var sw = Stopwatch.StartNew();
            for (var i = 0; i < 10; i++) { using var b = new Bitmap(screens[0].Bounds.Width, screens[0].Bounds.Height, PixelFormat.Format32bppPArgb); using var g = Graphics.FromImage(b); g.CopyFromScreen(screens[0].Bounds.Location, System.Drawing.Point.Empty, b.Size); }
            Log($"P1 full-screen snapshot {screens[0].Bounds.Width}x{screens[0].Bounds.Height} (CopyFromScreen): {sw.Elapsed.TotalMilliseconds / 10:0.0} ms");
        }

        // ---- windows
        var windows = new List<Window>();
        const int count = 8;
        for (var i = 0; i < count; i++)
        {
            var back = Snapshot(420, 300);
            var win = new Window
            {
                WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = System.Windows.Media.Brushes.Transparent, ShowInTaskbar = false,
                Width = 420, Height = 300, Left = 60 + (i % 4) * 300, Top = 60 + (i / 4) * 340, Topmost = false, ShowActivated = false,
                Content = new System.Windows.Controls.Image { Source = Glass.Render(back, 420, 300), Stretch = Stretch.Fill }
            };
            win.Show();
            windows.Add(win);
        }
        var dpi = VisualTreeHelper.GetDpi(windows[0]);
        Log($"windows: {count} top-level transparent WPF windows 420x300 DIPs; DPI scale {dpi.DpiScaleX:0.00} (this machine's real scale; 150/200% not switched)");
        Pump(500);

        var proc = Process.GetCurrentProcess();
        double Cpu(Action phase, out double seconds)
        {
            proc.Refresh(); var c0 = proc.TotalProcessorTime; var sw = Stopwatch.StartNew();
            phase(); sw.Stop(); proc.Refresh();
            seconds = sw.Elapsed.TotalSeconds;
            return (proc.TotalProcessorTime - c0).TotalSeconds / seconds / cores * 100.0;
        }

        // ---- P2 idle
        var idle = Cpu(() => Pump(5000), out var s2);
        Log($"P2 idle, {count} glass windows, {s2:0.0}s: CPU {idle:0.00}% of the machine");

        // ---- P3 drag one window around
        var frames = new List<double>();
        var drag = Cpu(() =>
        {
            var w0 = windows[0]; var sw = Stopwatch.StartNew(); double last = 0;
            void OnRender(object? s, EventArgs e)
            {
                var t = sw.Elapsed.TotalSeconds; frames.Add((t - last) * 1000); last = t;
                w0.Left = 100 + 700 * (0.5 + 0.5 * Math.Sin(t * 2)); w0.Top = 100 + 400 * (0.5 + 0.5 * Math.Cos(t * 1.7));
            }
            CompositionTarget.Rendering += OnRender;
            Pump(5000);
            CompositionTarget.Rendering -= OnRender;
        }, out var s3);
        frames.RemoveAt(0);
        var sorted = frames.OrderBy(x => x).ToList();
        Log($"P3 drag 1 of {count}, {s3:0.0}s: CPU {drag:0.00}%; {frames.Count / s3:0} fps, frame gap median {sorted[sorted.Count / 2]:0.0} ms, p95 {sorted[(int)(sorted.Count * 0.95)]:0.0} ms, max {sorted[^1]:0.0} ms");

        // ---- P4 resize: stretch the same bitmap vs regenerate every frame (fixed 60 steps, driven by a timer, so a slow path cannot hang the run)
        foreach (var regenerate in new[] { false, true })
        {
            var w1 = windows[1]; var n = 0;
            var img = (System.Windows.Controls.Image)w1.Content;
            var swTotal = Stopwatch.StartNew();
            var cpu = Cpu(() =>
            {
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
                var frame = new DispatcherFrame();
                timer.Tick += (_, _) =>
                {
                    var t = n / 60.0; n++;
                    var w = 420 + 300 * (0.5 + 0.5 * Math.Sin(t * 3)); var h = 300 + 200 * (0.5 + 0.5 * Math.Cos(t * 2.5));
                    w1.Width = w; w1.Height = h;
                    if (regenerate) img.Source = Glass.Render(Snapshot((int)w, (int)h), (int)w, (int)h);
                    if (n >= 120) { timer.Stop(); frame.Continue = false; }
                };
                timer.Start();
                Dispatcher.PushFrame(frame);
            }, out var s4);
            Log($"P4 resize one window, {(regenerate ? "REGENERATE glass every step" : "stretch the same bitmap")}: 120 steps in {s4:0.0}s = {n / s4:0} steps/s, CPU {cpu:0.00}%");
        }

        // ---- P5 live backdrop: all windows re-glassed at 2 Hz (a live wallpaper behind them = periodic snapshot)
        var live = Cpu(() =>
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            timer.Tick += (_, _) => { foreach (var w in windows) ((System.Windows.Controls.Image)w.Content).Source = Glass.Render(Snapshot(420, 300), 420, 300); };
            timer.Start(); Pump(6000); timer.Stop();
        }, out var s5);
        Log($"P5 live backdrop: {count} windows re-rendered at 2 Hz, {s5:0.0}s: CPU {live:0.00}%");

        foreach (var w in windows) w.Close();
        app.Shutdown();
        return 0;
    }

    private static void Pump(int ms)
    {
        var end = DateTime.UtcNow.AddMilliseconds(ms);
        var frame = new DispatcherFrame();
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
        t.Tick += (_, _) => { t.Stop(); frame.Continue = false; };
        t.Start();
        Dispatcher.PushFrame(frame);
    }

    // a w*h BGRA crop of the real desktop (the backdrop of a glass window)
    private static byte[] Snapshot(int w, int h)
    {
        using var b = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(b)) g.CopyFromScreen(60, 60, 0, 0, new System.Drawing.Size(w, h));
        var data = b.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
        var bytes = new byte[data.Stride * h];
        System.Runtime.InteropServices.Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
        b.UnlockBits(data);
        return bytes;
    }
}

internal static class Glass
{
    /// <summary>Blur (1/4 scale box blur x2) + refraction near the rounded edge + tint + rim light, into a frozen BGRA bitmap with transparent corners.</summary>
    public static unsafe BitmapSource Render(byte[] backdrop, int w, int h)
    {
        var stride = w * 4;
        var sw = Math.Max(2, w / 4); var sh = Math.Max(2, h / 4);
        var small = new int[sw * sh];
        // downscale
        Parallel.For(0, sh, y =>
        {
            for (var x = 0; x < sw; x++)
            {
                int r = 0, g = 0, b = 0, n = 0;
                for (var dy = 0; dy < 4; dy++)
                    for (var dx = 0; dx < 4; dx++)
                    {
                        var sx = Math.Min(w - 1, x * 4 + dx); var sy = Math.Min(h - 1, y * 4 + dy);
                        var i = sy * stride + sx * 4; b += backdrop[i]; g += backdrop[i + 1]; r += backdrop[i + 2]; n++;
                    }
                small[y * sw + x] = ((r / n) << 16) | ((g / n) << 8) | (b / n);
            }
        });
        var tmp = new int[small.Length];
        for (var pass = 0; pass < 2; pass++) { Box(small, tmp, sw, sh, 3, true); Box(tmp, small, sw, sh, 3, false); }

        var pixels = new byte[stride * h];
        const float radius = 16f, bezel = 26f;
        Parallel.For(0, h, y =>
        {
            for (var x = 0; x < w; x++)
            {
                // distance inside a rounded rectangle
                var dx = Math.Min(x, w - 1 - x); var dy = Math.Min(y, h - 1 - y);
                float d;
                if (dx < radius && dy < radius) { var cx = radius - dx; var cy = radius - dy; d = radius - MathF.Sqrt(cx * cx + cy * cy); } else d = Math.Min(dx, dy);
                var o = y * stride + x * 4;
                if (d < 0) { continue; } // transparent corner
                // refraction: sample towards the centre, stronger near the edge
                float sx = x, sy = y;
                if (d < bezel)
                {
                    var t = 1f - d / bezel; var k = t * t * 14f;
                    sx += (w / 2f - x) / (w / 2f) * k; sy += (h / 2f - y) / (h / 2f) * k;
                }
                var bx = Math.Clamp((int)(sx / 4), 0, sw - 1); var by = Math.Clamp((int)(sy / 4), 0, sh - 1);
                var c = small[by * sw + bx];
                int r = (c >> 16) & 255, g = (c >> 8) & 255, b = c & 255;
                // tint (+12% white) and a brighter rim
                r += (255 - r) * 12 / 100; g += (255 - g) * 12 / 100; b += (255 - b) * 12 / 100;
                var rim = d < 1.6f ? 0.35f : 0f;
                r += (int)((255 - r) * rim); g += (int)((255 - g) * rim); b += (int)((255 - b) * rim);
                pixels[o] = (byte)b; pixels[o + 1] = (byte)g; pixels[o + 2] = (byte)r; pixels[o + 3] = 255;
            }
        });
        var bmp = BitmapSource.Create(w, h, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, pixels, stride);
        bmp.Freeze();
        return bmp;
    }

    private static void Box(int[] src, int[] dst, int w, int h, int r, bool horizontal)
    {
        var lines = horizontal ? h : w; var len = horizontal ? w : h;
        Parallel.For(0, lines, line =>
        {
            for (var i = 0; i < len; i++)
            {
                int rr = 0, gg = 0, bb = 0, n = 0;
                for (var k = Math.Max(0, i - r); k <= Math.Min(len - 1, i + r); k++)
                {
                    var c = src[horizontal ? line * w + k : k * w + line]; rr += (c >> 16) & 255; gg += (c >> 8) & 255; bb += c & 255; n++;
                }
                dst[horizontal ? line * w + i : i * w + line] = ((rr / n) << 16) | ((gg / n) << 8) | (bb / n);
            }
        });
    }
}
