# HARK

**Hear. Adapt. Recognize. Keep.**

A developer-grade, scriptable speech-to-text pipeline that captures **system playback audio** on Windows 11 (WASAPI loopback — no microphone needed) and transcribes it in near real time via **Azure AI Speech**, emitting clean text to stdout plus optional rolling text, JSON Lines, and SRT outputs. The desktop overlay can optionally mix in your **local microphone** too, so a headset user's own voice is captioned alongside the far side.

It exists to replace accessibility-only tooling (Live Captions, Voice Typing) with something **owned, automatable, and agent-friendly**.

![.NET 9](https://img.shields.io/badge/.NET-9.0-512BD4) ![Azure AI Speech](https://img.shields.io/badge/Azure-AI%20Speech-0078D4) ![Auth](https://img.shields.io/badge/auth-Entra%20ID%20(keyless)-2F8D46)

## Install (Windows)

The quickest way to run the desktop overlay is the installer on the
[**Releases**](https://github.com/HarryJamesGreenblatt/Hark/releases/latest) page — no .NET SDK or
build step required (it ships a self-contained app).

1. Download **`Hark-Setup.zip`** from the latest release and extract it — inside is a single
   `Hark-Setup.exe`. (Shipping the exe *inside a zip* keeps the browser download clean; SmartScreen's
   "isn't commonly downloaded" reputation gate applies to bare executables, not archives.)
2. Run **`Hark-Setup.exe`**. SmartScreen shows *"Windows protected your PC"* for a new publisher —
   click **More info → Run anyway**.
3. Click **Install** and approve the single **UAC prompt** — it trusts HARK's signing certificate so
   the packaged app validates. HARK then installs into **Start / Search**, with an **Add/Remove
   Programs** entry and a launch-at-startup task.
4. If the machine isn't already configured (via env vars, `config.json`, or user-secrets), the
   installer prompts for your **Azure resource locations** — Speech region + resource id, and
   optionally the Azure OpenAI endpoint/deployment for recaps — and writes them to
   `%APPDATA%\Hark\config.json`. Already-configured machines skip this step.

HARK lives in the system tray; press **Ctrl+Win+H** to toggle captions. Uninstall via **Add/Remove
Programs** like any packaged app. You still need an Azure **Speech** resource and `az login` with the
right role — see [Prerequisites](#prerequisites) and [Configuration](#configuration).

> **How the installer is built:** pushing a `vX.Y.Z` tag runs
> [`.github/workflows/release.yml`](.github/workflows/release.yml), which builds and **signs** the
> MSIX, embeds it into a single self-contained `Hark-Setup.exe`, and publishes it (zipped) as a
> GitHub Release. Signing uses the `MSIX_CERT_PFX` / `MSIX_CERT_PASSWORD` repo secrets; the public
> cert ships inside the installer to establish trust at install time.

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
| **Hear** | `Capture/LoopbackCaptureService` (+ `MicCaptureService`) | Taps the default render endpoint via WASAPI loopback; the desktop app can also mix in the local mic (opt-in), so a headset user's own voice is captioned too |
| **Adapt** | `Audio/PcmConverter` | Downmix → resample to 16 kHz mono → 16-bit PCM (loopback + mic mixed in the float domain) |
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
- _(Desktop only)_ **Microphone mixing** is off by default (loopback-only, like native Live Captions).
  Toggle the **mic button** in the overlay (or set `HARK_MIX_MIC=1`) to also caption your own voice
  alongside system/far-side audio — the headset scenario. Leave it off on speakers, where the mic
  would re-capture playback and double the transcript.

## Configuration

Provide the resource via flags, environment variables, an external config file, or
`dotnet user-secrets` — checked in this priority order:

```
CLI flags  →  environment variables  →  %APPDATA%\Hark\config.json  →  dotnet user-secrets
```

| Setting | Flag | Env var / key |
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

> **Published exe?** `dotnet user-secrets` is a **development-only** mechanism — it doesn't ship with
> a built executable. For a published build on a non-dev machine, drop the same values in an external
> **`%APPDATA%\Hark\config.json`** instead (it lives in your user profile, so it's never in the repo
> and can't be committed). Only resource *locations* live here — auth stays keyless, no keys:
> ```json
> {
>   "HARK_SPEECH_REGION": "eastus2",
>   "HARK_SPEECH_RESOURCE_ID": "<your-speech-resource-arm-id>",
>   "HARK_AOAI_ENDPOINT": "https://<your-aoai>.openai.azure.com/",
>   "HARK_AOAI_DEPLOYMENT": "<your-chat-deployment-name>"
> }
> ```

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

- **Toggle:** `Ctrl+Win+H` shows/hides a selectable, resizable, always-on-top captions bar. It docks
  as a **full-width bar at the top** of the screen (native Live Captions layout), and stays movable.
- **HAL-9000 status eye:** a metallic-framed red "eye" indicator that's dim when idle and, while
  listening, glows and **pulses in time with the captured audio** (RMS level, eased at 60fps). Red
  reads as "recording."
- **Speaker diarization:** captions are attributed to anonymous, session-scoped speakers
  (`Guest-1`, `Guest-2`, …) using Azure Speech's `ConversationTranscriber`. Each detected speaker
  gets a **pill**; clicking it opens a dedicated **page** showing just that speaker's lines.
- **CAPTIONS / SUMMARY switch:** a segmented control cross-fades between the live captions and an
  **AI recap** (Teams-style by default; Narrative and per-speaker styles also available). SUMMARY is
  disabled until there are captions to summarize; the recap is cached and only regenerated when the
  captions change, so switching back and forth is free. Requires the optional Azure OpenAI config above.

> Diarization labels are anonymous and can occasionally swap or merge — expected for single-channel
> speaker separation. Spoken/narration audio works best; sung or heavily overlapping speech is harder.

## Provisioning

HARK's Azure resources are defined as **Infrastructure-as-Code** under [`infra/`](infra) (Bicep),
so the whole stack can be stood up reproducibly on any subscription — no click-ops required. Auth
stays keyless (Entra ID / RBAC) throughout; the templates create the resources **and** the
data-plane role assignments.

| File | Purpose |
|---|---|
| `infra/main.bicep` | Subscription-scoped entry point (resource group + modules + outputs) |
| `infra/modules/speech.bicep` | Azure AI Speech account + `Cognitive Services Speech User` role |
| `infra/modules/openai.bicep` | (Optional) Azure OpenAI account + chat deployment + `OpenAI User` role |
| `infra/main.parameters.json` | Sample parameters (region, model, optional overrides) |

> Resource names double as **globally-unique** custom subdomains (required for keyless auth), so
> the templates auto-generate them from a subscription-derived suffix by default — deploying to a
> fresh subscription never collides with an existing one. Supply `speechAccountName` /
> `openAiAccountName` only if you want to pin your own names.

### Option A — GitHub Actions (portable, keyless)

The [`Provision Azure Infra`](.github/workflows/provision-infra.yml) workflow deploys the Bicep to
whichever subscription you point it at, authenticating via **OpenID Connect** (federated
credentials — no keys stored in GitHub). It runs automatically on pushes that touch `infra/**`, and
can also be triggered manually from the **Actions** tab (`workflow_dispatch`) to choose a region and
whether to include Azure OpenAI. On success it prints the exact `dotnet user-secrets` values to set
locally.

One-time setup (per subscription): create an Entra app with a federated credential for this repo,
grant it `Owner` (or `Contributor` + `User Access Administrator`) on the subscription, and add the
`AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, and `AZURE_SUBSCRIPTION_ID` repository secrets. See the
comments at the top of the workflow file for details.

### Option B — deploy from your machine

```powershell
az login
$me = az ad signed-in-user show --query id -o tsv

# Speech only:
az deployment sub create --location eastus2 --template-file infra/main.bicep `
  --parameters infra/main.parameters.json principalId=$me

# ...or include Azure OpenAI for desktop recaps:
az deployment sub create --location eastus2 --template-file infra/main.bicep `
  --parameters infra/main.parameters.json principalId=$me deployOpenAi=true
```

The deployment **outputs** map directly to the user-secrets above
(`speechRegion`, `speechResourceId`, `openAiEndpoint`, `openAiDeployment`).

## Dependencies

| Package | Purpose |
|---|---|
| [NAudio](https://github.com/naudio/NAudio) | WASAPI loopback capture + resampling |
| [Microsoft.CognitiveServices.Speech](https://learn.microsoft.com/azure/ai-services/speech-service/) | Continuous speech recognition + diarization |
| [Azure.AI.OpenAI](https://learn.microsoft.com/azure/ai-services/openai/) | AI recaps (desktop SUMMARY view) |
| [Azure.Identity](https://learn.microsoft.com/dotnet/api/azure.identity) | Keyless Entra ID auth (`AzureCliCredential`) |

## License

MIT
