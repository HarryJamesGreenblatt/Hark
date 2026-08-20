using System.Drawing;
using System.Reflection;
using System.Windows;
using System.Windows.Forms;
using Azure.Identity;
using Hark.Core;
using Hark.Core.Summarization;
using Microsoft.Extensions.Configuration;

namespace Hark.App;

/// <summary>
/// HARK desktop host — a tray-resident captions overlay (Live Captions, done right).
/// Reuses the Hark.Core pipeline via <see cref="HarkSession"/>; a global hotkey (Ctrl+Win+H) toggles
/// capture and the on-screen overlay. No autostart: the hotkey is live only while the app runs.
/// </summary>
public partial class App : Application
{
    private OverlayWindow? _overlay;
    private NotifyIcon? _tray;
    private GlobalHotkey? _hotkey;
    private HarkSession? _session;

    private readonly ConversationStore _store = new();
    private readonly Dictionary<string, SpeakerWindow> _speakerWindows = new(StringComparer.OrdinalIgnoreCase);

    private string? _region;
    private string? _resourceId;
    private string? _aoaiEndpoint;
    private string? _aoaiDeployment;
    private bool _busy;

    private ISummarizer? _summarizer;
    private CancellationTokenSource? _summaryCts;
    private string? _cachedSummary;
    private int _cachedRevision = -1;
    private SummaryStyle _cachedStyle;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _region = Environment.GetEnvironmentVariable("HARK_SPEECH_REGION");
        _resourceId = Environment.GetEnvironmentVariable("HARK_SPEECH_RESOURCE_ID");

        // The resource ARM id embeds the subscription id, so it deliberately never lives in
        // source or launch profiles — fall back to dotnet user-secrets (dev-machine-local,
        // never committed) when the environment variables aren't set. See README.
        // Azure OpenAI (recap) settings live only in user-secrets.
        var config = new ConfigurationBuilder()
            .AddUserSecrets(Assembly.GetExecutingAssembly())
            .Build();
        _region ??= config["HARK_SPEECH_REGION"];
        _resourceId ??= config["HARK_SPEECH_RESOURCE_ID"];
        _aoaiEndpoint = config["HARK_AOAI_ENDPOINT"];
        _aoaiDeployment = config["HARK_AOAI_DEPLOYMENT"];

        // Like native Live Captions, the bar stays hidden until captions are toggled on.
        _overlay = new OverlayWindow();
        _overlay.SetRunning(false);
        _overlay.CloseRequested += () => Shutdown();
        _overlay.SpeakerSelected += OpenSpeakerWindow;
        _overlay.SummaryRequested += OnSummaryRequested;

        // New speakers discovered by diarization surface as pills in the CONVERSATION index.
        _store.SpeakerAdded += speaker =>
            Dispatcher.BeginInvoke(() => _overlay?.AddSpeaker(speaker));

        // Enable the SUMMARY switch only once there are captions to summarize.
        _store.Changed += () =>
            Dispatcher.BeginInvoke(() => _overlay?.SetSummaryAvailable(_store.All.Count > 0));

        _tray = BuildTrayIcon();

        // Global toggle: Ctrl+Win+H. Behaves like a standard Windows global hotkey.
        _hotkey = new GlobalHotkey(GlobalHotkey.ModControl | GlobalHotkey.ModWin, 0x48 /* 'H' */);
        _hotkey.Pressed += ToggleAsync;

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
                "Press Ctrl+Win+H to toggle captions. Right-click the tray icon to exit.",
                ToolTipIcon.Info);
        }
    }

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
                    diarize: true);
                _session.Error += OnSessionError;

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

    private void OnSessionError(string message) =>
        Dispatcher.BeginInvoke(() =>
        {
            _tray?.ShowBalloonTip(4000, "HARK — recognizer", message, ToolTipIcon.Warning);
            _overlay?.ShowStatus($"Recognizer: {message}");
        });

    /// <summary>Opens (or focuses) the dedicated page for a speaker selected in the index.</summary>
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
        _cachedSummary = null;
        _cachedRevision = -1;
    }

    /// <summary>
    /// Serves a cached recap when the captions are unchanged, otherwise generates a new one via
    /// Azure OpenAI. Caching is keyed on the store <see cref="ConversationStore.Revision"/> plus the
    /// requested style, so toggling back and forth without new speech doesn't re-call the service.
    /// </summary>
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
        if (_cachedSummary is not null && _cachedRevision == revision && _cachedStyle == style)
        {
            _overlay.SetSummaryText(_cachedSummary);   // unchanged captions → reuse, no API call
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
            var recap = await _summarizer.SummarizeAsync(transcript, style, cts.Token);

            if (cts.IsCancellationRequested) return;

            _cachedSummary = recap;
            _cachedRevision = revision;
            _cachedStyle = style;
            _overlay.SetSummaryText(recap);
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

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkey?.Dispose();
        try { _session?.DisposeAsync().AsTask().GetAwaiter().GetResult(); } catch { /* best effort */ }
        if (_tray is not null) { _tray.Visible = false; _tray.Dispose(); }
        base.OnExit(e);
    }
}

