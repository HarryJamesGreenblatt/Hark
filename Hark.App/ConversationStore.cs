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

    /// <summary>
    /// Persistent acoustic-label → display-name map. Because the streaming engine keeps emitting the same
    /// <c>Guest-N</c> for a given voice, a rename must follow FUTURE utterances of that label — not just
    /// rewrite past ones — so new lines are attributed to the chosen name instead of re-spawning Guest-N.
    /// </summary>
    private readonly Dictionary<string, string> _aliases = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every raw (acoustic) label seen, so a rename can re-point all labels currently showing a name.</summary>
    private readonly HashSet<string> _seenRaw = new(StringComparer.OrdinalIgnoreCase);

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
        var raw = Normalize(speaker);
        _seenRaw.Add(raw);
        var name = ResolveAlias(raw);   // a renamed acoustic label follows FUTURE utterances, not just past ones
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

    /// <summary>Resolves a raw (acoustic) label to the display name it currently maps to (itself if unaliased).</summary>
    /// <param name="rawLabel">The raw engine label (e.g. <c>Guest-2</c>), or <see langword="null"/> if unattributed.</param>
    /// <returns>The current display name for that label.</returns>
    public string ResolveDisplay(string? rawLabel) => ResolveAlias(Normalize(rawLabel));

    /// <summary>Maps a normalized raw label through the alias table (identity when no alias exists).</summary>
    private string ResolveAlias(string rawNormalized) =>
        _aliases.TryGetValue(rawNormalized, out var display) ? display : rawNormalized;

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
        _aliases.Clear();
        _seenRaw.Clear();
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
        _aliases.Clear();
        _seenRaw.Clear();

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

    /// <summary>
    /// Renames a speaker globally: every line attributed to <paramref name="oldName"/> is re-tagged to
    /// <paramref name="newName"/>, merging into it (in conversation order) if that speaker already
    /// exists. Returns <see langword="false"/> — a no-op — when the new name is blank, the speaker is
    /// unknown, or the names are equal. On success bumps <see cref="Revision"/> and raises <see cref="Changed"/>.
    /// </summary>
    /// <param name="oldName">The current speaker label to rename.</param>
    /// <param name="newName">The new label (trimmed; blank is rejected).</param>
    /// <returns><see langword="true"/> if a rename was applied; otherwise <see langword="false"/>.</returns>
    public bool Rename(string oldName, string newName)
    {
        if (string.IsNullOrWhiteSpace(newName)) return false;
        var from = Normalize(oldName);
        var to = newName.Trim();
        if (from.Equals(to, StringComparison.OrdinalIgnoreCase)) return false;

        // Re-point every acoustic label currently displaying as `from` so FUTURE utterances follow the
        // rename (the streaming engine keeps emitting the same Guest-N for that voice).
        bool anyAlias = false;
        foreach (var raw in _seenRaw)
            if (ResolveAlias(raw).Equals(from, StringComparison.OrdinalIgnoreCase))
            {
                _aliases[raw] = to;
                anyAlias = true;
            }

        if (!anyAlias && !_bySpeaker.ContainsKey(from)) return false;

        for (int i = 0; i < _all.Count; i++)
            if (_all[i].Speaker.Equals(from, StringComparison.OrdinalIgnoreCase))
                _all[i] = _all[i] with { Speaker = to };

        // Rebuild the target bucket from the rewritten conversation so a merge keeps chronological order.
        _bySpeaker.Remove(from);
        _bySpeaker.Remove(to);
        var merged = _all.Where(e => e.Speaker.Equals(to, StringComparison.OrdinalIgnoreCase))
                         .Select(e => e.Text).ToList();
        if (merged.Count > 0) _bySpeaker[to] = merged;

        Revision++;
        Changed?.Invoke();
        return true;
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
