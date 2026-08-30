# 🎬 Episode 25 — The Diagram Is Data: Native Rendering & the Dual-Layer Crystal Ball

> **Date:** 2026-08-30 · **Branch:** `main` · **Commit:** `99ccbf9` (dual-layer Vision)
> **One-liner:** After a long research-driven refactor of the Vision feature, landed the realization that
> **a diagram is structured data, not a picture** — so HARK now **draws** the didactic diagram **natively
> in WPF** (exact eye-hub, crisp text, instant) while the **generative model fills the pupil** with the
> cinematographic scene it's actually good at: a **dual-layer crystal ball**, both classes conjured in
> parallel every beat, no config flag.

## 🎯 Intent
User opened with *"as evidenced by the /storyline, we're approaching the vision feature wrong and need to
refactor — please catch up as to advise."* The session was a long Socratic diagnosis: reject the
latency-race framing, find **who has actually solved this class of problem and how**, and rebuild.
User's pivotal reframe: *"I shouldn't be asking 'was the problem solved this way' and match to that, but
rather 'has this problem been solved before and if so by whom and how?'"* Later: *"it has still yet to
produce even one infographic… we designed this initially with image-1 and never realigned it for the
other models through research of their respective apis."* Then the build, ending: *"dispense of the mode
config and secret and make it so we can use both."*

## 🛠️ What changed
- **New diagram class (`Hark.Oracle/Vision/`, `99ccbf9`)** — `InfographicConcept` (a **structured**
  `Title` + up to 5 `InfographicNode{Label, Color}`), `InfographicDesigner` (a persona → that concept as
  strict-schema JSON; colour enum blue/green/orange/purple/red), and `InfographicPromptComposer` (a
  FLUX-idiomatic radial-diagram prompt — **now used only by the Spike**, superseded in the app by native
  rendering). `VisionService` gained `ConjureDiagramAsync` (concept-only, **no image model**).
- **Native WPF diagram renderer (`Hark.App/OverlayWindow.xaml`+`.cs`, `99ccbf9`)** — a `DiagramLayer`
  behind the eye; `SetVisionDiagram(InfographicConcept)` draws a **radial mind-map**: a title, a faint
  ring, and colour-coded node pills spaced by trig **around the canvas centre = the eye centre** (so the
  hub is **always exactly concentric**), each joined by a connector the eye occludes. 600 ms crossfade
  between diagrams. The eye was re-centred on the canvas (caption moved to the bottom) so it no longer
  offsets the hub.
- **Dual render, flag removed (`Hark.App/App.xaml.cs`, `99ccbf9`)** — dropped `HARK_AOAI_IMAGE_MODE`
  (field, config read, `DiagramMode`, the user-secret). `BuildVisionService` always builds concept +
  diagram + render tiers. `ConjureVisionAsync` now runs **both classes in parallel** from the same window
  (`Task.WhenAll` over `ConjureDiagramAsync` → native backdrop and `ConjureAsync` → FLUX pupil image),
  each landing independently; caches both for reopen-unchanged.
- **Spike `infographic` mode (`Hark.Oracle.Spike/Program.cs`, `99ccbf9`)** — renders a FLUX-idiomatic
  diagram (the capability probe + latency check) bypassing the concept pipeline.

## 🧠 Decisions
- **A diagram is STRUCTURED DATA — draw it natively; don't generate it with an image model** — **because**
  three rounds of tuning (centre the eye, redesign the FLUX prompt to radial) all failed the same way:
  the eye was **misaligned** (FLUX places the empty centre as part of a picture and it **wanders** every
  render — you can't align a fixed eye to a generated hole), every diagram looked **structurally
  identical** (one FLUX template), and text **garbled**. Native rendering fixes all four at once — exact
  hub, crisp text, free structural variety, **instant** (no 8 s render), and clean over the translucent
  overlay (no opaque square). This is the "stop tuning, change the model" lesson applied to a whole
  feature.
- **Dual-layer, both every beat, no flag** — **because** we were using the image model for the one job
  (structured diagrams) it's **worst** at and leaving the pupil — its **best** job (photographic imagery)
  — empty. Split by strength: **native diagram backdrop + generative scene pupil**, conjured in parallel.
- **Realign the render tier to the model's OWN API, not gpt-image-1's** — **because** the pipeline was
  built for gpt-image-1 and never realigned for FLUX. Per BFL's FLUX.2 prompting guide (`docs.bfl.ml`):
  **no negative prompts** (FLUX ignores them — our composer's "not a diagram/text/interface" was dead or
  harmful), front-loaded `Subject+Style+Context`, **colour words not hex** (FLUX renders hex strings as
  visible text), and first-class **typography + JSON** — which is *why* FLUX could make a clean
  infographic once asked correctly. (The scene tier still benefits; the diagram tier went native.)
- **Reproduce judgment, not the shell — one level deeper** — **because** the same EP15 principle
  (native judgment, drop the orchestration) now applies to the whole crew, not just the Production
  Designer. But the LLM crew/Director orchestration belongs to a chat app, not an unattended overlay.
- **Research by driving a real browser** — **because** there is **no web-search MCP** configured (user
  `mcp.json` was empty; declined a 3rd-party DDG server). Browser automation reads the rendered DOM,
  unlike `fetch_webpage` (which returned only chrome). Used it to survey the VJ / Deforum / calm-tech /
  PCG literature and the BFL API docs.

## 🚧 Problems & resolutions
- **Symptom:** on a single-topic stream the images kept showing the **same "laptop at a desk."** →
  **Root cause:** *procedural oatmeal* — a faithful read of a single-topic source has no
  perceived-uniqueness lever. → **Explored & rejected** (with a spike): **multimodal anti-repetition**
  (feed gpt-4.1-mini its own prior FRAMES, tell it to differ) — proven to diversify only **treatment**
  (light/palette), never the **subject**, at +2.4 s latency. The EP18/EP22 ceiling, confirmed by A/B/C.
- **Symptom:** FLUX "never made an infographic." → **Root cause:** the pipeline **forbade** diagrams at
  two layers (`ConceptDesigner` + composer negative clause) **and** called FLUX with gpt-image grammar.
  → **Fix (this episode):** a dedicated diagram class; and ultimately native rendering.
- **Symptom:** first FLUX infographics were garbled — **hex codes leaked as label text**, code mangled. →
  **Fix:** colour words (not hex), one focal line, ≤3 labels (proven in the Spike) — then rendered moot by
  going native.
- **Symptom:** live, the diagram **eye was misaligned**, **every graphic looked the same**, and the
  opaque square **clashed** with the translucent overlay — and **persisted through tuning**. → **Root
  cause:** all three are structural consequences of generating a diagram as a picture. → **Fix:** the
  native-rendering pivot.
- **Symptom (build):** `FontFamily` / `Size` **ambiguous**, `HorizontalAlignment.Center` "cannot be
  accessed with an instance reference." → **Root cause:** `Hark.App` references **System.Drawing**
  (WinForms tray) so WPF types collide, and the alignment *enum* is shadowed by the `FrameworkElement`
  *property*. → **Fix:** `using Size =`/`FontFamily =` aliases + fully-qualified
  `System.Windows.HorizontalAlignment.Center`. **(Grep signature: `CS0104` / `CS0176` in OverlayWindow.)**

## ✅ Verification
- **Spike renders** — a hand-authored FLUX infographic proved FLUX *can* render a clean diagram (code +
  labels legible once hex was dropped); the radial-composer sample rendered a clean 5-node ring with a
  centre sized for the eye. **Latency:** FLUX ~8–10 s; native diagram **instant**.
- **Live runs** — the dual crystal ball tracks the talk (Roadmap → Basics → Roles → Key Skills); native
  diagrams are legible, exactly hub-centred, and crossfade. User: *"this is a bit better actually… a good
  place to checkpoint."*
- **Builds green** across `Hark.Oracle` + `Hark.App` + Spike; committed `99ccbf9` (8 files, +459/−34).

## 🔓 Open threads
- **gpt-image → FLUX artifact reconciliation (the "confuses image-1/2" cleanup):** ~100 references across
  the repo still frame Vision around gpt-image-1/2. Living docs updated this episode (this `STORYLINE.md`
  snapshot + threads; `README.md`; misleading `VisionRenderer`/`VisionService`/Spike comments). **Still
  stale:** the **north-star docs** (`cristobal-vision.md`, `crystal-ball-design-brief.md`) predate the
  native-diagram pivot; and **`InfographicPromptComposer` is now dead code in the app** (Spike-only) —
  keep as a FLUX reference or prune.
- **Infra as Code is still wrong (carried from EP23/24):** `infra/main.bicep` + `modules/openai.bicep` +
  `main.parameters.json` describe `kind:'OpenAI'`, `gpt-image-1`, capacity **1** — not the **Foundry
  (`AIServices`)** account, `gpt-4.1-mini` + `gpt-image-2` + **flux2-pro**, capacity **10**, or the FLUX
  **Cognitive Services User** RBAC. The stack is **not reproducible from code** until reconciled — the
  single largest remaining artifact debt.
- **Native structural variety** — the diagram is radial every beat (the eye-hub *needs* a centre-clear
  layout). Now that it's native, other shapes (flow / hierarchy / list) are just layout templates; a
  content-type detector could pick per beat (loosening the always-radial hub is the tradeoff).
- **The pupil scene is still oatmeal-prone** — the FLUX scene inherits the EP18 single-topic ceiling; the
  diagram is now the primary didactic payload, which lowers the stakes, but the scene tier's variety
  lever (framing vs subject) is unresolved.
- Carried: the standing threads in [`STORYLINE.md`](./STORYLINE.md) — installer WPF rewrite (EP23), the
  engine boundary, diarization Fork A.
