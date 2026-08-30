using System.Text;

namespace Hark.Oracle.Vision;

/// <summary>
/// Render — the realisation tier of <c>Oracle.Vision</c>: composes a <see cref="VisualConcept"/> into a
/// well-formed image-generation prompt (deterministic, no model call). The analog of sequitur's
/// <c>build_poster_prompt</c> — it asks for one coherent <em>scene of the world</em>, layering the
/// aesthetic, stance, motifs, composition intent, and palette onto the central concept, and closes by
/// insisting on one coherent photographic scene (not a symbol collage, diagram, text, or interface).
/// </summary>
public static class VisionPromptComposer
{
    /// <summary>Composes a visual concept into a still-image prompt.</summary>
    /// <param name="concept">The art director's contribution.</param>
    /// <returns>The image-generation prompt.</returns>
    public static string Compose(VisualConcept concept)
    {
        ArgumentNullException.ThrowIfNull(concept);

        var sb = new StringBuilder();
        sb.Append("A single evocative image that captures ").Append(Clean(concept.Concept)).Append('.');

        if (!string.IsNullOrWhiteSpace(concept.Aesthetic))
            sb.Append(" Rendered as ").Append(Clean(concept.Aesthetic)).Append('.');

        sb.Append(concept.Stance == ConceptStance.Contrast
            ? " The image contrasts the feeling — a deliberate visual irony."
            : " The image underscores the feeling.");

        if (concept.Motifs is { Count: > 0 })
            sb.Append(" Elements in the scene: ").Append(string.Join(", ", concept.Motifs)).Append('.');

        if (!string.IsNullOrWhiteSpace(concept.Composition))
            sb.Append(" Composition: ").Append(Clean(concept.Composition)).Append('.');
        sb.Append(" One clear focal point and strong contrast, set in a real, specific place with real ")
          .Append("light — never an isolated object floating on an empty black background.");

        if (!string.IsNullOrWhiteSpace(concept.Palette))
            sb.Append(" Palette: ").Append(Clean(concept.Palette)).Append('.');

        if (!string.IsNullOrWhiteSpace(concept.Theme))
            sb.Append(" Mood: ").Append(Clean(concept.Theme)).Append('.');

        // Keep it one coherent real scene — no symbol-collage / UI / text (which read as incoherent).
        sb.Append(" Render it as one coherent, real photographic scene — not a collage of symbols, a ")
          .Append("diagram, text, or a screen or interface.");

        return sb.ToString();
    }

    /// <summary>Trims a trailing period/space so composed clauses join cleanly.</summary>
    private static string Clean(string s) => s.TrimEnd('.', ' ');

    /// <summary>
    /// A gentler, more abstract variant of the prompt — dropping literal motifs for pure mood, palette,
    /// and aesthetic — used to retry ONCE when a render is refused by content safety, so a scattered,
    /// topic-dependent RAI block doesn't lose the beat entirely.
    /// </summary>
    /// <param name="concept">The art director's contribution.</param>
    /// <returns>A softened image-generation prompt.</returns>
    public static string ComposeSoftened(VisualConcept concept)
    {
        ArgumentNullException.ThrowIfNull(concept);

        var sb = new StringBuilder();
        sb.Append("An abstract, atmospheric image evoking ")
          .Append(Clean(string.IsNullOrWhiteSpace(concept.Theme) ? concept.Concept : concept.Theme))
          .Append('.');

        if (!string.IsNullOrWhiteSpace(concept.Aesthetic))
            sb.Append(" Rendered as ").Append(Clean(concept.Aesthetic)).Append('.');
        if (!string.IsNullOrWhiteSpace(concept.Palette))
            sb.Append(" Palette: ").Append(Clean(concept.Palette)).Append('.');

        sb.Append(" A calm, symbolic, non-literal mood piece — soft light and natural forms, nothing ")
          .Append("graphic, violent, or otherwise sensitive. One coherent scene, not a collage of symbols, ")
          .Append("a diagram, text, or an interface.");

        return sb.ToString();
    }
}
