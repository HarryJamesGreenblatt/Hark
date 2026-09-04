# 🎬 Episode 43 — Hackathon Polish: Animated Thumbnails & Per-Source Eye Reactivity (2.1.0 re-cut)

> **Date:** 2026-09-03 · **Branch:** `main` · **Commits:** `c21ed01` (thumbnails) · `a66a0aa` (reactivity)
> · tag **`v2.1.0`** (re-cut) · **One-liner:** With HARK 2.1.0 cut, prepped a **Microsoft Hackathon** entry — an
> animated **Oracle-eye thumbnail** that reads its own name into being — and fixed a live-test issue where a
> **headset-only** user barely lit the eye, by giving the mic and system audio **independent reactivity paths**.

## 🎯 Intent
Enter HARK into the internal Microsoft Hackathon, where an **animated thumbnail** is a difference-maker in a
big project gallery. Then, during a cross-machine live test: *"when only on headset without system audio the
sound-reactiveness is dampened and doesn't fire consistently nor is it powerful enough to emit the standard
glow."*

## 🛠️ What changed
- **Animated thumbnails (`c21ed01`)** — `assets/thumbnail/`. Reproducible **Python (Pillow/numpy)** generators
  render seamless looping GIFs faithful to the app's eye (silver frame → socket → breathing cornea + HAL-core
  parallax):
  - `oracle-brand.gif` — the hero tile: the Oracle **reads its own name into being**, each word performing its
    meaning — **Hear** (fixate) · **Adapt** (a three-point gaze *scan*, "sorting who's speaking") · **Render**
    (a vision renders into the pupil) · **Keep** (a **shutter-blink** that reads as a camera snapshot) — with
    **H·A·R·K** acronym emphasis (bright caps, dimmed lowercase). Seamless 9 s loop, ~2 MB (well under the 5 MB
    tile cap).
  - `oracle-eye.gif` — a pure breathing-eye fallback.
- **Per-source eye reactivity (`a66a0aa`)** — `AudioFeatures` now carries **system** *and* **mic** bands
  separately; `HarkSession` measures each on its own windowed-RMS path (mic from the **pre-mix** samples, so
  the input is untouched); `OverlayWindow.SetAudioFeatures` drives the eye from **`max()`** of two
  independently-tuned reactivity gains — the mic path **~2.4× hotter** with its own noise-floor gate.
- **Hackathon submission collateral** — `context/hackathon-entry.md`: a judge-facing **product description**
  (problem → the four movements → the Oracle → Microsoft-native stack → try it / what's next), distinct from
  the developer README. Plus the pieces the Hackbox form asks for: a gallery **tagline** — *“Hear. Adapt.
  Render. Keep.” — captions with a mind (and an eye) of their own*; a **≤200-char problem/opportunity** line —
  *“PCs hear every meeting, call, and video—then forget them. HARK turns any audio your PC hears into
  speaker-aware, summarized, visualized, exportable knowledge.”*; and **keywords** — Speech-to-text · Speaker
  diarization · Azure AI Speech · Microsoft Foundry · Generative AI · Accessibility.

## 🧠 Decisions
- **GIF thumbnail, ≤ 5 MB, ~3:2 — because** research (Devpost's documented spec + general gallery behaviour)
  showed an animated GIF is the mechanism that tiles autoplay; the risk is a platform re-encoding it to a
  static frame, so **verify with a test upload** (Hackbox is internal/unverifiable from here).
- **Perform "Adapt" with the gaze, not pills — because** a ring of mind-map pills at tile size is unreadable
  (just dots) and fights the wordmark; a quick gaze-scan performs "adapt" (attention/diarization) with zero
  clutter, on the single focal object. Each word now performs its meaning: Hear=pulse, Adapt=scan,
  Render=vision, Keep=blink.
- **Fix reactivity by the mapping, not the input — because** boosting mic *input* risks noise/feedback; the
  user's steer was to raise the **coefficient of reactivity** per source. Independent paths + `max()` keep the
  system-audio feel unchanged while lighting the eye for a headset-only user. (Validated live: headset-only
  reacts; HARK mixed with a Bing-AI Navy TTS narration kept "separate designated reactivity.")
- **Re-cut `v2.1.0` in place (delete the tag + release, recreate at the fixed HEAD) — because** 2.1.0 was
  only just published with no downloads, and the user wants 2.1.0 itself to be the *good* build (carrying the
  headset fix) rather than a stale buggy release plus a separate patch. (Normally you don't move a published
  tag; here the release is minutes old and unshared, so a clean delete-then-recreate is fine.)
- **The gallery tagline complements the thumbnail, doesn't repeat it — because** the animated tile already
  spells *Hear·Adapt·Render·Keep*; a tagline that restates the acronym wastes the slot. The tagline adds the
  meaning the acronym only hints at (*a mind and an eye of their own*), so the words and the watching-eye GIF
  reinforce each other rather than duplicating.

## ✅ Verification
- `dotnet build` (App + CLI) clean; `dotnet test` → **4 passed**.
- Thumbnails previewed frame-by-frame (peak/trough, scan phases) and tuned to ~2 MB seamless loops.
- Live cross-machine test confirmed the headset-only eye now lights + emits the standard glow, and mixes
  cleanly with other audio sources (HARK + a Bing-AI Navy TTS narration, each keeping its own reactivity).
- `v2.1.0` deleted (tag + release) and re-cut at the fixed HEAD → the tag-driven `release.yml` rebuilds the
  signed MSIX/installer and re-publishes.

## 🔓 Open threads
- **Submit the Hackbox entry** — copy in `context/hackathon-entry.md` (description · tagline · problem/opportunity · keywords); pair it with `oracle-brand.gif` as the tile.
- **Test-upload `oracle-brand.gif` to Hackbox** to confirm the tile keeps animating (fallback: pure-eye GIF or
  the video slot).
- Mic reactivity gains (`26/48/54`) / floors (`.012/.008/.005`) are easy dials if a different mic needs nudging.
- Installer startup delay still deferred past 2.1 (EP41).
