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
    #region Fields

    /// <summary>The Speech resource region, e.g. <c>eastus2</c>.</summary>
    private readonly string _region;

    /// <summary>The full ARM resource ID of the Speech account.</summary>
    private readonly string _resourceId;

    /// <summary>Optional BCP-47 language tag override.</summary>
    private readonly string? _language;

    /// <summary>Optional credential override forwarded to the transcriber.</summary>
    private readonly TokenCredential? _credential;

    /// <summary>Optional sink that finalized/interim segments are also written to.</summary>
    private readonly ITranscriptSink? _sink;

    /// <summary>Whether to use the diarizing transcriber, which attributes segments to speakers.</summary>
    private readonly bool _diarize;

    /// <summary>The active recognizer, or <see langword="null"/> when not running.</summary>
    private ISpeechTranscriber? _transcriber;

    /// <summary>The active WASAPI loopback capture, or <see langword="null"/> when not running.</summary>
    private LoopbackCaptureService? _capture;

    /// <summary>Converts captured audio to 16 kHz mono 16-bit PCM, or <see langword="null"/> when not running.</summary>
    private PcmConverter? _converter;

    /// <summary>Whether a capture/recognition session is active.</summary>
    private bool _running;

    /// <summary>Whether <see cref="DisposeAsync"/> has already run, guarding against duplicate cleanup.</summary>
    private bool _disposed;

    /// <summary>Tick count of the last <see cref="AudioLevel"/> notification, used to throttle to ~20 Hz.</summary>
    private long _lastLevelTick;

    #endregion

    #region Properties

    /// <summary>True while a capture/recognition session is active.</summary>
    public bool IsRunning => _running;

    #endregion

    #region Events

    /// <summary>Raised with provisional, still-changing hypotheses (low latency, may be revised).</summary>
    public event Action<TranscriptSegment>? Interim;

    /// <summary>Raised with finalized, stable segments (suitable for persisting/committing).</summary>
    public event Action<TranscriptSegment>? Final;

    /// <summary>Raised when the recognizer reports a non-fatal error or cancellation reason.</summary>
    public event Action<string>? Error;

    /// <summary>
    /// Raised (~20 Hz while running) with the normalized RMS audio level (0..1) of the captured
    /// stream — suitable for driving a level meter or a sound-reactive indicator.
    /// </summary>
    public event Action<double>? AudioLevel;

    #endregion

    #region Constructor(s)

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

    #endregion

    #region Methods

    /// <summary>
    /// Starts the recognizer and begins capturing system playback audio, feeding converted PCM into
    /// the recognizer. Safe to call again after a matching <see cref="StopAsync"/>.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel starting the recognizer or capture.</param>
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
    /// <param name="cancellationToken">Token used to cancel stopping the recognizer.</param>
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

    /// <summary>Feeds one converted PCM buffer to the transcriber and reports the audio level.</summary>
    /// <param name="buffer">The converted PCM buffer.</param>
    /// <param name="bytes">The number of valid bytes in <paramref name="buffer"/>.</param>
    private void OnDataAvailable(byte[] buffer, int bytes)
    {
        var pcm = _converter?.Convert(buffer, bytes);
        if (pcm is { Length: > 0 })
        {
            _transcriber?.Write(pcm, pcm.Length);
            ReportAudioLevel(pcm);
        }
    }

    /// <summary>
    /// Raises <see cref="AudioLevel"/> with the normalized RMS (0..1) of the converted PCM,
    /// throttled to ~20 Hz. RMS (loudness) is far more dynamic than peak for system audio, which
    /// tends to sit near full-scale — making a reactive indicator actually move.
    /// </summary>
    /// <param name="pcm">The converted 16-bit PCM buffer to measure.</param>
    private void ReportAudioLevel(byte[] pcm)
    {
        var handler = AudioLevel;
        if (handler is null) return;

        long now = Environment.TickCount64;
        if (now - _lastLevelTick < 50) return;
        _lastLevelTick = now;

        double sumSquares = 0;
        int count = 0;
        for (int i = 0; i + 1 < pcm.Length; i += 2)
        {
            double sample = (short)(pcm[i] | (pcm[i + 1] << 8)) / 32768.0;
            sumSquares += sample * sample;
            count++;
        }

        double rms = count > 0 ? Math.Sqrt(sumSquares / count) : 0.0;
        handler(rms);
    }

    /// <summary>Forwards an interim hypothesis to the sink and re-raises it via <see cref="Interim"/>.</summary>
    /// <param name="segment">The interim transcript segment.</param>
    private void OnInterim(TranscriptSegment segment)
    {
        _sink?.Write(segment);
        Interim?.Invoke(segment);
    }

    /// <summary>Forwards a finalized segment to the sink and re-raises it via <see cref="Final"/>.</summary>
    /// <param name="segment">The finalized transcript segment.</param>
    private void OnFinal(TranscriptSegment segment)
    {
        _sink?.Write(segment);
        Final?.Invoke(segment);
    }

    /// <summary>Re-raises a recognizer error via <see cref="Error"/>.</summary>
    /// <param name="message">The error message reported by the recognizer.</param>
    private void OnError(string message) => Error?.Invoke(message);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        try { await StopAsync().ConfigureAwait(false); }
        catch { /* best-effort teardown */ }
    }

    #endregion
}
