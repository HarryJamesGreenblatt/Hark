# 🎬 Episode 22 — HARK 1.0.0: The Milestone, and the Interim-Visual Dead-End

> **Date:** 2026-08-28 · **Branch:** `main` · **Commits:** `03092db..8297cd6` · **Tag:** `v1.0.0` (→ `8297cd6`)
> **One-liner:** Cut **HARK 1.0.0** — the first non-`0.1.x` release — after biasing the Oracle's concept
> tier toward **literal, on-topic** scenes. The headline *engineering* lesson, though, is a **dead-end**:
> the attempt to make the render dead-time buffer's **scrying sheen run on every conjure** regressed
> (in the autonomous loop, conjuring is near-continuous, so the sheen was **always on** — masking the
> pupil swell and reading as "it never renders") and was **reverted before commit**. The interim-visual
> objective — *convey the meaning faster while the slow image renders* — remains **unmet and open**.

## 🎯 Intent
Two threads from the tail of EP21: (1) the images were still *"too abstract / off topic"*, and (2) the
scrying interim *"only happens one time at the beginning… then the images queue up just as slowly."*
User then called it: *"scale back and try again a different way"* for the interim, and *"it's time for
release 1.0.0."* So this session = one shippable concept fix + the 1.0.0 milestone, with the interim
visual deliberately parked for a rethink.

## 🛠️ What changed
- **Concept tier → literal, on-topic (`Hark.Oracle/Vision/ConceptDesigner.cs`, `8297cd6`)** — the Oracle
  now **DEFAULTS TO THE LITERAL** (depict the actual subject/people/place so a viewer instantly
  recognizes the beat; be metaphorical only when the beat itself is abstract), reserves **CONTRAST**
  (visual irony) for the *rare* genuinely-ironic beat "never as an excuse to go abstract or off-topic",
  softens the anti-repetition steer to *"do not change subject just to differ"*, and drops the
  temperature **0.9 → 0.7**. Targets the "chair reading a book / person holding pottery" absurdity.
- **Release 1.0.0 (`v1.0.0` tag → `8297cd6`)** — annotated tag pushed; the `on: push: tags: ['v*']`
  `Release` workflow (run `33156910904`, **success**) published
  [v1.0.0](https://github.com/HarryJamesGreenblatt/Hark/releases/tag/v1.0.0) via `github-actions[bot]`
  with a single **`Hark-Setup.zip`** asset (≈120 MB) — consistent with the whole `v0.1.x` line.

## 🧠 Decisions
- **Ship 1.0.0 now, with the interim visual explicitly unfinished** — **because** the core product
  (captions · diarization · named speakers · recaps · mic mixing · the sound-reactive Vision eye) is
  solid and demoed; the interim-visual polish is an open research question, not a release blocker. Better
  to plant the 1.0.0 flag and iterate the Vision interim in point releases.
- **Bias the concept to literal rather than re-tune the prompt composer** — **because** the abstraction
  came from the *concept* stage (over-metaphor + CONTRAST + "be different" steer), not the render stage.
  Fix it at the source; validate live before touching `VisionPromptComposer` again.
- **Revert the "scry on every conjure" experiment rather than tune it** — **because** it was
  *structurally* wrong for the autonomous loop, not a constant to nudge (see below). A revert, not a
  knob, was the honest move — matching the standing "we'll revert if this doesn't work" discipline.

## 🚧 Problems & resolutions
- **Symptom:** making the scrying sheen fire on **every** conjure made it *"never render an image and
  always just do the animation,"* and the **pupil swell disappeared**. → **Root cause:** in the
  autonomous beat loop a render takes ~1 min while beats fire every few seconds, so "conjuring" is
  **near-continuous** — the sheen was effectively **always on**, sweeping over the held image and
  visually **masking** the pupil dilation. It also fought the goal: an abstract rotating sheen conveys
  *"working"*, not *the meaning of the current topic*. → **Fix:** **reverted** the change in the working
  tree (back to `03092db`'s **first-open-only** buffer; pupil swell intact; images render/hold) **before
  any commit** — so `main` never carried the regression. Kept only the unrelated concept literal-bias fix.
- **Symptom (self-inflicted):** flagged a release **asset "discrepancy"** — that 1.0.0 shipped only
  `Hark-Setup.zip` and dropped a portable `…-win-x64.zip`. → **Root cause:** conflated the **sibling
  WavBall** repo's README (which *does* ship `WavBall-win-x64.zip`) with HARK. Verified via the GitHub
  MCP: **v0.1.4 and v1.0.0 both ship only `Hark-Setup.zip`** — no discrepancy. Lesson: check the actual
  release assets, don't pattern-match across sibling repos.

## ✅ Verification
- `Release` run `33156910904` = **success**; [v1.0.0](https://github.com/HarryJamesGreenblatt/Hark/releases/tag/v1.0.0)
  published (not draft/prerelease), asset `Hark-Setup.zip` (`sha256:38fd2ea7…`), changelog v0.1.4→v1.0.0.
- Post-revert overlay confirmed back to known-good: sheen + concept buffer **only on a blank first open**,
  pupil swells again, autonomous beats hold the previous image until the new one lands.
- The concept literal-bias fix builds green (shipped in `8297cd6`) but is **not yet live-validated** —
  first real read of whether renders land on-topic is the next test.

## 🔓 Open threads
- **Interim visual — STILL OPEN (objective unmet), superseding EP21's "shipped" note.** What ships in
  1.0.0: a **first-open-only** concept-text buffer + scrying sheen. What's *unmet*: an interim that
  **conveys the current topic's meaning** during **every** render's ~1 min wait (autonomous beats hold
  the prior image with no interim; a rotating sheen isn't semantic). User is rethinking *"a different
  way"* — candidates to explore: a fast **low-res/`low`-quality first pass** then refine; a cheap
  **text-to-simple-graphic** (icon/emoji/CSS motif) from the concept; keep the concept **caption visible
  under** the held image so it at least tracks topic in words. Not a knob — needs a new approach.
- **Image quality / relevance — first lever pulled, needs live proof.** The concept tier now defaults to
  literal (`8297cd6`); confirm on a real conversation whether "absurd / off-topic" renders subside before
  touching `VisionPromptComposer`.
- **Dead API:** `OverlayWindow.SetAudioLevel` + `HarkSession.AudioLevel` remain unused after the
  `AudioFeatures` switch — keep for CLI/compat or prune.
- **Post-1.0.0 hygiene:** `SetHighDpiMode`/manifest DPI warning; the two CS4014 fire-and-forget conjure
  warnings; consider a real code-signing cert (Azure Trusted Signing) so `Hark-Setup.exe` stops needing
  the zip-to-dodge-SmartScreen dance.
