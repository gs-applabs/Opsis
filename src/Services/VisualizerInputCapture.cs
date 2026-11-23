using System.Buffers.Binary;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace MotionDesk.Services;

/// <summary>
/// Dedicated WASAPI microphone/line-in capture for the music overlay ONLY.
/// The shared output loopback remains unchanged when a user selects an input.
/// Uses a bounded 1024-sample ring, and releases all device resources on pause.
/// Microphone permission is acquired only after explicit selection in the UI.
/// </summary>
internal sealed class VisualizerInputCapture : IDisposable
{
    private readonly object gate = new();
    private readonly string deviceId;
    private readonly float[] ring = new float[SharedLoopbackCapture.RingLength];
    private WasapiCapture? capture;
    private System.Threading.Timer? retry;
    private bool active = true, disposed;
    private int pos, generation;
    private long lastDataTick, lastSignalTick;
    private static readonly Guid FloatFormat = new("00000003-0000-0010-8000-00AA00389B71");

    internal VisualizerInputCapture(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId)) throw new ArgumentException("Input device ID is required", nameof(deviceId));
        this.deviceId = deviceId;
        lock (gate) StartLocked();
    }

    internal bool IsAvailable { get { lock (gate) return capture != null; } }
    internal int Generation { get { lock (gate) return generation; } }
    internal bool HasRecentSignal { get { lock (gate) return lastSignalTick != 0 && Environment.TickCount64 - lastSignalTick < 1300; } }

    internal bool CopyLatest(float[] destination)
    {
        if (destination.Length != ring.Length) throw new ArgumentException("Unexpected sample count", nameof(destination));
        lock (gate)
        {
            if (lastDataTick == 0 || Environment.TickCount64 - lastDataTick > 250)
            {
                Array.Clear(destination); return false;
            }
            int tail = ring.Length - pos;
            Array.Copy(ring, pos, destination, 0, tail);
            Array.Copy(ring, 0, destination, tail, pos);
            return true;
        }
    }

    internal void SetActive(bool value)
    {
        WasapiCapture? old = null;
        lock (gate)
        {
            if (disposed || active == value) return;
            active = value;
            if (value) StartLocked();
            else old = DetachLocked();
        }
        Close(old);
    }

    private void StartLocked()
    {
        if (disposed || !active || capture != null) return;
        WasapiCapture? incoming = null;
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var endpoint = enumerator.GetDevice(deviceId);
            if (endpoint.State != DeviceState.Active || endpoint.DataFlow != DataFlow.Capture)
                throw new InvalidOperationException("Selected microphone/line-in is not active");
            incoming = new WasapiCapture(endpoint);
            incoming.DataAvailable += OnData;
            incoming.RecordingStopped += OnStopped;
            incoming.StartRecording();
            capture = incoming;
            unchecked { generation++; }
        }
        catch (Exception)
        {
            if (incoming != null)
            {
                incoming.DataAvailable -= OnData;
                incoming.RecordingStopped -= OnStopped;
                incoming.Dispose();
            }
            ScheduleRetryLocked(); // an unplugged microphone must not switch to another source
        }
    }

    private void ScheduleRetryLocked()
    {
        retry?.Dispose();
        if (!active || disposed) return;
        retry = new System.Threading.Timer(_ =>
        {
            lock (gate) if (!disposed && active && capture == null) StartLocked();
        }, null, 2000, System.Threading.Timeout.Infinite);
    }

    private WasapiCapture? DetachLocked()
    {
        retry?.Dispose(); retry = null;
        lastDataTick = lastSignalTick = 0;
        Array.Clear(ring); pos = 0;
        var old = capture; capture = null;
        if (old != null)
        {
            old.DataAvailable -= OnData;
            old.RecordingStopped -= OnStopped;
        }
        return old;
    }

    private static void Close(WasapiCapture? old)
    {
        if (old == null) return;
        try { old.StopRecording(); } catch (Exception) { }
        old.Dispose();
    }

    private void OnStopped(object? sender, StoppedEventArgs e)
    {
        WasapiCapture? old;
        lock (gate)
        {
            if (disposed || !ReferenceEquals(sender, capture)) return;
            old = DetachLocked();
            ScheduleRetryLocked();
        }
        old?.Dispose(); // callback may execute on the capture thread; don't stop it recursively
    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
        if (sender is not WasapiCapture source) return;
        var format = source.WaveFormat;
        int sampleBytes = format.BitsPerSample / 8;
        int channels = format.Channels;
        int stride = format.BlockAlign;
        bool floating = format.Encoding == WaveFormatEncoding.IeeeFloat ||
            (format is WaveFormatExtensible extensible && extensible.SubFormat == FloatFormat);
        if (channels <= 0 || sampleBytes is < 2 or > 4 || stride < sampleBytes * channels) return;
        int frames = e.BytesRecorded / stride;
        lock (gate)
        {
            if (!ReferenceEquals(source, capture) || !active || disposed) return;
            lastDataTick = Environment.TickCount64;
            double energy = 0;
            for (int i = 0; i < frames; i++)
            {
                float mono = 0;
                for (int ch = 0; ch < channels; ch++)
                {
                    var bytes = e.Buffer.AsSpan(i * stride + ch * sampleBytes);
                    float value = sampleBytes switch
                    {
                        2 => BinaryPrimitives.ReadInt16LittleEndian(bytes) / 32768f,
                        3 => ((bytes[0] | bytes[1] << 8 | bytes[2] << 16) << 8) / 2147483648f,
                        _ when floating => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(bytes)),
                        _ => BinaryPrimitives.ReadInt32LittleEndian(bytes) / 2147483648f
                    };
                    if (!float.IsFinite(value)) continue;
                    mono += value;
                    energy += value * value;
                }
                ring[pos] = mono / channels;
                pos = (pos + 1) % ring.Length;
            }
            if (frames > 0 && energy / (frames * channels) > .0000025) lastSignalTick = lastDataTick;
        }
    }

    public void Dispose()
    {
        WasapiCapture? old;
        lock (gate)
        {
            if (disposed) return;
            active = false; disposed = true;
            old = DetachLocked();
        }
        Close(old);
    }
}
