using System.Drawing;
using System.Reflection;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Threading;
using Azure.Identity;
using Hark.Core;
using Hark.Core.Summarization;
using Hark.Core.Transcription;
using Hark.Oracle.Vision;
using Microsoft.Extensions.Configuration;

namespace Hark.App;

/// <summary>
/// HARK desktop host — a tray-resident captions overlay (Live Captions, done right).
/// Reuses the Hark.Core pipeline via <see cref="HarkSession"/>; global hotkeys toggle capture and
/// microphone mixing. No autostart: the hotkeys are live only while the app runs.
/// </summary>
public partial class App : Application
{
    #region Fields

    /// <summary>The single captions overlay window, created on startup and shown/hidden as capture toggles.</summary>
    private OverlayWindow? _overlay;

    /// <summary>Held for the process lifetime to enforce a single running instance.</summary>
    private System.Threading.Mutex? _singleInstance;

    /// <summary>The tray icon and its context menu used to toggle captions and exit the app.</summary>
    private NotifyIcon? _tray;

    /// <summary>The global Ctrl+Win+H hotkey that toggles captions on and off while the app runs.</summary>
    private GlobalHotkey? _hotkey;

    /// <summary>The global Ctrl+Shift+M hotkey that toggles microphone mixing while the app runs.</summary>
    private GlobalHotkey? _micHotkey;

    /// <summary>The active capture/transcription session, or <see langword="null"/> when captions are stopped.</summary>
    private HarkSession? _session;

    /// <summary>Accumulates transcript segments and speaker metadata for the current session.</summary>
    private readonly ConversationStore _store = new();

    /// <summary>Open per-speaker detail windows, keyed by speaker name (case-insensitive).</summary>
    private readonly Dictionary<string, SpeakerWindow> _speakerWindows = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Azure Speech region, sourced from the environment or user-secrets.</summary>
    private string? _region;

    /// <summary>Azure Speech resource ARM id, sourced from the environment or user-secrets.</summary>
    private string? _resourceId;

    /// <summary>Azure OpenAI endpoint used for recap summarization, sourced from user-secrets.</summary>
    private string? _aoaiEndpoint;

    /// <summary>Azure OpenAI deployment name used for recap summarization, sourced from user-secrets.</summary>
    private string? _aoaiDeployment;

    /// <summary>Azure OpenAI image deployment used for the Vision render tier, sourced from config (optional).</summary>
    private string? _aoaiImageDeployment;

    /// <summary>
    /// Whether to capture and mix the local microphone into the transcribed stream. Defaults off
    /// (loopback-only, like native Live Captions); set <c>HARK_MIX_MIC=1</c> to enable, or toggle the
    /// headset button live. Kept off by default because on speakers the mic re-captures playback and
    /// doubles the transcript — headset users opt in.
    /// </summary>
    private bool _mixMic;

    /// <summary>Guards <see cref="ToggleAsync(CancellationToken)"/> against re-entrancy while starting or stopping.</summary>
    private bool _busy;

    /// <summary>Lazily created Azure OpenAI summarizer used to generate recaps.</summary>
    private ISummarizer? _summarizer;

    /// <summary>Cancellation source for the in-flight recap request, cancelled when superseded.</summary>
    private CancellationTokenSource? _summaryCts;

    /// <summary>The most recently generated topic-pivoted (Conversation) recap, cached to avoid redundant calls.</summary>
    private MeetingRecap? _cachedRecap;

    /// <summary>The most recently generated people-pivoted (Speakers) recap, cached alongside the Conversation one.</summary>
    private SpeakerRecap? _cachedSpeakerRecap;

    /// <summary>Lazily created Vision service (art-director concept + optional render), or null when unconfigured.</summary>
    private VisionService? _vision;

    /// <summary>Cancellation for the in-flight Vision conjure, cancelled when superseded or the page closes.</summary>
    private CancellationTokenSource? _visionCts;

    /// <summary>The last rendered Vision image, reused when the eye is re-opened on unchanged captions.</summary>
    private byte[]? _cachedVisionImage;

    /// <summary>The store revision the cached Vision image was conjured from.</summary>
    private int _cachedVisionRevision = -1;

    /// <summary>Whether the full-window Vision page is currently open (drives the auto-conjure loop).</summary>
    private bool _visionPageOpen;

    /// <summary>Guards against overlapping conjures (manual open, auto beat-check).</summary>
    private bool _visionConjuring;

    /// <summary>Drives the autonomous beat trigger while the Vision page is open (Stage 2).</summary>
    private DispatcherTimer? _visionTimer;

    /// <summary>Wall-clock of the last finalized caption, for the mid-utterance debounce.</summary>
    private DateTime _lastCaptionUtc = DateTime.MinValue;

    /// <summary>Wall-clock of the last image render (set when it STARTS), the cadence floor between renders.</summary>
    private DateTime _lastVisionRenderUtc = DateTime.MinValue;

    /// <summary>Concept of the currently-displayed Vision image, so the Oracle conjures a DISTINCT next one.</summary>
    private string? _shownVisionConcept;

    /// <summary>Theme (caption) of the currently-displayed Vision image, reused when the eye is re-opened.</summary>
    private string? _shownVisionTheme;

    /// <summary>Transcript line count at the last image render, so a new beat is conjured from the material
    /// spoken SINCE the last image — not a long rolling window that keeps dragging in the earlier topic.</summary>
    private int _lastVisionRenderIndex;

    /// <summary>The <see cref="ConversationStore.Revision"/> that the cached recaps were generated from.</summary>
    private int _cachedRevision = -1;

    /// <summary>The <see cref="SummaryStyle"/> that the cached recaps were generated with.</summary>
    private SummaryStyle _cachedStyle;

    /// <summary>Lazily created live speaker-naming refiner (Oracle naming), or null when unconfigured.</summary>
    private SpeakerNamingRefiner? _namer;

    /// <summary>Drives the autonomous live naming loop while capture is running.</summary>
    private DispatcherTimer? _nameTimer;

    /// <summary>Cancellation for the in-flight naming pass, cancelled when superseded or capture stops.</summary>
    private CancellationTokenSource? _nameCts;

    /// <summary>Guards against overlapping live naming passes.</summary>
    private bool _naming;

    /// <summary>Store revision of the last naming attempt, so a pass runs only when new speech has arrived.</summary>
    private int _lastNamedRevision = -1;

    /// <summary>Wall-clock the last naming pass STARTED, the cadence floor between passes.</summary>
    private DateTime _lastNameRunUtc = DateTime.MinValue;

    #endregion

    #region Methods

    /// <summary>
    /// Wires up the overlay, tray icon, and global hotkey, and loads Azure Speech/OpenAI
    /// configuration from the environment or user-secrets.
    /// </summary>
    /// <param name="e">Startup event arguments supplied by WPF.</param>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Single-instance guard: a second launch (Start tile, startup task, or a double-click) would
        // stack another tray icon and overlay and fight over the global hotkeys. Keep the first; a
        // duplicate exits immediately.
        _singleInstance = new System.Threading.Mutex(initiallyOwned: true, @"HarryGreenblatt.Hark.SingleInstance", out bool isFirstInstance);
        if (!isFirstInstance)
        {
            Shutdown();
            return;
        }

        // Config precedence: env var > %APPDATA%\Hark\config.json > user-secrets.
        // user-secrets is dev-machine-local (never committed) but Development-only, so it doesn't
        // ship with a published exe; the external %APPDATA%\Hark\config.json fills that gap on
        // non-dev machines while staying out of the repo. The resource ARM id embeds the
        // subscription id, so it deliberately never lives in source or launch profiles. See README.
        var config = new ConfigurationBuilder()
            .AddUserSecrets(Assembly.GetExecutingAssembly())
            .AddJsonFile(HarkConfig.ExternalConfigPath, optional: true, reloadOnChange: false)
            .AddEnvironmentVariables()
            .Build();
        _region = config["HARK_SPEECH_REGION"];
        _resourceId = config["HARK_SPEECH_RESOURCE_ID"];
        _aoaiEndpoint = config["HARK_AOAI_ENDPOINT"];
        _aoaiDeployment = config["HARK_AOAI_DEPLOYMENT"];
        _aoaiImageDeployment = config["HARK_AOAI_IMAGE_DEPLOYMENT"];

        // Mic mixing is off by default; only an explicit 1/true opts in.
        var mixMic = config["HARK_MIX_MIC"];
        _mixMic = string.Equals(mixMic, "1", StringComparison.OrdinalIgnoreCase)
               || string.Equals(mixMic, "true", StringComparison.OrdinalIgnoreCase);

        // Like native Live Captions, the bar stays hidden until captions are toggled on.
        _overlay = new OverlayWindow();
        _overlay.SetRunning(false);
        _overlay.CloseRequested += () => Shutdown();
        _overlay.SpeakerSelected += OpenSpeakerWindow;
        _overlay.SpeakerRenameRequested += OnSpeakerRenameRequested;
        _overlay.SummaryRequested += OnSummaryRequested;
        _overlay.MicToggleRequested += OnMicToggleRequested;
        _overlay.VisionRequested += OnVisionRequested;
        _overlay.VisionClosed += OnVisionClosed;
        _overlay.SetMicEnabled(_mixMic);   // reflect the configured default in the toggle

        // New speakers discovered by diarization surface as pills in the CONVERSATION index.
        _store.SpeakerAdded += speaker =>
            Dispatcher.BeginInvoke(() => _overlay?.AddSpeaker(speaker));

        // Enable the SUMMARY switch only once there are captions to summarize; stamp the caption clock
        // for the Vision debounce (Changed fires on the UI thread as segments finalize).
        _store.Changed += () =>
        {
            _lastCaptionUtc = DateTime.UtcNow;
            Dispatcher.BeginInvoke(() => _overlay?.SetSummaryAvailable(_store.All.Count > 0));
        };

        _tray = BuildTrayIcon();

        // Global toggle: Ctrl+Win+H. Behaves like a standard Windows global hotkey.
        _hotkey = new GlobalHotkey(GlobalHotkey.MOD_CONTROL | GlobalHotkey.MOD_WIN, 0x48 /* 'H' */);
        _hotkey.Pressed += ToggleAsync;

        // Teams-style global mic toggle: Ctrl+Shift+M.
        _micHotkey = new GlobalHotkey(
            GlobalHotkey.MOD_CONTROL | GlobalHotkey.MOD_SHIFT, 0x4D /* 'M' */);
        _micHotkey.Pressed += ToggleMic;

        if (!_hotkey.IsRegistered)
        {
            _tray.ShowBalloonTip(
                4000, "HARK",
                "Couldn't register Ctrl+Win+H (it may be reserved). Use the tray menu to toggle captions.",
                ToolTipIcon.Warning);
        }
        else
        {
            _tray.ShowBalloonTip(
                3000, "HARK is running",
                "Ctrl+Win+H toggles captions; Ctrl+Shift+M toggles the microphone.",
                ToolTipIcon.Info);
        }

        if (!_micHotkey.IsRegistered)
        {
            _tray.ShowBalloonTip(
                4000, "HARK",
                "Couldn't register Ctrl+Shift+M (it may be in use). Use the microphone button instead.",
                ToolTipIcon.Warning);
        }
    }

    /// <summary>Builds the tray icon, its context menu, and the double-click toggle handler.</summary>
    /// <returns>The configured <see cref="NotifyIcon"/>.</returns>
    private NotifyIcon BuildTrayIcon()
    {
        var menu = new ContextMenuStrip();
        var toggleItem = new ToolStripMenuItem("Start captions (Ctrl+Win+H)", null, (_, _) => ToggleAsync());
        var exitItem = new ToolStripMenuItem("Exit HARK", null, (_, _) => Shutdown());
        menu.Items.AddRange(new ToolStripItem[] { toggleItem, new ToolStripSeparator(), exitItem });

        // Keep the toggle label in sync whenever the menu opens.
        menu.Opening += (_, _) =>
            toggleItem.Text = (_session?.IsRunning ?? false)
                ? "Stop captions (Ctrl+Win+H)"
                : "Start captions (Ctrl+Win+H)";

        var tray = new NotifyIcon
        {
            Icon = LoadAppIcon(),
            Visible = true,
            Text = "HARK — captions (Ctrl+Win+H)",
            ContextMenuStrip = menu,
        };

        // Double-click the tray icon toggles captions, matching the hotkey.
        tray.DoubleClick += (_, _) => ToggleAsync();
        return tray;
    }

    /// <summary>Loads the app's HAL-eye icon from the executable, falling back to the system icon.</summary>
    /// <returns>The tray icon to display.</returns>
    private static Icon LoadAppIcon()
    {
        try
        {
            var path = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(path))
                return Icon.ExtractAssociatedIcon(path) ?? SystemIcons.Application;
        }
        catch { /* fall through to the system icon */ }
        return SystemIcons.Application;
    }

    /// <summary>Fire-and-forget entry point for hotkey/menu/UI handlers.</summary>
    private async void ToggleAsync() => await ToggleAsync(CancellationToken.None);

    /// <summary>Starts or stops capture and captions, toggling the overlay and tray state accordingly.</summary>
    /// <param name="cancellationToken">Token used to cancel starting or stopping the session.</param>
    private async Task ToggleAsync(CancellationToken cancellationToken)
    {
        if (_busy) return;          // ignore re-entrancy while starting/stopping
        _busy = true;
        try
        {
            if (_session?.IsRunning == true)
            {
                await _session.StopAsync(cancellationToken);
                _overlay?.SetRunning(false);
                _overlay?.Hide();      // toggle "off" — the bar disappears like native Live Captions

                StopNamingLoop();

                // Second pass: re-diarize the buffered audio offline for better speaker attribution.
                _ = RefineDiarizationAsync();
            }
            else
            {
                if (string.IsNullOrWhiteSpace(_region) || string.IsNullOrWhiteSpace(_resourceId))
                {
                    _tray?.ShowBalloonTip(
                        5000, "HARK — configuration needed",
                        "Set HARK_SPEECH_REGION and HARK_SPEECH_RESOURCE_ID, then try again.",
                        ToolTipIcon.Warning);
                    return;
                }

                _overlay?.ClearText();
                ResetConversation();
                _overlay?.ShowAndActivate();   // toggle "on" — surface the bar above other windows

                // Same keyless auth as the CLI: AzureCliCredential (the az login identity).
                _session = new HarkSession(
                    _region!, _resourceId!, language: null,
                    credential: new AzureCliCredential(),
                    sink: _overlay is null ? null : new OverlaySink(_overlay, _store),
                    diarize: true,
                    captureAudio: true,
                    mixMicrophone: _mixMic);
                _session.Error += OnSessionError;
                _session.AudioLevel += OnAudioLevel;

                await _session.StartAsync(cancellationToken);
                _overlay?.SetRunning(true);
                StartNamingLoop();
            }
        }
        catch (Exception ex)
        {
            _tray?.ShowBalloonTip(5000, "HARK — error", ex.Message, ToolTipIcon.Error);
            _overlay?.SetRunning(false);
            _overlay?.ShowStatus($"Couldn't start captions: {ex.Message}");
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>Surfaces a recognizer error via a tray balloon tip and the overlay status line.</summary>
    /// <param name="message">The error message reported by the recognizer.</param>
    private void OnSessionError(string message) =>        Dispatcher.BeginInvoke(() =>
        {
            _tray?.ShowBalloonTip(4000, "HARK — recognizer", message, ToolTipIcon.Warning);
            _overlay?.ShowStatus($"Recognizer: {message}");
        });

    /// <summary>Marshals the capture audio level onto the UI to drive the sound-reactive HAL eye.</summary>
    /// <param name="level">The current normalized audio level.</param>
    private void OnAudioLevel(double level) =>
        Dispatcher.BeginInvoke(() => _overlay?.SetAudioLevel(level));

    /// <summary>
    /// Handles the overlay's mic toggle: remembers the choice (so the next session honors it) and,
    /// when a session is live, starts or stops microphone mixing immediately.
    /// </summary>
    /// <param name="enabled">Whether the microphone should be mixed into the captions.</param>
    private void OnMicToggleRequested(bool enabled)
    {
        _mixMic = enabled;
        _session?.SetMicEnabled(enabled);
    }

    /// <summary>Toggles microphone mixing from the global shortcut and synchronizes the overlay.</summary>
    private void ToggleMic()
    {
        bool enabled = !_mixMic;
        _overlay?.SetMicEnabled(enabled);
        OnMicToggleRequested(enabled);
    }

    /// <summary>
    /// After capture stops, re-diarizes the buffered session audio in one offline pass (Azure Fast
    /// Transcription) and rebuilds the conversation with more accurate, globally-clustered speaker
    /// attribution. Runs in the background; failures are surfaced quietly and leave the live result
    /// intact. The rebuilt transcript feeds the speaker pages and both recap views.
    /// </summary>
    private async Task RefineDiarizationAsync()
    {
        var session = _session;
        if (session is null || string.IsNullOrWhiteSpace(_resourceId)) return;

        var pcm = session.GetBufferedAudioPcm();
        if (pcm is null || pcm.Length < 16_000 * 2) return;   // < ~1s of audio — nothing worth refining

        try
        {
            // The live pass tends to over-segment, so hint the ceiling from its speaker count, clamped.
            int liveSpeakers = _store.Speakers.Count;
            int maxSpeakers = Math.Clamp(liveSpeakers > 0 ? liveSpeakers : 2, 2, 8);

            var refiner = new FastTranscriptionRefiner(_resourceId!, new AzureCliCredential());
            var segments = await refiner.RefineAsync(pcm, maxSpeakers);
            if (segments.Count == 0) return;

            // Recognition-mode oracle (Stage 0): an optional text-only semantic pass re-labels the
            // acoustic segments (merging over-splits, fixing cross-ups), text left immutable. Reuses the
            // recap AOAI config; skipped when unconfigured, and degrades to the acoustic result on failure.
            if (!string.IsNullOrWhiteSpace(_aoaiEndpoint) && !string.IsNullOrWhiteSpace(_aoaiDeployment))
            {
                try
                {
                    var semantic = new SemanticDiarizationRefiner(_aoaiEndpoint!, _aoaiDeployment!, new AzureCliCredential());
                    segments = await semantic.RefineAsync(segments);
                }
                catch
                {
                    // Optional enhancement — keep the acoustic result if the semantic pass fails.
                }

                // Oracle naming: auto-apply real names where the transcript identifies a speaker;
                // labels it can't confidently name stay Guest-N (correctable by hand afterward).
                try
                {
                    var namer = new SpeakerNamingRefiner(_aoaiEndpoint!, _aoaiDeployment!, new AzureCliCredential());
                    segments = await namer.NameAsync(segments);
                }
                catch
                {
                    // Optional enhancement — keep the Guest-N labels if naming fails.
                }
            }

            var rebuilt = segments;
            Dispatcher.BeginInvoke(() =>
            {
                _overlay?.ClearSpeakers();
                _store.Rebuild(rebuilt.Select(s => new ConversationStore.Entry(
                    string.IsNullOrEmpty(s.SpeakerId) ? ConversationStore.DefaultSpeaker : s.SpeakerId!,
                    s.Text)));

                // Re-render the caption transcript from the refined result so the TRANSCRIPT/LATEST view
                // reflects the corrected attribution too — not just the speaker pages and recaps.
                _overlay?.SetCaptionLines(rebuilt.Select(s =>
                    string.IsNullOrEmpty(s.SpeakerId) ? s.Text : $"{s.SpeakerId}: {s.Text}"));

                // The refined transcript supersedes any cached recap.
                _cachedRecap = null;
                _cachedSpeakerRecap = null;
                _cachedRevision = -1;
            });
        }
        catch (Exception ex)
        {
            Dispatcher.BeginInvoke(() => _tray?.ShowBalloonTip(
                4000, "HARK — refine", $"Couldn't refine speakers: {ex.Message}", ToolTipIcon.Warning));
        }
    }

    /// <summary>Opens (or focuses) the dedicated page for a speaker selected in the index.</summary>
    /// <param name="speaker">The speaker name to open or focus a window for.</param>
    private void OpenSpeakerWindow(string speaker)
    {
        if (_speakerWindows.TryGetValue(speaker, out var existing))
        {
            existing.Activate();
            return;
        }

        var window = new SpeakerWindow(_store, speaker);
        window.Closed += (_, _) => _speakerWindows.Remove(speaker);
        _speakerWindows[speaker] = window;

        // Cascade pages so multiple speakers don't stack exactly on top of each other.
        if (_overlay is not null)
        {
            int index = _speakerWindows.Count - 1;
            window.Left = _overlay.Left + 40 * index;
            window.Top = Math.Max(0, _overlay.Top - window.Height - 20 - 30 * index);
        }
        window.Show();
    }

    /// <summary>
    /// Applies a speaker rename requested from a pill: renames globally in the store, relabels the pill,
    /// re-renders the caption transcript, and rebinds (or merges) any open page for that speaker.
    /// </summary>
    /// <param name="oldName">The speaker's current label.</param>
    /// <param name="newName">The requested new label.</param>
    private void OnSpeakerRenameRequested(string oldName, string newName)
    {
        if (!_store.Rename(oldName, newName)) return;
        var applied = newName.Trim();

        _overlay?.RenameSpeaker(oldName, applied);
        _overlay?.SetCaptionLines(BuildCaptionLines());

        // Follow any open page for the renamed speaker: merge into an existing one, or rebind in place.
        if (_speakerWindows.Remove(oldName, out var window))
        {
            if (_speakerWindows.TryGetValue(applied, out var existing))
            {
                window.Close();
                existing.Activate();
            }
            else
            {
                window.Rebind(applied);
                _speakerWindows[applied] = window;
            }
        }
    }

    /// <summary>Renders the store as speaker-prefixed caption lines (unprefixed for the default speaker).</summary>
    /// <returns>The caption lines, in conversation order.</returns>
    private IEnumerable<string> BuildCaptionLines() =>
        _store.All.Select(e =>
            e.Speaker.Equals(ConversationStore.DefaultSpeaker, StringComparison.OrdinalIgnoreCase)
                ? e.Text
                : $"{e.Speaker}: {e.Text}");

    // ── Live naming cadence (Oracle) ──
    /// <summary>How often the naming loop checks whether a pass is due.</summary>
    private static readonly TimeSpan NameCheckInterval = TimeSpan.FromSeconds(8);
    /// <summary>Minimum spacing between naming passes, measured from pass START.</summary>
    private static readonly TimeSpan NameRunInterval = TimeSpan.FromSeconds(15);
    /// <summary>Earliest lines fed to a naming pass — introductions ("here he is, Mr. …") live here.</summary>
    private const int NameHeadLines = 24;
    /// <summary>Most-recent lines fed to a naming pass, bounding cost on long sessions.</summary>
    private const int NameTailLines = 120;

    /// <summary>
    /// Starts the autonomous live naming loop (skipped when AOAI is unconfigured). Mirrors the Vision
    /// beat loop: a timer periodically fires the Oracle to name still-anonymous speakers as evidence
    /// accrues, applying results through the same rename/merge path used by manual renames.
    /// </summary>
    private void StartNamingLoop()
    {
        if (string.IsNullOrWhiteSpace(_aoaiEndpoint) || string.IsNullOrWhiteSpace(_aoaiDeployment)) return;

        _namer ??= new SpeakerNamingRefiner(_aoaiEndpoint!, _aoaiDeployment!, new AzureCliCredential());
        _nameTimer ??= new DispatcherTimer { Interval = NameCheckInterval };
        _nameTimer.Tick -= OnNameTick;
        _nameTimer.Tick += OnNameTick;
        _lastNamedRevision = -1;
        _nameTimer.Start();
    }

    /// <summary>Stops the live naming loop and cancels any in-flight pass (on capture stop).</summary>
    private void StopNamingLoop()
    {
        _nameTimer?.Stop();
        _nameCts?.Cancel();
    }

    /// <summary>
    /// The autonomous loop: once speech has settled and the cadence floor has elapsed, run a naming pass
    /// over the current transcript — but only while there is still an anonymous <c>Guest-N</c> to identify.
    /// </summary>
    private async void OnNameTick(object? sender, EventArgs e)
    {
        if (_naming || _namer is null || _session?.IsRunning != true) return;
        if (_store.All.Count == 0 || !HasAnonymousSpeaker()) return;

        var now = DateTime.UtcNow;
        if (_store.Revision == _lastNamedRevision) return;      // no new speech since the last attempt
        if (now - _lastNameRunUtc < NameRunInterval) return;    // naming cadence floor

        await RunNamingPassAsync();
    }

    /// <summary>
    /// Runs one naming pass: infers real names from the conversation and applies them to still-anonymous
    /// labels via <see cref="OnSpeakerRenameRequested"/> (so live splits merge into a single name). Manual
    /// and already-resolved names are left untouched; a failure leaves the labels as they were.
    /// </summary>
    private async Task RunNamingPassAsync()
    {
        if (_namer is null) return;

        _naming = true;
        _nameCts?.Cancel();
        var cts = _nameCts = new CancellationTokenSource();
        int revision = _store.Revision;
        _lastNameRunUtc = DateTime.UtcNow;

        try
        {
            var names = await _namer.InferNamesAsync(BuildNamingSegments(), cts.Token);
            if (cts.IsCancellationRequested || _session?.IsRunning != true) return;

            _lastNamedRevision = revision;

            // Apply only to still-anonymous labels; manual/earlier names win and never flip.
            foreach (var (label, name) in names)
                if (IsAnonymousLabel(label) && !string.IsNullOrWhiteSpace(name))
                    OnSpeakerRenameRequested(label, name);
        }
        catch (OperationCanceledException)
        {
            // Superseded or capture stopped; ignore.
        }
        catch
        {
            // Optional enhancement — leave labels as-is if a live naming pass fails.
        }
        finally
        {
            _naming = false;
        }
    }

    /// <summary>Builds the transcript fed to a naming pass: the earliest lines (intros) plus the recent tail.</summary>
    /// <returns>Lightweight segments carrying each line's current label and text.</returns>
    private IReadOnlyList<TranscriptSegment> BuildNamingSegments()
    {
        var all = _store.All;
        IEnumerable<ConversationStore.Entry> chosen = all.Count <= NameHeadLines + NameTailLines
            ? all
            : all.Take(NameHeadLines).Concat(all.Skip(all.Count - NameTailLines));

        return chosen
            .Select(e => new TranscriptSegment(e.Text, IsFinal: true, TimeSpan.Zero, TimeSpan.Zero, e.Speaker))
            .ToList();
    }

    /// <summary>Whether any speaker is still an anonymous label the Oracle could try to name.</summary>
    private bool HasAnonymousSpeaker() => _store.Speakers.Any(IsAnonymousLabel);

    /// <summary>Whether a label is still anonymous (<c>Guest-N</c> or the default speaker), i.e. un-named.</summary>
    /// <param name="label">The speaker label to test.</param>
    /// <returns><see langword="true"/> when the label carries no real name.</returns>
    private static bool IsAnonymousLabel(string label)
    {
        if (string.IsNullOrEmpty(label)) return true;
        if (label.Equals(ConversationStore.DefaultSpeaker, StringComparison.OrdinalIgnoreCase)) return true;
        const string prefix = "Guest-";
        return label.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && label.Length > prefix.Length
            && int.TryParse(label.AsSpan(prefix.Length), out _);
    }

    /// <summary>Clears the conversation model, the speaker index, and any open speaker pages.</summary>
    private void ResetConversation()
    {
        foreach (var window in _speakerWindows.Values.ToArray())
            window.Close();
        _speakerWindows.Clear();

        _overlay?.ClearSpeakers();
        _store.Clear();

        // A new session invalidates any cached recap.
        _cachedRecap = null;
        _cachedSpeakerRecap = null;
        _cachedRevision = -1;

        // ...and any Vision state (a cleared store makes the render index and cache stale).
        _cachedVisionImage = null;
        _cachedVisionRevision = -1;
        _lastVisionRenderIndex = 0;
        _shownVisionConcept = null;
        _shownVisionTheme = null;
    }

    /// <summary>
    /// Serves a cached recap when the captions are unchanged, otherwise generates a new one via
    /// Azure OpenAI. Caching is keyed on the store <see cref="ConversationStore.Revision"/> plus the
    /// requested style, so toggling back and forth without new speech doesn't re-call the service.
    /// </summary>
    /// <param name="style">The requested recap style.</param>
    private async void OnSummaryRequested(SummaryStyle style)
    {
        if (_overlay is null) return;

        if (string.IsNullOrWhiteSpace(_aoaiEndpoint) || string.IsNullOrWhiteSpace(_aoaiDeployment))
        {
            _overlay.SetSummaryText(
                "Summary isn't configured. Set HARK_AOAI_ENDPOINT and HARK_AOAI_DEPLOYMENT in user-secrets.");
            return;
        }

        if (_store.All.Count == 0)
        {
            _overlay.SetSummaryText("Nothing to summarize yet — captions will appear here as a recap.");
            return;
        }

        int revision = _store.Revision;
        bool sameRevision = _cachedRevision == revision && _cachedStyle == style;

        // Both styles render as structured, expandable recaps — Conversation pivots on topics,
        // Speakers on people. Each has its own cache keyed on revision + style, so toggling between
        // them (or re-opening SUMMARY) without new speech is free.
        if (style == SummaryStyle.Conversation)
        {
            if (sameRevision && _cachedRecap is not null)
            {
                _overlay.SetStructuredRecap(_cachedRecap);
                return;
            }
        }
        else if (sameRevision && _cachedSpeakerRecap is not null)
        {
            _overlay.SetSpeakerRecap(_cachedSpeakerRecap);
            return;
        }

        // Supersede any in-flight request (e.g. rapid style changes).
        _summaryCts?.Cancel();
        var cts = _summaryCts = new CancellationTokenSource();

        _overlay.SetSummaryBusy("Generating recap…");

        var transcript = string.Join(
            Environment.NewLine,
            _store.All.Select(entry => $"{entry.Speaker}: {entry.Text}"));

        try
        {
            _summarizer ??= new AzureOpenAiSummarizer(_aoaiEndpoint!, _aoaiDeployment!, new AzureCliCredential());

            if (style == SummaryStyle.Conversation)
            {
                var recap = await _summarizer.SummarizeConversationAsync(transcript, cts.Token);

                if (cts.IsCancellationRequested) return;

                _cachedRecap = recap;
                _cachedRevision = revision;
                _cachedStyle = style;
                _overlay.SetStructuredRecap(recap);
            }
            else
            {
                var recap = await _summarizer.SummarizeSpeakersAsync(transcript, cts.Token);

                if (cts.IsCancellationRequested) return;

                _cachedSpeakerRecap = recap;
                _cachedRevision = revision;
                _cachedStyle = style;
                _overlay.SetSpeakerRecap(recap);
            }
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer request; ignore.
        }
        catch (Exception ex)
        {
            _overlay.SetSummaryText($"Couldn't generate recap: {ex.Message}");
        }
    }

    /// <summary>
    /// Handles the HAL eye dilating open: shows the Vision page and starts the autonomous beat loop.
    /// The open itself force-renders once (human-paced); the loop then re-conjures only on genuine
    /// topic shifts, rate-limited, while the page stays open.
    /// </summary>
    private async void OnVisionRequested()
    {
        if (_overlay is null) return;
        _visionPageOpen = true;
        StartVisionTimer();

        // Concept needs the chat deployment; the image additionally needs the image deployment.
        if (string.IsNullOrWhiteSpace(_aoaiEndpoint) || string.IsNullOrWhiteSpace(_aoaiDeployment))
        {
            _overlay.SetVisionStatus("Vision isn't configured. Set HARK_AOAI_ENDPOINT and HARK_AOAI_DEPLOYMENT.");
            return;
        }
        if (_store.All.Count == 0)
        {
            _overlay.SetVisionStatus("Start captioning — a visual concept of the conversation will appear here.");
            return;
        }

        _vision ??= BuildVisionService();

        // Re-opening the eye with unchanged captions: reuse the last image, no service call.
        if (_store.Revision == _cachedVisionRevision && _cachedVisionImage is not null)
        {
            _overlay.SetVisionImage(_cachedVisionImage, _shownVisionTheme ?? string.Empty);
            return;
        }

        await ConjureVisionAsync(manual: true);
    }

    /// <summary>Stops the autonomous loop and cancels any in-flight conjure when the page collapses.</summary>
    private void OnVisionClosed()
    {
        _visionPageOpen = false;
        _visionTimer?.Stop();
        _visionCts?.Cancel();
    }

    /// <summary>Starts (or restarts) the 5 s autonomous beat-trigger loop.</summary>
    private void StartVisionTimer()
    {
        _visionTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _visionTimer.Tick -= OnVisionAutoTick;
        _visionTimer.Tick += OnVisionAutoTick;
        _visionTimer.Start();
    }

    // ── Autonomous cadence tuning (Stage 2) ──
    /// <summary>Quiet gap after the last caption before a render may fire (avoids mid-utterance).</summary>
    private static readonly TimeSpan VisionDebounce = TimeSpan.FromSeconds(2.5);
    /// <summary>Minimum spacing between renders, measured from render START so it overlaps the model's own
    /// latency (the real cadence floor) rather than stacking on top of it.</summary>
    private static readonly TimeSpan VisionRenderInterval = TimeSpan.FromSeconds(12);
    /// <summary>Max transcript lines fed to a conjure — small so a shifted topic dominates the window fast.</summary>
    private const int VisionWindowLines = 16;

    /// <summary>
    /// The autonomous loop: while the page is open, once the speech has settled and the render cadence
    /// floor has elapsed since the last render STARTED, conjure a fresh vision of the newest material.
    /// The Oracle is told the vision on screen so each new one is distinct. Leaves the shown image up
    /// until the replacement lands.
    /// </summary>
    private async void OnVisionAutoTick(object? sender, EventArgs e)
    {
        if (!_visionPageOpen || _visionConjuring || _overlay is null) return;
        if (_store.All.Count == 0) return;

        // Self-heal: if the page was opened before captioning, build the service once config + speech exist.
        if (_vision is null)
        {
            if (string.IsNullOrWhiteSpace(_aoaiEndpoint) || string.IsNullOrWhiteSpace(_aoaiDeployment)) return;
            _vision = BuildVisionService();
        }

        var now = DateTime.UtcNow;
        if (_store.Revision == _cachedVisionRevision) return;       // no new speech since the last image
        if (now - _lastCaptionUtc < VisionDebounce) return;         // still mid-utterance
        if (now - _lastVisionRenderUtc < VisionRenderInterval) return;   // render cadence floor

        await ConjureVisionAsync(manual: false);
    }

    /// <summary>
    /// Runs one conjure and renders it. On the manual open it shows a busy state and windows the recent
    /// transcript; on an auto beat it stays silent (leaving the shown image up until the new one lands)
    /// and windows only the speech since the last image. Always tells the Oracle its last vision so the
    /// new one is visibly DISTINCT. Superseding and overlap-guarded.
    /// </summary>
    private async Task ConjureVisionAsync(bool manual)
    {
        if (_overlay is null || _vision is null) return;

        _visionConjuring = true;
        _visionCts?.Cancel();
        var cts = _visionCts = new CancellationTokenSource();

        int revision = _store.Revision;
        _lastVisionRenderUtc = DateTime.UtcNow;   // start-to-start cadence: overlaps the render latency

        if (manual) _overlay.SetVisionStatus("Conjuring a vision…");

        // Window the transcript to the CURRENT beat: a small recent slice, and for an auto beat never
        // reaching before the last rendered image — so the new image reflects the NEW material.
        int count = _store.All.Count;
        int floor = manual ? 0 : Math.Clamp(_lastVisionRenderIndex, 0, count);
        int start = Math.Max(floor, count - VisionWindowLines);
        var window = string.Join(
            Environment.NewLine,
            _store.All.Skip(start).Select(entry => $"{entry.Speaker}: {entry.Text}"));

        try
        {
            // Pass the last shown concept so the Oracle deliberately conjures a different scene.
            var result = await _vision.ConjureAsync(window, _shownVisionConcept, cts.Token);
            if (cts.IsCancellationRequested || result is null) return;

            if (result.Image is not null)
            {
                _cachedVisionImage = result.Image;
                _cachedVisionRevision = revision;
                _lastVisionRenderIndex = count;             // next beat is windowed from here forward
                _shownVisionConcept = result.Concept.Concept;   // remember what we just showed, to differ next
                _shownVisionTheme = result.Concept.Theme;
                _overlay.SetVisionImage(result.Image, result.Concept.Theme);
            }
            else if (manual)
            {
                // Concept-only (no render tier configured): show the art director's judgment as text.
                _overlay.SetVisionConcept(result.Concept.Concept);
            }
        }
        catch (OperationCanceledException)
        {
            // Superseded or the page was closed; ignore.
        }
        catch (Exception ex)
        {
            if (manual) _overlay.SetVisionStatus($"Couldn't conjure a vision: {ex.Message}");
        }
        finally
        {
            _visionConjuring = false;
        }
    }

    /// <summary>Builds the Vision service: the art-director concept tier, plus the render tier when an image deployment is configured.</summary>
    private VisionService BuildVisionService()
    {
        var designer = new ConceptDesigner(_aoaiEndpoint!, _aoaiDeployment!, new AzureCliCredential());
        var renderer = string.IsNullOrWhiteSpace(_aoaiImageDeployment)
            ? null
            : new VisionRenderer(_aoaiEndpoint!, _aoaiImageDeployment!, new AzureCliCredential());
        return new VisionService(designer, renderer);
    }

    /// <summary>Releases the hotkey, session, and tray icon when the application shuts down.</summary>
    /// <param name="e">Exit event arguments supplied by WPF.</param>
    protected override void OnExit(ExitEventArgs e)
    {
        _hotkey?.Dispose();
        _micHotkey?.Dispose();
        _visionTimer?.Stop();
        _visionCts?.Cancel();
        try { _session?.DisposeAsync().AsTask().GetAwaiter().GetResult(); } catch { /* best effort */ }
        if (_tray is not null) { _tray.Visible = false; _tray.Dispose(); }
        base.OnExit(e);
    }

    #endregion
}

