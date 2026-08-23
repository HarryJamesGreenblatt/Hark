# 🎬 Episode 14 — The Oracle's Recognition Head: Semantic Diarization Refinement (Stage 0 Plan)

> **Date:** 2026-08-23 · **Branch:** `main` · **Commits:** _(design only — no code this session)_
> **One-liner:** Reframed the long-planned **grounding oracle** as two-headed — Cristóbal only ever used
> the *augmentation* head; the unused *recognition* head is exactly the mechanism to repair diarization
> — and laid a concrete **Stage 0** plan: a text-only LLM **semantic post-pass** that re-labels the
> offline refiner's segments (merging over-splits, fixing host/guest cross-ups) with **immutable text**,
> reusing the existing Azure OpenAI infra and requiring no engine-boundary work.

## 🎯 Intent
Discuss whether the **oracle layer** — designed in Episode 9 and so far associated only with the
[Codename Cristóbal](../cristobal-vision.md) visualization north star — could also serve as a mechanism
to improve HARK's **diarization**, which still misaligns speaker identities (host/guest cross-ups and,
per Episode 12, one continuous speaker split into several `Guest-N`). The offline
`FastTranscriptionRefiner` clusters *globally* but is still **acoustic-only**, so the same failure axes
persist. The session set out to validate the idea and, if it held up, **commence concrete planning for
the cheapest first increment (Stage 0)**.

## 🛠️ What changed
**No code this session** — this is a design + planning episode (like Episode 9's diarization/engine
sketch). The deliverable is the **Decisions** below and the **Stage 0 build plan**, captured concretely
enough to implement directly next session.

## 🧠 Decisions

- **The oracle was always two-headed; recognition is the diarization ally** — **because** Episode 9
  specified the grounding oracle with two modes: **recognition** ("match a known corpus → can *seed
  refinement* by snapping ASR to canonical text") and **augmentation** ("open retrieval/generation →
  the crystal-ball"). Cristóbal explicitly consumes only the *augmentation* half. The *recognition*
  half is unused — and it is precisely the shape of a diarization-repair mechanism. The vision doc
  already separates **fidelity** refinement (diarization `RefinementEvent`) from the **evocative**
  refine (Cristóbal) but asserts *"both ride the same event spine."* Building the recognition head
  first, pointed at fidelity, is higher day-one value **and de-risks Cristóbal** by forcing the shared
  recognition path + event spine into existence before the flashier consumer needs them.

- **A semantic signal is the right tool because diarization error is under-constrained *acoustic*
  clustering** — **because** every current path (streaming `ConversationTranscriber`, offline
  `FastTranscriptionRefiner`) clusters on **embeddings only**. Its failure modes are all acoustic:
  overlap blends embeddings, degraded/phone guest feeds steal clusters, cold-start centroids, and
  over-segmentation. A semantic oracle attacks an **orthogonal error axis** — it reads *content*
  (who's addressed, question/answer structure, first-person continuity), which the acoustics ignore.
  Fusing two independent error sources is the classic ensemble win; neither alone suffices on talk-show
  audio.

- **Stage 0 corrects labels only — text is immutable** — **because** the safest possible design lets the
  LLM **remap `SpeakerId` per segment** but never rewrite, merge, or drop transcript text. We consume
  only an `index → speaker` map from the model and re-stamp the *existing* segments; any text the model
  emits is ignored structurally. This bounds hallucination to zero on the transcript itself and makes
  the pass strictly non-destructive (worst case = today's acoustic result).

- **Reuse the recap infra, add a sibling component** — **because** HARK already calls Azure OpenAI with
  keyless auth + strict JSON-schema structured outputs (`AzureOpenAiSummarizer`). Stage 0 is a new
  stateless `SemanticDiarizationRefiner` mirroring that pattern and the `FastTranscriptionRefiner`
  shape: same `HARK_AOAI_ENDPOINT`/`HARK_AOAI_DEPLOYMENT` config, same `AzureCliCredential`. No new
  model, no new dependency, **no engine boundary required**.

- **Gracefully optional, chained after the acoustic pass** — **because** it slots into
  `App.RefineDiarizationAsync` as one extra `await` between `FastTranscriptionRefiner.RefineAsync(...)`
  and `ConversationStore.Rebuild(...)`. If AOAI is unconfigured (recap already degrades the same way),
  the semantic pass is skipped and the acoustic result stands — identical to today's behavior.

- **Soft constraint, never override** — **because** an over-confident semantic prior can *introduce*
  error (hallucinated binding, collapsing two real speakers into one). The pass reinforces rather than
  dictates: per-index remap falls back to the acoustic label when missing/invalid, and a guard keeps
  the acoustic result if the model collapses a clearly-multi-speaker session to one.

- **Privacy posture is *better* than augmentation** — **because** Stage 0 is an LLM pass over transcript
  **text that already leaves the box for recaps** — it adds *no new* privacy surface, unlike the
  augmentation head's open retrieval. This makes recognition the safer half to ship first.

- **Name/role binding is deferred out of Stage 0** — **because** turning `Guest-N` into real names
  touches the UI (pills, speaker pages) and reopens the "never invent names" tension. Stage 0 scopes to
  **canonical `Guest-N` re-clustering** (merge + swap), which fixes the headline bugs uniformly; name
  binding rides a later stage.

## 🧩 Stage 0 — concrete build plan

**Goal:** collapse over-segmentation (one speaker → three `Guest-N`) and fix host/guest cross-ups by
re-labeling the offline refiner's segments with a content-aware LLM pass, without altering any text.

### New component — `Hark.Core/Transcription/SemanticDiarizationRefiner.cs`
A stateless recognition-mode refiner, sibling to `FastTranscriptionRefiner`:

```csharp
public sealed class SemanticDiarizationRefiner
{
    public SemanticDiarizationRefiner(string endpoint, string deployment, TokenCredential? credential = null);

    /// Returns the same segments with text/offset/duration untouched and SpeakerId possibly remapped.
    /// ≤1 speaker, empty input, or any failure → returns the input unchanged (never worse than acoustic).
    public Task<IReadOnlyList<TranscriptSegment>> RefineAsync(
        IReadOnlyList<TranscriptSegment> segments, CancellationToken cancellationToken = default);
}
```

- Builds an `AzureOpenAIClient(endpoint, cred).GetChatClient(deployment)`, exactly like
  `AzureOpenAiSummarizer`.
- Renders the input as an indexed, labeled list into the user message:
  `[0] Guest-2: text…\n[1] Guest-2: text…\n[2] Guest-1: text…` (index is the array position; label is
  the acoustic `SpeakerId`, or `DefaultSpeaker` if null).
- Strict JSON-schema structured output, `Temperature = 0.0` (deterministic re-labeling), a modest
  `MaxOutputTokenCount` (the response is a compact map, not prose).

### System prompt (intent)
> You correct speaker attribution in a diarized transcript. You are given an ordered list of
> utterances, each with an index and a provisional speaker label (`Guest-N`). Acoustic diarization
> frequently (a) **splits one continuous speaker into several labels** and (b) **swaps two similar
> speakers**. Using conversational coherence — first-person continuity, who is being addressed,
> question/answer structure, turn-taking cadence — reassign each utterance to its **true** speaker.
> **Do not change, merge, or drop any text.** Reuse the same `Guest-N` namespace and canonicalize
> (give each real person one label). If unsure about an utterance, keep its provided label.

### Output schema (strict)
```json
{
  "type": "object",
  "properties": {
    "assignments": {
      "type": "array",
      "items": {
        "type": "object",
        "properties": { "index": { "type": "integer" }, "speaker": { "type": "string" } },
        "required": ["index", "speaker"],
        "additionalProperties": false
      }
    }
  },
  "required": ["assignments"],
  "additionalProperties": false
}
```

### Apply + safety
1. Build `map = index → speaker` from `assignments`.
2. For each original segment `i`: new speaker = `map[i]` if present & non-blank, else the original
   acoustic label (**text/offset/duration copied verbatim**).
3. **Canonicalize** the resulting labels to contiguous `Guest-1..k` in order of first appearance, so
   downstream (`ConversationStore`, speaker pages, Speakers recap) stays clean.
4. **Guards:** empty input or a single acoustic speaker → return input unchanged (no call). If the model
   collapses a session with ≥2 acoustic speakers and substantial total duration down to 1 speaker →
   keep the acoustic result. Any exception → return input unchanged.

### Wiring — `Hark.App/App.xaml.cs` (`RefineDiarizationAsync`)
Insert one chained pass between the acoustic refine and the rebuild:

```csharp
var segments = await refiner.RefineAsync(pcm, maxSpeakers);
if (segments.Count == 0) return;

// Recognition-mode oracle (Stage 0): content-aware relabel, text immutable. Optional — skipped if AOAI unconfigured.
if (!string.IsNullOrWhiteSpace(_aoaiEndpoint) && !string.IsNullOrWhiteSpace(_aoaiDeployment))
{
    var semantic = new SemanticDiarizationRefiner(_aoaiEndpoint!, _aoaiDeployment!, new AzureCliCredential());
    segments = await semantic.RefineAsync(segments);
}

Dispatcher.BeginInvoke(() => { /* _store.Rebuild(...) as today */ });
```

No other call sites change; `ConversationStore.Rebuild` already replaces the whole conversation and the
recap caches already invalidate here.

### Verification plan
- **Repro clips:** the Johnny Carson / Space Ghost test clips that surfaced the over-segmentation
  (Episode 12) — before: one continuous host split into `Guest-1/2/3`; after the semantic pass:
  collapsed to one, host/guest correctly separated. Confirm on the speaker pages + the Speakers recap.
- **Non-destructive guard:** assert the concatenated **text** is byte-identical before/after the
  semantic pass (only labels differ) — a cheap correctness check.
- **Degradation:** with `HARK_AOAI_*` unset, confirm behavior is exactly today's acoustic result.
- `dotnet build` green + `get_errors` clean on the new component and the app.

### Cost / latency
One extra chat call per **Stop** (text-only, no audio), same order of magnitude as a recap call, run in
the existing background `RefineDiarizationAsync` path (off the UI thread). No caching needed — it fires
once per session Stop.

## ✅ Verification
Design/planning only — nothing to run yet. The Stage 0 plan above is the artifact; its own verification
plan is defined inline for the implementation session.

## 🔓 Open threads
- **Stage 0 — ready to build (this episode's plan):** `SemanticDiarizationRefiner` + one chained call in
  `RefineDiarizationAsync`. Highest-value, lowest-risk increment; reuses recap infra; no engine boundary.
- **Stage 0.5 — name/role binding:** let recognition promote `Guest-N` → a real name/role on a confident
  self-introduction ("I'm joined by…", "thanks for having me"). Requires a UI rename affordance on pills
  + speaker pages; keeps the "never invent names" guard (bind only on explicit evidence).
- **Stage 1 — promote onto the engine boundary:** once the typed `HarkEvent` stream + `ConversationStore`
  move into `Hark.Core`, the semantic refiner becomes a `RefinementEvent(SupersedesRevision, Segments)`
  **producer** that can also **re-label live caption history** (the piece Episode 10 left deferred), not
  just the store.
- **Stage 2 — live debounced recognition oracle:** run recognition continuously, **debounced on thematic
  beats** (not per word), confidence-gated — feeding both diarization refinement *and*, eventually,
  Cristóbal as a second `GroundingEvent` consumer. This is where the recognition and augmentation heads
  reunite on one spine.
- **`maxSpeakers` interplay:** with a semantic merge downstream, the acoustic pass could safely *raise*
  its ceiling and let the LLM merge down; left as-is for Stage 0, revisit after measuring.
- **Over-merge risk:** watch for the model collapsing two genuinely-distinct speakers; the single-speaker
  guard is the first line — tune with real clips.

_(Carried forward from earlier episodes: the engine boundary, Codename Cristóbal, RBAC verification for
Fast Transcription, live-caption retroactive re-labeling, and the standing cost/test/overlay-polish
threads all remain in `STORYLINE.md` → Open threads.)_
