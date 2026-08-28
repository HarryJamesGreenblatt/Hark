using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.AI.OpenAI;
using Azure.Core;
using Azure.Identity;
using OpenAI.Chat;

namespace Hark.Core.Transcription;

/// <summary>
/// Recognition (Azure OpenAI) — an optional text-only <em>naming</em> pass that turns anonymous
/// <c>Guest-N</c> labels into real display names when the transcript itself identifies a speaker
/// (an introduction, direct address, or self-identification). It never invents: a label with no clear
/// textual evidence keeps its <c>Guest-N</c> label. Intended to run after
/// <see cref="SemanticDiarizationRefiner"/>, over its canonical labels.
/// <para>
/// Non-destructive to text/timing — only <see cref="TranscriptSegment.SpeakerId"/> changes. Two labels
/// the model resolves to the same person collapse to one name (which also mops up the streaming-split
/// case). Empty input or no confident names returns the input unchanged; a genuine service failure
/// <b>throws</b> for the caller to handle, which keeps the un-named result.
/// </para>
/// <para>
/// Keyless, mirroring <see cref="SemanticDiarizationRefiner"/>: the signed-in identity must hold the
/// "Cognitive Services OpenAI User" role on the resource.
/// </para>
/// </summary>
public sealed class SpeakerNamingRefiner
{
    #region Fields

    /// <summary>The chat client bound to the configured Azure OpenAI deployment.</summary>
    private readonly ChatClient _chat;

    /// <summary>Case-insensitive options for deserializing the model's JSON name map.</summary>
    private static readonly JsonSerializerOptions NamesJson = new() { PropertyNameCaseInsensitive = true };

    #endregion

    #region Constructor(s)

    /// <summary>Creates a naming refiner bound to an Azure OpenAI chat deployment.</summary>
    /// <param name="endpoint">The resource endpoint, e.g. <c>https://my-aoai.openai.azure.com/</c>.</param>
    /// <param name="deployment">The chat model deployment name.</param>
    /// <param name="credential">Optional credential override; defaults to <see cref="DefaultAzureCredential"/>.</param>
    public SpeakerNamingRefiner(string endpoint, string deployment, TokenCredential? credential = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(deployment);

        var client = new AzureOpenAIClient(new Uri(endpoint), credential ?? new DefaultAzureCredential());
        _chat = client.GetChatClient(deployment);
    }

    #endregion

    #region Methods

    /// <summary>
    /// Infers real display names for the diarized speakers from the transcript, returning the segments
    /// with confidently-identified labels renamed and every other label left as its <c>Guest-N</c>.
    /// </summary>
    /// <param name="segments">The canonically-labeled segments (e.g. from <see cref="SemanticDiarizationRefiner"/>).</param>
    /// <param name="cancellationToken">Cancels the in-flight request.</param>
    /// <returns>The segments with names applied, or the input unchanged when nothing could be identified.</returns>
    public async Task<IReadOnlyList<TranscriptSegment>> NameAsync(
        IReadOnlyList<TranscriptSegment> segments, CancellationToken cancellationToken = default)
    {
        if (segments is null || segments.Count == 0) return segments ?? Array.Empty<TranscriptSegment>();

        var names = await RequestNamesAsync(segments, cancellationToken).ConfigureAwait(false);
        if (names.Count == 0) return segments;

        var result = new List<TranscriptSegment>(segments.Count);
        foreach (var seg in segments)
        {
            result.Add(!string.IsNullOrWhiteSpace(seg.SpeakerId) && names.TryGetValue(seg.SpeakerId, out var name)
                ? seg with { SpeakerId = name }
                : seg);
        }
        return result;
    }

    /// <summary>
    /// Infers a <c>label → real name</c> map from the transcript <em>without</em> mutating segments — the
    /// live counterpart to <see cref="NameAsync"/>. Lets the host apply names through its own rename/merge
    /// path (so live splits of one person collapse into a single name). Returns an empty map on empty input.
    /// </summary>
    /// <param name="segments">The current, labeled conversation (named or <c>Guest-N</c>).</param>
    /// <param name="cancellationToken">Cancels the in-flight request.</param>
    /// <returns>A map from the label as it appears to the inferred real name (confident names only).</returns>
    public async Task<IReadOnlyDictionary<string, string>> InferNamesAsync(
        IReadOnlyList<TranscriptSegment> segments, CancellationToken cancellationToken = default)
    {
        if (segments is null || segments.Count == 0) return EmptyMap;
        return await RequestNamesAsync(segments, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Asks the model for a <c>Guest-N → real name</c> map, keeping only confident (non-blank) names.</summary>
    private async Task<IReadOnlyDictionary<string, string>> RequestNamesAsync(
        IReadOnlyList<TranscriptSegment> segments, CancellationToken cancellationToken)
    {
        var messages = new ChatMessage[]
        {
            new SystemChatMessage(SystemPrompt),
            new UserChatMessage(BuildLabeledTranscript(segments)),
        };

        var options = new ChatCompletionOptions
        {
            Temperature = 0f,                 // deterministic identification
            MaxOutputTokenCount = 2000,
            ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                "speaker_names",
                BinaryData.FromString(NamesSchema),
                jsonSchemaIsStrict: true),
        };

        var completion = await _chat.CompleteChatAsync(messages, options, cancellationToken).ConfigureAwait(false);
        if (completion.Value.Content.Count == 0) return EmptyMap;

        var parsed = JsonSerializer.Deserialize<NamesResult>(completion.Value.Content[0].Text, NamesJson);
        if (parsed?.Speakers is not { Count: > 0 }) return EmptyMap;

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in parsed.Speakers)
        {
            // A blank name means "leave this label as Guest-N".
            if (!string.IsNullOrWhiteSpace(s.Label) && !string.IsNullOrWhiteSpace(s.Name))
                map[s.Label.Trim()] = s.Name.Trim();
        }
        return map;
    }

    /// <summary>Renders the segments as labeled lines the model reads to identify each speaker.</summary>
    private static string BuildLabeledTranscript(IReadOnlyList<TranscriptSegment> segments)
    {
        var sb = new StringBuilder(segments.Count * 48);
        foreach (var seg in segments)
        {
            var label = string.IsNullOrWhiteSpace(seg.SpeakerId) ? "Unknown" : seg.SpeakerId;
            sb.Append(label).Append(": ").Append(seg.Text).Append('\n');
        }
        return sb.ToString();
    }

    #endregion

    #region Nested Types

    /// <summary>One resolved identity: the anonymous label and the real name (blank when unidentified).</summary>
    private sealed record SpeakerName(
        [property: JsonPropertyName("label")] string Label,
        [property: JsonPropertyName("name")] string Name);

    /// <summary>The model's full response: one entry per distinct label.</summary>
    private sealed record NamesResult(
        [property: JsonPropertyName("speakers")] IReadOnlyList<SpeakerName> Speakers);

    #endregion

    #region Prompt & schema

    /// <summary>An empty name map, used when the model returns nothing usable.</summary>
    private static readonly IReadOnlyDictionary<string, string> EmptyMap = new Dictionary<string, string>();

    /// <summary>System prompt instructing the model to name only clearly-identified speakers, never inventing.</summary>
    private const string SystemPrompt =
        "You identify the real names of the SPEAKERS in a transcript. Each line is 'Guest-N: text', " +
        "where Guest-N are anonymous, per-session labels for the people who are actually speaking.\n\n" +
        "For each distinct Guest-N label, decide whether the transcript clearly reveals that speaker's " +
        "real name — for example they are introduced ('here he is, Mr. Don Rickles'), directly addressed " +
        "('thanks, Katie'), or they self-identify ('I'm Dean'). If so, map the label to that name.\n\n" +
        "Rules:\n" +
        "- Name the SPEAKER of a label, NEVER a person who is merely mentioned or talked about. A " +
        "celebrity the speakers joke about is not the speaker's own name.\n" +
        "- NEVER invent, guess, or infer a name that the text does not clearly support. When in doubt, " +
        "return an empty string for that label — it will stay 'Guest-N'.\n" +
        "- Use the natural name as spoken (e.g. 'Don Rickles', 'Dean Martin'); don't add titles or " +
        "surnames you weren't given.\n" +
        "- If two labels are clearly the same real person, give them the same name.\n\n" +
        "Return a JSON object with a 'speakers' array of { label, name } — one entry per distinct " +
        "Guest-N label. Use name = \"\" when the speaker cannot be confidently identified.";

    /// <summary>Strict JSON schema mirroring <see cref="NamesResult"/>, used for structured outputs.</summary>
    private const string NamesSchema = """
        {
          "type": "object",
          "properties": {
            "speakers": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "label": { "type": "string" },
                  "name": { "type": "string" }
                },
                "required": ["label", "name"],
                "additionalProperties": false
              }
            }
          },
          "required": ["speakers"],
          "additionalProperties": false
        }
        """;

    #endregion
}
