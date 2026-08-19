using Azure.Core;
using Azure.Identity;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;

namespace Hark.Core.Transcription;

/// <summary>
/// Recognize (Azure) — continuous speech-to-text via the Azure AI Speech service.
/// Uses keyless Microsoft Entra ID authentication (<see cref="DefaultAzureCredential"/>), so no
/// keys live in code or config; the signed-in identity must hold the "Cognitive Services Speech User"
/// role on the target resource. Audio is streamed over a persistent websocket for low-latency interim results.
/// </summary>
public sealed class AzureSpeechTranscriber : ISpeechTranscriber
{
    /// <summary>Token scope for Azure AI / Cognitive Services data-plane access.</summary>
    private const string CognitiveServicesScope = "https://cognitiveservices.azure.com/.default";

    /// <summary>Refresh the Entra token comfortably before its ~1 hour expiry.</summary>
    private static readonly TimeSpan TokenRefreshInterval = TimeSpan.FromMinutes(8);

    /// <summary>
    /// Languages considered by continuous language identification when the caller doesn't pin one.
    /// Keep this to the realistic set for the audio — continuous LID is most reliable with a small
    /// candidate list (the service supports a limited number of simultaneous languages).
    /// </summary>
    private static readonly string[] CandidateLanguages = { "en-US", "es-ES" };

    private readonly string _region;
    private readonly string _resourceId;
    private readonly string? _language;
    private readonly TokenCredential _credential;

    private PushAudioInputStream? _pushStream;
    private SpeechRecognizer? _recognizer;
    private Timer? _tokenRefreshTimer;
    private bool _disposed;

    /// <inheritdoc />
    public event Action<TranscriptSegment>? Interim;

    /// <inheritdoc />
    public event Action<TranscriptSegment>? Final;

    /// <inheritdoc />
    public event Action<string>? Error;

    /// <summary>
    /// Creates an Azure transcriber bound to a specific Speech resource.
    /// </summary>
    /// <param name="region">The resource region, e.g. <c>eastus2</c>.</param>
    /// <param name="resourceId">
    /// The full ARM resource ID of the Speech account, e.g.
    /// <c>/subscriptions/{sub}/resourceGroups/rg-hark/providers/Microsoft.CognitiveServices/accounts/spch-hark</c>.
    /// </param>
    /// <param name="language">Optional BCP-47 language tag (e.g. <c>en-US</c>). Defaults to the service default.</param>
    /// <param name="credential">Optional credential override; defaults to <see cref="DefaultAzureCredential"/>.</param>
    public AzureSpeechTranscriber(string region, string resourceId, string? language = null, TokenCredential? credential = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(region);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);

        _region = region;
        _resourceId = resourceId;
        _language = language;
        // Picks up Visual Studio / Azure CLI sign-in for local dev, Managed Identity when hosted.
        _credential = credential ?? new DefaultAzureCredential();
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var config = SpeechConfig.FromAuthorizationToken(
            await BuildAuthTokenAsync(cancellationToken).ConfigureAwait(false), _region);

        // Request word-level detail and stable interim results for a responsive stream.
        config.OutputFormat = OutputFormat.Simple;

        var format = AudioStreamFormat.GetWaveFormatPCM(
            samplesPerSecond: 16_000, bitsPerSample: 16, channels: 1);
        _pushStream = AudioInputStream.CreatePushStream(format);

        var audioConfig = AudioConfig.FromStreamInput(_pushStream);

        if (!string.IsNullOrWhiteSpace(_language))
        {
            // Caller pinned a specific language.
            config.SpeechRecognitionLanguage = _language;
            _recognizer = new SpeechRecognizer(config, audioConfig);
        }
        else
        {
            // No language pinned: enable continuous language identification so mixed-language
            // audio (e.g. Spanish + English lyrics) is transcribed throughout instead of being
            // dropped by a single-language model. Continuous LID re-evaluates the language as the
            // audio evolves, rather than only detecting it once at the start.
            config.SetProperty(PropertyId.SpeechServiceConnection_LanguageIdMode, "Continuous");
            var autoDetect = AutoDetectSourceLanguageConfig.FromLanguages(CandidateLanguages);
            _recognizer = new SpeechRecognizer(config, autoDetect, audioConfig);
        }

        _recognizer.Recognizing += OnRecognizing;
        _recognizer.Recognized += OnRecognized;
        _recognizer.Canceled += OnCanceled;

        await _recognizer.StartContinuousRecognitionAsync().ConfigureAwait(false);

        // Keep the authorization token fresh for long-running sessions.
        _tokenRefreshTimer = new Timer(
            async _ => await RefreshTokenAsync().ConfigureAwait(false),
            state: null, dueTime: TokenRefreshInterval, period: TokenRefreshInterval);
    }

    /// <inheritdoc />
    public void Write(byte[] pcm, int count)
    {
        if (count <= 0 || _pushStream is null) return;

        // PushAudioInputStream.Write expects an exactly-sized buffer.
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

        if (_recognizer is not null)
            await _recognizer.StopContinuousRecognitionAsync().ConfigureAwait(false);
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

    /// <summary>Refreshes the recognizer's authorization token in place.</summary>
    private async Task RefreshTokenAsync()
    {
        try
        {
            if (_recognizer is null) return;
            _recognizer.AuthorizationToken = await BuildAuthTokenAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Error?.Invoke($"Token refresh failed: {ex.Message}");
        }
    }

    private void OnRecognizing(object? sender, SpeechRecognitionEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Result.Text)) return;
        Interim?.Invoke(ToSegment(e.Result, isFinal: false));
    }

    private void OnRecognized(object? sender, SpeechRecognitionEventArgs e)
    {
        if (e.Result.Reason != ResultReason.RecognizedSpeech || string.IsNullOrEmpty(e.Result.Text)) return;
        Final?.Invoke(ToSegment(e.Result, isFinal: true));
    }

    private void OnCanceled(object? sender, SpeechRecognitionCanceledEventArgs e)
    {
        var detail = e.Reason == CancellationReason.Error
            ? $"{e.ErrorCode}: {e.ErrorDetails}"
            : e.Reason.ToString();
        Error?.Invoke(detail);
    }

    /// <summary>Maps an SDK recognition result to a transport-agnostic <see cref="TranscriptSegment"/>.</summary>
    private static TranscriptSegment ToSegment(RecognitionResult result, bool isFinal) =>
        new(result.Text, isFinal, TimeSpan.FromTicks(result.OffsetInTicks), result.Duration);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        try { await StopAsync().ConfigureAwait(false); }
        catch { /* best-effort teardown */ }

        _recognizer?.Dispose();
        _pushStream?.Dispose();
    }
}
