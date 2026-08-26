using System.Text;

namespace Hark.Oracle.Vision;

/// <summary>
/// Render — the realisation tier of <c>Oracle.Vision</c>: composes a <see cref="VisualConcept"/> into a
/// well-formed image-generation prompt (deterministic, no model call). The analog of sequitur's
/// <c>build_poster_prompt</c> — it asks for one evocative <em>scene of the world</em>, layering the
/// aesthetic, stance, motifs, composition intent, and palette onto the central concept, and closes with
/// an <b>anti-literalism counter</b> (image backends otherwise read "crystal ball" literally).
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
            sb.Append(" Recurring visual motifs: ").Append(string.Join(", ", concept.Motifs)).Append('.');

        if (!string.IsNullOrWhiteSpace(concept.Composition))
            sb.Append(" Composition: ").Append(Clean(concept.Composition)).Append('.');
        sb.Append(" One clear focal point and strong contrast, set in a real, specific place with real ")
          .Append("light — never an isolated object floating on an empty black background.");

        if (!string.IsNullOrWhiteSpace(concept.Palette))
            sb.Append(" Palette: ").Append(Clean(concept.Palette)).Append('.');

        if (!string.IsNullOrWhiteSpace(concept.Theme))
            sb.Append(" Mood: ").Append(Clean(concept.Theme)).Append('.');

        // Anti-literal counter: without it the backend renders a literal crystal ball / UI / text.
        sb.Append(" Compose it as one real, evocative scene in the world — not a crystal ball, glass ")
          .Append("sphere, screen, UI, text, diagram, or literal depiction of a conversation. Suggest ")
          .Append("the feeling through a single iconic image.");

        return sb.ToString();
    }

    /// <summary>Trims a trailing period/space so composed clauses join cleanly.</summary>
    private static string Clean(string s) => s.TrimEnd('.', ' ');
}
