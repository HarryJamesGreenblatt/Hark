using System.Text.Json.Serialization;

namespace Hark.Core.Summarization;

/// <summary>
/// A people-pivoted recap: one <see cref="SpeakerBrief"/> per speaker who took part. The people-side
/// counterpart to <see cref="MeetingRecap"/> (which pivots on topics). Produced by
/// <see cref="ISummarizer.SummarizeSpeakersAsync"/>.
/// </summary>
public sealed record SpeakerRecap(
    [property: JsonPropertyName("speakers")] IReadOnlyList<SpeakerBrief> Speakers);

/// <summary>
/// One speaker's contribution within a <see cref="SpeakerRecap"/>: the anonymous <see cref="Speaker"/>
/// label, a one-line <see cref="Summary"/> characterizing their role/stance, and the <see cref="Points"/>
/// bullets revealed when the card is expanded.
/// </summary>
public sealed record SpeakerBrief(
    [property: JsonPropertyName("speaker")] string Speaker,
    [property: JsonPropertyName("summary")] string Summary,
    [property: JsonPropertyName("points")] IReadOnlyList<string> Points);
