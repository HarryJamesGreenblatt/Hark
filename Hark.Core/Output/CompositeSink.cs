using Hark.Core.Transcription;

namespace Hark.Core.Output;

/// <summary>
/// Keep (composite) — fans a single segment out to several sinks, so a session can stream to
/// stdout while simultaneously persisting to a text file, JSON Lines, and/or SRT.
/// </summary>
public sealed class CompositeSink : ITranscriptSink
{
    private readonly IReadOnlyList<ITranscriptSink> _sinks;

    /// <summary>Wraps the supplied sinks.</summary>
    /// <param name="sinks">The sinks to broadcast to.</param>
    public CompositeSink(params ITranscriptSink[] sinks) => _sinks = sinks;

    /// <inheritdoc />
    public void Write(TranscriptSegment segment)
    {
        foreach (var sink in _sinks)
            sink.Write(segment);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        foreach (var sink in _sinks)
            await sink.DisposeAsync().ConfigureAwait(false);
    }
}
