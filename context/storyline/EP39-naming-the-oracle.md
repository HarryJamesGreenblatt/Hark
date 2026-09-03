# 🎬 Episode 39 — Naming the Oracle: One Presence, Retiring HAL · Cristóbal · Crystal Ball

> **Date:** 2026-09-02 · **Branch:** `main` · **One-liner:** Consolidated HARK's fragmented
> visual-interpretive identity under one canonical name — **the Oracle** — retiring the conflated
> monikers **HAL**, **Cristóbal**, and **"crystal ball"** across branding, docs, and code symbols
> (the red-eye aesthetic stays; only the names change).

## 🎯 Intent
A user-added branding item (on top of the locked 2.1.0 six): *"'Oracle' means Eye and also confers the
imagery of a purveyor of premonitions and visions… it should be more concretely anchored into the branding,
documentation and architectural artifacts rather than HAL or eye or the other monikers it seems to be
conflated with."* **Oracle** already named the mind tier (`Hark.Oracle`) and the concept persona (EP19), so
it's the natural anchor: etymologically a **seer** (the eye) that **has visions** (the didactic Vision) —
though *not* predictive here.

## 🛠️ What changed
Scope (chosen via three questions): **deep** (docs + branding + code symbols), **retire "HAL" as a name**
(keep the aesthetic), and for the north-star — **keep `cristobal-vision.md`, add an outcome note + deprecate
it, and create a new Oracle doc**. Historical storyline episodes left as-is; the living snapshot updated.

- **Code symbols renamed** (`Hark.App/OverlayWindow.xaml` + `.cs`): `HalEye→OracleEye`,
  `HalCornea→OracleCornea`, `HalGlow→OracleGlow`, `HalScale→OracleScale` (+ the `*Big` replicas), and the
  handler `OnHalEyeReleased→OnOracleEyeReleased`. Installer style `HalButton→OracleButton`
  (`Hark.Installer/InstallerWindow.xaml`).
- **Code comments / UI copy** swept to Oracle vocabulary across `App.xaml.cs`, `OverlayWindow.xaml(.cs)`,
  `HtmlReportWriter.cs`, `HarkSession.cs`, `InstallerWindow.xaml.cs`, `InfographicConcept.cs`,
  `InfographicPromptComposer.cs`, `Generate-Icon.ps1`, `Hark.Installer.csproj`, `infra/modules/openai.bicep`
  — "HAL eye"→"the Oracle's eye", "crystal ball"→"the Oracle"/"Vision", "HAL-like pulse"→"organic pulse",
  "HAL palette"→"Oracle palette".
- **README** — intro ("sound-reactive **Oracle** with an image-generating **Vision** mode"), the
  architecture diagram's Render box (`the Oracle / its eye + / Vision scene`), the Render table row, the
  Summary & Vision section, and the Features ("**The Oracle's eye**", "**Vision — the Oracle**").
- **New doc [`context/oracle.md`](../oracle.md)** — the canonical identity: why "Oracle", the anatomy
  (the eye / the mind / the Vision), the code layering, and the retired-name lineage.
- **Deprecated the north-star doc** — `cristobal-vision.md` gets a **⚠️ DEPRECATED** banner + outcome note
  pointing to `oracle.md`; `crystal-ball-design-brief.md` gets a lighter naming-note pointer. Both kept for
  their still-true *substance*.
- **Living snapshot** in `STORYLINE.md` updated (the eye, the Vision-open, the North-star line → the Oracle);
  historical index rows/episodes untouched.

## 🧠 Decisions
- **One presence, one name — because** HAL (eye) + Cristóbal (mind) + crystal ball (view) were three names
  for one thing; "Oracle" fuses seer-eye and vision-having, and already named the code tier.
- **Retire "HAL" as a *name*, keep the red-eye *aesthetic* — because** the 2001 look is good and on-brand;
  it just shouldn't be a competing identity.
- **Keep + deprecate the Cristóbal doc, add a new Oracle doc — because** (user's call) the origin story's
  substance is still valuable history; the *names* are what's dated.
- **Leave genuine Speech-SDK terms and historical episodes alone — because** `SpeechRecognizer` etc. are API,
  and the storyline is an append-only log.

## 🚧 Problems & resolutions
- **WPF x:Name rename risk:** renaming a code-behind field alone would desync from the XAML-generated field.
  → **Fix:** rename the XAML `x:Name` **and** every `.cs` reference together (text edits), then build to
  verify the generated fields line up. Full solution build **0 errors**; `dotnet test` **4 passed**.
- **False positives:** `halfPt`, `half-configured`, `MarshalAs`/`marshaled` contain "HAL"/"hal" but aren't
  the moniker → deliberately left untouched.

## ✅ Verification
- `dotnet build Hark.slnx` → **0 errors** (app, core, oracle, installer, cli); `dotnet test` → **4 passed**.
- `grep` confirms no forward-facing HAL / crystal-ball references remain outside the historical storyline and
  the (now-deprecated, intentionally historical) north-star docs.

## 🔓 Open threads
- **HARK 2.1.0 — the branding pair is done.** Item #1 (Hear·Adapt·Render·Keep, EP38) + this Oracle
  consolidation. Remaining: the **export-polish cluster** (session title · PDF light mode · HAL→Oracle icon
  consistency — now framed as the Oracle-eye mark), the **installer pre-UAC delay**, and the **organic
  eye-motion** spike. Full list: `/memories/repo/hark-2.1.0-backlog.md`.
