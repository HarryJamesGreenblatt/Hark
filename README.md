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
- Signed in via Azure CLI (`az login`) — its identity is used for keyless auth
- _(Optional, desktop only)_ An **Azure OpenAI** resource with a **chat model deployment** for AI
  recaps, and the **Cognitive Services OpenAI User** role on it. Without this, captions and speaker
  pages work fully; only the **SUMMARY** view is disabled (it shows a "not configured" note).

## Configuration

Provide the resource via flags, environment variables, or `dotnet user-secrets` (checked in that
priority order):

| Setting | Flag | Env var |
|---|---|---|
| Region | `--region eastus2` | `HARK_SPEECH_REGION` |
| Resource ARM id | `--resource-id <id>` | `HARK_SPEECH_RESOURCE_ID` |

> **The resource ARM id embeds your subscription id**, so it's never hardcoded in source or
> `launchSettings.json`. Store it locally instead (one-time, per project):
> ```powershell
> dotnet user-secrets set "HARK_SPEECH_REGION" "eastus2" --project Hark.Cli
> dotnet user-secrets set "HARK_SPEECH_RESOURCE_ID" "<your-speech-resource-arm-id>" --project Hark.Cli
> # repeat with --project Hark.App for the desktop overlay
> ```
> User secrets live outside the repo (`%APPDATA%\Microsoft\UserSecrets\`) and are never committed.

> **Auth is keyless.** HARK authenticates with `AzureCliCredential` (your `az login` identity) and
> never reads or stores account keys. The explicit credential keeps `DefaultAzureCredential` free
> for other tooling and ensures the role-bearing CLI identity is the one used.

### Summaries (desktop, optional)

The desktop overlay can generate an AI recap of the captured conversation via **Azure OpenAI**.
Point HARK at a **chat model deployment** using `dotnet user-secrets` (endpoint and deployment name
only — no keys):

```powershell
dotnet user-secrets set "HARK_AOAI_ENDPOINT" "https://<your-aoai>.openai.azure.com/" --project Hark.App
dotnet user-secrets set "HARK_AOAI_DEPLOYMENT" "<your-chat-deployment-name>" --project Hark.App
```

Auth is the same keyless `AzureCliCredential`; your `az login` identity needs the
**Cognitive Services OpenAI User** role on the resource. If these secrets are absent, the SUMMARY
view simply shows a note instead of failing.

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

## Desktop overlay (`Hark.App`)

A tray-resident captions bar that reuses the same `Hark.Core` pipeline.

- **Toggle:** `Ctrl+Win+H` shows/hides a selectable, resizable, always-on-top captions bar.
- **Speaker diarization:** captions are attributed to anonymous, session-scoped speakers
  (`Guest-1`, `Guest-2`, …) using Azure Speech's `ConversationTranscriber`. Each detected speaker
  gets a **pill**; clicking it opens a dedicated **page** showing just that speaker's lines.
- **CAPTIONS / SUMMARY switch:** a segmented control cross-fades between the live captions and an
  **AI recap** (Teams-style by default; Narrative and per-speaker styles also available). The recap
  is cached and only regenerated when the captions change, so switching back and forth is free.
  Requires the optional Azure OpenAI configuration above.

> Diarization labels are anonymous and can occasionally swap or merge — expected for single-channel
> speaker separation. Spoken/narration audio works best; sung or heavily overlapping speech is harder.

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

### Azure OpenAI (optional — for desktop recaps)

```powershell
az cognitiveservices account create --name aoai-hark --resource-group rg-hark `
  --kind OpenAI --sku S0 --location eastus2 --custom-domain aoai-hark

# Deploy a chat model (pick a model/version available in your region)
az cognitiveservices account deployment create -n aoai-hark -g rg-hark `
  --deployment-name gpt-4.1-mini --model-name gpt-4.1-mini --model-version "2025-04-14" `
  --model-format OpenAI --sku-name GlobalStandard --sku-capacity 10

$aoai = az cognitiveservices account show -n aoai-hark -g rg-hark --query id -o tsv
az role assignment create --assignee-object-id $me --assignee-principal-type User `
  --role "Cognitive Services OpenAI User" --scope $aoai

# Endpoint = https://aoai-hark.openai.azure.com/  ·  deployment = gpt-4.1-mini
# Store them in user-secrets (see "Summaries" above).
```

## Dependencies

| Package | Purpose |
|---|---|
| [NAudio](https://github.com/naudio/NAudio) | WASAPI loopback capture + resampling |
| [Microsoft.CognitiveServices.Speech](https://learn.microsoft.com/azure/ai-services/speech-service/) | Continuous speech recognition + diarization |
| [Azure.AI.OpenAI](https://learn.microsoft.com/azure/ai-services/openai/) | AI recaps (desktop SUMMARY view) |
| [Azure.Identity](https://learn.microsoft.com/dotnet/api/azure.identity) | Keyless Entra ID auth (`AzureCliCredential`) |

## License

MIT
