using System.Reflection;
using Azure.Identity;
using Hark.Core;
using Hark.Core.Output;
using Microsoft.Extensions.Configuration;

// HARK — Hear. Adapt. Recognize. Keep.
// Captures system playback audio (WASAPI loopback) and streams it to Azure AI Speech for
// near-real-time transcription, fanning results out to stdout and optional files.

var options = CliOptions.Parse(args);
if (options.ShowHelp)
{
    CliOptions.PrintHelp();
    return 0;
}

// Config precedence: CLI flag > env var > user-secrets (dev-machine-local, never committed).
// The resource ARM id embeds the subscription id, so it deliberately never lives in source or
// launch profiles — see dotnet user-secrets in the README's Configuration section.
var config = new ConfigurationBuilder()
    .AddUserSecrets(Assembly.GetExecutingAssembly())
    .AddEnvironmentVariables()
    .Build();

string? region = options.Region ?? config["HARK_SPEECH_REGION"];
string? resourceId = options.ResourceId ?? config["HARK_SPEECH_RESOURCE_ID"];

if (string.IsNullOrWhiteSpace(region) || string.IsNullOrWhiteSpace(resourceId))
{
    Console.Error.WriteLine(
        "Missing Speech resource configuration. Provide --region and --resource-id, " +
        "set HARK_SPEECH_REGION / HARK_SPEECH_RESOURCE_ID, or configure dotnet user-secrets " +
        "for Hark.Cli (see README).");
    Console.Error.WriteLine("Run 'hark --help' for details.");
    return 2;
}

// Keep — build the requested sinks.
var sinks = new List<ITranscriptSink> { new StdoutSink(showInterim: !options.Quiet) };
if (options.OutPath is not null) sinks.Add(new RollingFileSink(options.OutPath));
if (options.JsonPath is not null) sinks.Add(new JsonSink(options.JsonPath));
if (options.SrtPath is not null) sinks.Add(new SrtSink(options.SrtPath));
await using var sink = new CompositeSink(sinks.ToArray());

// The shared pipeline (Hear → Adapt → Recognize → Keep).
// Use the Azure CLI sign-in (az login) explicitly rather than DefaultAzureCredential, so HARK
// authenticates as the identity that holds the 'Cognitive Services Speech User' role on the
// resource — independent of any Visual Studio / IDE sign-in used for other work.
await using var session = new HarkSession(
    region, resourceId, options.Language, new AzureCliCredential(), sink);
session.Error += msg => Console.Error.WriteLine($"[Recognize] {msg}");

using var shutdown = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;       // let us tear down gracefully instead of hard-killing
    shutdown.Cancel();
};

await session.StartAsync(shutdown.Token);

Console.Error.WriteLine("HARK listening on default output device.");
Console.Error.WriteLine("Play audio through your speakers/headphones. Press Ctrl+C to stop.");

try
{
    await Task.Delay(Timeout.Infinite, shutdown.Token);
}
catch (OperationCanceledException)
{
    // expected on Ctrl+C
}

Console.Error.WriteLine("Stopping…");
await session.StopAsync();
return 0;

/// <summary>Parsed command-line options for the HARK CLI.</summary>
internal sealed class CliOptions
{
    #region Properties

    /// <summary>The Speech resource region, e.g. <c>eastus2</c>.</summary>
    public string? Region { get; private init; }

    /// <summary>The full ARM resource ID of the Speech account.</summary>
    public string? ResourceId { get; private init; }

    /// <summary>Optional BCP-47 recognition language tag.</summary>
    public string? Language { get; private init; }

    /// <summary>Optional destination path for the rolling plain-text transcript.</summary>
    public string? OutPath { get; private init; }

    /// <summary>Optional destination path for the JSON Lines transcript.</summary>
    public string? JsonPath { get; private init; }

    /// <summary>Optional destination path for the SRT subtitle file.</summary>
    public string? SrtPath { get; private init; }

    /// <summary>Whether interim hypotheses are suppressed on stdout (finals only).</summary>
    public bool Quiet { get; private init; }

    /// <summary>Whether help text was requested (or an unknown argument was encountered).</summary>
    public bool ShowHelp { get; private init; }

    #endregion

    #region Methods

    /// <summary>Parses command-line arguments into a <see cref="CliOptions"/> instance.</summary>
    /// <param name="args">The raw command-line arguments.</param>
    /// <returns>The parsed options.</returns>
    public static CliOptions Parse(string[] args)
    {
        string? region = null, resourceId = null, language = null, outPath = null, jsonPath = null, srtPath = null;
        bool quiet = false, help = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--region": region = Next(args, ref i); break;
                case "--resource-id": resourceId = Next(args, ref i); break;
                case "--language" or "-l": language = Next(args, ref i); break;
                case "--out" or "-o": outPath = Next(args, ref i); break;
                case "--json": jsonPath = Next(args, ref i); break;
                case "--srt": srtPath = Next(args, ref i); break;
                case "--quiet" or "-q": quiet = true; break;
                case "--help" or "-h": help = true; break;
                default:
                    Console.Error.WriteLine($"Unknown argument: {args[i]}");
                    help = true;
                    break;
            }
        }

        return new CliOptions
        {
            Region = region,
            ResourceId = resourceId,
            Language = language,
            OutPath = outPath,
            JsonPath = jsonPath,
            SrtPath = srtPath,
            Quiet = quiet,
            ShowHelp = help,
        };
    }

    /// <summary>Returns the next argument after the current index, advancing <paramref name="i"/>.</summary>
    /// <param name="args">The full argument list.</param>
    /// <param name="i">The current index, advanced by one if a next argument exists.</param>
    /// <returns>The next argument, or <see langword="null"/> if none remains.</returns>
    private static string? Next(string[] args, ref int i) => i + 1 < args.Length ? args[++i] : null;

    /// <summary>Writes usage help text to stdout.</summary>
    public static void PrintHelp()
    {
        Console.WriteLine(
            """
            HARK — Hear. Adapt. Recognize. Keep.
            Transcribe system playback audio in near real time via Azure AI Speech.

            USAGE:
              hark [options]

            CONFIG (required; flag or environment variable):
              --region <name>         Speech resource region   (env: HARK_SPEECH_REGION)
              --resource-id <id>      Speech resource ARM id    (env: HARK_SPEECH_RESOURCE_ID)

            OPTIONS:
              -l, --language <bcp47>  Recognition language (e.g. en-US). Default: service default.
              -o, --out <path>        Append finalized lines to a rolling text transcript.
                  --json <path>       Append finalized segments as JSON Lines.
                  --srt <path>        Write an SRT subtitle file on exit.
              -q, --quiet             Suppress interim hypotheses on stdout (finals only).
              -h, --help              Show this help.

            AUTH:
              Uses Microsoft Entra ID (DefaultAzureCredential) — your Visual Studio / Azure CLI
              sign-in. The identity needs the 'Cognitive Services Speech User' role on the resource.

            EXAMPLE:
              hark --region eastus2 --out transcript.txt --json transcript.jsonl
            """);
    }

    #endregion
}
