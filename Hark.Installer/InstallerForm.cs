using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;

namespace Hark.Installer;

/// <summary>A single-window installer that trusts the signing cert then installs the embedded MSIX.</summary>
internal sealed class InstallerForm : Form
{
    // ── HAL palette ──
    static readonly Color Plate      = Color.FromArgb(12, 12, 12);
    static readonly Color Panel      = Color.FromArgb(20, 20, 22);
    static readonly Color HalRed     = Color.FromArgb(255, 40, 26);
    static readonly Color HalDeep    = Color.FromArgb(150, 10, 4);
    static readonly Color TextDim    = Color.FromArgb(200, 205, 212);
    static readonly Color TextBright = Color.FromArgb(240, 244, 248);

    readonly Label _statusLabel;
    readonly ProgressBar _progressBar;
    readonly Button _actionButton;
    bool _done;

    public InstallerForm()
    {
        Text = "HARK Setup";
        ClientSize = new Size(560, 300);
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

        var topBar = new Panel { Location = new Point(0, 0), Size = new Size(560, 96), BackColor = Panel };
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
            Size = new Size(520, 44),
            Location = new Point(20, 112),
        };
        Controls.Add(_statusLabel);

        _progressBar = new ProgressBar
        {
            Location = new Point(20, 164),
            Size = new Size(520, 20),
            Minimum = 0,
            Maximum = 100,
            Value = 0,
        };
        Controls.Add(_progressBar);

        var bottomPanel = new Panel { Location = new Point(0, 208), Size = new Size(560, 92), BackColor = Panel };
        Controls.Add(bottomPanel);
        bottomPanel.Controls.Add(new Panel { Location = new Point(0, 0), Size = new Size(560, 2), BackColor = HalRed });

        _actionButton = new Button
        {
            Text = "Install",
            Font = new Font("Segoe UI Semibold", 12f),
            Size = new Size(160, 42),
            Location = new Point(200, 24),
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

    void UpdateStatus(string text, int pct)
    {
        _statusLabel.Text = text;
        _progressBar.Value = pct;
        Refresh();
    }

    async void OnActionClick(object? sender, EventArgs e)
    {
        if (_done) { Close(); return; }
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
            UpdateStatus("HARK installed. Find it in Start / Search. Set your Azure config (see README) to caption.", 100);
        }
        catch
        {
            UpdateStatus("Opening App Installer...", 85);
            Process.Start(new ProcessStartInfo(msixPath) { UseShellExecute = true });
            UpdateStatus("Follow the App Installer prompts to complete setup.", 100);
        }
        finally
        {
            try { File.Delete(msixPath); } catch { /* temp cleanup is best-effort */ }
        }

        _done = true;
        _actionButton.Text = "Close";
        _actionButton.Enabled = true;
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
