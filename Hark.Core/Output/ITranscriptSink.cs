using Hark.Core.Transcription;

namespace Hark.Core.Output;

/// <summary>
/// Keep — a destination for recognized transcript segments. Implementations persist or
/// surface segments (stdout, rolling text file, JSON, SRT). A sink may choose to render
/// interim segments (live feedback) and/or final segments (durable record).
/// </summary>
public interface ITranscriptSink : IAsyncDisposable
{
    /// <summary>Handles a single recognized segment.</summary>
    void Write(TranscriptSegment segment);
}
