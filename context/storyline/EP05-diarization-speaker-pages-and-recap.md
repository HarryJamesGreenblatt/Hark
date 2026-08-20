# 🎬 Episode 5 — Diarization, Speaker Pages & AI Recap

> **Date:** 2026-08-19 · **Branch:** `main` · **Commits:** `956fad6..6f5c439`
> **One-liner:** HARK grew from a single-stream caption bar into a multi-speaker experience — real-time speaker diarization, per-speaker pages, and a Teams-style AI recap with a CAPTIONS/SUMMARY mode switch.

## 🎯 Intent
Two arcs in one session:
1. **Make captions actually reliable** — the overlay appeared but sat perpetually "Idle" and, once running, dropped large portions of audio (especially non-English lyrics).
2. **Go beyond captions** — approximate what Teams meeting recaps do: discern that there are multiple speakers, isolate each so they can be read independently or together, and generate a narrative summary.

Delivered incrementally, **one phase per commit**.

## 🛠️ What changed

**Act 1 — Caption reliability (`956fad6`)**
- `Hark.App/App.xaml.cs`, `Hark.App/OverlayWindow.xaml.cs` — start/recognizer failures were only shown in a fleeting tray balloon; added `OverlayWindow.ShowStatus(...)` so errors surface persistently in the bar. Explained the "visible overlay + Idle" state = `StartAsync` threw and was swallowed.
- `Hark.Core/Transcription/AzureSpeechTranscriber.cs` — root cause of dropped speech: the recognizer defaulted to a single `en-US` model. Enabled **continuous language identification** (`SpeechServiceConnection_LanguageIdMode = Continuous`) over a small candidate set when no language is pinned, so mixed-language audio is transcribed instead of discarded.

**Act 2 — Diarization core, Phase 1 (`15c662e`)**
- `Hark.Core/Transcription/TranscriptSegment.cs` — added optional `SpeakerId` (default `null`, so existing call sites keep compiling).
- `Hark.Core/Transcription/ConversationDiarizingTranscriber.cs` (new) — an `ISpeechTranscriber` built on the Speech SDK's `ConversationTranscriber`, emitting anonymous `Guest-N` labels; pins a single language (diarization requires it).
- `Hark.Core/HarkSession.cs` — new `diarize` option selects the engine (defaults **off**, so the CLI is unchanged); transcriber field is now the `ISpeechTranscriber` interface.
- `Hark.App/App.xaml.cs`, `Hark.App/OverlaySink.cs` — desktop app opts into `diarize: true`; overlay lines are prefixed with the speaker label.

**Act 3 — Speaker pages UI, Phase 2 (`3afb4c8`)**
- `Hark.App/ConversationStore.cs` (new) — the shared source of truth: ordered combined transcript + per-speaker line index, with `Changed` / `SpeakerAdded` events. Written in exactly one place (`OverlaySink.Write` on the UI thread), finalized segments only.
- `Hark.App/OverlayWindow.xaml(.cs)` — the overlay became the index with a dynamic **speaker-pill bar**; clicking a pill opens a page.
- `Hark.App/SpeakerWindow.xaml(.cs)` (new) — a dedicated, styled page rendering one speaker's lines, live-refreshed from the store, independently movable/closable/copyable.
- `Hark.App/App.xaml.cs` — owns/opens/cascades speaker pages and resets the conversation each session.

**Interlude — naming + caching prep (`f166d15`)**
- Renamed the index label from CONVERSATION toward **CAPTIONS** (with **SUMMARY** to follow); reverted the header brand to plain **HARK** since a mode switch will show the active mode.
- `Hark.App/ConversationStore.cs` — added a monotonic `Revision` (bumped on commit/clear) to power summary caching.

**Act 4 — AI recap, Phase 3 (`6f5c439`)**
- `Hark.Core/Summarization/` (new) — `ISummarizer`, `SummaryStyle` (Teams / Narrative / PerSpeaker), and `AzureOpenAiSummarizer` (keyless Entra auth, chat deployment).
- `Hark.App/OverlayWindow.xaml(.cs)` — a segmented **CAPTIONS / SUMMARY** switch that cross-fades content; speaker pills collapse in SUMMARY mode; a recap-style picker appears there.
- `Hark.App/App.xaml.cs` — `OnSummaryRequested` builds the transcript from the store and calls the summarizer, with **caching keyed on `Revision` + style** and supersession of in-flight requests. Config read from user-secrets.
- `Hark.Core/Hark.Core.csproj` — added the Azure OpenAI client package.

## 🧠 Decisions
- **Diarization prioritized over multi-language for diarized sessions** — **because** `ConversationTranscriber` works best with a pinned language; the plain engine keeps continuous LID for non-diarized use.
- **Source of truth = `ConversationStore`, UI-thread-only, finalized segments only** — **because** it keeps per-speaker pages stable (no interim churn) and avoids locking.
- **Summary caching keyed on store `Revision` + style** — **because** toggling SUMMARY↔CAPTIONS with no new speech should reuse the result and not re-call the service; new speech (or a new session) invalidates it.
- **Mode switch visually separated from speaker pills** — **because** the mode set is fixed (CAPTIONS/SUMMARY) while pills are a dynamic list; different row, shape, and interaction prevent confusion.
- **All external config stays in user-secrets / keyless Entra** — **because** endpoints, deployment names, and resource identifiers must never be committed; the app authenticates as the signed-in identity holding the appropriate data-plane role.

## 🚧 Problems & resolutions
- **Symptom:** overlay visible but stuck on "Idle" while audio played → **Root cause:** `HarkSession.StartAsync` threw and the error only flashed a tray balloon → **Fix:** surface failures in the bar via `ShowStatus`; confirmed sign-in was valid, so the real issue was elsewhere.
- **Symptom:** English fragments captioned but Spanish dropped, English spotty → **Root cause:** single-language `en-US` recognizer over sung, code-switching audio → **Fix:** continuous language identification across a small candidate set. (Caveat recorded: sung lyrics are worst-case for any STT; spoken/narration works well.)
- **Symptom:** build failures `CS0104` (`Button`/`Brushes` ambiguous) and `CS0117` on `StyleSelector` → **Root cause:** the WPF project has implicit **WinForms** global usings, and `StyleSelector` collides with the WPF type name → **Fix:** fully-qualify the WPF `Button`/`Brushes`, drop the extra `using`, and rename the picker element to `StylePicker`.

## ✅ Verification
- Rebuilt after each phase; whole-solution build green at each commit.
- Live captures confirmed: multi-language capture much improved on a news clip; `Guest-1..4` pills appeared on a multi-speaker dialogue and each pill opened its own page (screenshots reviewed during the session).
- Caption reliability, diarization, and per-speaker pages verified interactively; the recap path is wired and builds, pending a live smoke test once summary config is set.

## 🔓 Open threads
- **Summary smoke test** — set the Azure OpenAI user-secrets and grant the signed-in identity the appropriate OpenAI data-plane role, then verify a live recap and the cache-reuse behavior.
- **README/docs** — document the new summary secrets (variable names only) and the required role.
- **Recap styles** — Teams is the default; consider a persisted style preference and richer per-speaker recaps.
- **Diarization caveats** — labels are anonymous and session-scoped and can occasionally swap/merge; consider a rename/merge affordance.
- **Personal Azure resources** — same deferral as before, now also applies to an Azure OpenAI resource under a personal subscription.
- **Tests** — no coverage yet for `PcmConverter`, the session lifecycle, or `ConversationStore`.
