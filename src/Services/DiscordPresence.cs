using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace MotionDesk.Services;

/// <summary>Optional local Discord IPC. A selected session owns its pipe and cancellation.
/// Exactly one worker may connect, write and read; disabling never waits on Discord.</summary>
public sealed class DiscordPresence : IDisposable
{
    public static DiscordPresence Instance { get; } = new();
    private const int MaxFrameBytes = 1 << 20;
    private const int MaxResponseFrames = 8;
    private System.Windows.Forms.Timer? _timer;
    private Session? _session;
    private int _busy;
    internal bool ActivityDemand => _timer != null;
    private readonly long _startedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    private sealed class Session
    {
        private readonly object _gate = new();
        private readonly CancellationTokenSource _cancel = new();
        private NamedPipeClientStream? _pipe;
        private bool _working, _retired;
        internal readonly string Id;
        internal string LastSent = "";
        internal Session(string id) { Id = id; }
        internal CancellationToken Token => _cancel.Token; // only the active worker reads this
        internal bool Begin()
        {
            lock (_gate)
            {
                if (_retired || _working) return false;
                _working = true; return true;
            }
        }
        internal NamedPipeClientStream? Pipe { get { lock (_gate) return _pipe; } }
        internal bool Attach(NamedPipeClientStream pipe)
        {
            lock (_gate)
            {
                if (_retired) return false;
                _pipe = pipe; return true;
            }
        }
        internal void Disconnect()
        {
            lock (_gate)
            {
                var old = _pipe; _pipe = null; LastSent = "";
                try { old?.Dispose(); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
            }
        }
        internal void Retire()
        {
            lock (_gate)
            {
                if (_retired) return;
                _retired = true;
                // Cancellation and disposal interrupt pending async I/O. No UI-thread
                // protocol write or synchronous wait; closing the IPC session ends ownership.
                try { _cancel.Cancel(); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
                Disconnect();
                if (!_working) _cancel.Dispose();
            }
        }
        internal void End()
        {
            lock (_gate)
            {
                _working = false;
                if (_retired) _cancel.Dispose();
            }
        }
    }

    internal static bool ShouldRun(bool selected, string? id, bool safeMode) =>
        selected && !safeMode && IsValidId(id);
    public static bool IsValidId(string? id) => !string.IsNullOrWhiteSpace(id) &&
        id.Trim().Length is >= 15 and <= 25 && id.Trim().All(c => c is >= '0' and <= '9');
    internal static bool ValidFrameLength(int length) => length > 0 && length <= MaxFrameBytes;

    /// <summary>UI thread: reconcile selected identity. Repeated settings application is a no-op.</summary>
    public void Apply()
    {
        var s = AppSettings.Peek();
        if (!ShouldRun(RuntimeSessionPolicy.Allows(SessionBackgroundService.DiscordPresence, s.DiscordPresenceEnabled,
            SafeModeService.IsSafeMode), s.DiscordApplicationId, SafeModeService.IsSafeMode))
        { Stop(); return; }
        string id = s.DiscordApplicationId.Trim();
        if (_session?.Id == id && _timer != null) return;
        Stop();
        _session = new Session(id);
        _timer = new System.Windows.Forms.Timer { Interval = 20_000 };
        _timer.Tick += (_, _) => Push();
        _timer.Start();
        AppActivity.RequestRefresh();
        Push();
    }

    public void Stop()
    {
        _timer?.Stop(); _timer?.Dispose(); _timer = null;
        var old = _session; _session = null;
        old?.Retire();
        AppActivity.RequestRefresh();
    }

    private void Push()
    {
        var session = _session;
        if (session == null || !RuntimeSessionPolicy.Allows(SessionBackgroundService.DiscordPresence,
            AppSettings.Peek().DiscordPresenceEnabled, SafeModeService.IsSafeMode) ||
            Interlocked.Exchange(ref _busy, 1) != 0) return;
        if (!session.Begin()) { Volatile.Write(ref _busy, 0); return; }
        try
        {
            // Snapshot desktop state on the UI thread, never construct a wallpaper
            // engine or inspect WinForms windows from the worker.
            string details = MotionDesk.Widgets.WallpaperHostEngine.IsEnabledIfCreated
                ? "Wallpaper: " + MotionDesk.Widgets.WallpaperSettings.Load().Mode : "Desktop customization";
            string state = AppActivity.IsFullscreenAppActive ? "Paused while another app is in front" : "Opsis " + AppVersion.Label;
            _ = Task.Run(async () =>
            {
                try { await PushCoreAsync(session, details, state).ConfigureAwait(false); }
                catch (Exception ex) { session.Disconnect(); System.Diagnostics.Debug.WriteLine(ex); }
                finally { session.End(); Volatile.Write(ref _busy, 0); }
            });
        }
        catch (Exception ex)
        {
            session.End(); Volatile.Write(ref _busy, 0);
            System.Diagnostics.Debug.WriteLine(ex);
        }
    }

    private async Task PushCoreAsync(Session session, string details, string state)
    {
        var token = session.Token;
        token.ThrowIfCancellationRequested();
        var pipe = await EnsureConnectedAsync(session, token).ConfigureAwait(false);
        if (pipe == null) return;
        string key = details + "|" + state;
        if (key == session.LastSent) return;
        string nonce = Guid.NewGuid().ToString();
        var payload = new
        {
            cmd = "SET_ACTIVITY",
            args = new { pid = Environment.ProcessId, activity = new { details, state, timestamps = new { start = _startedAt } } },
            nonce
        };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        await SendFrameAsync(pipe, 1, Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload)), timeout.Token).ConfigureAwait(false);
        await ReadResponseAsync(pipe, nonce, timeout.Token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        session.LastSent = key;
    }

    private static async Task<NamedPipeClientStream?> EnsureConnectedAsync(Session session, CancellationToken token)
    {
        if (session.Pipe is { IsConnected: true } existing) return existing;
        session.Disconnect();
        for (int i = 0; i < 10; i++)
        {
            token.ThrowIfCancellationRequested();
            var pipe = new NamedPipeClientStream(".", "discord-ipc-" + i, PipeDirection.InOut, PipeOptions.Asynchronous);
            if (!session.Attach(pipe)) { pipe.Dispose(); return null; }
            try
            {
                await pipe.ConnectAsync(250, token).ConfigureAwait(false);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromSeconds(3));
                await SendFrameAsync(pipe, 0, Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { v = 1, client_id = session.Id })), timeout.Token).ConfigureAwait(false);
                await ReadResponseAsync(pipe, null, timeout.Token).ConfigureAwait(false);
                return pipe;
            }
            catch (Exception ex) when (ex is IOException or TimeoutException or OperationCanceledException or ObjectDisposedException or JsonException)
            {
                session.Disconnect();
                token.ThrowIfCancellationRequested();
            }
        }
        return null;
    }

    private static async Task SendFrameAsync(NamedPipeClientStream pipe, int opcode, byte[] body, CancellationToken token)
    {
        if (!ValidFrameLength(body.Length)) throw new IOException("Invalid Discord frame length.");
        var frame = new byte[8 + body.Length];
        BitConverter.GetBytes(opcode).CopyTo(frame, 0);
        BitConverter.GetBytes(body.Length).CopyTo(frame, 4);
        body.CopyTo(frame, 8);
        await pipe.WriteAsync(frame.AsMemory(), token).ConfigureAwait(false);
        await pipe.FlushAsync(token).ConfigureAwait(false);
    }

    internal static bool MatchesResponse(JsonElement root, string? nonce)
    {
        if (root.ValueKind != JsonValueKind.Object) return false;
        string? evt = root.TryGetProperty("evt", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
        if (evt == "ERROR") throw new IOException("Discord rejected the IPC request.");
        if (nonce == null) return evt == "READY";
        return root.TryGetProperty("nonce", out var n) && n.ValueKind == JsonValueKind.String && n.GetString() == nonce;
    }

    private static async Task ReadResponseAsync(NamedPipeClientStream pipe, string? nonce, CancellationToken token)
    {
        var header = new byte[8];
        for (int i = 0; i < MaxResponseFrames; i++)
        {
            await pipe.ReadExactlyAsync(header.AsMemory(), token).ConfigureAwait(false);
            int opcode = BitConverter.ToInt32(header, 0), length = BitConverter.ToInt32(header, 4);
            if (!ValidFrameLength(length)) throw new IOException("Invalid Discord response length.");
            var body = new byte[length];
            await pipe.ReadExactlyAsync(body.AsMemory(), token).ConfigureAwait(false);
            if (opcode == 2) throw new IOException("Discord closed the IPC session.");
            if (opcode == 3) { await SendFrameAsync(pipe, 4, body, token).ConfigureAwait(false); continue; }
            if (opcode != 1) continue;
            using var json = JsonDocument.Parse(body);
            if (MatchesResponse(json.RootElement, nonce)) return;
        }
        throw new IOException("Discord response frame limit reached.");
    }

    public void Dispose() => Stop();
}
