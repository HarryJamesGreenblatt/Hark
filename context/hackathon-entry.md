# HARK

### *Hear. Adapt. Render. Keep.*

> **HARK turns any conversation your PC can hear into live captions, speaker-aware summaries, and a living visualization — narrated by an Oracle that watches, understands, and renders what's being said.**

---

## The problem

Windows can *caption* audio — but it can't *understand* it. Live Captions and Voice Typing are accessibility features: they transcribe, then forget. There's no notion of **who** spoke, **what it meant**, or any way to **keep** the result. And none of it is scriptable or agent-friendly.

HARK replaces accessibility-only tooling with something **owned, automatable, and interpretive** — a pipeline that not only hears a conversation, but makes sense of it and shows you what it means.

## What it does

HARK taps whatever your machine is playing — a meeting, a video, a call, or your own headset mic — and moves through four stages:

- **🎧 Hear** — Captures system playback via **WASAPI loopback** (no microphone needed) plus an optional local mic, and streams it to **Azure AI Speech** for near-real-time captions.
- **🧠 Adapt** — Attributes every line to a speaker, **re-diarizes the whole session** on stop for accuracy, **names voices from context**, and distills the discussion into structured **Conversation** and **Speaker** recaps.
- **🔮 Render** — **The Oracle.** Click its glowing, sound-reactive eye and it dilates into a full-screen **Vision**: a live **mind-map** of the conversation with the eye as its hub, and a **cinematic AI-generated scene** rendered inside its pupil — re-conjured beat by beat as the discussion moves.
- **💾 Keep** — Exports the entire session — transcript, both recaps, and the Vision — as a polished report in **five formats**: Markdown, Word, PowerPoint, PDF, and web, all sharing one editorial layout.

## The Oracle — the part you'll remember

Most transcription tools are a wall of text. HARK gives its AI a **face and a mind**.

The **Oracle** is a metallic-framed, glowing-red eye that pulses with the audio — its pupil dilates on bass, its highlight shimmers on treble, and it **gazes around like a real eye** (a procedural model grounded in actual oculomotor research: ballistic saccades, fixation micro-tremor, and pupil parallax). Click it, and it opens into the Vision: the Oracle **watches the conversation and renders a live, didactic picture of what it means** — a structured mind-map behind the eye, and a **FLUX.2** cinematographic scene conjured inside the pupil.

It makes an otherwise invisible AI pipeline something you can literally **watch think**.

## How it's built — Microsoft-native, end to end

- **.NET 9 · WPF · WASAPI** — a tray overlay *and* a scriptable CLI over one shared engine.
- **Azure AI Speech** — real-time streaming transcription + fast-transcription re-diarization.
- **Microsoft Foundry** — `gpt-4.1-mini` for the structured recaps and speaker naming; **FLUX.2-pro** for the Vision scenes.
- **Entra ID, keyless** — role-based auth, no secrets in source.
- **Bicep + GitHub Actions** — one-click Azure provisioning and a **signed MSIX installer**, published tag-driven as a self-contained release.

## Try it

Grab **`Hark-Setup.zip`** from the [latest release](https://github.com/HarryJamesGreenblatt/Hark/releases/latest), extract, and run — it's self-contained (no .NET SDK needed). Press **Ctrl+Win+H** to toggle captions, then click the Oracle's eye. Captions and speaker pages work with just an Azure Speech resource; the Summary and Vision features light up when you add a Foundry endpoint (the installer can provision everything for you).

## What's next

- A live, in-conversation mind-map (today's Vision re-conjures on topic beats).
- A local/offline transcription engine behind the existing swap point, for fully on-device capture.
- Deeper agent integration — HARK's structured output is built to feed downstream tools.
