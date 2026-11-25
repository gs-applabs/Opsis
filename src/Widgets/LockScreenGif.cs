using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace MotionDesk.Widgets
{
    /// <summary>
    /// A GIF as a source for the lock screen picture (new code; no network, nothing is written outside the folder given by the caller). Windows itself cannot play an animation on the lock
    /// screen, so Opsis offers what is supported: pick a frame of the GIF and use it through the normal lock-screen image mechanism (policy value, with the existing reset button).
    /// This class only checks the file and cuts a frame out of it; it never touches system files.
    /// </summary>
    internal static class LockScreenGif
    {
        public const long MaxBytes = 25L * 1024 * 1024;
        public const int MaxSide = 8192, MaxFrames = 2000;

        public sealed record Info(bool Ok, string Reason, int Width, int Height, int Frames);

        /// <summary>Checks the file (exists, size, GIF header, dimensions, frame count). Never throws.</summary>
        public static Info Validate(string? path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return new(false, "the file does not exist", 0, 0, 0);
                var fi = new FileInfo(path);
                if (fi.Length <= 0) return new(false, "the file is empty", 0, 0, 0);
                if (fi.Length > MaxBytes) return new(false, "the file is larger than " + MaxBytes / 1024 / 1024 + " MB", 0, 0, 0);
                var head = new byte[6];
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)) { if (fs.Read(head, 0, 6) < 6) return new(false, "not a GIF file", 0, 0, 0); }
                var sig = System.Text.Encoding.ASCII.GetString(head);
                if (sig != "GIF87a" && sig != "GIF89a") return new(false, "not a GIF file (wrong header)", 0, 0, 0);
                using var img = Image.FromFile(path);
                if (img.Width < 16 || img.Height < 16) return new(false, "the picture is too small", img.Width, img.Height, 0);
                if (img.Width > MaxSide || img.Height > MaxSide) return new(false, "the picture is larger than " + MaxSide + " pixels", img.Width, img.Height, 0);
                int frames = Math.Max(1, img.GetFrameCount(FrameDimension.Time));
                if (frames > MaxFrames) return new(false, "too many frames (" + frames + ")", img.Width, img.Height, frames);
                return new(true, "", img.Width, img.Height, frames);
            }
            catch (Exception ex) when (ex is OutOfMemoryException or ArgumentException or IOException or UnauthorizedAccessException or System.Runtime.InteropServices.ExternalException)
            {
                return new(false, "the file could not be read as a GIF", 0, 0, 0);
            }
        }

        /// <summary>Saves one frame (clamped to the valid range) as a PNG in <paramref name="folder"/> and returns its path, or null when the GIF is not valid.</summary>
        public static string? ExtractFrame(string path, int frame, string folder)
        {
            if (!Validate(path).Ok) return null;
            Directory.CreateDirectory(folder);
            using var img = Image.FromFile(path);
            int count = Math.Max(1, img.GetFrameCount(FrameDimension.Time));
            img.SelectActiveFrame(FrameDimension.Time, Math.Clamp(frame, 0, count - 1));
            var png = Path.Combine(folder, "lockscreen-frame.png");
            using var bmp = new Bitmap(img.Width, img.Height); using (var g = Graphics.FromImage(bmp)) g.DrawImage(img, 0, 0, img.Width, img.Height);
            bmp.Save(png, ImageFormat.Png);
            return png;
        }
    }
}
