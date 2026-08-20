namespace Hark.App;

/// <summary>
/// The conversation model shared across windows: the combined, speaker-attributed transcript
/// plus a per-speaker index. Finalized segments are appended here; the CONVERSATION overlay and
/// each <see cref="SpeakerWindow"/> render from it and refresh on <see cref="Changed"/>.
/// <para>All members are expected to be used on the WPF UI thread.</para>
/// </summary>
public sealed class ConversationStore
{
    /// <summary>Label used when diarization didn't attribute a speaker.</summary>
    public const string DefaultSpeaker = "Speaker";

    /// <summary>A single finalized line attributed to a speaker.</summary>
    public readonly record struct Entry(string Speaker, string Text);

    private readonly List<Entry> _all = new();
    private readonly Dictionary<string, List<string>> _bySpeaker = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The full conversation in order, each line tagged with its speaker.</summary>
    public IReadOnlyList<Entry> All => _all;

    /// <summary>The distinct speakers discovered so far.</summary>
    public IReadOnlyCollection<string> Speakers => _bySpeaker.Keys;

    /// <summary>Raised whenever the conversation content changes (new line or clear).</summary>
    public event Action? Changed;

    /// <summary>Raised the first time a given speaker is seen.</summary>
    public event Action<string>? SpeakerAdded;

    /// <summary>Appends a finalized line for the given (normalized) speaker.</summary>
    public void CommitFinal(string? speaker, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        var name = Normalize(speaker);
        var line = text.Trim();

        _all.Add(new Entry(name, line));

        if (!_bySpeaker.TryGetValue(name, out var lines))
        {
            lines = new List<string>();
            _bySpeaker[name] = lines;
            SpeakerAdded?.Invoke(name);
        }
        lines.Add(line);

        Changed?.Invoke();
    }

    /// <summary>The finalized lines attributed to a speaker (empty if unknown).</summary>
    public IReadOnlyList<string> LinesFor(string speaker) =>
        _bySpeaker.TryGetValue(Normalize(speaker), out var lines)
            ? lines
            : Array.Empty<string>();

    /// <summary>Resets the conversation (used when a new session starts).</summary>
    public void Clear()
    {
        _all.Clear();
        _bySpeaker.Clear();
        Changed?.Invoke();
    }

    private static string Normalize(string? speaker) =>
        string.IsNullOrWhiteSpace(speaker) || speaker.Equals("Unknown", StringComparison.OrdinalIgnoreCase)
            ? DefaultSpeaker
            : speaker;
}
