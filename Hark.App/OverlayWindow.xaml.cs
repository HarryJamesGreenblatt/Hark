using System.Text;
using System.IO;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
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

    /// <summary>How much of the transcript the captions view shows.</summary>
    private enum CaptionScope { Latest, Transcript }

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

    /// <summary>Glyph color for the mic toggle when the microphone is on (mixed in).</summary>
    private static readonly Brush MicOnBrush = new SolidColorBrush(Color.FromRgb(0x3B, 0x7D, 0xDD));

    /// <summary>Glyph color for the mic toggle when the microphone is off.</summary>
    private static readonly Brush MicOffBrush = new SolidColorBrush(Color.FromRgb(0x5F, 0x63, 0x68));

    /// <summary>Which content (captions or summary) is currently displayed.</summary>
    private ViewMode _mode = ViewMode.Captions;

    /// <summary>Whether captions show just the latest line or the full, scrollable transcript.</summary>
    private CaptionScope _scope = CaptionScope.Latest;

    /// <summary>The recap style currently chosen (Conversation or Speakers).</summary>
    private SummaryStyle _style = SummaryStyle.Conversation;

    /// <summary>The last Conversation recap rendered, kept so the copy button can serialize it.</summary>
    private MeetingRecap? _lastRecap;

    /// <summary>The last Speakers recap rendered, kept so the copy button can serialize it.</summary>
    private SpeakerRecap? _lastSpeakerRecap;

    /// <summary>Whether at least one speaker pill has been added to the index.</summary>
    private bool _hasSpeakers;

    /// <summary>The speaker pill currently being renamed via the inline editor, if any.</summary>
    private System.Windows.Controls.Button? _renamePill;

    /// <summary>Whether capture is currently running, driving the HAL eye and hint text.</summary>
    private bool _running;

    /// <summary>Whether the mic toggle is on (the local microphone is mixed into the captions).</summary>
    private bool _micOn;

    /// <summary>Whether the full-window HAL-eye Vision page is currently open.</summary>
    private bool _visionOpen;

    /// <summary>The large Vision eye's start transform (matched over the bar's small eye) for the zoom.</summary>
    private double _visionStartX, _visionStartY, _visionStartScale = 0.08;

    /// <summary>Whether an open/close Vision transition is mid-animation, to reject overlapping clicks.</summary>
    private bool _visionAnimating;

    /// <summary>Latest audio level target (0..1), published by the audio callback, eased in the render loop.</summary>
    private double _audioTarget;

    /// <summary>Displayed audio level (0..1), eased toward <see cref="_audioTarget"/> each frame.</summary>
    private double _eyeLevel;

    /// <summary>Timestamp of the previous compositor frame, for dt-based easing.</summary>
    private TimeSpan _lastRenderTime;

    #endregion

    #region Properties

    /// <summary>The recap style currently chosen in the picker.</summary>
    public SummaryStyle SelectedStyle => _style;

    #endregion

    #region Events

    /// <summary>Raised when the user clicks the overlay's close (✕) button.</summary>
    public event Action? CloseRequested;

    /// <summary>Raised when the user clicks a speaker pill in the CONVERSATION index.</summary>
    public event Action<string>? SpeakerSelected;

    /// <summary>Raised when the user renames a speaker pill: (oldName, newName). The host applies it globally.</summary>
    public event Action<string, string>? SpeakerRenameRequested;

    /// <summary>
    /// Raised when a summary is needed: on switching to SUMMARY mode, or when the recap style
    /// changes while in SUMMARY mode. The host decides whether to serve a cached result or generate.
    /// </summary>
    public event Action<SummaryStyle>? SummaryRequested;

    /// <summary>Raised when the user toggles the mic; the argument is the requested on/off state.</summary>
    public event Action<bool>? MicToggleRequested;

    /// <summary>Raised when the HAL eye dilates open, asking the host to conjure a Vision image.</summary>
    public event Action? VisionRequested;

    /// <summary>Raised when the Vision page collapses, so the host can cancel any in-flight conjure.</summary>
    public event Action? VisionClosed;

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
        MicOnBrush.Freeze();
        MicOffBrush.Freeze();

        Loaded += (_, _) => PositionAsTopBar();
        DragHandle.MouseLeftButtonDown += OnDragHandlePressed;

        // Click the HAL eye to dilate into the full-window Vision page. Open on the button's *release*
        // (not press) so this same click's mouse-up can't land on the just-revealed big eye and
        // immediately close it; the press only suppresses the drag-handle's window move.
        HalEye.MouseLeftButtonDown += (_, e) => e.Handled = true;
        HalEye.MouseLeftButtonUp += OnHalEyeReleased;
        HalEyeBig.MouseLeftButtonUp += (_, _) => { if (!_visionAnimating) CloseVision(); };

        CopyItem.Click += (_, _) => CopySelectionOrAll();
        CopyAllItem.Click += (_, _) => CopyAll();
        CloseButton.Click += (_, _) => CloseRequested?.Invoke();

        // Inline speaker-rename editor: Enter saves, Esc cancels; clicking away closes it (StaysOpen=False).
        RenameBox.KeyDown += OnRenameBoxKeyDown;
        SpeakerRenamePopup.Closed += (_, _) => _renamePill = null;

        // Mic toggle: flip state, update the glyph, and notify the host to start/stop mixing.
        MicButton.Click += (_, _) =>
        {
            _micOn = !_micOn;
            UpdateMicButton();
            MicToggleRequested?.Invoke(_micOn);
        };

        // Mode switch + recap-style picker.
        CaptionsModeButton.Click += (_, _) => SetMode(ViewMode.Captions);
        SummaryModeButton.Click += (_, _) => SetMode(ViewMode.Summary);

        // Caption scope switch (latest line vs full transcript).
        LatestScopeButton.Click += (_, _) => SetScope(CaptionScope.Latest);
        TranscriptScopeButton.Click += (_, _) => SetScope(CaptionScope.Transcript);

        // Recap style switch (Conversation vs Speakers).
        ConversationStyleButton.Click += (_, _) => SetStyle(SummaryStyle.Conversation);
        SpeakersStyleButton.Click += (_, _) => SetStyle(SummaryStyle.Speakers);

        // Copy whatever the window currently shows — captions (per scope) or the recap (per style).
        CopyButton.Click += (_, _) => CopyView();

        UpdateModeButtons();
        UpdateScopeButtons();
        UpdateStyleButtons();
        UpdateMicButton();
        SetSummaryAvailable(false);   // nothing to summarize until captions arrive

        // Re-fit the bar when the summary's own content changes (section / card expansion). Captions
        // height is driven explicitly from Render / ApplyCaptionScope so manual resizes aren't fought.
        RecapPanel.SizeChanged += (_, _) => ScheduleHeightAdjust();
        SpeakerPanel.SizeChanged += (_, _) => ScheduleHeightAdjust();

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
        ScheduleHeightAdjust();
    }

    /// <summary>Renders finished recap text (used only for status/error notes) in the summary view.</summary>
    /// <param name="text">The recap text to display.</param>
    public void SetSummaryText(string text)
    {
        ShowPlainText();
        SummaryText.Text = text ?? string.Empty;
        ScheduleHeightAdjust();
    }

    /// <summary>
    /// Renders a topic-pivoted (Conversation) recap: an overview, expandable per-topic notes, and a
    /// flat list of follow-up tasks. Empty sections are hidden.
    /// </summary>
    /// <param name="recap">The structured recap to display.</param>
    public void SetStructuredRecap(MeetingRecap recap)
    {
        _lastRecap = recap;
        RecapOverview.Text = recap.Overview ?? string.Empty;
        RecapOverview.Visibility = string.IsNullOrWhiteSpace(recap.Overview)
            ? Visibility.Collapsed : Visibility.Visible;

        TopicList.ItemsSource = recap.Topics;
        SetSectionVisible(NotesToggle, recap.Topics.Count > 0);

        TaskList.ItemsSource = recap.FollowUps;
        SetSectionVisible(TasksToggle, recap.FollowUps.Count > 0);

        ShowConversation();
    }

    /// <summary>
    /// Renders a people-pivoted (Speakers) recap: one expandable card per speaker (a one-line
    /// characterization that expands to reveal their points).
    /// </summary>
    /// <param name="recap">The speaker recap to display.</param>
    public void SetSpeakerRecap(SpeakerRecap recap)
    {
        _lastSpeakerRecap = recap;
        SpeakerList.ItemsSource = recap.Speakers;
        SetSectionVisible(SpeakersToggle, recap.Speakers.Count > 0);
        ShowSpeakers();
    }

    /// <summary>Shows or hides a collapsible section header, collapsing it when the section is empty.</summary>
    /// <param name="toggle">The section's header toggle.</param>
    /// <param name="visible">Whether the section has content to show.</param>
    private static void SetSectionVisible(System.Windows.Controls.Primitives.ToggleButton toggle, bool visible)
    {
        toggle.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (!visible) toggle.IsChecked = false;   // ensure the bound list collapses when empty
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

        StyleSwitch.Visibility = mode == ViewMode.Summary ? Visibility.Visible : Visibility.Collapsed;
        ScopeSwitch.Visibility = mode == ViewMode.Captions ? Visibility.Visible : Visibility.Collapsed;

        if (mode == ViewMode.Summary)
        {
            SummaryRequested?.Invoke(SelectedStyle);
            FadeSwap(fadeIn: SummaryScroll, fadeOut: CaptionBox);
        }
        else
        {
            ApplyCaptionScope();   // reset captions to its own height (don't keep the summary's)
            FadeSwap(fadeIn: CaptionBox, fadeOut: SummaryScroll);
        }
    }

    /// <summary>Cross-fades from <paramref name="fadeOut"/> to <paramref name="fadeIn"/>, then re-fits height.</summary>
    /// <param name="fadeIn">The element to fade in and make visible.</param>
    /// <param name="fadeOut">The element to fade out and collapse once faded.</param>
    private void FadeSwap(UIElement fadeIn, UIElement fadeOut)
    {
        var duration = new Duration(TimeSpan.FromMilliseconds(180));

        fadeIn.Visibility = Visibility.Visible;
        fadeIn.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration));

        var outAnim = new DoubleAnimation(1, 0, duration);
        outAnim.Completed += (_, _) =>
        {
            fadeOut.Visibility = Visibility.Collapsed;
            ScheduleHeightAdjust();   // re-fit once the outgoing view is gone
        };
        fadeOut.BeginAnimation(OpacityProperty, outAnim);
    }

    /// <summary>Switches the caption scope (latest line vs full transcript) and re-fits.</summary>
    /// <param name="scope">The scope to switch to.</param>
    private void SetScope(CaptionScope scope)
    {
        if (_scope == scope) return;
        _scope = scope;
        UpdateScopeButtons();
        ApplyCaptionScope();
    }

    /// <summary>
    /// Applies the current caption scope: LATEST keeps the bar at a single (current) line; TRANSCRIPT
    /// grows to the full conversation up to the screen, then scrolls. Re-renders and re-fits.
    /// </summary>
    private void ApplyCaptionScope()
    {
        var area = SystemParameters.WorkArea;
        CaptionBox.MaxHeight = _scope == CaptionScope.Transcript
            ? Math.Max(120, area.Height - 120)
            : 160;
        Render();
    }

    /// <summary>Applies selected/idle colors to the LATEST / TRANSCRIPT scope buttons.</summary>
    private void UpdateScopeButtons()
    {
        bool transcript = _scope == CaptionScope.Transcript;
        LatestScopeButton.Background = transcript ? System.Windows.Media.Brushes.Transparent : ModeSelectedBg;
        LatestScopeButton.Foreground = transcript ? ModeIdleFg : ModeSelectedFg;
        TranscriptScopeButton.Background = transcript ? ModeSelectedBg : System.Windows.Media.Brushes.Transparent;
        TranscriptScopeButton.Foreground = transcript ? ModeSelectedFg : ModeIdleFg;
    }

    /// <summary>Switches the recap style (Conversation vs Speakers) and re-requests the summary.</summary>
    /// <param name="style">The recap style to switch to.</param>
    private void SetStyle(SummaryStyle style)
    {
        if (_style == style) return;
        _style = style;
        UpdateStyleButtons();
        if (_mode == ViewMode.Summary) SummaryRequested?.Invoke(_style);
    }

    /// <summary>Applies selected/idle colors to the CONVERSATION / SPEAKERS style buttons.</summary>
    private void UpdateStyleButtons()
    {
        bool speakers = _style == SummaryStyle.Speakers;
        ConversationStyleButton.Background = speakers ? System.Windows.Media.Brushes.Transparent : ModeSelectedBg;
        ConversationStyleButton.Foreground = speakers ? ModeIdleFg : ModeSelectedFg;
        SpeakersStyleButton.Background = speakers ? ModeSelectedBg : System.Windows.Media.Brushes.Transparent;
        SpeakersStyleButton.Foreground = speakers ? ModeSelectedFg : ModeIdleFg;
    }

    /// <summary>Reflects the mic toggle's on/off state without notifying the host (initial sync).</summary>
    /// <param name="enabled">Whether the microphone is being mixed in.</param>
    public void SetMicEnabled(bool enabled)
    {
        _micOn = enabled;
        UpdateMicButton();
    }

    /// <summary>Lights (on) or dims (off) the headset glyph and updates its tooltip.</summary>
    private void UpdateMicButton()
    {
        MicButton.Foreground = _micOn ? MicOnBrush : MicOffBrush;
        MicButton.ToolTip = _micOn
            ? "Microphone on — your voice is captioned (click or Ctrl+Shift+M to mute)"
            : "Microphone off — click or press Ctrl+Shift+M to caption your own voice";
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
            ToolTip = "Open this speaker's page  ·  right-click to rename",
        };

        // Handlers read the pill's current Content so a rename needs only relabel it (no rewiring).
        button.Click += (s, _) =>
        {
            if (s is System.Windows.Controls.Button b) SpeakerSelected?.Invoke((string)b.Content);
        };

        // Left-click already opens the page, so the menu carries only Rename.
        var renameItem = new System.Windows.Controls.MenuItem
        {
            Header = "Rename…",
            Style = (Style)Bar.FindResource("DarkMenuItemStyle"),
        };
        renameItem.Click += (_, _) => BeginRename(button);

        var menu = new System.Windows.Controls.ContextMenu
        {
            Style = (Style)Bar.FindResource("DarkContextMenuStyle"),
        };
        menu.Items.Add(renameItem);
        button.ContextMenu = menu;

        SpeakerBar.Items.Add(button);
        _hasSpeakers = true;
        UpdateSpeakerBarVisibility();
    }

    /// <summary>
    /// Reflects a committed rename in the pill bar: relabels the pill, or removes it when the rename
    /// merged the speaker into an existing pill. The host refreshes the caption history and pages.
    /// </summary>
    /// <param name="oldName">The pill's current label.</param>
    /// <param name="newName">The label to show (or merge into).</param>
    public void RenameSpeaker(string oldName, string newName)
    {
        var pill = FindPill(oldName);
        if (pill is null) return;

        bool merged = _speakers.Contains(newName)
                   && !newName.Equals(oldName, StringComparison.OrdinalIgnoreCase);
        _speakers.Remove(oldName);

        if (merged)
        {
            SpeakerBar.Items.Remove(pill);
        }
        else
        {
            _speakers.Add(newName);
            pill.Content = newName;
        }

        _hasSpeakers = _speakers.Count > 0;
        UpdateSpeakerBarVisibility();
    }

    /// <summary>Finds the speaker pill whose label matches <paramref name="speaker"/> (case-insensitive).</summary>
    /// <param name="speaker">The speaker label to look for.</param>
    /// <returns>The matching pill, or <see langword="null"/> if none.</returns>
    private System.Windows.Controls.Button? FindPill(string speaker)
    {
        foreach (var item in SpeakerBar.Items)
            if (item is System.Windows.Controls.Button b &&
                b.Content is string s && s.Equals(speaker, StringComparison.OrdinalIgnoreCase))
                return b;
        return null;
    }

    /// <summary>Opens the inline rename editor anchored under a speaker pill, pre-filled with its label.</summary>
    /// <param name="pill">The pill being renamed.</param>
    private void BeginRename(System.Windows.Controls.Button pill)
    {
        _renamePill = pill;
        RenameBox.Text = (string)pill.Content;
        SpeakerRenamePopup.PlacementTarget = pill;
        SpeakerRenamePopup.IsOpen = true;
        RenameBox.Focus();
        RenameBox.SelectAll();
    }

    /// <summary>Commits the inline rename, asking the host to apply it globally.</summary>
    private void CommitRename()
    {
        if (_renamePill is null) return;
        var oldName = (string)_renamePill.Content;
        var proposed = RenameBox.Text?.Trim() ?? string.Empty;
        SpeakerRenamePopup.IsOpen = false;

        if (proposed.Length > 0 && !proposed.Equals(oldName, StringComparison.OrdinalIgnoreCase))
            SpeakerRenameRequested?.Invoke(oldName, proposed);
    }

    /// <summary>Handles Enter (save) / Esc (cancel) while typing a new speaker name.</summary>
    /// <param name="sender">Unused.</param>
    /// <param name="e">The key event arguments.</param>
    private void OnRenameBoxKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { CommitRename(); e.Handled = true; }
        else if (e.Key == Key.Escape) { SpeakerRenamePopup.IsOpen = false; e.Handled = true; }
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
        // Gate the RMS noise floor first so true silence reads as 0 (no "constant baseline" glow),
        // then boost hard so normal conversational speech — not just sustained shouts — reaches full.
        const double noiseFloor = 0.02;   // RMS below this is treated as silence
        double gated = level <= noiseFloor ? 0.0 : (level - noiseFloor) / (1.0 - noiseFloor);
        _audioTarget = Math.Sqrt(Math.Min(1.0, gated * 11.0));
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

        // Envelope follower: a fast attack snaps to a speech peak; a slow release lets the eye hold
        // near-full through the dips between syllables and resonate briefly after speech, then cool.
        const double attackTau = 0.025;   // seconds — snappy onset
        const double releaseTau = 0.22;   // seconds — brief sustain, then a lively cool-down
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
            // Full range: a dim ember when silent (gated to ~0) up to a bright, red-bloomed pulse on
            // loud speech. No glow at rest so it clearly "cools" between utterances.
            HalCornea.Opacity = 0.28 + 0.72 * l;
            HalGlow.Opacity = 0.90 * l;
            HalGlow.BlurRadius = 4 + 26 * l;
            HalScale.ScaleX = HalScale.ScaleY = 0.86 + 0.36 * l;
        }
        else
        {
            // Idle: dormant, dim eye (still eases down smoothly from any prior glow).
            HalCornea.Opacity = 0.24 + 0.10 * l;
            HalGlow.Opacity = 0.0;
            HalGlow.BlurRadius = 0;
            HalScale.ScaleX = HalScale.ScaleY = 1.0;
        }

        // Drive the large Vision eye with the same envelope (scaled for its size) while it's open.
        if (_visionOpen)
        {
            if (_running)
            {
                HalCorneaBig.Opacity = 0.30 + 0.70 * l;
                HalGlowBig.Opacity = 0.90 * l;
                HalGlowBig.BlurRadius = 30 + 120 * l;
                HalScaleBig.ScaleX = HalScaleBig.ScaleY = 0.92 + 0.12 * l;
            }
            else
            {
                HalCorneaBig.Opacity = 0.30 + 0.10 * l;
                HalGlowBig.Opacity = 0.0;
                HalGlowBig.BlurRadius = 0;
                HalScaleBig.ScaleX = HalScaleBig.ScaleY = 1.0;
            }
        }
    }

    /// <summary>Opens the Vision page when the bar's HAL eye is released (ignored mid-transition).</summary>
    /// <param name="sender">Unused.</param>
    /// <param name="e">The mouse button event arguments.</param>
    private void OnHalEyeReleased(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (!_visionAnimating && !_visionOpen) OpenVision();
    }

    /// <summary>
    /// Dilates the eye into the full-window Vision page in two staged beats: the bar chrome fades to
    /// darkness while a matched tiny eye holds over the real one, then that eye zooms to the centre and
    /// scales up — a continuous "push into the eye" rather than a separate fade + pop.
    /// </summary>
    private void OpenVision()
    {
        if (_visionOpen) return;
        _visionOpen = true;
        _visionAnimating = true;

        Height = SystemParameters.WorkArea.Height;

        // Present the canvas (still invisible) and force a layout pass so we can measure where the
        // large eye rests vs. where the bar's small eye sits, and match the two for a seamless zoom.
        VisionCanvas.Visibility = Visibility.Visible;
        VisionCanvas.BeginAnimation(OpacityProperty, null);   // release any held clock so Opacity=0 applies
        VisionCanvas.Opacity = 0;

        // Reset the eye to its identity (resting) transform BEFORE measuring: TransformToVisual includes
        // the render transform, so a leftover held pose from a prior cycle would corrupt the measured
        // centre (making the start offset ~0 → "teleports to centre and scales in place"). Release the
        // held clocks first so these identity values actually take effect.
        VisionEyeScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        VisionEyeScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        VisionEyeTranslate.BeginAnimation(TranslateTransform.XProperty, null);
        VisionEyeTranslate.BeginAnimation(TranslateTransform.YProperty, null);
        VisionEyeScale.ScaleX = VisionEyeScale.ScaleY = 1.0;
        VisionEyeTranslate.X = VisionEyeTranslate.Y = 0.0;
        UpdateLayout();

        var smallCentre = HalEye.TransformToVisual(VisionCanvas)
            .Transform(new System.Windows.Point(HalEye.ActualWidth / 2, HalEye.ActualHeight / 2));
        var bigCentre = HalEyeBig.TransformToVisual(VisionCanvas)
            .Transform(new System.Windows.Point(HalEyeBig.ActualWidth / 2, HalEyeBig.ActualHeight / 2));

        _visionStartScale = HalEyeBig.ActualWidth > 0 ? HalEye.ActualWidth / HalEyeBig.ActualWidth : 0.08;
        _visionStartX = smallCentre.X - bigCentre.X;
        _visionStartY = smallCentre.Y - bigCentre.Y;

        // Now park the large eye tiny and directly over the bar eye as the zoom's start pose.
        VisionEyeScale.ScaleX = VisionEyeScale.ScaleY = _visionStartScale;
        VisionEyeTranslate.X = _visionStartX;
        VisionEyeTranslate.Y = _visionStartY;

        // Beat 1 — other components fade to darkness (the eye stays, being the large matched one).
        // Beat 2 (the zoom) is chained on this fade's completion, so the eye holds its parked pose
        // through beat 1 rather than depending on a BeginTime delay's ambiguous held value.
        Bar.BeginAnimation(OpacityProperty, null);
        Bar.Opacity = 1;
        Bar.BeginAnimation(OpacityProperty,
            new DoubleAnimation(1, 0, new Duration(TimeSpan.FromMilliseconds(180))));

        var canvasFade = new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(180)));
        canvasFade.Completed += (_, _) => ZoomVisionEyeToCentre();
        VisionCanvas.BeginAnimation(OpacityProperty, canvasFade);

        VisionRequested?.Invoke();   // ask the host to conjure while the eye zooms in
    }

    /// <summary>Beat 2 of the open: zoom the parked eye from the bar corner to the centred full size.</summary>
    private void ZoomVisionEyeToCentre()
    {
        if (!_visionOpen) return;   // closed during the fade — abort the zoom

        var zoom = new Duration(TimeSpan.FromMilliseconds(560));
        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };

        var grow = new DoubleAnimation(_visionStartScale, 1.0, zoom) { EasingFunction = ease };
        grow.Completed += (_, _) => _visionAnimating = false;
        VisionEyeScale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        VisionEyeScale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);

        VisionEyeTranslate.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(_visionStartX, 0, zoom) { EasingFunction = ease });
        VisionEyeTranslate.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(_visionStartY, 0, zoom) { EasingFunction = ease });
    }

    /// <summary>Collapses the Vision page back into the bar: the eye zooms back to the bar, then the
    /// chrome fades in and the window shrinks to the docked bar.</summary>
    private void CloseVision()
    {
        if (!_visionOpen) return;
        _visionAnimating = true;
        VisionClosed?.Invoke();   // cancel any in-flight conjure

        var zoom = new Duration(TimeSpan.FromMilliseconds(320));
        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };

        var shrink = new DoubleAnimation(1.0, _visionStartScale, zoom) { EasingFunction = ease };
        VisionEyeScale.BeginAnimation(ScaleTransform.ScaleXProperty, shrink);
        VisionEyeScale.BeginAnimation(ScaleTransform.ScaleYProperty, shrink);
        VisionEyeTranslate.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(0, _visionStartX, zoom) { EasingFunction = ease });
        VisionEyeTranslate.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(0, _visionStartY, zoom) { EasingFunction = ease });

        // Bring the chrome back (hidden behind the still-dark canvas), then fade the canvas out over it.
        Bar.BeginAnimation(OpacityProperty, null);
        Bar.Opacity = 1;

        var fade = new DoubleAnimation(1, 0, new Duration(TimeSpan.FromMilliseconds(220)))
        {
            BeginTime = TimeSpan.FromMilliseconds(180),
        };
        fade.Completed += (_, _) =>
        {
            VisionCanvas.Visibility = Visibility.Collapsed;
            _visionOpen = false;
            _visionAnimating = false;
            AdjustHeightToContent();   // shrink the window back down to the bar
        };
        VisionCanvas.BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>Shows a status line on the Vision page (idle hint, "conjuring…", unconfigured, or error).</summary>
    /// <param name="message">The status text to display.</param>
    public void SetVisionStatus(string message)
    {
        HideVisionOrb();
        VisionStatusText.Text = message;
        VisionStatusText.Visibility = Visibility.Visible;
    }

    /// <summary>Shows the art-director concept as text when there's no render tier (or it failed).</summary>
    /// <param name="concept">The one-line visual concept.</param>
    public void SetVisionConcept(string concept)
    {
        HideVisionOrb();
        VisionStatusText.Text = concept;
        VisionStatusText.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// Renders a Vision image (PNG bytes) inside the HAL eye's orb — the cornea becomes the crystal
    /// ball — with a conversation-relative caption (the concept's theme) beneath it.
    /// </summary>
    /// <param name="png">The rendered image as PNG bytes.</param>
    /// <param name="caption">Optional caption about the conversation (the concept theme).</param>
    public void SetVisionImage(byte[] png, string? caption = null)
    {
        var bmp = new BitmapImage();
        using (var ms = new MemoryStream(png))
        {
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;   // decode now so the stream can be disposed
            bmp.StreamSource = ms;
            bmp.EndInit();
        }
        bmp.Freeze();

        VisionOrbBrush.ImageSource = bmp;
        VisionOrb.Visibility = Visibility.Visible;
        VisionOrb.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(500))));

        VisionStatusText.Text = caption ?? string.Empty;
        VisionStatusText.Visibility = string.IsNullOrWhiteSpace(caption) ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Clears the orb image so the eye shows its plain red cornea (idle / conjuring state).</summary>
    private void HideVisionOrb()
    {
        VisionOrb.BeginAnimation(OpacityProperty, null);
        VisionOrb.Opacity = 0;
        VisionOrb.Visibility = Visibility.Collapsed;
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

    /// <summary>
    /// Replaces the caption history with a refined transcript so the TRANSCRIPT/LATEST caption view
    /// reflects the offline second pass's corrected speaker attribution — not just the store that
    /// drives the speaker pages and recaps.
    /// </summary>
    /// <param name="lines">The refined, speaker-prefixed lines, in order.</param>
    public void SetCaptionLines(IEnumerable<string> lines)
    {
        _history.Clear();
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            _history.AddLast(line.Trim());
            while (_history.Count > MaxHistoryLines) _history.RemoveFirst();
        }
        _interim = string.Empty;
        Render();
    }

    /// <summary>Rebuilds the caption document for the current scope (latest line or full transcript).</summary>
    private void Render()
    {
        // Preserve the user's selection length so live interim updates don't fight active copying.
        bool hadSelection = !CaptionBox.Selection.IsEmpty;

        var para = new Paragraph();

        if (_scope == CaptionScope.Latest)
        {
            // Just the current line: the live interim, or the last finalized line when idle.
            var latest = _interim.Length > 0
                ? _interim
                : _history.Count > 0 ? _history.Last!.Value : string.Empty;
            if (latest.Length > 0)
                para.Inlines.Add(new Run(latest) { Foreground = _interim.Length > 0 ? InterimBrush : FinalBrush });
        }
        else
        {
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
        }

        CaptionDoc.Blocks.Clear();
        CaptionDoc.Blocks.Add(para);

        // Keep the newest text in view unless the user is actively selecting older text.
        if (!hadSelection) CaptionBox.ScrollToEnd();

        ScheduleHeightAdjust();
    }

    /// <summary>The entire transcript (history + current interim) as plain text.</summary>
    private string FullText()
    {
        var sb = new StringBuilder();
        foreach (var line in _history) sb.AppendLine(line);
        if (_interim.Length > 0) sb.AppendLine(_interim);
        return sb.ToString().TrimEnd();
    }

    /// <summary>The single latest caption line shown in LATEST scope (live interim, or last final).</summary>
    private string LatestLine() =>
        _interim.Length > 0 ? _interim : _history.Count > 0 ? _history.Last!.Value : string.Empty;

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

    /// <summary>
    /// Copies whatever the window currently shows: in CAPTIONS mode the transcript (the latest line in
    /// LATEST scope, or the full history in TRANSCRIPT); in SUMMARY mode the active recap — with its
    /// nested bullets — as markdown. Briefly confirms with a check glyph.
    /// </summary>
    private async void CopyView()
    {
        string text = _mode == ViewMode.Summary
            ? (_style == SummaryStyle.Speakers ? BuildSpeakerText(_lastSpeakerRecap) : BuildRecapText(_lastRecap))
            : (_scope == CaptionScope.Transcript ? FullText() : LatestLine());
        if (string.IsNullOrWhiteSpace(text)) return;

        try { Clipboard.SetText(text); }
        catch { return; }   // clipboard can be transiently locked by another app; just skip feedback

        CopyButton.Content = "\uE73E";   // checkmark
        CopyButton.ToolTip = "Copied";
        await Task.Delay(1200);
        CopyButton.Content = "\uE8C8";   // back to the copy glyph
        CopyButton.ToolTip = "Copy what's shown to the clipboard";
    }

    /// <summary>Serializes a Conversation recap (overview, topics + details, follow-up tasks) to markdown.</summary>
    /// <param name="recap">The recap to serialize, or <see langword="null"/>.</param>
    /// <returns>Markdown text, or an empty string when there is nothing to copy.</returns>
    private static string BuildRecapText(MeetingRecap? recap)
    {
        if (recap is null) return string.Empty;

        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(recap.Overview))
            sb.AppendLine(recap.Overview.Trim()).AppendLine();

        if (recap.Topics.Count > 0)
        {
            sb.AppendLine("## Meeting Notes").AppendLine();
            foreach (var t in recap.Topics)
            {
                sb.Append("### ").AppendLine(t.Title?.Trim());
                if (!string.IsNullOrWhiteSpace(t.Summary)) sb.AppendLine(t.Summary.Trim());
                foreach (var d in t.Details)
                    sb.Append("- ").AppendLine(d?.Trim());
                sb.AppendLine();
            }
        }

        if (recap.FollowUps.Count > 0)
        {
            sb.AppendLine("## Follow-up Tasks").AppendLine();
            foreach (var f in recap.FollowUps)
            {
                sb.Append("- [ ] ").Append(f.Task?.Trim());
                if (!string.IsNullOrWhiteSpace(f.Owner)) sb.Append(" \u2014 ").Append(f.Owner.Trim());
                sb.AppendLine();
            }
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>Serializes a Speakers recap (one heading + points per speaker) to markdown.</summary>
    /// <param name="recap">The recap to serialize, or <see langword="null"/>.</param>
    /// <returns>Markdown text, or an empty string when there is nothing to copy.</returns>
    private static string BuildSpeakerText(SpeakerRecap? recap)
    {
        if (recap is null || recap.Speakers.Count == 0) return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("## Speakers").AppendLine();
        foreach (var s in recap.Speakers)
        {
            sb.Append("### ").AppendLine(s.Speaker?.Trim());
            if (!string.IsNullOrWhiteSpace(s.Summary)) sb.AppendLine(s.Summary.Trim());
            foreach (var p in s.Points)
                sb.Append("- ").AppendLine(p?.Trim());
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>Docks the window as a full working-area-width bar flush at the top of the screen.</summary>
    private void PositionAsTopBar()
    {
        // Dock as a full working-area-width bar flush at the top, like native Live Captions.
        var area = SystemParameters.WorkArea;
        Width = area.Width;
        Left = area.Left;
        Top = area.Top;

        // Cap the summary so a long recap scrolls internally rather than running off the bottom.
        SummaryScroll.MaxHeight = Math.Max(120, area.Height - 120);
        ApplyCaptionScope();   // set the caption box's height cap for the current scope
    }

    /// <summary>Queues a height re-fit after the current layout pass (coalesces rapid changes).</summary>
    private void ScheduleHeightAdjust() =>
        Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(AdjustHeightToContent));

    /// <summary>
    /// Sizes the window height to its content (a caption line or the visible recap), clamped between
    /// <see cref="Window.MinHeight"/> and the screen height so a long recap scrolls inside instead of
    /// growing off-screen. Width stays full via <see cref="PositionAsTopBar"/>.
    /// </summary>
    private void AdjustHeightToContent()
    {
        if (!IsLoaded) return;
        var area = SystemParameters.WorkArea;
        if (_visionOpen) { Height = area.Height; return; }   // Vision fills the working area
        Bar.Measure(new System.Windows.Size(area.Width, area.Height));
        Height = Math.Clamp(Bar.DesiredSize.Height, MinHeight, area.Height);
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
