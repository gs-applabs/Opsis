using Microsoft.Web.WebView2.Core;
using MotionDesk.Widgets;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace OpsisMusicOverlay;

internal sealed class WallpaperMusicSurface : IDisposable
{
    private static readonly Lazy<string> Installer = new(() =>
    {
        using var stream = typeof(WallpaperMusicSurface).Assembly.GetManifestResourceStream("OpsisMusicOverlay.wallpaper-music.js")!;
        using var reader = new StreamReader(stream);
        using var suite = typeof(WallpaperMusicSurface).Assembly.GetManifestResourceStream("OpsisMusicOverlay.suite-visualizers.js")!;
        using var suiteReader = new StreamReader(suite);
        return "window.opsisSuiteVisualizer ??= (() => {" + suiteReader.ReadToEnd() + "})();\n" + reader.ReadToEnd();
    });
    private static readonly string WallpaperFile = JsonSerializer.Serialize(
        new Uri(Path.Combine(AppContext.BaseDirectory, "widgets", "wallpaper", "index.html")).AbsoluteUri);

    private readonly WallpaperHostEngine host = WallpaperHostEngine.Instance;
    private CoreWebView2? current;
    private string? cover;
    private byte[]? coverSource;
    private string? coverTrackIdentity;
    private bool installed, pending, disposed;
    private Task<string>? activeRenderScript;
    private Task<string>? activeClearScript;

    // A WaitAsync timeout does not cancel WebView2's underlying native request.
    // Drop frames until that request actually completes; never queue replacement work.
    internal bool CanAcceptFrame => !disposed && !pending && Environment.TickCount64 >= nextAttempt &&
        (activeRenderScript == null || activeRenderScript.IsCompleted) &&
        (activeClearScript == null || activeClearScript.IsCompleted);
    private int revision;
    private long nextAttempt;

    public async Task RenderAsync(Screen? screen, MusicOverlaySettings settings,
        MediaSnapshot? snapshot, float[] bands, bool preview, byte[]? frequency, byte[]? waveform)
    {
        var now = Environment.TickCount64;
        if (screen == null || !CanAcceptFrame) return;
        ObserveCompleted(ref activeRenderScript);
        ObserveCompleted(ref activeClearScript);
        pending = true;
        int frameRevision = revision;
        try
        {
            host.SetMusicSceneRequested(true);
            var core = host.GetMusicRenderer(screen.DeviceName);
            if (core == null) return;
            if (!ReferenceEquals(core, current))
            {
                ClearDrawing();
                current = core;
            }
            if (!installed)
            {
                var ready = await ExecuteRenderAsync(core, Guard(Installer.Value)) == "true";
                if (frameRevision != revision || disposed) return;
                installed = ready;
                if (!installed) return;
            }

            var track = snapshot?.Track;
            var visibleCover = settings.HideTrackMetadata ? null : track?.Cover;
            var visibleIdentity = settings.HideTrackMetadata ? null : track?.Identity;
            bool sendCover = !ReferenceEquals(visibleCover, coverSource) || visibleIdentity != coverTrackIdentity;
            if (sendCover)
            {
                coverSource = visibleCover;
                coverTrackIdentity = visibleIdentity;
                cover = AlbumCoverData(coverSource);
            }
            var elapsed = snapshot?.Timeline?.GetPosition(DateTimeOffset.UtcNow).TotalSeconds ?? 0;
            var payload = JsonSerializer.Serialize(new
            {
                x = settings.X, y = settings.Y,
                width = settings.WidthPercent, height = settings.HeightPercent, colors = settings.Palette,
                font = settings.Typeface, bars = settings.Preset == VisualPreset.MirrorBars,
                title = settings.HideTrackMetadata ? null : track?.Title,
                artist = settings.HideTrackMetadata ? null : track?.Artist,
                album = settings.HideTrackMetadata ? null : track?.Album,
                albumArtist = settings.HideTrackMetadata ? null : track?.AlbumArtist,
                subtitle = settings.HideTrackMetadata ? null : track?.Subtitle,
                source = settings.HideTrackMetadata ? null : track?.Source,
                trackNumber = settings.HideTrackMetadata ? 0 : track?.TrackNumber ?? 0,
                albumTrackCount = settings.HideTrackMetadata ? 0 : track?.AlbumTrackCount ?? 0,
                genres = settings.HideTrackMetadata ? null : track?.Genres,
                trackKey = visibleIdentity,
                reducedMotion = !MotionDesk.Services.AppSettings.Peek().EnableAnimations || System.Windows.Forms.SystemInformation.HighContrast,
                duration = settings.HideTrackMetadata ? 0 : snapshot?.Timeline?.Duration.TotalSeconds ?? 0,
                elapsed = settings.HideTrackMetadata ? 0 : elapsed,
                bands, preview, frequency, waveform, preset = VisualPresets.ModeOf(settings.Preset),
                updateCover = sendCover,
                cover = sendCover ? cover : null
            });
            string? result = await ExecuteRenderAsync(core, Guard($"return window.opsisMusicPainter?.paint({payload}) === true"));
            if (frameRevision == revision && result != "true")
            {
                installed = false;
                coverSource = null;
                coverTrackIdentity = null;
                cover = null;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or COMException or TimeoutException)
        {
            nextAttempt = now + 1500;
            installed = false;
            coverSource = null;
            coverTrackIdentity = null;
            cover = null;
            System.Diagnostics.Debug.WriteLine(ex);
        }
        finally { pending = false; }
    }

    private static string Guard(string script) => $$"""
        (() => {
            if (location.protocol !== 'file:' || location.pathname.toLowerCase() !== new URL({{WallpaperFile}}).pathname.toLowerCase()) return false;
            {{script}}
        })()
        """;

    private static void ObserveCompleted(ref Task<string>? task)
    {
        if (task?.IsCompleted != true) return;
        if (task.IsFaulted) _ = task.Exception;
        task = null;
    }

    private Task<string> ExecuteRenderAsync(CoreWebView2 core, string script)
    {
        activeRenderScript = core.ExecuteScriptAsync(script);
        return activeRenderScript.WaitAsync(TimeSpan.FromSeconds(2));
    }

    private static string? AlbumCoverData(byte[]? data)
    {
        if (data == null) return null;
        try
        {
            using var raw = new MemoryStream(data);
            using var image = Image.FromStream(raw, useEmbeddedColorManagement: false, validateImageData: true);
            using var thumb = new Bitmap(image, new Size(112, 112));
            using var encoded = new MemoryStream();
            thumb.Save(encoded, System.Drawing.Imaging.ImageFormat.Png);
            return "data:image/png;base64," + Convert.ToBase64String(encoded.ToArray());
        }
        catch (Exception ex) when (ex is ArgumentException or ExternalException or OutOfMemoryException) { return null; }
    }

    private async void ClearDrawing()
    {
        var core = current;
        current = null;
        installed = false;
        coverSource = null;
        coverTrackIdentity = null;
        cover = null;
        if (core == null) return;
        try
        {
            // Clear at most once per retained renderer. No timer retries or queue.
            ObserveCompleted(ref activeClearScript);
            if (activeClearScript != null) return;
            activeClearScript = core.ExecuteScriptAsync(Guard("window.opsisMusicPainter?.remove(); return true;"));
            await activeClearScript.WaitAsync(TimeSpan.FromSeconds(2));
        }
        catch (Exception ex) when (ex is InvalidOperationException or COMException or TimeoutException)
        {
            System.Diagnostics.Debug.WriteLine(ex);
        }
    }

    public void Clear()
    {
        revision++;
        ClearDrawing();
        host.SetMusicSceneRequested(false);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Clear();
    }
}
