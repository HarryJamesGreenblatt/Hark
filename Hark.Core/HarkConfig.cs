namespace Hark.Core;

/// <summary>
/// Shared configuration conventions for HARK hosts (CLI and desktop).
/// </summary>
public static class HarkConfig
{
    /// <summary>
    /// Path to the optional external config file: <c>%APPDATA%\Hark\config.json</c>.
    /// <para>
    /// This lives in the user profile (never in the repo), so it can hold non-sensitive resource
    /// locations — Speech region/ARM id, Azure OpenAI endpoint/deployment — for a <b>published</b>
    /// exe, where <c>dotnet user-secrets</c> (a Development-only mechanism) isn't available. Keyless
    /// auth still applies; no keys are stored here.
    /// </para>
    /// </summary>
    public static string ExternalConfigPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Hark",
        "config.json");
}
