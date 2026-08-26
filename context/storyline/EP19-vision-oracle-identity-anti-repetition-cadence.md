# 🎬 Episode 19 — The Oracle Finds Its Voice: Neutral Identity, Anti-Repetition, Faster Cadence

> **Date:** 2026-08-25 · **Branch:** `main` · **Commit:** `7fdf0c7`
> **One-liner:** Refined the Vision beat engine along three axes live testing demanded — gave the concept
> tier a **neutral "Oracle" identity** (dropped the crystal-ball / iconic-vs-literal dogma), added
> **prompt-agnostic anti-repetition** (each beat is told the vision already on screen and conjures a
> distinct one), and **overhauled the cadence** (removed the beat-check + Jaccard gate that was starving
> renders; render every ~12 s measured from render *start*). Then a narrative test exposed the real
> ceiling: a **~1-minute lag** that's render latency + quota, not cadence — setting up the next phase.

## 🎯 Intent
Close EP18's top open thread (cross-beat anti-repetition) and the two complaints its live tests left:
autonomous images were **"very duplicative"** and **"painfully slow"** between beats. Do it **without**
hard-coding any one use case — keep the Oracle agnostic, acting under its own identity to conjure the beat
rather than chasing a specific prompt.

## 🛠️ What changed
- **Oracle identity** (`ConceptDesigner.cs`) — rewrote `SystemPrompt` into a clean **Oracle persona**:
  *"the inner seer… conjure ONE coherent scene aligned with the beat… no agenda, no fixed style… don't
  force a metaphor, don't force literalism — read the beat and render what it's genuinely about."*
  Removed the "crystal ball" language (it was driving literal crystal imagery), the "iconic **NOT**
  literal" dogma, and all case-specific examples — the model now reads each beat without conflicting
  interior motives.
- **Anti-repetition** (`ConceptDesigner.cs` + `VisionService.cs` + `App.xaml.cs`) — `DesignAsync` takes a
  `previousVision`; when present it appends *"the vision now on screen is «X» — this is a NEW moment,
  conjure a fresh scene with a different setting and subject."* `VisionService.ConjureAsync(window,
  previousVision, ct)` threads it through; `App` tracks `_shownVisionConcept` and feeds it into every
  auto-conjure. Cheap, deterministic, prompt-agnostic — fights *self*-repetition without naming subjects.
- **Cadence overhaul** (`App.xaml.cs`) — **deleted** the cheap beat-check tier (`VisionBeatCheckInterval`)
  and the `IsNewBeat`/`ThemeWords` Jaccard gate that could *block* a new image on a stable topic; deleted
  the `shouldRender` predicate overload. The auto loop now simply conjures once the speech has settled and
  the cadence floor has elapsed. `VisionRenderInterval` **40 s → 12 s**, measured from render **start** so
  it overlaps the model's own latency instead of stacking on top of it; the previous image stays on screen
  until the replacement lands (no blank during conjure).
- **Composer** (`VisionPromptComposer.cs`) — trimmed to match the neutral Oracle voice (carried in the
  same commit).

## 🧠 Decisions
- **Fight duplication by feeding the model its OWN last output, not by hard-coding subjects** — **because**
  the user's explicit constraint is that the Oracle stay agnostic to any one prompt. Passing the prior
  concept back with "make the next one distinct" is a one-line, subject-free steer that generalizes to any
  conversation. It can only fight *self*-repetition, though (see below).
- **Remove the gates; keep one good steer** — **because** the beat-check + Jaccard gate was *starving* the
  loop: a genuinely stable topic blocked every render, which read as "painfully slow." Fewer gates + the
  anti-repetition steer produced *"many more images, less duplication"* in testing. The `gpt-image-1` quota
  (~2 RPM) plus the single-flight guard (`_visionConjuring`) is the real backstop against spam — the
  heuristic gate was redundant cost, not protection.
- **Measure the rate limit from render START, not finish** — **because** finish-to-start spacing stacks on
  top of the ~30 s render latency (≈55 s real gaps); start-to-start folds the latency into the cadence.
- **"Crystal ball" belongs in the *UX*, not the *prompt*** — **because** the word was leaking into the
  imagery (literal crystals). The orb is the frame the user sees; the Oracle should render the beat's
  world, not its own housing.

## ✅ Verification
- Builds green (0 errors) across `Hark.Oracle` and `Hark.App`; committed as `7fdf0c7`.
- **Duplication fix proven live:** a "learn front-end / build in public" stream produced visibly distinct
  scenes across beats (dual-monitor desk, a bookshelf, a library reader, a team whiteboard, an open HTML
  primer) instead of the same frame. Remaining sameness was correctly diagnosed as the **single-topic
  source** — the Oracle reading the beat faithfully, not repeating itself.
- **Narrative test (bunny/bear → rabbit stew → a duck):** hard beat cuts confirmed the images track and
  swap — and made the **latency** unmistakable.

## 🚧 Problems & resolutions
- **Symptom:** images "very duplicative." → **Root cause:** no cross-beat memory + gates blocking renders.
  → **Fix:** `previousVision` anti-repetition steer + removed the starving gates.
- **Symptom:** "painfully slow" between beats. → **Root cause:** 40 s render interval measured from finish,
  stacked on latency; beat-check gate added another 12 s. → **Fix:** 12 s from render start, gates removed.
- **Symptom (new, from the narrative test):** images lag the narration by **~1 minute**. → **Root cause:**
  **not** cadence — it's `gpt-image-1` **render latency** (~20–40 s; the image is always "as of ~30 s ago")
  **plus the ~2 RPM quota** (one image / ~30 s). The cadence *wants* to fire faster than the quota can
  serve; the single-flight guard prevents an unbounded backlog, but the effective ceiling is "a fresh image
  every ~30 s, showing what was said ~30 s ago." Tuning cadence can't beat the model + quota. → **Deferred**
  to the next phase (below).

## 🔓 Open threads
- **NEXT PHASE — fill the render dead-time** (user's standing idea, now the priority). Options ranked
  on-brand → nuclear:
  1. **Scrying shimmer** *(recommended)* — a self-contained "the Oracle is conjuring…" animation inside the
     orb (mist/caustics over the last image, or the HAL eye pulsing). No external calls, no aesthetic clash.
  2. **Surface the fast concept immediately** — the cheap `gpt-4.1-mini` concept lands in ~2 s, long before
     the image; show its **theme text + a palette-tinted shimmer** the instant the beat is understood, then
     dissolve into the real image. Turns dead time into anticipation. (Pairs with #1.)
  3. **Generative CSS / icon motif** keyed to the concept palette — medium effort, on-brand.
  4. **Web GIF / image lookup** — quickest to look rich but external dependency + licensing/aesthetic risk;
     breaks the single-coherent-vision illusion. The user called it "maybe nuclear" — hold unless 1–3 fall short.
- **Cut raw latency** — try a **smaller image size** on `gpt-image-1` to shave the ~30 s; a request for
  **higher RPM quota** would raise the whole ceiling.
- **Tuning knobs** now: `VisionDebounce` 2.5 s · `VisionRenderInterval` 12 s (from start) ·
  `VisionWindowLines` 16. (Removed: `VisionBeatCheckInterval`, the Jaccard gate, the `shouldRender` overload.)
- **Conditioning-morph** (carried) — generate each frame *from* the previous (edits endpoint) for a slow
  dissolve instead of a hard swap.
- Carried: the engine boundary, diarization Fork A, and the standing threads in `STORYLINE.md`.
