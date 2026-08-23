# 🎬 Episode 13 — Installable Release: MSIX, a Single-File Setup & the SmartScreen Lesson

> **Date:** 2026-08-22 · **Branch:** `main` · **Commits:** `30f2cbd..3012a90`
> **One-liner:** HARK became a **shippable Windows app** — a HAL-eye brand mark, a signed MSIX, and a
> single self-contained `Hark-Setup.exe` (embedding the package) published by a tag-triggered release
> pipeline; five point releases (v0.1.0→v0.1.4) shook out SmartScreen, a duplicate-instance bug, and
> an in-installer Azure-config step, all verified against a real install.

## 🎯 Intent
After the HAL-eye windowed-RMS fix (logged in Episode 12), pivot to distribution: "make an installer
where I can install this on a target machine as a standalone executable" that registers HARK in the
tray and at startup — modelled on the sibling **WavBall** repo's installer. Goal: a clean
Add/Remove-Programs app, Windows-Search registration, and a browser-downloadable setup.

## 🛠️ What changed

**Brand mark (`Hark.App/Scripts/Generate-Icon.ps1` + `Assets/`)**
- A programmatic **HAL-9000 eye** icon set (System.Drawing): metallic ring, saturated-red radial core,
  top-only gloss. Emits `Icon.ico` (multi-res) + MSIX tiles (`Square44/71/150/310`, `Wide310x150`,
  `StoreLogo`). `Hark.App.csproj` sets `ApplicationIcon`; the tray now loads it via
  `Icon.ExtractAssociatedIcon`. Icons are **transparent badges** (no plate) so the tray reads like a
  proper app badge, not a black square; tiles get their dark backing from the manifest
  `BackgroundColor`.

**Packaging (`Hark.App/Package.appxmanifest`)**
- MSIX manifest: identity `HarryGreenblatt.Hark`, `Publisher=CN=Harry Greenblatt`, `microphone` +
  `runFullTrust` capabilities, tile visuals, and a **`windows.startupTask`** (launch-at-login,
  user-toggleable in Settings → Startup). HARK already starts hidden in the tray, so the startup task
  needed no app-side change.

**Single-file installer (`Hark.Installer/`)**
- A WinForms `Hark-Setup.exe` (HAL dark/red theme) that **embeds** the signed `Hark.msix` + public
  `Hark.cer`. `Program.cs` has a `--cert-only` elevated path; `InstallerForm.cs` is a 3-phase state
  machine (**Install → Configure → Done**): trust cert (one UAC) → extract the embedded msix to temp →
  `Add-AppxPackage`, then an **Azure-config step**.

**Release pipeline (`.github/workflows/release.yml`)**
- On a `v*` tag: build + **sign** the MSIX (`MSIX_CERT_PFX`/`MSIX_CERT_PASSWORD` secrets), embed it
  into a single self-contained `Hark-Setup.exe`, **zip it**, and publish to a GitHub Release. A
  self-signed code-signing cert (`CN=Harry Greenblatt`, thumbprint `4A1B2064…`) was generated locally;
  the public `.cer` is committed, the `.pfx` (base64) + password live only in repo secrets.

**Single-instance guard (`Hark.App/App.xaml.cs`)**
- A named `Mutex` in `OnStartup`; duplicate launches `Shutdown()` immediately.

**Installer config step (`Hark.Installer/InstallerForm.cs`)**
- After install, if HARK isn't already configured, reveal fields for Speech region + resource id (and
  optional AOAI endpoint/deployment) and write `%APPDATA%\Hark\config.json`. `IsAlreadyConfigured()`
  checks **all three sources the app reads** — env vars, `config.json`, and user-secrets — so
  configured machines skip the prompt.

## 🧠 Decisions
- **MSIX + single embedded-exe over a portable zip** — **because** the goal was a real installed
  experience (Start/Search, Add/Remove Programs, clean uninstall, startup task) from *one* downloaded
  file, not a loose folder.
- **Leave the installer exe *unsigned*; only sign the MSIX** — **because** a *self-signed* exe triggers
  SmartScreen's harsher block, while an unsigned one gets the softer "Run anyway." The MSIX inside is
  what must be signed for `Add-AppxPackage` to validate.
- **Ship the exe *inside a zip*** — **because** SmartScreen's "isn't commonly downloaded" reputation
  gate targets bare executables, not archives (this is exactly why WavBall downloads cleanly). This is
  the free fix; a trusted cert (Azure Trusted Signing) is the real one.
- **Installer-only release (dropped the portable zip)** — **because** shipping both the installer and a
  portable build was duplicative; the installer is the headline.
- **Detect config across all sources, not just `config.json`** — **because** `AddUserSecrets` also
  works in published builds (the `UserSecretsId` is compiled into the assembly), so a dev machine with
  user-secrets *is* configured; the prompt must mirror the app's real precedence.

## 🚧 Problems & resolutions
- **Symptom:** icon script threw `Cannot convert LinearGradientBrush to SwitchParameter`. → **Root
  cause:** a local `$plate` brush collided with the `-Plate` switch (PowerShell names are
  case-insensitive). → **Fix:** rename the local to `$plateBrush`.
- **Symptom:** tray icon showed a **black square** (unlike Spotify's badge). → **Root cause:** the
  `.ico` was drawn with a dark plate. → **Fix:** transparent `.ico`; the eye's ring is the badge.
- **Symptom:** toast **notification icon** was a generic placeholder. → **Root cause:** unpackaged
  runs have no AppUserModelID. → **Fix:** the **MSIX packaged identity** makes toasts use the tile
  logo automatically (confirmed post-install).
- **Symptom:** Edge **blocked the download** ("isn't commonly downloaded"). → **Root cause:** shipping
  a bare unsigned exe. → **Fix:** ship it zipped (v0.1.1); WavBall did this all along.
- **Symptom:** captions/hotkeys misbehaved when toggled off; a "Couldn't register Ctrl+Shift+M"
  balloon. → **Root cause:** **no single-instance guard** — Start tile + startup task + double-clicks
  spawned **three** processes stacking overlays and fighting over hotkeys. → **Fix:** named-mutex guard
  (v0.1.2).
- **Symptom:** the config prompt appeared even though HARK was already set up. → **Root cause:** the
  check only looked at `config.json`, but the config lived in **user-secrets** (which the installed app
  reads). → **Fix:** `IsAlreadyConfigured()` now checks env vars + `config.json` + user-secrets
  (v0.1.4).
- **Meta:** `gh run watch | Select-Object -Last 30` reported exit 1 while the run *passed* — a pipeline
  exit-code quirk. Always confirm with `gh run view`.

## ✅ Verification
- Five releases published as `Hark-Setup.zip`: **v0.1.0** (bare exe, SmartScreen-blocked) → **v0.1.1**
  (zip) → **v0.1.2** (single-instance) → **v0.1.3** (config prompt) → **v0.1.4** (config detection).
- Real install confirmed by the user: browser download clean (zip), tray + **notification icon = the
  HAL eye**, single instance holds. Config round-trip verified end-to-end — a simulated fresh machine
  (user-secrets moved aside) showed the prompt and wrote `config.json`; a second install then reported
  "already configured." Dev user-secrets restored afterward.

## 🔓 Open threads
- **Clean browser download needs a *trusted* cert.** The zip dodges the download block, but the exe
  still shows a first-run "Run anyway," and the install UAC trusts a self-signed cert. **Azure Trusted
  Signing (~$10/mo)** would sign the msix + exe for warning-free downloads — the real fix.
- **Second-launch UX:** duplicates currently exit silently; nicer would be to surface/toggle the
  existing instance (needs light IPC or a registered window message).
- **Installer naming:** HARK's own recap suggested "HARK Installer" over "HARK Setup" (minor; "Setup"
  is conventional).
