using Hark.Core.Transcription;

namespace Hark.Core.Output;

/// <summary>
/// Keep (file) — appends finalized segments to a rolling plain-text transcript, one line each.
/// Interim segments are ignored so the file stays a clean, durable record. Writes are flushed
/// immediately so the transcript is tail-able while a session is running.
/// </summary>
public sealed class RollingFileSink : ITranscriptSink
{
    #region Fields

    /// <summary>The underlying writer appending to the destination transcript file.</summary>
    private readonly StreamWriter _writer;

    /// <summary>Whether each line is prefixed with the segment offset.</summary>
    private readonly bool _timestamps;

    /// <summary>Serializes access to <see cref="_writer"/> across concurrent segment writes.</summary>
    private readonly Lock _gate = new();

    #endregion

    #region Constructor(s)

    /// <summary>Opens (or creates) the transcript file for appending.</summary>
    /// <param name="path">Destination file path.</param>
    /// <param name="timestamps">When true, each line is prefixed with the segment offset.</param>
    public RollingFileSink(string path, bool timestamps = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        _writer = new StreamWriter(path, append: true) { AutoFlush = true };
        _timestamps = timestamps;
    }

    #endregion

    #region Methods

    /// <inheritdoc />
    public void Write(TranscriptSegment segment)
    {
        if (!segment.IsFinal) return;

        lock (_gate)
        {
            if (_timestamps)
                _writer.WriteLine($"[{segment.Offset:hh\\:mm\\:ss}] {segment.Text}");
            else
                _writer.WriteLine(segment.Text);
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        lock (_gate) _writer.Dispose();
        return ValueTask.CompletedTask;
    }

    #endregion
}
