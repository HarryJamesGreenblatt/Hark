using System.Reflection;
using Azure.Identity;
using Hark.Oracle.Vision;
using Microsoft.Extensions.Configuration;

// Oracle.Vision judgment spike — feed a conversation window, print the VisualConcept + composed image
// prompt. Concept-only (no renderer) so it proves the art-director JUDGMENT against the existing chat
// deployment, before any gpt-image deployment exists. Reads the same AOAI config as Hark.App:
//   env var > %APPDATA%\Hark\config.json > user-secrets, resolved via the app's UserSecretsId.
//   dotnet run --project Hark.Oracle.Spike [-- path\to\window.txt]
var config = new ConfigurationBuilder()
    .AddUserSecrets(Assembly.GetExecutingAssembly())
    .AddJsonFile(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Hark", "config.json"),
        optional: true, reloadOnChange: false)
    .AddEnvironmentVariables()
    .Build();

string? endpoint = config["HARK_AOAI_ENDPOINT"];
string? deployment = config["HARK_AOAI_DEPLOYMENT"];
if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(deployment))
{
    Console.Error.WriteLine("HARK_AOAI_ENDPOINT / HARK_AOAI_DEPLOYMENT not found (env, %APPDATA%\\Hark\\config.json, or user-secrets).");
    return 1;
}

// A default evocative window; override by passing a text file path as an argument.
// Pass "raw" to BYPASS the ConceptDesigner + composer and send the transcript straight to the image
// model — to see what the model does unguided by the art-director persona:
//   dotnet run --project Hark.Oracle.Spike -- [raw] [path\to\window.txt]
// Pass "infographic" to render a FLUX-idiomatic infographic prompt (structured, quoted labels, hex, no
// negatives) — the capability test for whether FLUX can produce a clean, readable diagram.
bool raw = args.Any(a => string.Equals(a, "raw", StringComparison.OrdinalIgnoreCase));
bool infographic = args.Any(a => string.Equals(a, "infographic", StringComparison.OrdinalIgnoreCase)
                              || string.Equals(a, "info", StringComparison.OrdinalIgnoreCase));
string? windowFile = args.FirstOrDefault(a => File.Exists(a));
string window = windowFile is not null
    ? await File.ReadAllTextAsync(windowFile)
    : """
      Speaker-1: I keep thinking about the house I grew up in. Every summer felt like it would never end.
      Speaker-2: And now?
      Speaker-1: Now I drive past and it's just... smaller. Someone painted over the blue door. I don't know why that got me.
      Speaker-2: It's not the door.
      Speaker-1: No. It's not the door.
      """;

string? imageDeployment = config["HARK_AOAI_IMAGE_DEPLOYMENT"];

// Renders a prompt, times it, saves the PNG to temp, opens it, and prints the path + elapsed.
async Task<int> RenderAndSave(string prompt, string label)
{
    if (string.IsNullOrWhiteSpace(imageDeployment))
    {
        Console.Error.WriteLine("HARK_AOAI_IMAGE_DEPLOYMENT not set — nothing to render.");
        return 4;
    }
    var provider = config["HARK_AOAI_IMAGE_PROVIDER"];
    Console.WriteLine($"\n=== rendering ({label}) — deployment={imageDeployment}, provider={provider ?? "<openai>"} ===");
    try
    {
        var renderer = new VisionRenderer(endpoint, imageDeployment, new AzureCliCredential(),
            config["HARK_AOAI_IMAGE_QUALITY"], provider);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var png = await renderer.RenderAsync(prompt);
        sw.Stop();
        var path = Path.Combine(Path.GetTempPath(), $"hark-vision-{label}-{DateTime.Now:HHmmss}.png");
        await File.WriteAllBytesAsync(path, png);
        Console.WriteLine($"  OK — {png.Length} bytes in {sw.Elapsed.TotalSeconds:F1}s → {path}");
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"  Render FAILED: {ex.Message}");
        return 4;
    }
}

// ── RAW: transcript straight to the image model, no concept persona ──
if (raw)
{
    Console.WriteLine("RAW mode — bypassing ConceptDesigner; the transcript is the prompt.\n");
    Console.WriteLine(window.Trim());
    return await RenderAndSave(window.Trim(), "raw");
}

// ── INFOGRAPHIC: a FLUX-idiomatic infographic prompt (structured, quoted labels, hex, NO negatives) ──
// Capability test: can FLUX.2 render a CLEAN, READABLE infographic when prompted per its own guide?
// Bypasses ConceptDesigner AND VisionPromptComposer (both forbid diagrams/text). Pass a .txt to supply
// your own prompt; otherwise a hand-crafted Python-function infographic prompt is used.
//   dotnet run --project Hark.Oracle.Spike -- infographic [path\to\prompt.txt]
if (infographic)
{
    string infoPrompt;
    if (windowFile is not null)
    {
        infoPrompt = window.Trim();
    }
    else
    {
        // A realistic radial concept (the EP live back-end-dev talk) composed through the REAL composer,
        // so this tests InfographicPromptComposer's radial/center-clear grammar, not a hand-authored prompt.
        var sample = new InfographicConcept("Back-end developer responsibilities",
        [
            new InfographicNode("server-side logic", "blue"),
            new InfographicNode("database management", "green"),
            new InfographicNode("API development", "orange"),
            new InfographicNode("server management", "purple"),
            new InfographicNode("security", "red"),
        ]);
        infoPrompt = InfographicPromptComposer.Compose(sample);
    }

    Console.WriteLine("INFOGRAPHIC mode — radial mind-map via InfographicPromptComposer (bypasses ConceptDesigner + composer).\n");
    Console.WriteLine(infoPrompt);
    return await RenderAndSave(infoPrompt, "infographic");
}

var designer = new ConceptDesigner(endpoint, deployment, new AzureCliCredential());
var vision = new VisionService(designer);   // concept-only — the judgment spike

Console.WriteLine("Conjuring a visual concept from the conversation window...\n");
Console.WriteLine(window.Trim());
Console.WriteLine();

VisionResult? result;
try
{
    result = await vision.ConjureAsync(window);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Conjuring failed: {ex.Message}");
    return 2;
}

if (result is null)
{
    Console.Error.WriteLine("No concept returned.");
    return 3;
}

var c = result.Concept;
Console.WriteLine("=== visual concept ===");
Console.WriteLine($"  theme       {c.Theme}");
Console.WriteLine($"  concept     {c.Concept}");
Console.WriteLine($"  stance      {c.Stance} — {c.StanceReason}");
Console.WriteLine($"  motifs      {string.Join(", ", c.Motifs)}");
Console.WriteLine($"  composition {c.Composition}");
Console.WriteLine($"  aesthetic   {c.Aesthetic}");
Console.WriteLine($"  palette     {c.Palette}");
Console.WriteLine("\n=== composed image prompt ===\n");
Console.WriteLine(result.Prompt);

return string.IsNullOrWhiteSpace(imageDeployment) ? 0 : await RenderAndSave(result.Prompt, "concept");
