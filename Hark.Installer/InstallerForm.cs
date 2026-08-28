using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace Hark.Installer;

/// <summary>
/// A single-window installer: trusts the signing cert, installs the embedded MSIX, then — only if
/// HARK isn't already configured — collects the (non-secret) Azure resource locations and writes
/// <c>%APPDATA%\Hark\config.json</c> so the user never has to hunt down the README.
/// </summary>
internal sealed class InstallerForm : Form
{
    // ── HAL palette ──
    static readonly Color Plate      = Color.FromArgb(12, 12, 12);
    static readonly Color Panel      = Color.FromArgb(20, 20, 22);
    static readonly Color Field      = Color.FromArgb(30, 30, 34);
    static readonly Color HalRed     = Color.FromArgb(255, 40, 26);
    static readonly Color HalDeep    = Color.FromArgb(150, 10, 4);
    static readonly Color TextDim    = Color.FromArgb(200, 205, 212);
    static readonly Color TextBright = Color.FromArgb(240, 244, 248);

    readonly Label _statusLabel;
    readonly ProgressBar _progressBar;
    readonly Button _actionButton;
    readonly Panel _configPanel;
    readonly TextBox _regionBox;
    readonly TextBox _resourceBox;
    readonly TextBox _aoaiEndpointBox;
    readonly TextBox _aoaiDeploymentBox;
    readonly TextBox _aoaiImageDeploymentBox;

    enum Phase { Install, Configure, Done }
    Phase _phase = Phase.Install;

    /// <summary>Non-secret external config the desktop app reads at runtime.</summary>
    static string ConfigPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Hark", "config.json");

    /// <summary>Hark.App's user-secrets file — the app loads it too (the id is compiled into the assembly).</summary>
    static string UserSecretsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Microsoft", "UserSecrets", "d2185b7b-b2db-438e-9a76-f08d51c63093", "secrets.json");

    public InstallerForm()
    {
        // Layout is authored in 96-dpi units; scale it to the actual monitor (paired with the
        // PerMonitorV2 awareness in the csproj) so the dialog isn't cramped on high-DPI displays.
        AutoScaleDimensions = new SizeF(96f, 96f);
        AutoScaleMode = AutoScaleMode.Dpi;

        Text = "HARK Setup";
        ClientSize = new Size(580, 558);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = Plate;

        try
        {
            using var iconStream = typeof(Program).Assembly.GetManifestResourceStream("Icon.png");
            if (iconStream is not null)
            {
                using var bmp = new Bitmap(iconStream);
                Icon = Icon.FromHandle(bmp.GetHicon());
            }
        }
        catch { /* form icon is cosmetic */ }

        var topBar = new Panel { Location = new Point(0, 0), Size = new Size(580, 96), BackColor = Panel };
        Controls.Add(topBar);

        int titleX = 24;
        try
        {
            using var iconStream = typeof(Program).Assembly.GetManifestResourceStream("Icon.png");
            if (iconStream is not null)
            {
                topBar.Controls.Add(new PictureBox
                {
                    Image = new Bitmap(iconStream),
                    SizeMode = PictureBoxSizeMode.Zoom,
                    Size = new Size(56, 56),
                    Location = new Point(20, 20),
                    BackColor = Color.Transparent,
                });
                titleX = 92;
            }
        }
        catch { /* logo is cosmetic */ }

        topBar.Controls.Add(new Label
        {
            Text = "HARK",
            Font = new Font("Segoe UI", 24f, FontStyle.Bold),
            ForeColor = TextBright,
            AutoSize = true,
            Location = new Point(titleX, 12),
            BackColor = Color.Transparent,
        });
        topBar.Controls.Add(new Label
        {
            Text = "Hear. Adapt. Recognize. Keep.  -  Setup",
            Font = new Font("Segoe UI", 10.5f),
            ForeColor = TextDim,
            AutoSize = true,
            Location = new Point(titleX + 2, 58),
            BackColor = Color.Transparent,
        });

        _statusLabel = new Label
        {
            Text = "Click Install to set up HARK. It lives in the system tray; press Ctrl+Win+H to caption.",
            Font = new Font("Segoe UI", 10.5f),
            ForeColor = TextDim,
            Size = new Size(540, 44),
            Location = new Point(20, 112),
        };
        Controls.Add(_statusLabel);

        _progressBar = new ProgressBar
        {
            Location = new Point(20, 158),
            Size = new Size(540, 18),
            Minimum = 0,
            Maximum = 100,
            Value = 0,
        };
        Controls.Add(_progressBar);

        // ── Config panel (revealed after install only when config.json is absent) ──
        _configPanel = new Panel { Location = new Point(20, 190), Size = new Size(540, 290), Visible = false };
        Controls.Add(_configPanel);
        _regionBox         = AddConfigField(0,   "Azure Speech region",          "e.g. eastus2");
        _resourceBox       = AddConfigField(58,  "Speech resource ID (ARM id)",  "/subscriptions/.../Microsoft.CognitiveServices/accounts/...");
        _aoaiEndpointBox   = AddConfigField(116, "Azure OpenAI endpoint (optional — enables SUMMARY)", "https://<name>.openai.azure.com/");
        _aoaiDeploymentBox = AddConfigField(174, "OpenAI chat deployment (optional)", "e.g. gpt-4.1-mini");
        _aoaiImageDeploymentBox = AddConfigField(232, "OpenAI image deployment (optional — enables Vision)", "e.g. gpt-image-1");

        var bottomPanel = new Panel { Location = new Point(0, 488), Size = new Size(580, 70), BackColor = Panel };
        Controls.Add(bottomPanel);
        bottomPanel.Controls.Add(new Panel { Location = new Point(0, 0), Size = new Size(580, 2), BackColor = HalRed });

        _actionButton = new Button
        {
            Text = "Install",
            Font = new Font("Segoe UI Semibold", 12f),
            Size = new Size(180, 40),
            Location = new Point(200, 16),
            BackColor = HalDeep,
            ForeColor = TextBright,
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
        };
        _actionButton.FlatAppearance.BorderColor = HalRed;
        _actionButton.FlatAppearance.BorderSize = 1;
        _actionButton.Click += OnActionClick;
        bottomPanel.Controls.Add(_actionButton);
    }

    /// <summary>Adds a labeled textbox to the config panel and returns the textbox.</summary>
    TextBox AddConfigField(int y, string label, string placeholder)
    {
        _configPanel.Controls.Add(new Label
        {
            Text = label,
            Font = new Font("Segoe UI", 9.5f),
            ForeColor = TextDim,
            AutoSize = true,
            Location = new Point(0, y),
        });
        var box = new TextBox
        {
            Location = new Point(0, y + 24),
            Size = new Size(538, 26),
            BackColor = Field,
            ForeColor = TextBright,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Segoe UI", 10f),
            PlaceholderText = placeholder,
        };
        _configPanel.Controls.Add(box);
        return box;
    }

    void UpdateStatus(string text, int pct)
    {
        _statusLabel.Text = text;
        _progressBar.Value = pct;
        Refresh();
    }

    async void OnActionClick(object? sender, EventArgs e)
    {
        switch (_phase)
        {
            case Phase.Configure:
                SaveConfigIfProvided();
                _phase = Phase.Done;
                _actionButton.Text = "Close";
                UpdateStatus("All set. Launch HARK from Start / Search and press Ctrl+Win+H to caption.", 100);
                return;
            case Phase.Done:
                Close();
                return;
        }

        await RunInstallAsync();
    }

    async Task RunInstallAsync()
    {
        _actionButton.Enabled = false;

        // Step 1: trust the signing cert (elevated — one UAC prompt), unless already trusted.
        if (!IsCertTrusted())
        {
            UpdateStatus("Installing certificate (admin required)...", 15);
            var psi = new ProcessStartInfo
            {
                FileName = Environment.ProcessPath ?? Application.ExecutablePath,
                Arguments = "--cert-only",
                Verb = "runas",
                UseShellExecute = true,
            };
            try
            {
                var proc = Process.Start(psi);
                if (proc is not null)
                {
                    await proc.WaitForExitAsync();
                    if (proc.ExitCode != 0)
                    {
                        UpdateStatus("Certificate installation failed.", 0);
                        _actionButton.Enabled = true;
                        return;
                    }
                }
            }
            catch (System.ComponentModel.Win32Exception)
            {
                UpdateStatus("Installation cancelled at the elevation prompt.", 0);
                _actionButton.Enabled = true;
                return;
            }
            UpdateStatus("Certificate installed.", 40);
        }
        else
        {
            UpdateStatus("Certificate already trusted.", 40);
        }

        // Step 2: extract the embedded MSIX to a temp file.
        string msixPath;
        try
        {
            msixPath = ExtractEmbeddedMsix();
        }
        catch
        {
            UpdateStatus("ERROR: this installer was built without an embedded package.", 40);
            _actionButton.Enabled = true;
            return;
        }

        // Step 3: install the signed package (per-user, no elevation).
        UpdateStatus("Installing HARK...", 70);
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -Command \"Add-AppxPackage -Path '{msixPath}' -ForceApplicationShutdown\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
            };
            var proc = Process.Start(psi);
            if (proc is not null)
            {
                var stderr = await proc.StandardError.ReadToEndAsync();
                await proc.WaitForExitAsync();
                if (proc.ExitCode != 0 && !string.IsNullOrWhiteSpace(stderr))
                    throw new InvalidOperationException(stderr.Trim());
            }
        }
        catch
        {
            UpdateStatus("Opening App Installer...", 85);
            Process.Start(new ProcessStartInfo(msixPath) { UseShellExecute = true });
            UpdateStatus("Follow the App Installer prompts, then re-run this setup to configure Azure.", 100);
            _actionButton.Text = "Close";
            _phase = Phase.Done;
            _actionButton.Enabled = true;
            return;
        }
        finally
        {
            try { File.Delete(msixPath); } catch { /* temp cleanup is best-effort */ }
        }

        // Step 4: configure — always show the panel, prefilled with whatever the app would read
        // (env → config.json → user-secrets), so the user confirms or updates the resources on every
        // install instead of stale config silently hiding the fields. Saving writes config.json,
        // which takes precedence over user-secrets and so overrides anything stale there.
        _actionButton.Enabled = true;
        bool prefilled = PrefillConfigFields();
        _phase = Phase.Configure;
        _configPanel.Visible = true;
        _actionButton.Text = "Save & Finish";
        UpdateStatus(prefilled
            ? "HARK installed. Confirm or update your Azure resources below, then Save & Finish."
            : "HARK installed. Enter your Azure resource locations (or leave blank to set up later).", 90);
    }

    /// <summary>Writes config.json when a Speech region + resource id are supplied; a blank pair skips it.</summary>
    void SaveConfigIfProvided()
    {
        var region = _regionBox.Text.Trim();
        var resource = _resourceBox.Text.Trim();
        if (region.Length == 0 || resource.Length == 0) return;   // treat blank as "skip, set up later"

        var map = new Dictionary<string, string>
        {
            ["HARK_SPEECH_REGION"] = region,
            ["HARK_SPEECH_RESOURCE_ID"] = resource,
        };
        var endpoint = _aoaiEndpointBox.Text.Trim();
        var deployment = _aoaiDeploymentBox.Text.Trim();
        var imageDeployment = _aoaiImageDeploymentBox.Text.Trim();
        if (endpoint.Length > 0) map["HARK_AOAI_ENDPOINT"] = endpoint;
        if (deployment.Length > 0) map["HARK_AOAI_DEPLOYMENT"] = deployment;
        if (imageDeployment.Length > 0) map["HARK_AOAI_IMAGE_DEPLOYMENT"] = imageDeployment;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(map, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* leave config unwritten; the app shows a friendly note and the README covers it */ }
    }

    /// <summary>
    /// Prefills the config fields from whatever the app would read — env var → <c>config.json</c> →
    /// user-secrets — so the user confirms or edits current values instead of re-typing them (and so
    /// stale user-secrets no longer hide the panel). Returns true if any Speech value was detected.
    /// </summary>
    bool PrefillConfigFields()
    {
        _regionBox.Text              = DetectConfigValue("HARK_SPEECH_REGION") ?? string.Empty;
        _resourceBox.Text            = DetectConfigValue("HARK_SPEECH_RESOURCE_ID") ?? string.Empty;
        _aoaiEndpointBox.Text        = DetectConfigValue("HARK_AOAI_ENDPOINT") ?? string.Empty;
        _aoaiDeploymentBox.Text      = DetectConfigValue("HARK_AOAI_DEPLOYMENT") ?? string.Empty;
        _aoaiImageDeploymentBox.Text = DetectConfigValue("HARK_AOAI_IMAGE_DEPLOYMENT") ?? string.Empty;
        return _regionBox.Text.Length > 0 || _resourceBox.Text.Length > 0;
    }

    /// <summary>
    /// Reads a config value the way the app resolves it — environment variable →
    /// <c>%APPDATA%\Hark\config.json</c> → user-secrets — returning the first non-empty value, or null.
    /// </summary>
    static string? DetectConfigValue(string key)
    {
        var env = Environment.GetEnvironmentVariable(key);
        if (!string.IsNullOrWhiteSpace(env)) return env;

        return JsonValue(ConfigPath, key) ?? JsonValue(UserSecretsPath, key);
    }

    /// <summary>Reads a string property from a flat JSON config file, or null if absent/unreadable.</summary>
    static string? JsonValue(string path, string key)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            return root.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString()
                : null;
        }
        catch { return null; }
    }

    /// <summary>Writes the embedded MSIX to a temp file and returns its path.</summary>
    static string ExtractEmbeddedMsix()
    {
        using var stream = typeof(Program).Assembly.GetManifestResourceStream("Hark.msix")
            ?? throw new FileNotFoundException("Embedded Hark.msix not found.");
        var path = Path.Combine(Path.GetTempPath(), $"Hark-{Guid.NewGuid():N}.msix");
        using var file = File.Create(path);
        stream.CopyTo(file);
        return path;
    }

    /// <summary>True if the embedded cert is already present in LocalMachine\TrustedPeople.</summary>
    static bool IsCertTrusted()
    {
        try
        {
            var cert = Program.LoadEmbeddedCert();
            if (cert is null) return false;
            using var store = new X509Store(StoreName.TrustedPeople, StoreLocation.LocalMachine);
            store.Open(OpenFlags.ReadOnly);
            return store.Certificates.Find(X509FindType.FindByThumbprint, cert.Thumbprint, false).Count > 0;
        }
        catch { return false; }
    }
}
