# 🎬 Episode 31 — The Resting Eye: Null-Scene Handling & a Deferred State Machine

> **Date:** 2026-09-01 · **Branch:** `main` · **Commits:** `ea9316b`, `b6cab77`
> **One-liner:** Closed EP30's **null-scene gap** — the pupil now **cross-fades to the red sound-reactive
> glow** on a topic shift or a failed render and fades the new scene back in when ready; the idle filler
> split cleanly into **LIVE image-blink vs REVIEW topic-slideshow**; and a formal **state-machine refactor
> (Nystrom) was considered and deliberately deferred** because the informal handling now works.

## 🎯 Intent
Same-day continuation of EP30's field testing. FLUX renders fail **often** (`FLUX render returned 200 with
no image`, content refusals), so the crystal ball kept hitting empty/awkward gaps. The user's evolving
steers: the scrying spinner *"seems more synthetic"* → prefer the red reactive eye; the buffer-cycling was a
*"happy accident"* that must **not** drag the mind-map topic backward while live; the topic+image lock-step
belongs in **review**, not live; and — after repeatedly dancing away from it — *"I keep feeling like I need a
way to fill that space with a fallback image."*

## 🛠️ What changed
- **Dropped the scrying spinner; a null rests on the red eye (`ea9316b`)** — `BeginVisionConjuring` no
  longer sweeps the rotating sheen or a "Conjuring…" caption (it felt synthetic); the dead scrying helpers
  (`StartScrying`, `SetVisionConjuringConcept`, `_scrying`) were removed and the temp "Vision render failed"
  balloon deleted.
- **LIVE vs REVIEW filler split (`ea9316b`)** — `OnPupilFillerTick` now: **LIVE** cycles only the recent
  IMAGE buffer on a stall (the organic "blink through the buildup") and **keeps the current topic**; a live
  topic changes only when a genuinely new beat arrives. **REVIEW** (Live pill showing) auto-advances the
  whole timeline as a **synchronized topic + scene slideshow**. (Inverted from a first attempt that walked
  topics on a live idle-timer — a natural speech pause tripped it mid-live.)
- **~7 s review cadence + fill the null (`ea9316b`)** — finer 2 s tick with separate intervals
  (`ReviewSlideInterval` 7 s, `BlinkInterval` 5 s, shared `_lastFillerAdvanceUtc`); a first pass filled a
  null by holding the most-recent scene (`HandleMissingScene`).
- **Cross-fade to the red glow (`b6cab77`)** — superseded the hold-a-stale-scene fill: `FadePupilToEye()`
  cross-fades the scene out (550 ms EaseInOut) to the red sound-reactive cornea on a **topic shift**
  (`ShowDiagramAsync`) or a **null/failed** render, aligned with the diagram change; the next scene fades
  back in via `TransitionPupil` when it lands. Removed the now-orphaned `HandleMissingScene`.

## 🧠 Decisions
- **Fill a null with the red-glow cross-fade, not fetched imagery.** — **because** web images (Imgur/Giphy/
  search) **clash with the FLUX aesthetic** (an EP18/19 lesson) and are the wrong safety posture for exactly
  the sensitive beats that fail; and holding a stale scene mislabels the topic. The red reactive eye is
  on-aesthetic, filter-immune, and honest — "no vision right now" reads as the eye resting.
- **Live never walks topics; review is the slideshow.** — **because** the mind-map dragging backward mid-live
  was the core "herky-jerky" bug. `LivePill.Visibility` is the mode signal (a smell, noted below).
- **Defer the formal FSM (Nystrom State pattern).** — **because** after testing the cross-fade the user judged
  the informal handling *"works better and seems to kind of manage the states as intended without it being
  formally designed as FSM."* The design was mapped (two concurrent machines — **Topic** {LiveFollow ⇄
  ReviewSlideshow} + **Pupil** {Resting·Conjuring·Holding·Blinking}, enum-FSM not GoF objects, the cross-fade
  living in a `Conjuring` onEnter) but **not built** — revisit only if the informal state handling gets
  brittle again. (Nystrom's own "don't over-apply" caution.)

## 🚧 Problems & resolutions
- **Symptom:** on a null the topic "jumps back to the init topic… herky-jerky," and the topic+image lock-step
  only kicked in *after* a first trip through review. → **Root cause:** the idle-timer topic-recap fought live
  generation (a speech pause read as "idle"), and the review detour consumed the idle window. → **Fix:** make
  the topic-walk a **review-only** behavior; live does image-blink only.
- **Symptom:** the review slideshow felt like ~10 s. → **Root cause:** a coarse 5 s tick gating an 8 s
  interval lands advances at 10 s. → **Fix:** 2 s tick + explicit 7 s review / 5 s blink intervals.
- **Symptom:** frequent FLUX nulls leave the orb empty. → **Fix:** `FadePupilToEye()` — cross-fade to the red
  glow on topic-shift/null, scene fades back in when ready.

## ✅ Verification
Both commits build green and were field-tested live (the "US Military Involvement" / "US Army Overview"
sessions). User verdict on the cross-fade: *"works better."* The FSM was explicitly **not** built.

## 🔓 Open threads
- **Native on-topic pupil fill (optional upgrade).** The red-glow fill is on-aesthetic but not *topical*; a
  **native procedural** pupil fill (a soft fill tinted by the beat's own node colours, instant + filter-immune)
  would make the gap on-topic. Deferred — the red glow is good enough for now.
- **Formal FSM — a "maybe."** Mapped but not built; revisit only if the informal state handling regresses.
- **Multi-format export — Phases 2+ (from EP30).** HTML + Markdown ship; next **PDF** (WebView2 on the HTML),
  then **PPTX** (Open XML, beat-per-slide) + **DOCX**.
- Carried: the **fairy-tale content-filter** test (EP29) and the FLUX **negative clause** in
  `VisionPromptComposer.Compose` (EP27).
