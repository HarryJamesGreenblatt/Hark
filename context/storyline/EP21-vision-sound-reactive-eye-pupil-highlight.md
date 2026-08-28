# 🎬 Episode 21 — The Eye Comes Alive: Banded Audio, an Organic Pupil & a Drifting Highlight

> **Date:** 2026-08-28 · **Branch:** `main` · **Commit:** `19f8825`
> **One-liner:** Made the Vision eye genuinely **sound-reactive across several audio dimensions** — split
> the capture RMS into **bass/treble bands** (a cheap one-pole low-pass, no FFT) and drove the image
> **"pupil" dilation** from a slow **bass capacitor + under-damped spring** (an organic, inertial swell,
> not a peak-locked snap) and the glass **highlight** from a treble-widened **Lissajous drift** — plus a
> **bigger eye**, a **thinner silver ring**, and dropping the off-topic abstract `Theme` caption. Tuned
> across **three live self-tests** into "the animations are looking good."

## 🎯 Intent
User wanted design polish on the Vision feature, in their words: the image **pupil** should "subtly
dilate… range out to just about the full diameter of the iris"; the **reflective highlight** is static
and should "have some motion to simulate small angle changes in the light"; and — crucially — *"rather
than randomizing keyframes on movements… look at the gh repo **WavBall** to see how it manages WASAPI
and enables using **different dimensions of audio analysis to control different parameters**"* so the
animation is **sound-reactive, driven from the capture**. Also: the eye "might need to be a little
bigger." Follow-up tests then dictated three tuning rounds (below).

## 🛠️ What changed
- **Banded audio (`Hark.Core/Audio/AudioFeatures.cs` (new) · `Hark.Core/HarkSession.cs`)** — a new
  `AudioFeatures(Level, Bass, Treble)` record and an `AudioFeatures` event raised from the **same ~20 Hz
  windowed-RMS** loop as the existing `AudioLevel`. `ReportAudioLevel` now runs a **one-pole low-pass**
  (`_lp += α·(sample−_lp)`, α≈0.12 ≈ 330 Hz at 16 kHz) so `bass = _lp` and `treble = sample − _lp`,
  accumulating a sum-of-squares per band → a windowed RMS for each. No FFT, no new NuGet.
- **Wiring (`Hark.App/App.xaml.cs`)** — subscribe to `AudioFeatures` (was `AudioLevel`), forwarding all
  three bands to `OverlayWindow.SetAudioFeatures`. Also **dropped the `Theme` sub-caption** (pass no
  caption to `SetVisionImage`) and removed the now write-only `_shownVisionTheme` field.
- **Eye XAML (`Hark.App/OverlayWindow.xaml`)** — the big Vision eye grew **300 → 360 px** with every
  absolute inner margin scaled ×1.2 (socket 33→**40**… then **→26** to thin the ring, cornea/orb 65→**78**,
  gloss 82,49,134,164→**98,59,161,197**). The `VisionOrb` gained a `ScaleTransform` (**pupil dilation**)
  and the gloss ellipse a `TranslateTransform` (**highlight drift**).
- **Reactivity (`Hark.App/OverlayWindow.xaml.cs`)** — a `Shape(level, gain, floor)` helper (gate + gain +
  √) reused per band; `SetAudioFeatures` sets three targets. The render loop eases **bass** (slow) and
  **treble** (fast) with their own time constants. `ApplyPupilAndHighlight`: a slow **capacitor**
  charges/bleeds on low-end, an **under-damped spring** chases it (momentum → gradual, overshooting
  dilation), and the highlight is a slow **Lissajous** whose amplitude follows treble through its own
  slow follower.
- **Render dead-time buffer (follow-up, same session) (`Hark.Oracle/Vision/VisionService.cs` ·
  `Hark.App/OverlayWindow.xaml(.cs)` · `App.xaml.cs`)** — `ConjureAsync` gained an `onConcept` callback
  that fires the moment the **fast** concept lands, **before** the ~1 min render. `App` surfaces it
  immediately as an on-topic buffer caption (`Concept.Concept` — the concrete cousin of the dropped
  `Theme`), and a **scrying sheen** (a rotating, softly-pulsing glint over the cornea) marks the wait so
  the eye reads as *actively conjuring* rather than a frozen red disc. Shown only on a first open (no
  image up); autonomous beats still **hold the previous image** until the new one lands (no scry churn).

## 🧠 Decisions
- **A few bands → orthogonal visual axes (the WavBall pattern), not one RMS driving everything** —
  **because** a single loudness signal can't distinguish facets of the sound. Broadband → cornea
  brightness/pulse, **bass → pupil dilation**, **treble → highlight shimmer**, so the layers throb on
  independent schedules — exactly how WavBall's `Reactivity.Bar` derives `Energy`/`BassFlux`/`SnareFlux`
  and assigns each to a different behavior.
- **One-pole low-pass, not an FFT** — **because** 2–3 bands at speech rates don't need NWaves' mel-FFT.
  WavBall runs a 2048-point `RealFft` + mel filterbank because it draws **64 spectrum bars**; the eye
  needs bass-vs-treble, which a two-line IIR delivers dependency-free. Right tool per resolution.
- **The pupil is a capacitor + spring, deliberately NOT a direct band map** — **because** the first cut
  mapped `scale = 0.80 + 0.18·bassEnvelope` and the user's retest called it "too fast… swells up and
  shrinks down very quickly… should be **influenced, not total full control**… grow/shrink slowly and
  **change directions**." A slow asymmetric capacitor (charge on activity, stay *sated*, bleed off slow)
  feeding an under-damped spring gives inertial, overshooting motion — **WavBall's autonomous-goal idea**
  (the "Navi-fairy" that charges around peaks, sates, and eases away), applied to a UI parameter. The
  user pointed us at that relationship explicitly as a better source than "randomness or arbitrary
  guesses."
- **Drop the `Theme` sub-caption rather than fix it** — **because** `VisualConcept.Theme` is a
  deliberately abstract *master feeling* ("Fluid responsiveness and gradual change"), which the user read
  as "weird / off-topic." There is **no literal-topic field** in the concept by design (metaphor-not-
  literal), so inventing a topic summarizer would fight the whole grounding; removing it was the honest
  fix. (Its concrete cousin `Concept.Concept` is the better buffer text — see Open threads.)

## 🚧 Problems & resolutions
- **Symptom:** pupil "swells up and shrinks down very quickly," too peak-sensitive. → **Root cause:** a
  direct `bassEnvelope → scale` map, even with attack/release easing, tracks every dip. → **Fix:** slow
  asymmetric capacitor (`τ_up` 0.6 s / `τ_down` 1.8 s) + under-damped spring (`k=12`, ω≈3.5 rad/s;
  `c=3.2`, ζ≈0.46) integrated explicit-Euler; clamp the rails and zero the outward velocity there.
- **Symptom:** highlight "a little aggressive… jerky, a little less natural." → **Root cause:** the drift
  **amplitude** was modulated straight off the snappy treble envelope (0.02 s attack), so it darted on
  every transient. → **Fix:** ease a `_glossAmp` toward `3 + 6·treble` with τ≈0.5 s and slow the Lissajous
  frequencies (0.73/0.51 → 0.61/0.43 rad/s); linear travel = amp × angular-freq, so lowering both calms it.
- **Symptom:** pupil "tends to constantly be smaller… the upper limit of growth could expand." → **Root
  cause:** the capacitor asymptotes to the **bass-envelope ceiling**, which is modest for speech, so the
  reach — not the cap — was the limit. → **Fix:** raise the bass shaping gain (16→22) **and** the
  charge→size gain (0.17→0.22, base 0.80→0.82) **and** open the upper rail (0.99→**1.0**, pupil == full
  iris at peak).
- **Symptom:** `CS0136` — a loop-local `treble` clashed with the band field. → **Fix:** renamed the
  loop-local to `highpass`.

## ✅ Verification
- Builds green across `Hark.Core` + `Hark.App` (`19f8825`); pre-existing warnings only (two CS4014 on the
  fire-and-forget conjure calls, the app.manifest DPI note).
- **Proven across three live self-tests**, HARK captioning its own review: (1) reading an Arizona Green
  Tea nutrition label — pupil renders in the orb, highlight shifts, "it does seem to be audio reactive";
  (2) reading Norman's *The Design of Everyday Things* — "the pupil is a little bit more organic… growing
  and shrinking in an organic way"; (3) a public-safety bit — "the animations are looking good… pretty
  handy." Each round's critique fed the next tuning pass above.
- Committed + pushed to `origin/main` (`804cb96..19f8825`).

## 🔓 Open threads
- **Render dead-time buffer — shipped (this session):** the fast concept now surfaces immediately as an
  on-topic caption + a scrying sheen fills the ~1 min wait (see What changed). Remaining polish: consider
  keeping the concept visible *under* the landed image, and a `low`-quality / faster render tier.
- **Image quality / relevance (deferred next phase):** live renders were "absurd and off topic" (a chair
  reading a book, someone holding pottery) — a **concept + prompt-composition** issue in the Oracle Vision
  tier (`ConceptDesigner` → `VisionPromptComposer`), independent of the eye animation.
- **Dead API:** `OverlayWindow.SetAudioLevel` (and the `HarkSession.AudioLevel` event) are now unused by
  the app after the switch to `AudioFeatures` — keep for CLI/compat or prune later.
