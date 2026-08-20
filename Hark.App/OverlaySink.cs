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
    private readonly OverlayWindow _overlay;
    private readonly ConversationStore _store;
    private readonly Dispatcher _dispatcher;

    public OverlaySink(OverlayWindow overlay, ConversationStore store)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        ArgumentNullException.ThrowIfNull(store);
        _overlay = overlay;
        _store = store;
        _dispatcher = overlay.Dispatcher;
    }

    /// <inheritdoc />
    public void Write(TranscriptSegment segment)
    {
        // Prefix with the anonymous speaker label when diarization attributed one.
        var text = string.IsNullOrEmpty(segment.SpeakerId)
            ? segment.Text
            : $"{segment.SpeakerId}: {segment.Text}";

        if (segment.IsFinal)
        {
            _dispatcher.BeginInvoke(() =>
            {
                _overlay.CommitFinal(text);
                // Feeds the per-speaker pages and the speaker index in the CONVERSATION overlay.
                _store.CommitFinal(segment.SpeakerId, segment.Text);
            });
        }
        else
        {
            _dispatcher.BeginInvoke(() => _overlay.ShowInterim(text));
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
