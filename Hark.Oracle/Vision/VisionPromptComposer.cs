using System.Text;

namespace Hark.Oracle.Vision;

/// <summary>
/// Render — the realisation tier of <c>Oracle.Vision</c>: composes a <see cref="VisualConcept"/> into a
/// FLUX-idiomatic image prompt (deterministic, no model call). Follows BFL's guidance: front-load the
/// SUBJECT, then style, then context (word order matters), keep it ~40-80 words, and state everything
/// POSITIVELY — FLUX.2 has no negative prompts, so "not X" only biases toward X.
/// </summary>
public static class VisionPromptComposer
{
    /// <summary>Composes a visual concept into a FLUX-idiomatic image prompt (front-loaded, positive-only).</summary>
    /// <param name="concept">The art director's contribution.</param>
    /// <returns>The image-generation prompt.</returns>
    public static string Compose(VisualConcept concept)
    {
        ArgumentNullException.ThrowIfNull(concept);

        // Subject first (the concept sentence), then scene elements, style, context — word order matters to
        // FLUX. Everything positive: the coherence/setting asks replace the old "not a collage / not on
        // black" negatives (which FLUX can't negate and would bias toward).
        var sb = new StringBuilder();
        sb.Append(Capitalize(Clean(concept.Concept))).Append('.');

        if (concept.Motifs is { Count: > 0 })
            sb.Append(' ').Append(Capitalize(string.Join(", ", concept.Motifs))).Append('.');

        if (!string.IsNullOrWhiteSpace(concept.Aesthetic))
            sb.Append(" Rendered as ").Append(Clean(concept.Aesthetic)).Append('.');

        if (!string.IsNullOrWhiteSpace(concept.Composition))
            sb.Append(' ').Append(Capitalize(Clean(concept.Composition))).Append('.');

        sb.Append(" Cinematic natural light, one clear focal point, a single coherent scene set in a full, real environment.");

        if (!string.IsNullOrWhiteSpace(concept.Palette))
            sb.Append(' ').Append(Capitalize(Clean(concept.Palette))).Append(" palette.");

        if (!string.IsNullOrWhiteSpace(concept.Theme))
            sb.Append(' ').Append(Capitalize(Clean(concept.Theme))).Append(" mood.");

        return sb.ToString();
    }

    /// <summary>Trims a trailing period/space so composed clauses join cleanly.</summary>
    private static string Clean(string s) => s.TrimEnd('.', ' ');

    /// <summary>Upper-cases the first character so each composed clause reads as a sentence.</summary>
    private static string Capitalize(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s[1..];

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

        // Softened retry, still POSITIVE-only: "serene and wholesome" replaces "nothing graphic/violent"
        // (which would bias FLUX toward it), and "a single coherent scene" replaces the collage negative.
        var sb = new StringBuilder();
        sb.Append("An abstract, atmospheric scene evoking ")
          .Append(Clean(string.IsNullOrWhiteSpace(concept.Theme) ? concept.Concept : concept.Theme))
          .Append('.');

        if (!string.IsNullOrWhiteSpace(concept.Aesthetic))
            sb.Append(" Rendered as ").Append(Clean(concept.Aesthetic)).Append('.');
        if (!string.IsNullOrWhiteSpace(concept.Palette))
            sb.Append(' ').Append(Capitalize(Clean(concept.Palette))).Append(" palette.");

        sb.Append(" A calm, gentle, symbolic mood piece — soft light, natural forms, serene and wholesome, a single coherent scene.");

        return sb.ToString();
    }
}
