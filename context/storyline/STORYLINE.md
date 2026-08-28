# 🧭 HARK Storyline — Session Handoff Log

An **episodic** record of distinctive working sessions on HARK. Each episode encapsulates one
session's intent, decisions, changes, and loose threads — so any future session (human or agent)
can resume with full context instead of starting cold.

> **Read order for a cold start:** this file's _Current State_ snapshot → the latest episode →
> earlier episodes only if you need the "why" behind a decision.
>
> Prior art: [`../../problem-statement.md`](../../problem-statement.md) is the original brief
> (treat it as **Episode 0** — the pre-implementation research/design handoff).

---

## 📌 Current State (snapshot)

_Last updated: 2026-08-28 (end of Episode 22; HARK **1.0.0** released — plus the concept literal-bias fix and the interim-visual revert)._

- **Status:** CLI MVP + desktop overlay working **and now at 1.0.0** — published to the public
  personal repo `github.com/HarryJamesGreenblatt/Hark` with a tag-driven GitHub Release shipping a
  single **`Hark-Setup.exe`** (self-contained, embeds a signed MSIX). The overlay is a
  **multi-speaker** experience:
  reliable multi-language captions, real-time speaker diarization, per-speaker pages, and an
  AI recap with a CAPTIONS/SUMMARY mode switch. `Hear` now captures **loopback + the local mic**
  (mixed), so a headset user's own voice is captioned alongside the far side. The recap picker is now
  **Conversation** (topic-pivoted) / **Speakers** (people-pivoted) — both **structured, expandable**
  views (JSON-schema output). On Stop, an **offline Fast Transcription second pass** re-diarizes the
  buffered audio globally and rebuilds the conversation, fixing streaming diarization's host/guest
  crossups. The bar docks as a **full-width top bar** whose **height fits its content** (collapsible
  recap sections; a captions **LATEST/TRANSCRIPT** scope switch), with a **sound-reactive HAL-9000
  status eye**.
- **Branch:** `main` · working tree clean.
- **Apps:** `Hark.Cli` (terminal) and `Hark.App` (WPF tray overlay) drive the shared
  `Hark.Core/HarkSession`. `Hark.App`: starts hidden; `Ctrl+Win+H` toggles the bar; header has a
  ✕ close button; **Ctrl+Shift+M** toggles microphone mixing globally and stays synchronized with
  the overlay mic control. In the desktop app, diarization is on — captions are attributed to
  anonymous `Guest-N` speakers, each with a clickable pill that opens a dedicated page; **right-clicking
  a pill renames** that speaker globally (and an **Oracle naming pass** fills real names in
  automatically, live); a segmented
  **CAPTIONS / SUMMARY** switch cross-fades to a Teams-style recap. **Clicking the HAL eye** dilates it
  into a full-window "crystal ball" **Vision page** (corner→centre match-cut zoom; the large eye stays
  audio-reactive) that **conjures a live image — rendered inside the orb — from the conversation** via
  `Hark.Oracle.Vision` (`gpt-image-1`, keyless), captioned with the conversation's theme. The concept tier
  acts under a neutral **Oracle** identity (no crystal-ball / iconic-vs-literal dogma). While the page
  stays open it **autonomously re-conjures on a cadence** (~12 s from render start, debounced), each beat
  told the vision already on screen so it conjures a **distinct** one (prompt-agnostic anti-repetition) and
  windowed to the speech since the last image so the new picture reflects the new topic. Human-paced +
  superseding + revision-cached. **Known ceiling:** images lag ~30–60 s behind narration — `gpt-image-1`
  render latency (~30 s) + ~2 RPM quota, not cadence; filling that dead-time is the next phase.
- **Pipeline engines (`Hark.Core`):**
  - Capture: `LoopbackCaptureService` (system playback / far side) plus, on the desktop,
    `MicCaptureService` (local mic). `HarkSession(mixMicrophone: …)` mixes the mic into the loopback
    stream in the float domain (mic clocks the stream when active; loopback is queued ~1 s) — **default off** (CLI
    unchanged); the app defaults it **off** too, `HARK_MIX_MIC=1` or the overlay mic toggle opts in (headset case).
  - Non-diarized: `AzureSpeechTranscriber` with **continuous language identification** (mixed
    languages) when no language is pinned.
  - Diarized: `ConversationDiarizingTranscriber` (`ConversationTranscriber`, pinned language,
    `Guest-N` labels). Selected via `HarkSession(diarize: …)` — defaults **off** (CLI unchanged). On the
    desktop, `captureAudio: true` buffers the PCM so `FastTranscriptionRefiner` can re-diarize offline
    on Stop and rebuild the conversation with globally-clustered speakers.
  - Recap: `ISummarizer` / `AzureOpenAiSummarizer` — `SummarizeConversationAsync` → `MeetingRecap`
    (overview + expandable topics + follow-up tasks) and `SummarizeSpeakersAsync` → `SpeakerRecap`
    (one expandable card per speaker); both via strict JSON-schema structured outputs.
- **Source of truth (desktop):** `Hark.App/ConversationStore` — combined + per-speaker transcript,
  written UI-thread-only from `OverlaySink` (finalized segments), with a `Revision` counter used to
  cache summaries (reuse when captions unchanged; regenerate on new speech/session). It also holds a
  **persistent acoustic-label → display-name alias map** so a speaker rename (manual or Oracle) follows
  every future utterance of that voice, not just the past ones. It also holds a
  **persistent acoustic-label → display-name alias map** so a speaker rename (manual or Oracle) follows
  every future utterance of that voice, not just the past ones.
- **Auth:** `AzureCliCredential` (explicit), keyless. Requires `az login` with the
  **Cognitive Services Speech User** role on the Speech resource, and — for recaps — the
  **Cognitive Services OpenAI User** role on the Azure OpenAI resource.
- **Config:** all sensitive values live outside source. Resolution precedence is **CLI flags → env
  vars → `%APPDATA%\Hark\config.json` → `dotnet user-secrets`**. Dev machines use user-secrets;
  **published exes** (where user-secrets isn't available) use the external `%APPDATA%\Hark\config.json`
  — both stay out of the repo. Only resource *locations* are stored (region/ARM id, AOAI
  endpoint/deployment); auth stays keyless. Missing recap config shows a friendly inline note.
- **Summary infra:** an Azure OpenAI resource in `rg-hark` with a `gpt-4.1-mini` chat deployment
  backs the SUMMARY view. A **`gpt-image-1`** image deployment (GlobalStandard, `2025-04-15`) is also
  provisioned on the same account for the Vision render tier (`eastus2` supports it; low RPM quota).
  Provisioning is now **codified** (see below) rather than hand-run.
  Billable; delete/purge when done experimenting.
- **Infra as Code:** the full Azure stack (resource group, Speech resource, optional Azure OpenAI
  account + chat deployment, an **optional `gpt-image-1` image deployment** for the Vision render tier
  (`deployOpenAiImage=true`), and the keyless RBAC role assignments) is defined as **Bicep** under
  `infra/` and deployed by a keyless **GitHub Actions** pipeline
  (`.github/workflows/provision-infra.yml`, OIDC). Resource names auto-generate as globally-unique
  by default, so the stack stands up cleanly on any subscription. Deployment outputs map directly to
  the app's user-secrets.
- **How to run:** VS **F5** per project, or `.\run.ps1` (CLI), or raw `dotnet run` — after the
  one-time user-secrets setup for the resources you're targeting.
- **`gh` auth:** enterprise (`hgreenblatt_microsoft`) and personal (`HarryJamesGreenblatt`, owns the
  repo) both configured; `gh auth switch` to flip.
- **Known env gotcha:** the Azure CLI can crash on a broken ACL under
  `~/.azure/cliextensions/account/...` → worked around by **running VS elevated**.
- **North star (vision):** [**Codename Cristóbal**](../cristobal-vision.md) — hook HARK's engine into
  an agent that dispatches a generative image model to render live *didactic* visualizations of the
  conversation. Key design: HARK's summaries are the **wrong** image seed (literal + over-complicated);
  the right seed is an **art-director refine** that already exists, grounded, in the sibling project
  `sequitur_studios` (its **Production Designer** lands one iconic `visual_concept`). Rides the engine
  road below as a `GroundingEvent` producer.

---

## 🗂️ Episodes

| # | Date | Title | Headline outcome |
|---|------|-------|------------------|
| 0 | — | [Problem Statement / Research Brief](../../problem-statement.md) | Chose the stack & architecture (pre-code). |
| 1 | 2026-06-24 | [Foundation & Fixes](./EP01-foundation-and-fixes.md) | Verified the MVP live; fixed auth + stdout; initialized git; added launch profiles. |
| 2 | 2026-06-24 | [Desktop Captions Overlay](./EP02-desktop-captions-overlay.md) | Added `Hark.App` WPF tray overlay (Ctrl+Win+H), selectable/resizable; shared `HarkSession`. |
| 3 | 2026-06-24 | [Overlay Close & Toggle](./EP03-overlay-close-and-toggle.md) | Added ✕ close button + native hidden-until-toggled on/off behavior. |
| 4 | 2026-08-18 | [GitHub Publish & Secret Hardening](./EP04-github-publish-and-secret-hardening.md) | Published to a personal public GitHub repo; found + fixed a leaked subscription id (moved to `dotnet user-secrets`); scrubbed git history. |
| 5 | 2026-08-19 | [Diarization, Speaker Pages & AI Recap](./EP05-diarization-speaker-pages-and-recap.md) | Fixed multi-language captions + silent failures; added speaker diarization, per-speaker pages, and a Teams-style Azure OpenAI recap with a CAPTIONS/SUMMARY mode switch. |
| 6 | 2026-08-19 | [Summary Enablement & AOAI Provisioning](./EP06-summary-enablement-and-aoai-provisioning.md) | Provisioned the Azure OpenAI resource + `gpt-4.1-mini` deployment behind SUMMARY (keyless), wired user-secrets, and documented the setup in the README. |
| 7 | 2026-08-19 | [Overlay UX: Top Bar, SUMMARY Gating & a HAL-9000 Eye](./EP07-overlay-ux-top-bar-and-hal-eye.md) | Full-width top-bar dock; SUMMARY disabled until captions exist; a sound-reactive HAL-9000 status eye, fixed by adopting WavBall's 60fps render-loop reactivity pattern. |
| 8 | 2026-08-20 | [Infrastructure as Code + Provisioning Pipeline](./EP08-infra-as-code-and-provisioning-pipeline.md) | Codified the Azure stack as Bicep + a keyless GitHub Actions pipeline, with auto-unique naming so it stands up on any subscription in one run. |
| 9 | 2026-08-22 | [Structured Recap + Diarization & Engine-Boundary Design](./EP09-structured-recap-and-diarization-engine-design.md) | Shipped a nested Teams-Recap-style structured summary (expandable per-topic notes + follow-up tasks); designed the second-pass diarization fix (Fast Transcription) and the HARK engine boundary (typed `HarkEvent` stream + reserved grounding/refinement seams). |
| 10 | 2026-08-22 | [Conversation/Speakers, Offline Diarization Second Pass & Responsive Overlay](./EP10-conversation-speakers-diarization-secondpass-responsive-overlay.md) | Recap picker → Conversation/Speakers (both structured+expandable); offline Fast Transcription second pass re-diarizes buffered audio on Stop; overlay height fits content with collapsible sections + a LATEST/TRANSCRIPT captions scope switch. |
| 11 | 2026-08-22 | [Microphone Mixing (Hear Yourself Too)](./EP11-microphone-mixing.md) | Added `MicCaptureService`; `HarkSession` mixes the local mic into the transcribed stream (float-domain sum, mic-clocked with a ~1 s loopback queue); a live overlay mic toggle, off by default, `HARK_MIX_MIC=1` opts in — a headset user's own voice is now captioned. |
| 12 | 2026-08-22 | [The HAL Eye & the Feedback Loop](./EP12-hal-eye-and-the-feedback-loop.md) | HARK began captioning/summarizing its own dev session; its recaps became the bug reports — the HAL eye was tuned across rounds from that dictated feedback (de-washed cornea, RMS noise gate, full 0.28–1.0 range, ADSR envelope: fast attack + 0.38 s sustain/resonate). A recap follow-up task shipped a view-aware **copy button** (captions per scope / recap as markdown). Mic now defaults off, uses a mic glyph, and has a manually verified global **Ctrl+Shift+M** toggle. |
| 13 | 2026-08-22 | [Installable Release: MSIX, a Single-File Setup & the SmartScreen Lesson](./EP13-installable-release-and-installer-pipeline.md) | HARK became a shippable Windows app — a HAL-eye icon set, a signed MSIX, and a single self-contained `Hark-Setup.exe` embedding the package, published by a `v*`-tag release pipeline. Five point releases (v0.1.0→v0.1.4) shook out SmartScreen (ship the exe zipped), a duplicate-instance bug (single-instance mutex), and an in-installer Azure-config step that detects existing config across env/config.json/user-secrets. |
| 14 | 2026-08-23 | [The Oracle's Recognition Head: Semantic Diarization Refinement (Stage 0 Shipped & Validated)](./EP14-oracle-assisted-diarization-recognition-head.md) | Reframed the grounding oracle as two-headed — Cristóbal used only the *augmentation* head; the unused *recognition* head is the mechanism to repair diarization. **Shipped Stage 0**: a text-only LLM **semantic post-pass** (`SemanticDiarizationRefiner`) that re-labels the offline refiner's segments (merge over-splits, fix cross-ups) with **immutable text**, reusing the recap AOAI infra, chained into `RefineDiarizationAsync`, no engine boundary; plus a caption re-render + an honest regrouping metric. **Validated live**: synthetic worst-case still drifts, but a real Larry King/Nixon interview diarizes host↔guest correctly (`4→3 speakers, 30 lines regrouped`). |
| 15 | 2026-08-23 | [The Oracle Spike: Vision's Concept Judgment, Native and Proven](./EP15-oracle-spike-vision-concept.md) | Pinned Cristóbal's true form (a **mode of HARK** — HAL is the eye, Cristóbal the mind) and scaffolded `Hark.Oracle` / `Hark.Oracle.Vision`: a **two-tier** augmentation service (art-director **Concept** → gpt-image **Render**) **distilled natively** from sequitur's film-craft grounding (Rizzo Ch.4 + Glebas 7/9/10/11/13 @ `4150645`) — no Python, no runtime coupling. **Concept judgment proven live** (`UNDERSCORE` on nostalgia, `CONTRAST` visual-irony on an ironic window). Render tier valid but untested (needs a `gpt-image-1` deployment). |
| 16 | 2026-08-23 | [The Eye Dilates: HAL-Eye Vision Mode (UX Shell) + the Zoom That Fought Back](./EP16-hal-eye-vision-mode-shell.md) | Built the **UX shell** of the Vision mode: clicking the bar's HAL eye dilates into a full-window "crystal ball" page via a cinematic **corner→centre match-cut zoom** (chrome fades, then the matched eye flies to centre and scales up; the large eye stays audio-reactive). Also fixed an **invalid `Hark.slnx`** (duplicate project entries). The zoom's every-other-time bug took three theories to crack — the real cause was measuring the eye's centre via `TransformToVisual` **before** resetting its render transform. Render tier still deferred (needs `gpt-image-1`). |
| 17 | 2026-08-24 | [The Crystal Ball Sees: Vision Render Wired Into HARK](./EP17-vision-render-wired-into-app.md) | Wired `Hark.Oracle.Vision` into `Hark.App`: clicking the HAL eye now conjures a **real `gpt-image-1` image rendered inside the orb** from the last 40 lines, captioned with the conversation's theme — the **manual, human-paced** increment (one call per eye-open, superseding + revision-cached; autonomous topic-beat trigger deferred). Refined from live tests (Carson clip → an apt porcelain hand; a self-referential test → an eyeball, correctly): image moved **inside** the orb, caption switched to `Theme`, and `medium` quality cut render time to ~30–40 s. |
| 18 | 2026-08-25 | [The Living Crystal Ball: Autonomous Beats, Concrete Concepts, Per-Beat Windows](./EP18-vision-autonomous-beats-and-concept-refinement.md) | **Stage 2**: while the Vision page is open it **autonomously re-conjures on genuine topic shifts** — a 5 s loop where a cheap concept beat-check (Jaccard vs the shown theme) gates the expensive `gpt-image-1` render, debounced (2.5 s) + rate-limited (12 s beat-check / 40 s render) so it can't spam the model. Then fixed two live-test gaps: concepts were too abstract/samey (grounded prompt now demands a **concrete, particular** scene + **avoids the speakers' obvious domain**; composer drops the object-on-black look) and new beats referenced the first topic (each beat now **windowed to the speech since the last image**, not a rolling 40 lines). |
| 19 | 2026-08-25 | [The Oracle Finds Its Voice: Neutral Identity, Anti-Repetition, Faster Cadence](./EP19-vision-oracle-identity-anti-repetition-cadence.md) | Refined the beat engine on three axes live testing demanded: a neutral **Oracle identity** (dropped the crystal-ball / iconic-vs-literal dogma that was leaking literal crystals), **prompt-agnostic anti-repetition** (each beat is told the vision on screen and conjures a distinct one via a `previousVision` steer), and a **cadence overhaul** (removed the beat-check + Jaccard gate that starved renders; render every ~12 s from render *start*, previous image held until the new lands). Proven live: *"many more images, less duplication."* A narrative test (bunny→bear→rabbit stew) then pinned the real ceiling — a **~1-min lag** that's `gpt-image-1` latency + ~2 RPM quota, **not** cadence — teeing up the next phase: fill the render dead-time (scrying shimmer + surface the fast concept immediately). Commit `7fdf0c7`. |
| 20 | 2026-08-27 | [Putting Names to Voices: Manual Rename, Live Oracle Naming & the Alias-Map Fix](./EP20-speaker-naming-manual-rename-and-live-oracle.md) | Turned anonymous `Guest-N` into **real identities** two convergent ways: a **right-click Rename** on the pills (dark popup; global apply + in-order **merge**) and an autonomous **Oracle naming pass** (`SpeakerNamingRefiner`, strict-schema, never-invent) that infers names **live** from introductions/address/self-ID on a `DispatcherTimer` cadence **mirroring the Vision beat loop** — both flowing through one `ConversationStore.Rename`. Key fix: a rename is a **persistent acoustic-label → name alias** applied at `CommitFinal` (not a one-shot rewrite), so the streaming engine's repeating `Guest-N` stops re-spawning after a rename. Named labels stay **stable** (manual override wins); the "Jane vs Dean" miss was pinned as **upstream ASR**, not inference. Commit `daa5a77`. |
| 21 | 2026-08-28 | [The Eye Comes Alive: Banded Audio, an Organic Pupil & a Drifting Highlight](./EP21-vision-sound-reactive-eye-pupil-highlight.md) | Made the Vision eye **sound-reactive across dimensions**: split the capture RMS into **bass/treble** bands (a one-pole low-pass, no FFT) via a new `AudioFeatures` event, then drove the image **"pupil"** dilation from a slow **bass capacitor + under-damped spring** (organic, inertial swell — **WavBall's peak-fed autonomous-goal** idea applied to a UI param, *not* a peak-locked map) and the glass **highlight** from a treble-widened **Lissajous drift**. Also: eye **300→360** with a **thinner silver ring**, and dropped the off-topic abstract `Theme` caption. Tuned across **three live self-tests** (dilation too fast → capacitor+spring; highlight jerky → slow amp follower; pupil too small → higher gains + full-iris rail) into "the animations are looking good." Commit `19f8825`. |
| 22 | 2026-08-28 | [HARK 1.0.0: The Milestone, and the Interim-Visual Dead-End](./EP22-release-1.0.0-and-interim-visual-dead-end.md) | Cut **HARK 1.0.0** (first non-`0.1.x` release; `Release` run success → single `Hark-Setup.zip`, changelog v0.1.4→v1.0.0) after biasing the Oracle **concept tier toward literal, on-topic scenes** (`CONTRAST` only on real irony, anti-repetition softened to "don't change subject just to differ", temp 0.9→0.7) to kill the "chair reading a book" absurdity (`8297cd6`). Key **dead-end**: running the scrying sheen on **every** conjure regressed (autonomous conjuring is near-continuous → sheen always on, masked the pupil swell, "never renders") and was **reverted before commit** — so `main` kept only `03092db`'s first-open buffer. The **interim-visual objective (convey meaning during every render) stays OPEN**; also corrected a self-inflicted false "missing portable zip" discrepancy (WavBall≠HARK). Commits `03092db..8297cd6`, tag `v1.0.0`. |

---

## 🔓 Open threads (carried forward)

These are unresolved at the end of the latest episode — natural starting points for the next one.

- **Installable release — shipped (Episode 13); 1.0.0 cut (Episode 22):** a HAL-eye icon set, a signed MSIX
  (`Package.appxmanifest` with a launch-at-startup task), and a single self-contained
  **`Hark.Installer` → `Hark-Setup.exe`** that embeds the signed package + public cert (trust cert
  → `Add-AppxPackage` → optional Azure-config step). Built by `.github/workflows/release.yml` on a
  `v*` tag. **HARK `v1.0.0`** shipped 2026-08-28 (→ `8297cd6`; `Release` run success; single
  `Hark-Setup.zip`, changelog v0.1.4→v1.0.0). Remaining: **Azure Trusted Signing (~$10/mo)** to sign the
  msix + exe for warning-free
  browser downloads (today the exe is unsigned — zipped to dodge SmartScreen's download block, but a
  first-run "Run anyway" and a self-signed cert-trust UAC remain); a nicer second-launch UX (surface
  the existing instance instead of exiting silently); optional installer rename ("Setup" → "Installer").

- **Speaker distinction — baseline shipped (Episode 10); naming shipped (Episode 20):** an offline
  **Fast Transcription second pass**
  now re-diarizes the buffered session audio on Stop (`FastTranscriptionRefiner` + `maxSpeakers`),
  rebuilding the conversation with globally-clustered speakers. **Speakers can now be named** — manual
  right-click Rename + a live Oracle naming pass, bound to the voice by a persistent alias (see EP20).
  Remaining: verify the **Cognitive
  Services User** RBAC role in the wild; the live CAPTIONS history isn't retroactively re-labeled (needs
  the engine-boundary `RefinementEvent`); revisit the clamped `maxSpeakers` hint; optional phrase-list of
  known names on the live path (would also improve naming); LLM-Speech enhanced mode.
- **Oracle-assisted diarization — Stage 0 shipped & validated (Episode 14):** the grounding oracle's
  unused **recognition head** repairs diarization on an axis orthogonal to acoustics. **Stage 0** is a
  text-only LLM **semantic post-pass** — `Hark.Core/Transcription/SemanticDiarizationRefiner` re-labels
  the offline refiner's segments (merges over-splits, fixes host/guest cross-ups) with **immutable text**
  (consumes only an `index → speaker` map), reusing the recap AOAI config (`HARK_AOAI_*`, keyless),
  chained into `App.RefineDiarizationAsync` between the acoustic refine and `ConversationStore.Rebuild`;
  the caption transcript re-renders from the refined result and a diagnostic balloon reports genuine
  regrouping. Gracefully optional, non-destructive, no engine boundary. **Validated:** clean on a real
  Larry King/Nixon interview (`4→3 speakers, 30 lines regrouped`, host↔guest correct); a synthetic
  worst-case (Space Ghost) still drifts because the residual errors are **sub-segment boundary** (Fork A)
  and **ASR fidelity** — both outside whole-segment remap. Runs once per Stop (one chat call), so no
  debounce needed until the live oracle (Stage 2). Remaining: **Fork A** split-capable refine (peel
  mixed-speaker segments, byte-identical text); ~~name/role binding (0.5)~~ **\u2014 shipped in Episode 20**
  (manual + live Oracle naming through `ConversationStore.Rename`); `RefinementEvent` producer +
  live-history relabel on the engine boundary (1); live debounced oracle that also feeds Cristóbal (2).
  Full record: [`EP14`](./EP14-oracle-assisted-diarization-recognition-head.md).
- **Engine boundary (sketched, ready to build behavior-preserving):** promote the pipeline to a
  typed `HarkEvent` stream in `Hark.Core` (`SegmentEvent`/`AudioLevelEvent`/`StatusEvent` + reserved
  `RefinementEvent`/`GroundingEvent`), add `event Action<HarkEvent> Events` to `HarkSession`
  (non-breaking), and move `ConversationStore` into `Hark.Core` as the materialized projection
  (`Revision` = supersession key). Turns future features (second-pass diarizer, grounding oracle,
  a "crystal-ball" live-visual aid) into subscribers rather than rewrites.
- **Oracle spike — Vision's Concept judgment proven (Episode 15); Vision UX shell shipped (Episode 16):**
  Cristóbal's form is settled — a **mode of HARK** where the **HAL eye** dilates into a crystal ball;
  **HAL is the eye, Cristóbal the mind** (no user-facing "Cristóbal"; the codename lives on the engine).
  New `Hark.Oracle` library, a sibling layer **on top of** `Hark.Core` (`Core` ear → `Oracle` mind →
  `Oracle.Vision` render). `Vision` is **two-tier**: `ConceptDesigner` (art-director persona →
  `VisualConcept`, strict JSON) → `VisionPromptComposer` + `VisionRenderer` (→ gpt-image prompt → Azure
  OpenAI `ImageClient`, keyless). The judgment is **distilled natively** from sequitur's transformative
  `reference/` grounding (Rizzo Ch.4 + Glebas 7/9/10/11/13 @ `4150645`) — **no Python, no runtime
  coupling, no live feed** (distilled once into a baked prompt). **Proven live** (`UNDERSCORE`/`CONTRAST`);
  Render tier valid but untested. **Episode 16 shipped the Vision UX shell** in `Hark.App`: clicking the
  bar's HAL eye dilates into a full-window "crystal ball" page via a **corner→centre match-cut zoom**
  (chrome fades to darkness, then the matched large eye flies to centre and scales up, staying
  audio-reactive); click the eye to return. **`gpt-image-1` is provisioned** (Bicep
  `deployOpenAiImage=true` → a GlobalStandard `2025-04-15` image deployment on `aoai-hark-svl5li`,
  `eastus2`; output `HARK_AOAI_IMAGE_DEPLOYMENT=gpt-image-1`). **Episode 17 wired the render tier into
  the app**: `Hark.App` references `Hark.Oracle`; clicking the eye runs `VisionService.ConjureAsync` on
  the last 40 lines and renders the `gpt-image-1` image **inside the orb**, captioned with the
  conversation's `Theme` — the **manual, human-paced** trigger (one call per eye-open, superseding +
  revision-cached, `medium` quality ~30–40 s). **Proven live** (Carson clip → apt porcelain hand; a
  self-referential test → an eye, correctly; duck+boat proved content-tracking). **Episode 18 shipped
  Stage 2** — the autonomous beat trigger: while the page is open, a 5 s loop runs a cheap concept
  beat-check (Jaccard < 0.5 vs the shown theme) that gates the expensive `gpt-image-1` render, debounced
  (2.5 s) + rate-limited (12 s beat-check / 40 s render) so it can't spam the model; each beat is windowed
  to the speech **since the last image** (`VisionWindowLines` 16) so the new picture reflects the new
  topic, not the first. EP18 also **refined the grounded concept** to demand a concrete, particular scene
  and avoid the speakers' obvious domain (fixing samey spotlit-mic images).
  Remaining: cross-beat **anti-repetition memory** (pass the previous concept/motifs into `DesignAsync`);
  conditioning-**morph** (edits endpoint, slow dissolve instead of a hard swap); tune the beat/window
  constants against a long real conversation; in-Vision affordances (Esc-to-return / minimal in-page
  controls, since the chrome sits behind the opaque canvas while open); optional orb size / `low` quality
  for more speed. **Episode 21** polished the eye itself (banded audio → organic pupil + drifting
  highlight; see the HAL-eye thread). **Episode 22** biased the concept tier to **literal/on-topic**
  (CONTRAST only on real irony; anti-repetition softened to "don't change subject just to differ"; temp
  0.9→0.7, `8297cd6`) — the first lever against the "absurd/off-topic" renders, **needs live proof**.
  **Interim visual is still OPEN:** a first-open-only concept caption + scrying sheen ship in 1.0.0, but
  running the sheen on *every* conjure **regressed** (near-continuous conjuring → sheen always on, masked
  the pupil swell) and was **reverted**; the real goal — convey the current topic's meaning during
  **every** ~1 min render — is unsolved (candidates: a fast `low`-quality first pass then refine; a cheap
  text→icon/CSS motif; keep the concept caption visible under the held image). Full records:
  [`EP15`](./EP15-oracle-spike-vision-concept.md),
  [`EP16`](./EP16-hal-eye-vision-mode-shell.md), [`EP17`](./EP17-vision-render-wired-into-app.md),
  [`EP16`](./EP16-hal-eye-vision-mode-shell.md), [`EP17`](./EP17-vision-render-wired-into-app.md),
  [`EP18`](./EP18-vision-autonomous-beats-and-concept-refinement.md),
  [`EP21`](./EP21-vision-sound-reactive-eye-pupil-highlight.md),
  [`EP22`](./EP22-release-1.0.0-and-interim-visual-dead-end.md).
- **Codename Cristóbal — the visualization north star (design captured):** hook HARK into an agent
  that dispatches a generative image model to render live *didactic* visualizations. The hard-won
  insight: **summaries are the wrong seed** (they force literalism + over-complication); the right seed
  is an **art-director refine** producing **one iconic, metaphor-not-literal `visual_concept`** — which
  **already exists, grounded**, in the sibling project `sequitur_studios` (the **Production Designer**
  agent + Azure `gpt-image-1` backend; reuse its `visual_concept` / `concept_stance` UNDERSCORE·CONTRAST
  / `motifs` vocabulary). Cristóbal = **HARK (live source) ⨝ sequitur (studio)**, but reusing only
  sequitur's grounded **judgment + `gpt-image-1` backend as stateless calls** — **not** its gated,
  human-in-the-loop `Engine`/`Gate` pipeline (that *dailies* model is synchronous by design; wrong for a
  live stream). The **runtime is the async grounding oracle** (debounced, autonomous, superseding); the
  "beat detector" is just the **oracle's trigger**. Cadence: each beat's concept clobbers the last (via
  `Revision`) → a slow-dissolve mood image. It is the **augmentation** half of the oracle: a
  `GroundingEvent` producer, with Cristóbal's image agent as consumer.
  **Enabling spine = the engine boundary (Phases 1–2 below).** Full design: [`context/cristobal-vision.md`](../cristobal-vision.md).
- **HAL eye — refined (Episode 12); sound-reactive across bands (Episode 21):** de-washed (saturated
  cornea + confined top-only gloss) and made genuinely reactive (RMS **noise gate** so silence reads dark,
  full 0.28–1.0 range, an **ADSR envelope follower**). A follow-up caught the last "slow/inconsistent"
  complaint as **structural, not tuning:** the level meter dropped every PCM chunk inside its 50 ms
  throttle and reported one arbitrary chunk's RMS (flicker) — fixed to a true **windowed RMS** over every
  sample, release tightened `0.38→0.22 s`. **Episode 21** took the Vision eye multi-dimensional: the same
  windowed RMS is split into **bass/treble** (a one-pole low-pass, no FFT — a new `AudioFeatures` event)
  and mapped to **orthogonal axes** — broadband → cornea pulse, **bass → an organic pupil dilation**
  (slow capacitor + under-damped spring, *not* a direct map, so it swells/settles with momentum), **treble
  → a drifting glass highlight** (a slow amp-followed Lissajous). The eye also grew 300→360 with a thinner
  silver ring. The "idle breathing shimmer" note below is now partly delivered (an idle-breath sine on the
  pupil + always-on highlight drift). Optional: per-device gate-floor tuning; prune the now-unused
  `SetAudioLevel`/`AudioLevel` path.
- **Copy what's shown — shipped (Episode 12):** a header **copy button** copies whatever the window
  displays per the toggles — captions (LATEST line / full TRANSCRIPT) or the active recap serialized to
  markdown with its nested bullets. Surfaced as a recap follow-up task (to stop screenshotting recaps).
- **Diarization over-segmentation (surfaced by the app, Episode 12):** the live
  `ConversationTranscriber` sometimes splits **one continuous speaker into three** `Guest-N` — seen
  both in mic and loopback-only mode. The Stop second pass clusters globally, but the **live** path
  needs a min-turn/merge heuristic, a lower `maxSpeakers` hint, or the engine-boundary
  `RefinementEvent` re-labeling live history. **Next natural target.**
- **Language selector (native-style):** native Live Captions forces a language choice up front; HARK
  uses continuous LID (non-diarized) / a pinned language (diarized, `en-US`). A native-style picker
  could let users set the diarized language without a rebuild — pairs with the mode-switch row.
- **Live recap smoke test:** run `Hark.App`, capture dialogue, click SUMMARY, and confirm a recap
  returns from the `gpt-4.1-mini` deployment and that the revision-based cache reuses it until new
  speech arrives.
- **Infra as Code + CI/CD:** ✅ **Done (Episode 8)** — Azure provisioning is now Bicep under
  `infra/` driven by a keyless GitHub Actions pipeline. Remaining: optionally enable Azure OpenAI
  by default on the target subscription, and run the live end-to-end smoke test against the
  freshly-provisioned resources.
- **Personal Azure resources (deferred):** provision Speech **and** Azure OpenAI resources under a
  personal subscription, `az login` as that identity, assign the data-plane roles, and point
  `dotnet user-secrets` at them — fully decouples HARK from the enterprise account.
- **Recap styles / persistence:** the picker is now **Conversation** / **Speakers**, both structured
  and expandable (Narrative dropped). Consider persisting the chosen style/scope and an
  expand-by-default option.
- **Overlay refinement (minor, Episode 10):** a "spot of refinement" on the responsive layout \u2014 e.g. the
  cross-fade height settle on mode switch, the LATEST-scope wrap cap, the LATEST/TRANSCRIPT labels, and
  the default caption scope.
- **Diarization caveats:** `Guest-N` labels are anonymous and session-scoped and can occasionally
  swap/merge — this is the streaming-diarization limitation the Episode 9 **second-pass** plan
  targets; consider a rename/merge affordance in the interim.
- **Cost hygiene:** the Azure OpenAI resource is billable; delete/purge when experimentation winds
  down (`az cognitiveservices account delete` + `purge`).
- **Mic mixing — shipped (Episode 11):** `Hear` now also captures the local mic and mixes it into the
  transcribed stream (`MicCaptureService` + float-domain sum, mic-clocked when active with a ~1 s
  drop-oldest loopback queue), off by default with a live overlay mic toggle (`HARK_MIX_MIC=1` opts
  in). The mic-as-clock design fixed the total-silence stall (loopback fires no callbacks with nothing
  playing). Remaining: **speakers** re-capture playback → echo/double transcript unless mic is left off
  (an AEC pass would fix it properly); a per-source "this is me" diarization hint could label the local
  speaker deterministically.
- **Tests:** no coverage yet for `PcmConverter`, the `HarkSession` lifecycle, or `ConversationStore`.
- **Overlay polish (optional):** drag-from-anywhere (except while selecting); click-through toggle;
  persistent settings (position, opacity, font size) vs env vars.
- **Permanent CLI fix:** replace the "run elevated" workaround with an ACL repair
  (`icacls "$env:USERPROFILE\.azure\cliextensions\account" /reset /T /C /Q`) or
  `az extension remove --name account`.
- **Launcher parity (optional):** align `run.ps1` defaults with the VS "with transcripts" profile.
- **Pending memory:** approve/save the credential convention (see Episode 1 → Decisions).

---

## ✍️ Episode template

Copy [`_TEMPLATE.md`](./_TEMPLATE.md) to `EP<NN>-<slug>.md` for each new session, then add a row to
the **Episodes** table and refresh the **Current State** snapshot above.
