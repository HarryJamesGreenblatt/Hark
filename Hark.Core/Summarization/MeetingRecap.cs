using System.Text.Json.Serialization;

namespace Hark.Core.Summarization;

/// <summary>
/// A structured, Teams-Recap-style meeting summary: a short <see cref="Title"/> headline, a short
/// overview, the meeting broken into the distinct <see cref="Topics"/> that were actually discussed
/// (each expandable into detail bullets), and a flat list of <see cref="FollowUps"/>. Produced by
/// <see cref="ISummarizer.SummarizeConversationAsync"/>.
/// </summary>
public sealed record MeetingRecap(
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("overview")] string Overview,
    [property: JsonPropertyName("topics")] IReadOnlyList<RecapTopic> Topics,
    [property: JsonPropertyName("followUps")] IReadOnlyList<RecapFollowUp> FollowUps);

/// <summary>
/// One discussion topic within a <see cref="MeetingRecap"/>: a short <see cref="Title"/>, a one-line
/// <see cref="Summary"/>, and the <see cref="Details"/> bullets revealed when the topic is expanded.
/// </summary>
public sealed record RecapTopic(
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("summary")] string Summary,
    [property: JsonPropertyName("details")] IReadOnlyList<string> Details);

/// <summary>
/// A single follow-up action item from a <see cref="MeetingRecap"/>: what will be done and the
/// anonymous speaker label (<see cref="Owner"/>) responsible, or <c>null</c> if unassigned.
/// </summary>
public sealed record RecapFollowUp(
    [property: JsonPropertyName("task")] string Task,
    [property: JsonPropertyName("owner")] string? Owner);
