# HARK — Hackathon Submission Video Plan

> **Purpose.** A complete, self-contained brief for producing HARK's 2-minute Microsoft-Hackathon
> submission video. Written so a fresh agent/machine can pick it up cold. Companion to the submission copy
> in [`hackathon-entry.md`](./hackathon-entry.md). Decisions below are **made** unless marked "open."

## Goal & format
- **2 minutes, hard limit.** A **commercial**, not a technical walkthrough — pace and feeling over
  feature-by-feature narration.
- **No voiceover.** The 2-min cap makes VO cram-heavy (too many UI things to articulate → jump-cut soup),
  and a vocal soundtrack would fight a VO for the word-channel. So: **song + on-screen text + visuals** carry
  it. (TTS/VO was considered and dropped.)

## The concept (DECIDED): the recursive, self-demonstrating run-through
An **uncommentated run-through where the video's soundtrack IS the product's input.** Play a song through the
machine → HARK *hears* it, captions the lyrics, *renders* Vision beats of them, and *exports* a report. The
medium becomes the demo — only HARK could make this video. It's honest (real run, no faked narration) and it
shows the whole **Hear → Adapt → Render → Keep** pipeline end to end.

## The song (DECIDED): Bing Crosby — "Do You Hear What I Hear?"
Chosen after live testing. Why it's near-perfect:
- **It transcribes cleanly.** Azure Speech (ASR) is trained on *speech*, not *singing* — sung vowels +
  backing music make it drop words. Crosby's slow, crooned, clearly-enunciated delivery reads almost like
  speech → transcribed **"clear as crystal."** (The Police "Every Breath You Take" was tested first and
  dropped ~every other word — **abandoned** for that reason.)
- **The title leads with HEAR** — HARK's first movement.
- **The refrains ARE the product's arc:**
  - *"Do you **HEAR** what I hear?"* → **Hear** (transcribe)
  - *"Do you **SEE** what I see?"* → **Render** (the Oracle's Vision)
  - *"Do you **KNOW** what I know?"* → **Adapt / Keep** (the summary/comprehension)
  The song literally *asks the three questions HARK answers.* This is the video's spine, gift-wrapped.
- **Varied imagery per verse** (night wind/star → shepherd/song → king's palace → shivering child/gold →
  peace/light) → the Vision beats naturally differ, sidestepping HARK's single-topic "oatmeal" sameness.
- **Licensing:** a licensed familiar track is fine here — internal MVP/prototype **pitch**, not for sale.

## Proven output (already captured — reuse or re-capture)
A real session was run and exported: **`context/output/Hark-20260904-120926.md`** (+ matching `.pptx`).
Results to expect/reproduce:
- **8 Vision beats**, topics in order: (1) Night Wind & Land · (2) Observing Sky & Land · (3) Star / Shepherd
  boy · (4) "voice as big as the sea" song imagery · (5) Shepherd & Mighty King · (6) Gift to a child (silver
  & gold) · (7) Peace & Prayer · (8) Hope & Light from the child.
- A coherent, even moving **Conversation summary** ("a message of hope and peace").
- The **Speaker card is named "Bing Crosby"** (HARK identified the performer).
- A clean lyric **transcript** with a couple of charming mishears ("little land" for "little lamb") — **leave
  them in**; they prove it's real, live ASR, not a scripted fake.

## Technical constraints & the tricks that solve them
1. **Render latency (~10 s per FLUX beat) — must be hidden in a 2-min cut:**
   - **Preferred trick:** after a live capture, the 8 beats are cached on the **timeline rail**. **Record the
     instant replay** — click through the pre-rendered beats *in time with the song*; zero model wait, buttery
     transitions.
   - **Alternative:** capture live, then in post **speed-ramp the dead time 4–8×** and snap to real-time the
     instant a vision lands. **Keep the song audio continuous** underneath — jump-cut the *video* freely,
     never the audio. The unbroken song is what makes fast cuts read as intentional.
2. **ASR limitation (general rule):** never feed *sung + music*. Use clear crooned / near-spoken vocals
   (Crosby works). If a track drops words, isolate the **vocal stem** (LALAL.ai / Moises / vocalremover.org /
   Audacity) and feed that while the viewer hears the full song. Spoken sources (e.g. **HAL 9000** dialogue —
   on-brand for the Oracle) are a bulletproof fallback that transcribe perfectly.
3. **Leaving HARK running with the overlay toggled off is safe** — no recurring background Azure calls (only a
   one-time offline diarization refine fires right after each stop). Fine to leave idle between takes.

## 2-minute beat sheet (cut to the song's build)
1. **0:00** — black; the dim Oracle eye; title flash **HARK**.
2. **Song starts** → the eye **pulses** to it; lyric **captions** stream in on *"Do you hear…"* — **Hear**.
3. On *"Do you see…"* → **click the eye** → the match-cut zoom into the full-window **Vision** page.
4. **Render** — feature the **3–4 most visually striking/varied** beats (strong candidates: the *star
   dancing*, the *king's palace*, the *child in light*). Instant-replay / speed-cut between them.
5. On *"Do you know…"* → flash the **summary** + the **"Bing Crosby"** speaker card — **Adapt**.
6. **Finale** — hit **Save** → the **multi-format report** of *this very song* generates → land on the polished
   **PPTX/PDF** — **Keep**. (This is the recursion mic-drop: the video documents its own soundtrack.)
7. **End card** — `assets/thumbnail/oracle-brand.gif` + the tagline:
   *"Hear. Adapt. Render. Keep. — captions with a mind (and an eye) of their own."*

## Assets on hand (in this repo)
- **Wordmark loop / thumbnail:** `assets/thumbnail/oracle-brand.gif` (pure-eye fallback: `oracle-eye.gif`).
- **Proven session report + deck:** `context/output/Hark-20260904-120926.md` and its `.pptx`.
- **Submission copy** (description · tagline · problem/opportunity · keywords): `context/hackathon-entry.md`.
- **Run HARK:** installer from the GitHub Release (`v2.1.0`), or `dotnet run --project Hark.App` (region in
  user-secrets). Toggle captions with **Ctrl+Win+H**; click the Oracle's eye to open Vision.

## Open decisions for the production session (other machine)
- **Video editor:** DaVinci Resolve (free, strong) / Premiere / Clipchamp / CapCut.
- **Screen capture:** OBS or Xbox Game Bar at ≥1080p60; capture the HARK window + the system-audio playback.
- **Which 3–4 beats to feature:** view the eight FLUX scenes and pick the most visually distinct.
- **On-screen text:** whether to add subtle Hear/Adapt/Render/Keep labels, or let the lyric captions + visuals
  imply the movements.
- **End-card styling:** match the dark HAL aesthetic; keep the whole cut ≤ 2:00 including the end card.
- **Audio:** license/source the Crosby recording; ensure the song is the continuous bed under all cuts.
