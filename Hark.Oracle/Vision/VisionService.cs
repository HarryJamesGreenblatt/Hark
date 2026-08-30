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
    private readonly InfographicDesigner? _infographic;

    #endregion

    #region Constructor(s)

    /// <summary>Creates a vision service from its two tiers.</summary>
    /// <param name="designer">The concept tier (required).</param>
    /// <param name="renderer">The render tier, or <see langword="null"/> for concept-only (the judgment spike).</param>
    /// <param name="infographic">Optional diagram tier; when set, conjuring routes through the infographic class instead of the scene class.</param>
    public VisionService(ConceptDesigner designer, VisionRenderer? renderer = null, InfographicDesigner? infographic = null)
    {
        _designer = designer ?? throw new ArgumentNullException(nameof(designer));
        _renderer = renderer;
        _infographic = infographic;
    }

    #endregion

    #region Methods

    /// <summary>
    /// Conjures a visual concept from a conversation window, and — when a renderer is configured — the image.
    /// Returns <see langword="null"/> when the window yields no concept.
    /// </summary>
    /// <param name="transcriptWindow">Recent dialogue, one line per finalized segment.</param>
    /// <param name="cancellationToken">Cancels the in-flight request.</param>
    public Task<VisionResult?> ConjureAsync(string transcriptWindow, CancellationToken cancellationToken = default)
        => ConjureAsync(transcriptWindow, previousVision: null, onConcept: null, cancellationToken);

    /// <summary>
    /// Conjures a concept and, when a renderer is configured, the image. <paramref name="previousVision"/>
    /// (the concept currently on screen) lets the Oracle deliberately conjure a visibly different scene
    /// for the new beat instead of repeating itself. <paramref name="onConcept"/> fires the moment the
    /// (fast) concept lands — before the (slow) render — so a caller can surface it immediately as a
    /// buffer while the image catches up.
    /// </summary>
    /// <param name="transcriptWindow">Recent dialogue, one line per finalized segment.</param>
    /// <param name="previousVision">The concept currently displayed, to differ from; or <see langword="null"/>.</param>
    /// <param name="onConcept">Invoked with the concept as soon as it is designed, before rendering; or <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels the in-flight request.</param>
    public async Task<VisionResult?> ConjureAsync(
        string transcriptWindow,
        string? previousVision,
        Action<VisualConcept>? onConcept = null,
        CancellationToken cancellationToken = default)
    {
        var concept = await _designer.DesignAsync(transcriptWindow, previousVision, cancellationToken).ConfigureAwait(false);
        if (concept is null) return null;

        onConcept?.Invoke(concept);   // surface the fast concept before the slow render

        var prompt = VisionPromptComposer.Compose(concept);
        byte[]? image = _renderer is null
            ? null
            : await _renderer.RenderAsync(prompt, cancellationToken).ConfigureAwait(false);

        return new VisionResult(concept, prompt, image);
    }

    /// <summary>
    /// Conjures the diagram class: designs an <see cref="InfographicConcept"/> (structured title + nodes)
    /// from the window, for the host to render NATIVELY. No image model is called — a diagram is structured
    /// data, drawn deterministically by the UI, not generated as a picture. Returns null when the diagram
    /// tier isn't configured or the window yields nothing.
    /// </summary>
    /// <param name="transcriptWindow">Recent dialogue, one line per finalized segment.</param>
    /// <param name="cancellationToken">Cancels the in-flight request.</param>
    public Task<InfographicConcept?> ConjureDiagramAsync(string transcriptWindow, CancellationToken cancellationToken = default)
        => _infographic is null
            ? Task.FromResult<InfographicConcept?>(null)
            : _infographic.DesignAsync(transcriptWindow, cancellationToken);

    #endregion
}
