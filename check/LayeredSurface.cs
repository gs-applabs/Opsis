using System.Drawing.Imaging;
using System.Runtime.InteropServices;
namespace MotionDesk.Services
{
    internal sealed class LayeredSurface : IDisposable
    {
        private IntPtr _memDc, _dib, _bits, _previousBitmap;
        private Bitmap? _bmp;
        private int _w, _h;

        public Graphics Begin(int w, int h)
        {
            if (w != _w || h != _h || _bmp == null) Recreate(w, h);
            var g = Graphics.FromImage(_bmp!);
            g.Clear(Color.FromArgb(1, 0, 0, 0));   // alpha 1: invisible, but the window still receives the mouse everywhere (alpha 0 would be click-through)
            return g;
        }

        public bool Present(IntPtr hwnd, Point location)
        {
            if (_bmp == null) return false;
            var size = new SIZE { cx = _w, cy = _h }; var src = new POINT(); var dst = new POINT { x = location.X, y = location.Y };
            var blend = new BLEND { BlendOp = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
            var screen = GetDC(IntPtr.Zero);
            bool updated = UpdateLayeredWindow(hwnd, screen, ref dst, ref size, _memDc, ref src, 0, ref blend, 2);
            ReleaseDC(IntPtr.Zero, screen);
            return updated;
        }

        private void Recreate(int w, int h)
        {
            Free();
            _w = w; _h = h;
            var screen = GetDC(IntPtr.Zero);
            _memDc = CreateCompatibleDC(screen);
            var bi = new BITMAPINFOHEADER { biSize = 40, biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
            _dib = CreateDIBSection(_memDc, ref bi, 0, out _bits, IntPtr.Zero, 0);
            _previousBitmap = SelectObject(_memDc, _dib);
            ReleaseDC(IntPtr.Zero, screen);
            _bmp = new Bitmap(w, h, w * 4, PixelFormat.Format32bppPArgb, _bits);
        }

        private void Free()
        {
            _bmp?.Dispose();
            _bmp = null;
            if (_memDc != IntPtr.Zero)
            {
                if (_previousBitmap != IntPtr.Zero && _previousBitmap != new IntPtr(-1)) SelectObject(_memDc, _previousBitmap);
                DeleteDC(_memDc);
            }
            if (_dib != IntPtr.Zero) DeleteObject(_dib);
            _dib = _memDc = _bits = _previousBitmap = IntPtr.Zero;
        }
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
