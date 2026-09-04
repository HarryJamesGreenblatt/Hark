using System.Text.Json;
using Azure.AI.OpenAI;
using Azure.Core;
using Azure.Identity;
using OpenAI.Chat;

namespace Hark.Core.Summarization;

/// <summary>
/// Summarize (Azure OpenAI) — turns a speaker-attributed transcript into a recap via a chat
/// deployment. Uses keyless Microsoft Entra ID authentication (<see cref="DefaultAzureCredential"/>),
/// mirroring the Speech pipeline: no keys in code or config; the signed-in identity must hold the
/// "Cognitive Services OpenAI User" role on the resource.
/// </summary>
public sealed class AzureOpenAiSummarizer : ISummarizer
{
    #region Fields

    /// <summary>The chat client bound to the configured Azure OpenAI deployment.</summary>
    private readonly ChatClient _chat;

    /// <summary>Case-insensitive options for deserializing the model's JSON recap.</summary>
    private static readonly JsonSerializerOptions RecapJson = new() { PropertyNameCaseInsensitive = true };

    /// <summary>An empty recap, returned for empty transcripts or empty completions.</summary>
    private static readonly MeetingRecap EmptyRecap =
        new(string.Empty, string.Empty, Array.Empty<RecapTopic>(), Array.Empty<RecapFollowUp>());

    /// <summary>An empty speaker recap, returned for empty transcripts or empty completions.</summary>
    private static readonly SpeakerRecap EmptySpeakerRecap = new(Array.Empty<SpeakerBrief>());

    #endregion

    #region Constructor(s)

    /// <summary>
    /// Creates a summarizer bound to an Azure OpenAI chat deployment.
    /// </summary>
    /// <param name="endpoint">The resource endpoint, e.g. <c>https://my-aoai.openai.azure.com/</c>.</param>
    /// <param name="deployment">The chat model deployment name.</param>
    /// <param name="credential">Optional credential override; defaults to <see cref="DefaultAzureCredential"/>.</param>
    public AzureOpenAiSummarizer(string endpoint, string deployment, TokenCredential? credential = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(deployment);

        var client = new AzureOpenAIClient(new Uri(endpoint), credential ?? new DefaultAzureCredential());
        _chat = client.GetChatClient(deployment);
    }

    #endregion

    #region Methods

    /// <inheritdoc />
    public async Task<MeetingRecap> SummarizeConversationAsync(string transcript, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(transcript))
            return EmptyRecap;

        var messages = new ChatMessage[]
        {
            new SystemChatMessage(ConversationSystemPrompt),
            new UserChatMessage($"Transcript:\n\n{transcript}"),
        };

        // JSON-schema structured output guarantees a parseable, well-shaped recap; the higher token
        // budget lets the recap scale with the meeting instead of collapsing to a terse paragraph.
        var options = new ChatCompletionOptions
        {
            Temperature = 0.4f,
            MaxOutputTokenCount = 3000,
            ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                "meeting_recap",
                BinaryData.FromString(ConversationSchema),
                jsonSchemaIsStrict: true),
        };

        var completion = await _chat.CompleteChatAsync(messages, options, cancellationToken)
            .ConfigureAwait(false);

        if (completion.Value.Content.Count == 0)
            return EmptyRecap;

        var recap = JsonSerializer.Deserialize<MeetingRecap>(completion.Value.Content[0].Text, RecapJson);
        return recap ?? EmptyRecap;
    }

    /// <inheritdoc />
    public async Task<SpeakerRecap> SummarizeSpeakersAsync(string transcript, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(transcript))
            return EmptySpeakerRecap;

        var messages = new ChatMessage[]
        {
            new SystemChatMessage(SpeakersSystemPrompt),
            new UserChatMessage($"Transcript:\n\n{transcript}"),
        };

        var options = new ChatCompletionOptions
        {
            Temperature = 0.4f,
            MaxOutputTokenCount = 3000,
            ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                "speaker_recap",
                BinaryData.FromString(SpeakersSchema),
                jsonSchemaIsStrict: true),
        };

        var completion = await _chat.CompleteChatAsync(messages, options, cancellationToken)
            .ConfigureAwait(false);

        if (completion.Value.Content.Count == 0)
            return EmptySpeakerRecap;

        var recap = JsonSerializer.Deserialize<SpeakerRecap>(completion.Value.Content[0].Text, RecapJson);
        return recap ?? EmptySpeakerRecap;
    }

    /// <summary>Builds the system prompt instructing the model how to summarize in the given style.</summary>
    /// <param name="style">The recap style to produce.</param>
    /// <returns>The system prompt text.</returns>
    private const string ConversationSystemPrompt =
        "You produce a structured meeting recap from a transcript. Each line is prefixed with its " +
        "speaker's label — EITHER an anonymous placeholder (like 'Guest-1') OR a real name assigned " +
        "during the session (like 'Harry'). Always refer to each speaker by the EXACT label on their " +
        "lines: keep 'Guest-1' as 'Guest-1' and 'Harry' as 'Harry'; never invent a name that isn't in " +
        "the transcript, and never demote a provided name back to a 'Guest-N' placeholder. Treat every " +
        "distinct label as a distinct speaker, INCLUDING labels that come from played-back or system " +
        "audio (e.g. a narrator or a read-aloud source).\n\n" +
        "Return a JSON object with:\n" +
        "- title: a short, specific headline naming the whole conversation (≤ 8 words, no trailing " +
        "period; e.g. 'Q3 Roadmap and Hiring Plan'). Name it by its topics, never by invented names.\n" +
        "- overview: 1-3 sentences capturing the meeting's purpose and outcome.\n" +
        "- topics: the meeting broken into the distinct subjects that were actually discussed. " +
        "Segment by topic shift, not by speaker. For each topic provide:\n" +
        "    - title: a short noun phrase naming the topic.\n" +
        "    - summary: one sentence stating the gist of that topic.\n" +
        "    - details: 2-5 bullets expanding on it — specific points raised, positions taken, " +
        "decisions made, numbers or examples mentioned, and any disagreements. Be substantive and " +
        "specific; do not merely restate the title or summary.\n" +
        "- followUps: a flat list of concrete action items or commitments. For each, set 'task' " +
        "(what will be done) and 'owner' (the EXACT label of the speaker responsible — a real name if " +
        "one is shown on their lines, otherwise the Guest label — or null if unassigned). " +
        "If there are none, return an empty array.\n\n" +
        "Cover the whole conversation proportionally: the more that was said, the more topics and " +
        "detail you should produce. Be faithful to the transcript; never fabricate details, names, " +
        "decisions, or tasks.";

    /// <summary>
    /// System prompt for the people-pivoted recap. Asks for one brief per speaker so the Speakers view
    /// genuinely complements the topic-pivoted Conversation view rather than restating it.
    /// </summary>
    private const string SpeakersSystemPrompt =
        "You produce a per-speaker recap from a transcript. Each line is prefixed with its speaker's " +
        "label — EITHER an anonymous placeholder (like 'Guest-1') OR a real name assigned during the " +
        "session (like 'Harry'). Refer to each speaker by the EXACT label on their lines: keep " +
        "'Guest-1' as 'Guest-1' and 'Harry' as 'Harry'; never invent a name, and never demote a " +
        "provided name to a 'Guest-N' placeholder.\n\n" +
        "Return a JSON object with a 'speakers' array containing one entry for EVERY distinct label " +
        "that appears in the transcript — including any that come from played-back or system audio, " +
        "such as a narrator or read-aloud source (they count as speakers too). For each, provide:\n" +
        "- speaker: the exact label shown on that speaker's lines (e.g. 'Harry' or 'Guest-1').\n" +
        "- summary: one sentence characterizing that speaker's role, stance, or overall contribution.\n" +
        "- points: 2-5 bullets of the specific things they said — positions they took, questions they " +
        "raised, claims or numbers they gave, and any commitments they made. Be substantive and " +
        "specific; attribute only what that speaker actually said.\n\n" +
        "Include every distinct speaker present in the transcript. Be faithful to the transcript; " +
        "never fabricate speakers, names, or content.";

    /// <summary>Strict JSON schema mirroring <see cref="MeetingRecap"/>, used for structured outputs.</summary>
    private const string ConversationSchema = """
        {
          "type": "object",
          "properties": {
            "title": { "type": "string" },
            "overview": { "type": "string" },
            "topics": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "title": { "type": "string" },
                  "summary": { "type": "string" },
                  "details": { "type": "array", "items": { "type": "string" } }
                },
                "required": ["title", "summary", "details"],
                "additionalProperties": false
              }
            },
            "followUps": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "task": { "type": "string" },
                  "owner": { "type": ["string", "null"] }
                },
                "required": ["task", "owner"],
                "additionalProperties": false
              }
            }
          },
          "required": ["title", "overview", "topics", "followUps"],
          "additionalProperties": false
        }
        """;

    /// <summary>Strict JSON schema mirroring <see cref="SpeakerRecap"/>, used for structured outputs.</summary>
    private const string SpeakersSchema = """
        {
          "type": "object",
          "properties": {
            "speakers": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "speaker": { "type": "string" },
                  "summary": { "type": "string" },
                  "points": { "type": "array", "items": { "type": "string" } }
                },
                "required": ["speaker", "summary", "points"],
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
