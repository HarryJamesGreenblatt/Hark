using System.Text;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace Hark.App;

/// <summary>
/// A dedicated page for a single speaker, opened from a pill in the CONVERSATION index.
/// Renders that speaker's finalized lines from the shared <see cref="ConversationStore"/> and
/// refreshes live as the conversation grows.
/// </summary>
public partial class SpeakerWindow : Window
{
    #region Fields

    /// <summary>Text color for the speaker's finalized lines.</summary>
    private static readonly Brush FinalBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));

    /// <summary>The shared conversation model this page renders from.</summary>
    private readonly ConversationStore _store;

    /// <summary>The speaker this page is bound to.</summary>
    private readonly string _speaker;

    #endregion

    #region Properties

    /// <summary>The speaker this page is bound to.</summary>
    public string Speaker => _speaker;

    #endregion

    #region Constructor(s)

    /// <summary>Initializes the page for the given speaker and subscribes to store changes.</summary>
    /// <param name="store">The shared conversation model to render from.</param>
    /// <param name="speaker">The speaker whose lines this page displays.</param>
    public SpeakerWindow(ConversationStore store, string speaker)
    {
        ArgumentNullException.ThrowIfNull(store);
        InitializeComponent();

        _store = store;
        _speaker = speaker;

        Title = $"HARK — {speaker}";
        SpeakerLabel.Text = speaker;

        DragHandle.MouseLeftButtonDown += OnDragHandlePressed;
        CopyItem.Click += (_, _) => CopySelectionOrAll();
        CopyAllItem.Click += (_, _) => CopyAll();
        CloseButton.Click += (_, _) => Close();

        _store.Changed += OnStoreChanged;
        Closed += (_, _) => _store.Changed -= OnStoreChanged;

        Render();
    }

    #endregion

    #region Methods

    /// <summary>Re-renders the page whenever the conversation store changes.</summary>
    private void OnStoreChanged() => Dispatcher.BeginInvoke(Render);

    /// <summary>Rebuilds the caption document from the speaker's finalized lines.</summary>
    private void Render()
    {
        bool hadSelection = !CaptionBox.Selection.IsEmpty;

        var para = new Paragraph();
        bool first = true;
        foreach (var line in _store.LinesFor(_speaker))
        {
            if (!first) para.Inlines.Add(new LineBreak());
            para.Inlines.Add(new Run(line) { Foreground = FinalBrush });
            first = false;
        }

        CaptionDoc.Blocks.Clear();
        CaptionDoc.Blocks.Add(para);

        if (!hadSelection) CaptionBox.ScrollToEnd();
    }

    /// <summary>The speaker's entire transcript as plain text.</summary>
    /// <returns>The speaker's finalized lines joined by newlines.</returns>
    private string FullText()
    {
        var sb = new StringBuilder();
        foreach (var line in _store.LinesFor(_speaker)) sb.AppendLine(line);
        return sb.ToString().TrimEnd();
    }

    /// <summary>Copies the current text selection, or the entire transcript if nothing is selected.</summary>
    private void CopySelectionOrAll()
    {
        if (!CaptionBox.Selection.IsEmpty)
            CaptionBox.Copy();
        else
            CopyAll();
    }

    /// <summary>Copies the speaker's entire transcript to the clipboard.</summary>
    private void CopyAll()
    {
        var text = FullText();
        if (text.Length > 0) Clipboard.SetText(text);
    }

    /// <summary>Begins moving the window when the drag handle is pressed.</summary>
    /// <param name="sender">Unused.</param>
    /// <param name="e">The mouse button event arguments.</param>
    private void OnDragHandlePressed(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    #endregion
}
