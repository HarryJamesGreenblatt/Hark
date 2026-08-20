namespace Hark.Core.Summarization;

/// <summary>
/// The flavor of recap to produce. The default is <see cref="Teams"/> (a meeting-style recap);
/// additional styles let the same transcript be re-summarized differently.
/// </summary>
public enum SummaryStyle
{
    /// <summary>Teams-style meeting recap: brief overview, key points, and action items.</summary>
    Teams,

    /// <summary>A single concise narrative paragraph.</summary>
    Narrative,

    /// <summary>A short summary of what each speaker contributed.</summary>
    PerSpeaker,
}
