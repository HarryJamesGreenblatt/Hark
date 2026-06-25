using Azure.Identity;
using Hark.Core.Audio;
using Hark.Core.Capture;
using Hark.Core.Output;
using Hark.Core.Transcription;

// HARK — Hear. Adapt. Recognize. Keep.
// Captures system playback audio (WASAPI loopback) and streams it to Azure AI Speech for
// near-real-time transcription, fanning results out to stdout and optional files.

var options = CliOptions.Parse(args);
if (options.ShowHelp)
{
    CliOptions.PrintHelp();
    return 0;
}

string? region = options.Region ?? Environment.GetEnvironmentVariable("HARK_SPEECH_REGION");
string? resourceId = options.ResourceId ?? Environment.GetEnvironmentVariable("HARK_SPEECH_RESOURCE_ID");

if (string.IsNullOrWhiteSpace(region) || string.IsNullOrWhiteSpace(resourceId))
{
    Console.Error.WriteLine(
        "Missing Speech resource configuration. Provide --region and --resource-id, " +
        "or set HARK_SPEECH_REGION and HARK_SPEECH_RESOURCE_ID.");
    Console.Error.WriteLine("Run 'hark --help' for details.");
    return 2;
}

// Keep — build the requested sinks.
var sinks = new List<ITranscriptSink> { new StdoutSink(showInterim: !options.Quiet) };
if (options.OutPath is not null) sinks.Add(new RollingFileSink(options.OutPath));
if (options.JsonPath is not null) sinks.Add(new JsonSink(options.JsonPath));
if (options.SrtPath is not null) sinks.Add(new SrtSink(options.SrtPath));
await using var sink = new CompositeSink(sinks.ToArray());

// Recognize — Azure Speech via keyless Entra auth.
// Use the Azure CLI sign-in (az login) explicitly rather than DefaultAzureCredential, so HARK
// authenticates as the identity that holds the 'Cognitive Services Speech User' role on the
// resource — independent of any Visual Studio / IDE sign-in used for other work.
await using var transcriber = new AzureSpeechTranscriber(
    region, resourceId, options.Language, new AzureCliCredential());
transcriber.Interim += sink.Write;
transcriber.Final += sink.Write;
transcriber.Error += msg => Console.Error.WriteLine($"[Recognize] {msg}");

// Hear — WASAPI loopback capture.
using var capture = new LoopbackCaptureService();

using var shutdown = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;       // let us tear down gracefully instead of hard-killing
    shutdown.Cancel();
};

await transcriber.StartAsync(shutdown.Token);

capture.Start();
var format = capture.WaveFormat
    ?? throw new InvalidOperationException("Capture did not expose a wave format after starting.");

// Adapt — convert each captured buffer to 16 kHz mono 16-bit PCM and feed the recognizer.
var converter = new PcmConverter(format);
capture.DataAvailable += (buffer, bytes) =>
{
    var pcm = converter.Convert(buffer, bytes);
    if (pcm.Length > 0) transcriber.Write(pcm, pcm.Length);
};

Console.Error.WriteLine($"HARK listening on default output device ({format.SampleRate} Hz, {format.Channels}ch).");
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
capture.Stop();
await transcriber.StopAsync();
return 0;

/// <summary>Parsed command-line options for the HARK CLI.</summary>
internal sealed class CliOptions
{
    public string? Region { get; private init; }
    public string? ResourceId { get; private init; }
    public string? Language { get; private init; }
    public string? OutPath { get; private init; }
    public string? JsonPath { get; private init; }
    public string? SrtPath { get; private init; }
    public bool Quiet { get; private init; }
    public bool ShowHelp { get; private init; }

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

    private static string? Next(string[] args, ref int i) => i + 1 < args.Length ? args[++i] : null;

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
}
