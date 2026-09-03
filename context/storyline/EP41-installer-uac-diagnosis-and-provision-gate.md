# 🎬 Episode 41 — The Installer: Diagnosing the UAC Delay & Gating the Provision Form

> **Date:** 2026-09-03 · **Branch:** `main` · **Commit:** `5b02534` · **One-liner:** Investigated the
> installer's slow-to-launch feel (root cause: **machine-/first-run-dependent** Defender scan + compressed
> single-file cold extraction, **not** app code) and **deferred** the fix past 2.1; then shipped a small UX
> fix — the **Azure provisioning form is now gated behind an explicit opt-in** so its red button can't be
> mistaken for Finish.

## 🎯 Intent
Take on 2.1.0 item #6 (the installer). A live clue reframed it: *"I've just downloaded the 2.0 release Hark
setup and it ran a good bit faster than it does on my other machine, which makes me wonder what's going on."*
Then a pivot: *"I'll live with the UAC installer delay for 2.1, but… the provision Azure infra form should be
gated on a 'Provision Azure Infra?' selection because it shows a button similar to the finish button that's
confusing and easy to hit by accident."*

## 🛠️ What changed
- **Provisioning form gated behind opt-in — `5b02534`** (`InstallerWindow.xaml` + `.cs`). Post-install used to
  reveal the whole provisioning card **including the red "Provision Azure infra" button**, which looks like
  the red **Finish** button and was easy to hit by accident. Now the panel shows only a
  **"Provision Azure infrastructure?"** `CheckBox` (`ProvisionOptIn`) + a one-line explainer; the fields and
  the red action live in a collapsed `ProvisionForm` revealed only when the box is ticked
  (`OnProvisionOptInChanged`). Installer builds clean.
- **No code change for the UAC delay** — investigated and deferred (see below).

## 🧠 Decisions
- **The UAC/startup delay is machine-dependent, not app code — and it's deferred.** The released
  `Hark-Setup.exe` is published (see `release.yml`) as **self-contained + single-file + compressed**
  (`PublishSingleFile` + `IncludeNativeLibrariesForSelfExtract` + `EnableCompressionInSingleFile`), **without
  ReadyToRun** on the installer, and the **exe is unsigned** (only the MSIX inside is signed; the exe is
  zipped to dodge SmartScreen's download gate). That explains "faster on this machine": the slow costs are
  **first-run + machine-dependent and cache after run #1** — (1) **compressed single-file cold extraction** to
  `%TEMP%\.net\Hark-Setup\<hash>\`, and (2) **Defender/SmartScreen scanning** the unsigned, Mark-of-the-Web
  exe *before* it may run (this is the pre-UAC gap; `requireAdministrator` means UAC fires before any managed
  code, so a managed splash can't cover it). **User chose to live with it for 2.1.**
- **Fix map for later (banked):** the real pre-UAC lever is **signing** (Trusted Signing → Defender/SmartScreen
  trust, no scan-block, no "Windows protected your PC"); post-UAC levers are **ReadyToRun on the installer** +
  **dropping `EnableCompressionInSingleFile`** (the release is already zipped, so in-bundle compression is
  largely redundant and it's what makes cold extraction slow) + the **splash** for perceived progress.
- **Gate over restyle — because** hiding the form until opt-in removes the accidental-hit entirely; once the
  user deliberately ticks the box, the red action reading as "primary" is fine (it's the form's own action).

## 🚧 Problems & resolutions
- **XAML wrapper insertion:** wrapping the existing fields in a new `ProvisionForm` `StackPanel` left the inner
  elements at their old indent — cosmetically off but XAML-valid; the installer builds clean (0 errors).

## ✅ Verification
- `dotnet build Hark.Installer` → **0 errors**. Behaviour to confirm in a real installer run: post-install
  shows only the checkbox until ticked, then the form + Provision button appear.

## 🔓 Open threads
- **Installer startup delay — DEFERRED past 2.1** (accepted). When revisited: **Trusted Signing** (biggest
  win, ~$10/mo) + installer **ReadyToRun** + drop single-file **compression** + a **splash** for the post-UAC
  gap. Measure-first still applies (double-click→UAC vs UAC→window; run #2 warm-cache delta).
- **HARK 2.1.0 — one item left:** the **organic eye-motion** research spike (#2). Full list:
  `/memories/repo/hark-2.1.0-backlog.md`.
