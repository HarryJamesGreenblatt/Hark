using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Hark.Core.Capture;

/// <summary>
/// Hear — captures system playback audio via WASAPI loopback.
/// Taps the default render endpoint, so it transcribes whatever is playing
/// through the speakers/headphones (browser, Spotify, a meeting, etc.) with no microphone involved.
/// </summary>
public sealed class LoopbackCaptureService : IDisposable
{
    /// <summary>The active WASAPI loopback capture. Created on <see cref="Start"/>, released on <see cref="Stop"/>/<see cref="Dispose"/>.</summary>
    private WasapiLoopbackCapture? _capture;

    /// <summary>Indicates whether the instance has been disposed.</summary>
    private bool _disposed;

    /// <summary>
    /// The native format of the captured stream (WASAPI loopback is 32-bit IEEE float PCM).
    /// Populated once <see cref="Start"/> has been called.
    /// </summary>
    public WaveFormat? WaveFormat => _capture?.WaveFormat;

    /// <summary>
    /// Raised on the capture thread with each new buffer. Provides the raw byte buffer and the
    /// number of valid bytes in it. The buffer is reused by NAudio, so handlers must consume it synchronously.
    /// </summary>
    public event Action<byte[], int>? DataAvailable;

    /// <summary>
    /// Starts capturing from the default output device. <see cref="WaveFormat"/> becomes available afterwards.
    /// </summary>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Explicitly resolve the default render endpoint to avoid COM 0x80070490
        // when WasapiLoopbackCapture() tries to discover it internally on some systems.
        using var enumerator = new MMDeviceEnumerator();
        var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

        _capture = new WasapiLoopbackCapture(device);
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
            Console.Error.WriteLine($"[Hear] Capture stopped with error: {e.Exception.Message}");
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
