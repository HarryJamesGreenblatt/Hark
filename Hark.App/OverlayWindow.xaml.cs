using System.Text;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Hark.Core.Summarization;

namespace Hark.App;

/// <summary>
/// The on-screen captions bar — a borderless, topmost, translucent window styled after Windows
/// Live Captions, but actually useful: the rolling transcript is selectable/copyable and the
/// window is resizable + scrollable. Driven by the HARK pipeline via <c>OverlaySink</c>.
/// </summary>
public partial class OverlayWindow : Window
{
    #region Constants

    /// <summary>How many finalized lines to retain in the scrollback above the live interim line.</summary>
    private const int MaxHistoryLines = 200;

    #endregion

    #region Nested Types

    /// <summary>Which content the overlay is showing.</summary>
    private enum ViewMode { Captions, Summary }

    #endregion

    #region Fields

    /// <summary>The rolling scrollback of finalized caption lines, capped at <see cref="MaxHistoryLines"/>.</summary>
    private readonly LinkedList<string> _history = new();

    /// <summary>The current live (not yet finalized) hypothesis line.</summary>
    private string _interim = string.Empty;

    /// <summary>Speakers that already have a pill in the index, to avoid duplicates.</summary>
    private readonly HashSet<string> _speakers = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Text color for finalized caption lines.</summary>
    private static readonly Brush FinalBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));

    /// <summary>Text color for the live interim caption line.</summary>
    private static readonly Brush InterimBrush = new SolidColorBrush(Color.FromRgb(0xC8, 0xCC, 0xD0));

    /// <summary>Background color for the selected captions/summary mode button.</summary>
    private static readonly Brush ModeSelectedBg = new SolidColorBrush(Color.FromRgb(0x3B, 0x7D, 0xDD));

    /// <summary>Foreground color for the selected captions/summary mode button.</summary>
    private static readonly Brush ModeSelectedFg = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
    /// <summary>Foreground color for the idle (unselected) captions/summary mode button.</summary>
    private static readonly Brush ModeIdleFg = new SolidColorBrush(Color.FromRgb(0x9A, 0xA0, 0xA6));

    /// <summary>Which content (captions or summary) is currently displayed.</summary>
    private ViewMode _mode = ViewMode.Captions;

    /// <summary>Whether at least one speaker pill has been added to the index.</summary>
    private bool _hasSpeakers;

    /// <summary>Whether capture is currently running, driving the HAL eye and hint text.</summary>
    private bool _running;

    /// <summary>Latest audio level target (0..1), published by the audio callback, eased in the render loop.</summary>
    private double _audioTarget;

    /// <summary>Displayed audio level (0..1), eased toward <see cref="_audioTarget"/> each frame.</summary>
    private double _eyeLevel;

    /// <summary>Timestamp of the previous compositor frame, for dt-based easing.</summary>
    private TimeSpan _lastRenderTime;

    #endregion

    #region Properties

    /// <summary>The recap style currently chosen in the picker.</summary>
    public SummaryStyle SelectedStyle =>
        StylePicker.SelectedItem is SummaryStyle s ? s : SummaryStyle.Conversation;

    #endregion

    #region Events

    /// <summary>Raised when the user clicks the overlay's close (✕) button.</summary>
    public event Action? CloseRequested;

    /// <summary>Raised when the user clicks a speaker pill in the CONVERSATION index.</summary>
    public event Action<string>? SpeakerSelected;

    /// <summary>
    /// Raised when a summary is needed: on switching to SUMMARY mode, or when the recap style
    /// changes while in SUMMARY mode. The host decides whether to serve a cached result or generate.
    /// </summary>
    public event Action<SummaryStyle>? SummaryRequested;

    #endregion

    #region Constructor(s)

    /// <summary>
    /// Initializes the overlay's controls, freezes its static brushes, wires up window/menu/mode
    /// handlers, and starts the compositor-driven render loop for the HAL eye.
    /// </summary>
    public OverlayWindow()
    {
        InitializeComponent();
        FinalBrush.Freeze();
        InterimBrush.Freeze();
        ModeSelectedBg.Freeze();
        ModeSelectedFg.Freeze();
        ModeIdleFg.Freeze();

        Loaded += (_, _) => PositionAsTopBar();
        DragHandle.MouseLeftButtonDown += OnDragHandlePressed;

        CopyItem.Click += (_, _) => CopySelectionOrAll();
        CopyAllItem.Click += (_, _) => CopyAll();
        CloseButton.Click += (_, _) => CloseRequested?.Invoke();

        // Mode switch + recap-style picker.
        CaptionsModeButton.Click += (_, _) => SetMode(ViewMode.Captions);
        SummaryModeButton.Click += (_, _) => SetMode(ViewMode.Summary);

        StylePicker.ItemsSource = Enum.GetValues<SummaryStyle>();
        StylePicker.SelectedItem = SummaryStyle.Conversation;
        StylePicker.SelectionChanged += (_, _) =>
        {
            if (_mode == ViewMode.Summary && StylePicker.SelectedItem is SummaryStyle)
                SummaryRequested?.Invoke(SelectedStyle);
        };

        UpdateModeButtons();
        SetSummaryAvailable(false);   // nothing to summarize until captions arrive

        // Drive the HAL eye from the WPF compositor (~60fps), decoupled from the audio callback
        // rate, so it eases smoothly toward the latest level instead of stepping at ~20 Hz.
        CompositionTarget.Rendering += OnRendering;
        Closed += (_, _) => CompositionTarget.Rendering -= OnRendering;
    }

    #endregion

    #region Methods

    /// <summary>Shows a transient status (e.g. "Generating recap…") in the summary view.</summary>
    /// <param name="message">The status text to display.</param>
    public void SetSummaryBusy(string message)
    {
        ShowPlainText();
        SummaryText.Text = message;
    }

    /// <summary>Renders finished recap text (used only for status/error notes) in the summary view.</summary>
    /// <param name="text">The recap text to display.</param>
    public void SetSummaryText(string text)
    {
        ShowPlainText();
        SummaryText.Text = text ?? string.Empty;
    }

    /// <summary>
    /// Renders a topic-pivoted (Conversation) recap: an overview, expandable per-topic notes, and a
    /// flat list of follow-up tasks. Empty sections are hidden.
    /// </summary>
    /// <param name="recap">The structured recap to display.</param>
    public void SetStructuredRecap(MeetingRecap recap)
    {
        RecapOverview.Text = recap.Overview ?? string.Empty;
        RecapOverview.Visibility = string.IsNullOrWhiteSpace(recap.Overview)
            ? Visibility.Collapsed : Visibility.Visible;

        TopicList.ItemsSource = recap.Topics;
        NotesHeader.Visibility = recap.Topics.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        TaskList.ItemsSource = recap.FollowUps;
        TasksHeader.Visibility = recap.FollowUps.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        ShowConversation();
    }

    /// <summary>
    /// Renders a people-pivoted (Speakers) recap: one expandable card per speaker (a one-line
    /// characterization that expands to reveal their points).
    /// </summary>
    /// <param name="recap">The speaker recap to display.</param>
    public void SetSpeakerRecap(SpeakerRecap recap)
    {
        SpeakerList.ItemsSource = recap.Speakers;
        SpeakersHeader.Visibility = recap.Speakers.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ShowSpeakers();
    }

    /// <summary>Shows the plain-text box (status/errors) and hides both structured panels.</summary>
    private void ShowPlainText()
    {
        SummaryText.Visibility = Visibility.Visible;
        RecapPanel.Visibility = Visibility.Collapsed;
        SpeakerPanel.Visibility = Visibility.Collapsed;
    }

    /// <summary>Shows the topic-pivoted Conversation panel and hides the others.</summary>
    private void ShowConversation()
    {
        SummaryText.Visibility = Visibility.Collapsed;
        RecapPanel.Visibility = Visibility.Visible;
        SpeakerPanel.Visibility = Visibility.Collapsed;
    }

    /// <summary>Shows the people-pivoted Speakers panel and hides the others.</summary>
    private void ShowSpeakers()
    {
        SummaryText.Visibility = Visibility.Collapsed;
        RecapPanel.Visibility = Visibility.Collapsed;
        SpeakerPanel.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// Enables/disables the SUMMARY switch. Disabled (dimmed) when there are no captions to
    /// summarize; if disabled while summary is showing, snaps back to captions.
    /// </summary>
    /// <param name="available">Whether there is content available to summarize.</param>
    public void SetSummaryAvailable(bool available)
    {
        SummaryModeButton.IsEnabled = available;
        SummaryModeButton.Opacity = available ? 1.0 : 0.4;
        SummaryModeButton.ToolTip = available ? null : "Capture some captions first";

        if (!available && _mode == ViewMode.Summary)
            SetMode(ViewMode.Captions);
    }

    /// <summary>Switches between captions and summary with a short cross-fade.</summary>
    /// <param name="mode">The view mode to switch to.</param>
    private void SetMode(ViewMode mode)
    {
        if (_mode == mode)
        {
            // Re-request a summary if the user taps SUMMARY again (host may refresh from cache).
            if (mode == ViewMode.Summary) SummaryRequested?.Invoke(SelectedStyle);
            return;
        }

        _mode = mode;
        UpdateModeButtons();
        UpdateSpeakerBarVisibility();

        StylePicker.Visibility = mode == ViewMode.Summary ? Visibility.Visible : Visibility.Collapsed;

        if (mode == ViewMode.Summary)
        {
            SummaryRequested?.Invoke(SelectedStyle);
            FadeSwap(fadeIn: SummaryScroll, fadeOut: CaptionBox);
        }
        else
        {
            FadeSwap(fadeIn: CaptionBox, fadeOut: SummaryScroll);
        }
    }

    /// <summary>Cross-fades from <paramref name="fadeOut"/> to <paramref name="fadeIn"/>.</summary>
    /// <param name="fadeIn">The element to fade in and make visible.</param>
    /// <param name="fadeOut">The element to fade out and collapse once faded.</param>
    private static void FadeSwap(UIElement fadeIn, UIElement fadeOut)
    {
        var duration = new Duration(TimeSpan.FromMilliseconds(180));

        fadeIn.Visibility = Visibility.Visible;
        fadeIn.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration));

        var outAnim = new DoubleAnimation(1, 0, duration);
        outAnim.Completed += (_, _) => fadeOut.Visibility = Visibility.Collapsed;
        fadeOut.BeginAnimation(OpacityProperty, outAnim);
    }

    /// <summary>Applies selected/idle colors to the captions and summary mode buttons.</summary>
    private void UpdateModeButtons()
    {
        bool summary = _mode == ViewMode.Summary;
        CaptionsModeButton.Background = summary ? System.Windows.Media.Brushes.Transparent : ModeSelectedBg;
        CaptionsModeButton.Foreground = summary ? ModeIdleFg : ModeSelectedFg;
        SummaryModeButton.Background = summary ? ModeSelectedBg : System.Windows.Media.Brushes.Transparent;
        SummaryModeButton.Foreground = summary ? ModeSelectedFg : ModeIdleFg;
    }

    /// <summary>Shows the speaker pill bar only in captions mode once at least one speaker exists.</summary>
    private void UpdateSpeakerBarVisibility() =>
        SpeakerBarPanel.Visibility = _mode == ViewMode.Captions && _hasSpeakers
            ? Visibility.Visible
            : Visibility.Collapsed;

    /// <summary>Adds a pill for a newly-discovered speaker (no-op if already present).</summary>
    /// <param name="speaker">The speaker name to add a pill for.</param>
    public void AddSpeaker(string speaker)
    {
        if (string.IsNullOrWhiteSpace(speaker) || !_speakers.Add(speaker)) return;

        var button = new System.Windows.Controls.Button
        {
            Content = speaker,
            Style = (Style)Bar.FindResource("SpeakerButtonStyle"),
            ToolTip = $"Open {speaker}'s page",
        };
        button.Click += (_, _) => SpeakerSelected?.Invoke(speaker);

        SpeakerBar.Items.Add(button);
        _hasSpeakers = true;
        UpdateSpeakerBarVisibility();
    }

    /// <summary>Removes all speaker pills (used when a new session starts).</summary>
    public void ClearSpeakers()
    {
        _speakers.Clear();
        SpeakerBar.Items.Clear();
        _hasSpeakers = false;
        UpdateSpeakerBarVisibility();
    }

    /// <summary>Updates the live (interim) hypothesis line.</summary>
    /// <param name="text">The interim text to display.</param>
    public void ShowInterim(string text)
    {
        _interim = text ?? string.Empty;
        Render();
    }

    /// <summary>Commits a finalized line to the rolling history and clears the interim line.</summary>
    /// <param name="text">The finalized text to commit.</param>
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

    /// <summary>Reflects capture state in the HAL eye and hint text.</summary>
    /// <param name="running">Whether capture is currently running.</param>
    public void SetRunning(bool running)
    {
        _running = running;
        _audioTarget = 0.0;
        HintText.Text = running ? "Listening · Ctrl+Win+H to stop" : "Idle · Ctrl+Win+H to start";
        // The render loop eases the eye to its new resting state.
    }

    /// <summary>
    /// Publishes the latest audio level (RMS, 0..1). Cheap and non-visual — the render loop reads
    /// this target and eases the eye toward it, so the audio callback rate never gates the animation.
    /// </summary>
    /// <param name="level">The raw RMS audio level (0..1).</param>
    public void SetAudioLevel(double level)
    {
        level = level < 0 ? 0 : level > 1 ? 1 : level;
        // RMS sits low (~0.05–0.3); boost + perceptual sqrt curve to use the full visual range.
        _audioTarget = Math.Sqrt(Math.Min(1.0, level * 4.5));
    }

    /// <summary>
    /// Per-frame easing of the HAL eye toward the latest audio target (or rest when idle/silent).
    /// Asymmetric time constants — fast attack, slower release — give a lively, HAL-like pulse.
    /// </summary>
    /// <param name="sender">Unused.</param>
    /// <param name="e">Compositor rendering event arguments; expected to be a <see cref="RenderingEventArgs"/>.</param>
    private void OnRendering(object? sender, EventArgs e)
    {
        if (e is not RenderingEventArgs args) return;

        var now = args.RenderingTime;
        double dt = (now - _lastRenderTime).TotalSeconds;
        _lastRenderTime = now;
        if (dt <= 0 || dt > 0.25) dt = 1.0 / 60.0;   // guard against pauses / first frame

        double target = _running ? _audioTarget : 0.0;

        // Exponential smoothing with separate attack (rising) and release (falling) time constants.
        const double attackTau = 0.045;   // seconds — snappy onset
        const double releaseTau = 0.20;   // seconds — gentle decay
        double tau = target > _eyeLevel ? attackTau : releaseTau;
        double alpha = 1.0 - Math.Exp(-dt / tau);
        _eyeLevel += (target - _eyeLevel) * alpha;

        ApplyEye(_eyeLevel);
    }

    /// <summary>Maps the eased level (0..1) onto the eye's cornea brightness, glow, and pulse.</summary>
    /// <param name="l">The eased audio level (0..1).</param>
    private void ApplyEye(double l)
    {
        if (_running)
        {
            HalCornea.Opacity = 0.35 + 0.65 * l;
            HalGlow.Opacity = 0.12 + 0.80 * l;
            HalGlow.BlurRadius = 3 + 24 * l;
            HalScale.ScaleX = HalScale.ScaleY = 0.9 + 0.32 * l;
        }
        else
        {
            // Idle: dormant, dim eye (still eases down smoothly from any prior glow).
            HalCornea.Opacity = 0.24 + 0.10 * l;
            HalGlow.Opacity = 0.0;
            HalGlow.BlurRadius = 0;
            HalScale.ScaleX = HalScale.ScaleY = 1.0;
        }
    }

    /// <summary>
    /// Surfaces a diagnostic message (e.g. a failed start or recognizer error) directly in the
    /// caption area so it persists, instead of relying on a fleeting tray balloon.
    /// </summary>
    /// <param name="message">The diagnostic message to display.</param>
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

    /// <summary>Rebuilds the caption document from the history and interim line.</summary>
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

    /// <summary>Copies the current text selection, or the entire transcript if nothing is selected.</summary>
    private void CopySelectionOrAll()
    {
        if (!CaptionBox.Selection.IsEmpty)
            CaptionBox.Copy();
        else
            CopyAll();
    }

    /// <summary>Copies the entire transcript (history + interim) to the clipboard.</summary>
    private void CopyAll()
    {
        var text = FullText();
        if (text.Length > 0) Clipboard.SetText(text);
    }

    /// <summary>Docks the window as a full working-area-width bar flush at the top of the screen.</summary>
    private void PositionAsTopBar()
    {
        // Dock as a full working-area-width bar flush at the top, like native Live Captions.
        var area = SystemParameters.WorkArea;
        Width = area.Width;
        Left = area.Left;
        Top = area.Top;
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

    /// <summary>Begins moving the window when the drag handle is pressed.</summary>
    /// <param name="sender">Unused.</param>
    /// <param name="e">The mouse button event arguments.</param>
    private void OnDragHandlePressed(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    #endregion
}
