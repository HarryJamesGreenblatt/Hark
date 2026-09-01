using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.AI.OpenAI;
using Azure.Core;
using Azure.Identity;
using OpenAI.Chat;

namespace Hark.Oracle.Vision;

/// <summary>
/// The concept tier for the diagram class — the infographic analogue of <see cref="ConceptDesigner"/>.
/// Reads a window of live conversation about an explanatory / technical topic and distils ONE teachable
/// idea into a compact <see cref="InfographicConcept"/> (title + one focal line + up to three labeled
/// parts). A structured chat call (strict JSON schema), keyless via Microsoft Entra ID.
/// </summary>
public sealed class InfographicDesigner
{
    #region Fields

    private readonly ChatClient _chat;
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    #endregion

    #region Constructor(s)

    /// <summary>Creates a designer bound to an Azure OpenAI chat deployment.</summary>
    /// <param name="endpoint">The resource endpoint, e.g. <c>https://my-aoai.openai.azure.com/</c>.</param>
    /// <param name="deployment">The chat model deployment name.</param>
    /// <param name="credential">Optional credential override; defaults to <see cref="DefaultAzureCredential"/>.</param>
    public InfographicDesigner(string endpoint, string deployment, TokenCredential? credential = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(deployment);

        var client = new AzureOpenAIClient(new Uri(endpoint), credential ?? new DefaultAzureCredential());
        _chat = client.GetChatClient(deployment);
    }

    #endregion

    #region Methods

    /// <summary>Distils a conversation window into a single infographic concept (or null when there's nothing to work with).</summary>
    /// <param name="transcriptWindow">Recent dialogue, one line per finalized segment.</param>
    /// <param name="cancellationToken">Cancels the in-flight request.</param>
    public async Task<InfographicConcept?> DesignAsync(string transcriptWindow, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(transcriptWindow)) return null;

        var messages = new ChatMessage[]
        {
            new SystemChatMessage(SystemPrompt),
            new UserChatMessage($"Conversation window:\n\n{transcriptWindow}"),
        };

        var options = new ChatCompletionOptions
        {
            Temperature = 0.5f,   // legible, on-topic diagrams; less variety than the scene tier
            MaxOutputTokenCount = 1200,   // room for a one-sentence detail per node
            ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                "infographic_concept", BinaryData.FromString(Schema), jsonSchemaIsStrict: true),
        };

        var completion = await _chat.CompleteChatAsync(messages, options, cancellationToken).ConfigureAwait(false);
        if (completion.Value.Content.Count == 0) return null;

        var dto = JsonSerializer.Deserialize<Dto>(completion.Value.Content[0].Text, Json);
        if (dto is null) return null;

        var nodes = (dto.Nodes ?? [])
            .Where(n => !string.IsNullOrWhiteSpace(n.Label))
            .Select(n => new InfographicNode(n.Label!, n.Color ?? string.Empty, n.Detail ?? string.Empty))
            .ToList();

        return new InfographicConcept(dto.Title ?? string.Empty, nodes);
    }

    #endregion

    #region Nested Types

    private sealed record Dto(
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("nodes")] IReadOnlyList<NodeDto>? Nodes);

    private sealed record NodeDto(
        [property: JsonPropertyName("label")] string? Label,
        [property: JsonPropertyName("color")] string? Color,
        [property: JsonPropertyName("detail")] string? Detail);

    #endregion

    #region Prompt & schema

    /// <summary>The Oracle persona for the diagram class: one legible radial mind-map of the current beat.</summary>
    private const string SystemPrompt =
        "You are the Oracle — HARK's inner seer, drawing a live mind-map of a conversation. You are given a " +
        "window of what is being said RIGHT NOW. Distil this beat into a radial mind-map.\n\n" +
        "Return: a TITLE (the central topic of this beat, a few words) and up to 5 NODES (the key facets or " +
        "sub-points actually being discussed). Each NODE has a short LABEL (1-4 words), a COLOR word " +
        "(choose from blue, green, orange, purple, red), and a DETAIL: ONE concise sentence expanding the " +
        "label with the specific point from the conversation (shown when the user hovers the node).\n\n" +
        "Keep labels short and legible; prefer 3-5 nodes drawn straight from what is being said. Never use hex " +
        "colour codes — only the colour words above. The mind-map should teach, at a glance, what THIS beat is " +
        "about.";

    /// <summary>Strict JSON schema mirroring <see cref="InfographicConcept"/>.</summary>
    private const string Schema = """
        {
          "type": "object",
          "properties": {
            "title": { "type": "string" },
            "nodes": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "label": { "type": "string" },
                  "color": { "type": "string", "enum": ["blue", "green", "orange", "purple", "red"] },
                  "detail": { "type": "string" }
                },
                "required": ["label", "color", "detail"],
                "additionalProperties": false
              }
            }
          },
          "required": ["title", "nodes"],
          "additionalProperties": false
        }
        """;

    #endregion
}
