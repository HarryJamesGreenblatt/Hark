namespace Hark.Core.Summarization;

/// <summary>
/// Produces a natural-language recap of a speaker-attributed transcript. Implementations may be
/// cloud-backed (Azure OpenAI) or local, without the rest of the app needing to change.
/// </summary>
public interface ISummarizer
{
    /// <summary>
    /// Produces a topic-pivoted, Teams-Recap-style summary: an overview, per-topic notes (each with
    /// expandable detail bullets), and a flat list of follow-up tasks.
    /// </summary>
    /// <param name="transcript">The speaker-attributed conversation, one line per finalized segment.</param>
    /// <param name="cancellationToken">Cancels an in-flight summarization.</param>
    /// <returns>The structured, topic-pivoted recap.</returns>
    Task<MeetingRecap> SummarizeConversationAsync(string transcript, CancellationToken cancellationToken = default);

    /// <summary>
    /// Produces a people-pivoted recap: one brief per speaker (a one-line characterization plus the
    /// specific points, positions, and commitments attributable to them).
    /// </summary>
    /// <param name="transcript">The speaker-attributed conversation, one line per finalized segment.</param>
    /// <param name="cancellationToken">Cancels an in-flight summarization.</param>
    /// <returns>The structured, people-pivoted recap.</returns>
    Task<SpeakerRecap> SummarizeSpeakersAsync(string transcript, CancellationToken cancellationToken = default);
}
