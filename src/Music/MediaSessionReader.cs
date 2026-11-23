using Windows.Media;
using Windows.Media.Control;
using Windows.Storage.Streams;
using Session = Windows.Media.Control.GlobalSystemMediaTransportControlsSession;
using Playback = Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus;

namespace OpsisMusicOverlay;

internal sealed record TrackInfo(string Source, string Title, string Artist, string Album, byte[]? Cover)
{
    public string Identity => string.Join("\u001f", Source, Title, Artist, Album);
}
internal sealed record TrackTimeline(TimeSpan Position, TimeSpan Duration, DateTimeOffset Updated, double Rate)
{
    public TimeSpan GetPosition(DateTimeOffset now)
    {
        if (Duration <= TimeSpan.Zero) return TimeSpan.Zero;
        var elapsed = Math.Max(0, (now - Updated).TotalSeconds);
        return TimeSpan.FromSeconds(Math.Clamp(Position.TotalSeconds + elapsed * Rate, 0, Duration.TotalSeconds));
    }
}
internal sealed record MediaSnapshot(TrackInfo Track, TrackTimeline? Timeline);

internal sealed class MediaSessionReader : IDisposable
{
    private GlobalSystemMediaTransportControlsSessionManager? manager;
    private Session? watchedSession;
    private TrackInfo? track;
    private int revision;
    private int loadedRevision = -1;
    private DateTimeOffset nextRequest;
    private DateTimeOffset nextMetadataCheck;
    private volatile bool disposed;

    public async Task<MediaSnapshot?> ReadAsync(CancellationToken cancellation)
    {
        if (disposed || cancellation.IsCancellationRequested) return null;
        try
        {
            if (manager == null)
            {
                if (DateTimeOffset.UtcNow < nextRequest) return null;
                nextRequest = DateTimeOffset.UtcNow.AddSeconds(3);
                manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask(cancellation);
                if (disposed) return null;
            }
            var current = manager.GetCurrentSession();
            Session? selected = IsPlaying(current) ? current : manager.GetSessions().FirstOrDefault(IsPlaying);
            if (selected == null)
            {
                Watch(null);
                return null;
            }

            Watch(selected);
            var session = watchedSession!;
            int version = Volatile.Read(ref revision);
            var now = DateTimeOffset.UtcNow;
            if (track == null || version != loadedRevision || now >= nextMetadataCheck)
            {
                var props = await session.TryGetMediaPropertiesAsync().AsTask(cancellation);
                if (disposed || !IsPlaying(session)) return null;
                var artist = string.IsNullOrWhiteSpace(props?.Artist) ? props?.AlbumArtist : props.Artist;
                var album = string.IsNullOrWhiteSpace(props?.AlbumTitle) ? props?.Subtitle : props.AlbumTitle;
                var updated = new TrackInfo(session.SourceAppUserModelId, props?.Title ?? "", artist ?? "",
                    album ?? "", null);
                bool changed = track == null || version != loadedRevision || track.Identity != updated.Identity;
                if (changed) updated = updated with { Cover = await ReadCoverAsync(props?.Thumbnail, cancellation) };
                else updated = updated with { Cover = track?.Cover };
                if (disposed || cancellation.IsCancellationRequested || version != Volatile.Read(ref revision) || !IsPlaying(session)) return null;
                track = updated;
                loadedRevision = version;
                nextMetadataCheck = now.AddSeconds(8);
            }

            var position = session.GetTimelineProperties();
            var duration = position.EndTime - position.StartTime;
            var rate = session.GetPlaybackInfo()?.PlaybackRate ?? 1;
            if (!double.IsFinite(rate) || rate < 0 || rate > 8) rate = 1;
            var timeline = duration > TimeSpan.Zero && duration < TimeSpan.FromDays(7)
                ? new TrackTimeline(position.Position - position.StartTime, duration,
                    position.LastUpdatedTime.Year > 2000 ? position.LastUpdatedTime : now, rate) : null;
            return new MediaSnapshot(track!, timeline);
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool IsPlaying(Session? session)
    {
        try
        {
            var info = session?.GetPlaybackInfo();
            return info?.PlaybackStatus == Playback.Playing && info.PlaybackType != MediaPlaybackType.Video;
        }
        catch (System.Runtime.InteropServices.COMException) { return false; }
    }

    private void Watch(Session? session)
    {
        if (Equals(session, watchedSession)) return;
        if (watchedSession != null) watchedSession.MediaPropertiesChanged -= OnMetadataChanged;
        watchedSession = session;
        track = null;
        Interlocked.Increment(ref revision);
        if (session != null) session.MediaPropertiesChanged += OnMetadataChanged;
    }

    private void OnMetadataChanged(Session sender, MediaPropertiesChangedEventArgs args) => Interlocked.Increment(ref revision);

    private static async Task<byte[]?> ReadCoverAsync(IRandomAccessStreamReference? thumbnail, CancellationToken cancellation)
    {
        if (thumbnail == null) return null;
        try
        {
            using var stream = await thumbnail.OpenReadAsync().AsTask(cancellation);
            if (stream.Size is 0 or > 4_000_000) return null;
            using var reader = new DataReader(stream);
            var length = (uint)stream.Size;
            if (await reader.LoadAsync(length).AsTask(cancellation) != length) return null;
            var data = new byte[length];
            reader.ReadBytes(data);
            return data;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or ArgumentException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        disposed = true;
        try { Watch(null); }
        catch (System.Runtime.InteropServices.COMException) { }
        manager = null;
    }
}
