using System.Drawing;
using System.Reflection;
using System.Windows;
using System.Windows.Forms;
using Azure.Identity;
using Hark.Core;
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

    private string? _region;
    private string? _resourceId;
    private bool _busy;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _region = Environment.GetEnvironmentVariable("HARK_SPEECH_REGION");
        _resourceId = Environment.GetEnvironmentVariable("HARK_SPEECH_RESOURCE_ID");

        // The resource ARM id embeds the subscription id, so it deliberately never lives in
        // source or launch profiles — fall back to dotnet user-secrets (dev-machine-local,
        // never committed) when the environment variables aren't set. See README.
        if (string.IsNullOrWhiteSpace(_region) || string.IsNullOrWhiteSpace(_resourceId))
        {
            var config = new ConfigurationBuilder()
                .AddUserSecrets(Assembly.GetExecutingAssembly())
                .Build();
            _region ??= config["HARK_SPEECH_REGION"];
            _resourceId ??= config["HARK_SPEECH_RESOURCE_ID"];
        }

        // Like native Live Captions, the bar stays hidden until captions are toggled on.
        _overlay = new OverlayWindow();
        _overlay.SetRunning(false);
        _overlay.CloseRequested += () => Shutdown();

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
                _overlay?.ShowAndActivate();   // toggle "on" — surface the bar above other windows

                // Same keyless auth as the CLI: AzureCliCredential (the az login identity).
                _session = new HarkSession(
                    _region!, _resourceId!, language: null,
                    credential: new AzureCliCredential(),
                    sink: _overlay is null ? null : new OverlaySink(_overlay));
                _session.Error += OnSessionError;

                await _session.StartAsync(cancellationToken);
                _overlay?.SetRunning(true);
            }
        }
        catch (Exception ex)
        {
            _tray?.ShowBalloonTip(5000, "HARK — error", ex.Message, ToolTipIcon.Error);
            _overlay?.SetRunning(false);
        }
        finally
        {
            _busy = false;
        }
    }

    private void OnSessionError(string message) =>
        Dispatcher.BeginInvoke(() =>
            _tray?.ShowBalloonTip(4000, "HARK — recognizer", message, ToolTipIcon.Warning));

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkey?.Dispose();
        try { _session?.DisposeAsync().AsTask().GetAwaiter().GetResult(); } catch { /* best effort */ }
        if (_tray is not null) { _tray.Visible = false; _tray.Dispose(); }
        base.OnExit(e);
    }
}

