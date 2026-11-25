using System;
using System.IO;
using SkiaSharp;

namespace MotionDesk.Services;

/// <summary>Bounded, static raster decoding; no dependence on installed WebP codecs.
/// Native Skia is loaded only when a pack image is decoded or a WebP tile is displayed.</summary>
public static class WallpaperPackImage
{
    public static SKBitmap Decode(byte[] bytes, string extension)
    {
        if (bytes.Length == 0 || bytes.Length > 32 * 1024 * 1024)
            throw new InvalidDataException("Image byte quota exceeded.");
        using var stream = new MemoryStream(bytes, writable: false);
        using var codec = SKCodec.Create(stream) ?? throw new InvalidDataException("Invalid image.");
        var expected = extension.ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => SKEncodedImageFormat.Jpeg,
            ".png" => SKEncodedImageFormat.Png,
            ".webp" => SKEncodedImageFormat.Webp,
            _ => throw new InvalidDataException("Unsupported image extension.")
        };
        var info = codec.Info;
        if (codec.EncodedFormat != expected || codec.FrameCount > 1 ||
            info.Width <= 0 || info.Height <= 0 || info.Width > 8192 || info.Height > 8192 ||
            (long)info.Width * info.Height > 40_000_000)
            throw new InvalidDataException("Invalid image format, animation or dimensions.");
        var bitmap = new SKBitmap(new SKImageInfo(info.Width, info.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        try
        {
            if (bitmap.GetPixels() == IntPtr.Zero ||
                codec.GetPixels(bitmap.Info, bitmap.GetPixels()) != SKCodecResult.Success)
                throw new InvalidDataException("Incomplete or invalid image data.");
            return bitmap;
        }
        catch { bitmap.Dispose(); throw; }
    }

    public static System.Drawing.Image ReadThumbnail(string path, int width)
    {
        if (new FileInfo(path).Length > 32L * 1024 * 1024)
            throw new InvalidDataException("Image byte quota exceeded.");
        using var bitmap = Decode(File.ReadAllBytes(path), Path.GetExtension(path));
        int w = Math.Clamp(width, 1, Math.Min(960, bitmap.Width));
        int h = Math.Clamp((int)((long)bitmap.Height * w / bitmap.Width), 1, 960);
        using var thumb = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(thumb))
        {
            canvas.Clear(SKColors.Transparent);
            canvas.DrawBitmap(bitmap, new SKRect(0, 0, w, h));
        }
        using var image = SKImage.FromBitmap(thumb);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100) ?? throw new InvalidDataException("Thumbnail encoding failed.");
        using var stream = new MemoryStream(encoded.ToArray(), writable: false);
        using var original = System.Drawing.Image.FromStream(stream);
        return new System.Drawing.Bitmap(original);
    }
}
