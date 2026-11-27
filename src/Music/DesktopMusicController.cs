using MotionDesk.Services;
using System.Text.Json;
using Microsoft.Win32;

namespace OpsisMusicOverlay;

internal sealed class DesktopMusicController : IDisposable
{
    private static string SettingsPath => Path.Combine(MotionDesk.Services.AppPaths.AppDataFolder, "music-overlay.json");
    private readonly MediaSessionReader media = new();
    private readonly CancellationTokenSource shutdown = new();
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 42 };
    private readonly WallpaperMusicSurface surface = new();
    private const int BandCount = 64;
    private static readonly float[] Silence = new float[BandCount];
    private MusicOverlaySettings settings = ReadSettings();
    private AudioSpectrumService? spectrum;
    private MediaSnapshot? snapshot;
    private bool paused, reading, disposed, drawing;
    private int playbackGeneration;
    private volatile bool locked;
    private long nextRead, previewUntil, nextDisplayLookup;
    private Screen? display;
    private string? displaySetting;

    public DesktopMusicController()
    {
        // The overlay selects an isolated input; never change the system loopback.
        SystemEvents.SessionSwitch += SessionSwitched;
        timer.Tick += Tick;
        if (settings.Enabled) timer.Start();
    }

    public MusicOverlaySettings Settings => settings;

    public void SetAudioInput(string? deviceId)
    {
        if (settings.InputDeviceId == deviceId) return;
        // Avoid silently switching to a microphone: the UI has a separate, explicit selector.
        if (deviceId != null && !SharedLoopbackCapture.AvailableInputs().Any(x => x.Id == deviceId))
            return;
        StopAudio(); // recreate the overlay capture without interrupting system widgets
        surface.Clear();
        settings = settings with { InputDeviceId = deviceId };
        SaveSettings();
    }

    public void SetTrackPrivacy(bool enabled)
    {
        if (settings.HideTrackMetadata == enabled) return;
        settings = settings with { HideTrackMetadata = enabled };
        SaveSettings();
        // Clear cached album cover on the next frame, not only when the song changes.
        surface.Clear();
    }

    public void SetEnabled(bool enabled)
    {
        if (settings.Enabled == enabled) return;
        settings = settings with { Enabled = enabled };
        SaveSettings();
        if (enabled)
        {
            nextRead = 0;
            timer.Start();
        }
        else
        {
            timer.Stop();
            StopPlayback();
        }
    }

    private async void Tick(object? sender, EventArgs e)
    {
        if (disposed) return;
        var now = Environment.TickCount64;
        bool preview = now < previewUntil;
        if (!settings.Enabled && !preview)
        {
            timer.Stop();
            StopPlayback();
            return;
        }
        if (paused || locked)
        {
            if (drawing || spectrum != null || snapshot != null || previewUntil != 0) StopPlayback();
            return;
        }

        if (settings.Enabled) spectrum ??= new AudioSpectrumService(BandCount, settings.InputDeviceId);
        bool audible = settings.Enabled && (spectrum?.HasRecentSignal ?? false);
        if (preview || audible || snapshot != null)
        {
            drawing = true;
            // Reuse the display selection across visualizer frames, checking for hotplug periodically.
            if (now >= nextDisplayLookup || displaySetting != settings.Display)
            {
                displaySetting = settings.Display;
                display = Screen.AllScreens.FirstOrDefault(s => s.DeviceName == displaySetting) ?? Screen.PrimaryScreen;
                nextDisplayLookup = now + 1000;
            }
            // Per-monitor presentation privacy: no title, artwork OR visualizer drawn there.
            var monitorId = display == null ? null : MonitorTopologyService.ForDevice(display.DeviceName)?.Id;
            var hide = display != null && (MotionDesk.Services.AppSettings.Peek().PresentationMonitorIds?.Any(id =>
                id.Equals(display.DeviceName,StringComparison.OrdinalIgnoreCase) ||
                id.Equals(monitorId,StringComparison.OrdinalIgnoreCase)) ?? false);
            if (hide) { drawing = false; surface.Clear(); StopAudio(); return; }
            var bands = spectrum?.GetBands() ?? Silence;
            var analysis = spectrum?.GetVisualizerFrame();
            _ = surface.RenderAsync(display, settings, snapshot, bands, preview && !audible && snapshot == null,
                analysis?.Frequency, analysis?.Waveform);
        }
        else if (drawing)
        {
            drawing = false;
            surface.Clear();
        }

        if (!settings.Enabled || reading || now < nextRead) return;
        nextRead = now + 750;
        reading = true;
        var generation = playbackGeneration;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            var current = await media.ReadAsync(timeout.Token);
            if (!disposed && settings.Enabled && generation == playbackGeneration && !paused && !locked
                && !timeout.IsCancellationRequested) snapshot = current;
        }
        finally { reading = false; }
    }

    public void SetHostPaused(bool value)
    {
        paused = value;
        if (value) StopPlayback();
    }

    private void SessionSwitched(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.SessionUnlock)
            locked = e.Reason == SessionSwitchReason.SessionLock;
    }

    public void Refresh() => nextRead = 0;
    public void SetPreset(VisualPreset preset)
    {
        if (!Enum.IsDefined(preset) || settings.Preset == preset) return;
        settings = settings with { Preset = preset }; SaveSettings();
    }
    public void SetTypeface(string? name)
    {
        if (settings.Typeface == name) return;
        settings = settings with { Typeface = name }; SaveSettings();
    }
    public void Preview()
    {
        if (disposed || paused || locked) return;
        previewUntil = Environment.TickCount64 + 15000;
        timer.Start();
    }
    public void ResetPosition() => SetPosition(settings.Display, MusicOverlaySettings.DefaultX, MusicOverlaySettings.DefaultY);

    public void SetSize(int width, int height)
    {
        width = Math.Clamp(width, MusicOverlaySettings.MinSize, MusicOverlaySettings.MaxSize);
        height = Math.Clamp(height, MusicOverlaySettings.MinSize, MusicOverlaySettings.MaxSize);
        if (settings.WidthPercent == width && settings.HeightPercent == height) return;
        settings = settings with { WidthPercent = width, HeightPercent = height };
        SaveSettings();
    }

    public void SetColor(int index, Color color)
    {
        var palette = (string[])settings.Palette.Clone();
        if (index < 0 || index >= palette.Length) return;
        var value = $"#{color.R:x2}{color.G:x2}{color.B:x2}";
        if (string.Equals(palette[index], value, StringComparison.OrdinalIgnoreCase)) return;
        palette[index] = value;
        settings = settings.WithPalette(palette);
        SaveSettings();
    }

    public void ResetPalette()
    {
        settings = settings.WithPalette(null);
        SaveSettings();
    }

    public void SetPosition(string? display, double x, double y)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y)) return;
        x = Math.Clamp(x, 0, 1); y = Math.Clamp(y, 0, 1);
        if (settings.Display == display && settings.X == x && settings.Y == y) return;
        settings = settings with { Display = display, X = x, Y = y };
        SaveSettings();
    }

    private static MusicOverlaySettings ReadSettings()
    {
        try
        {
            return (JsonSerializer.Deserialize<MusicOverlaySettings>(File.ReadAllText(SettingsPath)) ?? new()).Normalize();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return new(LayoutVersion: 1); }
    }

    private void SaveSettings()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            var temporary = SettingsPath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings));
            File.Move(temporary, SettingsPath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private void StopPlayback()
    {
        playbackGeneration++;
        previewUntil = 0;
        nextRead = 0;
        snapshot = null;
        drawing = false;
        surface.Clear();
        StopAudio();
    }

    private void StopAudio() { spectrum?.Dispose(); spectrum = null; }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        shutdown.Cancel();
        timer.Dispose();
        SystemEvents.SessionSwitch -= SessionSwitched;
        StopAudio();
        media.Dispose();
        surface.Dispose();
        shutdown.Dispose();
    }
}
