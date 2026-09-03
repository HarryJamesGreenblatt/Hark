# HARK

**H**$\text{ear}$. &emsp;
**A**$\text{dapt}$. &emsp;
**R**$\text{ender}$. &emsp;
**K**$\text{eep}$.

A developer-grade, scriptable speech-to-text pipeline that captures **system playback audio** on
Windows 11 (WASAPI loopback — no microphone needed) and transcribes it in near real time via
**Azure AI Speech**, emitting clean text to stdout plus optional rolling text, JSON Lines, and SRT
outputs. The desktop overlay adds real-time **speaker diarization**, per-speaker pages, an AI **recap**,
a sound-reactive **"Oracle"** with an **image generating Vision Mode**, and a one-click **multi-format session report** (Markdown,
Word, PowerPoint, PDF, or web) — and can optionally mix in your **local microphone** so a
headset user's own voice is captioned alongside the far side.

It exists to replace accessibility-only tooling (Live Captions, Voice Typing) with something **owned,
automatable, and agent-friendly**.

![.NET 9](https://img.shields.io/badge/.NET-9.0-512BD4) ![Azure AI Speech](https://img.shields.io/badge/Azure-AI%20Speech-0078D4) ![Vision](https://img.shields.io/badge/Vision-FLUX.2--pro%20on%20Foundry-8A2BE2) ![Auth](https://img.shields.io/badge/auth-Entra%20ID%20(keyless)-2F8D46)

---

## Install (Windows)

The quickest way to run the desktop overlay is the installer on the
[**Releases**](https://github.com/HarryJamesGreenblatt/Hark/releases/latest) page — no .NET SDK or
build step required (it ships a self-contained app).

1. Download **`Hark-Setup.zip`** from the latest release and extract it — inside is a single
   `Hark-Setup.exe`. (Shipping the exe *inside a zip* keeps the browser download clean; SmartScreen's
   "isn't commonly downloaded" reputation gate applies to bare executables, not archives.)
2. Run **`Hark-Setup.exe`**. SmartScreen shows *"Windows protected your PC"* for a new publisher —
   click **More info → Run anyway**. The installer runs **elevated** (one **UAC prompt**), since it
   trusts HARK's signing certificate and can optionally provision Azure resources via the Azure CLI.
3. **Enter or confirm your Azure settings.** The window opens on the config fields, prefilled from
   anything already on the machine (env vars → `config.json` → user-secrets): the **Speech** region +
   resource id, and optionally your **Foundry** endpoint + chat/image deployments (which enable the
   **Summary** and **Vision** features). Have no infrastructure yet? Leave them blank — you can
   **provision** them in the next step.
4. Click **Install & Finish.** Nothing touches the machine until this point, so abandoning setup never
   leaves a half-configured install behind. It trusts the cert, installs the packaged app (Start /
   Search entry, Add/Remove Programs, launch-at-startup task), and writes your settings to **both**
   `%APPDATA%\Hark\config.json` **and** user-secrets — so a later re-run is a clean **upgrade install**.
5. _(Optional)_ **Provision Azure infrastructure.** After installing, an optional card can stand up the
   whole stack on whatever subscription you're signed into (`az login`) — Speech + a **Foundry**
   account with the chat and **FLUX** deployments — then fills the config fields from the deployment
   outputs. It **auto-fits FLUX capacity to the subscription's quota**, so it works on a fresh sub
   without a quota dance. Skip it if you already have resources.

HARK lives in the system tray; press **Ctrl+Win+H** to toggle captions. Uninstall via **Add/Remove
Programs** like any packaged app. You still need an Azure **Speech** resource (or provision one) and
`az login` with the right role — see [Prerequisites](#prerequisites) and [Configuration](#configuration).

> **How the installer is built:** pushing a `vX.Y.Z` tag runs
> [`.github/workflows/release.yml`](.github/workflows/release.yml), which builds and **signs** the
> MSIX, compiles the `infra/` Bicep to **ARM JSON**, embeds both into a single self-contained
> `Hark-Setup.exe`, and publishes it (zipped) as a GitHub Release. Signing uses the `MSIX_CERT_PFX` /
> `MSIX_CERT_PASSWORD` repo secrets; the public cert ships inside the installer to establish trust at
> install time. The embedded ARM JSON is what the in-app provisioner deploys (so the target never needs
> the Bicep compiler).

## How it works

HARK's four movements — **Hear · Adapt · Render · Keep**:

```
┌──────────────┐   ┌──────────────┐   ┌──────────────┐   ┌──────────────┐
│     Hear     │──►│    Adapt     │──►│    Render    │──►│     Keep     │
│ capture +    │   │ diarize ·    │   │ the Oracle   │   │ save + the   │
│ transcribe   │   │ refine · name│   │ its eye +    │   │ multi-format │
│ (live caps)  │   │ · summarize  │   │ Vision scene │   │ report       │
└──────────────┘   └──────────────┘   └──────────────┘   └──────────────┘
```

| Movement | Components | Responsibility |
|---|---|---|
| **Hear** | `Capture/LoopbackCaptureService` (+ `MicCaptureService`) · `Audio/PcmConverter` · `Transcription/AzureSpeechTranscriber` | Taps the default render endpoint via WASAPI loopback (+ optional local mic), downmixes/resamples to 16 kHz mono PCM, and streams it to Azure Speech → live captions |
| **Adapt** | `ConversationDiarizingTranscriber` · `FastTranscriptionRefiner` · `SemanticDiarizationRefiner` · `SpeakerNamingRefiner` · `Summarization/AzureOpenAiSummarizer` | Attributes lines to `Guest-N`, re-diarizes offline on Stop, names voices from context, and distils the Conversation/Speaker recaps |
| **Render** | `Hark.Oracle.Vision` (`ConceptDesigner` · `InfographicDesigner` · `VisionRenderer`) | Conjures the Oracle's dual-layer Vision — a native WPF mind-map behind its eye + a FLUX scene in the pupil — beat by beat |
| **Keep** | `Output/*Sink` · `Hark.App/Reporting` | Fans live results to stdout · text · JSON Lines · SRT · the overlay, and exports the whole session as a **Markdown · Word · PowerPoint · PDF · Web** report |

Inside **Hear**, the dataflow is `48k stereo float → (downmix + resample + 16-bit) → 16k mono PCM →
Azure Speech → Interim/Final segments`. `ISpeechTranscriber` is an explicit **swap point**: the Azure
engine is the default, and a local/offline transcriber can drop in without touching the rest of the
pipeline. The solution is three projects over a shared core: **`Hark.Cli`** (terminal — Hear + Keep),
**`Hark.App`** (WPF tray overlay — all four movements), and **`Hark.Oracle`** (the AI recap + Vision
tiers — Adapt + Render), all driving **`Hark.Core/HarkSession`**.

## Prerequisites

- Windows 10/11 with a default output device
- .NET 9 SDK _(to build/run from source; the installer ships self-contained)_
- An Azure AI **Speech** resource (`kind=SpeechServices`, `S0`) — or let the installer **provision** one
- The signed-in identity holds the **Cognitive Services Speech User** role on that resource
- Signed in via Azure CLI (`az login`) — its identity is used for keyless auth
- _(Optional, desktop only)_ A **Foundry (`AIServices`)** account for **Summary** and **Vision**, with a
  **chat deployment** (e.g. `gpt-4.1-mini`) and — for Vision — a **FLUX** deployment (e.g. `flux2-pro`)
  on the **same endpoint**. Your identity needs **Cognitive Services OpenAI User** (chat/gpt-image) and
  **Cognitive Services User** (the FLUX provider route) on it. Without this, captions and speaker pages
  work fully; only **Summary** and **Vision** are disabled (they show a "not configured" note).
- _(Desktop only)_ **Microphone mixing** is off by default (loopback-only, like native Live Captions).
  Toggle the **mic button** in the overlay (or set `HARK_MIX_MIC=1`, or **Ctrl+Shift+M**) to also caption
  your own voice — the headset scenario. Leave it off on speakers, where the mic would re-capture playback
  and double the transcript.

## Configuration

Provide resources via flags, environment variables, an external config file, or `dotnet user-secrets` —
checked in this priority order:

```
CLI flags  →  environment variables  →  %APPDATA%\Hark\config.json  →  dotnet user-secrets
```

| Setting | Flag | Env var / key |
|---|---|---|
| Speech region | `--region eastus2` | `HARK_SPEECH_REGION` |
| Speech resource ARM id | `--resource-id <id>` | `HARK_SPEECH_RESOURCE_ID` |
| Foundry endpoint (Summary + Vision) | — | `HARK_AOAI_ENDPOINT` |
| Chat deployment (Summary) | — | `HARK_AOAI_DEPLOYMENT` |
| Image deployment (Vision) | — | `HARK_AOAI_IMAGE_DEPLOYMENT` |
| Image provider route (FLUX) | — | `HARK_AOAI_IMAGE_PROVIDER` |
| Image quality (gpt-image only) | — | `HARK_AOAI_IMAGE_QUALITY` |
| Mix local mic (desktop) | — | `HARK_MIX_MIC` |

> **The resource ARM id embeds your subscription id**, so it's never hardcoded in source or
> `launchSettings.json`. Store it locally instead (one-time, per project):
> ```powershell
> dotnet user-secrets set "HARK_SPEECH_REGION" "eastus2" --project Hark.Cli
> dotnet user-secrets set "HARK_SPEECH_RESOURCE_ID" "<your-speech-resource-arm-id>" --project Hark.Cli
> # repeat with --project Hark.App for the desktop overlay
> ```
> User secrets live outside the repo (`%APPDATA%\Microsoft\UserSecrets\`) and are never committed.

> **Published exe?** `dotnet user-secrets` is a **development-only** mechanism — it doesn't ship with a
> built executable. For a published build on a non-dev machine, drop the same values in an external
> **`%APPDATA%\Hark\config.json`** (the installer writes this for you). Only resource *locations* live
> here — auth stays keyless, no keys:
> ```json
> {
>   "HARK_SPEECH_REGION": "eastus2",
>   "HARK_SPEECH_RESOURCE_ID": "<your-speech-resource-arm-id>",
>   "HARK_AOAI_ENDPOINT": "https://<your-foundry>.openai.azure.com/",
>   "HARK_AOAI_DEPLOYMENT": "gpt-4.1-mini",
>   "HARK_AOAI_IMAGE_DEPLOYMENT": "flux2-pro",
>   "HARK_AOAI_IMAGE_PROVIDER": "flux-2-pro"
> }
> ```

> **Auth is keyless.** HARK authenticates with `AzureCliCredential` (your `az login` identity) and
> never reads or stores account keys. The explicit credential keeps `DefaultAzureCredential` free for
> other tooling and ensures the role-bearing CLI identity is the one used.

### Summary & Vision (desktop, optional)

The desktop overlay's **Summary** (AI recap) and **Vision** (the Oracle's visualization) tiers both run
on a single **Foundry** endpoint that hosts the chat and image deployments. Point HARK at it with
user-secrets (endpoint + deployment names only — no keys):

```powershell
dotnet user-secrets set "HARK_AOAI_ENDPOINT" "https://<your-foundry>.openai.azure.com/" --project Hark.App
dotnet user-secrets set "HARK_AOAI_DEPLOYMENT" "gpt-4.1-mini" --project Hark.App        # Summary (chat)
dotnet user-secrets set "HARK_AOAI_IMAGE_DEPLOYMENT" "flux2-pro" --project Hark.App     # Vision (image)
dotnet user-secrets set "HARK_AOAI_IMAGE_PROVIDER" "flux-2-pro" --project Hark.App      # FLUX route
```

> The render tier is **provider-agnostic**. Set **`HARK_AOAI_IMAGE_PROVIDER=flux-2-pro`** to render the
> Vision scene via the **Black Forest Labs (FLUX)** route (the effective default), or leave it **unset**
> to use the OpenAI **`gpt-image`** route (with optional **`HARK_AOAI_IMAGE_QUALITY`**). Leave
> `HARK_AOAI_IMAGE_DEPLOYMENT` unset to keep the scene tier off entirely. (The didactic mind-map
> **diagram** behind the eye is rendered **natively** and needs no image deployment.)
>
> Your `az login` identity needs **Cognitive Services OpenAI User** (chat/gpt-image) and, for FLUX,
> **Cognitive Services User** on the Foundry account. If the config is absent, Summary/Vision simply
> show a note instead of failing.

## Usage (CLI)

```powershell
# Easiest: the launcher sets region + resource id, then runs
./run.ps1                              # stream to stdout + transcript.txt
./run.ps1 -Json transcript.jsonl -Srt captions.srt
./run.ps1 -Language en-US -Quiet       # finals only

# Or invoke the CLI directly
dotnet run --project Hark.Cli -- --region eastus2 --out transcript.txt --json transcript.jsonl
```

Play clear-speech audio through your speakers/headphones; transcription streams live and finalized lines
persist to the chosen outputs. Press **Ctrl+C** to stop (SRT is written on exit).

## Desktop overlay (`Hark.App`)

A tray-resident captions bar that reuses the same `Hark.Core` pipeline.

- **Toggle:** `Ctrl+Win+H` shows/hides a selectable, always-on-top captions bar that docks as a
  **full-width bar at the top** of the screen (native Live Captions layout) and fits its content height.
- **The Oracle's eye:** a metallic-framed red "eye" that's dim when idle and, while listening, glows
  and **pulses in time with the captured audio** — the pupil dilates on **bass**, the highlight drifts on
  **treble**.
- **Speaker diarization:** captions are attributed to anonymous, session-scoped speakers (`Guest-1`,
  `Guest-2`, …) via Azure Speech's `ConversationTranscriber`. Each speaker gets a **pill**; clicking it
  opens a dedicated **page** of just that speaker's lines. On **Stop**, an offline **Fast Transcription**
  second pass re-diarizes the buffered audio globally and rebuilds the conversation, fixing streaming
  crossups.
- **Naming speakers:** **right-click a pill → Rename** (applied globally; renaming into an existing name
  **merges** them), or let the **Oracle name speakers automatically**, live, as identities are revealed
  (introductions, direct address, self-ID). A manual name always wins.
- **CAPTIONS / SUMMARY switch:** a segmented control cross-fades between live captions and an **AI recap**
  — **Conversation** (topic-pivoted) or **Speakers** (people-pivoted), both structured/expandable. The
  recap is cached and regenerated only when captions change.
- **Vision — the Oracle:** **clicking the Oracle's eye** dilates it into a full-window Vision page (a
  corner→centre match-cut zoom; the large eye stays audio-reactive) that renders a **dual-layer** live
  visualization of the conversation, **conjured in parallel every beat** by `Hark.Oracle.Vision`:
  - a **native WPF radial mind-map** drawn behind the eye — the eye sits at its empty centre as the hub
    (exact concentricity, crisp text, instant, crossfaded) — from a structured `InfographicConcept`
    (title + colour-coded facet nodes); **plus**
  - a **FLUX cinematographic scene** rendered inside the orb (the pupil) from a `VisualConcept` — anchored
    to each beat's actual subject, rendered evocatively so it tracks the talk without repeating.

  The architectural turn: **a diagram is structured data — drawn natively, not generated by an image
  model** — which freed the generative model (FLUX.2-pro) for the imagery it's actually good at.
- **Timeline & Save — a shareable report:** every conjured beat is kept on a **timeline rail** (click a past
  beat to review it; a **Live** pill returns to the present, and hovering a mind-map pill holds the review so
  you can read a detail). A header **Save** button exports the whole session — transcript, both recaps, and
  the vision slideshow — as **Markdown, Word (`.docx`), PowerPoint (`.pptx`), PDF, or a self-contained web
  page**, via a pluggable `SessionReport` / `IReportWriter` registry. Every format shares one **layout
  language** — a "beat card" that sets the colour-coded mind-map nodes beside the beat's scene — rendered as a
  keep-together table in Word and a **cinematic, editorial deck** in PowerPoint (full-bleed scenes that
  alternate sides, a hero title slide). Missing recaps are generated on demand at save time.

> Diarization labels start anonymous and can occasionally swap or merge — expected for single-channel
> separation; the right-click Rename is always there to fix one. Spoken/narration audio works best; sung
> or heavily overlapping speech is harder.

## Provisioning

HARK's Azure resources are defined as **Infrastructure-as-Code** under [`infra/`](infra) (Bicep), so the
whole stack stands up reproducibly on any subscription — no click-ops. Auth stays keyless (Entra ID /
RBAC) throughout; the templates create the resources **and** the data-plane role assignments.

| File | Purpose |
|---|---|
| `infra/main.bicep` | Subscription-scoped entry point (resource group + modules + outputs) |
| `infra/modules/speech.bicep` | Azure AI Speech account + `Cognitive Services Speech User` role |
| `infra/modules/openai.bicep` | (Optional) **Foundry (`AIServices`)** account + chat + **FLUX** (+ optional gpt-image) deployments, with `Cognitive Services OpenAI User` **and** `Cognitive Services User` roles |
| `infra/main.parameters.json` | Sample parameters (region, models, capacities, optional overrides) |

Toggles: `deployOpenAi` adds the Foundry account (chat + Summary), `deployFlux` (**default true**) adds
the FLUX render tier, and `deployOpenAiImage` (default false) adds an optional gpt-image deployment.

> Resource names double as **globally-unique** custom subdomains (required for keyless auth), so the
> templates auto-generate them from a subscription-derived (deterministic) suffix by default — deploying
> to a fresh subscription never collides, and re-running is idempotent. Supply `speechAccountName` /
> `openAiAccountName` only to pin your own.

There are three ways to deploy it:

### Option A — from the installer (easiest)

Run `Hark-Setup.exe`, install, and use the **Provision Azure infrastructure** card. It deploys the
embedded ARM JSON to whatever subscription you're `az login`'d into, **auto-fits FLUX capacity to that
sub's quota**, and fills the config for you. See [Install](#install-windows).

### Option B — GitHub Actions (portable, keyless)

The [`Provision Azure Infra`](.github/workflows/provision-infra.yml) workflow deploys the Bicep to
whichever subscription you point it at, authenticating via **OpenID Connect** (federated credentials —
no keys stored in GitHub). It runs on pushes that touch `infra/**`, and can be triggered manually
(`workflow_dispatch`) to choose a region and toggles. On success it prints the exact user-secrets to set.

One-time setup (per subscription): create an Entra app with a federated credential for this repo, grant
it `Owner` (or `Contributor` + `User Access Administrator`), and add the `AZURE_CLIENT_ID`,
`AZURE_TENANT_ID`, and `AZURE_SUBSCRIPTION_ID` repository secrets. See the comments at the top of the
workflow file.

### Option C — deploy from your machine

```powershell
az login
$me = az ad signed-in-user show --query id -o tsv

# Speech only:
az deployment sub create --location eastus2 --template-file infra/main.bicep `
  --parameters infra/main.parameters.json principalId=$me

# ...or include the Foundry account (chat Summary + FLUX Vision):
az deployment sub create --location eastus2 --template-file infra/main.bicep `
  --parameters infra/main.parameters.json principalId=$me deployOpenAi=true
```

The deployment **outputs** map directly to the config keys (`speechRegion`, `speechResourceId`,
`openAiEndpoint`, `openAiDeployment`, `fluxDeployment`).

## Dependencies

| Package | Purpose |
|---|---|
| [NAudio](https://github.com/naudio/NAudio) | WASAPI loopback + mic capture and resampling |
| [Microsoft.CognitiveServices.Speech](https://learn.microsoft.com/azure/ai-services/speech-service/) | Continuous speech recognition + diarization |
| [Azure.AI.OpenAI](https://learn.microsoft.com/azure/ai-services/openai/) | AI recaps + Vision concept tier (Foundry chat) |
| [Azure.Identity](https://learn.microsoft.com/dotnet/api/azure.identity) | Keyless Entra ID auth (`AzureCliCredential`) |
| [DocumentFormat.OpenXml](https://learn.microsoft.com/office/open-xml/) | Word (`.docx`) + PowerPoint (`.pptx`) report writers |
| [Microsoft.Web.WebView2](https://learn.microsoft.com/microsoft-edge/webview2/) | Rendering the styled HTML report to PDF |

## License

MIT
