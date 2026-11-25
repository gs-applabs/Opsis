using System.Buffers.Binary;
using NAudio.Wave;
using NAudio.CoreAudioApi;

namespace MotionDesk.Services;

/// <summary>
/// ΕΝΑ κοινό WASAPI loopback capture για όλη την εφαρμογή (ref-counted). Πριν, κάθε
/// AudioSpectrumService (κάθε Audio widget + η σελίδα Audio Enhancement) άνοιγε ΔΙΚΟ ΤΟΥ capture
/// στη συσκευή εξόδου — πολλαπλές ροές, πολλαπλά buffers και callbacks για τα ίδια ακριβώς δείγματα.
/// Επίσης ακολουθεί την αλλαγή προεπιλεγμένης συσκευής εξόδου (π.χ. ακουστικά/Bluetooth): το
/// RecordingStopped επανεκκινεί το capture στη νέα συσκευή αντί να μένει νεκρό.
/// </summary>
internal static class SharedLoopbackCapture
{
    public const int RingLength = 1024;
    private static readonly object Sync = new();
    private static readonly float[] Ring = new float[RingLength];
    private static int _ringPos;
    private static long _lastDataTick;
    private static long _lastSignalTick;
    private static readonly Guid FloatFormat = new("00000003-0000-0010-8000-00AA00389B71");
    private static int _refCount;
    private static WasapiCapture? _capture;
    private static string? _inputDeviceId; // null = default OUTPUT loopback (backwards compatible)

    internal static string? InputDeviceId { get { lock (Sync) return _inputDeviceId; } }

    internal static IReadOnlyList<(string Id, string Name)> AvailableInputs()
    {
        try
        {
            using var devices = new MMDeviceEnumerator();
            return devices.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
                .Select(d => (d.ID, d.FriendlyName)).ToArray();
        }
        catch { return Array.Empty<(string, string)>(); }
    }

    internal static void SelectInput(string? deviceId)
    {
        WasapiCapture? old;
        lock (Sync)
        {
            if (string.Equals(_inputDeviceId, deviceId, StringComparison.Ordinal)) return;
            _inputDeviceId = deviceId;
            old = DetachLocked();
            Array.Clear(Ring);
            _ringPos = 0;
        }
        CloseCapture(old);
        lock (Sync) { if (_refCount > 0 && _capture == null) StartLocked(); }
    }
    private static int _generation;
    /// <summary>Changes whenever a working audio capture replaces an old one.</summary>
    internal static int Generation { get { lock (Sync) return _generation; } }
    private static System.Threading.Timer? _restartTimer;

    public static bool HasRecentSignal { get { lock (Sync) return _lastSignalTick != 0 && Environment.TickCount64 - _lastSignalTick < 1300; } }

    public static bool IsAvailable { get { lock (Sync) return _capture != null; } }

    public static void Acquire()
    {
        lock (Sync)
        {
            if (_refCount++ == 0) StartLocked();
        }
    }

    public static void Release()
    {
        WasapiCapture? capture;
        lock (Sync)
        {
            if (_refCount == 0 || --_refCount != 0) return;
            capture = DetachLocked();
        }
        CloseCapture(capture);
    }

    // Αντιγράφει τα τελευταία RingLength δείγματα (παλαιότερο → νεότερο) στο dest. Σε σιωπή (καθόλου
    // δεδομένα >250ms — το loopback δεν στέλνει τίποτα όταν δεν παίζει ήχος) γεμίζει μηδενικά.
    public static bool CopyLatest(float[] dest)
    {
        lock (Sync)
        {
            if (_lastDataTick == 0 || Environment.TickCount64 - _lastDataTick > 250)
            {
                Array.Clear(dest);
                return false;
            }
            int tail = RingLength - _ringPos;
            Array.Copy(Ring, _ringPos, dest, 0, tail);
            Array.Copy(Ring, 0, dest, tail, _ringPos);
            return true;
        }
    }

    private static void StartLocked()
    {
        WasapiCapture? capture = null;
        try
        {
            if (string.IsNullOrEmpty(_inputDeviceId)) capture = new WasapiLoopbackCapture();
            else
            {
                using var enumerator = new MMDeviceEnumerator();
                // Deliberate microphone/line-in access only after explicit user selection.
                var device = enumerator.GetDevice(_inputDeviceId);
                if (device.State != DeviceState.Active || device.DataFlow != DataFlow.Capture)
                    throw new InvalidOperationException("Audio input device is no longer available.");
                capture = new WasapiCapture(device);
            }
            capture.DataAvailable += OnData;
            capture.RecordingStopped += OnStopped;
            capture.StartRecording();
            _capture = capture;
            unchecked { _generation++; }
        }
        catch
        {
            if (capture != null)
            {
                capture.DataAvailable -= OnData;
                capture.RecordingStopped -= OnStopped;
                capture.Dispose();
            }
            _capture = null;
            ScheduleRestartLocked();
        }
    }

    private static WasapiCapture? DetachLocked()
    {
        _restartTimer?.Dispose(); _restartTimer = null;
        _lastSignalTick = _lastDataTick = 0;
        var c = _capture; _capture = null;
        if (c == null) return null;
        c.DataAvailable -= OnData;
        c.RecordingStopped -= OnStopped;
        return c;
    }

    private static void CloseCapture(WasapiCapture? c)
    {
        if (c == null) return;
        try { c.StopRecording(); } catch { }
        c.Dispose();
    }

    // Η συσκευή άλλαξε/αφαιρέθηκε (ή σφάλμα): ξαναδοκιμάζουμε σε λίγο στη νέα προεπιλεγμένη.
    private static void OnStopped(object? sender, StoppedEventArgs e)
    {
        WasapiCapture? stopped;
        lock (Sync)
        {
            if (_refCount == 0 || !ReferenceEquals(sender, _capture)) return;
            stopped = DetachLocked();
            ScheduleRestartLocked();
        }
        stopped?.Dispose();
    }

    private static void ScheduleRestartLocked()
    {
        _restartTimer?.Dispose();
        _restartTimer = new System.Threading.Timer(_ =>
        {
            lock (Sync)
            {
                if (_refCount == 0 || _capture != null) return;
                StartLocked();
            }
        }, null, 2000, System.Threading.Timeout.Infinite);
    }

    private static void OnData(object? sender, WaveInEventArgs e)
    {
        var capture = sender as WasapiCapture;
        if (capture == null) return;
        var format = capture.WaveFormat;
        int bytesPerSample = format.BitsPerSample / 8;
        int channels = format.Channels;
        int frameSize = format.BlockAlign;
        bool floating = format.Encoding == WaveFormatEncoding.IeeeFloat ||
            format is WaveFormatExtensible ext && ext.SubFormat == FloatFormat;
        if (channels <= 0 || frameSize < bytesPerSample * channels || bytesPerSample is < 2 or > 4) return;
        int frames = e.BytesRecorded / frameSize;

        lock (Sync)
        {
            if (!ReferenceEquals(capture, _capture)) return;
            _lastDataTick = Environment.TickCount64;
            double energy = 0;
            for (int i = 0; i < frames; i++)
            {
                float mono = 0;
                for (int c = 0; c < channels; c++)
                {
                    var data = e.Buffer.AsSpan(i * frameSize + c * bytesPerSample);
                    float sample = bytesPerSample switch
                    {
                        2 => BinaryPrimitives.ReadInt16LittleEndian(data) / 32768f,
                        3 => ((data[0] | data[1] << 8 | data[2] << 16) << 8) / 2147483648f,
                        _ when floating => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(data)),
                        _ => BinaryPrimitives.ReadInt32LittleEndian(data) / 2147483648f
                    };
                    if (!float.IsFinite(sample)) continue;
                    mono += sample;
                    energy += sample * sample;
                }
                Ring[_ringPos] = mono / channels;
                _ringPos = (_ringPos + 1) % RingLength;
            }
            if (frames > 0 && energy / (frames * channels) > 0.0000025) _lastSignalTick = _lastDataTick;
        }
    }
}
