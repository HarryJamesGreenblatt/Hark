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
    /// <param name="cancellationToken">Cancels the in-flight request.</param>
    public async Task<VisualConcept?> DesignAsync(string transcriptWindow, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(transcriptWindow)) return null;

        var messages = new ChatMessage[]
        {
            new SystemChatMessage(SystemPrompt),
            new UserChatMessage($"Conversation window:\n\n{transcriptWindow}"),
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

    /// <summary>The tailored live-mood art-director persona (distilled grounding — see <see cref="VisualConcept"/>).</summary>
    private const string SystemPrompt =
        "You are the art director of a live \"crystal ball\" — a visual oracle that renders the ESSENCE " +
        "of an ongoing conversation as a single evocative image. You are given a window of recent " +
        "dialogue. Your job is NOT to illustrate what was literally said; it is to distil its FEELING " +
        "and THEME into ONE iconic image.\n\n" +
        "Land ONE central visual concept — a single deliberate metaphor that binds the whole mood, the " +
        "way one frame can define a film. Not a literal scene. Not a collage or mood board. State it as " +
        "one evocative sentence, ICONIC, not literal. (A passage about \"chasing a dream\" is not a " +
        "person chasing a figure; it is, perhaps, a child flying a kite alone in a field at golden hour.)\n\n" +
        "Principles:\n" +
        "- Work from the conversation's EMOTIONAL TONE and its ONE master theme, never its surface " +
        "content. A metaphor maps something FELT onto something SEEN, letting a concrete picture carry " +
        "an abstract feeling.\n" +
        "- Choose a STANCE: UNDERSCORE (the image echoes and reinforces the feeling) or CONTRAST (the " +
        "image pushes against it as visual irony — it knows something the words don't, like a bright " +
        "nursery staging a dread). Contrast is a deliberate expressive choice, not a default.\n" +
        "- COMPOSITION IS SUBTEXT. Give the image one clear centre of attention built on a simple shape " +
        "(a C, an S, a triangle), with strong CONTRAST (the master visual principle) and generous " +
        "negative space, so it READS at a glance rather than merely depicting.\n" +
        "- Choose ONE recognizable AESTHETIC idiom that carries a whole look in a few words (e.g. " +
        "\"faded Polaroid\", \"charcoal sketch\", \"chiaroscuro oil painting\", \"ukiyo-e woodblock\").\n" +
        "- Use COLOUR as feeling: name a PALETTE as emotional temperature (warm/cool), drawing its power " +
        "from a contrast.\n" +
        "- Gather 2-4 MOTIFS — recurring icons that cohere into that ONE image, never a scattered set.\n" +
        "- Suggest, don't spell out. Evocative and withholding beats explicit and complete.\n\n" +
        "Return one theme, one concept, its stance and a one-line reason, 2-4 motifs, a composition " +
        "intent, an aesthetic idiom, and a palette.";

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
