using System.Drawing;
using System.Reflection;
using System.Windows;
using System.Windows.Forms;
using Azure.Identity;
using Hark.Core;
using Hark.Core.Summarization;
using Hark.Core.Transcription;
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

    /// <summary>The <see cref="ConversationStore.Revision"/> that the cached recaps were generated from.</summary>
    private int _cachedRevision = -1;

    /// <summary>The <see cref="SummaryStyle"/> that the cached recaps were generated with.</summary>
    private SummaryStyle _cachedStyle;

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

        // Mic mixing is off by default; only an explicit 1/true opts in.
        var mixMic = config["HARK_MIX_MIC"];
        _mixMic = string.Equals(mixMic, "1", StringComparison.OrdinalIgnoreCase)
               || string.Equals(mixMic, "true", StringComparison.OrdinalIgnoreCase);

        // Like native Live Captions, the bar stays hidden until captions are toggled on.
        _overlay = new OverlayWindow();
        _overlay.SetRunning(false);
        _overlay.CloseRequested += () => Shutdown();
        _overlay.SpeakerSelected += OpenSpeakerWindow;
        _overlay.SummaryRequested += OnSummaryRequested;
        _overlay.MicToggleRequested += OnMicToggleRequested;
        _overlay.SetMicEnabled(_mixMic);   // reflect the configured default in the toggle

        // New speakers discovered by diarization surface as pills in the CONVERSATION index.
        _store.SpeakerAdded += speaker =>
            Dispatcher.BeginInvoke(() => _overlay?.AddSpeaker(speaker));

        // Enable the SUMMARY switch only once there are captions to summarize.
        _store.Changed += () =>
            Dispatcher.BeginInvoke(() => _overlay?.SetSummaryAvailable(_store.All.Count > 0));

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
            Icon = SystemIcons.Application,
            Visible = true,
            Text = "HARK — captions (Ctrl+Win+H)",
            ContextMenuStrip = menu,
        };

        // Double-click the tray icon toggles captions, matching the hotkey.
        tray.DoubleClick += (_, _) => ToggleAsync();
        return tray;
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

            Dispatcher.BeginInvoke(() =>
            {
                _overlay?.ClearSpeakers();
                _store.Rebuild(segments.Select(s => new ConversationStore.Entry(
                    string.IsNullOrEmpty(s.SpeakerId) ? ConversationStore.DefaultSpeaker : s.SpeakerId!,
                    s.Text)));

                // The refined transcript supersedes any cached recap.
                _cachedRecap = null;
                _cachedSpeakerRecap = null;
                _cachedRevision = -1;

                _tray?.ShowBalloonTip(
                    3000, "HARK", "Refined speaker attribution for this session.", ToolTipIcon.Info);
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

    /// <summary>Releases the hotkey, session, and tray icon when the application shuts down.</summary>
    /// <param name="e">Exit event arguments supplied by WPF.</param>
    protected override void OnExit(ExitEventArgs e)
    {
        _hotkey?.Dispose();
        _micHotkey?.Dispose();
        try { _session?.DisposeAsync().AsTask().GetAwaiter().GetResult(); } catch { /* best effort */ }
        if (_tray is not null) { _tray.Visible = false; _tray.Dispose(); }
        base.OnExit(e);
    }

    #endregion
}

