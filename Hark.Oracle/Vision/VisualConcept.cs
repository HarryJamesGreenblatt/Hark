namespace Hark.Oracle.Vision;

/// <summary>Whether the design echoes the conversation's feeling or pushes against it.</summary>
public enum ConceptStance
{
    /// <summary>The image echoes and reinforces the feeling (Rizzo Ch. 4).</summary>
    Underscore,

    /// <summary>The image pushes against the feeling as visual irony — it knows something the words don't (Glebas Ch. 11).</summary>
    Contrast,
}

/// <summary>
/// The art director's contribution: one iconic, metaphor-not-literal image intent distilled from a
/// window of live conversation — the seed the renderer turns into a picture. It is the concept, not
/// the realisation (Rizzo Ch. 1: the Production Designer owns the concept; the composer owns the render).
/// <para>
/// Grounded in Michael Rizzo, <em>The Art Direction Handbook</em> Ch. 4 (land one central visual
/// concept) and Francis Glebas, <em>Directing the Story</em> Ch. 7/9/10/11/13 (direct the eye · make
/// images speak · convey meaning · irony · aim for the heart) — distilled from the transformative
/// <c>reference/</c> abridgments in <c>github.com/HarryJamesGreenblatt/sequitur_studios</c> @ <c>4150645</c>.
/// </para>
/// </summary>
/// <param name="Theme">The one master feeling the passage is about — the compass (Glebas Ch. 13).</param>
/// <param name="Concept">One evocative sentence — iconic, not literal (Rizzo Ch. 4).</param>
/// <param name="Stance">Echo the feeling, or push against it as irony (Rizzo Ch. 4 · Glebas Ch. 11).</param>
/// <param name="StanceReason">One line: why underscore or contrast.</param>
/// <param name="Motifs">2-4 icons that cohere into ONE image, never a collage (Rizzo's research wall).</param>
/// <param name="Composition">Focal/shape intent: one clear centre, simple shape, negative space (Glebas Ch. 7).</param>
/// <param name="Aesthetic">A recognizable visual idiom carrying a whole look in a few words (Rizzo Ch. 3).</param>
/// <param name="Palette">Colour as emotional temperature, its power drawn from a contrast (Glebas Ch. 13).</param>
public sealed record VisualConcept(
    string Theme,
    string Concept,
    ConceptStance Stance,
    string StanceReason,
    IReadOnlyList<string> Motifs,
    string Composition,
    string Aesthetic,
    string Palette);
