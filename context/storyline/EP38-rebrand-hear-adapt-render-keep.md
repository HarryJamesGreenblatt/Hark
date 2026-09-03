# 🎬 Episode 38 — The Rebrand: HARK = Hear · Adapt · Render · Keep

> **Date:** 2026-09-02 · **Branch:** `main` · **Commits:** `70aed5a` (after the EP37 lock `a4eb622`)
> **One-liner:** Shipped **2.1.0 item #1** — swapped the backronym's R-word **Recognize → Render** and
> **re-storied the architecture** so the four letters map onto the whole product (Hear = capture+transcribe,
> Adapt = diarize/refine/name/summarize, **Render = Vision**, Keep = save/export).

## 🎯 Intent
Start the locked 2.1.0 backlog (EP37) with the cheapest, highest-identity item: *"lets push this up and get
to work on 2.1."* The rebrand leads because it reframes how every other artifact is described.

## 🛠️ What changed
The key discovery up front: the backronym **already existed** as **"Hear · Adapt · Recognize · Keep"**, so
this was a precise **Recognize → Render** swap (both spell HARK), not a fresh coinage. Per a scoping
question, the user chose the **full re-story** (not tagline-only) — the four letters now describe the
**product's four movements**, with transcription folded into **Hear** and **Render = Vision**.

- **Brand taglines → "Hear. Adapt. Render. Keep."** — `README.md` title, `Hark.Cli/Program.cs` (banner
  comment + `--help` text), `Hark.App/Package.appxmanifest` (×2 Description), `Hark.Installer/InstallerWindow.xaml`.
- **`README.md` "How it works" re-storied** — replaced the low-level 4-box transcription dataflow with a
  **four-movement** diagram + a component table: **Hear** (capture · PCM · Azure Speech), **Adapt**
  (`ConversationDiarizingTranscriber` · `FastTranscriptionRefiner` · `SemanticDiarizationRefiner` ·
  `SpeakerNamingRefiner` · `AzureOpenAiSummarizer`), **Render** (`Hark.Oracle.Vision`), **Keep**
  (`Output/*Sink` · `Hark.App/Reporting` → Markdown·Word·PowerPoint·PDF·Web). The Hear-internal dataflow +
  the `ISpeechTranscriber` swap-point note are kept beneath it.
- **`Hark.Core` stage comments reconciled** — the doc-comments that labeled the transcription tier
  "Recognize" now read **"Hear"** (`HarkSession` summary, `Audio/PcmConverter`,
  `Transcription/AzureSpeechTranscriber`, `ISpeechTranscriber`, `ConversationDiarizingTranscriber`); the CLI
  pipeline comment + the `[Recognize]` error log tag → `[Hear]`.

## 🧠 Decisions
- **Full re-story over tagline-only — because** the product now does far more than "recognize"; the four
  letters tell a truer story when they map to capture → understanding → Vision → export. (Chosen via a
  focused scoping question rather than guessing, since a wrong mapping = a large wrong diff.)
- **Fold transcription into "Hear" — because** there's no "Render" in the core transcription pipeline
  (Vision lives in `Hark.Oracle`), so re-labeling the transcribe step "Render" would lie; "Hear = hear what's
  said (capture + transcribe)" is honest and leaves **Adapt** to mean diarize/refine/name/summarize.
- **Leave genuine Speech-SDK terms untouched — because** `SpeechRecognizer`, `RecognizedSpeech`,
  `OnRecognized`, the `recognizer` field, and the recognizer-error surfaces name the actual API/behavior, not
  the brand stage. Historical storyline episodes were also left as-is (append-only log; don't rewrite history).

## 🚧 Problems & resolutions
- **Ambiguity:** "Recognize" meant three things — the brand stage-word, an architecture stage label, and real
  SDK terms. → **Resolved** by treating them separately: rebrand the first two (to Render / Hear respectively),
  never touch the third. A `grep` for the SDK terms confirmed none were caught.

## ✅ Verification
- `grep` confirms every user-facing backronym now reads "Render" and no brand/stage "Recognize" remains
  (SDK terms intact).
- **Builds:** `Hark.Cli` (+ `Hark.Core`), `Hark.App`, and `Hark.Installer` all build clean (0 errors;
  only the pre-existing WFO0003/CS4014 warnings).

## 🔓 Open threads
- **HARK 2.1.0 — 1 of 6 done.** Remaining, in suggested order: the **export-polish cluster** — a **generated
  session title** (replace the hardcoded `"Hark session report"`), **PDF light mode**, and a **consistent HAL
  icon across exports** (all in `Hark.App/Reporting/`) — then the **installer pre-UAC delay** (measure first),
  with the **organic eye-motion** research spike in parallel. Full list + seams:
  `/memories/repo/hark-2.1.0-backlog.md`.
