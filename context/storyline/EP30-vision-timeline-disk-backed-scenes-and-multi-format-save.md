# 🎬 Episode 30 — The Timeline, Disk-Backed Scenes & a Multi-Format Save

> **Date:** 2026-09-01 · **Branch:** `main` · **Commits:** `9b6db29..7eb5e75`
> **One-liner:** Vision grew a **timeline rail** (click a past beat to review, then go Live), its scenes
> now **spill to disk** so RAM stays flat over long sessions, and the report **Save** became a
> pluggable **multi-format** export (HTML + Markdown today; DOCX/PPTX/PDF planned) behind a **file picker**.

## 🎯 Intent
Continue polishing the crystal-ball Vision into "a really compelling feature." The session moved through:
a **history/timeline** for the beats, an **idle topic recap** during lulls, a worry about **RAM vs the
ring-buffer limit** on long sessions, a **Save** action ("aside the existing copy") that aggregates every
view **including the vision slideshow**, and finally the shape of a **flexible export** — *"markdown, word,
pdf, or ppt (especially this)… maybe it's worth the squeeze to add the OpenXML stuff."*

## 🛠️ What changed
- **Vision timeline rail + review/live (`9b6db29`)** — a left `HistoryRailPanel` of clickable cards
  (scene thumbnail + beat title) and a top-right **`LivePill`**. `OverlayWindow` gained `VisionBeat`,
  `AddVisionBeat`, `BuildHistoryCard`, `ShowHistoryBeat` (review a past beat), `OnLivePillClick` (return
  to the present), and `VisionReviewRequested`/`VisionLiveRequested` events. `App` pauses the autonomous
  loop (`_visionReviewing`) while reviewing and pushes each completed beat to the rail after `Task.WhenAll`.
- **Disk-backed scenes + idle topic recap + Save (`0649c92`)** — `VisionBeat` now holds an **on-disk
  scene path**, not `byte[]`: each PNG is spilled to a per-run temp dir (`%TEMP%\Hark\vision-<guid>`),
  only the **148px thumbnail** stays in RAM, full-res decodes from disk for review/recap; dropped/cleared
  beats delete their file, and startup sweeps orphaned dirs. The `VisionHistoryMax` cap rose **12 → 60**
  (now bounds the rail's UI-element count, **not** RAM). The idle pupil filler learned to **walk whole
  beats** (topic + scene) as a chronological recap during a lull. A **Save** button (`\uE74E`) writes a
  self-contained report; a **file picker** (`SaveFileDialog`) replaced the hardcoded path.
- **Report refactor → `SessionReport` + `IReportWriter` (`ab844c2`)** — new `Hark.App/Reporting/`:
  `SessionReport.cs` (format-agnostic model + `IReportWriter` + a WPF-free `ReportPalette`),
  `HtmlReportWriter.cs` (the existing HTML, ported), `MarkdownReportWriter.cs` (new). `OverlayWindow`
  builds the model once (`BuildSessionReport`) and `SaveReport` drives a **writer registry** — the picker
  filter is generated from the writers, and the format is chosen by the picked extension.
- **Ignore the local output dir (`7eb5e75`)** — `context/output/` un-tracked + git-ignored (scratch reports).

## 🧠 Decisions
- **A diagram is structured data — keep it that way in the report too.** — **because** the vision beat is
  captured **structurally** (title + node labels/colours/details as text/chips) rather than a raster of the
  WPF mind-map: readable, searchable, and it lets the *deck* mode (later) become real slides. The actual
  scene PNGs are embedded/attached.
- **Spill scenes to disk rather than cap history for RAM.** — **because** the user wanted to keep more
  history but feared "overwhelming the RAM if the images are just kept unstored." Disk-backing removes the
  RAM ceiling; the remaining cost is thumbnails only, so the cap became a UI-element bound.
- **Pluggable writers + a picker, no baked-in path.** — **because** the deliverable should be flexible and
  portable across machines. `IReportWriter` makes each format an isolated unit; the `SaveFileDialog`
  chooses the destination at save time (no `%APPDATA%`/user-secret path assumption).
- **First-party / lib-light export stack (chosen, not yet built).** — **because** the user dislikes 3rd-party
  libs and trusts pandoc but agreed it isn't necessary here. Plan: **Markdown** (hand-rolled), **DOCX + PPTX**
  via **Open XML SDK** (first-party Microsoft), **PDF** via **WebView2** printing our HTML (first-party). No
  first-party lib *authors* PDF (Open XML = Office only; XPS ≠ PDF); WebView2's `PrintToPdfAsync` is the
  first-party PDF route — must load a temp **file** (`NavigateToString` caps at ~2 MB < our base64 images).
  PPTX is a genuine second layout: the timeline **is** a deck (one slide per beat).

## 🚧 Problems & resolutions
- **Symptom:** during idle recap after a **failed first image gen**, topics "keep cycling back to the
  beginning while new things are generated at the end" — herky-jerky, "getting stuck." → **Root cause:** the
  topic recap was gated on **image** staleness (`_lastPupilUpdateUtc`, 16 s), but a *failed* render never
  refreshes that clock, so the recap ran **during** active generation and fought `AddVisionBeat` (which
  resets the cursor to newest → jump to oldest each add). → **Attempted fix (reverted):** a second idle
  clock (`_lastBeatUtc` + `RecapIdle` 30 s) to gate the recap on a genuine no-new-beats lull. It stopped the
  fight but added **startup lag** (the mind-map waits) and the user wasn't convinced it was the real fix →
  **rolled back**. **Real fix deferred to an open thread:** the shuffling is a **null-scene gap** — the
  proper cure is an **image fallback** so a beat is never scene-less, or **targeting the render error**.
- **Symptom:** `Path` is ambiguous in the overlay (`System.IO.Path` vs `System.Windows.Shapes.Path`) →
  CS0104. → **Fix:** fully-qualify `System.IO.Path` (same WinForms/WPF ambiguity family as `Size`/`FontFamily`).

## ✅ Verification
Every stage builds green and was committed to bank it (`9b6db29`, `0649c92`, `ab844c2`, `7eb5e75`).
The user field-tested the timeline, disk-backed save, and **saved a report as both `.md` and `.html`**
(the DoD render came through cleanly). The recap remediation was the one change **rolled back** after the
field read. The multi-format writers beyond HTML/Markdown are **not yet built** (see Open threads).

## 🔓 Open threads
- **Null-scene gap → image fallback (or target the render error) — the live front.** When a beat's FLUX
  render fails (`FLUX render returned 200 with no image`, or a `content_safety_violation`), the beat is
  **scene-less**, and the idle recap/pupil shuffles awkwardly around the hole. Two directions to weigh:
  **(a) fill the space** with a fallback image (hold the previous scene, a generated placeholder, or the
  native diagram rasterized into the pupil) so no beat is ever empty; **(b) target the error** (retry /
  soften / the fairy-tale `BingBlockList` thread). The **timing-gate remediation was rejected** — fix the
  *gap*, not the cadence.
- **Multi-format export — Phases 2+.** Refactor + Markdown shipped (`ab844c2`). Remaining, in order:
  **PDF** (WebView2, reuses HTML), **PPTX** (Open XML, beat-per-slide deck — the differentiated mode),
  **DOCX** (Open XML). Adds two **first-party Microsoft** NuGet deps (`Microsoft.Web.WebView2`,
  `DocumentFormat.OpenXml`) + the WebView2 Runtime.
- Carried from EP29: the **fairy-tale content-filter** test (`safety_tolerance=5` vs `BingBlockList_Prompt`).
  Carried from EP27: the FLUX **negative clause** in `VisionPromptComposer.Compose` (a gpt-image-ism) and the
  **temporary diagnostic toast** in `App.ShowSceneAsync` (still surfacing the render-failed balloon).
