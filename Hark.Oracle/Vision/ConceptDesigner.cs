using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.AI.OpenAI;
using Azure.Core;
using Azure.Identity;
using OpenAI.Chat;

namespace Hark.Oracle.Vision;

/// <summary>
/// Concept (the art director) — the persona tier of <c>Oracle.Vision</c>. Reads a window of live
/// conversation and lands ONE iconic, metaphor-not-literal <see cref="VisualConcept"/>: the essence of
/// the moment as a single image intent, not an illustration of what was said. A structured chat call
/// (strict JSON schema), keyless via Microsoft Entra ID, mirroring the rest of the pipeline.
/// </summary>
public sealed class ConceptDesigner
{
    #region Fields

    /// <summary>The chat client bound to the configured Azure OpenAI deployment.</summary>
    private readonly ChatClient _chat;

    /// <summary>Case-insensitive options for deserializing the model's JSON concept.</summary>
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    #endregion

    #region Constructor(s)

    /// <summary>Creates a designer bound to an Azure OpenAI chat deployment.</summary>
    /// <param name="endpoint">The resource endpoint, e.g. <c>https://my-aoai.openai.azure.com/</c>.</param>
    /// <param name="deployment">The chat model deployment name.</param>
    /// <param name="credential">Optional credential override; defaults to <see cref="DefaultAzureCredential"/>.</param>
    public ConceptDesigner(string endpoint, string deployment, TokenCredential? credential = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(deployment);

        var client = new AzureOpenAIClient(new Uri(endpoint), credential ?? new DefaultAzureCredential());
        _chat = client.GetChatClient(deployment);
    }

    #endregion

    #region Methods

    /// <summary>Distils a conversation window into a single visual concept (or null when there's nothing to work with).</summary>
    /// <param name="transcriptWindow">Recent dialogue, one line per finalized segment.</param>
    /// <param name="previousVision">The concept currently on screen, to conjure a distinct new one from; or null.</param>
    /// <param name="cancellationToken">Cancels the in-flight request.</param>
    public async Task<VisualConcept?> DesignAsync(
        string transcriptWindow,
        string? previousVision = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(transcriptWindow)) return null;

        var user = string.IsNullOrWhiteSpace(previousVision)
            ? $"Conversation window:\n\n{transcriptWindow}"
            : $"Conversation window:\n\n{transcriptWindow}\n\nThe vision now on screen is: \"{previousVision}\". " +
              "This is a NEW moment — conjure a fresh scene with a different setting and subject, so it reads " +
              "as visibly distinct from the one on screen.";

        var messages = new ChatMessage[]
        {
            new SystemChatMessage(SystemPrompt),
            new UserChatMessage(user),
        };

        var options = new ChatCompletionOptions
        {
            Temperature = 0.9f,   // art direction wants variety, not determinism
            MaxOutputTokenCount = 1200,
            ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                "visual_concept", BinaryData.FromString(Schema), jsonSchemaIsStrict: true),
        };

        var completion = await _chat.CompleteChatAsync(messages, options, cancellationToken).ConfigureAwait(false);
        if (completion.Value.Content.Count == 0) return null;

        var dto = JsonSerializer.Deserialize<Dto>(completion.Value.Content[0].Text, Json);
        if (dto is null) return null;

        var stance = string.Equals(dto.Stance, "CONTRAST", StringComparison.OrdinalIgnoreCase)
            ? ConceptStance.Contrast
            : ConceptStance.Underscore;

        return new VisualConcept(
            dto.Theme ?? string.Empty,
            dto.Concept ?? string.Empty,
            stance,
            dto.StanceReason ?? string.Empty,
            dto.Motifs ?? Array.Empty<string>(),
            dto.Composition ?? string.Empty,
            dto.Aesthetic ?? string.Empty,
            dto.Palette ?? string.Empty);
    }

    #endregion

    #region Nested Types

    /// <summary>Wire shape for the model's JSON response.</summary>
    private sealed record Dto(
        [property: JsonPropertyName("theme")] string? Theme,
        [property: JsonPropertyName("concept")] string? Concept,
        [property: JsonPropertyName("stance")] string? Stance,
        [property: JsonPropertyName("stanceReason")] string? StanceReason,
        [property: JsonPropertyName("motifs")] IReadOnlyList<string>? Motifs,
        [property: JsonPropertyName("composition")] string? Composition,
        [property: JsonPropertyName("aesthetic")] string? Aesthetic,
        [property: JsonPropertyName("palette")] string? Palette);

    #endregion

    #region Prompt & schema

    /// <summary>The Oracle persona: conjure one coherent image aligned with the current beat.</summary>
    private const string SystemPrompt =
        "You are the Oracle — HARK's inner seer. You are given a window of what is being said RIGHT NOW " +
        "in a conversation. Conjure ONE image that shows the essence of this beat: a single, coherent " +
        "scene that a viewer would immediately feel belongs to THIS conversation.\n\n" +
        "You have no agenda of your own and no fixed style. Do not force a metaphor, and do not force " +
        "literalism — read the beat and render what it is genuinely about, as plainly or as poetically as " +
        "the moment itself calls for. Stay grounded in what is actually said, so the image reads as " +
        "ALIGNED with the conversation, not as a puzzle to decode.\n\n" +
        "Conjure ONE coherent scene — a single real place, a single clear subject, real light — never a " +
        "collage of assembled symbols, a diagram, text, or an interface. It should read at a glance.\n\n" +
        "Then describe it: a THEME (the master feeling of this beat, in a few words); a CONCEPT (that one " +
        "scene, in one vivid sentence); a STANCE — UNDERSCORE (the image echoes the feeling) or CONTRAST " +
        "(it quietly pushes against it) — with a one-line reason; 2-4 MOTIFS (elements that belong to that " +
        "ONE scene, never a scattered set); a COMPOSITION intent (one clear focal point with real depth); " +
        "an AESTHETIC idiom that carries a whole look in a few words (e.g. \"faded Polaroid\", \"charcoal " +
        "sketch\", \"chiaroscuro oil painting\"); and a PALETTE as emotional temperature.";

    /// <summary>Strict JSON schema mirroring <see cref="VisualConcept"/>.</summary>
    private const string Schema = """
        {
          "type": "object",
          "properties": {
            "theme":        { "type": "string" },
            "concept":      { "type": "string" },
            "stance":       { "type": "string", "enum": ["UNDERSCORE", "CONTRAST"] },
            "stanceReason": { "type": "string" },
            "motifs":       { "type": "array", "items": { "type": "string" } },
            "composition":  { "type": "string" },
            "aesthetic":    { "type": "string" },
            "palette":      { "type": "string" }
          },
          "required": ["theme", "concept", "stance", "stanceReason", "motifs", "composition", "aesthetic", "palette"],
          "additionalProperties": false
        }
        """;

    #endregion
}
