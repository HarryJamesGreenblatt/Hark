using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Hark.Core.Capture;

/// <summary>
/// Hear (self) — captures the local microphone via WASAPI shared-mode capture.
/// Complements <see cref="LoopbackCaptureService"/> (which hears the far side / system playback):
/// with a headset, the far side plays into the headphones and is captured by loopback, while the
/// user's own voice only exists on the mic. Mixing both gives a complete conversation to transcribe.
/// </summary>
public sealed class MicCaptureService : IDisposable
{
    /// <summary>The active WASAPI input capture. Created on <see cref="Start"/>, released on <see cref="Stop"/>/<see cref="Dispose"/>.</summary>
    private WasapiCapture? _capture;

    /// <summary>Indicates whether the instance has been disposed.</summary>
    private bool _disposed;

    /// <summary>
    /// The native format of the captured stream (device-dependent: 16-bit PCM or 32-bit float,
    /// mono or stereo, at the device's sample rate). Populated once <see cref="Start"/> has run.
    /// </summary>
    public WaveFormat? WaveFormat => _capture?.WaveFormat;

    /// <summary>
    /// Raised on the capture thread with each new buffer. Provides the raw byte buffer and the
    /// number of valid bytes in it. The buffer is reused by NAudio, so handlers must consume it synchronously.
    /// </summary>
    public event Action<byte[], int>? DataAvailable;

    /// <summary>
    /// Starts capturing from the default input device. Throws if the machine has no capture endpoint.
    /// <see cref="WaveFormat"/> becomes available afterwards.
    /// </summary>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_capture is not null) return;

        using var enumerator = new MMDeviceEnumerator();

        // Role.Multimedia, NOT Role.Communications: the Communications role marks the app as a
        // phone/meeting client, which triggers system-wide comms-mode processing (narrowband
        // filtering, ducking) on *all* playback — everything else would go tinny.
        var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);

        _capture = new WasapiCapture(device);
        _capture.DataAvailable += OnDataAvailable;
        _capture.RecordingStopped += OnRecordingStopped;
        _capture.StartRecording();
    }

    /// <summary>Stops capturing if a session is active.</summary>
    public void Stop() => _capture?.StopRecording();

    /// <summary>Forwards each captured buffer to subscribers, skipping empty callbacks.</summary>
    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded == 0) return;
        DataAvailable?.Invoke(e.Buffer, e.BytesRecorded);
    }

    /// <summary>Surfaces capture-thread errors to stderr without tearing down the host process.</summary>
    private static void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is not null)
            Console.Error.WriteLine($"[Hear:mic] Capture stopped with error: {e.Exception.Message}");
    }

    /// <summary>Stops recording (if active) and releases the unmanaged capture resources.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _capture?.StopRecording();
        _capture?.Dispose();
        _capture = null;
    }
}
