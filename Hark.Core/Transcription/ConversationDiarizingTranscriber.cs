using Azure.Core;
using Azure.Identity;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using Microsoft.CognitiveServices.Speech.Transcription;

namespace Hark.Core.Transcription;

/// <summary>
/// Recognize (Azure, diarized) — continuous conversation transcription that additionally
/// attributes each utterance to an anonymous, session-scoped speaker (<c>Guest-1</c>, <c>Guest-2</c>, …)
/// from a single-channel audio stream. Uses <see cref="ConversationTranscriber"/> under keyless
/// Microsoft Entra ID authentication, mirroring <see cref="AzureSpeechTranscriber"/>.
/// <para>
/// Diarization currently requires a pinned recognition language, so this engine intentionally does
/// not use continuous language identification. When no language is supplied it defaults to
/// <see cref="DefaultLanguage"/>.
/// </para>
/// </summary>
public sealed class ConversationDiarizingTranscriber : ISpeechTranscriber
{
    /// <summary>Recognition language used when the caller doesn't pin one.</summary>
    public const string DefaultLanguage = "en-US";

    /// <summary>Token scope for Azure AI / Cognitive Services data-plane access.</summary>
    private const string CognitiveServicesScope = "https://cognitiveservices.azure.com/.default";

    /// <summary>Refresh the Entra token comfortably before its ~1 hour expiry.</summary>
    private static readonly TimeSpan TokenRefreshInterval = TimeSpan.FromMinutes(8);

    private readonly string _region;
    private readonly string _resourceId;
    private readonly string _language;
    private readonly TokenCredential _credential;

    private PushAudioInputStream? _pushStream;
    private ConversationTranscriber? _transcriber;
    private Timer? _tokenRefreshTimer;
    private bool _disposed;

    /// <inheritdoc />
    public event Action<TranscriptSegment>? Interim;

    /// <inheritdoc />
    public event Action<TranscriptSegment>? Final;

    /// <inheritdoc />
    public event Action<string>? Error;

    /// <summary>
    /// Creates a diarizing transcriber bound to a specific Speech resource.
    /// </summary>
    /// <param name="region">The resource region, e.g. <c>eastus2</c>.</param>
    /// <param name="resourceId">The full ARM resource ID of the Speech account.</param>
    /// <param name="language">Optional BCP-47 language tag; defaults to <see cref="DefaultLanguage"/>.</param>
    /// <param name="credential">Optional credential override; defaults to <see cref="DefaultAzureCredential"/>.</param>
    public ConversationDiarizingTranscriber(string region, string resourceId, string? language = null, TokenCredential? credential = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(region);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);

        _region = region;
        _resourceId = resourceId;
        _language = string.IsNullOrWhiteSpace(language) ? DefaultLanguage : language;
        _credential = credential ?? new DefaultAzureCredential();
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var config = SpeechConfig.FromAuthorizationToken(
            await BuildAuthTokenAsync(cancellationToken).ConfigureAwait(false), _region);

        // Diarization requires a pinned language; continuous LID is not used here.
        config.SpeechRecognitionLanguage = _language;
        config.OutputFormat = OutputFormat.Simple;

        var format = AudioStreamFormat.GetWaveFormatPCM(
            samplesPerSecond: 16_000, bitsPerSample: 16, channels: 1);
        _pushStream = AudioInputStream.CreatePushStream(format);

        var audioConfig = AudioConfig.FromStreamInput(_pushStream);
        _transcriber = new ConversationTranscriber(config, audioConfig);

        _transcriber.Transcribing += OnTranscribing;
        _transcriber.Transcribed += OnTranscribed;
        _transcriber.Canceled += OnCanceled;

        await _transcriber.StartTranscribingAsync().ConfigureAwait(false);

        // Keep the authorization token fresh for long-running sessions.
        _tokenRefreshTimer = new Timer(
            async _ => await RefreshTokenAsync().ConfigureAwait(false),
            state: null, dueTime: TokenRefreshInterval, period: TokenRefreshInterval);
    }

    /// <inheritdoc />
    public void Write(byte[] pcm, int count)
    {
        if (count <= 0 || _pushStream is null) return;

        if (count == pcm.Length)
        {
            _pushStream.Write(pcm);
        }
        else
        {
            var exact = new byte[count];
            Array.Copy(pcm, exact, count);
            _pushStream.Write(exact);
        }
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_tokenRefreshTimer is not null)
        {
            await _tokenRefreshTimer.DisposeAsync().ConfigureAwait(false);
            _tokenRefreshTimer = null;
        }

        _pushStream?.Close();

        if (_transcriber is not null)
            await _transcriber.StopTranscribingAsync().ConfigureAwait(false);
    }

    /// <summary>Builds the Speech SDK authorization token from an Entra access token.</summary>
    private async Task<string> BuildAuthTokenAsync(CancellationToken cancellationToken)
    {
        var token = await _credential
            .GetTokenAsync(new TokenRequestContext(new[] { CognitiveServicesScope }), cancellationToken)
            .ConfigureAwait(false);

        // The Speech SDK expects: aad#{resourceId}#{aadAccessToken}
        return $"aad#{_resourceId}#{token.Token}";
    }

    /// <summary>Refreshes the transcriber's authorization token in place.</summary>
    private async Task RefreshTokenAsync()
    {
        try
        {
            if (_transcriber is null) return;
            _transcriber.AuthorizationToken = await BuildAuthTokenAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Error?.Invoke($"Token refresh failed: {ex.Message}");
        }
    }

    private void OnTranscribing(object? sender, ConversationTranscriptionEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Result.Text)) return;
        Interim?.Invoke(ToSegment(e.Result, isFinal: false));
    }

    private void OnTranscribed(object? sender, ConversationTranscriptionEventArgs e)
    {
        if (e.Result.Reason != ResultReason.RecognizedSpeech || string.IsNullOrEmpty(e.Result.Text)) return;
        Final?.Invoke(ToSegment(e.Result, isFinal: true));
    }

    private void OnCanceled(object? sender, ConversationTranscriptionCanceledEventArgs e)
    {
        var detail = e.Reason == CancellationReason.Error
            ? $"{e.ErrorCode}: {e.ErrorDetails}"
            : e.Reason.ToString();
        Error?.Invoke(detail);
    }

    /// <summary>Maps an SDK conversation result (with speaker id) to a <see cref="TranscriptSegment"/>.</summary>
    private static TranscriptSegment ToSegment(ConversationTranscriptionResult result, bool isFinal) =>
        new(result.Text, isFinal, TimeSpan.FromTicks(result.OffsetInTicks), result.Duration,
            SpeakerId: string.IsNullOrEmpty(result.SpeakerId) ? null : result.SpeakerId);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        try { await StopAsync().ConfigureAwait(false); }
        catch { /* best-effort teardown */ }

        _transcriber?.Dispose();
        _pushStream?.Dispose();
    }
}
