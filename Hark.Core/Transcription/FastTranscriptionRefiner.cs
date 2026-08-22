using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;

namespace Hark.Core.Transcription;

/// <summary>
/// Refine (Azure Fast Transcription) — a second, offline pass over the whole buffered session audio.
/// Because it sees the entire recording at once it clusters speakers <em>globally</em>, which is far
/// more accurate than the incremental, streaming diarization used for live captions. Returns the
/// re-diarized, speaker-attributed segments so the conversation can be rebuilt.
/// <para>
/// Keyless: authenticates with the same Microsoft Entra identity as the rest of the pipeline (the
/// signed-in identity must hold a data-plane role, e.g. "Cognitive Services User", on the resource).
/// </para>
/// </summary>
public sealed class FastTranscriptionRefiner
{
    #region Constants

    /// <summary>Token scope for Azure AI / Cognitive Services data-plane access.</summary>
    private const string CognitiveServicesScope = "https://cognitiveservices.azure.com/.default";

    /// <summary>Fast Transcription REST API version (synchronous transcribe operation).</summary>
    private const string ApiVersion = "2025-10-15";

    /// <summary>Recognition locale; diarization pins a single language, matching the live engine.</summary>
    private const string Locale = "en-US";

    #endregion

    #region Fields

    /// <summary>Shared HTTP client for the transcribe calls.</summary>
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };

    /// <summary>The resource's custom-subdomain endpoint, e.g. <c>https://spch-hark.cognitiveservices.azure.com</c>.</summary>
    private readonly string _endpoint;

    /// <summary>The credential used to authorize against the Speech resource.</summary>
    private readonly TokenCredential _credential;

    #endregion

    #region Constructor(s)

    /// <summary>
    /// Creates a refiner bound to a Speech resource, deriving its custom-subdomain endpoint from the
    /// ARM resource id.
    /// </summary>
    /// <param name="resourceId">The full ARM resource ID of the Speech account.</param>
    /// <param name="credential">Optional credential override; defaults to <see cref="DefaultAzureCredential"/>.</param>
    public FastTranscriptionRefiner(string resourceId, TokenCredential? credential = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        _endpoint = EndpointFromResourceId(resourceId);
        _credential = credential ?? new DefaultAzureCredential();
    }

    #endregion

    #region Methods

    /// <summary>
    /// Re-transcribes and re-diarizes the given 16 kHz mono 16-bit PCM audio in one offline pass.
    /// </summary>
    /// <param name="pcm16">The full session audio as 16 kHz mono 16-bit PCM.</param>
    /// <param name="maxSpeakers">Upper-bound hint for the number of speakers (clamped to 2–35).</param>
    /// <param name="phrases">Optional proper-noun hints (names, shows) to bias recognition.</param>
    /// <param name="cancellationToken">Cancels the in-flight request.</param>
    /// <returns>The re-diarized, speaker-attributed segments (empty if none were recognized).</returns>
    public async Task<IReadOnlyList<TranscriptSegment>> RefineAsync(
        byte[] pcm16, int maxSpeakers, IReadOnlyList<string>? phrases = null, CancellationToken cancellationToken = default)
    {
        if (pcm16 is null || pcm16.Length == 0) return Array.Empty<TranscriptSegment>();

        var wav = ToWav(pcm16);
        var definition = BuildDefinition(Math.Clamp(maxSpeakers, 2, 35), phrases);

        using var content = new MultipartFormDataContent();
        var audio = new ByteArrayContent(wav);
        audio.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        content.Add(audio, "audio", "audio.wav");
        content.Add(new StringContent(definition), "definition");

        var url = $"{_endpoint}/speechtotext/transcriptions:transcribe?api-version={ApiVersion}";
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };

        var token = await _credential
            .GetTokenAsync(new TokenRequestContext(new[] { CognitiveServicesScope }), cancellationToken)
            .ConfigureAwait(false);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);

        using var response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return ParsePhrases(json);
    }

    /// <summary>Builds the Fast Transcription request definition JSON (locale + diarization + phrases).</summary>
    private static string BuildDefinition(int maxSpeakers, IReadOnlyList<string>? phrases)
    {
        object definition = phrases is { Count: > 0 }
            ? new { locales = new[] { Locale }, diarization = new { enabled = true, maxSpeakers }, phraseList = new { phrases } }
            : new { locales = new[] { Locale }, diarization = new { enabled = true, maxSpeakers } };
        return JsonSerializer.Serialize(definition);
    }

    /// <summary>Maps the <c>phrases</c> array of the response into speaker-attributed segments.</summary>
    private static IReadOnlyList<TranscriptSegment> ParsePhrases(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("phrases", out var phrases) || phrases.ValueKind != JsonValueKind.Array)
            return Array.Empty<TranscriptSegment>();

        var segments = new List<TranscriptSegment>(phrases.GetArrayLength());
        foreach (var phrase in phrases.EnumerateArray())
        {
            var text = phrase.TryGetProperty("text", out var t) ? t.GetString() ?? string.Empty : string.Empty;
            if (string.IsNullOrWhiteSpace(text)) continue;

            long offsetMs = phrase.TryGetProperty("offsetMilliseconds", out var o) ? o.GetInt64() : 0;
            long durationMs = phrase.TryGetProperty("durationMilliseconds", out var d) ? d.GetInt64() : 0;

            // Fast Transcription speaker ids are 0-based; map to the app's 1-based Guest-N labels.
            string? speaker = phrase.TryGetProperty("speaker", out var s) && s.ValueKind == JsonValueKind.Number
                ? $"Guest-{s.GetInt32() + 1}"
                : null;

            segments.Add(new TranscriptSegment(
                text.Trim(), IsFinal: true,
                TimeSpan.FromMilliseconds(offsetMs), TimeSpan.FromMilliseconds(durationMs), speaker));
        }
        return segments;
    }

    /// <summary>Wraps raw PCM in a canonical 44-byte WAV header (16 kHz mono 16-bit).</summary>
    private static byte[] ToWav(byte[] pcm, int sampleRate = 16_000, short channels = 1, short bitsPerSample = 16)
    {
        int byteRate = sampleRate * channels * bitsPerSample / 8;
        short blockAlign = (short)(channels * bitsPerSample / 8);

        using var ms = new MemoryStream(44 + pcm.Length);
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8.ToArray());
        w.Write(36 + pcm.Length);
        w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray());
        w.Write(16);                 // PCM fmt chunk size
        w.Write((short)1);           // audio format = PCM
        w.Write(channels);
        w.Write(sampleRate);
        w.Write(byteRate);
        w.Write(blockAlign);
        w.Write(bitsPerSample);
        w.Write("data"u8.ToArray());
        w.Write(pcm.Length);
        w.Write(pcm);
        w.Flush();
        return ms.ToArray();
    }

    /// <summary>Derives the custom-subdomain endpoint (<c>https://{account}.cognitiveservices.azure.com</c>).</summary>
    private static string EndpointFromResourceId(string resourceId)
    {
        const string marker = "/accounts/";
        int idx = resourceId.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
        var name = idx >= 0 ? resourceId[(idx + marker.Length)..] : resourceId;
        int slash = name.IndexOf('/');
        if (slash >= 0) name = name[..slash];
        return $"https://{name.Trim()}.cognitiveservices.azure.com";
    }

    #endregion
}
