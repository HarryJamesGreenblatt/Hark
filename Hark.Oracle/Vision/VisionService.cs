namespace Hark.Oracle.Vision;

/// <summary>The output of one conjuring: the concept, the composed prompt, and (when a renderer is wired) the image.</summary>
/// <param name="Concept">The art director's visual concept.</param>
/// <param name="Prompt">The composed image-generation prompt.</param>
/// <param name="Image">The rendered PNG bytes, or <see langword="null"/> when no renderer is configured (concept-only).</param>
public sealed record VisionResult(VisualConcept Concept, string Prompt, byte[]? Image);

/// <summary>
/// <c>Oracle.Vision</c> — the augmentation service: turns a window of live conversation into an evocative
/// image. Orchestrates the two tiers — <see cref="ConceptDesigner"/> (the art-director judgment) then
/// <see cref="VisionPromptComposer"/> + <see cref="VisionRenderer"/> (the realisation). The renderer is
/// optional so the concept judgment can be exercised before a gpt-image deployment exists.
/// </summary>
public sealed class VisionService
{
    #region Fields

    private readonly ConceptDesigner _designer;
    private readonly VisionRenderer? _renderer;

    #endregion

    #region Constructor(s)

    /// <summary>Creates a vision service from its two tiers.</summary>
    /// <param name="designer">The concept tier (required).</param>
    /// <param name="renderer">The render tier, or <see langword="null"/> for concept-only (the judgment spike).</param>
    public VisionService(ConceptDesigner designer, VisionRenderer? renderer = null)
    {
        _designer = designer ?? throw new ArgumentNullException(nameof(designer));
        _renderer = renderer;
    }

    #endregion

    #region Methods

    /// <summary>
    /// Conjures a visual concept from a conversation window, and — when a renderer is configured — the image.
    /// Returns <see langword="null"/> when the window yields no concept.
    /// </summary>
    /// <param name="transcriptWindow">Recent dialogue, one line per finalized segment.</param>
    /// <param name="cancellationToken">Cancels the in-flight request.</param>
    public async Task<VisionResult?> ConjureAsync(string transcriptWindow, CancellationToken cancellationToken = default)
    {
        var concept = await _designer.DesignAsync(transcriptWindow, cancellationToken).ConfigureAwait(false);
        if (concept is null) return null;

        var prompt = VisionPromptComposer.Compose(concept);
        byte[]? image = _renderer is null
            ? null
            : await _renderer.RenderAsync(prompt, cancellationToken).ConfigureAwait(false);

        return new VisionResult(concept, prompt, image);
    }

    #endregion
}
