# 🎬 Episode 17 — The Crystal Ball Sees: Vision Render Wired Into HARK

> **Date:** 2026-08-24 · **Branch:** `main`
> **One-liner:** Wired the `Hark.Oracle.Vision` render tier into `Hark.App` so clicking the HAL eye now
> conjures a **real image inside the orb** — the manual, human-paced increment of Cristóbal — and refined
> it from live tests: image *inside* the crystal ball, a conversation-relative caption, and a faster
> `medium`-quality render.

## 🎯 Intent
Close the last gap from EP15/EP16: make the HAL-eye Vision page render **actual pixels** (not a
placeholder) from the live conversation, using the `gpt-image-1` deployment provisioned earlier this day.
Scope deliberately limited to the **manual trigger** (conjure on eye-open) — the autonomous topic-beat
trigger is a later stage — after an explicit design discussion about *not* opening up to excessive,
moment-to-moment image calls.

## 🛠️ What changed
- `Hark.App/Hark.App.csproj` — added a `ProjectReference` to `Hark.Oracle` (pulls `Oracle.Vision`
  transitively; the image SDK deps were already in the tree).
- `Hark.App/App.xaml.cs` — reads `HARK_AOAI_IMAGE_DEPLOYMENT`; `BuildVisionService()` composes a
  `ConceptDesigner` + optional `VisionRenderer` (null renderer ⇒ concept-only text, no crash);
  `OnVisionRequested()` pulls the **last 40 lines** from `ConversationStore`, calls `ConjureAsync`, and
  renders the result. Superseding + cancellation (`_visionCts`), and a **revision cache**
  (`_cachedVisionImage`/`_cachedVisionRevision`) so re-opening on unchanged captions doesn't re-bill.
- `Hark.App/OverlayWindow.xaml(.cs)` — `VisionRequested`/`VisionClosed` events (raised from
  `OpenVision`/`CloseVision`); the render appears **inside the eye's orb** (a `VisionOrb` ellipse with an
  `ImageBrush`, over the red cornea and under the glass gloss); `SetVisionImage`/`SetVisionConcept`/
  `SetVisionStatus` drive it; the big eye grew 220→300 px so the orb image is legible.
- `Hark.Oracle/Vision/VisionRenderer.cs` — set `Quality = new GeneratedImageQuality("medium")` (the SDK
  type is an extensible struct; only `Standard`/`High` are predefined, so the gpt-image tiers pass by
  name) — a much faster render than the default for a live, ambient mood image.

## 🧠 Decisions
- **Manual trigger first; autonomous beat-detection deferred** — **because** the excessive-call risk the
  user flagged only materializes with an *automatic* trigger. Conjuring on eye-open is human-paced (one
  call per click); the revision cache makes idle re-opens free. The Stage-2 topic-beat trigger will carry
  the real guards (topic-boundary gate, debounce, min-interval rate limit, supersession) — this increment
  proves the render path against real pixels first, using a trigger that *can't* run away.
- **Image lives INSIDE the orb, not below it** — **because** the whole metaphor is "the HAL eye dilates
  into a crystal ball." An `ImageBrush` on the cornea-sized ellipse makes the eye literally show the
  scene, with the glass gloss on top; a framed image underneath broke the illusion (user feedback).
- **Caption = `VisualConcept.Theme`, not `.Concept`** — **because** `Theme` is "the one master feeling the
  passage is about" (conversation-relative), while `Concept` describes the *picture*. The caption should
  anchor the *talk*, not narrate the image (user feedback: the image-description caption felt unrelated).
- **`medium` quality** — **because** the crystal ball is ambient, not a print poster; `medium` is
  meaningfully faster than the default `high`/`auto` (dropped multi-minute → ~30–40 s). `low` remains a
  one-word change if more speed is wanted.
- **`Hark.App` doesn't consume the image deployment until now, on purpose** — the config field
  (`HARK_AOAI_IMAGE_DEPLOYMENT`) shipped in EP16's installer/config work ahead of the code that reads it,
  so the wiring here just turns it on.

## ✅ Verification
- Builds green (0 errors) across `Hark.Oracle` and `Hark.App`.
- **Proven live, twice:**
  - A **Johnny Carson interview clip** (guests recounting being starstruck meeting Clark Gable / Cary
    Grant) → a fragile porcelain-doll hand reaching toward a dim vintage spotlight — *apt* for nervous
    admiration; the image resolved inside the orb with the theme beneath.
  - A **self-referential test** (narrating the act of testing the Vision feature) → an *eyeball* image —
    correct art-director behavior (a conversation *about an AI eye* yields an eye), not a misfire. When
    concrete content followed (a rubber-duck-and-paper-boat story), the render **drew the duck and the
    boat** — confirming it tracks content. Render time ~30–40 s at `medium` (down from "several minutes").

## 🚧 Problems & resolutions
- **Symptom:** SDK `GeneratedImageQuality.Medium` didn't compile (only `Standard`/`High` predefined). →
  **Root cause:** it's an extensible value type. → **Fix:** `new GeneratedImageQuality("medium")` (verified
  the string ctor via reflection).
- **Symptom:** first cut rendered the image in a frame *below* the eye, and captioned it with the image
  description. → **Fix:** moved the image into the orb ellipse and switched the caption to `Theme`.
- **Non-issue clarified:** the "weird eyeball" image was the pipeline working — a conversation about the
  eye feature produced an eye. A normal conversation (the Carson clip) reads well.

## 🔓 Open threads
- **Concept legibility / caption polish** — the `ConceptDesigner` is deliberately "iconic, not literal"
  (Rizzo/Glebas grounding — the crown jewel). If ordinary-conversation renders still feel too abstract, a
  *gentle* prompt nudge toward legibility is possible; touch it carefully.
- **Stage-2 autonomous trigger** — the debounced topic-beat detector that fires conjures on genuine theme
  shifts (with min-interval rate limit + supersession), the piece that makes the crystal ball *live*.
- **Conditioning-morph** — generate each frame *from* the previous (edits endpoint) for a slow dissolve.
- **In-Vision affordances** — chrome (close/mic) sits behind the opaque canvas while open; consider
  Esc-to-return / a minimal in-page control. Optional: orb size / `low` quality if more speed wanted.
- Carried: the engine boundary, diarization Fork A, and the standing threads in `STORYLINE.md`.
