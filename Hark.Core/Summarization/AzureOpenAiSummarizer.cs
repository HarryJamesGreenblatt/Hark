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
        new(string.Empty, Array.Empty<RecapTopic>(), Array.Empty<RecapFollowUp>());

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
    public async Task<string> SummarizeAsync(string transcript, SummaryStyle style, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(transcript))
            return string.Empty;

        var messages = new ChatMessage[]
        {
            new SystemChatMessage(SystemPromptFor(style)),
            new UserChatMessage($"Transcript:\n\n{transcript}"),
        };

        var options = new ChatCompletionOptions { Temperature = 0.4f, MaxOutputTokenCount = 1200 };

        var completion = await _chat.CompleteChatAsync(messages, options, cancellationToken)
            .ConfigureAwait(false);

        return completion.Value.Content.Count > 0
            ? completion.Value.Content[0].Text.Trim()
            : string.Empty;
    }

    /// <inheritdoc />
    public async Task<MeetingRecap> SummarizeStructuredAsync(string transcript, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(transcript))
            return EmptyRecap;

        var messages = new ChatMessage[]
        {
            new SystemChatMessage(StructuredSystemPrompt),
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
                BinaryData.FromString(RecapSchema),
                jsonSchemaIsStrict: true),
        };

        var completion = await _chat.CompleteChatAsync(messages, options, cancellationToken)
            .ConfigureAwait(false);

        if (completion.Value.Content.Count == 0)
            return EmptyRecap;

        var recap = JsonSerializer.Deserialize<MeetingRecap>(completion.Value.Content[0].Text, RecapJson);
        return recap ?? EmptyRecap;
    }

    /// <summary>Builds the system prompt instructing the model how to summarize in the given style.</summary>
    /// <param name="style">The recap style to produce.</param>
    /// <returns>The system prompt text.</returns>
    private static string SystemPromptFor(SummaryStyle style) => style switch
    {
        SummaryStyle.Narrative =>
            "You summarize transcripts. Speakers are anonymous labels like 'Guest-1'. " +
            "Write a single concise, neutral paragraph capturing the gist of the conversation. " +
            "Do not invent details or real names.",

        SummaryStyle.PerSpeaker =>
            "You summarize transcripts. Speakers are anonymous labels like 'Guest-1'. " +
            "For each speaker, write one short bullet summarizing their contribution, formatted as " +
            "'Guest-N: ...'. Keep it factual; do not invent details or real names.",

        _ => // Teams
            "You produce concise, professional meeting-style recaps. Speakers are anonymous labels " +
            "like 'Guest-1'. Structure the recap with three short sections using these exact headings:\n" +
            "Overview:\nKey points:\nAction items:\n" +
            "Use brief bullet points under 'Key points' and 'Action items'. If there are no action " +
            "items, write 'None'. Be faithful to the transcript; do not invent details or real names.",
    };

    /// <summary>
    /// System prompt for the structured Teams-Recap-style summary. Drives depth by asking the model to
    /// segment the conversation by topic and expand each with specific detail bullets, rather than
    /// compressing everything into one terse paragraph.
    /// </summary>
    private const string StructuredSystemPrompt =
        "You produce a structured meeting recap from a transcript. Speakers are anonymous labels " +
        "like 'Guest-1'; refer to them only by those labels and never invent real names.\n\n" +
        "Return a JSON object with:\n" +
        "- overview: 1-3 sentences capturing the meeting's purpose and outcome.\n" +
        "- topics: the meeting broken into the distinct subjects that were actually discussed. " +
        "Segment by topic shift, not by speaker. For each topic provide:\n" +
        "    - title: a short noun phrase naming the topic.\n" +
        "    - summary: one sentence stating the gist of that topic.\n" +
        "    - details: 2-5 bullets expanding on it — specific points raised, positions taken, " +
        "decisions made, numbers or examples mentioned, and any disagreements. Be substantive and " +
        "specific; do not merely restate the title or summary.\n" +
        "- followUps: a flat list of concrete action items or commitments. For each, set 'task' " +
        "(what will be done) and 'owner' (the Guest label responsible, or null if unassigned). " +
        "If there are none, return an empty array.\n\n" +
        "Cover the whole conversation proportionally: the more that was said, the more topics and " +
        "detail you should produce. Be faithful to the transcript; never fabricate details, names, " +
        "decisions, or tasks.";

    /// <summary>Strict JSON schema mirroring <see cref="MeetingRecap"/>, used for structured outputs.</summary>
    private const string RecapSchema = """
        {
          "type": "object",
          "properties": {
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
          "required": ["overview", "topics", "followUps"],
          "additionalProperties": false
        }
        """;

    #endregion
}
