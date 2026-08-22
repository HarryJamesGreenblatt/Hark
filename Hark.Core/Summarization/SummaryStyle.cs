namespace Hark.Core.Summarization;

/// <summary>
/// The flavor of recap to produce. Both styles are structured and expandable: <see cref="Conversation"/>
/// pivots on topics, <see cref="Speakers"/> pivots on people. The enum member names are shown directly
/// in the overlay's recap-style picker.
/// </summary>
public enum SummaryStyle
{
    /// <summary>Topic-pivoted meeting recap: overview, expandable per-topic notes, and follow-up tasks.</summary>
    Conversation,

    /// <summary>People-pivoted recap: one expandable card per speaker (characterization + their points).</summary>
    Speakers,
}
