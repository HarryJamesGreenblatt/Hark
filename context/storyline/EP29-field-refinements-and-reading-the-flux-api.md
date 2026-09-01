# 🎬 Episode 29 — Field Refinements & Reading the FLUX API (Not Guessing It)

> **Date:** 2026-08-31 · **Branch:** `main` · **Commit:** `6e00c77`
> **One-liner:** Three field-testing fixes — a caption **toggle now fully resets Vision**, a speaker
> **rename now refreshes the recap's task owners**, and — after actually **reading the FLUX.2 API** instead
> of theorizing about it — the **content-moderation dial (`safety_tolerance`) is turned to its max**.

## 🎯 Intent
Back from field testing. User: *"toggling does not clear the buffer ring or vision session"*; renamed
speakers *"still say 'the guests'"* in the meeting-notes follow-up-task owner; and fairy tales (Jack and the
Beanstalk, Little Red Riding Hood) *"struggle so badly"* — the giant-eats-children content trips the filter
so no image renders. The user's steer on the last one was decisive: *"I think we need to research the FLUX.2
pro API more carefully without making assumptions,"* then *"if you can reduce the content filtering, do it as
much as permissible."*

## 🛠️ What changed
- **Toggle clears the whole Vision session (`6e00c77`)** — `App.ResetConversation` also nulls the missed
  `_cachedDiagram` and calls a new **`OverlayWindow.ResetVision()`** (StopScrying + StopPupilFiller[clears the
  pupil ring buffer] + ClearVisionDiagram + HideVisionOrb). A caption toggle **bypasses** `CloseVision`'s
  cleanup, so the previous session's buffered pupil images and diagram were surviving into the next one.
- **Rename refreshes the recap owners (`6e00c77`)** — `App.OnSpeakerRenameRequested` now invalidates
  `_cachedRecap` / `_cachedSpeakerRecap` / `_cachedRevision`. The recap's follow-up-task **owner** references
  speaker labels, so a rename made the cached recap stale; invalidating it makes the next SUMMARY regenerate
  from the relabeled transcript (a specific `Guest-N` → real name now attributes correctly).
- **FLUX `safety_tolerance = 5` (`6e00c77`)** — `VisionRenderer.RenderProviderAsync` now sends
  `safety_tolerance` at the documented **max (0–5, default 2, 5 = least strict)** — only on the FLUX provider
  path. We were silently sitting at the default of 2.

## 🧠 Decisions
- **Read the first-party API before theorizing** — **because** the prior discussion was guessing about
  "FLUX generation types" and style fallbacks. The BFL **OpenAPI spec** (`api.bfl.ai/openapi.json`) + MS Learn
  settled it with facts (below), and the user explicitly asked to stop assuming.
- **Turn moderation to max-permissible, then TEST** — **because** the user asked to reduce filtering "as much
  as permissible" (= `safety_tolerance: 5`), and whether that actually fixes fairy tales is an **empirical**
  question, not a foregone one (see Open threads).

## 🔬 Research findings (first-party — MS Learn + BFL OpenAPI)
- **`safety_tolerance` is the moderation dial** and we weren't sending it. FLUX.2 [pro] `Flux2Inputs`:
  integer **0–5**, default **2**, **5 = least strict**, governs **input *and* output** moderation. Going
  beyond 5 requires contacting BFL.
- **Foundry provides *no* built-in content filtering for FLUX at deployment** (MS Learn) — so the block is
  largely BFL's own model-side moderation → the `safety_tolerance` knob.
- **FLUX has no style/type "modes"** — the "categories" (Photorealism / Typography / Grounding / Color /
  Structured / Creative) are prompt *capabilities*, not switches. "Different types" = model **variants**:
  [pro] (default), [flex] (`guidance` 1.5–10 + `steps` ≤ 50, best for text/layout), [max] (grounding search),
  [klein] (open weights). Style is prompt-driven.
- **Other unused params:** `seed`, `output_format` (jpeg/png/webp), `disable_pup` (pro/max upsample by
  default), `input_image…_8` (multi-ref), max output 4 MP.
- **Quota tiers:** FLUX.2-pro Low **15** / Med **30** / High **100** RPM (flex 5/10/25).
- *(Tooling note: `microsoft_docs_search`/`microsoft_docs_fetch` MCP for the Azure-hosted behavior; the
  integrated browser + `api.bfl.ai/openapi.json` for the exact request schema.)*

## 🚧 Problems & resolutions
- **Symptom:** toggling captions leaves the old Vision session/buffer. → **Root cause:** the toggle path
  hides the overlay and resets the store, but never runs `CloseVision`'s Vision cleanup, and `ResetConversation`
  missed `_cachedDiagram` + the overlay buffer. → **Fix:** `ResetVision()` + null `_cachedDiagram`.
- **Symptom:** renamed speaker still owns tasks under the old label. → **Root cause:** the recap is cached and
  its owners are speaker labels; rename didn't invalidate it. → **Fix:** invalidate the recap cache on rename.
- **Symptom:** fairy tales render nothing (`content_safety_violation`). → **Root cause (partial):** we render
  fable violence in literal/photographic language at the default moderation level. → **Fix (partial):**
  `safety_tolerance = 5`; the rest is an open thread pending a test.

## ✅ Verification
All three build green; committed + pushed `6e00c77`. The fairy-tale render is the **one thing not yet
proven** — it needs a live caption test (below).

## 🔓 Open threads
- **Fairy-tale render — TEST, don't assume.** `safety_tolerance = 5` dials down **BFL's** moderation, but the
  observed error was `content_safety_violation (**BingBlockList_Prompt**)`, which reads like a **Microsoft
  prompt term-block-list** — a *separate* layer `safety_tolerance` may not affect. Decisive test: caption
  **"Jack and the Beanstalk"** and watch the pupil. Renders → BFL moderation was it, done. Still
  `BingBlockList_Prompt` → it's the Microsoft prompt filter → next lever is **prompt-side wording** or a
  **content-filter opt-out** request (`aka.ms/oai/…`), researched the same first-party way.
- Carried from EP27: the FLUX **negative clause** in `VisionPromptComposer.Compose` (a gpt-image-ism) and the
  **temporary diagnostic toast** in `App.ShowSceneAsync`.
