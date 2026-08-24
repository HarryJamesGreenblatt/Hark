# 🎬 Episode 16 — The Eye Dilates: HAL-Eye Vision Mode (UX Shell) + the Zoom That Fought Back

> **Date:** 2026-08-23 · **Branch:** `main`
> **One-liner:** Built the **UX shell** of the north-star Vision mode — clicking the bar's HAL eye
> now dilates into a full-window "crystal ball" page via a cinematic **corner→centre match-cut zoom**
> — and, along the way, fixed a genuinely invalid solution file and hunted down a WPF animation bug
> through three wrong theories before finding the real (measurement-order) cause.

## 🎯 Intent
Two things, in order. First, the user reported the **solution "appears to be invalid."** Second — the
main thrust — the user noticed that clicking the HAL eye did **not** transform it into a centred,
scaled page "as previously conceived." That framing was a red herring worth correcting: the HAL-eye
Vision mode was **designed in EP15 but never implemented** (no click handler; `Hark.App` doesn't even
reference `Hark.Oracle`). With scope confirmed as the **UX shell only** (buildable now; the render tier
still needs a `gpt-image-1` deployment), the job was: click the eye → it becomes a large, centred eye on
its own dark page; click again to return. Then the user asked to make the transition **cinematic** — not
a fade+pop, but "components fade out, then we zoom *into* the eye and the perspective shifts to the
centred open eye."

## 🛠️ What changed
- `Hark.slnx` — removed **duplicate** `<Project>` entries (`Hark.Oracle` and `Hark.Oracle.Spike` were
  each listed twice), which is what made the solution invalid. Builds green after.
- `Hark.App/OverlayWindow.xaml` — wrapped the docked bar in a `RootLayer` grid and added a full-window
  **`VisionCanvas`** overlay (dark backdrop + a large HAL-eye replica with a `TransformGroup` of a
  `ScaleTransform` **`VisionEyeScale`** + `TranslateTransform` **`VisionEyeTranslate`**, plus `VISION`
  placeholder text where the concept image will render). Made the bar's small `HalEye` a real hit target
  (`Background="Transparent"`, hand cursor, "Open Vision" tooltip).
- `Hark.App/OverlayWindow.xaml.cs` — the Vision-mode logic:
  - Fields `_visionOpen`, `_visionAnimating`, and the measured start pose `_visionStartX/Y/Scale`.
  - `OnHalEyeReleased` opens on the small eye's **mouse-up** (press only `e.Handled = true` to suppress
    the drag-handle move); the big eye's mouse-up closes. Both gated by `_visionAnimating`.
  - `OpenVision` → a **two-beat** transition: beat 1 fades the bar chrome to darkness (the eye stays,
    being the matched large one parked tiny over the small eye); `ZoomVisionEyeToCentre` (beat 2, chained
    on the fade's `Completed`) flies the eye corner→centre and scales it up (`CubicEase`, EaseInOut).
  - `CloseVision` reverses it and restores the docked-bar height.
  - `ApplyEye` now also drives the large eye (same audio envelope) while Vision is open, so it's lit in
    listening mode throughout the zoom.
  - `AdjustHeightToContent` guards `_visionOpen` (Vision fills the working area; queued re-fits mustn't
    shrink it).

## 🧠 Decisions
- **Scope = UX shell, not the full render** — **because** the render tier (`VisionRenderer`) needs a
  `gpt-image-1` deployment that doesn't exist yet (EP15 open thread). The shell — the eye→page transform
  and the reactive large eye — is the whole *interaction*, buildable and demoable now; pixels drop in
  later without reworking the shell. Deliberately did **not** wire `Hark.Oracle` into `Hark.App` this pass.
- **Correct the "regression" framing** — **because** it was never built. Told the user plainly that EP15
  only *conceived* it, so this is net-new work, not a bug — keeps the storyline honest.
- **Match-cut over fade+pop** — **because** the user wanted the eye to *travel* and the "camera" to push
  in. A single large eye pre-matched onto the small eye (same size/place) means the eye never disappears;
  the chrome dissolves around it, then it zooms — reading as one continuous move, not two effects.
- **Open on mouse-up, not mouse-down** — **because** opening on down placed the big eye under the cursor
  mid-click, so the same gesture's mouse-up hit the big eye and closed it (see Problems). Symmetric
  up-to-open / up-to-close plus an `_visionAnimating` guard makes toggles deterministic.

## 🚧 Problems & resolutions
The zoom worked intermittently, and the debugging took **three wrong theories** before the real cause —
a good lesson in not stopping at the first plausible fix:
- **Symptom (round 1):** "works every ~3rd time." → **Root cause:** open fired on **mouse-down**; the
  just-revealed big eye sat under the cursor, so the *same click's* mouse-up landed on it and closed
  Vision — racy on hit-test timing. → **Fix:** open on the small eye's **mouse-up**; add `_visionAnimating`
  to reject overlapping clicks.
- **Symptom (round 2):** now "every *other* toggle it just appears in the centre." → **Wrong theory:**
  blamed WPF `HoldEnd` clocks overriding the local "park tiny" values; added `BeginAnimation(prop, null)`
  clock-clears and chained beat 2 on beat 1 instead of a `BeginTime` delay. Helped robustness but the
  alternation persisted.
- **Symptom (round 3):** "it travels the first time, then next time **teleports to centre and scales in
  place**." → **Real root cause:** `TransformToVisual` **includes the render transform**. I measured the
  big eye's centre *before* clearing its transform, so on alternate opens it was measured while still
  parked at the corner from the prior cycle → `_visionStartX ≈ 0` → zero travel. → **Fix:** **reset the
  eye to identity (scale 1 / translate 0) BEFORE measuring**, then compute the corner→centre offset, then
  park it. Deterministic every cycle. (Lesson: an every-*other*-time bug is almost always leftover state,
  and "measure geometry" must be done against a known/identity transform.)
- **Symptom:** `error CS0104: 'Point' is ambiguous` — WinForms is enabled for the tray icon. → **Fix:**
  qualified `System.Windows.Point`.

## ✅ Verification
- `Hark.slnx` builds green (0 errors) after de-duplication; `Hark.App` compiles clean (only the
  pre-existing `CS4014`/DPI-manifest warnings). Compile validated via the language service while the app
  held the output DLL locked (a running instance).
- The user confirmed live: after the measurement-order fix, **"the animation is now consistent"** — the
  eye travels corner→centre as it scales, and back, on every toggle.

## 🔓 Open threads
- **Render tier still dark** — the Vision page shows a placeholder; wiring real pixels needs the
  `gpt-image-1` deployment (EP15's standing infra step) + a `Hark.Oracle` reference from `Hark.App` and a
  `VisionService` call on open. That's the "Full Vision mode" branch deferred this pass.
- **Live concept feed** — once rendering, drive the page from the transcript window via
  `Hark.Oracle.Vision` (Concept → Compose → Render), superseding per beat (Stage 2 beat-intelligence).
- **In-Vision affordances** — close/mic/settings are behind the opaque canvas while open; only the eye
  returns. Consider a minimal in-page control or an Esc-to-return.
- Carried: the engine boundary, diarization Fork A / fidelity, and the standing threads in `STORYLINE.md`.
