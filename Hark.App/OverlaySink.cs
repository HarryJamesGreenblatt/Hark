using System.Windows.Threading;
using Hark.Core.Output;
using Hark.Core.Transcription;

namespace Hark.App;

/// <summary>
/// Keep (overlay) — renders transcript segments to the CONVERSATION <see cref="OverlayWindow"/>
/// and records finalized, speaker-attributed lines into the shared <see cref="ConversationStore"/>
/// (which drives the per-speaker pages). Recognition events arrive on background threads, so every
/// update is marshaled onto the WPF dispatcher.
/// </summary>
public sealed class OverlaySink : ITranscriptSink
{
    #region Fields

    /// <summary>The CONVERSATION overlay window that finalized/interim text is rendered to.</summary>
    private readonly OverlayWindow _overlay;

    /// <summary>The shared conversation model that finalized, speaker-attributed lines are recorded into.</summary>
    private readonly ConversationStore _store;

    /// <summary>The WPF dispatcher used to marshal recognition events onto the UI thread.</summary>
    private readonly Dispatcher _dispatcher;

    #endregion

    #region Constructor(s)

    /// <summary>Creates a sink that renders to <paramref name="overlay"/> and records into <paramref name="store"/>.</summary>
    /// <param name="overlay">The overlay window to render transcript segments to.</param>
    /// <param name="store">The shared conversation model to record finalized lines into.</param>
    public OverlaySink(OverlayWindow overlay, ConversationStore store)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        ArgumentNullException.ThrowIfNull(store);
        _overlay = overlay;
        _store = store;
        _dispatcher = overlay.Dispatcher;
    }

    #endregion

    #region Methods

    /// <inheritdoc />
    public void Write(TranscriptSegment segment)
    {
        if (segment.IsFinal)
        {
            _dispatcher.BeginInvoke(() =>
            {
                // Resolve the acoustic label to its current display name so live captions follow renames
                // (a Guest-N already aliased to a real name prefixes with that name, not Guest-N).
                _overlay.CommitFinal(Prefix(segment));
                // Feeds the per-speaker pages and the speaker index in the CAPTIONS overlay.
                _store.CommitFinal(segment.SpeakerId, segment.Text);
            });
        }
        else
        {
            _dispatcher.BeginInvoke(() => _overlay.ShowInterim(Prefix(segment)));
        }
    }

    /// <summary>Prefixes the segment text with its current display name (or none when unattributed).</summary>
    /// <param name="segment">The transcript segment to render.</param>
    /// <returns>The speaker-prefixed line for the overlay.</returns>
    private string Prefix(TranscriptSegment segment) =>
        string.IsNullOrEmpty(segment.SpeakerId)
            ? segment.Text
            : $"{_store.ResolveDisplay(segment.SpeakerId)}: {segment.Text}";

    /// <inheritdoc />
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    #endregion
}
