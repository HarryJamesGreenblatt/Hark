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

_Last updated: 2026-08-22 (end of Episode 9)._

- **Status:** CLI MVP + desktop overlay working, published to the public personal repo
  `github.com/HarryJamesGreenblatt/Hark`. The overlay is a **multi-speaker** experience:
  reliable multi-language captions, real-time speaker diarization, per-speaker pages, and an
  AI recap with a CAPTIONS/SUMMARY mode switch. The **Teams** recap is now a **nested,
  Teams-Recap-style** view — an overview, expandable per-topic "Meeting Notes", and a
  single-level "Follow-up Tasks" list (JSON-schema structured output). It docks as a
  **full-width top bar** with a **sound-reactive HAL-9000 status eye**. The recap model is
  provisioned and wired (Azure OpenAI, `gpt-4.1-mini`, keyless) — pending a live end-to-end smoke test.
- **Branch:** `main` · working tree clean.
- **Apps:** `Hark.Cli` (terminal) and `Hark.App` (WPF tray overlay) drive the shared
  `Hark.Core/HarkSession`. `Hark.App`: starts hidden; `Ctrl+Win+H` toggles the bar; header has a
  ✕ close button. In the desktop app, diarization is on — captions are attributed to anonymous
  `Guest-N` speakers, each with a clickable pill that opens a dedicated page; a segmented
  **CAPTIONS / SUMMARY** switch cross-fades to a Teams-style recap.
- **Pipeline engines (`Hark.Core`):**
  - Non-diarized: `AzureSpeechTranscriber` with **continuous language identification** (mixed
    languages) when no language is pinned.
  - Diarized: `ConversationDiarizingTranscriber` (`ConversationTranscriber`, pinned language,
    `Guest-N` labels). Selected via `HarkSession(diarize: …)` — defaults **off** (CLI unchanged).
  - Recap: `ISummarizer` / `AzureOpenAiSummarizer` (styles: Teams / Narrative / PerSpeaker).
    **Teams** now uses `SummarizeStructuredAsync` → a `MeetingRecap` (Overview + expandable
    `Topics[]` + flat `FollowUps[]`) via JSON-schema structured outputs + `ChatCompletionOptions`
    (temperature 0.4, higher token budget); Narrative/PerSpeaker stay plain text.
- **Source of truth (desktop):** `Hark.App/ConversationStore` — combined + per-speaker transcript,
  written UI-thread-only from `OverlaySink` (finalized segments), with a `Revision` counter used to
  cache summaries (reuse when captions unchanged; regenerate on new speech/session).
- **Auth:** `AzureCliCredential` (explicit), keyless. Requires `az login` with the
  **Cognitive Services Speech User** role on the Speech resource, and — for recaps — the
  **Cognitive Services OpenAI User** role on the Azure OpenAI resource.
- **Config:** all sensitive values live outside source. Resolution precedence is **CLI flags → env
  vars → `%APPDATA%\Hark\config.json` → `dotnet user-secrets`**. Dev machines use user-secrets;
  **published exes** (where user-secrets isn't available) use the external `%APPDATA%\Hark\config.json`
  — both stay out of the repo. Only resource *locations* are stored (region/ARM id, AOAI
  endpoint/deployment); auth stays keyless. Missing recap config shows a friendly inline note.
- **Summary infra:** an Azure OpenAI resource in `rg-hark` with a `gpt-4.1-mini` chat deployment
  backs the SUMMARY view. Provisioning is now **codified** (see below) rather than hand-run.
  Billable; delete/purge when done experimenting.
- **Infra as Code:** the full Azure stack (resource group, Speech resource, optional Azure OpenAI
  account + deployment, and the keyless RBAC role assignments) is defined as **Bicep** under
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

---

## 🔓 Open threads (carried forward)

These are unresolved at the end of the latest episode — natural starting points for the next one.

- **Speaker distinction — the agreed next build (Episode 9 design):** streaming diarization crosses
  host/guest up on talk-show audio. Plan: (1) **buffer the session audio** in `HarkSession` (today
  it's streamed to the recognizer and discarded), then (2) a **Fast Transcription second pass** on
  Stop (`diarization.maxSpeakers` + phrase list, optionally **LLM Speech (enhanced)** mode) that
  re-diarizes offline and rebuilds `ConversationStore` → feeds speaker pages + recap. Cheap stopgap:
  a **phrase list** of expected names on the live path. Research-grade alt (only if *live* accuracy
  is required): near-online diarization (N-sec delay + MIR front-end + self-enrolling embedding
  reinforcement + VBx smoothing).
- **Engine boundary (sketched, ready to build behavior-preserving):** promote the pipeline to a
  typed `HarkEvent` stream in `Hark.Core` (`SegmentEvent`/`AudioLevelEvent`/`StatusEvent` + reserved
  `RefinementEvent`/`GroundingEvent`), add `event Action<HarkEvent> Events` to `HarkSession`
  (non-breaking), and move `ConversationStore` into `Hark.Core` as the materialized projection
  (`Revision` = supersession key). Turns future features (second-pass diarizer, grounding oracle,
  a "crystal-ball" live-visual aid) into subscribers rather than rewrites.
- **Grounding oracle (vision, separate future project):** a debounced blackboard process over the
  transcript emitting `GroundingEvent`s — **recognition** (match known corpus → seed refinement) and
  **augmentation** (open retrieval/generation → live presentation "crystal ball"). Confidence-gating
  + privacy posture are prerequisites.
- **HAL eye fine-tuning:** improved but not perfect; dial `attackTau`/`releaseTau`, the level gain,
  and the glow/pulse terms in `OverlayWindow` to taste; consider an idle "breathing" shimmer.
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
- **Recap styles / persistence:** Teams is the default and now renders as a **structured, nested
  recap** (overview → expandable per-topic notes → follow-up tasks, via JSON-schema output);
  consider persisting the chosen style, an expand-by-default/animation option, and richer
  per-speaker recaps.
- **Diarization caveats:** `Guest-N` labels are anonymous and session-scoped and can occasionally
  swap/merge — this is the streaming-diarization limitation the Episode 9 **second-pass** plan
  targets; consider a rename/merge affordance in the interim.
- **Cost hygiene:** the Azure OpenAI resource is billable; delete/purge when experimentation winds
  down (`az cognitiveservices account delete` + `purge`).
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
