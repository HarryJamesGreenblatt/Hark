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

// A default evocative window; override by passing a text file path as the first argument.
string window = args.Length > 0 && File.Exists(args[0])
    ? await File.ReadAllTextAsync(args[0])
    : """
      Speaker-1: I keep thinking about the house I grew up in. Every summer felt like it would never end.
      Speaker-2: And now?
      Speaker-1: Now I drive past and it's just... smaller. Someone painted over the blue door. I don't know why that got me.
      Speaker-2: It's not the door.
      Speaker-1: No. It's not the door.
      """;

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
return 0;
