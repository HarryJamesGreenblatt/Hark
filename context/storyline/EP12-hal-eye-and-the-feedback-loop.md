# 🎬 Episode 12 — The HAL Eye & the Feedback Loop

> **Date:** 2026-08-22 · **Branch:** `main`
> **One-liner:** With mic mixing shipped, HARK started **captioning and summarizing its own
> development session** — and its recaps became the bug reports: the HAL eye ("washed out / very dim /
> then supercharged / doesn't sustain") was tuned across several rounds from the app's own dictated
> feedback, and a recap follow-up task ("add a copy button") shipped a view-aware clipboard copy so we
> could stop screenshotting. A genuine tools-improving-themselves loop.

## 🎯 Intent
Follow the mic work with "a few light touches," then fix the eye. The trigger was novel: once the
mic was mixed in, HARK transcribed the spoken test narration and its **SUMMARY** minuted real design
critique of itself — "the light appeared washed out and not as responsive as expected," "although
activity was detected, the light remained very dim." We treated that recap as the bug report.

## 🛠️ What changed

**Light touches (desktop)**
- **Mic off by default.** `HARK_MIX_MIC` now defaults **off** (loopback-only, like native Live
  Captions); `1`/`true` or the overlay toggle opts in. On speakers the mic re-captures playback and
  doubles the transcript, so opt-in is the safe default; headset users flip one button.
- **Mic glyph.** The toggle used Segoe MDL2 `E7F5` (headphone — read as an "iPod" icon); swapped to
  the Microphone `E720`.

**HAL eye — kill the washout, then the dimness (`OverlayWindow`)**
- **De-washed the cornea:** the radial fill's salmon-pink core (`#FF7B6A`) → a tight warm-white core
  dropping fast into saturated red (`#FF1E10`), and the glass gloss went from 50 %-white over the
  whole disc to a small **top-only** highlight (`#59FFFFFF`, faded by 0.7). The red now reads as deep
  and glowing, not pale.
- **Noise-gate the level:** `SetAudioLevel` subtracts an RMS **noise floor** (0.02, rescaled above
  it) so true silence reads as 0 — killing the "constant baseline" glow that kept the eye lit at rest.
- **Full dynamic range:** dropped the active brightness floor (a mid-pass overcorrection to 0.60 that
  read as "supercharged, never dark") to **0.28**, widened the span (cornea 0.28→1.0, glow 0→0.90,
  bigger scale pulse), so loud speech clearly spikes and cools back to a dim ember.
- **Envelope follower with sustain:** the final ADSR is a **fast attack (0.025 s), slow release
  (0.38 s)** so a speech peak snaps the eye up and it *holds through the dips between syllables and
  resonates briefly after* before cooling — fixing "cools off too quickly." The level boost went
  `4.5→…→11×` so **normal conversational speech** (not just sustained shouts) reaches full brightness.

**Copy what's shown (`OverlayWindow`)**
- A recap follow-up task literally asked for a copy button (we were screenshotting recaps to feed them
  back). Added a **copy button in the header** (Segoe MDL2 `E8C8`, ✓ flash on success) that copies
  whatever the window currently shows, keyed off the toggles: CAPTIONS → the latest line (LATEST) or
  the full transcript (TRANSCRIPT); SUMMARY → the active recap serialized to **markdown** with its
  nested topic details / speaker points (`## Meeting Notes`, `## Follow-up Tasks`, `## Speakers`),
  regardless of which cards are expanded.

## 🧠 Decisions
- **Treat HARK's own recap as the spec** — **because** it's a faithful, structured transcription of
  the design critique spoken during testing; the feedback loop (app captions dev session → recap →
  fix) is exactly the "tool that improves itself" premise, and the notes were precise enough to act on
  verbatim.
- **Noise-gate before mapping, don't just raise a floor** — **because** the first fix raised the
  brightness floor to 0.60, which HARK then flagged as "supercharged constantly, never fully dark,
  minimal reaction." The real cause was an RMS **noise floor** keeping the target lit; gating it (not
  clamping brightness) restores full range **and** a genuine dark rest.
- **Saturation + confined gloss beats brightness tuning for "washed out"** — **because** the washout
  was a *color/coverage* problem (pink core + full-disc white gloss), not a luminance one; deepening
  the red and shrinking the highlight fixed it structurally, and no amount of opacity tuning would
  have.

## 🚧 Problems & resolutions
- **Symptom:** eye "washed out, very dim even when active" (dictated into HARK's recap) → **Fix:**
  saturate cornea + confine gloss (washout) and strengthen the active mapping (dimness).
- **Symptom (self-caught):** first eye pass overcorrected — HARK's next recap read "supercharged
  constantly… never fully dark… spoke loudly but minimal reaction… incomplete range." → **Fix:**
  noise-gate the RMS floor + drop the brightness floor to 0.28 and reopen the full range; snappier
  attack/release so it cools between words.
- **Symptom (next recap):** "reaches full only on sustained sounds like 'FA' held for seconds… cools
  off too quickly… add a sustain/release phase to let it resonate" (the user framed it as ADSR). →
  **Fix:** raise the boost to 11× (normal speech reaches full) and lengthen release to 0.38 s
  (sustain/resonate), keeping the 0.025 s attack. The subsequent recap: "improved reflexive
  responsiveness… not too bad."

## 🔮 Next / open threads
- **Diarization over-segmentation (surfaced by the app itself):** HARK's recap noted that "after
  speaking continuously, the system conveyed the presence of three guests instead of one," and that it
  "also occurred during loopback content when not in microphone mode." The live
  `ConversationTranscriber` over-splits a single continuous speaker into several `Guest-N`. The Stop
  second pass (`FastTranscriptionRefiner`) helps globally, but the **live** path needs work — a
  minimum-turn-duration / merge heuristic, a lower `maxSpeakers` hint, or the engine-boundary
  `RefinementEvent` re-labeling live history. This is the next natural target.
- **HAL eye — optional polish:** an idle "breathing" shimmer while running-but-silent; fine-tune the
  gate floor per device if room noise varies.
