using System.Text;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace Hark.App;

/// <summary>
/// The on-screen captions bar — a borderless, topmost, translucent window styled after Windows
/// Live Captions, but actually useful: the rolling transcript is selectable/copyable and the
/// window is resizable + scrollable. Driven by the HARK pipeline via <c>OverlaySink</c>.
/// </summary>
public partial class OverlayWindow : Window
{
    /// <summary>How many finalized lines to retain in the scrollback above the live interim line.</summary>
    private const int MaxHistoryLines = 200;

    private readonly LinkedList<string> _history = new();
    private string _interim = string.Empty;

    private static readonly Brush FinalBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
    private static readonly Brush InterimBrush = new SolidColorBrush(Color.FromRgb(0xC8, 0xCC, 0xD0));
    private static readonly Brush RunningDot = new SolidColorBrush(Color.FromRgb(0x46, 0xD1, 0x7A));
    private static readonly Brush IdleDot = new SolidColorBrush(Color.FromRgb(0x5F, 0x63, 0x68));

    public OverlayWindow()
    {
        InitializeComponent();
        FinalBrush.Freeze();
        InterimBrush.Freeze();
        RunningDot.Freeze();
        IdleDot.Freeze();

        Loaded += (_, _) => PositionAtBottomCenter();
        DragHandle.MouseLeftButtonDown += OnDragHandlePressed;

        CopyItem.Click += (_, _) => CopySelectionOrAll();
        CopyAllItem.Click += (_, _) => CopyAll();
        CloseButton.Click += (_, _) => CloseRequested?.Invoke();
    }

    /// <summary>Raised when the user clicks the overlay's close (✕) button.</summary>
    public event Action? CloseRequested;

    /// <summary>Updates the live (interim) hypothesis line.</summary>
    public void ShowInterim(string text)
    {
        _interim = text ?? string.Empty;
        Render();
    }

    /// <summary>Commits a finalized line to the rolling history and clears the interim line.</summary>
    public void CommitFinal(string text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            _history.AddLast(text.Trim());
            while (_history.Count > MaxHistoryLines) _history.RemoveFirst();
        }
        _interim = string.Empty;
        Render();
    }

    /// <summary>Reflects capture state in the status dot and hint text.</summary>
    public void SetRunning(bool running)
    {
        StatusDot.Fill = running ? RunningDot : IdleDot;
        HintText.Text = running ? "Listening · Ctrl+Win+H to stop" : "Idle · Ctrl+Win+H to start";
    }

    /// <summary>
    /// Surfaces a diagnostic message (e.g. a failed start or recognizer error) directly in the
    /// caption area so it persists, instead of relying on a fleeting tray balloon.
    /// </summary>
    public void ShowStatus(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        HintText.Text = "Error · Ctrl+Win+H to retry";
        _interim = message.Trim();
        Render();
    }

    /// <summary>Clears all caption text (used when stopping a session).</summary>
    public void ClearText()
    {
        _history.Clear();
        _interim = string.Empty;
        Render();
    }

    private void Render()
    {
        // Preserve the user's selection length so live interim updates don't fight active copying.
        bool hadSelection = !CaptionBox.Selection.IsEmpty;

        var para = new Paragraph();
        bool first = true;
        foreach (var line in _history)
        {
            if (!first) para.Inlines.Add(new LineBreak());
            para.Inlines.Add(new Run(line) { Foreground = FinalBrush });
            first = false;
        }

        if (_interim.Length > 0)
        {
            if (!first) para.Inlines.Add(new LineBreak());
            para.Inlines.Add(new Run(_interim) { Foreground = InterimBrush });
        }

        CaptionDoc.Blocks.Clear();
        CaptionDoc.Blocks.Add(para);

        // Keep the newest text in view unless the user is actively selecting older text.
        if (!hadSelection) CaptionBox.ScrollToEnd();
    }

    /// <summary>The entire transcript (history + current interim) as plain text.</summary>
    private string FullText()
    {
        var sb = new StringBuilder();
        foreach (var line in _history) sb.AppendLine(line);
        if (_interim.Length > 0) sb.AppendLine(_interim);
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

    private void PositionAtBottomCenter()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Left + (area.Width - Width) / 2;
        Top = area.Bottom - Height - 48;
    }

    /// <summary>Shows the overlay and forces it above other topmost windows.</summary>
    public void ShowAndActivate()
    {
        Show();
        // Re-assert topmost so the bar surfaces above other always-on-top windows (e.g. browsers).
        Topmost = false;
        Topmost = true;
        Activate();
    }

    private void OnDragHandlePressed(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }
}
