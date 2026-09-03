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

_Last updated: 2026-09-03 (end of Episode 41. Installer work: **diagnosed** the slow-launch feel — root cause is **machine-/first-run-dependent** (compressed single-file cold extraction + Defender/SmartScreen scanning the unsigned exe), **not** app code (explains "faster on this machine") — and **deferred** the fix past 2.1 (real lever = Trusted Signing + installer ReadyToRun + drop single-file compression + a splash). Shipped a UX fix (`5b02534`): the Azure **provisioning form is gated behind a "Provision Azure infrastructure?" opt-in checkbox** so its red button can't be mistaken for Finish. See [`EP41`](./EP41-installer-uac-diagnosis-and-provision-gate.md). Prior: Episode 40 shipped the export-polish cluster + a mic-reset fix.)_w audio band, attraction/repulsion — a research spike), (3) a **generated session title** (replace the hardcoded `"Hark session report"`, one seam fans out to all formats), (4) **PDF export in light mode** (fix WebView2's white page-border clash), (5) a **consistent HAL icon across exports** (embed `Assets/Icon.png` everywhere), and (6) an **installer startup-delay** fix (splash + measure the pre-UAC bundle/verify cost). No code changed; backlog + seams captured. See [`EP37`](./EP37-2.1.0-backlog-lock.md). Prior: Episode 36 completed the five-format export set and cut **HARK 2.0.0**.)_

- **Status:** CLI MVP + desktop overlay working **and now at 2.0.0** — published to the public
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
  recap sections; a captions **LATEST/TRANSCRIPT** scope switch), with a **sound-reactive Oracle's eye**
  (**the Oracle** is HARK's seeing/interpreting presence — the canonical name for the eye + its Vision;
  see [`../oracle.md`](../oracle.md)). A header **Save** button exports the whole session as a **multi-format report**
  (**Markdown · Word · PowerPoint · PDF · Web page**) — every format sharing one **beat-card layout
  language**, with a cinematic editorial **PowerPoint** deck.
- **Branch:** `main` · working tree clean.
- **Apps:** `Hark.Cli` (terminal) and `Hark.App` (WPF tray overlay) drive the shared
  `Hark.Core/HarkSession`. `Hark.App`: starts hidden; `Ctrl+Win+H` toggles the bar; header has a
  ✕ close button; **Ctrl+Shift+M** toggles microphone mixing globally and stays synchronized with
  the overlay mic control (a **session reset** — toggle off→on — returns it to the configured `HARK_MIX_MIC`
  default, so a manual mid-session mic-on doesn't persist). In the desktop app, diarization is on — captions are attributed to
  anonymous `Guest-N` speakers, each with a clickable pill that opens a dedicated page; **right-clicking
  a pill renames** that speaker globally (and an **Oracle naming pass** fills real names in
  automatically, live); a segmented
  **CAPTIONS / SUMMARY** switch cross-fades to a Teams-style recap. **Clicking the Oracle's eye** dilates it
  into a full-window **Vision page** (corner→centre match-cut zoom; the large eye stays
  audio-reactive) that renders a **dual-layer** live visualization of the conversation, **conjured in
  parallel every beat** by `Hark.Oracle.Vision`: a **native WPF radial mind-map** drawn behind the eye
  (the eye sits at its empty centre as the hub — exact concentricity, crisp text, instant, crossfaded)
  from a structured `InfographicConcept` (title + colour-coded nodes), **plus** a **FLUX** cinematographic
  **scene rendered inside the orb** (the pupil) from a `VisualConcept`. The key architectural turn (EP25):
  **a diagram is structured data — drawn natively, not generated by an image model** (which fixed the
  wandering-hub / garbled-text / opaque-square failures of generating diagrams), freeing the generative
  model for the imagery it's actually good at. The render tier is **provider-agnostic** (a `VisionRenderer`
  that drives either the OpenAI `ImageClient` **or** the raw-HTTP **Black Forest Labs** route); **FLUX.2-pro
  is the effective default** (dev user-secrets), gpt-image remains selectable by config. Every conjured beat
  is kept on a **timeline rail** (EP30) — click a past beat to **review** it (the loop pauses), a **Live**
  pill returns to the present; scenes are **spilled to a per-run temp dir** (thumbnails-only in RAM). A
  header **Save** button exports the whole session (transcript + both recaps + the vision slideshow) via a
  pluggable **`SessionReport` / `IReportWriter`** registry behind a file picker (**HTML + Markdown** today).
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
- **North star (vision):** [**The Oracle**](../oracle.md) — HARK's seeing, interpreting presence (the
  canonical identity doc; supersedes the retired **Codename Cristóbal** / **HAL** / **crystal ball**
  monikers). The Oracle's engine dispatches a generative image model to render live *didactic*
  visualizations of the conversation. Key design: HARK's summaries are the **wrong** image seed (literal +
  over-complicated); the right seed is an **art-director refine** that already exists, grounded, in the
  sibling project `sequitur_studios` (its **Production Designer** lands one iconic `visual_concept`). Rides
  the engine road below as a `GroundingEvent` producer. Origin story:
  [`cristobal-vision.md`](../cristobal-vision.md) (deprecated).

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
| 23 | 2026-08-28 | [The Second Machine: Auth, the Installer, One Endpoint & gpt-image-2's Catch](./EP23-second-machine-auth-installer-endpoint-gpt-image-2.md) | Took 1.0.0 to a **second machine** and ran a gauntlet. `AzureCliCredential` needs **elevation** there (`WinError 5`; works running HARK **as admin**) — RBAC was fine (sub-scope Speech/OpenAI User inherit). Keys are **policy-blocked** on that sub (disable-local-auth) and interactive Entra (WAM + multi-tenant app reg + consent) was judged **overkill** for a personal tool, so **run-as-admin is the accepted call**. **Stale pre-Vision user-secrets** hid the installer's config fields → made the panel **always show, prefilled** from env→config.json→user-secrets (`6b6a84b`); the **scrunched** high-DPI window got **PerMonitorV2 + AutoScaleMode.Dpi** (`c524ed9`) but is **still cramped** → WPF rewrite proposed. Vision's "deployment not found" was a **two-foundry split** (chat + image must share one `HARK_AOAI_ENDPOINT`) → **consolidated** to one foundry rather than decouple (decoupling written, then reverted). Released **v1.0.1**. Live finding: **gpt-image-2 is slower + more abstract** than gpt-image-1 — the pipeline is tuned for image-1. |
| 24 | 2026-08-29 | [FLUX on Foundry, the Latency Red Herring & the Ambient Reframe](./EP24-foundry-flux-provider-latency-and-ambient-reframe.md) | Migrated Vision onto a **Foundry (`AIServices`)** account (`fdry-hark-fb360`: gpt-4.1-mini + gpt-image-2 + **flux2-pro** on one endpoint) and built a **dual-path `VisionRenderer`** — OpenAI `ImageClient` **or** a raw-HTTP **Black Forest Labs** provider route (`/providers/blackforestlabs/v1/...`, `cognitiveservices` scope) — behind new `HARK_AOAI_IMAGE_QUALITY`/`HARK_AOAI_IMAGE_PROVIDER` knobs (`cf89cf5`). Chased the "multi-minute lag" and pinned it to **capacity = 1** (~1 RPM → 429 backoff), **not** the model (Spike benchmarks: **FLUX ~10 s vs gpt-image-2 ~35 s**; FLUX quota 15 vs 2) → bumped `flux2-pro` to **capacity 10**; the silent "no image" was `JsonContent.Create` sending **chunked** → BFL `400 no_content_length_header`, fixed with `StringContent`. Hit an **impasse** on the repeating "laptop-loop" + speech lag and **reframed the whole feature**: a research-grounded **design brief** (`crystal-ball-design-brief.md`) recasts Vision as an **AI-directed ambient display** (Valve **AI Director**; **client-side prediction**/cross-dissolve; **calm tech**; **"procedural oatmeal"** = the loop's real name). Commits `0879711..cf89cf5`; brief + Spike `raw` mode uncommitted. |
| 25 | 2026-08-30 | [The Diagram Is Data: Native Rendering & the Dual-Layer Crystal Ball](./EP25-vision-native-diagrams-and-dual-layer-crystal-ball.md) | A long research-driven refactor of Vision. Ruled out dead-ends with evidence (a spike proved **multimodal anti-repetition** varies only *treatment*, not subject — the EP18 ceiling; L4D "director" and embedding-memory rejected). Browser research (no web-search MCP → drive a real browser) surfaced **who owns "continuous non-repetitive visuals for a live stream"** (VJs go abstract-reactive; Deforum interpolates) and the **FLUX.2 API misalignment** (we called FLUX with gpt-image grammar; BFL guide: **no negatives, colour words not hex, front-loaded, typography+JSON**). A spike proved FLUX **can** make a clean infographic when asked right — but the live diagram had a **wandering hub / structural sameness / opaque square** that survived tuning. **The pivot:** *a diagram is structured data — draw it **natively in WPF**, don't generate it.* Built a native **radial mind-map** (exact eye-hub, crisp text, instant, crossfade) from a structured `InfographicConcept`, and made Vision **dual-layer** — native diagram backdrop **+** FLUX scene in the pupil, conjured **in parallel** every beat, `HARK_AOAI_IMAGE_MODE` removed. Commit `99ccbf9`. |
| 26 | 2026-08-30 | [Hardening the Crystal Ball: Reconciled Infra, Toggle Persistence, a Resilient Blinking Pupil & the Scene-Oatmeal Reckoning](./EP26-crystal-ball-hardening-and-scene-oatmeal.md) | Hardened the dual-layer feature end to end. **Reconciled the gpt-image→FLUX artifact debt** incl. the **Foundry/FLUX Bicep** (`AIServices` account + `flux2-pro` `Black Forest Labs` + Cognitive Services User RBAC; gpt-image opt-in, FLUX default). Fixed a **toggle-persistence race** (the async offline refine resurrected the previous session → cancel-on-toggle + supersession guard + silenced balloon). Confirmed **FLUX is enterprise-safe** on Foundry — **"Models sold by Azure"** (Microsoft-hosted, DPA, not shared with BFL, not used to train), *unlike* Partner models (Claude→Anthropic processes). Made the pupil **resilient to RAI refusals** (`content_safety_violation`/BingBlockList, **not** a rate-limit): a recent-image **ring buffer** + filler cycle, and a **blink/crossfade** transition restructured to blink **inside** a circular-clipped pupil. Hardened the **FLUX parser** (200-with-empty-`data` → no more IndexOutOfRange, descriptive body). **Headline open thread:** the photographic **scene** still hits the EP18/EP22 oatmeal on single-topic content — free it to be **evocative** now the diagram carries the literal payload. Commits `258a839..fa2199c`. |
| 27 | 2026-08-30 | [Cinnamon in the Oatmeal: the Anchored Evocative Scene](./EP27-scene-oatmeal-cured-anchored-evocative-scene.md) | Closed EP26's headline thread. **Freed** the photographic scene from EP22's literal-bias, **over-corrected into drift** (a lighthouse / lone wanderer, unrelated to the talk), then landed the **anchored-but-varied middle**: *the diagram labels the structure; the scene opens a window onto THIS beat's actual subject*, cinematically — the variety comes from the beats differing, not invented metaphors (forbid **both** generic-repeat and unrelated-metaphor). Added a **softened-prompt RAI retry** and an average-hash **filler-buffer dedupe**. Also **synced the pupil blink/crossfade** to the up-stroke (no early pop) and rendered the FLUX scene at **pupil-sized 512²** (from 1024²) with a wider 16-frame buffer. Verified across CENTCOM/others. Commits `448e426..e26aea7`. |
| 28 | 2026-08-30 | [The WPF Installer & One-Click Azure Provisioning](./EP28-wpf-installer-and-one-click-azure-provisioning.md) | Rewrote the installer in **WPF** (fixing the Surface's squished high-DPI layout; resizable + scrollable, gliding progress) and gave it an in-app **"Provision Azure infrastructure"** facility — runs the same subscription deployment as the pipeline against **embedded, pre-compiled ARM JSON**, requires elevation, and **auto-fits FLUX capacity** to the target sub's quota (`az cognitiveservices usage list`). Reflowed to **config-first, install-LAST** (no more broken partial installs) with provisioning as an optional post-install view; persists to **config.json + user-secrets** for upgrade installs. Debugged a gauntlet: **SxS manifest** corruption (`--` in an XML comment), the az CLI **"content already consumed"** bug (fixed with `--no-wait` + poll `show`, **not** Bicep/model), a **per-sub quota** preflight failure (limit 4 vs requested 10), and a **silent no-render** (installer wasn't writing `HARK_AOAI_IMAGE_PROVIDER=flux-2-pro` → app used the wrong route). Relabeled config fields to **Foundry**. Validated end-to-end on the Surface. Commits `f2487d8..8b7b5bd`, released **v1.0.3**. |
| 29 | 2026-08-31 | [Field Refinements & Reading the FLUX API (Not Guessing It)](./EP29-field-refinements-and-reading-the-flux-api.md) | Three field-testing fixes plus a course-correction on method. A caption **toggle now fully resets Vision** (`ResetVision()` clears the pupil ring buffer + diagram; `_cachedDiagram` was also being missed), a speaker **rename now refreshes the recap's follow-up-task owners** (invalidate the recap cache so SUMMARY regenerates from the relabeled transcript), and — after the user pushed back on speculation and said to **read the FLUX.2 API without assumptions** — the **content-moderation dial `safety_tolerance` is set to its max (5)** on the FLUX payload (was silently at the default 2). Research (BFL **OpenAPI** + MS Learn): Foundry provides **no built-in content filter for FLUX**, FLUX has **no style "modes"** (only model variants pro/flex/max/klein), and the real block may be a **Microsoft `BingBlockList_Prompt`** layer separate from `safety_tolerance` — so the fairy-tale fix is an **open test**, not a foregone fix. Commit `6e00c77`. |
| 30 | 2026-09-01 | [The Timeline, Disk-Backed Scenes & a Multi-Format Save](./EP30-vision-timeline-disk-backed-scenes-and-multi-format-save.md) | Polished Vision into a reviewable, saveable feature. A **timeline rail** shows every beat as a clickable card (scene thumbnail + title) — open one to **review**, a **Live** pill returns to the present (the autonomous loop pauses meanwhile). Scenes now **spill to disk** (`%TEMP%\Hark\vision-<guid>`, thumbnails-only in RAM, orphan-swept) so RAM stays flat with session length; the history cap rose **12→60** (now a UI-element bound, not RAM), and the idle pupil filler walks whole beats as a **topic recap** during a lull. A new **Save** button aggregates transcript + both recaps + the **vision slideshow** into a self-contained report, refactored into a pluggable **`SessionReport` + `IReportWriter`** registry (`Hark.App/Reporting/`) behind a **`SaveFileDialog`** — **HTML + Markdown** ship now; the chosen forward stack is **first-party** (**PDF via WebView2**, **PPTX + DOCX via Open XML**, one slide per beat), pandoc considered and dropped. A recap **timing-gate remediation was rolled back** (added startup lag, didn't convince) — the real front is the **null-scene gap** (fill it with an **image fallback**, don't tune cadence). Commits `9b6db29..7eb5e75`. |
| 31 | 2026-09-01 | [The Resting Eye: Null-Scene Handling & a Deferred State Machine](./EP31-resting-eye-null-scene-and-deferred-state-machine.md) | Closed EP30's null-scene gap. FLUX nulls are frequent, so the pupil now **cross-fades to the red sound-reactive glow** on a live **topic shift** or a **failed render** (`FadePupilToEye`, 550 ms) — aligned with the mind-map change — and fades the next scene back in when ready; the synthetic **scrying spinner was dropped** (a null just rests on the red eye). The idle filler split cleanly: **LIVE** cycles only the recent IMAGE buffer and **keeps the topic** (the organic "blink through the buildup"); **REVIEW** (Live pill) auto-advances a **synchronized topic+scene slideshow** at ~7 s (finer 2 s tick + explicit review/blink intervals) — so the mind-map never drags backward mid-live. A first fill (`HandleMissingScene`, hold last scene) was **superseded** by the cross-fade. A formal **state-machine refactor** (Nystrom — concurrent **Topic** {LiveFollow⇄ReviewSlideshow} + **Pupil** {Resting·Conjuring·Holding·Blinking}, enum-FSM) was **mapped but deferred**: the informal handling *"works better… without it being formally designed as FSM."* Commits `ea9316b`, `b6cab77`. |
| 32 | 2026-09-01 | [Aligning the Crystal Ball: Topic-Chained Scenes & a FLUX-Idiomatic Composer](./EP32-topic-chained-scenes-and-flux-idiomatic-composer.md) | An evidence check on a captured Aladdin session pinned the alignment question: the **topics** track the transcript faithfully (only ASR errors, "Burton"→"Burden"), but the **image↔topic** match is loose *by architecture* — `InfographicDesigner` and `ConceptDesigner` **independently** distil the same window, so the scene can anchor to a different focal point than the diagram's title. Fix: **chained the scene to the diagram's topic** (`topicAnchor` — diagram runs first, its title anchors the scene concept). Then, after the user asked to **corroborate FLUX guidance online** (fetched BFL's first-party guide), **realigned the prompt composer to FLUX grammar**: subject **front-loaded**, **all negatives purged** (FLUX has no negative prompts — stated twice; "not a collage / never on black" → positive "single coherent scene in a full, real environment"), stance meta-line dropped, ~40-80 words (`ComposeSoftened` too). Also: pupil **"ether" alpha** (0.85, glow bleeds through) + slower 1100 ms standby fade, and **temp-cache hygiene** (`PurgeVisionCache` on toggle-off + `OnExit`). Research also surfaced **camera/lens specificity** and **JSON structured prompting** (schema maps ~1:1 to `VisualConcept`) as Phase 2 (tabled). Commits `3681b5d..2a1dc3a`. |
| 33 | 2026-09-01 | [Word Export & Testing the Source (the OpenXML DOCX Writer)](./EP33-word-export-and-testing-the-source.md) | Multi-format export Phase 2, MD→HTML→**Word** (PPTX saved for last). Added **`DocxReportWriter`** via the **Open XML SDK**: transcript + both recaps + the vision slideshow with each scene **embedded inline** (PNG IHDR parsed for EMU sizing), dodging the project's WPF `Color`/`Size` aliases; registered so the picker offers **Markdown · Word · Web page** (`DocumentFormat.OpenXml` 3.5.1). After the user pushed back on a fragile reflection harness (*"the smoke test was broken… lets just test the source"*), added a real **`Hark.Tests` xUnit project** (in `Hark.slnx`): 3 tests — the `.docx` **opens + validates with 0 Open XML errors** (embedded PNG), MD/HTML carry the content + base64 scene — `dotnet test` **3 passed**. **Deferred:** report **formatting** (all types dump plainly; Word images land on the next page, not beside their beat). Commit `6c43c45`. |
| 34 | 2026-09-02 | [The Report Layout & Vision QoL (HTML as the Design Source)](./EP34-report-layout-and-vision-qol.md) | Closed EP33's report-formatting thread. Designed a report **layout language in HTML** — a small design system (HAL-eye hero, styled section heads, four reusable **cards**: topic, speaker, **beat**, transcript) — then carried the **beat card** (colour-coded mind-map nodes **beside** the scene image, kept together) verbatim into **Markdown** and **Word**. **Vision now leads** (then Conversation summary, Speakers, Transcript). In **Word** the beat is a **keep-together two-column table** (`CantSplit`) on a light printable surface — **fixing the images-land-on-the-next-page bug** — after untangling the WPF-`Color` ambiguity and Open XML's **strict child-ordering** (`OpenXmlValidator` 0 errors). Also fixed that **recaps were missing** from saves (lazily set by the SUMMARY view → a new `ReportRecapsRequested` event generates both scopes on demand). Two **Vision QoL** touches: **save-progress feedback** (busy button + off-UI-thread write) and the review slideshow **holding while a mind-map pill is hovered**. Validated live (USMC/Beatles/Rock) + `dotnet test` **3 passed**. Commits `b015174..8b370b8`. |
| 35 | 2026-09-02 | [The Flagship Deck (PowerPoint Export & Its Design Polish)](./EP35-pptx-flagship-deck.md) | Shipped the flagship **PowerPoint (.pptx)** writer as an editorial, cinematic **dark** deck: a **hero title slide** (first scene full-bleed under a scrim), one slide per beat with the scene in a **full-bleed cover panel that alternates sides** beat to beat, a **kicker / title / accent-rule** hierarchy, **stacked node details** (label with detail hanging-indented beneath, applied to beats + recap/speakers), and a subtle **`HARK` + `NN / NN`** footer; picker now offers **Markdown · Word · PowerPoint · Web page**. Solved a PowerPoint **"repair"** prompt the validator misses — the missing **slide-layout→master back-relationship** + the standard **Presentation/View/TableStyles** parts. A **seam-blend then framed-photo** image experiment was **reverted** as a regression (harsh over light image regions); the full-bleed cover design is banked at **`847e873`**. `Pptx_is_structurally_valid` (0 errors); `dotnet test` **4 passed**. Commits `38b96f3`, `847e873`. |
| 36 | 2026-09-02 | [PDF Export & the 2.0.0 Release](./EP36-pdf-export-and-2.0.0-release.md) | Added the final export — **PDF** via **WebView2** `PrintToPdfAsync` on the styled HTML (loading a **temp file**, since `NavigateToString` caps ~2 MB with embedded scenes) — completing the set (**Markdown · Word · PowerPoint · PDF · Web page**). Polished report details: the beat scene is **vertically centered** beside its nodes in HTML+PDF, and the HTML builder gained a **`transcriptOpen`** option the **PDF** enables (a print can't expand a `<details>`) while the **web page** stays collapsed. Reordered the **deck only** so **Conversation summary + Speakers precede the vision beats** (docs/web/PDF keep Vision leading). Updated the **README** (timeline + Save, the export formats, OpenXML/WebView2 deps), memory, and storyline, then cut **HARK 2.0.0** — the tag-driven release publishing the signed `Hark-Setup.exe`. `dotnet test` **4 passed**. Commits `c0619eb`, `8fb484e`, tag `v2.0.0`. |
| 37 | 2026-09-02 | [Locking the 2.1.0 Backlog](./EP37-2.1.0-backlog-lock.md) | A **planning session** (no code). Reviewed the storyline top-to-bottom and **locked HARK 2.1.0** — six items, each grounded to a code seam: **(1)** rebrand **HARK = Hear · Adapt · Render · Keep** (a code+docs scrub); **(2)** **organic eye motion** (gaze/look-at on mind-map hover, a new audio band → saccades, attraction/repulsion — a research spike, the 2D-gaze worry answered by **layered pupil-offset**); **(3)** a **generated session title** (replace the hardcoded `"Hark session report"` in `BuildSessionReport` — one seam fans out to all five formats, via the `ReportRecapsRequested` on-save hook); **(4)** **PDF export in light mode** (WebView2 prints the dark HTML onto white pages → a `lightMode` flag on `HtmlReportWriter`); **(5)** a **consistent HAL icon** (embed `Assets/Icon.png` — today HTML/PDF use a CSS faux-eye, MD/Word omit it, PPTX uses the scene); **(6)** the **installer's long pre-UAC delay** — *not* reverting to WinForms; the honest read is `requireAdministrator` fires UAC before any managed code, so a splash covers only the **post**-UAC WPF cold-start while the **pre**-UAC gap is single-file verify/extract → **measure first**, then splash + ReadyToRun/Trusted Signing. Backlog + seams in `/memories/repo/hark-2.1.0-backlog.md`. |
| 38 | 2026-09-02 | [The Rebrand: Hear · Adapt · Render · Keep](./EP38-rebrand-hear-adapt-render-keep.md) | Shipped **2.1.0 item #1**. Found the backronym already existed as "Hear · Adapt · **Recognize** · Keep", so this was a precise **Recognize → Render** swap (both spell HARK). Per a scoping question, did the **full re-story** (not tagline-only): the four letters now map onto the **product's movements** — **Hear** = capture+transcribe, **Adapt** = diarize/refine/name/summarize, **Render** = Vision, **Keep** = save/export — with transcription folded into **Hear**. Changed the taglines (README title, CLI banner+`--help`, `Package.appxmanifest` ×2, installer XAML), rewrote the README **"How it works"** as a four-movement diagram + component table, and reconciled the `Hark.Core` stage comments ("Recognize"→"Hear", CLI `[Recognize]`→`[Hear]`). Left **genuine Speech-SDK terms** (`SpeechRecognizer`, `RecognizedSpeech`, `recognizer`) and historical episodes untouched. All three projects build clean. Commit `70aed5a`. |
| 39 | 2026-09-02 | [Naming the Oracle: One Presence, Retiring HAL · Cristóbal · Crystal Ball](./EP39-naming-the-oracle.md) | Consolidated HARK's fragmented visual-interpretive identity under one canonical name — **the Oracle** (a *seer*-eye that *has visions*; already the `Hark.Oracle` tier + the EP19 concept persona). Scope (via 3 questions): **deep** — docs + branding + **code symbols**; **retire "HAL" as a name** (keep the red-eye aesthetic); **deprecate** `cristobal-vision.md` (+ outcome note) and add a **new [`oracle.md`](../oracle.md)**. Renamed the eye symbols (`HalEye→OracleEye`, `HalCornea/Glow/Scale`, `*Big`, `OnHalEyeReleased`, installer `HalButton→OracleButton`), swept ~all HAL/crystal-ball comments + README + icon script + bicep to Oracle vocabulary, and refreshed the living snapshot + North-star line. Left **Speech-SDK terms** and **historical episodes** untouched. Full solution builds (0 errors); `dotnet test` **4 passed**. |
| 40 | 2026-09-03 | [Export Polish: A Generated Title, PDF Light Mode & a Consistent Oracle Mark](./EP40-export-polish-title-pdf-light-icon.md) | Shipped the 2.1.0 **export-polish cluster** (#3–#5). **#3** a **model-generated session title** — a new `title` field on **`MeetingRecap`** (system prompt + strict schema), so it's **free from the existing recap call**; `BuildSessionReport` uses `_lastRecap?.Title` with a static fallback, fanning out to all five formats. **#4** **PDF light mode** — a `lightMode` `:root` override on `HtmlReportWriter` (same pattern as `transcriptOpen`), passed only by `PdfReportWriter`, so the print sits on a light surface (no dark-vs-white-margin clash); the web page stays dark. **#5** a **consistent Oracle-eye icon** — `SessionReport.Icon` (loaded from `Assets/Icon.png` via a new `<Resource>` pack URI) embedded in **all five** writers (HTML/PDF `<img>`, Markdown data-URI, Word inline, PPTX title-slide mark), graceful fallback when absent. The xUnit `SampleReport` now carries an icon so the DOCX/PPTX validators cover it. Builds 0 errors; `dotnet test` **4 passed**. Commit `3e569bb`. |
| 41 | 2026-09-03 | [The Installer: Diagnosing the UAC Delay & Gating the Provision Form](./EP41-installer-uac-diagnosis-and-provision-gate.md) | Took on 2.1.0 #6 (installer). A live clue (*"the 2.0 setup ran faster on this machine than my other one"*) reframed the slow-launch feel as **machine-/first-run-dependent**, not app code: the released `Hark-Setup.exe` is **self-contained + single-file + compressed** (per `release.yml`), **without ReadyToRun**, and the **exe is unsigned** (zipped to dodge SmartScreen) — so the costs that vary machine-to-machine are **compressed single-file cold extraction** to `%TEMP%\.net\` and **Defender/SmartScreen scanning** the Mark-of-the-Web exe *before* it may run (the pre-UAC gap a managed splash can't cover, since `requireAdministrator` fires UAC first). **Deferred** the delay fix past 2.1 (banked levers: **Trusted Signing** + installer **ReadyToRun** + drop `EnableCompressionInSingleFile` + a **splash**). Shipped a UX fix (`5b02534`): the **provisioning form is gated behind a "Provision Azure infrastructure?" opt-in `CheckBox`** (`ProvisionForm` collapsed until ticked) so the red **Provision** button can't be mistaken for **Finish**. Installer builds clean. |

---

## 🔓 Open threads (carried forward)

These are unresolved at the end of the latest episode — natural starting points for the next one.

- **HARK 2.1.0 — the locked backlog (Episode 37):** the six-item milestone is **nearly complete**. **Done:**
  **#1** rebrand to **Hear · Adapt · Render · Keep** (EP38, `70aed5a`); the **Oracle identity**
  consolidation (EP39); **#3** a **model-generated session title** (a `title` field on `MeetingRecap`),
  **#4** **PDF light mode** (a `lightMode` flag on `HtmlReportWriter`), and **#5** a **consistent Oracle-eye
  icon** across all five formats (`SessionReport.Icon`) — the export-polish cluster (EP40, `3e569bb`),
  **live-validated** (*"looking better across each front"*); plus a mic-reset fix (`809805c`) and, in EP41,
  an installer **provisioning-form opt-in gate** (`5b02534`).
  **#6 installer pre-UAC delay is DEFERRED past 2.1** (accepted; diagnosed as machine-/first-run-dependent
  Defender-scan + compressed cold extraction — real fix = Trusted Signing + installer ReadyToRun + drop
  single-file compression + a splash; EP41).
  **Remaining for 2.1:** **(2)** **organic eye motion** for the Oracle's eye (gaze/look-at on mind-map
  hover, a new audio band → saccades, attraction/repulsion — a research spike, the 2D-gaze worry answered
  by **layered pupil-offset**). Full list + seams: `/memories/repo/hark-2.1.0-backlog.md`. Records:
  [`EP37`](./EP37-2.1.0-backlog-lock.md), [`EP38`](./EP38-rebrand-hear-adapt-render-keep.md),
  [`EP39`](./EP39-naming-the-oracle.md), [`EP40`](./EP40-export-polish-title-pdf-light-icon.md),
  [`EP41`](./EP41-installer-uac-diagnosis-and-provision-gate.md).

- **Multi-format export — COMPLETE (Episode 36):** all five formats ship — **Markdown · Word · PowerPoint ·
  PDF · Web page** — sharing one **beat-card layout language**. **PDF** renders via **WebView2**
  `PrintToPdfAsync` on the styled HTML (temp file, not `NavigateToString`); the beat scene is vertically
  centered, and the transcript prints **open** in PDF but stays collapsed on the web page. The **PowerPoint**
  deck is the cinematic treatment (hero title slide, alternating full-bleed cover panels, recaps before the
  beats). Shipped in **HARK 2.0.0** (`v2.0.0`). **Optional/open:** a cleaner image-edge treatment for the deck
  over **light** image regions (the seam-blend/framed-photo attempt was reverted; restore point `847e873`).
  Full record: [`EP36`](./EP36-pdf-export-and-2.0.0-release.md).
- **Report formatting — RESOLVED (Episode 34):** the reports no longer dump plainly. A **layout language** was
  designed in **HTML** (a small design system + four reusable cards) and the **beat card** — colour-coded
  mind-map nodes **beside** the scene image, kept together — was carried verbatim into **Markdown** and **Word**;
  **Vision leads** every format. In **Word** the beat is a **keep-together two-column table** (`CantSplit`),
  fixing the images-on-the-next-page bug. Recaps are now generated **on save** (a `ReportRecapsRequested` event)
  so summaries always appear. Full record: [`EP34`](./EP34-report-layout-and-vision-qol.md).
- **Vision image↔topic alignment — SHIPPED & validated (Episode 32):** the scene
  and diagram were **independent** distillations of the same window (loose match by architecture); now the
  scene is **chained to the diagram's topic** (`topicAnchor`, diagram-first) and the prompt composer is
  **FLUX-idiomatic** (front-loaded, positive-only, negatives purged, ~40-80 words) — both **validated live**
  ("solid enough to push"). **Phase 2 (TABLED):** emit the `VisualConcept` as **FLUX JSON** (BFL "maximum
  control"; schema maps ~1:1) + optional **camera/lens** specificity — FLUX-specific, needs a provider split
  (gpt-image keeps prose). Revisit only if alignment ever needs more. Full record:
  [`EP32`](./EP32-topic-chained-scenes-and-flux-idiomatic-composer.md).
- **Null-scene gap in Vision — the live front (Episode 30):** when a beat's FLUX render fails (`FLUX render
  returned 200 with no image`, or a `content_safety_violation`), the beat is **scene-less**, and the idle
  recap/pupil shuffles awkwardly around the hole ("herky-jerky, getting stuck"). A **timing-gate
  remediation** (`_lastBeatUtc`/`RecapIdle`, only recap during a genuine lull) stopped the fight with live
  generation but added **startup lag** and was **rolled back** — the fix should target the **gap**, not the
  cadence. Two directions: **(a) fill the space** — hold the previous scene, a generated placeholder, or the
  native diagram rasterized into the pupil, so no beat is ever empty; **(b) target the error** — retry /
  soften / the fairy-tale `BingBlockList` thread. Full record:
  [`EP30`](./EP30-vision-timeline-disk-backed-scenes-and-multi-format-save.md).
- **Null-scene gap in Vision — RESOLVED (Episode 31):** frequent FLUX nulls (`FLUX render returned 200 with
  no image` / content refusals) no longer leave the pupil empty or shuffling. On a live **topic shift** or a
  **failed render** the pupil **cross-fades to the red sound-reactive glow** (`FadePupilToEye`, 550 ms),
  aligned with the mind-map change, and the next scene fades back in when it lands; the synthetic **scrying
  spinner was dropped**. The idle filler split into **LIVE** (image-blink only, keep the topic) vs **REVIEW**
  (synchronized topic+scene slideshow, ~7 s) so the mind-map never walks backward mid-live. **Still open (an
  optional upgrade):** the red-glow fill is on-aesthetic but not *topical* — a **native procedural** pupil fill
  (tinted by the beat's own node colours) would make the gap on-topic. A formal **Nystrom state-machine** was
  mapped (concurrent Topic + Pupil machines) but **deferred** — revisit only if the informal handling
  regresses. Full record: [`EP31`](./EP31-resting-eye-null-scene-and-deferred-state-machine.md).
- **Multi-format report export — Phases 2+ (Episode 30):** Save was refactored into a `SessionReport` +
  `IReportWriter` registry behind a file picker; **HTML + Markdown** ship. Remaining, in order: **PDF**
  (WebView2 printing the HTML — the first-party PDF route, load a temp file since `NavigateToString` caps at
  ~2 MB), **PPTX** (Open XML, **beat-per-slide** — the differentiated "deck" mode), **DOCX** (Open XML).
  Adds two **first-party Microsoft** NuGet deps (`Microsoft.Web.WebView2`, `DocumentFormat.OpenXml`) + the
  WebView2 Runtime. Pandoc was considered (user trusts it) and **dropped** as unnecessary. Full record:
  [`EP30`](./EP30-vision-timeline-disk-backed-scenes-and-multi-format-save.md).
- **Distribution / running on a non-dev machine (Episode 23) — the live front:** 1.0.0/1.0.1 install
  and run, but three things bite on a second machine. (1) **Auth — accepted as run-as-admin (no code).**
  `AzureCliCredential` shells out to `az`; on that machine `az` needs elevation (`WinError 5` otherwise),
  so HARK runs **elevated**. Keys are **not an option** — the sub enforces **disable-local-auth**. The
  key-free alternative (interactive Entra: WAM broker + **multi-tenant** app reg + per-tenant consent,
  tenant/client id via config) was scoped and judged **overkill** for a personal tool on machines the
  owner controls; revisit only for uncontrolled distribution. RBAC is a non-issue (sub-scope **Speech
  User** + **OpenAI User** inherit). (2)
  **Installer DPI — RESOLVED (Episode 28): rewritten in WPF.** The scrunched high-DPI window is fixed by a
  full **WPF** rewrite (device-independent units + PerMonitorV2 manifest); the installer is also **resizable
  + scrollable**, reflowed **config-first / install-last**, relabeled to **Foundry**, and gained an in-app
  **"Provision Azure infrastructure"** facility (embedded pre-compiled ARM JSON, `--no-wait`+poll, auto-fit
  FLUX capacity to the sub's quota) that writes the full config incl. `HARK_AOAI_IMAGE_PROVIDER`. Validated
  on the Surface. See [`EP28`](./EP28-wpf-installer-and-one-click-azure-provisioning.md). (3) **One endpoint for chat + image.** HARK uses a single `HARK_AOAI_ENDPOINT` for concept **and**
  render, so both models must live on **one foundry** (different RGs fine, different **resources** not).
  Chose to **consolidate** (deploy gpt-image-2 onto the gpt-4.1 foundry) over decoupling — a
  `HARK_AOAI_IMAGE_ENDPOINT` was written then reverted; revisit only if a real two-resource need returns.
- **Vision image model — FLUX.2-pro on Foundry is now the default (Episode 24):** the EP23 gpt-image-2
  concern was resolved by going **provider-agnostic**. A **Foundry (`AIServices`)** account
  `fdry-hark-fb360` hosts gpt-4.1-mini + gpt-image-2 + **flux2-pro** on **one** endpoint; a **dual-path
  `VisionRenderer`** drives either the OpenAI `ImageClient` **or** the raw-HTTP **Black Forest Labs**
  route (`/providers/blackforestlabs/v1/...`, `cognitiveservices` scope, `StringContent` for
  Content-Length), selected by `HARK_AOAI_IMAGE_PROVIDER` (+ `HARK_AOAI_IMAGE_QUALITY` for gpt-image).
  **FLUX chosen** on the Spike benchmark: **~10 s vs gpt-image-2 ~35 s**, quota **15 vs 2**, and the user
  prefers its photographic look. The "multi-minute" app lag was **capacity = 1** (429 backoff), **not**
  the model → `flux2-pro` bumped to **capacity 10**. **Still open:** the new config knobs aren't in the
  README / installer / `config.json` guidance; a wider A/B (`gpt-image-1.5`, **MAI-Image-2.5**) is
  optional now that FLUX satisfies. Full record: [`EP24`](./EP24-foundry-flux-provider-latency-and-ambient-reframe.md).
- **Vision scene-oatmeal — RESOLVED (Episode 27):** the photographic **pupil scene** was freed from EP22's
  literal-bias, **over-corrected into drift** (a lighthouse / lone wanderer, unrelated to the talk), then
  re-grounded to the **anchored-but-varied middle** — *the diagram labels the structure; the scene opens a
  window onto THIS beat's actual subject*, cinematically, so variety comes from the beats differing (not
  invented metaphors). Added a **softened-prompt RAI retry** and an average-hash **filler-buffer dedupe**,
  and synced the **pupil blink/crossfade** to the up-stroke. Verified across CENTCOM/others. **Still open:**
  the FLUX **negative clause** in `VisionPromptComposer.Compose` is a gpt-image-ism (FLUX can't negate), and
  the **temporary diagnostic toast** in `ShowSceneAsync` should be removed. Full record:
  [`EP27`](./EP27-scene-oatmeal-cured-anchored-evocative-scene.md).
- **Fairy-tale content-filter — the live front (Episode 29):** single-topic *fables* (Jack and the
  Beanstalk, Little Red Riding Hood) render **nothing** — literal/photographic fable-violence trips a
  `content_safety_violation`. Researched the FLUX.2 API first-party: **`safety_tolerance`** (BFL, 0–5,
  default 2) is the moderation dial and we now send **5** (max permissible), but the observed error was
  `**BingBlockList_Prompt**` — a **Microsoft prompt term-block-list** that `safety_tolerance` may not affect.
  **Decisive open test:** caption "Jack and the Beanstalk" — renders → BFL moderation solved; still blocked →
  it's the Microsoft prompt filter, and the lever becomes **prompt-side wording** or a **content-filter
  opt-out** request (research it first-party, don't assume). FLUX has **no style modes** (only variants
  pro/flex/max/klein). Full record: [`EP29`](./EP29-field-refinements-and-reading-the-flux-api.md).
- **gpt-image → FLUX artifact reconciliation (Episode 25) — largely done (Episode 26):** the render tier is now **provider-agnostic**
  with **FLUX.2-pro the effective default**, and the didactic diagram is **rendered natively** (no image
  model) — but ~100 references across the repo still frame Vision around **gpt-image-1/2**, which now
  misleads. **Reconciled in EP25:** this `STORYLINE.md` snapshot + threads, `README.md`, and the
  misleading `VisionRenderer` / `VisionService` / Spike doc comments. **Still stale:** the north-star docs
  (`cristobal-vision.md`, `crystal-ball-design-brief.md`) predate the native-diagram pivot; and
  **`InfographicPromptComposer` is now dead code in the app** (Spike-only, superseded by native rendering)
  — keep as a FLUX-prompt reference or prune.
- **Infra as Code — reconciled (Episode 26):** `infra/main.bicep` + `modules/openai.bicep` +
  `main.parameters.json` now describe the **Foundry (`AIServices`)** account with `gpt-4.1-mini` +
  `gpt-image-2` + **`flux2-pro`** (`Black Forest Labs`, cap 10) and the FLUX **Cognitive Services User**
  RBAC; **FLUX is the default**, **gpt-image is opt-in** (`deployOpenAiImage=false`), verified against the
  live account and `az bicep build`. Remaining caveat: a manual `deployOpenAi=true` run auto-names
  `fdry-hark-<suffix>` — pass `openAiAccountName=fdry-hark-fb360` to reconcile the existing account rather
  than create a parallel one.

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
  **Interim visual is still OPEN — now reframed (Episode 24):** the "convey meaning during every ~1 min
  render" goal is **superseded** by a research-grounded **design brief**
  ([`../crystal-ball-design-brief.md`](../crystal-ball-design-brief.md)) that recasts Vision as an
  **AI-directed ambient display** — (1) an **AI Director** (HOLD/EVOLVE/CUT + **speculative pre-render**),
  (2) **client-side prediction**/**cross-dissolve** (never hard-cut), (3) **calm-tech** ambience (peripheral,
  "work even when it fails" idle state) that **dissolves** the real-time requirement, (4) fixing the
  **"procedural oatmeal"** laptop-loop with framing-variety + content-adaptive visual class. Proposed next
  step: the low-risk **cross-dissolve + always-"becoming" idle state** first. Full records:
  [`EP15`](./EP15-oracle-spike-vision-concept.md),
  [`EP16`](./EP16-hal-eye-vision-mode-shell.md), [`EP17`](./EP17-vision-render-wired-into-app.md),
  [`EP16`](./EP16-hal-eye-vision-mode-shell.md), [`EP17`](./EP17-vision-render-wired-into-app.md),
  [`EP18`](./EP18-vision-autonomous-beats-and-concept-refinement.md),
  [`EP21`](./EP21-vision-sound-reactive-eye-pupil-highlight.md),
  [`EP22`](./EP22-release-1.0.0-and-interim-visual-dead-end.md),
  [`EP24`](./EP24-foundry-flux-provider-latency-and-ambient-reframe.md).
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
