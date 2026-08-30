using System.Diagnostics;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace Hark.Installer;

/// <summary>
/// A single-window installer: trusts the signing cert, installs the embedded MSIX, then — only if
/// HARK isn't already configured — collects the (non-secret) Azure resource locations and writes
/// <c>%APPDATA%\Hark\config.json</c> so the user never has to hunt down the README. WPF is used so the
/// dialog scales correctly on high-DPI displays instead of rendering cramped.
/// </summary>
public partial class InstallerWindow : Window
{
    // ── HAL palette (brushes used by the code-built config fields) ──
    static readonly Brush Field       = new SolidColorBrush(Color.FromRgb(30, 30, 34));
    static readonly Brush FieldBorder = new SolidColorBrush(Color.FromRgb(58, 58, 64));
    static readonly Brush TextDim     = new SolidColorBrush(Color.FromRgb(200, 205, 212));
    static readonly Brush TextBright  = new SolidColorBrush(Color.FromRgb(240, 244, 248));
    static readonly Brush Placeholder = new SolidColorBrush(Color.FromRgb(106, 111, 118));

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

    public InstallerWindow()
    {
        InitializeComponent();

        // Window icon + top-bar logo from the embedded PNG.
        try
        {
            using var iconStream = typeof(Program).Assembly.GetManifestResourceStream("Icon.png");
            if (iconStream is not null)
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = iconStream;
                bmp.EndInit();
                bmp.Freeze();
                LogoImage.Source = bmp;
                Icon = bmp;
            }
        }
        catch { /* logo/icon is cosmetic */ }

        // ── Config fields (revealed after install only when config.json is absent) ──
        _regionBox              = AddConfigField("Azure Speech region", "e.g. eastus2");
        _resourceBox            = AddConfigField("Speech resource ID (ARM id)", "/subscriptions/.../Microsoft.CognitiveServices/accounts/...");
        _aoaiEndpointBox        = AddConfigField("Azure OpenAI endpoint (optional — enables SUMMARY)", "https://<name>.openai.azure.com/");
        _aoaiDeploymentBox      = AddConfigField("OpenAI chat deployment (optional)", "e.g. gpt-4.1-mini");
        _aoaiImageDeploymentBox = AddConfigField("OpenAI image deployment (optional — enables Vision)", "e.g. gpt-image-1");
    }

    /// <summary>Adds a labeled textbox (with a watermark overlay) to the config panel and returns the textbox.</summary>
    TextBox AddConfigField(string label, string placeholder)
    {
        ConfigPanel.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = TextDim,
            FontSize = 12.5,
            Margin = new Thickness(0, 0, 0, 4),
        });

        var box = new TextBox
        {
            Background = Field,
            Foreground = TextBright,
            CaretBrush = TextBright,
            BorderBrush = FieldBorder,
            BorderThickness = new Thickness(1),
            FontSize = 13,
            Padding = new Thickness(6, 4, 6, 4),
        };

        // WPF has no PlaceholderText; overlay a non-interactive watermark shown only when the box is empty.
        var watermark = new TextBlock
        {
            Text = placeholder,
            Foreground = Placeholder,
            FontSize = 13,
            Margin = new Thickness(8, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
            TextTrimming = TextTrimming.CharacterEllipsis,   // a long hint must not force horizontal overflow
        };
        box.TextChanged += (_, _) =>
            watermark.Visibility = string.IsNullOrEmpty(box.Text) ? Visibility.Visible : Visibility.Collapsed;

        var cell = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        cell.Children.Add(box);
        cell.Children.Add(watermark);
        ConfigPanel.Children.Add(cell);
        return box;
    }

    void UpdateStatus(string text, int pct)
    {
        StatusText.Text = text;
        // Glide the bar to the new value instead of snapping — a custom-Foreground WPF ProgressBar has no
        // built-in fill animation, so discrete Value sets read as jumps.
        var glide = new DoubleAnimation(pct, TimeSpan.FromMilliseconds(450))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        };
        Progress.BeginAnimation(ProgressBar.ValueProperty, glide);
    }

    async void OnActionClick(object sender, RoutedEventArgs e)
    {
        switch (_phase)
        {
            case Phase.Configure:
                SaveConfigIfProvided();
                _phase = Phase.Done;
                ActionBtn.Content = "Close";
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
        ActionBtn.IsEnabled = false;

        // Step 1: trust the signing cert (elevated — one UAC prompt), unless already trusted.
        if (!IsCertTrusted())
        {
            UpdateStatus("Installing certificate (admin required)...", 15);
            var psi = new ProcessStartInfo
            {
                FileName = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule!.FileName,
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
                        ActionBtn.IsEnabled = true;
                        return;
                    }
                }
            }
            catch (System.ComponentModel.Win32Exception)
            {
                UpdateStatus("Installation cancelled at the elevation prompt.", 0);
                ActionBtn.IsEnabled = true;
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
            ActionBtn.IsEnabled = true;
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
            ActionBtn.Content = "Close";
            _phase = Phase.Done;
            ActionBtn.IsEnabled = true;
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
        ActionBtn.IsEnabled = true;
        bool prefilled = PrefillConfigFields();
        _phase = Phase.Configure;
        ConfigPanel.Visibility = Visibility.Visible;
        ActionBtn.Content = "Save & Finish";
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
