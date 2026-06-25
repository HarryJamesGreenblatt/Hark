using System.Text.Json;
using Hark.Core.Transcription;

namespace Hark.Core.Output;

/// <summary>
/// Keep (JSON) — writes finalized segments as JSON Lines (one JSON object per line). This format
/// is append-friendly and trivially consumable by downstream agents and tools without needing to
/// parse a single large array that is only valid once the session ends.
/// </summary>
public sealed class JsonSink : ITranscriptSink
{
    private readonly StreamWriter _writer;
    private readonly Lock _gate = new();

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    /// <summary>Opens (or creates) the JSON Lines file for appending.</summary>
    /// <param name="path">Destination file path.</param>
    public JsonSink(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        _writer = new StreamWriter(path, append: true) { AutoFlush = true };
    }

    /// <inheritdoc />
    public void Write(TranscriptSegment segment)
    {
        if (!segment.IsFinal) return;

        var record = new
        {
            offset = segment.Offset.TotalSeconds,
            duration = segment.Duration.TotalSeconds,
            text = segment.Text,
        };

        lock (_gate)
            _writer.WriteLine(JsonSerializer.Serialize(record, Options));
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        lock (_gate) _writer.Dispose();
        return ValueTask.CompletedTask;
    }
}
