# 🎬 Episode 1 — Foundation & Fixes

> **Date:** 2026-06-24 · **Branch:** `main` · **Commits:** `533c7c6..82a8b4e`
> **One-liner:** Took the HARK MVP from "unverified code" to "proven working end-to-end," fixing
> Azure auth and stdout rendering, putting the project under git, and adding one-click run profiles.

## 🎯 Intent
Start from "I'm not sure how to test if this works" and manually validate the pipeline from the
CLI. Along the way: make the invocation tidy, stop stdout from duplicating lines, and get the repo
under version control so changes are revertible.

## 🛠️ What changed

- **`Hark.Cli/Program.cs`** — pass an explicit `new AzureCliCredential()` into
  `AzureSpeechTranscriber` (added `using Azure.Identity;`). Pins auth to the `az login` identity
  instead of letting `DefaultAzureCredential` resolve to the VS sign-in. _(in `533c7c6`)_
- **`Hark.Core/Output/StdoutSink.cs`** — fixed the interim "cascade":
  - dedupe identical consecutive `Recognizing` events (`segment.Text == _lastInterimText`),
  - suppress interims when `Console.IsOutputRedirected` (finals-only when piped/captured),
  - clamp each hypothesis to console width via a new `Fit()` (left-truncate with `…`) so long
	lines can't wrap and strand fragments. _(in `533c7c6`)_
- **Git init** — `git init -b main`; added `.gitattributes` (EOL normalization); reused the
  existing, already-correct `.gitignore`. Two commits:
  - `533c7c6` Initial commit: HARK loopback transcription MVP (20 files).
  - `82a8b4e` Add VS launch profiles for one-click runs.
- **`Hark.Cli/Properties/launchSettings.json`** — two profiles, **HARK** (stdout only) and
  **HARK (with transcripts)** (`--out`/`--json`/`--srt`), both injecting `HARK_SPEECH_REGION` +
  `HARK_SPEECH_RESOURCE_ID`. Enables `dotnet run` / F5 with no args. _(in `82a8b4e`)_

## 🧠 Decisions

- **Use `AzureCliCredential`, not `DefaultAzureCredential`, for this project** — because the user
  reserves `DefaultAzureCredential` for development-team/customer work, and the explicit credential
  deterministically selects the `az login` identity that holds the Speech role. _(Flagged as a
  reusable memory pending the user's approval to persist.)_
- **Keep the keyless (no-secret) posture** — rejected the Key Vault / subscription-key options;
  pinning the credential fixed auth without reintroducing secrets.
- **Defer speaker diarization** — user chose to retain the baseline MVP for now; the
  `SpeechRecognizer` → `ConversationTranscriber` switch remains an open thread.
- **`main` as default branch** and a dedicated `docs/sessions/` storyline folder for episodic
  handoffs (this file).

## 🚧 Problems & resolutions

- **Speech `401 AuthenticationFailure`** (WebSocket upgrade failed) → **Root cause:**
  `DefaultAzureCredential` resolved to a different identity/tenant than the role-bearing `az`
  login. → **Fix:** explicit `AzureCliCredential`.
- **stdout duplicate cascade** (≈20 identical interim lines; long lines spilling) → **Root cause:**
  Azure emits many identical `Recognizing` events, and `\r` redraw doesn't work when output is
  redirected or exceeds console width. → **Fix:** dedupe + redirect-detection + width clamp.
- **`Azure.Identity.AuthenticationFailedException` under F5** —
  `[WinError 5] Access is denied: ...\.azure\cliextensions\account\account-0.2.5.dist-info`.
  → **Root cause:** that one folder's ACL lost inheritance and has **no entry for the user**
  (only `BUILTIN\Administrators` + `OWNER RIGHTS`); non-elevated `az` can't read it and crashes on
  startup. → **Workaround chosen:** run Visual Studio **elevated**. **Permanent fix (deferred):**
  `icacls "$env:USERPROFILE\.azure\cliextensions\account" /reset /T /C /Q` or
  `az extension remove --name account`.

## ✅ Verification

- `--help` ran; full pipeline ran live three times. Sample final:
  _"Google Search is not only a powerful search tool but also the best friend for millions of
  people…"_ — committed once, cleanly.
- File sinks confirmed: `transcript.txt` (timestamped line) and `transcript.jsonl`
  (`{"offset":1.58,"duration":16.44,"text":"…"}`).
- After fixes, interims were distinct/growing (no duplicates) and width-clamped with `…`.
- Args-omitted run (`dotnet run --project Hark.Cli`) picked up config from `launchSettings.json`.
- Builds green throughout; `git status` clean at `82a8b4e`.

## 🔓 Open threads
- Speaker diarization (`ConversationTranscriber`).
- `README.md` still says `DefaultAzureCredential` — update to `AzureCliCredential`.
- Replace elevated-VS workaround with the ACL repair / extension removal.
- Optionally align `run.ps1` defaults with the VS "with transcripts" profile.
- Persist the credential-convention memory once approved.
