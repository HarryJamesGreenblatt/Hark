# 🎬 Episode 15 — The Oracle Spike: Vision's Concept Judgment, Native and Proven

> **Date:** 2026-08-23 · **Branch:** `main` · **Commits:** `629b38d`
> **One-liner:** After a long design thread that pinned down Cristóbal's true form, scaffolded
> `Hark.Oracle` / `Hark.Oracle.Vision` — a **two-tier** augmentation service (art-director **Concept**
> → gpt-image **Render**) whose judgment is **distilled natively** from sequitur's film-craft grounding
> (no Python, no runtime coupling) — and **proved the Concept judgment live**: `UNDERSCORE` on a
> nostalgia window, `CONTRAST` (visual irony) on an ironic one.

## 🎯 Intent
Turn the [Codename Cristóbal](../cristobal-vision.md) north star into a first buildable increment —
but only after resolving what it actually *is*. The thread worked through: is Cristóbal an API / MCP /
web app / agent / Office add-in? Is it inside HARK or standalone? Is the crystal ball HAL or a rival
identity? What does it truly need from the sibling `sequitur_studios`? The conclusion: build the
**Oracle spike** (Vision is the Oracle's *augmentation* service; Cristóbal-the-product is downstream),
reproduce sequitur's Production-Designer judgment **natively**, and prove the art-director judgment
before spending on image infra.

## 🛠️ What changed
- `Hark.Oracle/Vision/VisualConcept.cs` (new) — the record + `ConceptStance` (Underscore/Contrast).
  Fields: `Theme`, `Concept`, `Stance`, `StanceReason`, `Motifs`, `Composition`, `Aesthetic`, `Palette`.
- `Hark.Oracle/Vision/ConceptDesigner.cs` (new) — the **Concept** tier: an art-director persona over the
  Azure OpenAI chat deployment (strict JSON schema, keyless), distilling a window of dialogue into one
  iconic `VisualConcept`.
- `Hark.Oracle/Vision/VisionPromptComposer.cs` (new) — the deterministic **Render** composer
  (`VisualConcept` → gpt-image prompt), the native analog of sequitur's `build_poster_prompt`, including
  the **anti-literal counter** ("a real scene in the world — not a crystal ball, glass sphere, screen…").
- `Hark.Oracle/Vision/VisionRenderer.cs` (new) — the **Render** backend: a keyless Azure OpenAI
  `ImageClient` wrapper (native analog of `ImageStudio`); optional so the Concept tier runs before any
  image deployment exists.
- `Hark.Oracle/Vision/VisionService.cs` (new) — orchestrates Concept → Compose → (optional) Render,
  returning `VisionResult(Concept, Prompt, Image?)`.
- `Hark.Oracle.Spike/` (new) — a console harness that feeds a conversation window (default or a file
  arg) and prints the `VisualConcept` + composed prompt. Reuses Hark.App's config precedence
  (env → `%APPDATA%\Hark\config.json` → user-secrets via the shared `UserSecretsId`).
- `Hark.slnx` — added both projects.

## 🧠 Decisions
- **This is the Oracle spike, not the Cristóbal spike** — **because** Vision is the Oracle's
  *augmentation* service; Cristóbal is the downstream product that *hosts* it. Naming the work honestly
  keeps the layer boundaries clean.
- **Form: the crystal ball is a *mode of HARK*, and HAL is its eye** — **because** the natural UX is the
  HAL eye **dilating** into a full-window canvas. That resolves the identity clash: **HAL is the eye
  (the display/face); Cristóbal is the mind (the interpreting oracle).** They're anatomy vs. faculty,
  not rivals — "HAL's eye rendering Cristóbal's vision." So there is no user-facing "Cristóbal"; the
  code name lives on the engine, never on the glass.
- **Package layering: `Hark.Core` (ear) → `Hark.Oracle` (mind) → `Hark.Oracle.Vision` (render)** —
  **because** the Oracle *consumes* the transcript, so it's a layer **on top of** the engine (a sibling
  that depends on Core), not `Hark.Core.Oracle` (which would drag studio-specific rendering into the
  clean engine). The extra reference is the seam that keeps the engine reusable — and it's transitive,
  so a consumer still adds one dependency at the point of use.
- **Reproduce sequitur natively — don't reference it** — **because** cherry-picking one Python slice
  would add Python deps and couple HARK to a repo under active development (package drift). Cristóbal
  needs only two things from sequitur — the Production-Designer *judgment* and the `gpt-image` render —
  and both are native: a structured chat call (the pattern `AzureOpenAiSummarizer` already uses) and the
  Azure OpenAI `ImageClient` already in the dependency tree.
- **Distill-and-bake the grounding; no runtime feed** — **because** the film-craft grounding is a
  *design-time* input to authoring the prompt, not a runtime input. The reference chapters were read
  once (at authoring time) and crystallized into the baked prompt; the running Oracle makes **zero**
  GitHub/HTTP calls. Provenance is a comment (pinned commit), not a dependency. An unpinned live feed
  would *reintroduce* the very drift we're avoiding.
- **The grounding is the crown jewel, and it's safe to port** — **because** sequitur's `reference/`
  chapters are **transformative abridgments** (the verbatim `source/` is gitignored; only `reference/`
  ships). The judgment was distilled from Rizzo *Art Direction Handbook* Ch. 4 (one central visual
  concept) + Glebas *Directing the Story* Ch. 7/9/10/11/13 (direct the eye · make images speak · convey
  meaning · dramatic irony · aim for the heart), plus the `Contribution` vocabulary — pinned @ `4150645`.
- **Vision is two-tier (Concept persona + Render composer)** — **because** Rizzo's own split (Ch. 1)
  is concept vs. realisation: the persona owns the `visual_concept`; the deterministic composer +
  `ImageClient` own the picture. The Render composer carries the **anti-literal counter** and explicit
  **composition-as-subtext** (Glebas Ch. 7: one focal point, contrast, negative space) so images *read*.
- **Spike scope = generate-fresh per beat; morph & topic-awareness deferred** — **because** the
  "slow-dissolve" is a *conditioning* mechanism (each frame generated *from* the previous via the edits
  endpoint), and *when* to morph vs. cut is a topic-boundary decision. Detecting the boundary is the
  Oracle's live beat-intelligence (Stage 2); mapping it to cut-vs-morph is Cristóbal's policy. Both are
  omitted from the spike, whose only job is to prove the Concept judgment. The Render call is shaped so a
  future `previousFrame` conditioning drops in without redesign.

## ✅ Verification
- `dotnet build` on both new projects → green, 0 warnings; the image SDK surface
  (`GetImageClient`/`GenerateImageAsync`/`ImageBytes`) resolved, so the Render tier is valid (untested
  for pixels — no image deployment yet).
- **Concept judgment proven live** against the `gpt-4.1-mini` chat deployment, across both stances:
  - **`UNDERSCORE`** — a nostalgia window (a childhood home, a painted-over blue door) →
    *concept:* "A lone, small blue door fading into a vast washed-out wall under a setting sun";
    *motifs* include "faint shadows of a childlike figure"; soft-watercolor; muted warm with a cool-blue
    accent. Iconic, not literal — it did **not** draw people talking or a house.
  - **`CONTRAST`** — an ironic window (grim results waved off with forced birthday cheer) →
    *stance* correctly `Contrast`; *concept:* "A single wilting candle flickering in a dimly lit room
    filled with vibrant party decorations"; *chiaroscuro oil painting*; warm hues against cool shadows.
    A genuine visual irony.
  - Both composed prompts assembled correctly, including the anti-literal counter.

## 🚧 Problems & resolutions
- **Symptom:** a PowerShell here-string for the test sample mis-parsed (stray `'`). → **Fix:** wrote the
  sample via a string-array `Set-Content`; the shell was never actually stuck.
- **Non-issue flagged & cleared:** the committed `UserSecretsId` is a **non-secret pointer** (already in
  `Hark.App.csproj`); the values live outside the repo, auth is keyless. No endpoint/deployment/keys are
  hardcoded anywhere in the scaffold.
- **Cosmetic:** em-dashes render as `ΓÇö` in the default console code page — data is fine.

## 🔓 Open threads
- **Provision `gpt-image-1`** — a small infra step (Bicep + an image deployment on the AOAI resource) to
  exercise `VisionRenderer` and see real pixels. Concept works without it; Render is untested until then.
- **Host the spike in `Hark.App` as the HAL-eye Vision mode** — click the eye → it scales to a
  full-window canvas rendering the concept; the portable `Hark.Oracle.Vision` logic drives it.
- **Live beat-intelligence (Stage 2)** — the debounced trigger that also emits *topic structure* (new
  topic vs. new thread), the enabling signal for morph-vs-cut. Guard against O(n²) token cost (window +
  running profile, not full-transcript resend).
- **Conditioning-morph** — generate each frame *from* the previous (edits endpoint) for a true dissolve;
  add a drift knob so a big theme change isn't held back by the anchor.
- **Cut-vs-morph policy (Cristóbal layer)** — map the Oracle's topic-boundary signal to fresh (cut) vs.
  conditioned (morph).
- Carried: diarization Fork A / fidelity, the engine boundary, and the standing threads in `STORYLINE.md`.
