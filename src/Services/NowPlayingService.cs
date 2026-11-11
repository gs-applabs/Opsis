using System.Drawing;
using Windows.Media.Control;

namespace MotionDesk.Services
{
    /// <summary>What the Windows media session (GSMTC) says is playing, from ANY player. Clean-room: only the public WinRT API is used.</summary>
    public sealed record NowPlaying(string Title, string Artist, string Album, bool Playing, TimeSpan? Duration, string SourceApp);

    /// <summary>Where the music overlay sits on a work area (pure, tested). Positions are the nine anchor points of the screen.</summary>
    public enum OverlayAnchor { TopLeft, TopCenter, TopRight, MiddleLeft, Center, MiddleRight, BottomLeft, BottomCenter, BottomRight }

    public static class OverlayPlacement
    {
        public static Point Compute(Rectangle workArea, Size size, OverlayAnchor anchor, int margin)
        {
            int x = anchor switch
            {
                OverlayAnchor.TopLeft or OverlayAnchor.MiddleLeft or OverlayAnchor.BottomLeft => workArea.Left + margin,
                OverlayAnchor.TopRight or OverlayAnchor.MiddleRight or OverlayAnchor.BottomRight => workArea.Right - size.Width - margin,
                _ => workArea.Left + (workArea.Width - size.Width) / 2
            };
            int y = anchor switch
            {
                OverlayAnchor.TopLeft or OverlayAnchor.TopCenter or OverlayAnchor.TopRight => workArea.Top + margin,
                OverlayAnchor.BottomLeft or OverlayAnchor.BottomCenter or OverlayAnchor.BottomRight => workArea.Bottom - size.Height - margin,
                _ => workArea.Top + (workArea.Height - size.Height) / 2
            };
            return new Point(Math.Max(workArea.Left, x), Math.Max(workArea.Top, y));
        }
    }

    /// <summary>
    /// Event driven (no polling): follows the current GSMTC session and raises <see cref="Changed"/> when the track or the playing state changes. Album art is
    /// decoded to a small bitmap (max 160 px) and replaced - never accumulated. Started only while the overlay is enabled.
    /// </summary>
    public sealed class NowPlayingService : IDisposable
    {
        private GlobalSystemMediaTransportControlsSessionManager? _mgr;
        private GlobalSystemMediaTransportControlsSession? _session;
        private readonly System.Threading.SynchronizationContext? _ui = System.Threading.SynchronizationContext.Current;
        private int _generation;
        private bool _disposed;

        public NowPlaying? Current { get; private set; }
        public Bitmap? Art { get; private set; }
        public event Action? Changed;

        /// <summary>Test only: pretend a player reports this track.</summary>
        internal void SetForTest(NowPlaying? now, Bitmap? art) { var old = Art; Art = art; Current = now; Changed?.Invoke(); old?.Dispose(); }

        public async Task StartAsync()
        {
            try
            {
                _mgr = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                _mgr.CurrentSessionChanged += (_, _) => Attach();
                Attach();
            }
            catch (Exception ex) { RotatingLogger.Write("NowPlaying: GSMTC unavailable: " + ex.Message); }
        }

        private void Attach()
        {
            if (_disposed || _mgr == null) return;
            if (_session != null) { _session.MediaPropertiesChanged -= OnChanged; _session.PlaybackInfoChanged -= OnChanged; }
            _session = _mgr.GetCurrentSession();
            if (_session != null) { _session.MediaPropertiesChanged += OnChanged; _session.PlaybackInfoChanged += OnChanged; }
            _ = RefreshAsync();
        }

        private void OnChanged(GlobalSystemMediaTransportControlsSession s, object a) => _ = RefreshAsync();

        private async Task RefreshAsync()
        {
            int gen = System.Threading.Interlocked.Increment(ref _generation);
            NowPlaying? now = null; Bitmap? art = null;
            try
            {
                var s = _session;
                if (s != null)
                {
                    var props = await s.TryGetMediaPropertiesAsync();
                    var info = s.GetPlaybackInfo();
                    var tl = s.GetTimelineProperties();
                    now = new NowPlaying(props.Title ?? "", props.Artist ?? "", props.AlbumTitle ?? "",
                        info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                        tl.EndTime > TimeSpan.Zero ? tl.EndTime - tl.StartTime : null, s.SourceAppUserModelId ?? "");
                    if (props.Thumbnail != null)
                    {
                        try
                        {
                            using var rs = await props.Thumbnail.OpenReadAsync();
                            using var stream = System.IO.WindowsRuntimeStreamExtensions.AsStreamForRead(rs);
                            using var raw = new Bitmap(stream);
                            int side = Math.Min(160, Math.Max(raw.Width, raw.Height));
                            art = new Bitmap(side, side);
                            using var g = Graphics.FromImage(art); g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                            g.DrawImage(raw, 0, 0, side, side);
                        }
                        catch (Exception) { art?.Dispose(); art = null; }
                    }
                }
            }
            catch (Exception) { now = null; art?.Dispose(); art = null; }
            void Apply()
            {
                if (_disposed || gen != _generation) { art?.Dispose(); return; }       // a newer refresh superseded this one
                var old = Art; Art = art; Current = now; Changed?.Invoke(); old?.Dispose();
            }
            if (_ui != null) _ui.Post(_ => Apply(), null); else Apply();
        }

        public void Dispose() { _disposed = true; if (_session != null) { _session.MediaPropertiesChanged -= OnChanged; _session.PlaybackInfoChanged -= OnChanged; } Art?.Dispose(); Art = null; }
    }
}
