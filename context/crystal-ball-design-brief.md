# The Crystal Ball as an AI-Directed Ambient Display

> **Naming note (2026-09-02):** the "crystal ball" is now called **the Oracle** — see
> [`oracle.md`](./oracle.md) for the canonical identity. This brief is kept for its **research** (the
> calm-tech / procedural-oatmeal framing), which still informs the Vision; only the name is dated.
>
> A design brief for HARK's Vision feature, grounded in game-dev, HCI, and procedural-generation
> research. Written 2026-08-29 after an impasse: chasing *low-latency generative images synced to live
> speech* as an engineering race, and losing.
>
> **Outcome (EP25, 2026-08-30) — how this resolved:** the diagnosis here (it's an *ambient* display, not
> a latency race; the "laptop-loop" is *procedural oatmeal*) held up, but the proposed *mechanisms* did
> not survive contact. **Rejected:** the L4D-style "AI Director" (HOLD/EVOLVE/CUT) and cross-dissolve /
> predict-and-morph — read as decorative and a poor fit for an unattended WPF app; and multimodal
> anti-repetition (spike-proven to vary only *treatment*, not subject). **What actually worked** was a
> different cut of §4 (content-adaptive class): the oatmeal wasn't beaten by pacing or prediction but by
> recognising that the didactic layer is a **diagram = structured data**, which should be **drawn natively**
> (exact, crisp, instant) rather than generated — freeing the generative model for the **pictorial** pupil
> scene it's actually good at. Vision shipped as a **dual layer** (native mind-map + FLUX scene, parallel).
> See [`storyline/EP25`](./storyline/EP25-vision-native-diagrams-and-dual-layer-crystal-ball.md). The
> ambient stance (§3) and the oatmeal diagnosis (§4) remain the durable takeaways.

## The reframe (the whole point)

We've been treating the crystal ball as a **latency race** — "generate an image fast enough to track
what's being said." Every lever pulled (faster model, more capacity, tighter cadence) is an attempt to
*win that race*. The disciplines that solve exactly this class of problem — real-time games, netcode,
ambient computing — almost never win the race. They **hide it, predict it, or redefine it** so the race
stops mattering. HARK's problem isn't "the model is too slow" (it renders in ~8s); it's that we framed a
**perception/design** problem as an **engineering** one.

Four patterns from the literature, each mapped to HARK.

---

## 1. The AI Director — the Oracle should *direct*, not *react*

**Source:** Valve, *Left 4 Dead* "AI Director" (Mike Booth). An AI that watches player state and controls
**pacing, tension, and placement** to shape an experience — Gabe Newell called it *"procedural narrative…
more of a story-telling device than a simple difficulty mechanism"* (Edge, 2008). Notably, L4D ran a
**second, separate "Music Director"** alongside it that *"tracks the player's experience"* and keeps the
score *"appropriate to each player's situation"* (Tim Larkin, dev commentary).

**The insight:** a director decides **when to hold, when to evolve, and when to cut** — it doesn't fire on
every event. Our Oracle is currently a *reflex*: transcript beat → render. A director watches the
conversation's arc and **paces** the visuals to it.

**HARK mapping:** promote the Vision beat loop from "render on cadence" to a **Director** with an explicit
policy — HOLD (topic stable → keep the current image alive), EVOLVE (same topic, new facet → morph), CUT
(genuine topic shift → new scene). Consider L4D's **two-director split**: one director for *what* the
image is, a lighter one for *mood/pacing* (brightness, motion, dwell) — the mood one can react fast and
cheap while the image one takes its time.

## 2. Latency hiding — predict, interpolate, never hard-cut

**Source:** Glenn Fiedler, *Networked Physics* (gafferongames.com); **client-side prediction** from
QuakeWorld. Multiplayer games can't wait for the server round-trip, so the client **predicts ahead
locally** and, when the truth arrives, **smooths toward it** — *"move 10% of the distance… an
exponentially smoothed moving average"* — rather than snapping.

**The insight:** don't wait for the "correct" next image; show a **prediction** and **interpolate** toward
the truth. Hard cuts expose latency; continuous correction hides it.

**HARK mapping:**
- **Speculative pre-render.** While the current image is up, the Director predicts the likely next beat and
  **pre-generates** it, so on a real shift the image is already in hand (turns 8s into ~0s *perceived*).
- **Cross-dissolve, never cut.** Always **morph** between images (slow dissolve) so the surface is
  perpetually *becoming* — the demoscene/VJ move. Cadence becomes invisible; an 8s render hides inside a
  10s dissolve.
- **Cheap-now, refine-later.** We already do a weak version (the concept-text scrying buffer). The stronger
  version: an immediate cheap visual (tint/shimmer/abstract field seeded by the concept) that the real
  image resolves *into*.

## 3. Calm technology — it's *ambient*, so it needn't be real-time

**Source:** Amber Case, *Principles of Calm Technology* (calmtech.com), building on Weiser & Brown, *The
Coming Age of Calm Technology* (1996). Key principles: **make use of the periphery** (*"move easily from
the periphery of attention to the center, and back… informing without overburdening"*); **require the
smallest possible amount of attention**; and **"work even when it fails"** (*default to a usable state*).

**The insight:** an ambient, peripheral display is **not supposed to track literally in real time.** The
crystal ball is glanceable mood, not a focal live tracker. Holding it to a real-time standard is a
**category error** — and it's partly why the latency felt fatal and the repetition felt so glaring.

**HARK mapping:**
- **Adopt the ambient stance explicitly.** The eye lives at the edge of attention; "roughly right, slowly
  changing, always alive" beats "precisely synced." This *dissolves* most of the latency requirement.
- **Graceful failure = a designed idle state.** When a render is slow/failing, the orb should rest in an
  evocative *becoming* state (breathing light, slow drift), not a frozen literal image or a blank. "Work
  even when it fails" is a spec, not an afterthought.

## 4. "Procedural oatmeal" — the laptop-loop has a name (and a fix)

**Source:** Kate Compton's **"procedural oatmeal"** (Game Developer, 2016): you can generate *thousands of
bowls of oatmeal*, but they're *perceived as the same* — generation must aim for **perceived uniqueness**,
not mere combinatorial variety. And Shaker, Togelius & Nelson, *Procedural Content Generation in Games*
(pcgbook.com), on the standing **align-vs-vary** tension; recent PCG-via-ML work notes **"diversity
sampling consistently increases the number of generated solutions"** (Zakaria et al.).

**The insight:** the repeated "laptop at a desk" on the FreeCodeCamp video **is procedural oatmeal** —
faithful to a single-topic source, but with no *perceived uniqueness*. This is a **known** PCG failure,
and the levers are known too: design for perceived uniqueness, sample for diversity, and accept that
*literal alignment* and *variety* trade off (our EP22 literal-bias fix pushed us hard toward alignment).

**HARK mapping:**
- **Vary the *framing* on a stable topic.** When the Director detects HOLD/EVOLVE (topic unchanged), steer
  the concept to a **different lens** — angle, metaphor, moment, scale — not the same literal scene.
  Trade a little literal fidelity for perceived uniqueness *on purpose*, only when the topic is stable.
- **Content-adaptive visual class.** Detect the *kind* of passage (narrative/emotional vs.
  explanatory/technical) and pick the visual accordingly — a cinematic scene vs. a schematic/diagram. The
  FreeCodeCamp case is the explanatory kind, where each new concept becomes a *different* diagram — which
  is both more useful *and* self-diversifying. (This is where FLUX's strengths would finally earn out.)

---

## Synthesis: "HAL as an AI-directed ambient crystal ball"

Put together, the target isn't "a live image generator." It's an **ambient display driven by a director
that predicts and interpolates**:

1. **Director, not reflex** — a policy of HOLD / EVOLVE / CUT paced to conversational beats (§1).
2. **Predict + morph** — speculatively pre-render the next beat; cross-dissolve always; cheap-now →
   refine-later (§2).
3. **Ambient stance** — peripheral, glanceable, "always becoming," graceful on failure; drop the
   real-time-literal standard (§3).
4. **Perceived uniqueness** — vary framing on stable topics; adapt the visual *class* to the content type,
   which kills the oatmeal loop and is where FLUX's diagram strength applies (§4).

## What this changes in the code (mapped to current components)

- `App.OnVisionAutoTick` / the beat loop → a **Director** with an explicit HOLD/EVOLVE/CUT policy and a
  **speculative pre-render** of the predicted next beat.
- `VisionRenderer` / `OverlayWindow` → **cross-dissolve** transitions (morph, never cut) + a designed
  **idle/becoming** state for graceful failure.
- `ConceptDesigner` → a **framing-variety steer** when the topic is stable, and a **content-type detector**
  that selects cinematic-scene vs. schematic-diagram intent (the latter on FLUX).
- Cadence constants → reframed as **director timing** (dwell, dissolve length) rather than a race to
  minimize latency.

## Sources

- Valve / Turtle Rock, **AI Director**, *Left 4 Dead* (2008) — Mike Booth; Gabe Newell, *Edge* (Nov 2008);
  Tim Larkin, in-game developer commentary (Music Director).
- Glenn Fiedler, **"Networked Physics"** & client-side prediction (gafferongames.com, 2004); technique from
  QuakeWorld.
- Amber Case, **"Principles of Calm Technology"** (calmtech.com), after Mark Weiser & John Seely Brown,
  **"The Coming Age of Calm Technology"** (1996).
- Kate Compton, **"procedural oatmeal"** (*Game Developer*, 2016); Noor Shaker, Julian Togelius, Mark J.
  Nelson, **_Procedural Content Generation in Games_** (Springer, 2016, pcgbook.com).
