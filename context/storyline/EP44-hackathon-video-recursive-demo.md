# 🎬 Episode 44 — The Recursive Demo: A Self-Demonstrating Hackathon Video

> **Date:** 2026-09-04 · **Branch:** `main` · **One-liner:** Designed HARK's 2-minute Hackathon submission
> video as a **self-demonstrating run-through** — the soundtrack *is* the product's input — and locked the
> song (**Bing Crosby's "Do You Hear What I Hear?"**) whose refrains map onto Hear/Render/Know. Captured in
> [`../hackathon-video-plan.md`](../hackathon-video-plan.md) for production on another machine.

## 🎯 Intent
The Hackathon needs a **2-minute** video (hard limit) that reads as a *commercial*, not a technical
walkthrough. Talk through the creative approach — soundtrack, narration, pacing — and produce a durable brief
so the actual editing can happen elsewhere.

## 🛠️ What changed
- **`context/hackathon-video-plan.md` (new)** — a self-contained production brief: the concept, the song
  decision + rationale, the ASR constraint + tricks, the proven 8-beat output, a 2-minute beat sheet, assets,
  and open decisions for the editing session.
- No code changed this episode.

## 🧠 Decisions
- **Self-demonstrating, uncommentated run-through — because** at 2 min, voiceover is cram-heavy and would
  fight a vocal soundtrack for the word-channel. Instead the video's **soundtrack is the input**: play a song →
  HARK hears/captions it, renders Vision beats, and exports a report. Only HARK could make this video, and it's
  honest (a real run, no faked narration).
- **Song = Bing Crosby, "Do You Hear What I Hear?" — because** (1) it *transcribes cleanly* (crooned,
  near-spoken; sung+music breaks ASR — "Every Breath You Take" was tested and dropped ~every other word), (2)
  the title leads with **HEAR**, (3) its refrains **HEAR / SEE / KNOW** map onto **Hear / Render / Adapt-Keep**
  — the song asks the three questions HARK answers, and (4) its shifting imagery gives *varied* Vision beats
  (dodges the single-topic oatmeal problem). Licensed-familiar is fine for an internal MVP pitch.
- **Hide render latency by recording the instant timeline replay — because** FLUX beats take ~10 s; after a
  live capture the beats are cached on the rail, so clicking through them plays instantly. (Alt: speed-ramp the
  dead time in post with the song audio kept continuous.)

## 🚧 Problems & resolutions
- **Symptom:** the first-choice song (The Police, "Every Breath You Take") barely transcribed — dropped ~every
  other word. → **Root cause:** ASR is trained on *speech*; *sung* vocals + backing music defeat it. → **Fix:**
  switched to a **crooned, clearly-enunciated** standard (Crosby) that reads almost like speech; documented the
  vocal-stem-isolation and spoken-source (HAL dialogue) fallbacks for future tracks.

## ✅ Verification
- A real session was captured + exported (`context/output/Hark-20260904-120926.md` / `.pptx`): **8 varied
  Vision beats**, a coherent peace-themed summary, the speaker correctly named **"Bing Crosby,"** and a clean
  lyric transcript (with charming ASR mishears that *prove* it's live).

## 🔓 Open threads
- **Produce the video on the other machine** per `hackathon-video-plan.md` (editor + screen-capture choice,
  pick the 3–4 most striking beats, cut to ≤ 2:00, end card with `oracle-brand.gif` + tagline).
- Then **submit** the Hackbox entry (copy in `context/hackathon-entry.md`) with the video + animated tile.
