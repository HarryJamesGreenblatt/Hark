# 🎬 Episode 42 — The Oracle Learns to Look: Grounded Eye-Gaze & Cutting 2.1.0

> **Date:** 2026-09-03 · **Branch:** `main` · **Commits:** `23f727d..a9ef78a` (+ tag `v2.1.0`)
> **One-liner:** Gave the Vision eye **organic, research-grounded gaze** — a 2D iris-offset saccade/fixation
> model (Eyes Alive + main-sequence min-jerk saccades + 1/f jitter) with a dual audio-reactive / pill-hover
> mode and an intra-pupil HAL-core parallax — closing the last 2.1.0 item and cutting **HARK 2.1.0**.

## 🎯 Intent
Take on the final 2.1.0 backlog item (#2): *"give the Vision eye organic movement — moves like an eyeball."*
The user was (rightly) wary that this is a hard problem in **2D** normally solved inside a game engine's eye
rig, and pushed back hard when the first attempt jumped to code on assumptions: *"didn't really see any
research happening there… we're grounded on assumptions for a pretty tough problem… lets back out and do the
research and try again."*

## 🛠️ What changed
- **Reverted the rushed first attempt** (spring-chases-cursor prototype) with `git restore` — it was built on
  assumptions, not the literature.
- **Research pass** (browser/DDG + `fetch_webpage`; no web-search MCP in this workspace) → new artifact
  `context/research/eye-motion-gaze.md`. Extracted the actual **"Eyes Alive"** (Lee & Badler, SIGGRAPH 2002)
  statistical model from the paper (via `pypdf`; the copyrighted PDF was reviewed then deleted from the repo).
- **`Hark.App/OverlayWindow.xaml`** — wrapped the iris assembly (cornea + `VisionOrb` pupil + scrying sheen) in
  an `EyeGazeGroup` with a `GazeTranslate` (+ an identity `GazeForeshorten` hook); named the big cornea's
  radial brush `OracleCorneaGradientBig`. The silver frame + socket stay fixed; the gloss is left **outside**
  the group (structural parallax).
- **`Hark.App/OverlayWindow.xaml.cs`** — `UpdateGaze(dt)` on the compositor loop (gated on `_visionOpen`):
  min-jerk saccades whose duration follows the main sequence `T=α+βA`, Eyes-Alive magnitude/direction
  sampling, ~10% undershoot + corrective secondary, and 1/f (pink-noise) fixation jitter. Two modes:
  **pill-hover** → quick-but-smooth cursor pursuit; **ambient** → sound-reactive (audio-gated glances +
  level-scaled jitter), no autonomous timer. Plus **intra-pupil parallax**: the bright HAL-like cornea core
  leads the disc (`pupilLead ≈ 0.75`) so the gaze reads as aimed from the pupil point.
- Commits: `23f727d` (base gaze impl + artifact) → `a9ef78a` (parallax + dual-mode/hover tuning + artifact §4.5).

## 🧠 Decisions
- **Research before code — because** the first attempt's spring model was flatly wrong per the literature, and
  the user explicitly asked for grounding. The artifact is the durable record so this isn't relitigated.
- **A saccade is a fixed-duration min-jerk move, not a spring — because** that's the main sequence (Bahill
  1975; Yeo "EyeCatch" 2012). Spring easing has amplitude-independent timing + overshoot ringing; real
  saccades are ballistic and amplitude-scaled.
- **Ambient is sound-reactive, not an autonomous conversational timer — because** the user found the
  Eyes-Alive mutual/away schedule read as "constant" motion; they wanted *"influenced enough to jitter around
  organically when stimulated,"* calm when quiet. Kept the saccade *mechanics*, changed the *trigger*.
- **Cursor follow only on mind-map pill hover, quick + gentle — because** always-following felt wrong; slow
  pursuit clashed with the lively jitter; a full-excursion look strained to the socket rim. Tuned to 0.18 s /
  gain 0.09 / ±22 px.
- **No socket clip — because** it hard-cut the cornea's glow bloom; the ≤30 px offset clamp already keeps the
  102 px cornea inside the 154 px socket. (Corrects the artifact's original clip recommendation.)
- **Intra-pupil parallax is a cue beyond the literature — because** the user's HAL-9000 read (the bright core
  *is* the pupil point) wanted the core to **lead** the disc; it does much of the "sphere" work foreshortening
  was meant to, so `GazeForeshorten` stayed identity.

## 🚧 Problems & resolutions
- **Symptom:** parallax invisible ("no semblance of parallax") → **Root cause:** `pupilLead = 0.30/204` shifted
  the soft glowing core only ~6 px → **Fix:** raised to `0.75/204` (a clearly perceptible lead).
- **Symptom:** WPF type ambiguity risk on `Point` (System.Drawing vs System.Windows) → **Fix:** fully-qualified
  `System.Windows.Point` (same family as the Size/FontFamily aliases already in this file).
- **Process:** the first code pass got reverted for skipping research — the durable lesson of the episode.

## ✅ Verification
- `dotnet build Hark.App` clean at every stage; `dotnet test Hark.Tests` → **4 passed**.
- Multiple **live looks** with user sign-off through the tuning loop, ending *"i tested it its looking pretty
  dang good."*
- **HARK 2.1.0 cut** — annotated `v2.1.0` tag pushed → the tag-driven `release.yml` builds + signs the MSIX and
  publishes `Hark-Setup`.

## 🔓 Open threads
- **HARK 2.1.0 backlog is now empty** except the deliberately deferred **installer startup delay** (past 2.1;
  Trusted Signing + ReadyToRun + drop single-file compression + splash — EP41).
- Optional gaze follow-ons (artifact §4.4): the unused `GazeForeshorten` sphere-squash hook; a **mids audio
  band** as an independent saccade-energy signal; blink coupling on large glances.
