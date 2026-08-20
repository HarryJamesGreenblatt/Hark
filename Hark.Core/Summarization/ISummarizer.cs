namespace Hark.Core.Summarization;

/// <summary>
/// Produces a natural-language recap of a speaker-attributed transcript. Implementations may be
/// cloud-backed (Azure OpenAI) or local, without the rest of the app needing to change.
/// </summary>
public interface ISummarizer
{
    /// <summary>
    /// Summarizes the given transcript in the requested style.
    /// </summary>
    /// <param name="transcript">
    /// The conversation, one line per finalized segment, ideally prefixed with the speaker
    /// (e.g. <c>Guest-1: hello there</c>).
    /// </param>
    /// <param name="style">The recap style to produce.</param>
    /// <param name="cancellationToken">Cancels an in-flight summarization.</param>
    /// <returns>The recap text.</returns>
    Task<string> SummarizeAsync(string transcript, SummaryStyle style, CancellationToken cancellationToken = default);
}
