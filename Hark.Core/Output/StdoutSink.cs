using Hark.Core.Transcription;

namespace Hark.Core.Output;

/// <summary>
/// Keep (stdout) — renders live transcription to the console. Interim segments overwrite the
/// current line for a streaming feel; finalized segments are committed as their own line, which
/// keeps the output clean when piped to another process.
/// </summary>
public sealed class StdoutSink : ITranscriptSink
{
    #region Fields

    /// <summary>Whether provisional hypotheses are shown on a transient line.</summary>
    private readonly bool _interimEnabled;

    /// <summary>The character length of the last interim line written, for erasing it.</summary>
    private int _lastInterimLength;

    /// <summary>The last interim text written, to skip redundant redraws of identical hypotheses.</summary>
    private string? _lastInterimText;

    /// <summary>Serializes access to console output across concurrent segment writes.</summary>
    private readonly Lock _gate = new();

    #endregion

    #region Constructor(s)

    /// <summary>Creates the sink.</summary>
    /// <param name="showInterim">When true, provisional hypotheses are shown on a transient line.</param>
    /// <remarks>
    /// Live interim overwriting relies on carriage-return redraw on an interactive terminal. When
    /// stdout is redirected (piped/captured), '\r' is kept literally and every revision becomes its
    /// own line, so interims are suppressed and only finals are emitted.
    /// </remarks>
    public StdoutSink(bool showInterim = true) =>
        _interimEnabled = showInterim && !Console.IsOutputRedirected;

    #endregion

    #region Methods

    /// <inheritdoc />
    public void Write(TranscriptSegment segment)
    {
        lock (_gate)
        {
            if (segment.IsFinal)
            {
                ClearInterimLine();
                _lastInterimText = null;
                Console.WriteLine(segment.Text);
            }
            else if (_interimEnabled)
            {
                // Azure raises many Recognizing events with identical text; skip redundant redraws.
                if (segment.Text == _lastInterimText) return;
                _lastInterimText = segment.Text;

                // Keep the hypothesis on a single line so the carriage-return redraw stays reliable;
                // an over-wide line wraps and leaves fragments that ClearInterimLine can't erase.
                var line = Fit(segment.Text);
                ClearInterimLine();
                Console.Write(line);
                _lastInterimLength = line.Length;
            }
        }
    }

    /// <summary>Truncates an interim hypothesis to the current console width (with an ellipsis).</summary>
    /// <param name="text">The interim text to fit.</param>
    /// <returns>The text, truncated to the console width if necessary.</returns>
    private static string Fit(string text)
    {
        int max;
        try { max = Console.WindowWidth - 1; }
        catch { return text; } // no console window (e.g. redirected); leave as-is

        if (max <= 1 || text.Length <= max) return text;
        return string.Concat("…", text.AsSpan(text.Length - max + 1));
    }

    /// <summary>Erases the transient interim line so the next write starts clean.</summary>
    private void ClearInterimLine()
    {
        if (_lastInterimLength == 0) return;
        Console.Write('\r');
        Console.Write(new string(' ', _lastInterimLength));
        Console.Write('\r');
        _lastInterimLength = 0;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        lock (_gate) ClearInterimLine();
        return ValueTask.CompletedTask;
    }

    #endregion
}
