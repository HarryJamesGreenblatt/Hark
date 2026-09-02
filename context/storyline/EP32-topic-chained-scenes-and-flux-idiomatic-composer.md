# 🎬 Episode 32 — Aligning the Crystal Ball: Topic‑Chained Scenes & a FLUX‑Idiomatic Composer

> **Date:** 2026-09-01 · **Branch:** `main` · **Commits:** `3681b5d..2a1dc3a`
> **One-liner:** An evidence check on a captured Aladdin session showed the **topics** track the transcript
> faithfully but the **image↔topic** match is loose *by architecture* (scene and diagram are independent
> distillations of the same window) — so we **chained the scene to the diagram's topic** and **realigned the
> prompt composer to FLUX's first‑party grammar** (front‑loaded, positive‑only, no negatives); plus a pupil
> "ether" alpha, a slower standby fade, and **temp‑cache hygiene** on toggle‑off/exit.

## 🎯 Intent
Post‑polish the crystal ball and answer a nagging question: *"is this the right image for this topic, and are
the topics matched to what inspired them?"* The user asked to **corroborate the FLUX assessment with online
research** before changing prompts, and to **assess the ConceptDesigner** for gpt‑image‑isms that "clobber
FLUX's native power." Also a temp‑hygiene observation: scene assets were persisting in `%TEMP%` after toggle‑off.

## 🛠️ What changed
- **Pupil "ether" alpha + slower standby fade (`3681b5d`)** — the pupil scene now rests at
  `PupilSceneOpacity = 0.85` so the red cornea glow bleeds through (image reads as suspended in the ball's
  ether, subtly pulsing with the audio‑reactive glow); `FadePupilToEye` slowed 550 ms → 1100 ms so a scene
  dissolves more gradually on standby.
- **Temp‑cache hygiene (`98e6990`)** — the `%TEMP%\Hark\vision-<guid>\` scene cache was only cleared on the
  *next* toggle‑on. Now toggle‑**off** calls `ResetVision()` (clears + deletes the dir) and a new UI‑free
  `OverlayWindow.PurgeVisionCache()` runs in `App.OnExit`, so a session's assets don't linger. `ClearVisionHistory`
  reuses `PurgeVisionCache`; the next‑run orphan sweep stays as a crash safety net.
- **The chain (`dd4e844`)** — a `topicAnchor` threads `ConceptDesigner.DesignAsync` → `VisionService.ConjureAsync`
  → `App.ConjureVisionAsync`, which now runs the **fast diagram first** and feeds its **Title** to the scene
  concept ("the diagram names it, your scene illustrates it") so the image is *about that specific topic*, not a
  tangential facet of the window.
- **FLUX‑idiomatic composer (`2a1dc3a`)** — `VisionPromptComposer.Compose` rewritten: **subject front‑loaded**,
  **all negatives purged** ("not a collage / never on black" → positive "single coherent scene set in a full,
  real environment"), the abstract **stance meta‑line dropped**, ~40–80 words. `ComposeSoftened` got the same
  purge ("nothing graphic/violent" → "serene and wholesome").

## 🧠 Decisions
- **Chain the scene to the diagram's topic** — **because** the code shows the two tiers are *independent*
  distillations of the same window (`InfographicDesigner` picks a title+nodes; `ConceptDesigner` picks one
  cinematographic subject, with **no knowledge of the diagram**), so they can anchor to different focal points.
  Chaining couples them for tight illustration (the EP27 "evocative companion" looseness was the alternative).
- **Fill a null with the red‑glow cross‑fade, keep the ether alpha** — carried from EP31; the alpha makes the
  gap‑free scenes feel part of the ball.
- **Corroborate before rewriting prompts** (user's explicit ask) — **because** guessing about FLUX is costly.
  Fetched BFL's first‑party guide (`docs.bfl.ml/guides/prompting_guide_flux2`) and it *confirmed* the clobbers
  and surfaced two under‑used levers.

## 🔬 Research findings (first‑party — BFL FLUX.2 prompting guide)
- **No negative prompts — stated twice**, with the exact fix: *"Instead of 'no blur' say 'sharp focus'; instead
  of 'no people' describe an 'empty scene'."* Our composer's two negative clauses were the textbook anti‑pattern.
- **Subject + Action + Style + Context, front‑loaded** — *"word order matters… pays more attention to what comes
  first."* Length **30–80 words** ideal.
- **Camera/lens/film‑stock specificity** ("Shot on Kodak Portra 400, 35mm" > "professional photo") — a concrete
  lever for the "cinematographer" persona (fold into the style slot; not yet applied).
- **JSON structured prompting** is BFL's recommended path for *programmatic* generation, and its base schema maps
  ~1:1 to our `VisualConcept` (scene=Concept, subjects=Motifs, style=Aesthetic, color_palette=Palette,
  mood=Theme, composition=Composition). We currently flatten away the exact structure FLUX wants → **Phase 2**.
- (Corrects an EP25 note: hex colours *do* work for **object** colours; the "hex leaks as text" case was near
  typography. Our palette stays word‑based as emotional temperature.)

## 🚧 Problems & resolutions
- **Symptom:** the Aladdin scenes felt off‑topic. → **Root cause:** image↔topic looseness is *architectural*
  (independent tiers), not a bug; the topics themselves are faithful (only ASR errors: "Burton"→"Burden"). →
  **Fix:** chain the scene to the diagram title.
- **Symptom:** scene assets persist in `%TEMP%` after toggle‑off. → **Root cause:** the cache was cleared only on
  the next toggle‑on (`ResetConversation`). → **Fix:** clear on toggle‑off + `OnExit`.
- **Symptom:** `VisionService` single‑arg `ConjureAsync` overload broke after adding `topicAnchor`. → **Root
  cause:** the new param shifted the positional `CancellationToken` into the `topicAnchor` slot (CS1503). →
  **Fix:** pass `cancellationToken:` by name.

## ✅ Verification
All builds green; extracted the 10 embedded scenes from the saved `.md` report to inspect alignment against the
diagram titles (the transcript‑level topic match was confirmed directly). Chain + composer compile together and
were pushed (`dd4e844`, `2a1dc3a`); the pupil alpha/fade (`3681b5d`) and temp hygiene (`98e6990`) shipped first.
The image‑alignment *improvement* awaits a live A/B read.

## 🔓 Open threads
- **Live A/B of the chain + FLUX composer** — do the scenes now visibly illustrate the diagram's titled topic,
  and do the FLUX‑idiomatic prompts read cleaner? Tune if not.
- **Phase 2 — FLUX JSON structured prompt.** Emit the `VisualConcept` as FLUX JSON (BFL "maximum control"), and
  optionally add camera/lens specificity. FLUX‑specific → needs a provider split (gpt‑image keeps prose). Do only
  if the natural‑language composer isn't tight enough.
- Carried: the **native on‑topic pupil fill** (EP31), the **fairy‑tale content‑filter** test (EP29).
