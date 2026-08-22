namespace Hark.App;

/// <summary>
/// The conversation model shared across windows: the combined, speaker-attributed transcript
/// plus a per-speaker index. Finalized segments are appended here; the CONVERSATION overlay and
/// each <see cref="SpeakerWindow"/> render from it and refresh on <see cref="Changed"/>.
/// <para>All members are expected to be used on the WPF UI thread.</para>
/// </summary>
public sealed class ConversationStore
{
    #region Constants

    /// <summary>Label used when diarization didn't attribute a speaker.</summary>
    public const string DefaultSpeaker = "Speaker";

    #endregion

    #region Nested Types

    /// <summary>A single finalized line attributed to a speaker.</summary>
    public readonly record struct Entry(string Speaker, string Text);

    #endregion

    #region Fields

    /// <summary>The full conversation in order, each line tagged with its speaker.</summary>
    private readonly List<Entry> _all = new();

    /// <summary>Finalized lines grouped by normalized speaker name.</summary>
    private readonly Dictionary<string, List<string>> _bySpeaker = new(StringComparer.OrdinalIgnoreCase);

    #endregion

    #region Properties

    /// <summary>The full conversation in order, each line tagged with its speaker.</summary>
    public IReadOnlyList<Entry> All => _all;

    /// <summary>The distinct speakers discovered so far.</summary>
    public IReadOnlyCollection<string> Speakers => _bySpeaker.Keys;

    /// <summary>
    /// Monotonically increasing content version. Bumped on every change (new line or clear).
    /// Consumers (e.g. the summary generator) can cache a result against the revision it was
    /// produced from and skip regenerating while the revision is unchanged.
    /// </summary>
    public int Revision { get; private set; }

    #endregion

    #region Events

    /// <summary>Raised whenever the conversation content changes (new line or clear).</summary>
    public event Action? Changed;

    /// <summary>Raised the first time a given speaker is seen.</summary>
    public event Action<string>? SpeakerAdded;

    #endregion

    #region Methods

    /// <summary>Appends a finalized line for the given (normalized) speaker.</summary>
    /// <param name="speaker">The raw speaker label, or <see langword="null"/> if unattributed.</param>
    /// <param name="text">The finalized line of text.</param>
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

        Revision++;
        Changed?.Invoke();
    }

    /// <summary>The finalized lines attributed to a speaker (empty if unknown).</summary>
    /// <param name="speaker">The speaker to look up.</param>
    /// <returns>The finalized lines for the speaker, or an empty list if the speaker is unknown.</returns>
    public IReadOnlyList<string> LinesFor(string speaker) =>
        _bySpeaker.TryGetValue(Normalize(speaker), out var lines)
            ? lines
            : Array.Empty<string>();

    /// <summary>Resets the conversation (used when a new session starts).</summary>
    public void Clear()
    {
        _all.Clear();
        _bySpeaker.Clear();
        Revision++;
        Changed?.Invoke();
    }

    /// <summary>
    /// Replaces the entire conversation with a new set of entries (used by the offline refinement
    /// pass, which re-diarizes the whole session for more accurate speaker attribution). Raises
    /// <see cref="SpeakerAdded"/> for each distinct speaker and bumps <see cref="Revision"/> once.
    /// </summary>
    /// <param name="entries">The rebuilt, speaker-attributed lines, in order.</param>
    public void Rebuild(IEnumerable<Entry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        _all.Clear();
        _bySpeaker.Clear();

        foreach (var entry in entries)
        {
            var line = entry.Text?.Trim();
            if (string.IsNullOrEmpty(line)) continue;
            var name = Normalize(entry.Speaker);

            _all.Add(new Entry(name, line));
            if (!_bySpeaker.TryGetValue(name, out var lines))
            {
                lines = new List<string>();
                _bySpeaker[name] = lines;
                SpeakerAdded?.Invoke(name);
            }
            lines.Add(line);
        }

        Revision++;
        Changed?.Invoke();
    }

    /// <summary>Normalizes a raw speaker label, mapping blank or "Unknown" labels to <see cref="DefaultSpeaker"/>.</summary>
    /// <param name="speaker">The raw speaker label.</param>
    /// <returns>The normalized speaker label.</returns>
    private static string Normalize(string? speaker) =>
        string.IsNullOrWhiteSpace(speaker) || speaker.Equals("Unknown", StringComparison.OrdinalIgnoreCase)
            ? DefaultSpeaker
            : speaker;

    #endregion
}
