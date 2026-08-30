using System.Text;

namespace Hark.Oracle.Vision;

/// <summary>
/// Render tier for the diagram class: composes an <see cref="InfographicConcept"/> into a FLUX-idiomatic
/// image prompt (deterministic, no model call). The infographic analogue of <see cref="VisionPromptComposer"/>.
/// <para>
/// The grammar is aligned to Black Forest Labs' FLUX.2 prompting guide (docs.bfl.ml): front-loaded
/// <em>Subject → Style → Context</em>; quoted text for legible typography; plain colour <em>words</em>
/// (never hex — FLUX renders hex strings as visible label text); and <b>no negative prompts</b>
/// (FLUX.2 ignores them). A radial mind-map with an <em>empty centre</em> (so the HAL eye sits at the hub)
/// and up to five labeled nodes around it.
/// </para>
/// </summary>
public static class InfographicPromptComposer
{
    /// <summary>Fallback node colours, assigned by position when a node leaves <see cref="InfographicNode.Color"/> blank.</summary>
    private static readonly string[] Fallback = ["blue", "green", "orange", "purple", "red"];

    /// <summary>Composes an infographic concept into a FLUX-idiomatic still-image prompt.</summary>
    /// <param name="concept">The infographic intent.</param>
    /// <returns>The image-generation prompt.</returns>
    public static string Compose(InfographicConcept concept)
    {
        ArgumentNullException.ThrowIfNull(concept);

        var nodes = (concept.Nodes ?? [])
            .Where(n => !string.IsNullOrWhiteSpace(n.Label))
            .Take(5)
            .ToList();

        var sb = new StringBuilder();
        sb.Append("A clean modern mind-map infographic titled \"").Append(Clean(concept.Title))
          .Append("\", flat vector style on a solid near-black background. ");
        sb.Append("A large empty circular space in the centre keeps the middle clear. ");

        if (nodes.Count > 0)
        {
            sb.Append(nodes.Count).Append(nodes.Count == 1 ? " rounded node is" : " rounded nodes are")
              .Append(" spaced evenly in a ring around the empty centre, each joined to the middle by a thin glowing line: ");
            for (int i = 0; i < nodes.Count; i++)
            {
                var color = string.IsNullOrWhiteSpace(nodes[i].Color) ? Fallback[i % Fallback.Length] : Clean(nodes[i].Color);
                sb.Append("a ").Append(color).Append(" node labeled \"").Append(Clean(nodes[i].Label)).Append('"');
                sb.Append(i == nodes.Count - 1 ? ". " : i == nodes.Count - 2 ? ", and " : ", ");
            }
        }

        sb.Append("Crisp white sans-serif labels, softly glowing nodes, generous spacing, minimalist, ")
          .Append("plenty of empty space in the middle.");
        return sb.ToString();
    }

    /// <summary>Trims trailing punctuation/space so composed clauses join cleanly.</summary>
    private static string Clean(string s) => s.Trim().TrimEnd('.', ' ');
}
