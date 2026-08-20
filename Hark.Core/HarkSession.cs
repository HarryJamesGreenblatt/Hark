using Azure.Core;
using Hark.Core.Audio;
using Hark.Core.Capture;
using Hark.Core.Output;
using Hark.Core.Transcription;

namespace Hark.Core;

/// <summary>
/// The reusable HARK pipeline: wires Hear (loopback capture) → Adapt (PCM conversion) →
/// Recognize (Azure Speech) → Keep (sinks) behind a simple start/stop lifecycle.
/// <para>
/// Both the CLI and the desktop app drive this same session, so the capture/convert/recognize
/// orchestration lives in one place. Recognition results are re-surfaced as <see cref="Interim"/>
/// and <see cref="Final"/> events (in addition to any sinks) so a host UI can render them directly.
/// </para>
/// </summary>
public sealed class HarkSession : IAsyncDisposable
{
    private readonly string _region;
    private readonly string _resourceId;
    private readonly string? _language;
    private readonly TokenCredential? _credential;
    private readonly ITranscriptSink? _sink;
    private readonly bool _diarize;

    private ISpeechTranscriber? _transcriber;
    private LoopbackCaptureService? _capture;
    private PcmConverter? _converter;
    private bool _running;
    private bool _disposed;

    /// <summary>Raised with provisional, still-changing hypotheses (low latency, may be revised).</summary>
    public event Action<TranscriptSegment>? Interim;

    /// <summary>Raised with finalized, stable segments (suitable for persisting/committing).</summary>
    public event Action<TranscriptSegment>? Final;

    /// <summary>Raised when the recognizer reports a non-fatal error or cancellation reason.</summary>
    public event Action<string>? Error;

    /// <summary>True while a capture/recognition session is active.</summary>
    public bool IsRunning => _running;

    /// <summary>
    /// Creates a session bound to a Speech resource.
    /// </summary>
    /// <param name="region">The resource region, e.g. <c>eastus2</c>.</param>
    /// <param name="resourceId">The full ARM resource ID of the Speech account.</param>
    /// <param name="language">Optional BCP-47 language tag (e.g. <c>en-US</c>).</param>
    /// <param name="credential">
    /// Optional credential override forwarded to <see cref="AzureSpeechTranscriber"/>. Defaults to
    /// that type's keyless behavior when null.
    /// </param>
    /// <param name="sink">Optional sink that finalized/interim segments are also written to.</param>
    /// <param name="diarize">
    /// When true, uses a diarizing engine that attributes each segment to an anonymous speaker
    /// (<see cref="TranscriptSegment.SpeakerId"/>). Diarization pins a single language.
    /// </param>
    public HarkSession(
        string region,
        string resourceId,
        string? language = null,
        TokenCredential? credential = null,
        ITranscriptSink? sink = null,
        bool diarize = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(region);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);

        _region = region;
        _resourceId = resourceId;
        _language = language;
        _credential = credential;
        _sink = sink;
        _diarize = diarize;
    }

    /// <summary>
    /// Starts the recognizer and begins capturing system playback audio, feeding converted PCM into
    /// the recognizer. Safe to call again after a matching <see cref="StopAsync"/>.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_running) return;

        // Recognize — Azure Speech via keyless Entra auth (credential chosen by the host).
        // Diarizing engine attributes speakers; the plain engine supports multi-language LID.
        _transcriber = _diarize
            ? new ConversationDiarizingTranscriber(_region, _resourceId, _language, _credential)
            : new AzureSpeechTranscriber(_region, _resourceId, _language, _credential);
        _transcriber.Interim += OnInterim;
        _transcriber.Final += OnFinal;
        _transcriber.Error += OnError;

        await _transcriber.StartAsync(cancellationToken).ConfigureAwait(false);

        // Hear — WASAPI loopback capture.
        _capture = new LoopbackCaptureService();
        _capture.Start();

        var format = _capture.WaveFormat
            ?? throw new InvalidOperationException("Capture did not expose a wave format after starting.");

        // Adapt — convert each captured buffer to 16 kHz mono 16-bit PCM and feed the recognizer.
        _converter = new PcmConverter(format);
        _capture.DataAvailable += OnDataAvailable;

        _running = true;
    }

    /// <summary>Stops capture and recognition, flushing pending results. Safe to call when not running.</summary>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!_running) return;
        _running = false;

        if (_capture is not null)
        {
            _capture.DataAvailable -= OnDataAvailable;
            _capture.Stop();
            _capture.Dispose();
            _capture = null;
        }

        _converter = null;

        if (_transcriber is not null)
        {
            await _transcriber.StopAsync(cancellationToken).ConfigureAwait(false);
            _transcriber.Interim -= OnInterim;
            _transcriber.Final -= OnFinal;
            _transcriber.Error -= OnError;
            await _transcriber.DisposeAsync().ConfigureAwait(false);
            _transcriber = null;
        }
    }

    private void OnDataAvailable(byte[] buffer, int bytes)
    {
        var pcm = _converter?.Convert(buffer, bytes);
        if (pcm is { Length: > 0 }) _transcriber?.Write(pcm, pcm.Length);
    }

    private void OnInterim(TranscriptSegment segment)
    {
        _sink?.Write(segment);
        Interim?.Invoke(segment);
    }

    private void OnFinal(TranscriptSegment segment)
    {
        _sink?.Write(segment);
        Final?.Invoke(segment);
    }

    private void OnError(string message) => Error?.Invoke(message);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        try { await StopAsync().ConfigureAwait(false); }
        catch { /* best-effort teardown */ }
    }
}
