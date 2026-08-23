# 🎬 Episode 11 — Microphone Mixing (Hear Yourself Too)

> **Date:** 2026-08-22 · **Branch:** `main`
> **One-liner:** HARK now also captures the **local microphone** and mixes it into the transcribed
> stream, so a headset user's own voice is captioned alongside the far side — closing the "with a
> headset, nothing I say is captured" gap. When the mic is on it **clocks** the mixed stream (a capture
> endpoint runs continuously; loopback goes silent with no playback) and loopback is queued and mixed
> in the float domain; a **headset toggle** in the overlay flips it live; on by default, `HARK_MIX_MIC=0`
> opts out.

## 🎯 Intent
Until now `Hear` was **loopback only** (system playback / the far side). On a headset the far side
plays into the headphones (captured by loopback) but the user's own voice exists only on the mic —
so it was never transcribed. The ask: add and mix mic input, "sort of like WavBall does."

WavBall (reviewed via `gh`) keeps mic + loopback **separate** and combines them at the band/RMS level
because it only *visualizes*. HARK feeds a single PCM stream to Azure Speech, so it needs a genuine
**audio mix** — summed samples, not summed spectra.

## 🛠️ What changed

**New `MicCaptureService` (`Hark.Core/Capture/MicCaptureService.cs`)**
- Mirrors `LoopbackCaptureService`: `WasapiCapture` on `DataFlow.Capture`, emitting the same
  `Action<byte[],int> DataAvailable` + `WaveFormat` shape so it reuses `PcmConverter` unchanged.
- Uses **`Role.Multimedia`, not `Role.Communications`** — the Communications role marks the app as a
  phone/meeting client and triggers system-wide comms-mode DSP (narrowband + ducking) on *all*
  playback (learned in WavBall). Mic device format (PCM16/float, mono/stereo, any rate) is normalized
  by `PcmConverter`.

**`PcmConverter` — float-domain conversion + shared quantizer**
- Split into `ConvertToFloat(byte[],int) → float[]` (16 kHz mono, still in [-1, 1]) and a static
  `QuantizeToPcm16(ReadOnlySpan<float>)` (clamp → LE 16-bit). `Convert` now just composes the two, so
  its behavior/API is unchanged. Mixing happens in the **float** domain with a single clamp at
  quantization — avoids double-clipping two summed sources.

**`HarkSession` — mix mic into the loopback stream**
- New ctor flag `mixMicrophone` (default **off** → CLI unchanged). When on, `StartAsync` also starts a
  `MicCaptureService` + its own `PcmConverter`; a missing/failed mic is **non-fatal** (surfaced via
  `Error`, then it carries on loopback-only).
- **The mic clocks the stream when active.** `OnMicData` converts each mic buffer to 16 kHz mono float,
  drains up to N queued loopback (far-side) samples, sums, then quantizes and writes via a shared
  `Emit`. `OnDataAvailable` (loopback) enqueues into a lock-guarded `Queue<float>` capped at ~1 s
  (drop-oldest) while the mic is on, and emits directly when the mic is off. A capture endpoint is
  continuous, so the user's own voice is never dropped — even in total system silence, which
  suppresses loopback callbacks entirely.
- **Live toggle:** `SetMicEnabled(bool)` starts/stops the mic mid-session (and remembers the choice for
  the next start), so the overlay's headset button flips mixing without a restart.

**Desktop wiring (`Hark.App/App.xaml.cs` + overlay)**
- Mic mixing is **on by default** (`mixMicrophone: _mixMic`); `HARK_MIX_MIC=0`/`false` (via the same
  env → `%APPDATA%\Hark\config.json` → user-secrets precedence) disables it for the speaker case.
- A **headset toggle** in the overlay's top bar (lit = on, dim = off) calls `SetMicEnabled` live and
  seeds its initial state from the configured default.

## 🧠 Decisions
- **Mix real audio, not spectra — unlike WavBall** — **because** HARK transcribes; the recognizer needs
  one coherent PCM stream. Summing samples (float domain, single clamp) is the correct primitive;
  band-level RMS combine is a visualization shortcut that would destroy intelligibility.
- **The mic clocks the stream; loopback is queued** — **because** a WASAPI *capture* endpoint delivers
  continuous real-time audio (the ADC always samples), whereas *loopback* stops firing entirely when
  nothing is playing. Clocking off the mic guarantees the user's own voice is always emitted; loopback
  (which only matters when the far side is actually playing) is mixed in from a small queue. When the
  mic is off, loopback clocks directly as before.
- **On by default, `HARK_MIX_MIC=0` to opt out** — **because** the headset scenario (the actual ask)
  has no acoustic echo, so mixing is a pure win; on **speakers** the mic re-captures playback and
  would double the transcript, so users there need one flag to turn it off.
- **Missing mic is non-fatal** — **because** HARK's premise is loopback-first; a machine with no input
  device (or a denied mic) must still caption system audio.

## 🚧 Problems & resolutions
- **Symptom:** two async real-time streams to combine → **Resolution:** float-domain sum with a small
  (~1 s) drop-oldest queue absorbing inter-thread jitter; one clamp at quantization.
- **Symptom (shipped, then fixed):** first cut clocked off **loopback**, so speaking with nothing
  playing produced **no captions** — WASAPI loopback raises no callbacks during system silence, so the
  mic queue filled and dropped. → **Resolution:** invert the roles — the **mic clocks** the mixed
  stream whenever it's active (capture is continuous), loopback is the queued secondary. Caught
  live by HARK captioning its own "the mic isn't being picked up" test session once fixed.
- **Symptom:** `Role.Communications` would have been the "obvious" mic role → **Avoided:** it degrades
  all system playback to comms-mode audio; used `Role.Multimedia` per WavBall's hard-won note.

## 🔮 Next / open threads
- **Echo suppression on speakers:** rather than just toggling mic off, an AEC pass could let speaker
  users mix mic without doubling — larger scope.
- **Per-source diarization hint:** the mic is known to be "me"; a future engine-boundary event could
  label the local speaker deterministically instead of relying on clustering.
- **Level meter:** the HAL eye now reflects the *mixed* level; fine, but a per-source meter could show
  mic vs far-side activity separately.
- **HAL eye dimness (surfaced by the app itself):** the mic-test recap minuted that the eye "appeared
  washed out... remained very dim" — a real responsiveness/visibility note to chase (bloom layer /
  floor tuning).
