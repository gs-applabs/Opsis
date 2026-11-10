using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using OpsisGlass = Opsis.Glass;

namespace MotionDesk.Services
{
    /// <summary>
    /// 2.6.0: Opsis' use of the GPU glass layer (glass/Opsis.Glass). One <see cref="OpsisGlass.GlassManager"/> for the whole app, created the first time a window asks for it,
    /// fed with what the rest of Opsis knows (performance tier, live wallpaper, lock / remote / fullscreen / battery saver, safe mode). If the GPU part cannot start
    /// the windows keep their old DWM blur look (nothing breaks). Off by default (setting <c>UseGpuGlass</c>) until it has been tested on real machines.
    /// </summary>
    internal static class GlassService
    {
        private static readonly object Gate = new();
        private static OpsisGlass.GlassManager? _manager;
        private static System.Threading.Timer? _timer;
        private static OpsisGlass.GlassConditions? _last;
        private static bool _failed;
        /// <summary>Diagnostics: while true Refresh() does not touch the conditions (a test holds them).</summary>
        internal static volatile bool Hold;

        public static bool Enabled => AppSettings.Load().UseGpuGlass && !SafeModeService.IsSafeMode && Environment.OSVersion.Version.Build >= 22000 && !_failed;

        /// <summary>The manager when glass is enabled and the GPU part is ready; null otherwise (the caller keeps its non-GPU look).</summary>
        public static OpsisGlass.GlassManager? Manager
        {
            get
            {
                if (!Enabled) return null;
                lock (Gate)
                {
                    if (_manager == null)
                    {
                        try
                        {
                            _manager = new OpsisGlass.GlassManager();
                            if (!_manager.IsGpuReady) { RotatingLogger.Write("GlassService: GPU glass unavailable, keeping the DWM look"); _manager.Dispose(); _manager = null; _failed = true; return null; }
                            AppActivity.FullscreenChanged += _ => Refresh();
                            PerformanceGovernor.EffectiveChanged += _ => Refresh();
                            _timer = new System.Threading.Timer(_ => Refresh(), null, 2000, 2000);   // wallpaper on/off, mode changes (cheap: pushes only when something changed)
                            Refresh();
                        }
                        catch (Exception ex) { RotatingLogger.Write("GlassService: " + ex.Message); _manager = null; _failed = true; return null; }
                    }
                    return _manager;
                }
            }
        }

        public static void Refresh()
        {
            var m = _manager; if (m == null || Hold) return;
            try
            {
                var act = AppActivity.Snapshot;
                var wp = WallpaperHostEngine_IsLive();
                var c = new OpsisGlass.GlassConditions
                {
                    Tier = PerformanceGovernor.EffectiveTier switch { HardwareTier.High => OpsisGlass.GlassTier.High, HardwareTier.Balanced => OpsisGlass.GlassTier.Balanced, _ => OpsisGlass.GlassTier.Low },
                    LiveBackdrop = wp,
                    Locked = act.Locked, RemoteSession = act.Remote, FullscreenApp = act.Fullscreen, BatterySaver = act.BatterySaver,
                    SafeMode = SafeModeService.IsSafeMode
                };
                if (_last == c) return;
                _last = c; m.SetConditions(c);
            }
            catch (Exception ex) { RotatingLogger.Write("GlassService.Refresh: " + ex.Message); }
        }

        private static bool WallpaperHostEngine_IsLive()
        {
            try
            {
                var e = MotionDesk.Widgets.WallpaperHostEngine.Instance;
                return e.IsEnabled && MotionDesk.Widgets.WallpaperSettings.Load().Mode is not ("Desktop" or "Image");
            }
            catch (Exception) { return false; }
        }

        /// <summary>The look of a pane, from the user's Glass choices (Opsis.UI.GlassSettings) with the given corner radius.</summary>
        public static OpsisGlass.GlassStyle Style(int cornerRadius)
        {
            var g = AppSettings.Load().Glass;
            Color tint;
            try { tint = ColorTranslator.FromHtml(g.TintColor); } catch (Exception) { tint = Color.FromArgb(0x99, 0xCC, 0xFF); }
            return new OpsisGlass.GlassStyle
            {
                Tint = tint,
                TintAlpha = (float)(0.10 + 0.30 * Math.Clamp(g.Intensity, 0, 100) / 100.0),
                BlurSigma = Math.Clamp(g.Blur, 0, 40) / 5f,
                CornerRadius = cornerRadius,
                Gloss = Math.Clamp(g.Glossy, 0, 100) / 100f,
                Rim = Math.Clamp(g.RimLight, 0, 100) / 100f
            };
        }

        public static void Shutdown()
        {
            lock (Gate) { _timer?.Dispose(); _timer = null; _manager?.Dispose(); _manager = null; }
        }
    }

    /// <summary>
    /// A 32-bit premultiplied-alpha surface for a layered window (UpdateLayeredWindow): the window draws into <see cref="Begin"/>'s Graphics and calls <see cref="Present"/>.
    /// One DIB section reused for every frame; re-created only when the size changes.
    /// </summary>
    internal sealed class LayeredSurface : IDisposable
    {
        private IntPtr _memDc, _dib, _bits;
        private Bitmap? _bmp;
        private int _w, _h;

        public Graphics Begin(int w, int h)
        {
            if (w != _w || h != _h || _bmp == null) Recreate(w, h);
            var g = Graphics.FromImage(_bmp!);
            g.Clear(Color.FromArgb(1, 0, 0, 0));   // alpha 1: invisible, but the window still receives the mouse everywhere (alpha 0 would be click-through)
            return g;
        }

        public void Present(IntPtr hwnd, Point location)
        {
            if (_bmp == null) return;
            var size = new SIZE { cx = _w, cy = _h }; var src = new POINT(); var dst = new POINT { x = location.X, y = location.Y };
            var blend = new BLEND { BlendOp = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
            var screen = GetDC(IntPtr.Zero);
            UpdateLayeredWindow(hwnd, screen, ref dst, ref size, _memDc, ref src, 0, ref blend, 2);
            ReleaseDC(IntPtr.Zero, screen);
        }

        private void Recreate(int w, int h)
        {
            Free();
            _w = w; _h = h;
            var screen = GetDC(IntPtr.Zero);
            _memDc = CreateCompatibleDC(screen);
            var bi = new BITMAPINFOHEADER { biSize = 40, biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
            _dib = CreateDIBSection(_memDc, ref bi, 0, out _bits, IntPtr.Zero, 0);
            SelectObject(_memDc, _dib);
            ReleaseDC(IntPtr.Zero, screen);
            _bmp = new Bitmap(w, h, w * 4, PixelFormat.Format32bppPArgb, _bits);
        }

        private void Free() { _bmp?.Dispose(); _bmp = null; if (_dib != IntPtr.Zero) DeleteObject(_dib); if (_memDc != IntPtr.Zero) DeleteDC(_memDc); _dib = _memDc = _bits = IntPtr.Zero; }
        public void Dispose() => Free();

        [StructLayout(LayoutKind.Sequential)] private struct POINT { public int x, y; }
        [StructLayout(LayoutKind.Sequential)] private struct SIZE { public int cx, cy; }
        [StructLayout(LayoutKind.Sequential)] private struct BLEND { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
        [StructLayout(LayoutKind.Sequential)] private struct BITMAPINFOHEADER { public int biSize, biWidth, biHeight; public short biPlanes, biBitCount; public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant; }
        [DllImport("user32.dll")] private static extern bool UpdateLayeredWindow(IntPtr h, IntPtr dst, ref POINT pd, ref SIZE ps, IntPtr src, ref POINT psrc, int key, ref BLEND b, int flags);
        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFOHEADER bi, uint usage, out IntPtr bits, IntPtr section, uint offset);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr o);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr o);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    }
}
