# 🎬 Episode 18 — The Living Crystal Ball: Autonomous Beats, Concrete Concepts, Per-Beat Windows

> **Date:** 2026-08-24/25 · **Branch:** `main`
> **One-liner:** Made the Vision page **live** — while open it autonomously re-conjures on genuine topic
> shifts (guarded so it can't spam the image model), then refined the two things live tests exposed:
> concepts were **too abstract/samey** (fixed in the grounded prompt) and new beats kept **referencing the
> first topic** (fixed by windowing each beat to the material spoken *since the last image*).

## 🎯 Intent
Ship **Stage 2** of the Cristóbal oracle — the autonomous trigger that makes the crystal ball evolve with
the conversation without an eye re-click — but only under the cost guards agreed in EP17's design thread
(the user's standing concern: don't open up to moment-to-moment image calls). Then close the two gaps the
live tests surfaced: generic "spotlit microphone on black" images, and new-beat images that kept dragging
in the opening topic.

## 🛠️ What changed
- `Hark.Oracle/Vision/VisionService.cs` — new `ConjureAsync(window, shouldRender, ct)` overload: runs the
  cheap concept, then renders the expensive image **only if the predicate approves**. Lets the caller gate
  the render on the concept (e.g. only on a genuine new beat).
- `Hark.App/App.xaml.cs` — the autonomous loop (Stage 2): a 5 s `DispatcherTimer` started on eye-open,
  stopped on close/exit. Gates per tick: page open · not conjuring · new content since last check ·
  **2.5 s debounce** since the last caption · **12 s** between cheap beat-checks. It then runs a concept
  beat-check; the image renders only when `IsNewBeat` (Jaccard < 0.5 vs the *shown* image's theme) AND
  **≥ 40 s** since the last render. Auto checks never blank the shown orb; only a real beat swaps it.
  Manual eye-click still force-renders. Self-heals if opened before captioning; resets on new session.
- **Concept refinement** (`ConceptDesigner.cs` + `VisionPromptComposer.cs`) — the grounded prompt now
  demands a **concrete, particular** invented scene (tangible objects, a real place, a definite light),
  explicitly rejects vague emotional phrases and the generic "a lone figure / object under a dramatic
  spotlight", and adds **AVOID THE OBVIOUS**: never draw the literal world of the speakers (a talk among
  performers is not a stage/mic/spotlight). The composer stopped forcing "generous negative space" and now
  asks for a real, specific setting — killing the "object floating on black" look.
- **Per-beat windowing** (`App.xaml.cs`) — replaced the fixed `TakeLast(40)` (≈2–3 minutes = several
  topics) with a small **16-line** window; for an **auto beat** it never reaches before the last rendered
  image (`_lastVisionRenderIndex`), so a new image is conjured only from speech spoken **since** the last
  one — no more referencing the opening topic.

## 🧠 Decisions
- **The cheap concept call is the beat detector that gates the expensive render** — **because** a stable
  topic must not re-bill `gpt-image-1` (~2 RPM quota). Chat is cheap; image is not. Same topic → concept
  says "no new beat" → zero image spend. Cost profile: idle → 0; steady topic → cheap chat every ~12 s;
  genuine shift → at most one image / 40 s.
- **Heuristic Jaccard beat gate over an LLM "is this a new topic?" call** — **because** it's free,
  deterministic, and good enough with the min-interval backstop; a missed beat is soft (next check catches
  it) and a spurious one is bounded by the 40 s cap. Comparing the new theme to the *displayed* theme (not
  the last checked) avoids boiling-frog drift.
- **A new beat is windowed from the last image forward, not a rolling window** — **because** a long window
  keeps the first topic in view, so the "new" image kept referencing it (user's diagnosis). Windowing from
  `_lastVisionRenderIndex` (capped to 16 recent lines) makes each new image about the *new* material only.
- **Push the grounded concept toward CONCRETE, not more literal** — **because** the failure was
  over-abstraction into mood, not too much literalism. The fix demands a specific invented picture (the
  prompt's own "kite in a field at golden hour" quality) and forbids the speakers' obvious domain, so
  images are both distinct and meaningful. Tuned gently — the grounding is the crown jewel.

## ✅ Verification
- Builds green (0 errors) across `Hark.Oracle` and `Hark.App` at each step.
- **Stage 2 proven live:** an Adam Scott interview kept the page open and autonomously produced three
  distinct beats tracking the arc (friendship → public/private persona → bittersweet achievement) — the
  trigger fires and rate-limits as designed.
- **Concept refinement proven cheaply** via the concept-only spike: the same Adam Scott window that had
  produced three near-identical spotlit-mic images now yields *"A weathered, golden theatre ticket
  half-buried in soft autumn leaves on a quiet New York street under gentle afternoon light"* — concrete,
  specific, off the stage, and carrying the meaning. (No image cost — validated the judgment before pixels.)

## 🚧 Problems & resolutions
- **Symptom:** autonomous images were "sort of the same" and didn't invoke the conversation's meaning
  (three spotlit mics). → **Root cause:** the prompt said "work from the emotional tone, NEVER surface
  content", over-abstracting into mood; and the composer forced negative space → object-on-black. →
  **Fix:** demand a concrete particular scene + AVOID THE OBVIOUS domain; drop the forced negative space.
- **Symptom:** a new beat's image kept referencing the FIRST topic. → **Root cause:** `TakeLast(40)`
  window spanned multiple topics. → **Fix:** window each auto beat from the last render index, small (16).

## 🔓 Open threads
- **Cross-beat anti-repetition memory** — optionally pass the previous concept/motifs into `DesignAsync`
  with "don't reuse the last image's motifs" for even more variety (held back to keep the change surgical;
  the concreteness fix should diversify on its own).
- **Conditioning-morph** — generate each frame *from* the previous (edits endpoint) for a slow dissolve
  instead of a hard swap.
- **Tuning knobs** live as named consts (`VisionDebounce` 2.5 s · `VisionBeatCheckInterval` 12 s ·
  `VisionRenderInterval` 40 s · `VisionWindowLines` 16 · Jaccard 0.5) — a real long conversation may
  suggest adjustments.
- Carried: the engine boundary, diarization Fork A, and the standing threads in `STORYLINE.md`.
