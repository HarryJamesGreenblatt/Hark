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
              "Stay ALIGNED with what THIS window is about. If the talk has moved to a new subject, let the " +
              "scene move with it; if it is still the same subject, depict that same subject from a fresh angle " +
              "or moment so it doesn't simply repeat the image on screen. Do not change subject just to differ.";

        var messages = new ChatMessage[]
        {
            new SystemChatMessage(SystemPrompt),
            new UserChatMessage(user),
        };

        var options = new ChatCompletionOptions
        {
            Temperature = 0.7f,   // some art-direction variety, but grounded enough to stay on-topic
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
        "You have no agenda of your own and no fixed style. DEFAULT TO THE LITERAL: depict the actual " +
        "subject, people, place, and situation being discussed, so a viewer instantly recognizes what THIS " +
        "beat is about. Be poetic or metaphorical only when the beat itself is abstract; when in doubt, be " +
        "literal and on-topic, and never make the image a puzzle to decode.\n\n" +
        "Conjure ONE coherent scene — a single real place, a single clear subject, real light — never a " +
        "collage of assembled symbols, a diagram, text, or an interface. It should read at a glance.\n\n" +
        "Then describe it: a THEME (the master feeling of this beat, in a few words); a CONCEPT (that one " +
        "scene, in one vivid sentence); a STANCE — almost always UNDERSCORE (the image echoes the beat); use " +
        "CONTRAST (a quiet visual irony) only on the rare beat with a sharp, obvious irony, never as an excuse " +
        "to go abstract or off-topic — with a one-line reason; 2-4 MOTIFS (elements that belong to that " +
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
