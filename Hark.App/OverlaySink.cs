using System.Windows.Threading;
using Hark.Core.Output;
using Hark.Core.Transcription;

namespace Hark.App;

/// <summary>
/// Keep (overlay) — renders transcript segments to the on-screen <see cref="OverlayWindow"/>.
/// Recognition events arrive on background threads, so every update is marshaled onto the WPF
/// dispatcher. Interim segments update the live line; final segments are committed to history.
/// </summary>
public sealed class OverlaySink : ITranscriptSink
{
    private readonly OverlayWindow _overlay;
    private readonly Dispatcher _dispatcher;

    public OverlaySink(OverlayWindow overlay)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        _overlay = overlay;
        _dispatcher = overlay.Dispatcher;
    }

    /// <inheritdoc />
    public void Write(TranscriptSegment segment)
    {
        if (segment.IsFinal)
            _dispatcher.BeginInvoke(() => _overlay.CommitFinal(segment.Text));
        else
            _dispatcher.BeginInvoke(() => _overlay.ShowInterim(segment.Text));
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
