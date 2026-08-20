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
    private readonly ChatClient _chat;

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

        var completion = await _chat.CompleteChatAsync(messages, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return completion.Value.Content.Count > 0
            ? completion.Value.Content[0].Text.Trim()
            : string.Empty;
    }

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
}
