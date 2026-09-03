# Research — Organic Eye Motion (Gaze) for the Oracle

> **Purpose.** Grounding for HARK 2.1.0 item #2 ("give the Vision eye organic movement — moves like an
> eyeball"). This artifact captures the *actual* oculomotor + virtual-agent + game-engine literature so the
> implementation is built on established models, not hand-wavy assumptions. Written after a research pass
> (2026-09-03) that followed a **reverted** first attempt (a spring-chases-target prototype built on
> assumptions — see "What the research corrects" below).
>
> **Scope.** The Oracle's eye is a flat **2D** WPF construct (concentric rings: silver frame → black socket →
> red cornea → pupil/scene → gloss). It is not a 3D character rig. The research below is mostly from 3D
> virtual-agent / robotics work; the section "Porting to Hark's 2D eye" translates it.

---

## 1. The core problem, stated correctly

A convincing eye is not "a dot that moves smoothly toward where you want it to look." Human gaze is a
**discontinuous, ballistic** system with a small, well-characterised vocabulary of movements:

| Movement | What it is | Relevant here |
|---|---|---|
| **Saccade** | Rapid ballistic jump between fixation points (French *saccade* = "jerk"). ~30–100 ms. | Primary "aliveness" + look-at re-targeting |
| **Fixation** | Holding on a point between saccades. Not perfectly still — micro-movements persist. | The rest state; carries micro-motion |
| **Smooth pursuit** | Continuously tracking a *moving* target. Max ~30°/s. | Cursor / moving-target follow |
| **Microsaccade / tremor / drift** | Tiny involuntary motion *during* fixation. | The "never perfectly still" life |
| **Vergence, VOR, torsion** | Depth convergence, vestibular counter-roll, Listing's-law torsion. | Mostly N/A for a single 2D eye |

The believability comes from getting the **saccade dynamics** and the **fixation micro-motion** right, and
from choosing **when** to saccade (the statistics), not from smooth interpolation.

---

## 2. Canonical models (with the equations we'll actually use)

### 2.1 "Eyes Alive" — the reference for a *conversational* eye
**Lee, Badler & Badler, SIGGRAPH 2002** (*Eyes Alive*, ACM TOG 21(3):637–644). This is the most directly
applicable paper because Hark's eye reacts to a **conversation**.

- Builds a **statistical** eye-movement model from eye-tracking video, fitting distributions for **saccade
  magnitude, direction, duration, velocity, and inter-saccadic interval**.
- Critically splits behaviour into **two modes — "talking" and "listening"** — because gaze statistics differ
  (e.g. during talking we look away more / longer; during listening we hold gaze on the partner more). This
  maps *perfectly* onto Hark: `_running` + speech energy ≈ "talking/engaged" vs quiet ≈ "listening/idle".
- Finding that matters: **statistically-derived saccades** made faces look significantly more natural and
  engaged than **stationary** eyes or **random** saccades. So: *the distribution is the point* — random
  jitter is not enough, and neither is smooth drift.

**Concrete fitted parameters** (extracted from the paper — Lee, Badler & Badler 2002; the source PDF was
reviewed then removed from the repo, see method note). Source video was 9 min of informal conversation at
**30 fps** (so *frames ÷ 30 = seconds*). Two gaze states drive the timing: **mutual** (eye at the primary
position, i.e. on the conversational partner) and **away** (not).

- **Saccade magnitude** — exponential frequency distribution `P = 15.7 · e^(−A/6.9)` (A in degrees, P in %).
  Synthesised by inverting it: draw `P` ∈ [0, 15.7], then `A = −6.9 · ln(P / 15.7)`. **90% of saccades are
  < 15°.**
- **Saccade direction** — quantised into 8 bins; **up/down and left/right happen ~2× more than diagonals**:
  right 15.5%, 45° 6.5%, up 17.7%, 135° 7.4%, left 16.8%, 225° 7.9%, down 20.4%, 315° 7.8%.
- **Duration** — `D = D0 + d·A` with **D0 = 20–30 ms**, **d = 2–2.7 ms/deg** (their form of the main sequence).
- **Velocity profile** — empirical bell curve, duration normalised to 6 frames, fit by a 6th-order polynomial
  (their equivalent of min-jerk). Peak velocity for large saccades 400–600 deg/s; **min inter-saccadic
  interval 50–100 ms**; saccadic reaction time 180–220 ms.
- **Talking vs listening** (the key mode split):
  | | mean magnitude | mutual-gaze hold | gaze-away hold |
  |---|---|---|---|
  | **Talking** | 15.6° ± 11.9° (92% ≤ 25°) | 93.9 ± 94.9 fr ≈ **3.1 s** | 27.8 ± 24.0 fr ≈ **0.9 s** |
  | **Listening** | 13.8° ± 8.9° (98% < 25°, narrower) | 237.5 ± 47.1 fr ≈ **7.9 s** | 13.0 ± 7.1 fr ≈ **0.4 s** |
  Talking eyes are **more dynamic** (wider magnitudes, shorter mutual holds); listening eyes rest on the
  partner far longer. (Argyle & Cook: speaker looks at listener 41% of the time; listener at speaker 75%.)
- **Synthesis loop** (their EMSS): start in *mutual*; a saccade fires when the mutual/away **timer expires**
  (or a large head-rotation occurs) → `ParGen` draws magnitude/direction/duration → `SacSyn` executes.
  Inter-saccadic interval is **much shorter when away from primary** than when on it.

### 2.2 The **main sequence** — saccade duration/velocity vs amplitude
**Bahill, Clark & Stark 1975; Collewijn et al. 1988.** A saccade's duration and peak velocity are lawful
functions of its amplitude (larger = longer + faster, with velocity saturating):

```
T_s = α + β · A_s          (saccade duration, ms;  A_s = amplitude in degrees)
```

Concrete, citable values (from Lian et al. 2026, fitting VR eye-tracking; see §2.5):
- Realistic fit: **α = 37.21 ms, β = 3.04 ms/deg.**
- Becker (1989) individual ranges: **α = 20–30 ms, β = 1.5–3 ms/deg.**
- **"Half-speed looks more natural for a character"** (Knabe 2019) → they doubled to **α = 74.42, β = 6.08.**
  Worth remembering: slightly *slower-than-life* eye motion reads as more natural/less robotic on a synthetic
  face.
- Peak velocity rises ~linearly with amplitude up to **~15–20°**, then saturates (soft ceiling).

**Implication:** a saccade is a movement of a **computed fixed duration**, scaled to its size — not an
open-ended spring settle.

### 2.3 Saccade **trajectory** — minimum-jerk velocity profile
**Yeo et al. 2012 ("EyeCatch", ACM TOG)**, also used by Lian et al. 2026. The angular velocity over a saccade
of amplitude `A_s` from `t0` to `tf` follows a **minimum-jerk** bell curve:

```
ω(t) = 30 · A_s / (tf − t0)^5 · (t − t0)^2 · (t − tf)^2
```

i.e. smooth accelerate → peak → decelerate, zero velocity at both ends, area = A_s. Integrating gives the
position profile (a smootherstep-like S-curve). Alternatives in the literature: quadratic velocity (Itti
2003), min-jerk (Mitake 2007). **Min-jerk is the standard.**

> This is the single biggest correction to the reverted first attempt, which used a spring chasing a target.
> A spring gives an exponential approach with overshoot ringing and an *amplitude-independent* time constant.
> A real saccade is a **fixed-duration, amplitude-scaled, min-jerk** move. Use the profile, not a spring.

### 2.4 Saccadic **undershoot** + corrective secondary saccade
**Becker & Fuchs 1969; Henson 1978; Lian et al. 2026.** Primary saccades systematically **undershoot** the
target by ~**10%** (a deliberate strategy — minimises flight time / error cost, Harris 1995). Behaviour by
size:
- **Small (<10°):** usually a single accurate "normal-shoot."
- **Large (>20°):** undershoot, then a **corrective secondary saccade** — smaller, and almost always in the
  **same direction** as the primary (Ohl et al. 2011/2013).
- Endpoint scatter is **anisotropic** — elongated along the target direction, growing with amplitude.
- Model: mean amplitude error `m_a = k_ma·E + c_ma` (E = eccentricity, `k_ma < 0` for undershoot), directional
  error mean ≈ 0.

Adding undershoot + a same-direction correction is a cheap detail that reads as distinctly "human"
(the Lian 2026 user study found people *can* perceive its presence).

### 2.5 Smooth pursuit and the saccade/pursuit decision
**Lian et al. 2026 (Frontiers in Virtual Reality 7:1806316)** — a clean, recent, open-access implementation
that ties the above together. Decision rule each tick:
- Compute required velocity to reach the target: `ω_req = angle(current, target) / Δt`.
- If `ω_req > ω_th` (**~30°/s**) → **saccade**; else → **smooth pursuit** at
  `ω = min(|target − current| / Δt, ω_max)` with **ω_max = 30°/s**.
- A **reaction latency** precedes acquiring a newly-appeared target (don't snap instantly).
- (They also add a Kalman filter for *where* a moving target will be, and a one-saccade "head saccade" with a
  dead-zone for small shifts — both largely out of scope for a single 2D eye, but the head→"whole-eye subtle
  shift on big gazes" analogy is noted for later.)

### 2.6 Fixation micro-motion = **1/f (pink) noise**
**Ruhland & Murphy et al., "Eye Animation"** (Springer; and the Ruhland 2015 survey *A Review of Eye Gaze in
Virtual Agents, Social Robotics and HCI*, Comput. Graph. Forum 34). The procedural model adds **gaze jitter
and pupil unrest modelled as 1/f^α (pink) noise**, plus rotations obeying **Donders' & Listing's laws**.

**Implication:** the "aliveness" during fixation should be **pink noise**, not a sum of sines and not white
noise. Pink noise has the right natural texture (correlated, wandering, no obvious period). Pupil-size
"unrest" (small continuous dilation drift) is the same idea — which dovetails with Hark's existing
bass→pupil-dilation channel.

---

## 3. How real-time engines actually solve this (the "proper game engine" angle)

The user's instinct — that this problem "usually exists in a game engine" — is correct. In practice:

- **Eye rig + LookAt constraint.** A 3D character has eye bones (or blendshapes); a **LookAt/aim constraint**
  rotates them toward a target transform, with clamps for eye range and often head follow-through. This is the
  "where to point" layer.
- **Procedural saccade/blink layer on top.** The constraint gives the *target*; a procedural layer adds the
  *believable motion to get there* — saccades (main-sequence timing), microsaccades, blinks, and lid-follow.
- **Shipping products** that do exactly this: **"Realistic Eye Movements" for Unity (Tobias Knabe, 2019)** —
  used by Lian et al. 2026 as the state-of-the-art baseline; **Convai** and other agent SDKs expose a "Gaze &
  Attention" layer ("eye rotation, head movement, procedural saccades, blinks, eyelid follow").
- **Attention/target selection** (which object to look at) is a *separate* higher layer — bottom-up saliency
  maps (Itti), scripted points of interest, or (here) UI elements / the cursor / conversation state.

**Takeaway for Hark:** we don't have a bone rig, but the *architecture* ports cleanly:
**(a) a target-selection layer** (cursor hover, mind-map node, or ambient/idle), **(b) a gaze-execution layer**
(min-jerk saccades + pursuit + pink-noise fixation), driving **(c) a 2D pupil-offset** instead of bone
rotation.

---

## 4. Porting to Hark's 2D eye

### 4.1 The "flat 2D has no gaze direction" worry — resolved
A radially-symmetric disc has no inherent facing, but the well-known **cartoon / googly-eye** trick makes 2D
gaze read convincingly: **translate the inner iris assembly (cornea + pupil + gloss) as a unit within the
socket**, while the outer frame/socket stays put. The offset direction *is* the gaze direction. This is a **2D
projection of eye rotation** and composes with Hark's existing channels as an orthogonal axis:

| Channel | Signal (today) | Visual axis |
|---|---|---|
| Cornea brightness / glow / pulse | broadband level | luminance |
| Pupil **dilation** | bass envelope (capacitor+spring) | scale |
| Gloss **shimmer** drift | treble Lissajous | highlight position |
| **Gaze (NEW)** | target-selection + saccade model | **iris x/y offset** |

Geometry (from `OverlayWindow.xaml`, `OracleEyeBig` 360×360): cornea sits at Margin 78 (radius ≈ 102 px),
black socket at Margin 26 (radius ≈ 154 px) → **~52 px of radial clearance**. So a max gaze offset of
**~25–30 px** keeps the iris comfortably inside the socket. (Angle→pixel: treat the full ~30 px as roughly a
±20–25° gaze range for mapping the deg-based main-sequence numbers.)

### 4.2 2D concentric circles vs a 3D eyeball — does the transform actually read as gaze?

**The worry, stated fairly.** A 3D eye rotates a *sphere*; the iris rides the curved surface. Hark's eye is a
stack of *flat coplanar disks*. A pure 2D translate is **not** the same operation as a sphere rotation, so
will it look like an eye looking, or like a disk sliding?

**What a real eye does (the thing we're approximating).** Rotate the eyeball by angle `θ` in direction `φ`; the
pupil sits on the sphere at radius `R`. Under (near-)orthographic viewing from the front, the pupil centre
projects to a screen offset of

```
(dx, dy) = R·sinθ · (cosφ, sinφ)
```

Three visual consequences fall out of that projection: **(i) translation** of the iris (the dominant cue),
**(ii) radial foreshortening** — the iris disk compresses *along the direction of travel* (a circle on a
sphere seen off-axis projects to an ellipse), and **(iii) rim occlusion** — at large angles the iris starts to
disappear behind the lid/socket edge.

**Why pure 2D translation already works.** Gaze perception is overwhelmingly driven by cue (i) — the **offset
vector of the dark pupil/iris relative to its surround**. Emoji, Muppets, googly eyes, and the vast majority
of 2D game/UI characters convey gaze with *translation only* and read perfectly. So the baseline (translate
the iris assembly, frame stays put) is not a hack — it is the established, sufficient solution. The `R·sinθ`
saturation is nearly linear over our ±≤25° range, so mapping gaze *degrees* to a clamped *pixel* offset is a
faithful small-angle approximation.

**Cheap cues that add the “sphere” feel** (in priority order — each is optional polish on top of translation):

1. **Socket clip (do this one).** Clip the iris assembly to the socket circle (`Grid.Clip` = the black-socket
   ellipse). This gives cue (iii) rim-occlusion *for free* and makes it **impossible** for the red cornea to
   escape its housing no matter how the offset + dilation + jitter stack up — the failure mode I'd otherwise
   worry about. Safety + realism in one.
2. **Radial foreshortening (the strongest 3D tell).** Apply a small scale-squash of the iris **along the
   offset direction** as it moves out: `squash ≈ 1 − k·(offset/maxOffset)` on the travel axis. This is exactly
   cue (ii) and is what most distinguishes “eyeball” from “sliding coin.” Implement as a `ScaleTransform`
   whose axis follows the gaze vector (or, cheaply, anisotropic X/Y scale for mostly-horizontal gaze).
3. **Gloss parallax (glass-dome depth).** The specular gloss is a reflection on the *outer* dome, not part of
   the iris — it should move **less** (or not at all) under gaze while the iris slides beneath it. Decoupling
   the gloss from the gaze group (keep its existing treble-Lissajous only, or add gaze at ~30% gain) sells
   “looking around under a glass dome.”
4. **Non-linear offset (`R·sinθ`).** Map large gaze angles with the sine saturation instead of linear. Lowest
   priority — the effect is subtle inside ±25°.

**What moves, precisely (resolves the layer question):**

| Element | Gaze translate? | Notes |
|---|---|---|
| Silver frame + black socket | **No** | the fixed “housing”; its stillness is what sells eyeball-in-socket |
| Red cornea (`OracleCorneaBig`) + pupil (`VisionOrb`) + scrying sheen | **Yes** | the moving “iris assembly”; clipped to the socket |
| Gloss (`VisionGloss`) | **Reduced / no** | reflection on the outer dome — keep mostly still for parallax |

**These compose cleanly with the existing channels.** Gaze is a **translate on the parent group**; the bass
**dilation is a scale on the child** `VisionOrb`; they multiply correctly (WPF nests transforms). Optional
foreshortening is another parent scale that composes with both. No conflict with the treble-gloss Lissajous
(it lives on a decoupled element).

### 4.3 Proposed model (grounded, minimal, self-contained)
Per compositor frame (`OnRendering`, already running with `dt`), only while the Vision page is open:

1. **Target selection.**
   - Cursor over the Vision canvas → target = clamped offset toward the cursor (a real look-at). If the
     required speed > ~30°/s-equivalent → saccade to it; else smooth-pursue it.
   - Otherwise **ambient**: draw the next fixation point + dwell from an **Eyes-Alive-style distribution**,
     with **"talking" vs "listening"** selected by `_running` + `_eyeLevel` (engaged → shorter intervals,
     wider excursions; idle → longer holds, smaller excursions).
2. **Saccade execution.** On a new target, compute amplitude `A`, duration `T = α + βA` (start from the
   slower α=74.42, β=6.08 "natural for characters" values, tuned to px), and drive the offset along the
   **min-jerk profile** over `T`. Apply **~10% undershoot**; if `A` is large, schedule a **same-direction
   corrective** secondary saccade.
3. **Fixation micro-motion.** Between saccades add **1/f pink-noise** jitter (a few px) + slow drift; scale
   amplitude gently with engagement. (Optionally fold a touch into pupil size as "pupil unrest.")
4. **Compositing.** Sum → an `x/y` on a `TranslateTransform` wrapping the iris assembly (cornea + pupil +
   sheen), clipped to the socket circle, leaving the silver frame/socket fixed. Gloss decoupled (§4.2).

### 4.4 Optional follow-ons (not the first build)
- **Third audio band (mids) → saccade energy** (a genuinely new orthogonal signal vs bass/treble), instead of
  reusing broadband `_eyeLevel` for engagement.
- **Per-node look-at**: hover a mind-map pill → gaze to that node (target-selection layer already supports it).
- **Attraction/repulsion** with pills/cursor via a potential field (fits Hark's existing physics idioms), if
  ambient + look-at isn't lively enough.
- **Blink coupling** (Hark already has a pupil blink) — real blinks often accompany large gaze shifts.

---

## 5. What the research corrects vs. the reverted first attempt

| Reverted assumption | Grounded correction |
|---|---|
| Spring chases the gaze target | **Fixed-duration min-jerk saccade**, duration from the main sequence `T=α+βA` |
| Sum-of-sines "microsaccade tremor" | **1/f pink noise** jitter + drift (Ruhland/Murphy) |
| Perfect landing on target | **~10% undershoot** + same-direction **corrective secondary saccade** |
| Ad-hoc random dwell (0.5–2.7 s) | **Eyes-Alive** mutual/away timers: talking ≈3.1 s / 0.9 s, listening ≈7.9 s / 0.4 s |
| Engagement = broadband level (fine as a stopgap) | Ideally a **mids band** = independent saccade-energy signal |
| One undifferentiated "look" behaviour | Explicit **target-selection layer** (cursor / node / ambient) feeding a **gaze-execution layer** |

---

## 6. Sources

- **Lee, S. P., Badler, J. B., Badler, N. I. (2002).** *Eyes Alive.* ACM TOG 21(3):637–644.
  doi:10.1145/566654.566629 — statistical talking/listening saccade model. **[most directly applicable]**
- **Lian, R., Mitake, H., Hasegawa, S. (2026).** *Saccadic undershooting in gaze generation for virtual
  characters.* Front. Virtual Real. 7:1806316. doi:10.3389/frvir.2026.1806316 — open-access, gives the
  concrete main-sequence values, min-jerk profile, undershoot model, smooth-pursuit rule, Unity impl.
- **Bahill, Clark & Stark (1975).** *The main sequence…* Math. Biosciences 24:191–204.
- **Collewijn, Erkelens & Steinman (1988).** Binocular co-ordination of horizontal saccades. J. Physiol. 404.
- **Yeo, Lesmana, Neog & Pai (2012).** *EyeCatch.* ACM TOG 31 — min-jerk saccade trajectory.
- **Becker & Fuchs (1969); Henson (1978).** Saccadic undershoot ~10% + corrective saccades.
- **Ohl, Brandt & Kliegl (2011, 2013).** Secondary (corrective) saccades, same-direction after undershoot.
- **Ruhland, Peters, Andrist, Badler, Gleicher et al. (2015).** *A Review of Eye Gaze in Virtual Agents,
  Social Robotics and HCI.* Comput. Graph. Forum 34:299–326 — the survey.
- **Ruhland & Murphy et al., "Eye Animation"** (Springer) — procedural gaze: main sequence + Donders'/Listing's
  + **1/f pink-noise** microsaccadic jitter & pupil unrest.
- **Knabe, T. (2019).** *Realistic Eye Movements for Unity* — shipping game-engine implementation (SOTA
  baseline in Lian 2026).
- **Itti, Dhavale & Pighin (2003/2006).** Neurobiological attention-driven avatar gaze (target selection).

_Research method note: no web-search MCP in this workspace — sources gathered via integrated-browser
DuckDuckGo HTML + `fetch_webpage`. The full "Eyes Alive" PDF (downloaded by the user) was text-extracted with
`pypdf` to transcribe the §2.1 parameters above, then **deleted** from the repo (it is a copyrighted ACM
paper and this is a public repo). Re-download from the ACM DL (doi:10.1145/566654.566629) if the figures are
needed again._
