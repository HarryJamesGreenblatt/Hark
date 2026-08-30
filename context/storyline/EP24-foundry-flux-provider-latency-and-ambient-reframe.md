# 🎬 Episode 24 — FLUX on Foundry, the Latency Red Herring & the Ambient Reframe

> **Date:** 2026-08-29 · **Branch:** `main` · **Commits:** `0879711..cf89cf5` (+ uncommitted brief & Spike `raw` mode)
> **One-liner:** Migrated Vision off the legacy Azure OpenAI resource onto a **Foundry (`AIServices`)**
> account and built a **dual-path renderer** so HARK can drive **FLUX.2-pro** (Black Forest Labs) — not
> just gpt-image — then chased the "multi-minute lag" and found it was **never the model** (FLUX renders
> in ~10 s): the images sat at **capacity = 1** (~1 RPM → 429 backoff). Bumping capacity fixed the
> mechanics, but the deeper problem — a repeating "laptop at a desk" loop, images lagging speech —
> hit an **impasse**. The session ended by **reframing the whole feature** away from a latency race
> toward an **AI-directed ambient display**, grounded in game-dev / HCI / PCG research and written up as
> [`context/crystal-ball-design-brief.md`](../crystal-ball-design-brief.md).

## 🎯 Intent
Carried from EP23's open thread — *"leverage FLUX and other non-AOAI models"* — the session set out to
make the Vision render tier **provider-agnostic**. User framing across the session: *"focus on addressing
the conversion to Foundry such that I can leverage flux and other non-aoai models"* → *"build the
provider"* → *"is there a way to get better image-gen performance while not having to worry about making
too many calls?"* → the impasse: *"This is a hard problem that I frankly don't think I understand well
enough to solve… I wonder if that community finds any value in turning to research papers"* → *"go for it,
thanks"* (write the design brief).

## 🛠️ What changed
- **AOAI → Foundry migration (Azure, `az` CLI)** — stood up a new **Foundry** account
  **`fdry-hark-fb360`** (kind **`AIServices`**, `rg-hark`, `eastus2`) to replace the legacy `OpenAI`-kind
  `aoai-hark-svl5li`. Deployed **`gpt-4.1-mini`** (chat), **`gpt-image-2`**, and **`flux2-pro`**
  (`FLUX.2-pro`, Black Forest Labs) onto the **one** account — because HARK uses a single
  `HARK_AOAI_ENDPOINT` for concept **and** render (the EP23 constraint). Granted **Cognitive Services
  User** on the Foundry (covers the FLUX provider route); Speech User + OpenAI User remain sub-scope.
- **Dual-path renderer (`Hark.Oracle/Vision/VisionRenderer.cs`, `cf89cf5`)** — the renderer now branches
  on a **provider route**. `_providerRoute == null` → the existing OpenAI `ImageClient` path (gpt-image).
  Non-null → a **raw-HTTP Black Forest Labs** path: `POST {account}.services.ai.azure.com/providers/
  blackforestlabs/v1/{route}?api-version=preview`, bearer token from `AzureCliCredential` on the
  **`https://cognitiveservices.azure.com/.default`** scope, body `{model, prompt, width:1024,
  height:1024}`, response `data[0].b64_json`. Ctor gained `quality` + `provider` params; `_quality` is
  **omittable** (gpt-image `low/medium/high`; FLUX takes none).
- **Configurable image quality/provider (`Hark.App/App.xaml.cs`, `cf89cf5`)** — reads new
  **`HARK_AOAI_IMAGE_QUALITY`** and **`HARK_AOAI_IMAGE_PROVIDER`** knobs; `BuildVisionService` threads
  them into the `VisionRenderer`. Dev user-secrets now point at the Foundry endpoint with
  `HARK_AOAI_IMAGE_DEPLOYMENT=flux2-pro`, `HARK_AOAI_IMAGE_PROVIDER=flux-2-pro`.
- **Spike `raw` mode + timed render-and-save (`Hark.Oracle.Spike/Program.cs`, uncommitted)** — added a
  `raw` mode that **bypasses `ConceptDesigner`** (transcript → model directly) and a `RenderAndSave` that
  **times** the render, writes the PNG to temp, and opens it — the benchmark harness used to A/B the
  models. Usage: `dotnet run --project Hark.Oracle.Spike -- [raw] [window.txt]`.
- **Capacity bump (Azure)** — raised **`flux2-pro` capacity 1 → 10** (quota 15), the actual fix for the
  "multi-minute" lag (see Problems).
- **The design brief (`context/crystal-ball-design-brief.md`, uncommitted, `0879711` context)** — a
  research-grounded reframe of the Vision feature as an **AI-directed ambient display** (see Decisions).
- **Storyline note (`0879711`)** — recorded EP23's auth resolution (run-as-admin; keys policy-blocked;
  interactive Entra judged overkill) into the log.

## 🧠 Decisions
- **One Foundry account, not per-model resources** — **because** HARK's single `HARK_AOAI_ENDPOINT`
  couples concept + render to one host (EP23). A Foundry (`AIServices`) account hosts gpt-4.1-mini,
  gpt-image-2 **and** FLUX side-by-side, so the single-endpoint design keeps working across providers.
- **FLUX.2-pro is the default render model** — **because** the benchmark made it decisive: **FLUX ~10 s
  vs gpt-image-2 ~35 s**, the user prefers photographic images, **and** FLUX's quota is **15** vs
  gpt-image-2's **2**. Faster, higher-throughput, better-liked.
- **A dual-path renderer, not a FLUX rewrite** — **because** gpt-image still has uses (and is the OpenAI
  route we already trust); a provider branch keeps both live behind one config knob rather than forking.
- **`StringContent`, not `JsonContent.Create`, for the BFL POST** — **because** `JsonContent.Create`
  sent the body **chunked** (no `Content-Length`), which the BFL endpoint rejects with
  `400 no_content_length_header` (a *silent* "no image" in the app). Materializing the JSON into a
  `StringContent` sets the length header. **(Grep signature: `no_content_length_header`.)**
- **Reframe the feature, don't keep tuning it** — **because** three fields that *own* this exact problem
  (real-time games, netcode, ambient computing) almost never win a latency race; they **hide, predict, or
  redefine** it. The brief's four patterns: (1) **AI Director** (Valve L4D) — the Oracle should *direct*
  (HOLD/EVOLVE/CUT), not reflex-render every beat; (2) **client-side prediction** (Fiedler/QuakeWorld) —
  speculatively pre-render + **cross-dissolve**, never hard-cut; (3) **calm technology** (Case; Weiser &
  Brown) — it's an **ambient/peripheral** display, so it *needn't* track real-time, which **dissolves**
  the latency requirement, and "work even when it fails" makes a graceful idle state a *spec*; (4)
  **"procedural oatmeal"** (Compton; Togelius et al.) — the "laptop-loop" **has a name**: infinite output
  with no *perceived uniqueness*, a known PCG failure whose fixes are **framing variety on stable topics**
  + **content-type-adaptive** visual class (cinematic scene vs. diagram — where FLUX earns out).

## 🚧 Problems & resolutions
- **Symptom:** app took **multiple minutes** to show an image, even after FLUX benchmarked at ~10 s. →
  **Root cause:** the image deployments were at **capacity = 1** (~1 RPM); the autonomous beat loop's
  calls **queued behind 429 backoff**, not model latency. → **Fix:** raised `flux2-pro` capacity to
  **10**. (Latency was never the model — proven by the Spike timings.)
- **Symptom:** FLUX returned a **silent "no image"** in-app. → **Root cause:** `JsonContent.Create` sent
  a **chunked** body → BFL `400 no_content_length_header`. → **Fix:** `StringContent` with explicit JSON
  (Content-Length present). Verified a **682 KB** render in-process.
- **Symptom (earlier in arc):** FLUX on the OpenAI route → *"Model not supported with Responses API."* →
  **Root cause:** FLUX isn't an OpenAI-protocol model; it needs the **BFL provider route**
  (`/providers/blackforestlabs/v1/...`), not the `openai.azure.com` path. → **Fix:** the provider branch.
- **Symptom:** repeating **"laptop at a desk"** on a single-topic (FreeCodeCamp) stream. → **Diagnosis
  (not yet fixed):** this is **procedural oatmeal** — a faithful read of a single-topic source with no
  perceived-uniqueness lever. The EP22 literal-bias pushed hard toward alignment; variety is the opposite
  lever and must be applied *deliberately* on stable topics. The brief formalizes the fix (framing
  variety + content-adaptive class); **implementation deferred**.

## ✅ Verification
- **FLUX render proven in-process** — a **682 KB** PNG returned through the provider path after the
  `StringContent` fix (the `no_content_length_header` 400 gone).
- **Benchmarks (Spike, timed `RenderAndSave`):** **FLUX.2-pro** raw **9.6 s** / concept **7.8 s**;
  **gpt-image-2** raw **34.6 s** / concept **19.6 s** — the basis for defaulting to FLUX.
- **Capacity** — `flux2-pro` deployment now reports **capacity 10** (`az … deployment create … --query
  "{name,capacity:sku.capacity}"` → 10).
- **Builds green** across `Hark.Oracle` + `Hark.App` (`cf89cf5`).
- **Brief grounded** — sources pulled live (calmtech.com principles; Wikipedia L4D AI/Music Director +
  Gabe Newell "procedural narrative" quote; Wikipedia PCG "procedural oatmeal"; gafferongames.com
  client-side prediction). *Not yet exercised in the app — it's a design artifact, not code.*

## 🔓 Open threads
- **The brief is the strategic pivot — next session starts here.** Lowest-risk / highest-leverage first
  move (proposed, not started): **cross-dissolve + an always-"becoming" idle state** (patterns §2/§3) —
  makes the current ~10 s renders *feel* fine **without** touching the model, cadence, or Oracle. Then the
  bigger pieces: a **Director** (HOLD/EVOLVE/CUT + **speculative pre-render**), and the **content-adaptive
  concept** (framing-variety steer on stable topics + a narrative-vs-explanatory detector that picks
  cinematic-scene vs. schematic-diagram intent) — the real cure for the oatmeal loop.
- **Uncommitted work to land:** `context/crystal-ball-design-brief.md` (new) and the
  `Hark.Oracle.Spike/Program.cs` **`raw`-mode** benchmark harness are **not yet committed**.
- **Infra drift (still open):** `infra/main.bicep` + `modules/openai.bicep` still describe the **old**
  reality — `kind: 'OpenAI'`, `gpt-image-1`, capacity **1**. They don't capture the **Foundry
  (`AIServices`)** account, `gpt-image-2` / `flux2-pro`, capacity **10**, or the FLUX RBAC. The stack is
  **not reproducible from code** until this is reconciled (was already flagged in EP23's model thread).
- **Config knobs are new and undocumented:** `HARK_AOAI_IMAGE_QUALITY` / `HARK_AOAI_IMAGE_PROVIDER` exist
  in code + dev user-secrets but aren't in the README, the installer panel, or
  `%APPDATA%\Hark\config.json` guidance.
- Carried: the standing threads in [`STORYLINE.md`](./STORYLINE.md) — installer WPF rewrite (EP23),
  the engine boundary, diarization Fork A, and the (now reframed) **interim-visual** goal, which the brief
  supersedes with the ambient/predict-and-morph direction.
