using Azure.Core;
using Hark.Core.Audio;
using Hark.Core.Capture;
using Hark.Core.Output;
using Hark.Core.Transcription;

namespace Hark.Core;

/// <summary>
/// The reusable HARK pipeline: the Hear movement — loopback capture → PCM conversion → Azure Speech —
/// fanned to the Keep movement (sinks), behind a simple start/stop lifecycle.
/// <para>
/// Both the CLI and the desktop app drive this same session, so the capture/convert/transcribe
/// orchestration lives in one place. Transcription results are re-surfaced as <see cref="Interim"/>
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

    /// <summary>Whether to retain the converted PCM for an offline refinement pass.</summary>
    private readonly bool _captureAudio;

    /// <summary>
    /// Whether the local microphone is currently mixed into the transcribed stream. Seeded from the
    /// constructor and toggleable live via <see cref="SetMicEnabled"/>.
    /// </summary>
    private bool _micEnabled;

    /// <summary>Cap on buffered audio (~20 min at 16 kHz mono 16-bit) to bound memory.</summary>
    private const long MaxBufferedAudioBytes = 16_000L * 2 * 60 * 20;

    /// <summary>Buffered converted PCM for the current session, or <see langword="null"/> when not capturing.</summary>
    private MemoryStream? _audioBuffer;

    /// <summary>The active recognizer, or <see langword="null"/> when not running.</summary>
    private ISpeechTranscriber? _transcriber;

    /// <summary>The active WASAPI loopback capture, or <see langword="null"/> when not running.</summary>
    private LoopbackCaptureService? _capture;

    /// <summary>Converts captured loopback audio to 16 kHz mono float/PCM, or <see langword="null"/> when not running.</summary>
    private PcmConverter? _converter;

    /// <summary>The active microphone capture, or <see langword="null"/> when mic mixing is off or unavailable.</summary>
    private MicCaptureService? _micCapture;

    /// <summary>Converts captured mic audio to 16 kHz mono float, or <see langword="null"/> when mic mixing is off.</summary>
    private PcmConverter? _micConverter;

    /// <summary>Guards <see cref="_loopbackSamples"/> against the concurrent mic/loopback capture threads.</summary>
    private readonly object _micLock = new();

    /// <summary>
    /// 16 kHz mono float loopback (far-side/system) samples awaiting mixing, drained by the mic
    /// callback. When the mic is active it clocks the mixed stream — a capture endpoint delivers
    /// continuous audio, whereas WASAPI loopback goes silent (no callbacks) when nothing is playing —
    /// so this queue absorbs the timing jitter between the two capture threads.
    /// </summary>
    private readonly Queue<float> _loopbackSamples = new();

    /// <summary>Cap on queued loopback samples (~1s) so the far side drifting ahead of the mic can't grow unbounded.</summary>
    private const int LoopbackQueueCap = PcmConverter.TargetSampleRate;

    /// <summary>Whether a capture/recognition session is active.</summary>
    private bool _running;

    /// <summary>Whether <see cref="DisposeAsync"/> has already run, guarding against duplicate cleanup.</summary>
    private bool _disposed;

    /// <summary>Tick count of the last <see cref="AudioLevel"/> notification, used to throttle to ~20 Hz.</summary>
    private long _lastLevelTick;

    /// <summary>Windowed sum-of-squares accumulators for the SYSTEM (loopback) RMS + its bass/treble split.</summary>
    private double _sysSumSq, _sysBassSumSq, _sysTrebleSumSq;

    /// <summary>Windowed sum-of-squares accumulators for the MICROPHONE RMS + its bass/treble split.</summary>
    private double _micSumSq, _micBassSumSq, _micTrebleSumSq;

    /// <summary>Per-source one-pole low-pass state (carried across chunks) that splits bass from treble.</summary>
    private double _sysLowpass, _micLowpass;

    /// <summary>Sample counts accumulated since the last report, per source.</summary>
    private long _sysCount, _micCount;

    #endregion

    #region Properties

    /// <summary>True while a capture/recognition session is active.</summary>
    public bool IsRunning => _running;

    /// <summary>Whether the local microphone is currently being mixed into the transcribed stream.</summary>
    public bool MicEnabled => _micEnabled;

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

    /// <summary>
    /// Raised (~20 Hz while running) with the captured audio split into a few perceptual bands
    /// (overall / bass / treble) so independent visual parameters can react to different facets of
    /// the sound at once — e.g. the Oracle's eye pupil dilating on bass while its highlight shimmers on
    /// treble. Complements <see cref="AudioLevel"/> (both fire from the same window).
    /// </summary>
    public event Action<AudioFeatures>? AudioFeatures;

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
    /// <param name="captureAudio">When true, retains the converted PCM for an offline refinement pass.</param>
    /// <param name="mixMicrophone">
    /// When true, also captures the local microphone and mixes it into the transcribed stream, so the
    /// user's own voice is heard alongside system/far-side playback (essential when wearing a headset).
    /// Silently falls back to loopback-only if no capture device is present.
    /// </param>
    public HarkSession(
        string region,
        string resourceId,
        string? language = null,
        TokenCredential? credential = null,
        ITranscriptSink? sink = null,
        bool diarize = false,
        bool captureAudio = false,
        bool mixMicrophone = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(region);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);

        _region = region;
        _resourceId = resourceId;
        _language = language;
        _credential = credential;
        _sink = sink;
        _diarize = diarize;
        _captureAudio = captureAudio;
        _micEnabled = mixMicrophone;
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

        // Hear (self) — optionally add the local microphone, mixed into the same stream. Loopback
        // remains the clock; mic buffers are queued and mixed in on each loopback callback.
        if (_micEnabled)
            TryStartMic();

        // Retain the converted audio for an optional offline refinement pass after stopping.
        _audioBuffer = _captureAudio ? new MemoryStream() : null;

        _running = true;
    }

    /// <summary>
    /// Turns microphone mixing on or off while running (and remembers the choice for the next start).
    /// Enabling opens the default capture device and mixes it into the transcribed stream; disabling
    /// stops and releases it. A no-op if already in the requested state.
    /// </summary>
    /// <param name="enabled">Whether the microphone should be mixed in.</param>
    public void SetMicEnabled(bool enabled)
    {
        _micEnabled = enabled;
        if (!_running) return;

        if (enabled)
        {
            if (_micCapture is null) TryStartMic();
        }
        else
        {
            StopMic();
        }
    }

    /// <summary>
    /// Opens the default microphone and wires it into the mix. Non-fatal on failure (no device, or it
    /// won't open): the session carries on with system audio only and reports the reason via
    /// <see cref="Error"/>.
    /// </summary>
    private void TryStartMic()
    {
        try
        {
            _micCapture = new MicCaptureService();
            _micCapture.Start();
            var micFormat = _micCapture.WaveFormat
                ?? throw new InvalidOperationException("Microphone did not expose a wave format after starting.");
            _micConverter = new PcmConverter(micFormat);
            _micCapture.DataAvailable += OnMicData;
        }
        catch (Exception ex)
        {
            // No mic (or it failed to open) is non-fatal: carry on with system audio only.
            Error?.Invoke($"Microphone unavailable; continuing with system audio only. {ex.Message}");
            _micCapture?.Dispose();
            _micCapture = null;
            _micConverter = null;
        }
    }

    /// <summary>Stops and releases the microphone capture and drains any queued loopback samples.</summary>
    private void StopMic()
    {
        if (_micCapture is not null)
        {
            _micCapture.DataAvailable -= OnMicData;
            _micCapture.Stop();
            _micCapture.Dispose();
            _micCapture = null;
        }
        _micConverter = null;
        lock (_micLock) _loopbackSamples.Clear();
    }

    /// <summary>Stops capture and recognition, flushing pending results. Safe to call when not running.</summary>
    /// <param name="cancellationToken">Token used to cancel stopping the recognizer.</param>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!_running) return;
        _running = false;

        StopMic();

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

    /// <summary>
    /// Handles one loopback (far-side/system) buffer. When the mic is active it clocks the mixed
    /// stream, so loopback is merely queued here for the mic callback to mix in; otherwise loopback
    /// is the clock and is emitted directly.
    /// </summary>
    /// <param name="buffer">The raw loopback capture buffer.</param>
    /// <param name="bytes">The number of valid bytes in <paramref name="buffer"/>.</param>
    private void OnDataAvailable(byte[] buffer, int bytes)
    {
        var samples = _converter?.ConvertToFloat(buffer, bytes);
        if (samples is not { Length: > 0 }) return;

        // Measure the SYSTEM (loopback) energy from these pure loopback samples, on its own reactivity
        // path (kept separate from the mic so each drives the eye with its own sensitivity).
        bool report = AudioLevel is not null || AudioFeatures is not null;
        if (report)
            Accumulate(samples, ref _sysLowpass, ref _sysSumSq, ref _sysBassSumSq, ref _sysTrebleSumSq, ref _sysCount);

        // With the mic active, the mic clocks the stream (a capture endpoint is continuous, whereas
        // loopback stops firing when nothing is playing). Queue loopback for the mic callback to mix.
        if (_micCapture is not null)
        {
            lock (_micLock)
            {
                foreach (var s in samples)
                    _loopbackSamples.Enqueue(s);
                while (_loopbackSamples.Count > LoopbackQueueCap)
                    _loopbackSamples.Dequeue();
            }
            if (report) MaybeReportFeatures();
            return;
        }

        Emit(samples);
        if (report) MaybeReportFeatures();
    }

    /// <summary>
    /// Handles one microphone buffer, mixing in any queued loopback (far-side) audio and emitting the
    /// result. The mic clocks the combined stream so the user's own voice is never dropped, even when
    /// the system is silent (which suppresses loopback callbacks entirely).
    /// </summary>
    /// <param name="buffer">The raw mic capture buffer.</param>
    /// <param name="bytes">The number of valid bytes in <paramref name="buffer"/>.</param>
    private void OnMicData(byte[] buffer, int bytes)
    {
        var samples = _micConverter?.ConvertToFloat(buffer, bytes);
        if (samples is not { Length: > 0 }) return;

        // Measure the MIC energy from the PRE-mix samples, on its own (hotter) reactivity path.
        bool report = AudioLevel is not null || AudioFeatures is not null;
        if (report)
            Accumulate(samples, ref _micLowpass, ref _micSumSq, ref _micBassSumSq, ref _micTrebleSumSq, ref _micCount);

        // Mix in queued loopback in the float domain (a single clamp at quantization avoids
        // double-clipping); when nothing is playing the queue is empty and this is a no-op.
        lock (_micLock)
        {
            int n = Math.Min(samples.Length, _loopbackSamples.Count);
            for (int i = 0; i < n; i++)
                samples[i] += _loopbackSamples.Dequeue();
        }

        Emit(samples);
        if (report) MaybeReportFeatures();
    }

    /// <summary>
    /// Quantizes mixed float samples to 16-bit PCM, writes them to the transcriber, drives the level
    /// meter, and buffers them for the optional offline refinement pass.
    /// </summary>
    /// <param name="samples">The mixed 16 kHz mono float samples to emit.</param>
    private void Emit(float[] samples)
    {
        var pcm = PcmConverter.QuantizeToPcm16(samples);
        if (pcm.Length == 0) return;

        _transcriber?.Write(pcm, pcm.Length);

        // Buffer for the offline refinement pass, capped to bound memory on long sessions.
        if (_audioBuffer is not null && _audioBuffer.Length < MaxBufferedAudioBytes)
            _audioBuffer.Write(pcm, 0, pcm.Length);
    }

    /// <summary>
    /// Returns the converted PCM buffered for this session (16 kHz mono 16-bit), or
    /// <see langword="null"/> when audio capture wasn't enabled or nothing was captured. Valid after
    /// <see cref="StopAsync"/> until the next <see cref="StartAsync"/>.
    /// </summary>
    /// <returns>The buffered session audio, or <see langword="null"/>.</returns>
    public byte[]? GetBufferedAudioPcm() => _audioBuffer is { Length: > 0 } ? _audioBuffer.ToArray() : null;

    /// <summary>
    /// Accumulates one source's float samples into its windowed RMS + bass/treble sum-of-squares, using a
    /// per-source one-pole low-pass (~330 Hz) to split the body from the sibilance. Called separately for
    /// the system (loopback) and the mic so each keeps its own energy window and filter continuity.
    /// </summary>
    private static void Accumulate(float[] samples, ref double lowpass,
        ref double sumSq, ref double bassSq, ref double trebleSq, ref long count)
    {
        const double lpAlpha = 0.12;   // one-pole ~330 Hz cut at 16 kHz (alpha = 1 - e^(-2π·fc/fs))
        foreach (float s in samples)
        {
            lowpass += lpAlpha * (s - lowpass);
            double highpass = s - lowpass;
            sumSq += s * s;
            bassSq += lowpass * lowpass;
            trebleSq += highpass * highpass;
            count++;
        }
    }

    /// <summary>
    /// Raises <see cref="AudioLevel"/> and <see cref="AudioFeatures"/> at ~20 Hz with the windowed RMS of
    /// each source (system + mic) as SEPARATE bands, so a consumer can react to the mic and the system
    /// audio with independent sensitivity. Resets both windows on each report.
    /// </summary>
    private void MaybeReportFeatures()
    {
        var levelHandler = AudioLevel;
        var featuresHandler = AudioFeatures;
        if (levelHandler is null && featuresHandler is null) return;

        long now = Environment.TickCount64;
        if (now - _lastLevelTick < 50) return;
        _lastLevelTick = now;

        static double Rms(double sumSq, long n) => n > 0 ? Math.Sqrt(sumSq / n) : 0.0;
        double sysL = Rms(_sysSumSq, _sysCount), sysB = Rms(_sysBassSumSq, _sysCount), sysT = Rms(_sysTrebleSumSq, _sysCount);
        double micL = Rms(_micSumSq, _micCount), micB = Rms(_micBassSumSq, _micCount), micT = Rms(_micTrebleSumSq, _micCount);
        _sysSumSq = _sysBassSumSq = _sysTrebleSumSq = 0; _sysCount = 0;
        _micSumSq = _micBassSumSq = _micTrebleSumSq = 0; _micCount = 0;

        levelHandler?.Invoke(Math.Max(sysL, micL));
        featuresHandler?.Invoke(new AudioFeatures(sysL, sysB, sysT, micL, micB, micT));
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

        _audioBuffer?.Dispose();
        _audioBuffer = null;
    }

    #endregion
}
