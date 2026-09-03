namespace Hark.Oracle.Vision;

/// <summary>One node of a radial mind-map: a short label, a colour word, and a hover detail.</summary>
/// <param name="Label">A short label (1-4 words) naming a facet of the topic.</param>
/// <param name="Color">A plain colour word for the node (blue/green/orange/purple/red) — never a hex code (FLUX leaks hex as text).</param>
/// <param name="Detail">One concise sentence expanding the label, revealed when the node is hovered; may be empty.</param>
public sealed record InfographicNode(string Label, string Color, string Detail = "");

/// <summary>
/// A radial infographic intent distilled from a window of live conversation — the diagram analogue of
/// <see cref="VisualConcept"/> for explanatory / conceptual passages. A central <see cref="Title"/> with
/// up to five labeled <see cref="Nodes"/> radiating around an <em>empty centre</em>, so the Oracle's eye sits
/// at the hub. In the app this is <b>rendered natively</b> by the overlay (deterministic layout, exact
/// hub); the Spike can instead compose it into a FLUX prompt via <see cref="InfographicPromptComposer"/>.
/// </summary>
/// <param name="Title">The central topic of this beat, a few words.</param>
/// <param name="Nodes">Up to five short labeled facets radiating around the centre.</param>
public sealed record InfographicConcept(string Title, IReadOnlyList<InfographicNode> Nodes);
