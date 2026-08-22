# 🎬 Episode 9 — Structured Recap + Diarization & Engine-Boundary Design

> **Date:** 2026-08-22 · **Branch:** `main` · **Commits:** `b63a6ab`, `b28cbc2`
> **One-liner:** Shipped a nested, Teams-Recap-style structured summary (expandable per-topic notes + follow-up tasks), then designed — but did not yet build — the path to fix speaker crossups (a second-pass, better-diarized transcript) and the engine boundary that turns HARK into a reusable streaming core.

## 🎯 Intent
Two arcs in one session:
1. **Fix the shallow recap.** The Teams summary read far thinner than a real Teams Recap; the user wanted the nested experience — "Meeting Notes with per-topic summaries that expand revealing additional bullets, along with a single-level Follow-up Tasks."
2. **Think forward.** With the recap good enough that "the other modes are almost superfluous," attention shifted to the **distinction of voices** (host/guest keep getting crossed up on Johnny Carson / Space Ghost test clips), a **two-pass refinement** instinct (like VS Code dictation, less phonetic and more grounded), and ultimately a longer-term vision of a **grounding oracle** and shaping HARK as a reusable **engine**.

## 🛠️ What changed

**Structured recap (`b63a6ab`)** — the Teams style now renders as a nested, expandable UI instead of flat text.
- `Hark.Core/Summarization/MeetingRecap.cs` (new) — `MeetingRecap` (Overview + `Topics[]` + `FollowUps[]`), `RecapTopic` (Title, Summary, Details[]), `RecapFollowUp` (Task, Owner?). `[property: JsonPropertyName]` on positional records to match the schema.
- `Hark.Core/Summarization/ISummarizer.cs` — added `SummarizeStructuredAsync` alongside the existing string `SummarizeAsync`.
- `Hark.Core/Summarization/AzureOpenAiSummarizer.cs` — structured generation via **JSON-schema structured outputs** (`ChatResponseFormat.CreateJsonSchemaFormat`, strict) + `ChatCompletionOptions { Temperature = 0.4, MaxOutputTokenCount = 3000 }`. New topic-segmenting system prompt that requires 2–5 detail bullets per topic. Also gave the plain-text styles a token budget.
- `Hark.App/App.xaml.cs` — `OnSummaryRequested` branches: **Teams** → structured (own `_cachedRecap`, revision-keyed), other styles → text path.
- `Hark.App/OverlayWindow.xaml` / `.xaml.cs` — expandable topic cards (a `ToggleButton` header with a rotating chevron + title + one-line summary; a details `ItemsControl` gated by `BooleanToVisibilityConverter`), a single-level follow-up task list with an optional owner pill, and `SetStructuredRecap` / `ShowPlainText` / `ShowStructured` to swap the summary area between plain text and the structured panel.
- `Hark.App/NullOrEmptyToVisibilityConverter.cs` (new) — hides the owner pill when unassigned.

**Repo hygiene (`b28cbc2`)** — `.gitignore` now ignores the local `*.code-workspace`.

_No code was written for the diarization or engine-boundary work — those are **designs** captured below under Decisions and Open threads._

## 🧠 Decisions

- **Recap depth is structural, not a prompt-tuning problem** — **because** the old Teams prompt actively demanded terseness ("concise", "brief"), and single-shot summarization has a documented *compression bias* (long input → disproportionately short output; OpenAI's "Summarizing Long Documents" cookbook). Fix = **JSON-schema structured output + topic segmentation + a real token budget**, so depth scales with the conversation.
- **Teams style goes structured; Narrative/PerSpeaker stay plain text** — **because** the nested expand-to-reveal experience is the Teams idiom specifically; the others are legitimately flat. One overlay area swaps between a text box and the structured panel.
- **Speaker crossups are a streaming-diarization limitation, not a bug we can tune away** — **because** Azure's realtime `ConversationTranscriber` clusters speaker embeddings *incrementally* with no global view and can't revise early guesses; talk-show audio (overlap, applause, music stings, phone-quality guest feeds, single-channel loopback) is the worst case. The user's "capture-the-flag" mental model (loudest/closest voice grabs the ID) maps onto the real failure modes (overlap blends the embedding; similar/degraded voices steal each other's cluster; cold-start centroids; no retroactive relabeling).
- **The fix is a second (offline) pass, which also satisfies the "VS Code dictation" instinct** — **because** the **Fast Transcription API** (`api-version=2025-10-15`) runs offline over the *whole* recording, so it clusters speakers **globally** (far better host/guest separation) and exposes `diarization.maxSpeakers` (2–35), a **phrase list** (proper-noun boosting), and an **LLM Speech (enhanced)** mode with custom prompting — a genuinely language-model-grounded pass. Enabling prerequisite: **buffer the session audio** (currently `HarkSession.OnDataAvailable` streams PCM straight to the recognizer and discards it).
- **Enrollment-based tracking is the "track a voiceprint precisely" ideal, but needs bootstrapping** — **because** target-speaker tracking behaves like the user's original frequency-tracking intuition, but a talk show gives no reference clips up front. The bootstrapped form is **self-enrollment**: maintain a persistent per-speaker centroid (EMA of embeddings), confidence-gated, so identities *self-sharpen* as the stream progresses.
- **A live high-accuracy diarizer would be *near-online* diarization** — **because** the user's proposed confluence (broadcast **N-second delay** for bounded lookahead + an **MIR/DSP front-end** to separate-then-embed and strip music/applause + **self-enrolling embedding reinforcement** + **VBx**-style temporal smoothing) is exactly the real architecture. Honest tradeoff recorded: it reconstructs, at bounded latency, what the offline batch pass gets for free — so build it only if *live* accuracy is a hard requirement; otherwise the stop-time batch pass is ~90% of the benefit for ~5% of the effort.
- **Shape HARK as an engine now, cheaply** — **because** the whole long-term vision (reinforcement, grounding oracle, summary, captions, a future "crystal-ball" live-visual aid) is just producers of / subscribers to **one typed event stream**. The seam already half-exists (`ISpeechTranscriber` events, `ITranscriptSink`, `ConversationStore.Revision`). Formalizing it converts future features from rewrites into new subscribers. (Sketch only this session — see Open threads.)

## 🚧 Problems & resolutions
- **Symptom:** first `dotnet build` failed to copy `Hark.Core.dll` (`MSB3026`, file locked by another MSBuild). → **Root cause:** a running `Hark.App` instance held the DLL. → **Fix:** re-ran the build after the lock cleared; **Build succeeded, 0 errors** (the running app must be closed to see the new recap UI).
- **Grounding oracle — cautions recorded (not problems yet):** continuous retrieval must be **debounced** (trigger on topic-shift/clause boundaries, not per word); outputs must be **confidence-gated** and clearly separate "identified/retrieved fact" from "generated illustration"; and "researching the stream" changes the **privacy posture** (content leaves the box) — a design constraint to decide early for the headset/presentation use case.

## ✅ Verification
- Structured recap: `dotnet build Hark.App` → **Build succeeded, 0 errors**; `get_errors` clean across all edited files. Live visual confirmation pending (close the running app, relaunch, click **SUMMARY** with **Teams** selected → expandable per-topic notes + flat task list).
- Both commits pushed to `origin/main` (`be5f6b9..b28cbc2`).
- Diarization / engine-boundary work is **design only** — nothing to verify yet.

## 🔓 Open threads

**Structured recap (shipped, minor follow-ups)**
- **Live smoke test of the nested recap** — confirm the JSON-schema call returns well-formed topics/tasks on real dialogue and the expand/collapse + owner pills render as intended.
- **Default expansion / animation (optional)** — topics start collapsed (matches Teams); consider expand-by-default or an animated open.

**Diarization — the agreed next build (highest value)**
1. **Buffer the session audio** — tee the converted PCM in `HarkSession` into a memory/temp WAV (prerequisite for any second pass).
2. **Fast Transcription second pass on Stop** — POST the buffered audio with `diarization.maxSpeakers` + a phrase list, rebuild `ConversationStore` from the higher-fidelity, better-diarized result → feeds speaker pages + recap. (Optional: **LLM Speech (enhanced)** mode for the strongest phonetic/context cleanup.)
- **Cheap live stopgap:** a phrase list of expected proper nouns (show/host/guest names) on the live path — helps names, doesn't fix diarization.
- **Research-grade (only if *live* accuracy is required):** near-online diarization — N-sec delay + MIR front-end (VAD, music/noise suppression, overlap-gated target-speaker separation) + self-enrolling embedding reinforcement + VBx smoothing. Would be assembled from ONNX models (separation net, x-vector extractor) + custom windowed clustering, since Azure exposes none of these as realtime knobs.

**Engine boundary (sketched — ready to build as a behavior-preserving slice)**
- Introduce a `HarkEvent` hierarchy in `Hark.Core`: `SegmentEvent`, `AudioLevelEvent`, `StatusEvent`, plus **reserved** `RefinementEvent(SupersedesRevision, Segments)` and `GroundingEvent(Topic, Entities, MatchedWork?, Confidence, SuggestedVisual?)`.
- Add `event Action<HarkEvent> Events` to `HarkSession` (multiplexing the existing Interim/Final/Error/AudioLevel — non-breaking) and move `ConversationStore` down into `Hark.Core` as the **materialized projection** (its `Revision` is the supersession key for `RefinementEvent`).
- Rule that makes it an engine: `Hark.Core` references no WPF/console; producers and consumers only know `HarkEvent` + the conversation projection. The overlay, CLI sinks, and summarizer re-seat as consumers; the second-pass diarizer becomes a `RefinementEvent` producer; a future grounding oracle a `GroundingEvent` producer; a future "crystal-ball" live-visual aid a `GroundingEvent` consumer.

**Grounding oracle (vision — separate future project)**
- A parallel, debounced blackboard process over the transcript emitting `GroundingEvent`s. Two modes: **recognition** (match a known corpus → can *seed refinement* by snapping ASR to canonical text) and **augmentation** (open retrieval/generation → the news-anchor-thumbnail "crystal ball" for live presentations). Confidence-gating + privacy posture are prerequisites.

_(Episode 8's open threads — live end-to-end smoke test, personal-subscription resources, cost hygiene, tests, HAL-eye fine-tuning, language selector — remain carried forward.)_
