namespace Hark.Core.Transcription;

/// <summary>
/// Hear — the engine swap point. Accepts 16 kHz mono 16-bit PCM and raises
/// recognition events. Implementations may be cloud-backed (Azure Speech) or local (Whisper),
/// without the rest of the pipeline needing to change.
/// </summary>
public interface ISpeechTranscriber : IAsyncDisposable
{
    /// <summary>Raised with provisional, still-changing hypotheses (low latency, may be revised).</summary>
    event Action<TranscriptSegment>? Interim;

    /// <summary>Raised with finalized, stable segments (suitable for persisting).</summary>
    event Action<TranscriptSegment>? Final;

    /// <summary>Raised when the engine reports a non-fatal error or cancellation reason.</summary>
    event Action<string>? Error;

    /// <summary>Starts a continuous recognition session.</summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Pushes a chunk of 16 kHz mono 16-bit PCM audio into the recognizer.
    /// </summary>
    /// <param name="pcm">The PCM buffer.</param>
    /// <param name="count">The number of valid bytes in <paramref name="pcm"/>.</param>
    void Write(byte[] pcm, int count);

    /// <summary>Stops the continuous recognition session and flushes any pending results.</summary>
    Task StopAsync(CancellationToken cancellationToken = default);
}
