# 🎬 Episode 26 — Hardening the Crystal Ball: Reconciled Infra, Toggle Persistence, a Resilient Blinking Pupil & the Scene-Oatmeal Reckoning

> **Date:** 2026-08-30 · **Branch:** `main` · **Commits:** `258a839..fa2199c`
> **One-liner:** With the dual-layer crystal ball shipped (EP25), this session **hardened** it end to end —
> reconciled the gpt-image→FLUX artifact debt (incl. the Foundry/FLUX **Bicep**), fixed a **toggle-persistence
> race**, confirmed **FLUX is enterprise-safe** on Foundry (sold-by-Azure, DPA), and made the pupil **resilient
> to content-safety refusals** (a recent-image **ring buffer** + a masked-in-pupil **blink/crossfade**) — then
> pinned the remaining truth: the **photographic *scene* layer is still the EP18/EP22 oatmeal**, and the fix is
> to free it to be **evocative** now that the diagram carries the literal payload.

## 🎯 Intent
Carried from EP25's open threads plus live-test findings. User beats across the session: *"infra last docs
first please including readme"* → *"why are we including both models if only flux is used"* → *"if i leave
hark running after toggling off… the previous session's state comes back"* → *"maybe expose toasts so i can
see if its a 429 or something else"* → *"where are these images going… should we save them"* → *"foundry
offers no umbrella protections of enterprise data? if not does the gpt series?"* → the blink/crossfade UX
iterations → *"the images being produced are all very similar and stop following our concept of the beat…
we might carefully document the storyline… and take it to the next session."*

## 🛠️ What changed
- **gpt-image→FLUX artifact reconciliation (`258a839`)** — the render tier is provider-agnostic with
  **FLUX.2-pro the effective default** and the diagram is **native**, but ~100 refs still said gpt-image-1/2.
  Fixed the living docs: `README.md` (provider/quality knobs; diagram needs no image deployment), dated
  **EP24–25 banners** on `cristobal-vision.md` + `crystal-ball-design-brief.md`, and the misleading
  `VisionRenderer` / `VisionService` / `InfographicConcept` comments. **Infra Bicep reconciled to reality:**
  `modules/openai.bicep` is now an **`AIServices` (Foundry)** account hosting `gpt-4.1-mini` + `gpt-image-2`
  + **`flux2-pro`** (`format: 'Black Forest Labs'`, GlobalStandard cap 10) with the **Cognitive Services
  User** RBAC for the FLUX provider route; model versions/SKUs verified against the live account; the
  account-name prefix is now `fdry-`. **gpt-image stays opt-in** (`deployOpenAiImage=false`); FLUX is the
  default (`deployFlux=true`), so a fresh deploy never creates the unneeded gpt-image resource.
- **Toggle-persistence fix (`c74b009`)** — toggling captions off→on could **resurrect the previous session**:
  the fire-and-forget offline re-diarization from the stop landed *after* the new session started and rebuilt
  the store with the old transcript. Now the refine runs under a **`CancellationTokenSource` cancelled on any
  toggle** (threaded through Fast-Transcription + semantic + naming), its apply is **guarded against session
  supersession**, and its **error balloon is silenced** (best-effort background pass; the metric balloon was
  already gone in EP20) → no debug toasts.
- **Resilient pupil: ring buffer + blink/crossfade (`721198b`)** — the pupil froze when a render was refused.
  Added a **recent-image ring buffer** (last 5) + a **filler cycle** (5 s tick, only after a 16 s stall past
  the normal cadence). Restructured the pupil into a **circular-clipped `Grid`** holding the FLUX image **and**
  a blink eyelid, so the lid blinks **inside** the pupil (masked by it) rather than superimposed. Unified
  fresh renders and filler cycles into one **`TransitionPupil`** = a cornea-red eyelid **blink** + image
  **crossfade**.
- **FLUX parser hardening (`fa2199c`)** — FLUX intermittently returns **HTTP 200 with an empty `data:[]`**
  (a soft-moderated no-image), and the old `data[0]` threw *"Index was outside the bounds of the array."* Now
  guards every step and, on failure, throws a **descriptive error including the (truncated) body** so the
  cause is visible. **(Grep signature: `returned 200 with no image`.)**
- **Temporary diagnostic (in `721198b`, still present)** — a toast surfaces scene-render failures on
  autonomous beats (otherwise swallowed). It **confirmed the freeze cause is RAI, not rate-limit** and later
  surfaced the empty-`data` index bug. Marked for removal.

## 🧠 Decisions
- **FLUX on Foundry is enterprise-safe — same protections as the gpt series** — **because** Microsoft's docs
  put **Black Forest Labs FLUX under "Models sold by Azure"** (hosted+operated by Microsoft under Product
  Terms + the **DPA**): prompts/outputs are not shared with the provider, not used to train, stateless models,
  processed in your Foundry resource's geography. This is **unlike** "Partner & Community" models (e.g. Claude,
  where Anthropic is the processor). So data does **not** "go out to Black Forest Labs." (Abuse-monitoring +
  synchronous Guardrails still apply — the latter is the `content_safety_violation` we hit.)
- **Persistence/slide-decks deferred; the freeze fix needs no storage** — **because** the frozen-pupil cure is
  a purely **in-memory** ring buffer. Saving images (for an M365/OneDrive-style session deck) is a real but
  separate opt-in feature; the render already lives under Azure DPA, so it's a lesser privacy concern, but
  it's decoupled from the loop.
- **gpt-image stays opt-in in IaC; FLUX is the default** — **because** the render tier is committed to FLUX;
  a fresh deployment shouldn't create the unused gpt-image resource. The renderer keeps its gpt-image code
  path for anyone who explicitly deploys one.
- **The freeze was RAI, not a cap** — **because** the diagnostic toast read `content_safety_violation
  (BingBlockList_Prompt)` — scattered, topic-dependent prompt blocks, not a rate limit. So the cure is
  **survive a refused render** (buffer), not "slow down."

## 🚧 Problems & resolutions
- **Symptom:** previous conversation reappears after toggle off→on. → **Root cause:** the async offline refine
  rebuilt the store into the freshly-started session. → **Fix:** cancel-on-toggle + session-supersession guard.
- **Symptom:** the pupil freezes on one image while the diagram keeps advancing. → **Root cause:** FLUX
  `content_safety_violation` refusals, **swallowed silently** on autonomous beats. → **Fix:** ring buffer +
  filler cycle so refused beats don't matter; diagnostic toast to see the cause.
- **Symptom:** intermittent *"Index was outside the bounds of the array."* → **Root cause:** FLUX 200 with
  empty `data:[]` → unguarded `data[0]`. → **Fix:** guarded parse + descriptive body-in-error.
- **Symptom:** the first blink drew a **red circle superimposed over** the pupil (and bigger than it, since
  the pupil scales with audio). → **Root cause:** the lid was a sibling layer, fixed-size, not masked by the
  (scaled) pupil. → **Fix:** restructured `VisionOrb` into a circular-clipped `Grid` carrying the dilation,
  with the image + lid as children → the lid blinks *inside* the pupil.

## ✅ Verification
- **Builds green** across the batch; commits `258a839..fa2199c` pushed (all Hark.App/Oracle/infra — the
  Hark.App pushes touch no `infra/**` path, so the provision workflow doesn't fire; a push touching infra
  runs `deployOpenAi=false` = Speech-only idempotent, **no duplicates**).
- **Live tests** — Cobain/Fonda/CENTCOM runs: diagrams track beats well; the RAI toast fired and the ring
  buffer kept the pupil alive; the blink now renders **inside** the pupil; no more index crashes.
- **Bicep** — `az bicep build infra/main.bicep` exit 0; model shapes verified against `fdry-hark-fb360`.
- **Data-governance** — grounded in Microsoft Learn (`Models sold by Azure` data-privacy; the BFL
  "sold-by-Azure" list; the Claude "partner" contrast).

## 🔓 Open threads
- **THE SCENE-OATMEAL — the headline next-session task.** The native **diagram** tracks beats beautifully, but
  the photographic **pupil scene** still hits the **EP18/EP22 single-topic ceiling** (CENTCOM → "commander at a
  map" every beat; the ring buffer's cycling reinforces it). The beats/windowing are **intact** — this is the
  `ConceptDesigner` **literal-bias**, which is now **redundant and counterproductive**: since the diagram
  carries the literal/didactic payload, the **scene should be freed to be evocative/metaphorical** (undo EP22's
  literal steer *for the scene tier only*) → varied ambient mood instead of a repeated literal subject. Likely
  also: don't cycle *near-identical* buffer images (dedupe the buffer), and consider a **softened-prompt retry**
  on `content_safety_violation` to recover blocked beats rather than only masking them.
- **Remove the temporary diagnostic toast** (`ShowSceneAsync` in `App.xaml.cs`) once the scene work stabilises.
- **Infra manual-run caveat:** a manual `deployOpenAi=true` run auto-names `fdry-hark-<suffix>` — pass
  `openAiAccountName=fdry-hark-fb360` to reconcile the existing account instead of creating a parallel one.
  Optional: delete the unused `gpt-image-2` deployment (cap 1) on the live account to stop any billing.
- **Persistence / session-deck** — a deliberate opt-in feature for later (config-designated, OneDrive-friendly
  folder); the diagram + scene + recap per beat are the raw material for an auto-built deck.
- Carried: installer WPF rewrite (EP23), the engine boundary, diarization Fork A.
