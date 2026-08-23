# 🔭 Codename Cristóbal — the visualization seam (design north star)

> **Status:** vision / design (no HARK code yet). Captured 2026-08-22 (during Episode 13's tail).
> **One-liner:** a *separate* project where an agent dispatches a **generative image model** to
> conjure live **didactic visualizations** of what HARK is capturing — with the key realization that
> **HARK's summaries are the wrong seed**, and the right seed is an **art-director refine** that already
> exists, grounded, in the sibling project **`sequitur_studios`**.

## What Cristóbal is
A project that hooks into HARK's engine so an **agent dispatches a generative image model** to
visualize the *essence* of an ongoing conversation — a live "crystal ball" (e.g. for a headset /
presentation). HARK is the **ear**; Cristóbal renders the **eye**.

## The core insight (why summaries don't work as the seed)
Even good recaps are the **wrong artifact** to seed an image prompt:
- **Literalism** — a summary phrase like "following your dreams" makes the image model draw someone
  *literally chasing a figure*.
- **Over-complication** — the nested topic/detail structure becomes a cluttered collage.

The right output conveys the **feeling/theme**, not the content: for "a rosy, ambitious story about
chasing a dream," not a literal chase but *"a doe-eyed kid flying a kite alone in a grassy knoll at
golden hour."* That is **editorial / conceptual illustration** — one evocative metaphor + a mood, not
a literal scene.

## The missing stage = an art-director refine (and sequitur already has it)
The needed partner model does **semantic → visual-intent translation with deliberate abstraction** —
an **art director**, not a note-taker. **`sequitur_studios` already implements exactly this**, grounded:

- **`.github/agents/production_designer.agent.md`** — the Production Designer lands **one central
  `visual_concept`**: "one evocative sentence, **iconic not literal** — 'the city as a rain-streaked
  maze'," a "single deliberate image, **not a mood board**" (grounded in Rizzo's *The Art Direction
  Handbook*, Ch.4; Ch.5 "design for the camera, not literal reality"). Its typed `Contribution`:
  `visual_concept` (free text) · `concept_stance` (**UNDERSCORE** echoes the emotion / **CONTRAST**
  pushes against it) · `medium_look` · `era` · `set_kind` · `motifs` (2–4 that read as **one strong
  image, not a cluttered collage**).
- **`.github/skills/keyartist/SKILL.md`** + `compose_key_art.py` — composes a one-sheet from the PD's
  concept (poster archetype + type). Cristóbal likely wants the PD's `visual_concept`, not the poster
  layer, but the "single strong image / anti-clutter" discipline applies.
- **Render path:** sequitur's model-agnostic grammar → `build_prompt` + `ImageStudio` → **Azure
  `gpt-image-1`** still backend (`sequitur/prompt.py`, `sequitur/image.py`). Grounding also in
  *Directing the Story* Ch.9 "how to make images speak" / Ch.10 "convey and suggest meaning".

**`concept_stance` is the gem to reuse:** UNDERSCORE = kite at golden hour (echoes hope); CONTRAST =
same kid, tangled string, overcast (quiet irony). A deliberate expressive choice, not just illustration.

Adopt sequitur's proven `Contribution` vocabulary instead of inventing a parallel `VisualConcept`.

## The seam: Cristóbal = HARK (source) ⨝ sequitur (studio)
sequitur's premise is authored (a Screenwriter's scene); HARK's premise is **derived live from speech
and evolves**. The only genuinely new piece is the **beat detector**:

```
HARK engine ─(GroundingEvent: a debounced thematic "beat")→ a Brief (premise + mood)
     │  (the beat detector = the one new component:                    │
     │   rolling transcript → evolving premise/mood)                   ▼
     │                                       sequitur Production Designer → visual_concept (+ stance/motifs)
     │                                                                  │
     │                                    build_prompt + ImageStudio (Azure gpt-image-1)
     ▼                                                                  │
  HARK/Cristóbal "crystal ball"  ◀──────────────── image ──────────────┘
```

Everything downstream of the `Brief` already exists in sequitur. Cristóbal adds the ear (HARK) + the
beat detector + the **async-supersession cadence**: each new beat lands a new `visual_concept` that
**clobbers** the last (via HARK's projection `Revision` key) → the image *slow-dissolves* as the theme
develops, rather than flickering per sentence.

## How it rides HARK's engine road (see STORYLINE → engine boundary)
- It is the **augmentation** half of the oracle layer: a `GroundingEvent(… SuggestedVisual)` **producer**;
  Cristóbal's image agent is a `GroundingEvent` **consumer**.
- **Enabling spine = Phases 1–2** of the engine road: the typed `HarkEvent` stream + `ConversationStore`
  as a materialized projection in `Hark.Core` (`Revision` = supersession key). Build those first; this
  plugs in as a subscriber.
- Distinct from **fidelity** refinement (the diarization `RefinementEvent` that makes the transcript
  *right*) — this is an orthogonal **interpretation-for-visualization** refine that makes it *evocative*.
  Both ride the same event spine.

## Constraints to decide early (carried from the grounding-oracle design)
- **Debounce on thematic beats** (topic/emotional shift, clause boundaries) — not per word.
- **Confidence-gate**, and keep "identified/retrieved fact" separate from "generated illustration".
- **Privacy posture:** "researching/visualizing the stream" means content **leaves the box** — a
  deliberate constraint for the headset/presentation use case.

## Integration reality
`sequitur_studios` is **Python/Azure** (`pip`, `from sequitur import Studio/Engine/Brief`, Key Vault +
`az login`, Azure `gpt-image-1`); HARK is **.NET**. Clean seam = HARK emits `Brief`s and a bridge
invokes sequitur's plan+render, returning an image path/URL to HARK's consumer — keep the grounded
art-director where it already lives (roles → grounding → code intact); HARK becomes "another
front-of-house source" for the studio.

## Pointers
- Repo: `github.com/HarryJamesGreenblatt/sequitur_studios` (public). Key files:
  `.github/agents/production_designer.agent.md`, `.github/skills/keyartist/SKILL.md`,
  `sequitur/crew/production_design.py`, `sequitur/prompt.py`, `sequitur/image.py`,
  `artifacts/the art direction handbook for tv and film/`, `artifacts/directing the story/` (Ch.9–10).
- HARK side: `EP09` (engine-boundary + grounding-oracle design), STORYLINE → _Open threads_ (engine
  boundary; this doc).
