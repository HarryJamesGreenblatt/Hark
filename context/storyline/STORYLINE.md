# 🧭 HARK Storyline — Session Handoff Log

An **episodic** record of distinctive working sessions on HARK. Each episode encapsulates one
session's intent, decisions, changes, and loose threads — so any future session (human or agent)
can resume with full context instead of starting cold.

> **Read order for a cold start:** this file's _Current State_ snapshot → the latest episode →
> earlier episodes only if you need the "why" behind a decision.
>
> Prior art: [`../../problem-statement.md`](../../problem-statement.md) is the original brief
> (treat it as **Episode 0** — the pre-implementation research/design handoff).

---

## 📌 Current State (snapshot)

_Last updated: 2026-06-24 (end of Episode 3)._

- **Status:** CLI MVP + desktop captions overlay both working, verified live.
- **Branch:** `main` · **HEAD:** `5d758eb` · working tree clean.
- **Apps:** `Hark.Cli` (terminal) and `Hark.App` (WPF tray overlay) — both drive the shared
  `Hark.Core/HarkSession`. `Hark.App`: starts hidden; `Ctrl+Win+H` toggles a selectable/resizable
  captions bar on/off; header has a ✕ close button that exits cleanly.
- **Auth:** `AzureCliCredential` (explicit) — **not** `DefaultAzureCredential`. Requires `az login`
  with the **Cognitive Services Speech User** role on the Speech resource.
- **Speech resource:** region `eastus2`, `spch-hark` (kind `SpeechServices`, `S0`).
  Config lives in `run.ps1` and each project's `Properties/launchSettings.json`.
- **How to run:** VS **F5** (launch profiles) per project, or `.\run.ps1` (CLI), or raw `dotnet run`.
- **Known env gotcha:** the Azure CLI crashes on a broken ACL under
  `~/.azure/cliextensions/account/...` → currently worked around by **running VS elevated**.

---

## 🗂️ Episodes

| # | Date | Title | Headline outcome |
|---|------|-------|------------------|
| 0 | — | [Problem Statement / Research Brief](../../problem-statement.md) | Chose the stack & architecture (pre-code). |
| 1 | 2026-06-24 | [Foundation & Fixes](./EP01-foundation-and-fixes.md) | Verified the MVP live; fixed auth + stdout; initialized git; added launch profiles. |
| 2 | 2026-06-24 | [Desktop Captions Overlay](./EP02-desktop-captions-overlay.md) | Added `Hark.App` WPF tray overlay (Ctrl+Win+H), selectable/resizable; shared `HarkSession`. |
| 3 | 2026-06-24 | [Overlay Close & Toggle](./EP03-overlay-close-and-toggle.md) | Added ✕ close button + native hidden-until-toggled on/off behavior. |

---

## 🔓 Open threads (carried forward)

These are unresolved at the end of the latest episode — natural starting points for the next one.

- **Speaker diarization (deferred):** swap `SpeechRecognizer` → `ConversationTranscriber` for
  real-time speaker labels; needs a `SpeakerId` on `TranscriptSegment` and sink updates.
- **Overlay polish (optional):** drag-from-anywhere (except while selecting); click-through toggle;
  persistent settings (region/resource, position, opacity, font size) vs env vars.
- **Permanent CLI fix:** replace the "run elevated" workaround with an ACL repair
  (`icacls "$env:USERPROFILE\.azure\cliextensions\account" /reset /T /C /Q`) or
  `az extension remove --name account`.
- **Launcher parity (optional):** align `run.ps1` defaults with the VS "with transcripts" profile.
- **Pending memory:** approve/save the credential convention (see Episode 1 → Decisions).

---

## ✍️ Episode template

Copy [`_TEMPLATE.md`](./_TEMPLATE.md) to `EP<NN>-<slug>.md` for each new session, then add a row to
the **Episodes** table and refresh the **Current State** snapshot above.
