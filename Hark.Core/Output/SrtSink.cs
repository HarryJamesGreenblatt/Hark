using System.Globalization;
using System.Text;
using Hark.Core.Transcription;

namespace Hark.Core.Output;

/// <summary>
/// Keep (SRT) — emits a SubRip subtitle file. SRT cues are sequentially numbered with
/// <c>HH:MM:SS,mmm</c> start/end timestamps, so finalized segments are buffered and the file is
/// written atomically on dispose (at the end of the session).
/// </summary>
public sealed class SrtSink : ITranscriptSink
{
    #region Fields

    /// <summary>Destination <c>.srt</c> file path.</summary>
    private readonly string _path;

    /// <summary>Buffered finalized segments, written atomically on dispose.</summary>
    private readonly List<TranscriptSegment> _segments = new();

    /// <summary>Serializes access to <see cref="_segments"/> across concurrent segment writes.</summary>
    private readonly Lock _gate = new();

    #endregion

    #region Constructor(s)

    /// <summary>Records the destination path for the subtitle file.</summary>
    /// <param name="path">Destination <c>.srt</c> file path.</param>
    public SrtSink(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = path;
    }

    #endregion

    #region Methods

    /// <inheritdoc />
    public void Write(TranscriptSegment segment)
    {
        if (!segment.IsFinal) return;
        lock (_gate) _segments.Add(segment);
    }

    /// <summary>Formats a timespan as an SRT timestamp (<c>HH:MM:SS,mmm</c>).</summary>
    /// <param name="t">The timespan to format.</param>
    /// <returns>The formatted SRT timestamp.</returns>
    private static string Format(TimeSpan t) =>
        string.Create(CultureInfo.InvariantCulture,
            $"{(int)t.TotalHours:D2}:{t.Minutes:D2}:{t.Seconds:D2},{t.Milliseconds:D3}");

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(_path));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var sb = new StringBuilder();
            for (int i = 0; i < _segments.Count; i++)
            {
                var seg = _segments[i];
                var end = seg.Offset + seg.Duration;

                sb.Append(i + 1).Append('\n');
                sb.Append(Format(seg.Offset)).Append(" --> ").Append(Format(end)).Append('\n');
                sb.Append(seg.Text).Append('\n').Append('\n');
            }

            File.WriteAllText(_path, sb.ToString());
        }

        return ValueTask.CompletedTask;
    }

    #endregion
}
