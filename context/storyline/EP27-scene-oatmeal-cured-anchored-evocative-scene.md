# 🎬 Episode 27 — Cinnamon in the Oatmeal: the Anchored Evocative Scene

> **Date:** 2026-08-30 · **Branch:** `main` · **Commits:** `448e426..e26aea7`
> **One-liner:** Closed EP26's headline open thread — freed the photographic **scene** from literal
> "oatmeal", over-corrected straight into **drift**, then landed the **anchored-but-varied** middle;
> plus a synced pupil blink/crossfade and a pupil-sized FLUX render.

## 🎯 Intent
Carried directly from EP26's headline thread: the native **diagram** tracks each beat, but the
photographic **pupil scene** still hit the EP18/EP22 single-topic ceiling (a briefing about CENTCOM →
"commander at a map" every beat). User: *"lets put some cinnamon and sugar in that bland-ass oatmeal."*

## 🛠️ What changed
- **Freed the scene, then re-anchored it (`448e426`)** — `Hark.Oracle/Vision/ConceptDesigner.cs`: the
  EP22 *"DEFAULT TO THE LITERAL"* system-prompt steer was first inverted to a pure mood/metaphor steer,
  which **over-corrected into drift** (a lighthouse, a lone wanderer — unrelated to the talk). It was
  then re-grounded to the **middle**: *"the diagram labels the structure; you open a WINDOW onto the
  ACTUAL place/moment/subject of THIS beat"* — anchored to the specific operation/place/era/person,
  rendered cinematically, forbidding **both** generic-repeat and unrelated-metaphor. Temperature 0.7 → 0.8.
- **Softened-prompt RAI retry (`448e426`)** — `VisionPromptComposer.ComposeSoftened` (abstract mood/
  palette/aesthetic, drops literal motifs) + `VisionService.ConjureAsync` catches a content-safety
  refusal (`IsContentSafetyRefusal`) and retries **once** softened before losing the beat.
- **Pupil filler-buffer dedupe (`448e426`)** — `OverlayWindow.AddToPupilBuffer` skips scenes within an
  8×8 average-hash Hamming distance of an already-buffered frame, so the RAI-stall filler cycle never
  re-shows a near-identical picture (which reinforced the oatmeal).
- **Synced blink/crossfade (`9d81415`)** — the pupil transition's crossfade fired the instant the lid
  closed while the up-swipe was fast (170 ms), so it read as an abrupt pop on the *down*-stroke. Re-timed:
  down 150 ms, **up 340 ms**, fade **420 ms started with the up-stroke** so the image resolves as the lid
  clears.
- **Pupil-sized FLUX render (`e26aea7`)** — `VisionRenderer.FluxRenderSize = 512` (was 1024²) since the
  scene only fills the ~200 px pupil (~5× oversample was wasted latency/cost); gpt-image stays 1024 (its
  floor). Filler buffer widened 5 → 16 now that frames are smaller.

## 🧠 Decisions
- **Anchored-but-varied, not pure-mood** — **because** on a single-topic source the variety is already
  in the **beats** (Desert Storm ≠ Afghanistan ≠ MacDill HQ). Anchor the scene to each beat's specific
  subject and the variety falls out; inventing metaphors for variety just yields drift.
- **Render the scene at pupil size (512²)** — **because** the FLUX scene only fills the small pupil;
  generating 1024² and scaling down was pure waste. Latency win is certain; cost win likely (MP-priced).
- **Dedupe the filler buffer** — **because** cycling near-identical frames during an RAI stall actively
  reinforced the repetition the whole episode was fighting.

## 🚧 Problems & resolutions
- **Symptom:** every scene the same literal subject (oatmeal). → **Root cause:** EP22 literal-bias, now
  redundant since the diagram carries the literal payload. → **Fix:** free the scene to be evocative.
- **Symptom:** freed scene drifted to unrelated imagery (lighthouse, wanderer). → **Root cause:** "follow
  the mood NOT the noun" severed the topic anchor. → **Fix:** re-anchor to the beat's specific subject,
  cinematic treatment; forbid both failure modes explicitly.
- **Symptom:** pupil image "pops" too fast / seems tied to the blink's down-stroke. → **Root cause:** the
  fade started behind the fully-closed lid and a fast up-swipe revealed an already-half-faded image. →
  **Fix:** slow the up-swipe and lock the fade to span it.

## ✅ Verification
User tested across several sources (Cobain/Fonda/**CENTCOM**) and confirmed the scene now tracks each
beat's specific subject (aligned) and varies as beats change (no more lighthouse/wanderer drift), and the
transitions read smoothly. Builds green; pushed `448e426`, `9d81415`, `e26aea7`.

## 🔓 Open threads
- The FLUX **negative clause** in `VisionPromptComposer.Compose` is still a gpt-image-ism (memory flags
  it counterproductive on FLUX — it can't negate) — a separate composer realignment.
- The **temporary diagnostic toast** in `App.ShowSceneAsync` is still present — remove once the scene
  work is fully settled.
