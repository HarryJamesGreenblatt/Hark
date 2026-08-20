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
    private static readonly Brush FinalBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));

    private readonly ConversationStore _store;
    private readonly string _speaker;

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

    /// <summary>The speaker this page is bound to.</summary>
    public string Speaker => _speaker;

    private void OnStoreChanged() => Dispatcher.BeginInvoke(Render);

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

    private string FullText()
    {
        var sb = new StringBuilder();
        foreach (var line in _store.LinesFor(_speaker)) sb.AppendLine(line);
        return sb.ToString().TrimEnd();
    }

    private void CopySelectionOrAll()
    {
        if (!CaptionBox.Selection.IsEmpty)
            CaptionBox.Copy();
        else
            CopyAll();
    }

    private void CopyAll()
    {
        var text = FullText();
        if (text.Length > 0) Clipboard.SetText(text);
    }

    private void OnDragHandlePressed(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }
}
