# HARK

**Hear. Adapt. Recognize. Keep.**

A developer-grade, scriptable speech-to-text pipeline that captures **system playback audio** on Windows 11 (WASAPI loopback — no microphone) and transcribes it in near real time via **Azure AI Speech**, emitting clean text to stdout plus optional rolling text, JSON Lines, and SRT outputs.

It exists to replace accessibility-only tooling (Live Captions, Voice Typing) with something **owned, automatable, and agent-friendly**.

![.NET 9](https://img.shields.io/badge/.NET-9.0-512BD4) ![Azure AI Speech](https://img.shields.io/badge/Azure-AI%20Speech-0078D4) ![Auth](https://img.shields.io/badge/auth-Entra%20ID%20(keyless)-2F8D46)

## How it works

```
┌──────────────┐  48k stereo float  ┌──────────────┐  16k mono PCM  ┌───────────────────┐  segments  ┌──────────────┐
│     Hear     │ ─────────────────► │    Adapt     │ ─────────────► │     Recognize     │ ─────────► │     Keep     │
│ WASAPI loop  │   DataAvailable    │ resample +   │  PushStream    │ Azure Speech      │  Interim/  │ stdout · txt │
│ (NAudio)     │                    │ downmix +16b │                │ continuous reco   │  Final     │ json · srt   │
└──────────────┘                    └──────────────┘                └───────────────────┘            └──────────────┘
```

| Stage | Type | Responsibility |
|---|---|---|
| **Hear** | `Capture/LoopbackCaptureService` | Taps the default render endpoint via WASAPI loopback |
| **Adapt** | `Audio/PcmConverter` | Downmix → resample to 16 kHz mono → 16-bit PCM |
| **Recognize** | `Transcription/AzureSpeechTranscriber` | Streams PCM to Azure Speech; raises `Interim`/`Final` |
| **Keep** | `Output/*Sink` | Fans results to stdout, rolling text, JSON Lines, SRT |

`ISpeechTranscriber` is an explicit **swap point**: the Azure engine is the default, and a local/offline `WhisperTranscriber` can drop in without touching the rest of the pipeline.

## Prerequisites

- Windows 10/11 with a default output device
- .NET 9 SDK
- An Azure AI **Speech** resource (`kind=SpeechServices`, `S0`)
- The signed-in identity holds the **Cognitive Services Speech User** role on that resource
- Signed in via Azure CLI (`az login`) or Visual Studio — used for keyless auth

## Configuration

Provide the resource via flags or environment variables:

| Setting | Flag | Env var |
|---|---|---|
| Region | `--region eastus2` | `HARK_SPEECH_REGION` |
| Resource ARM id | `--resource-id <id>` | `HARK_SPEECH_RESOURCE_ID` |

> **Auth is keyless.** HARK uses `DefaultAzureCredential` and never reads or stores account keys.

## Usage

```powershell
# Easiest: the launcher sets region + resource id, then runs
./run.ps1                              # stream to stdout + transcript.txt
./run.ps1 -Json transcript.jsonl -Srt captions.srt
./run.ps1 -Language en-US -Quiet       # finals only

# Or invoke the CLI directly
dotnet run --project Hark.Cli -- --region eastus2 --out transcript.txt --json transcript.jsonl
```

Play a clear-speech video through your speakers/headphones; transcription streams live and finalized lines persist to the chosen outputs. Press **Ctrl+C** to stop (SRT is written on exit).

## Provisioning (one-time)

```powershell
az group create --name rg-hark --location eastus2
az cognitiveservices account create --name spch-hark --resource-group rg-hark `
  --kind SpeechServices --sku S0 --location eastus2 --custom-domain spch-hark

$scope = az cognitiveservices account show -n spch-hark -g rg-hark --query id -o tsv
$me    = az ad signed-in-user show --query id -o tsv
az role assignment create --assignee-object-id $me --assignee-principal-type User `
  --role "Cognitive Services Speech User" --scope $scope
```

## Dependencies

| Package | Purpose |
|---|---|
| [NAudio](https://github.com/naudio/NAudio) | WASAPI loopback capture + resampling |
| [Microsoft.CognitiveServices.Speech](https://learn.microsoft.com/azure/ai-services/speech-service/) | Continuous speech recognition |
| [Azure.Identity](https://learn.microsoft.com/dotnet/api/azure.identity) | Keyless Entra ID auth (`DefaultAzureCredential`) |

## License

MIT
