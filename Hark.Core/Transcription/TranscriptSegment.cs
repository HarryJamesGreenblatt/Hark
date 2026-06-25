namespace Hark.Core.Transcription;

/// <summary>
/// A single unit of recognized speech, emitted by an <see cref="ISpeechTranscriber"/>.
/// Interim segments are provisional hypotheses; final segments are stable.
/// </summary>
/// <param name="Text">The recognized text.</param>
/// <param name="IsFinal">True when the recognizer has finalized this segment.</param>
/// <param name="Offset">Start time of the segment relative to the start of the session.</param>
/// <param name="Duration">Duration of the recognized audio for this segment.</param>
public readonly record struct TranscriptSegment(
    string Text,
    bool IsFinal,
    TimeSpan Offset,
    TimeSpan Duration);
