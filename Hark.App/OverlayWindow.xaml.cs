using System.Text;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Hark.Core.Summarization;
using Hark.Oracle.Vision;
using Hark.App.Reporting;
using Size = System.Windows.Size;
using FontFamily = System.Windows.Media.FontFamily;

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

    /// <summary>Whether capture is currently running, driving the Oracle's eye and hint text.</summary>
    private bool _running;

    /// <summary>Whether the mic toggle is on (the local microphone is mixed into the captions).</summary>
    private bool _micOn;

    /// <summary>Whether the full-window Oracle Vision page is currently open.</summary>
    private bool _visionOpen;

    /// <summary>The large Vision eye's start transform (matched over the bar's small eye) for the zoom.</summary>
    private double _visionStartX, _visionStartY, _visionStartScale = 0.08;

    /// <summary>Whether an open/close Vision transition is mid-animation, to reject overlapping clicks.</summary>
    private bool _visionAnimating;

    /// <summary>Latest audio level target (0..1), published by the audio callback, eased in the render loop.</summary>
    private double _audioTarget;

    /// <summary>Displayed audio level (0..1), eased toward <see cref="_audioTarget"/> each frame.</summary>
    private double _eyeLevel;

    /// <summary>Latest bass/treble targets (0..1) — drive the pupil's dilation and the highlight's shimmer.</summary>
    private double _bassTarget, _trebleTarget;

    /// <summary>Displayed bass/treble levels (0..1), eased toward their targets each frame.</summary>
    private double _bassLevel, _trebleLevel;

    /// <summary>Slow low-end capacitor (0..1): charges on sustained bass, bleeds off in quiet — the pupil's "sated" state.</summary>
    private double _pupilCharge;

    /// <summary>Spring position/velocity of the pupil dilation, so it carries momentum instead of snapping to peaks.</summary>
    private double _pupilPos = 0.80, _pupilVel;

    /// <summary>Most recent frame dt (s), shared with the pupil's spring integration.</summary>
    private double _frameDt;

    /// <summary>Slowly-followed highlight drift amplitude (px), so treble widens the shimmer gently instead of darting.</summary>
    private double _glossAmp = 3.0;

    /// <summary>Accumulated time (s) that advances the highlight's slow drift, independent of audio.</summary>
    private double _glossPhase;

    /// <summary>Timestamp of the previous compositor frame, for dt-based easing.</summary>
    private TimeSpan _lastRenderTime;

    // --- Gaze (Vision eye look-at): a 2D iris-offset saccade/fixation model grounded in the eye-movement
    // literature (Eyes Alive, Lee & Badler 2002; min-jerk saccades on the main sequence; 1/f fixation
    // jitter). See context/research/eye-motion-gaze.md.
    /// <summary>Largest iris offset (px) the gaze may reach; clamped well inside the socket so the cornea can't escape.</summary>
    private const double GazeMaxOffset = 30.0;
    /// <summary>Pixels per degree, mapping the deg-based saccade model onto the eye's ~±25° offset range.</summary>
    private const double GazePxPerDeg = 1.2;
    /// <summary>Random source for the statistical saccade/fixation model.</summary>
    private readonly Random _gazeRng = new();
    /// <summary>Current iris offset (px) — the gaze position before pink-noise jitter.</summary>
    private double _gazeX, _gazeY;
    /// <summary>The in-progress saccade's start and (undershot) landing offsets (px).</summary>
    private double _sacFromX, _sacFromY, _sacToX, _sacToY;
    /// <summary>The saccade's TRUE target before undershoot, so a large one can fire a corrective secondary.</summary>
    private double _sacTrueX, _sacTrueY;
    /// <summary>Elapsed / total saccade time (s); <see cref="_sacDur"/> &lt;= 0 means fixating, not saccading.</summary>
    private double _sacT, _sacDur;
    /// <summary>Whether the landing saccade should fire a corrective secondary (large undershoots only).</summary>
    private bool _sacCorrect;
    /// <summary>Seconds left holding the current fixation before the next ambient glance.</summary>
    private double _fixHold;
    /// <summary>Cursor offset (px from the eye centre) while the pointer is over the canvas; used only on pill hover.</summary>
    private System.Windows.Point? _lookAt;
    /// <summary>Octave states for the 1/f (pink) fixation jitter, per axis.</summary>
    private double _pnX1, _pnX2, _pnX3, _pnY1, _pnY2, _pnY3;

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

    /// <summary>
    /// Raised by Save to obtain both recaps for the report, generating them if the user never opened
    /// SUMMARY. The host returns cached results when fresh; returns nulls when summaries aren't available.
    /// </summary>
    public event Func<Task<(MeetingRecap? Conversation, SpeakerRecap? Speakers)>>? ReportRecapsRequested;

    /// <summary>Raised when the user toggles the mic; the argument is the requested on/off state.</summary>
    public event Action<bool>? MicToggleRequested;

    /// <summary>Raised when the Oracle's eye dilates open, asking the host to conjure a Vision image.</summary>
    public event Action? VisionRequested;

    /// <summary>Raised when the Vision page collapses, so the host can cancel any in-flight conjure.</summary>
    public event Action? VisionClosed;

    #endregion

    #region Constructor(s)

    /// <summary>
    /// Initializes the overlay's controls, freezes its static brushes, wires up window/menu/mode
    /// handlers, and starts the compositor-driven render loop for the Oracle's eye.
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

        // Click the Oracle's eye to dilate into the full-window Vision page. Open on the button's *release*
        // (not press) so this same click's mouse-up can't land on the just-revealed big eye and
        // immediately close it; the press only suppresses the drag-handle's window move.
        OracleEye.MouseLeftButtonDown += (_, e) => e.Handled = true;
        OracleEye.MouseLeftButtonUp += OnOracleEyeReleased;
        OracleEyeBig.MouseLeftButtonUp += (_, _) => { if (!_visionAnimating) CloseVision(); };

        // Gaze look-at: while inspecting a mind-map pill the big eye follows the cursor slowly (smooth
        // pursuit); otherwise it is only audio-reactive (organic jitter + occasional glances when stimulated).
        VisionCanvas.MouseMove += OnVisionCanvasMouseMove;
        VisionCanvas.MouseLeave += (_, _) => _lookAt = null;

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

        // Save a full self-contained report (transcript + summary + vision slideshow) to disk.
        SaveButton.Click += (_, _) => SaveReport();

        UpdateModeButtons();
        UpdateScopeButtons();
        UpdateStyleButtons();
        UpdateMicButton();
        SetSummaryAvailable(false);   // nothing to summarize until captions arrive

        // Re-fit the bar when the summary's own content changes (section / card expansion). Captions
        // height is driven explicitly from Render / ApplyCaptionScope so manual resizes aren't fought.
        RecapPanel.SizeChanged += (_, _) => ScheduleHeightAdjust();
        SpeakerPanel.SizeChanged += (_, _) => ScheduleHeightAdjust();

        // Drive the Oracle's eye from the WPF compositor (~60fps), decoupled from the audio callback
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

    /// <summary>Reflects capture state in the Oracle's eye and hint text.</summary>
    /// <param name="running">Whether capture is currently running.</param>
    public void SetRunning(bool running)
    {
        _running = running;
        _audioTarget = _bassTarget = _trebleTarget = 0.0;
        HintText.Text = running ? "Listening · Ctrl+Win+H to stop" : "Idle · Ctrl+Win+H to start";
        // The render loop eases the eye to its new resting state.
    }

    /// <summary>
    /// Gates a raw RMS band level against its noise floor and shapes it into a 0..1 drive signal:
    /// silence reads as a true 0 (no constant glow), and a hard gain + square-root curve lift
    /// ordinary conversational levels — not just sustained peaks — toward full.
    /// </summary>
    /// <param name="level">The raw RMS level (0..1).</param>
    /// <param name="gain">Pre-curve gain; higher for quieter bands (e.g. treble) so they still register.</param>
    /// <param name="floor">RMS below this is treated as silence for this band.</param>
    private static double Shape(double level, double gain, double floor)
    {
        level = level < 0 ? 0 : level > 1 ? 1 : level;
        double gated = level <= floor ? 0.0 : (level - floor) / (1.0 - floor);
        return Math.Sqrt(Math.Min(1.0, gated * gain));
    }

    /// <summary>
    /// Publishes the latest audio level (RMS, 0..1). Cheap and non-visual — the render loop reads
    /// this target and eases the eye toward it, so the audio callback rate never gates the animation.
    /// </summary>
    /// <param name="level">The raw RMS audio level (0..1).</param>
    public void SetAudioLevel(double level) => _audioTarget = Shape(level, 11.0, 0.02);

    /// <summary>
    /// Publishes the latest banded audio levels (overall / bass / treble, raw RMS 0..1). The render
    /// loop eases each with its own time constant so the eye's core, its dilating pupil, and its
    /// shimmering highlight react to different facets of the sound independently.
    /// </summary>
    /// <param name="level">Overall broadband RMS (drives the core brightness/pulse).</param>
    /// <param name="bass">Low-band RMS (drives the pupil's dilation).</param>
    /// <param name="treble">High-band RMS (drives the highlight's shimmer).</param>
    public void SetAudioFeatures(double level, double bass, double treble)
    {
        _audioTarget = Shape(level, 11.0, 0.02);
        _bassTarget = Shape(bass, 22.0, 0.010);
        _trebleTarget = Shape(treble, 26.0, 0.006);
    }

    /// <summary>
    /// Per-frame easing of the Oracle's eye toward the latest audio target (or rest when idle/silent).
    /// Asymmetric time constants — fast attack, slower release — give a lively, organic pulse.
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

        // Band envelopes with their own time constants: bass swells and settles slowly (a breathing
        // pupil), treble snaps and falls quickly (a live, shivering highlight).
        double bassTgt = _running ? _bassTarget : 0.0;
        double trebleTgt = _running ? _trebleTarget : 0.0;
        _bassLevel += (bassTgt - _bassLevel) * (1.0 - Math.Exp(-dt / (bassTgt > _bassLevel ? 0.05 : 0.35)));
        _trebleLevel += (trebleTgt - _trebleLevel) * (1.0 - Math.Exp(-dt / (trebleTgt > _trebleLevel ? 0.02 : 0.12)));
        _frameDt = dt;
        _glossPhase += dt;

        if (_visionOpen) UpdateGaze(dt);

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
            OracleCornea.Opacity = 0.28 + 0.72 * l;
            OracleGlow.Opacity = 0.90 * l;
            OracleGlow.BlurRadius = 4 + 26 * l;
            OracleScale.ScaleX = OracleScale.ScaleY = 0.86 + 0.36 * l;
        }
        else
        {
            // Idle: dormant, dim eye (still eases down smoothly from any prior glow).
            OracleCornea.Opacity = 0.24 + 0.10 * l;
            OracleGlow.Opacity = 0.0;
            OracleGlow.BlurRadius = 0;
            OracleScale.ScaleX = OracleScale.ScaleY = 1.0;
        }

        // Drive the large Vision eye with the same envelope (scaled for its size) while it's open.
        if (_visionOpen)
        {
            if (_running)
            {
                OracleCorneaBig.Opacity = 0.30 + 0.70 * l;
                OracleGlowBig.Opacity = 0.90 * l;
                OracleGlowBig.BlurRadius = 30 + 120 * l;
                OracleScaleBig.ScaleX = OracleScaleBig.ScaleY = 0.92 + 0.12 * l;
            }
            else
            {
                OracleCorneaBig.Opacity = 0.30 + 0.10 * l;
                OracleGlowBig.Opacity = 0.0;
                OracleGlowBig.BlurRadius = 0;
                OracleScaleBig.ScaleX = OracleScaleBig.ScaleY = 1.0;
            }

            ApplyPupilAndHighlight();
        }
    }

    /// <summary>
    /// Drives the Vision orb's "pupil" dilation and the glass highlight's drift from the banded
    /// audio. The pupil is deliberately NOT a direct map of the bass: low-end slowly charges a
    /// capacitor (a "sated" reservoir) and an under-damped spring chases it, so the pupil carries
    /// momentum — it grows and shrinks gradually and drifts/overshoots rather than snapping to every
    /// peak. Modeled on WavBall's peak-fed goal that charges up, sates, and eases away. The highlight
    /// keeps its slow Lissajous drift, widened by treble transients.
    /// </summary>
    private void ApplyPupilAndHighlight()
    {
        double dt = _frameDt;

        // Capacitor: sustained low-end fills it fairly quickly, quiet bleeds it off slowly, so the
        // pupil stays "sated" a while after a loud passage instead of collapsing the instant it dips.
        double chargeTau = _bassLevel > _pupilCharge ? 0.6 : 1.8;
        _pupilCharge += (_bassLevel - _pupilCharge) * (1.0 - Math.Exp(-dt / chargeTau));

        // The spring's target: a rest pupil inside the iris that opens fuller as it charges. A higher
        // gain lets ordinary speech reach most of the way out (up to the full cornea), plus a slow
        // idle wander so it is never perfectly still.
        double breath = 0.02 * Math.Sin(_glossPhase * 0.35);
        double target = 0.82 + 0.22 * _pupilCharge + breath;

        // Under-damped spring (ω≈3.5 rad/s, ζ≈0.46): momentum makes the dilation gradual and lets it
        // gently overshoot and change direction — an audio-steered drift, not a peak-locked jump.
        const double stiffness = 12.0;
        const double damping = 3.2;
        double accel = stiffness * (target - _pupilPos) - damping * _pupilVel;
        _pupilVel += accel * dt;
        _pupilPos += _pupilVel * dt;

        // Rails: clamp travel and kill inward/outward velocity at the bound so it can't wind up or stick.
        if (_pupilPos < 0.70) { _pupilPos = 0.70; if (_pupilVel < 0) _pupilVel = 0; }
        else if (_pupilPos > 1.0) { _pupilPos = 1.0; if (_pupilVel > 0) _pupilVel = 0; }

        VisionOrbScale.ScaleX = VisionOrbScale.ScaleY = _pupilPos;

        // Highlight: a slow Lissajous drift (light playing over the glass at slightly changing
        // angles). Treble widens the drift, but through its own slow follower so the shimmer swells
        // and eases rather than darting on every transient (it was reading as jerky). Deterministic.
        double glossTarget = 3.0 + 6.0 * _trebleLevel;
        _glossAmp += (glossTarget - _glossAmp) * (1.0 - Math.Exp(-dt / 0.5));
        VisionGlossTranslate.X = Math.Sin(_glossPhase * 0.61) * _glossAmp;
        VisionGlossTranslate.Y = Math.Cos(_glossPhase * 0.43) * _glossAmp * 0.6;
    }

    /// <summary>
    /// Drives the Vision eye's gaze — a 2D iris offset orthogonal to the dilation (bass) and shimmer
    /// (treble) channels — in two modes. **Pill hover:** the iris follows the pointer with a slow, deliberate
    /// smooth pursuit (looking at whatever mind-map node you're inspecting). **Otherwise (ambient):** it is
    /// sound-reactive, not constant — it eases back to the primary position and only makes small ballistic
    /// glances when there's speech energy to steer it, with a 1/f (pink) micro-jitter whose amplitude tracks
    /// stimulation, so a quiet room is nearly still while active speech makes it jitter organically. Saccade
    /// mechanics (min-jerk trajectory, main-sequence duration, undershoot) follow the eye-movement literature
    /// (context/research/eye-motion-gaze.md).
    /// </summary>
    /// <param name="dt">Frame delta time (s).</param>
    private void UpdateGaze(double dt)
    {
        // Advance any in-progress saccade along the min-jerk position profile (smootherstep 6u⁵−15u⁴+10u³).
        if (_sacDur > 0)
        {
            _sacT += dt;
            double u = Math.Min(1.0, _sacT / _sacDur);
            double sp = u * u * u * (u * (u * 6 - 15) + 10);
            _gazeX = _sacFromX + (_sacToX - _sacFromX) * sp;
            _gazeY = _sacFromY + (_sacToY - _sacFromY) * sp;
            if (u >= 1.0)
            {
                _sacDur = 0;
                if (_sacCorrect)
                {
                    _sacCorrect = false;
                    BeginSaccade(_sacTrueX, _sacTrueY, correctable: false);   // corrective secondary
                }
            }
        }

        if (_pillHovered && _lookAt is System.Windows.Point look)
        {
            // Pill hover → gaze toward the cursor. A quick-but-smooth follow (matching the jitter's
            // liveliness), and a gentler excursion cap so the iris turns toward the pill without
            // straining to the socket rim (which reads as an unnatural stretch).
            double tx = Math.Clamp(look.X * 0.09, -22, 22);
            double ty = Math.Clamp(look.Y * 0.09, -22, 22);
            double follow = 1.0 - Math.Exp(-dt / 0.18);   // ~0.18 s — lively like the jitter, still smooth
            _gazeX += (tx - _gazeX) * follow;
            _gazeY += (ty - _gazeY) * follow;
            _sacDur = 0;            // the pointer leads; cancel any ambient glance
            _fixHold = 0.4;
        }
        else if (_sacDur <= 0)
        {
            // Ambient: sound-reactive, NOT constant. Ease back to the primary position and only glance when
            // there's stimulation to steer it — calm when quiet, organically active when there's speech.
            double stim = _running ? _eyeLevel : 0.0;

            // Gentle return to centre (weaker while stimulated so a glance can linger a touch).
            double pull = (1.0 - Math.Exp(-dt / 1.2)) * (1.0 - 0.6 * stim);
            _gazeX += (0 - _gazeX) * pull;
            _gazeY += (0 - _gazeY) * pull;

            // Audio-gated glances: the budget drains faster the louder it is, and only fires with enough
            // stimulation, so a quiet room barely moves while active speech scatters little glances.
            _fixHold -= dt * (0.25 + 2.5 * stim);
            if (stim > 0.15 && _fixHold <= 0)
            {
                double aDeg = Math.Min(SampleSaccadeMagnitudeDeg(), GazeMaxOffset / GazePxPerDeg) * (0.35 + 0.65 * stim);
                double dir = SampleSaccadeDirection();
                double mag = aDeg * GazePxPerDeg;
                BeginSaccade(Math.Cos(dir) * mag, Math.Sin(dir) * mag, correctable: false);
                _fixHold = 0.8 + _gazeRng.NextDouble() * 1.4;   // budget until the next possible glance
            }
        }

        // 1/f (pink) micro-jitter, amplitude driven by stimulation: near-still when quiet, organic when
        // stimulated (calmer mid-saccade so the ballistic move stays clean).
        double jitterAmp = (_sacDur > 0 ? 0.25 : 1.0) * (0.15 + 1.3 * (_running ? _eyeLevel : 0.0));
        double jx = PinkStep(ref _pnX1, ref _pnX2, ref _pnX3, dt) * jitterAmp;
        double jy = PinkStep(ref _pnY1, ref _pnY2, ref _pnY3, dt) * jitterAmp;

        double fx = Math.Clamp(_gazeX + jx, -GazeMaxOffset, GazeMaxOffset);
        double fy = Math.Clamp(_gazeY + jy, -GazeMaxOffset, GazeMaxOffset);
        GazeTranslate.X = fx;
        GazeTranslate.Y = fy;

        // Intra-pupil parallax: the bright pale-gold core (the HAL-like "pupil point") leads the red disc,
        // shifting further in the gaze direction so the gaze reads as aimed from the point — while staying
        // part of the same unit. The offset (relative to the disc's OWN centre) is pupilLead·offsetPx, so at
        // the ±30 px travel the core sits ~22 px off-centre in the ⌀204 px cornea — clearly ahead of the disc.
        const double pupilLead = 0.75 / 204.0;
        double ox = 0.5 + fx * pupilLead, oy = 0.5 + fy * pupilLead;
        OracleCorneaGradientBig.Center = new System.Windows.Point(ox, oy);
        OracleCorneaGradientBig.GradientOrigin = new System.Windows.Point(ox, oy);
    }

    /// <summary>
    /// Starts a ballistic saccade from the current offset to (<paramref name="toX"/>,<paramref name="toY"/>)
    /// with a ~10% undershoot; its DURATION follows the main sequence T=α+βA (the "natural for characters"
    /// half-speed fit). A large, correctable saccade remembers the true target so a secondary can correct it.
    /// </summary>
    private void BeginSaccade(double toX, double toY, bool correctable)
    {
        _sacFromX = _gazeX; _sacFromY = _gazeY;
        _sacTrueX = toX; _sacTrueY = toY;
        _sacToX = _gazeX + (toX - _gazeX) * 0.9;   // undershoot ~10% (a documented human tendency)
        _sacToY = _gazeY + (toY - _gazeY) * 0.9;
        _sacCorrect = correctable;

        double dx = _sacToX - _sacFromX, dy = _sacToY - _sacFromY;
        double aDeg = Math.Sqrt(dx * dx + dy * dy) / GazePxPerDeg;
        _sacDur = Math.Max(0.03, (74.42 + 6.08 * aDeg) / 1000.0);   // main sequence α/β (ms → s)
        _sacT = 0;
    }

    /// <summary>Draws a saccade magnitude (degrees) from Eyes Alive's fitted exponential (mean ≈ 6.9°; 90% &lt; 15°).</summary>
    private double SampleSaccadeMagnitudeDeg() => -6.9 * Math.Log(1.0 - _gazeRng.NextDouble());

    /// <summary>Direction bins (radians, screen space y-down) with Eyes Alive's Table 1 weights.</summary>
    private static readonly (double AngleRad, double Weight)[] GazeDirs =
    {
        (0.0,             15.54),   // right
        (-Math.PI / 4,     6.46),   // up-right
        (-Math.PI / 2,    17.69),   // up
        (-3 * Math.PI / 4, 7.44),   // up-left
        (Math.PI,         16.80),   // left
        (3 * Math.PI / 4,  7.89),   // down-left
        (Math.PI / 2,     20.38),   // down
        (Math.PI / 4,      7.79),   // down-right
    };

    /// <summary>Draws a saccade direction (radians) from Eyes Alive's 8-bin distribution (axes ~2× diagonals), jittered within the bin.</summary>
    private double SampleSaccadeDirection()
    {
        double total = 0; foreach (var d in GazeDirs) total += d.Weight;
        double r = _gazeRng.NextDouble() * total, acc = 0, ang = 0;
        foreach (var d in GazeDirs) { acc += d.Weight; if (r <= acc) { ang = d.AngleRad; break; } }
        return ang + (_gazeRng.NextDouble() - 0.5) * (Math.PI / 4);   // ±22.5° within the bin
    }

    /// <summary>Advances a 3-octave 1/f (pink) noise (leaky-integrated white noise) and returns a px sample.</summary>
    private double PinkStep(ref double o1, ref double o2, ref double o3, double dt)
    {
        o1 += ((_gazeRng.NextDouble() * 2 - 1) - o1) * (1.0 - Math.Exp(-dt / 0.05));
        o2 += ((_gazeRng.NextDouble() * 2 - 1) - o2) * (1.0 - Math.Exp(-dt / 0.20));
        o3 += ((_gazeRng.NextDouble() * 2 - 1) - o3) * (1.0 - Math.Exp(-dt / 0.80));
        return o1 * 0.5 + o2 * 0.9 + o3 * 1.4;   // low freqs weighted higher (1/f); ~±1.5 px
    }

    /// <summary>Recentres the gaze (called when the Vision page opens) so it starts on the primary position.</summary>
    private void ResetGaze()
    {
        _gazeX = _gazeY = 0; _sacDur = 0; _sacT = 0; _sacCorrect = false;
        _fixHold = 1.0; _lookAt = null;
        _pnX1 = _pnX2 = _pnX3 = _pnY1 = _pnY2 = _pnY3 = 0;
        GazeTranslate.X = GazeTranslate.Y = 0;
        OracleCorneaGradientBig.Center = OracleCorneaGradientBig.GradientOrigin = new System.Windows.Point(0.5, 0.5);
    }

    /// <summary>Tracks the cursor over the Vision canvas as a gaze look-at target (offset from the eye centre).</summary>
    /// <param name="sender">Unused.</param>
    /// <param name="e">The mouse-move event arguments.</param>
    private void OnVisionCanvasMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_visionOpen) { _lookAt = null; return; }
        var p = e.GetPosition(OracleEyeBig);
        _lookAt = new System.Windows.Point(
            p.X - OracleEyeBig.ActualWidth / 2, p.Y - OracleEyeBig.ActualHeight / 2);
    }

    /// <summary>Opens the Vision page when the bar's Oracle eye is released (ignored mid-transition).</summary>
    /// <param name="sender">Unused.</param>
    /// <param name="e">The mouse button event arguments.</param>
    private void OnOracleEyeReleased(object sender, MouseButtonEventArgs e)
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
        ResetGaze();
        StartPupilFiller();

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

        var smallCentre = OracleEye.TransformToVisual(VisionCanvas)
            .Transform(new System.Windows.Point(OracleEye.ActualWidth / 2, OracleEye.ActualHeight / 2));
        var bigCentre = OracleEyeBig.TransformToVisual(VisionCanvas)
            .Transform(new System.Windows.Point(OracleEyeBig.ActualWidth / 2, OracleEyeBig.ActualHeight / 2));

        _visionStartScale = OracleEyeBig.ActualWidth > 0 ? OracleEye.ActualWidth / OracleEyeBig.ActualWidth : 0.08;
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
        StopScrying();
        LivePill.Visibility = Visibility.Collapsed;   // leaving review; a reopen starts live
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
            ClearVisionDiagram();      // drop any diagram so a reopen starts clean
            StopPupilFiller();         // drop the pupil buffer / filler cycle too
            AdjustHeightToContent();   // shrink the window back down to the bar
        };
        VisionCanvas.BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>
    /// Clears ALL Vision session state — the pupil ring buffer/filler cycle, the diagram, the pupil image,
    /// and any scrying sheen — so a new capture session starts with a blank Oracle Vision. Called on a caption
    /// toggle (which bypasses <see cref="CloseVision"/>'s cleanup).
    /// </summary>
    public void ResetVision()
    {
        StopScrying();
        StopPupilFiller();
        ClearVisionDiagram();
        HideVisionOrb();
        ClearVisionHistory();
    }

    /// <summary>Shows a status line on the Vision page (idle hint, "conjuring…", unconfigured, or error).</summary>
    /// <param name="message">The status text to display.</param>
    public void SetVisionStatus(string message)
    {
        StopScrying();
        HideVisionOrb();
        VisionStatusText.Text = message;
        VisionStatusText.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// Marks the start of a conjure. Deliberately shows NO synthetic "loading" spinner — while a scene
    /// renders the Oracle simply rests in its regular red sound-reactive state; a held image, if
    /// any, stays until the new one lands.
    /// </summary>
    public void BeginVisionConjuring()
    {
        VisionStatusText.Visibility = Visibility.Collapsed;
    }

    /// <summary>Ensures the (legacy) scrying sheen is hidden. Kept as a no-op-safe clear called on render/close.</summary>
    private void StopScrying()
    {
        ScryingRotate.BeginAnimation(RotateTransform.AngleProperty, null);
        ScryingSheen.BeginAnimation(OpacityProperty, null);
        ScryingSheen.Visibility = Visibility.Collapsed;
    }

    /// <summary>Stops the conjuring/scrying state (e.g. a conjure that yielded no image). Idempotent.</summary>
    public void StopVisionConjuring() => StopScrying();

    /// <summary>Shows the art-director concept as text when there's no render tier (or it failed).</summary>
    /// <param name="concept">The one-line visual concept.</param>
    public void SetVisionConcept(string concept)
    {
        StopScrying();
        HideVisionOrb();
        VisionStatusText.Text = concept;
        VisionStatusText.Visibility = Visibility.Visible;
    }

    /// <summary>Recent successful pupil images, cycled to fill the gap when fresh renders stall (e.g. an RAI block).</summary>
    private readonly List<BitmapImage> _pupilBuffer = new();
    /// <summary>Perceptual average-hashes paralleling <see cref="_pupilBuffer"/>, to skip near-identical scenes.</summary>
    private readonly List<ulong> _pupilHashes = new();
    /// <summary>Wall-clock of the last FRESH pupil render, to detect a stall.</summary>
    private DateTime _lastPupilUpdateUtc = DateTime.MinValue;
    /// <summary>Index into <see cref="_pupilBuffer"/> for the filler cycle.</summary>
    private int _fillerIndex;
    /// <summary>Index into <see cref="_visionBeats"/> for the review-mode slideshow.</summary>
    private int _fillerBeatIndex;
    /// <summary>Wall-clock of the last filler advance (a review slide, or a live image-blink), pacing each.</summary>
    private DateTime _lastFillerAdvanceUtc = DateTime.MinValue;
    /// <summary>True while a diagram pill is hovered — holds the review slideshow so the user can read a detail.</summary>
    private bool _pillHovered;
    /// <summary>Drives the pupil filler cycle while the Vision page is open.</summary>
    private DispatcherTimer? _fillerTimer;

    private const int PupilBufferMax = 16;
    /// <summary>Average-hash Hamming distance at or below which two scenes count as near-identical (skip buffering).</summary>
    private const int PupilDupDistance = 6;
    /// <summary>Resting opacity of the pupil scene so the red cornea glow bleeds through — the image reads as suspended in the ball's ether.</summary>
    private const double PupilSceneOpacity = 0.85;
    private static readonly TimeSpan FillerTick = TimeSpan.FromSeconds(2);
    /// <summary>Only cycle once the pupil has been static past the normal render cadence (i.e. renders are stalling).</summary>
    private static readonly TimeSpan FillerIdle = TimeSpan.FromSeconds(16);
    /// <summary>Dwell on each beat while the review-mode slideshow auto-advances the timeline.</summary>
    private static readonly TimeSpan ReviewSlideInterval = TimeSpan.FromSeconds(7);
    /// <summary>Pace of the live image-blink (buffer cycle) while a render is stalled.</summary>
    private static readonly TimeSpan BlinkInterval = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Renders a Vision image (PNG bytes) inside the Oracle's eye orb — the cornea becomes the vision
    /// surface — with a conversation-relative caption (the concept's theme) beneath it. The image is also
    /// buffered so the filler cycle can hold the pupil alive if subsequent renders stall.
    /// </summary>
    /// <param name="png">The rendered image as PNG bytes.</param>
    /// <param name="caption">Optional caption about the conversation (the concept theme).</param>
    public void SetVisionImage(byte[] png, string? caption = null)
    {
        StopScrying();
        var bmp = new BitmapImage();
        using (var ms = new MemoryStream(png))
        {
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;   // decode now so the stream can be disposed
            bmp.StreamSource = ms;
            bmp.EndInit();
        }
        bmp.Freeze();

        AddToPupilBuffer(bmp);
        _lastPupilUpdateUtc = DateTime.UtcNow;

        TransitionPupil(bmp);

        VisionStatusText.Text = caption ?? string.Empty;
        VisionStatusText.Visibility = string.IsNullOrWhiteSpace(caption) ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>
    /// Transitions the pupil to a new image with a combined blink + cross-fade, used for BOTH fresh
    /// renders and filler scene-changes so the effect is consistent: the cornea-red eyelid sweeps DOWN
    /// inside the pupil, the image swaps while hidden, then the lid sweeps UP while the new image
    /// cross-fades in across the WHOLE up-stroke (settling just as the lid clears) — so the reveal reads
    /// on the swipe-up rather than flashing in behind the closed lid.
    /// </summary>
    private void TransitionPupil(BitmapImage bmp)
    {
        VisionOrb.BeginAnimation(OpacityProperty, null);
        VisionOrb.Opacity = 1;
        VisionOrb.Visibility = Visibility.Visible;
        VisionLid.Visibility = Visibility.Visible;

        var down = new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(150)))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn },
        };
        down.Completed += (_, _) =>
        {
            VisionOrbBrush.ImageSource = bmp;   // swap while hidden behind the closed lid

            // Reveal: the lid sweeps UP while the new image cross-fades in over the same (longer) stroke,
            // the fade outlasting the sweep slightly so the image settles in step with the opening lid.
            var up = new DoubleAnimation(1, 0, new Duration(TimeSpan.FromMilliseconds(340)))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            };
            up.Completed += (_, _) => VisionLid.Visibility = Visibility.Collapsed;

            VisionOrbImage.BeginAnimation(OpacityProperty, null);
            VisionOrbImage.Opacity = 0;
            VisionOrbImage.BeginAnimation(OpacityProperty,
                new DoubleAnimation(0, PupilSceneOpacity, new Duration(TimeSpan.FromMilliseconds(420))));

            VisionLidScale.BeginAnimation(ScaleTransform.ScaleYProperty, up);
        };
        VisionLidScale.BeginAnimation(ScaleTransform.ScaleYProperty, down);
    }

    /// <summary>Starts the pupil filler cycle (idempotent) — cycles recent scenes when renders stall.</summary>
    private void StartPupilFiller()
    {
        _fillerTimer ??= new DispatcherTimer { Interval = FillerTick };
        _fillerTimer.Tick -= OnPupilFillerTick;
        _fillerTimer.Tick += OnPupilFillerTick;
        _fillerTimer.Start();
    }

    /// <summary>Stops the filler cycle and drops the buffer (on page close).</summary>
    private void StopPupilFiller()
    {
        _fillerTimer?.Stop();
        _pupilBuffer.Clear();
        _pupilHashes.Clear();
        _fillerIndex = 0;
        _fillerBeatIndex = 0;
        _lastFillerAdvanceUtc = DateTime.MinValue;
        _lastPupilUpdateUtc = DateTime.MinValue;
    }

    /// <summary>
    /// Fills gaps when a render stalls, differently by mode: in LIVE it cycles only the recent IMAGE
    /// buffer (the organic "blink through the buildup") and leaves the mind-map on the current topic —
    /// a live topic changes only when a genuinely new beat arrives. In REVIEW (Live pill showing) it
    /// auto-advances the whole timeline as a synchronized topic + scene slideshow.
    /// </summary>
    private void OnPupilFillerTick(object? sender, EventArgs e)
    {
        if (!_visionOpen) return;

        var now = DateTime.UtcNow;

        // REVIEW: walk the whole timeline as a synchronized topic + scene slideshow.
        if (LivePill.Visibility == Visibility.Visible)
        {
            if (_pillHovered) return;   // reading a pill's detail — hold this topic, don't auto-advance
            if (_visionBeats.Count < 2 || now - _lastFillerAdvanceUtc < ReviewSlideInterval) return;
            _lastFillerAdvanceUtc = now;
            _fillerBeatIndex = (_fillerBeatIndex + 1) % _visionBeats.Count;
            var beat = _visionBeats[_fillerBeatIndex];
            SetVisionDiagram(beat.Diagram);
            ShowPupilFromPath(beat.ScenePath, holdClock: true);
            return;
        }

        // LIVE: on a stall/null, blink through recent IMAGES only — keep the current topic put.
        if (now - _lastPupilUpdateUtc >= FillerIdle && _pupilBuffer.Count >= 2
            && now - _lastFillerAdvanceUtc >= BlinkInterval)
        {
            _lastFillerAdvanceUtc = now;
            _fillerIndex = (_fillerIndex + 1) % _pupilBuffer.Count;
            TransitionPupil(_pupilBuffer[_fillerIndex]);
        }
    }

    /// <summary>
    /// Adds a fresh scene to the filler buffer, skipping images near-identical (by average-hash) to one
    /// already buffered — so the filler cycle never re-shows the same picture, which would only reinforce
    /// the oatmeal. The fresh render is still displayed regardless.
    /// </summary>
    private void AddToPupilBuffer(BitmapImage bmp)
    {
        var hash = AverageHash(bmp);
        bool duplicate = _pupilHashes.Any(h => HammingDistance(h, hash) <= PupilDupDistance);
        if (!duplicate)
        {
            _pupilBuffer.Add(bmp);
            _pupilHashes.Add(hash);
            if (_pupilBuffer.Count > PupilBufferMax)
            {
                _pupilBuffer.RemoveAt(0);
                _pupilHashes.RemoveAt(0);
            }
        }
        _fillerIndex = _pupilBuffer.Count - 1;
    }

    /// <summary>Computes a 64-bit average-hash (8×8 grayscale) perceptual fingerprint of an image.</summary>
    private static ulong AverageHash(BitmapSource source)
    {
        var scaled = new TransformedBitmap(source, new ScaleTransform(8.0 / source.PixelWidth, 8.0 / source.PixelHeight));
        var gray = new FormatConvertedBitmap(scaled, PixelFormats.Gray8, null, 0);
        var pixels = new byte[64];
        gray.CopyPixels(pixels, 8, 0);

        int sum = 0;
        for (int i = 0; i < 64; i++) sum += pixels[i];
        int average = sum / 64;

        ulong hash = 0;
        for (int i = 0; i < 64; i++)
            if (pixels[i] >= average) hash |= 1UL << i;
        return hash;
    }

    /// <summary>The number of differing bits between two hashes (Hamming distance).</summary>
    private static int HammingDistance(ulong a, ulong b) => System.Numerics.BitOperations.PopCount(a ^ b);

    /// <summary>Clears the orb image so the eye shows its plain red cornea (idle / conjuring state).</summary>
    private void HideVisionOrb()
    {
        VisionOrb.BeginAnimation(OpacityProperty, null);
        VisionOrb.Opacity = 0;
        VisionOrb.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Cross-fades the pupil image out to reveal the red sound-reactive cornea — used when a new topic
    /// rolls in ahead of its scene, so the eye rests red (aligned with the topic change) until the new
    /// image is ready to cross-fade back in. No-op when the pupil is already the bare eye.
    /// </summary>
    public void FadePupilToEye()
    {
        if (VisionOrb.Visibility != Visibility.Visible || VisionOrb.Opacity < 0.01) return;
        var fade = new DoubleAnimation(0, new Duration(TimeSpan.FromMilliseconds(1100)))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut },
        };
        fade.Completed += (_, _) =>
        {
            if (VisionOrb.Opacity >= 0.01) return;   // a new image re-showed mid-fade
            VisionOrb.Visibility = Visibility.Collapsed;
            VisionOrbBrush.ImageSource = null;       // clear so the reveal doesn't flash the old scene
        };
        VisionOrb.BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>Maps a node colour word to a saturated fill colour (also used for its glow and connector).</summary>
    private static Color DiagramColor(string? word) => (word?.Trim().ToLowerInvariant()) switch
    {
        "green" => Color.FromRgb(0x22, 0xC5, 0x5E),
        "orange" => Color.FromRgb(0xF5, 0x9E, 0x0B),
        "purple" => Color.FromRgb(0xA8, 0x55, 0xF7),
        "red" => Color.FromRgb(0xEF, 0x44, 0x44),
        _ => Color.FromRgb(0x3B, 0x82, 0xF6),   // blue default
    };

    /// <summary>
    /// Draws the diagram class NATIVELY: a radial mind-map behind the eye — a title plus up to five
    /// colour-coded node pills spaced on a ring around the centred eye (the hub), each joined by a thin
    /// connector. Deterministic layout means the eye is always exactly concentric (unlike a generated
    /// image's wandering hole). Crossfades from the previous diagram.
    /// </summary>
    /// <param name="concept">The structured infographic concept (title + nodes).</param>
    public void SetVisionDiagram(InfographicConcept concept)
    {
        if (concept is null) return;

        var visual = BuildDiagram(concept);
        visual.Opacity = 0;
        DiagramLayer.Children.Add(visual);

        var dur = new Duration(TimeSpan.FromMilliseconds(600));
        visual.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, dur));

        foreach (UIElement child in DiagramLayer.Children)
        {
            if (ReferenceEquals(child, visual)) continue;
            var captured = child;
            var fade = new DoubleAnimation(child.Opacity, 0, dur);
            fade.Completed += (_, _) => DiagramLayer.Children.Remove(captured);
            child.BeginAnimation(OpacityProperty, fade);
        }
    }

    /// <summary>Clears the diagram layer (on close / reset) so a stale diagram doesn't linger.</summary>
    private void ClearVisionDiagram()
    {
        foreach (UIElement child in DiagramLayer.Children)
            child.BeginAnimation(OpacityProperty, null);
        DiagramLayer.Children.Clear();
    }

    /// <summary>Builds one radial mind-map visual filling the diagram layer; nodes are laid out on load / resize.</summary>
    private FrameworkElement BuildDiagram(InfographicConcept concept)
    {
        var nodes = (concept.Nodes ?? [])
            .Where(n => !string.IsNullOrWhiteSpace(n.Label))
            .Take(5)
            .ToList();

        var root = new Grid();
        var canvas = new Canvas();
        root.Children.Add(canvas);

        var title = new TextBlock
        {
            Text = concept.Title ?? string.Empty,
            Foreground = new SolidColorBrush(Color.FromRgb(0xF0, 0xF3, 0xF6)),
            FontFamily = new FontFamily("Segoe UI Variable, Segoe UI"),
            FontSize = 30,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
            VerticalAlignment = System.Windows.VerticalAlignment.Top,
            Margin = new Thickness(40, 48, 40, 0),   // span the window width; wrap only near the edges
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };
        root.Children.Add(title);

        void Relayout(object? s, EventArgs e) => LayoutDiagramNodes(canvas, nodes);
        root.Loaded += Relayout;
        root.SizeChanged += (_, _) => LayoutDiagramNodes(canvas, nodes);
        return root;
    }

    /// <summary>Positions the connector ring, lines, and node pills around the centre of the canvas.</summary>
    private void LayoutDiagramNodes(Canvas canvas, IReadOnlyList<InfographicNode> nodes)
    {
        canvas.Children.Clear();
        _pillHovered = false;   // pills are recreated here; any prior hover state is stale
        double w = canvas.ActualWidth, h = canvas.ActualHeight;
        if (w < 20 || h < 20 || nodes.Count == 0) return;

        double cx = w / 2, cy = h / 2;
        double radius = Math.Max(170, Math.Min(cx, cy) - 130);   // ring sits outside the ~180px eye

        var ring = new Ellipse
        {
            Width = radius * 2,
            Height = radius * 2,
            Stroke = new SolidColorBrush(Color.FromArgb(0x28, 0xFF, 0xFF, 0xFF)),
            StrokeThickness = 1,
        };
        Canvas.SetLeft(ring, cx - radius);
        Canvas.SetTop(ring, cy - radius);
        canvas.Children.Add(ring);

        for (int i = 0; i < nodes.Count; i++)
        {
            double angle = -Math.PI / 2 + i * 2 * Math.PI / nodes.Count;   // start at top, clockwise
            double nx = cx + radius * Math.Cos(angle);
            double ny = cy + radius * Math.Sin(angle);
            var fill = DiagramColor(nodes[i].Color);

            var line = new Line
            {
                X1 = cx,
                Y1 = cy,
                X2 = nx,
                Y2 = ny,
                Stroke = new SolidColorBrush(Color.FromArgb(0x66, fill.R, fill.G, fill.B)),
                StrokeThickness = 1.5,
            };
            canvas.Children.Add(line);   // the inner part is occluded by the eye on top

            var pill = new Border
            {
                CornerRadius = new CornerRadius(22),
                Background = new SolidColorBrush(fill),
                Padding = new Thickness(18, 10, 18, 10),
                Effect = new DropShadowEffect { Color = fill, BlurRadius = 26, ShadowDepth = 0, Opacity = 0.55 },
                Child = new TextBlock
                {
                    Text = nodes[i].Label,
                    Foreground = new SolidColorBrush(Colors.White),
                    FontFamily = new FontFamily("Segoe UI Variable, Segoe UI"),
                    FontSize = 17,
                    FontWeight = FontWeights.SemiBold,
                    TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 150,
                },
            };
            pill.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(pill, nx - pill.DesiredSize.Width / 2);
            Canvas.SetTop(pill, ny - pill.DesiredSize.Height / 2);
            canvas.Children.Add(pill);

            // Hover a pill to reveal its detail; placed outward (above top-half nodes, below bottom-half).
            if (!string.IsNullOrWhiteSpace(nodes[i].Detail))
                AttachDetailPopup(canvas, pill, nodes[i].Detail, fill, ny < cy);
        }
    }

    /// <summary>Wires a hover popup to a node pill, revealing its detail on an Oracle-styled bubble.</summary>
    private void AttachDetailPopup(Canvas canvas, Border pill, string detail, Color accent, bool aboveCentre)
    {
        var popup = new System.Windows.Controls.Primitives.Popup
        {
            PlacementTarget = pill,
            Placement = aboveCentre
                ? System.Windows.Controls.Primitives.PlacementMode.Top
                : System.Windows.Controls.Primitives.PlacementMode.Bottom,
            AllowsTransparency = true,
            PopupAnimation = System.Windows.Controls.Primitives.PopupAnimation.Fade,
            StaysOpen = true,
            Child = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x16, 0x16, 0x1A)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x99, accent.R, accent.G, accent.B)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(13, 9, 13, 9),
                Margin = new Thickness(8),
                MaxWidth = 260,
                Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 18, ShadowDepth = 2, Opacity = 0.6 },
                Child = new TextBlock
                {
                    Text = detail,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xDE, 0xE3, 0xE8)),
                    FontFamily = new FontFamily("Segoe UI Variable, Segoe UI"),
                    FontSize = 14,
                    TextWrapping = TextWrapping.Wrap,
                },
            },
        };

        pill.Cursor = System.Windows.Input.Cursors.Hand;
        // Hovering a pill holds the review slideshow so the user can read the detail without the topic changing.
        pill.MouseEnter += (_, _) => { popup.IsOpen = true; _pillHovered = true; _lastFillerAdvanceUtc = DateTime.UtcNow; };
        pill.MouseLeave += (_, _) => { popup.IsOpen = false; _pillHovered = false; _lastFillerAdvanceUtc = DateTime.UtcNow; };
        canvas.Children.Add(popup);
    }

    // ── Vision timeline (history rail) ──

    /// <summary>A past Vision beat kept for the timeline rail: its diagram + on-disk scene path.</summary>
    private sealed record VisionBeat(InfographicConcept Diagram, string? ScenePath);
    private readonly List<VisionBeat> _visionBeats = new();
    /// <summary>Bounds the rail's UI-element count (scenes live on disk, so this no longer bounds RAM).</summary>
    private const int VisionHistoryMax = 60;

    /// <summary>Per-run temp folder that full-res scene PNGs are spilled to, so RAM stays flat with session length.</summary>
    private string? _visionCacheDir;
    private int _sceneSeq;

    /// <summary>Raised when the user opens a past beat for review (host should pause the live loop).</summary>
    public event Action? VisionReviewRequested;
    /// <summary>Raised when the user returns to the live present (host should resume the loop).</summary>
    public event Action? VisionLiveRequested;

    /// <summary>Records a completed beat as a rail card, spilling its scene image to disk (thumbnail kept in RAM).</summary>
    public void AddVisionBeat(InfographicConcept diagram, byte[]? scene)
    {
        if (diagram is null) return;
        var beat = new VisionBeat(diagram, scene is not null ? WriteSceneToCache(scene) : null);
        _visionBeats.Add(beat);
        while (_visionBeats.Count > VisionHistoryMax && HistoryRail.Children.Count > 0)
        {
            TryDeleteCache(_visionBeats[0].ScenePath);   // reclaim the dropped beat's disk file
            _visionBeats.RemoveAt(0);
            HistoryRail.Children.RemoveAt(0);
        }
        HistoryRail.Children.Add(BuildHistoryCard(beat, scene));
        HistoryRailPanel.Visibility = Visibility.Visible;
        HistoryScroll.ScrollToBottom();
        _fillerBeatIndex = _visionBeats.Count - 1;   // a new live beat is the newest; the review slideshow starts from here
    }

    /// <summary>Builds a clickable timeline card: a small scene thumbnail (or accent block) + the beat title.</summary>
    private FrameworkElement BuildHistoryCard(VisionBeat beat, byte[]? sceneBytes)
    {
        FrameworkElement thumb;
        if (sceneBytes is not null)
        {
            var bmp = new BitmapImage();
            using (var ms = new MemoryStream(sceneBytes))
            {
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = 148;   // thumbnail only — the full image stays on disk
                bmp.StreamSource = ms;
                bmp.EndInit();
            }
            bmp.Freeze();
            thumb = new Border
            {
                Height = 84,
                CornerRadius = new CornerRadius(6),
                Background = new ImageBrush(bmp) { Stretch = Stretch.UniformToFill },
            };
        }
        else
        {
            var accent = DiagramColor(beat.Diagram.Nodes is { Count: > 0 } ? beat.Diagram.Nodes[0].Color : "blue");
            thumb = new Border
            {
                Height = 84,
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(Color.FromArgb(0x4D, accent.R, accent.G, accent.B)),
                BorderBrush = new SolidColorBrush(accent),
                BorderThickness = new Thickness(1),
            };
        }

        var stack = new StackPanel();
        stack.Children.Add(thumb);
        stack.Children.Add(new TextBlock
        {
            Text = beat.Diagram.Title ?? string.Empty,
            Foreground = new SolidColorBrush(Color.FromRgb(0xC8, 0xCD, 0xD4)),
            FontFamily = new FontFamily("Segoe UI Variable, Segoe UI"),
            FontSize = 11.5,
            TextWrapping = TextWrapping.Wrap,
            MaxHeight = 32,
            Margin = new Thickness(2, 5, 2, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });

        var card = new Border
        {
            Margin = new Thickness(0, 0, 0, 10),
            Padding = new Thickness(6),
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromRgb(0x14, 0x16, 0x1A)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Cursor = System.Windows.Input.Cursors.Hand,
            Child = stack,
        };
        card.MouseLeftButtonUp += (_, _) => ShowHistoryBeat(beat);
        return card;
    }

    /// <summary>Enters review: shows a past beat's diagram + scene (decoded from disk) and reveals the Live button.</summary>
    private void ShowHistoryBeat(VisionBeat beat)
    {
        SetVisionDiagram(beat.Diagram);
        ShowPupilFromPath(beat.ScenePath, holdClock: true);
        LivePill.Visibility = Visibility.Visible;
        _fillerBeatIndex = Math.Max(0, _visionBeats.IndexOf(beat));   // slideshow resumes from the chosen beat
        _lastFillerAdvanceUtc = DateTime.UtcNow;                      // dwell on it before auto-advancing
        VisionReviewRequested?.Invoke();
    }

    /// <summary>Returns from review to the live present (the most recent beat) and resumes the loop.</summary>
    private void OnLivePillClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        LivePill.Visibility = Visibility.Collapsed;
        if (_visionBeats.Count > 0)
        {
            var latest = _visionBeats[^1];
            SetVisionDiagram(latest.Diagram);
            ShowPupilFromPath(latest.ScenePath, holdClock: true);
        }
        VisionLiveRequested?.Invoke();
    }

    /// <summary>Decodes a scene PNG from disk into the pupil. <paramref name="holdClock"/> stalls the idle cycle (review); otherwise the recap keeps walking.</summary>
    private void ShowPupilFromPath(string? path, bool holdClock)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
        BitmapImage bmp;
        try
        {
            bmp = new BitmapImage();
            using var fs = File.OpenRead(path);
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = fs;
            bmp.EndInit();
            bmp.Freeze();
        }
        catch { return; }
        StopScrying();
        if (holdClock) _lastPupilUpdateUtc = DateTime.UtcNow;   // review holds; idle recap keeps cycling
        TransitionPupil(bmp);
    }

    /// <summary>Writes a full-res scene PNG to the per-run disk cache; returns its path (null on failure).</summary>
    private string? WriteSceneToCache(byte[] png)
    {
        try
        {
            var path = System.IO.Path.Combine(CacheDir(), $"scene-{_sceneSeq++:D5}.png");
            File.WriteAllBytes(path, png);
            return path;
        }
        catch { return null; }
    }

    /// <summary>Lazily creates the per-run scene cache dir, sweeping orphaned dirs left by dead runs.</summary>
    private string CacheDir()
    {
        if (_visionCacheDir is null)
        {
            var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Hark");
            try
            {
                if (Directory.Exists(root))
                    foreach (var d in Directory.GetDirectories(root, "vision-*"))
                        try { Directory.Delete(d, true); } catch { /* another run may hold it */ }
            }
            catch { /* best effort */ }
            _visionCacheDir = System.IO.Path.Combine(root, "vision-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_visionCacheDir);
        }
        return _visionCacheDir;
    }

    private static void TryDeleteCache(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort */ }
    }

    /// <summary>Clears the timeline history + rail and deletes the on-disk scene cache (on session reset).</summary>
    private void ClearVisionHistory()
    {
        _visionBeats.Clear();
        HistoryRail.Children.Clear();
        HistoryRailPanel.Visibility = Visibility.Collapsed;
        LivePill.Visibility = Visibility.Collapsed;
        _fillerBeatIndex = 0;
        _lastFillerAdvanceUtc = DateTime.MinValue;
        PurgeVisionCache();
    }

    /// <summary>Deletes this run's on-disk scene cache dir (best-effort, no UI) — safe to call on shutdown.</summary>
    public void PurgeVisionCache()
    {
        if (_visionCacheDir is null) return;
        try { if (Directory.Exists(_visionCacheDir)) Directory.Delete(_visionCacheDir, true); } catch { /* best effort */ }
        _visionCacheDir = null;
        _sceneSeq = 0;
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

    /// <summary>Remembers the folder of the last saved report, so the picker reopens there.</summary>
    private string? _lastReportDir;

    /// <summary>Report writers offered in the Save picker, in filter order (first = default extension).</summary>
    private readonly IReadOnlyList<IReportWriter> _reportWriters = new IReportWriter[]
    {
        new MarkdownReportWriter(),
        new DocxReportWriter(),
        new PptxReportWriter(),
        new HtmlReportWriter(),
        new PdfReportWriter(),
    };

    /// <summary>
    /// Snapshots the session (transcript, recaps, vision slideshow) and asks the user where — and in
    /// which format — to save it. Self-contained, so it persists past the temp scene cache (cleared on toggle).
    /// </summary>
    private async void SaveReport()
    {
        var transcript = FullText();
        if (string.IsNullOrWhiteSpace(transcript) && _visionBeats.Count == 0) return;   // nothing captured yet

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Save Hark report",
            FileName = $"Hark-{DateTime.Now:yyyyMMdd-HHmmss}",
            Filter = string.Join("|", _reportWriters.Select(w => $"{w.FilterName} (*{w.Extension})|*{w.Extension}")),
            AddExtension = true,
            OverwritePrompt = true,
            InitialDirectory = _lastReportDir
                ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dialog.ShowDialog(this) != true) return;   // user cancelled the picker

        var ext = System.IO.Path.GetExtension(dialog.FileName);
        var writer = _reportWriters.FirstOrDefault(w => string.Equals(w.Extension, ext, StringComparison.OrdinalIgnoreCase))
                     ?? _reportWriters[Math.Clamp(dialog.FilterIndex - 1, 0, _reportWriters.Count - 1)];

        // Progress feedback for the whole save — generating the recaps and writing (with embedded scenes)
        // can take a few seconds, so show a busy state and only confirm once the file is on disk.
        SaveButton.IsEnabled = false;
        SaveButton.Content = "\uE895";   // sync glyph
        SaveButton.ToolTip = "Saving\u2026";
        try
        {
            // Ensure the recaps exist even if the user never opened SUMMARY — the host generates them on demand.
            if (ReportRecapsRequested is { } fetch && !string.IsNullOrWhiteSpace(transcript))
            {
                SaveButton.ToolTip = "Generating summary\u2026";
                try
                {
                    var (conversation, speakers) = await fetch();
                    if (conversation is not null) _lastRecap = conversation;
                    if (speakers is not null) _lastSpeakerRecap = speakers;
                }
                catch { /* keep whatever recaps we already have */ }
            }

            var report = BuildSessionReport();
            if (report is null) return;   // finally restores the button

            SaveButton.ToolTip = "Saving\u2026";
            await Task.Run(() => writer.WriteAsync(report, dialog.FileName));   // off the UI thread
            _lastReportDir = System.IO.Path.GetDirectoryName(dialog.FileName);

            SaveButton.Content = "\uE73E";   // checkmark
            SaveButton.ToolTip = "Saved";
            await Task.Delay(1400);
        }
        catch { /* disk/permission hiccup — fall through and reset the button */ }
        finally
        {
            SaveButton.IsEnabled = true;
            SaveButton.Content = "\uE74E";   // back to the save glyph
            SaveButton.ToolTip = "Save a full report (transcript, summary, vision)";
        }
    }

    /// <summary>Snapshots the current session into a format-agnostic report; null when there's nothing worth saving.</summary>
    private SessionReport? BuildSessionReport()
    {
        var transcript = FullText();
        bool hasRecap = _lastRecap is not null;
        bool hasSpeakers = _lastSpeakerRecap is { Speakers.Count: > 0 };

        var beats = new List<ReportBeat>(_visionBeats.Count);
        foreach (var b in _visionBeats)
        {
            byte[]? scene = null;
            if (!string.IsNullOrEmpty(b.ScenePath) && File.Exists(b.ScenePath))
                try { scene = File.ReadAllBytes(b.ScenePath); } catch { /* skip a missing frame */ }
            beats.Add(new ReportBeat(b.Diagram.Title ?? string.Empty, b.Diagram.Nodes ?? [], scene));
        }

        if (string.IsNullOrWhiteSpace(transcript) && !hasRecap && !hasSpeakers && beats.Count == 0)
            return null;

        // Prefer the model-generated conversation title (free from the recap call); fall back otherwise.
        var title = !string.IsNullOrWhiteSpace(_lastRecap?.Title) ? _lastRecap!.Title.Trim() : "Hark session report";

        return new SessionReport(title, DateTime.Now, transcript,
            _lastRecap, _lastSpeakerRecap, beats, OracleIconBytes());
    }

    /// <summary>The Oracle-eye icon bytes (PNG), loaded once from the app resource; null when unavailable.</summary>
    private static byte[]? _oracleIconBytes;
    private static bool _oracleIconTried;
    private static byte[]? OracleIconBytes()
    {
        if (_oracleIconTried) return _oracleIconBytes;
        _oracleIconTried = true;
        try
        {
            var info = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/Icon.png"));
            if (info is not null)
            {
                using var s = info.Stream;
                using var ms = new System.IO.MemoryStream();
                s.CopyTo(ms);
                _oracleIconBytes = ms.ToArray();
            }
        }
        catch { /* no icon resource available */ }
        return _oracleIconBytes;
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
