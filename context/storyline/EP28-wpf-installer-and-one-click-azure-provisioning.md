# 🎬 Episode 28 — The WPF Installer & One-Click Azure Provisioning

> **Date:** 2026-08-30 · **Branch:** `main` · **Commits:** `f2487d8..8b7b5bd` (released as **v1.0.3**)
> **One-liner:** Rewrote the installer in **WPF** (fixing the Surface's squished high-DPI layout) and
> gave it an in-app **"Provision Azure infrastructure"** facility — it runs the same subscription
> deployment as the pipeline against **embedded, pre-compiled ARM JSON**, auto-fits **FLUX capacity** to
> the target sub's quota, and writes the full config (incl. the FLUX **provider** key) so Vision renders.

## 🎯 Intent
User beats across the session: *"the installer… opens squished on my Surface… I thought switching to
**WPF** could fix it"* → *"the config… lets me assign the proper Foundry dep endpoint or models"* → *"do
pretty much what the Actions pipeline is doing for provisioning… an option within [the installer] where
we can run the dep script so the correct infra gets provisioned"* → *"we're installing stuff before
getting the config… a broken but completed install I need to uninstall every time"* → *"there shouldn't
be an aoai id field but a foundry field"* → *"make it dynamic such that it will assign whatever the quota
limit is"* → *"no images are being rendered and nothing is indicating why."*

## 🛠️ What changed
- **WinForms → WPF rewrite (`f2487d8`, `349d416`)** — replaced `InstallerForm.cs` with
  `InstallerWindow.xaml` + `.xaml.cs`; `Hark.Installer.csproj` → `UseWPF`, `StartupObject=Program`
  (custom `Main` keeps the `--cert-only` path, no App.xaml); new `app.manifest` (PerMonitorV2 DPI). WPF's
  device-independent units fix the squish. WPF has no `PlaceholderText` → watermark = a non-hit-test
  `TextBlock` overlay per field.
- **Resizable + scrollable (`bcfafd5`)** — `ResizeMode=CanResize` + `MinWidth/MinHeight`; the body is a
  `ScrollViewer` (content responsive to full height, then scrolls) so the button bar never overlaps the
  fields; watermark `TextTrimming` stops long hints forcing horizontal overflow; the progress bar glides
  (`DoubleAnimation` on `Value`) instead of snapping.
- **In-app Azure provisioning (`c7fef9f`)** — new `AzureProvisioner.cs` drives `az` (`cmd /c az`):
  availability, `az account show`, `az ad signed-in-user show`, then `az deployment sub create` against
  the **embedded** `infra/` Bicep, parsing outputs → the `HARK_*` config. `app.manifest` →
  **requireAdministrator**; cert import runs in-process when elevated.
- **Config-first, install-LAST flow (`35e1d38` → `76f1fd8`)** — dropped the mode chooser: **config
  fields are the landing**, **Install** is deferred and runs cert+MSIX **after** config is captured (so
  bailing never leaves a broken partial install), and **provisioning is an optional post-install step in
  its own view** (`EnterPostInstall` hides the config fields so the card sits at the top, no scrolling).
  Setup persists to **both** `config.json` **and** user-secrets (merged) so a re-run is an upgrade install
  that skips provisioning.
- **Pre-compiled ARM JSON (`76f1fd8`)** — CI (`release.yml`) now runs `az bicep build` →
  `Hark.Installer/main.json`, embedded and **preferred** by `ExtractTemplates` so the target never needs
  the Bicep compiler (raw-Bicep fallback for dev).
- **Foundry-labeled config (`a6738a7`)** — relabeled the endpoint fields "Azure OpenAI" → **"Foundry"**
  (one account hosts chat + FLUX); the `HARK_AOAI_*` keys the app reads are unchanged.
- **Resilient provisioning (`fdfe65e`, `dc68cc7`)** — submit with **`--no-wait`** then **poll**
  `az deployment sub show` for the terminal state; sidesteps the CLI's *"content already consumed"* bug.
- **Auto-fit FLUX capacity (`1a3cac4`)** — `ResolveFluxCapacityAsync` reads
  `az cognitiveservices usage list` for `AIServices.GlobalStandard.FLUX.2-pro` and requests the available
  headroom (blank field = auto; manual override kept), so provisioning fits any sub's quota.
- **Write the FLUX provider key (`8b7b5bd`)** — `BuildConfigMap` now also writes
  `HARK_AOAI_IMAGE_PROVIDER=flux-2-pro` when the image deployment name contains "flux", so the app routes
  through the Black Forest Labs API instead of the gpt-image OpenAI route.

## 🧠 Decisions
- **Install LAST** — **because** installing cert+MSIX before setup left a completed-but-unconfigured
  install to uninstall between tests; capturing config first and deferring install removes the failure.
- **Ship pre-compiled ARM JSON, not raw Bicep** — **because** `az deployment --template-file main.bicep`
  compiles Bicep at runtime on the target, which failed there; ARM JSON needs no compiler.
- **`--no-wait` + poll** — **because** the synchronous create long-polls the ARM operation and (in some az
  builds) throws *"content already consumed"* mid-deployment; submitting + polling `show` avoids that path.
- **Auto-fit FLUX capacity to quota** — **because** quotas are **per-subscription** (main sub 10/15, the
  Surface sub capped at **4**); hard-coding 10 fails preflight on a fresh sub. Reading the sub's own
  headroom makes it work anywhere.
- **Relabel to Foundry, keep one endpoint** — **because** the app uses a single endpoint hosting both chat
  and FLUX; "Azure OpenAI" was legacy naming. No dual-endpoint app change.

## 🚧 Problems & resolutions
- **Symptom:** installer squished on the Surface despite DPI flags. → **Root cause:** WinForms AutoScale. →
  **Fix:** WPF (device-independent units) + PerMonitorV2 manifest.
- **Symptom:** *"The application… failed to start because its side-by-side configuration is incorrect."* →
  **Root cause:** a `--cert-only` string in an **XML comment** (`--` is illegal in XML comments) corrupted
  `app.manifest`. → **Fix:** reword the comment (`50706a1`). *(Grep: `side-by-side configuration`.)*
- **Symptom:** provisioning failed *"content for this response already consumed"*, appearing at the end. →
  **Root cause:** an az CLI response-formatting bug during the create long-poll (**not** Bicep, **not**
  the wrong model — the deployment itself succeeds). → **Fix:** `--no-wait` + poll `show`.
- **Symptom:** three failed **preflight validate** deployments — insufficient quota, "requires 10 … limit
  is 4 for FLUX.2-pro RPM". → **Root cause:** per-subscription quota; the template requested capacity 10. →
  **Fix:** auto-fit capacity to the sub's quota headroom.
- **Symptom:** Vision renders nothing, silently, after a successful install. → **Root cause:** the
  installer wrote `HARK_AOAI_IMAGE_DEPLOYMENT=flux2-pro` but **not** `HARK_AOAI_IMAGE_PROVIDER`, so the app
  used the gpt-image route against a FLUX deployment. → **Fix:** derive + write the provider key.

## ✅ Verification
User validated the **full experience on the Surface Business 7** (a different tenant/subscription): the
WPF window scales correctly, the config-first → install → optional-provision flow completes without broken
installs, provisioning succeeds (auto-fitting FLUX capacity to the sub's quota of 4), and **Vision renders
FLUX scenes** once the provider key is written. Every step builds green; shipped as **v1.0.3**.

## 🔓 Open threads
- **Release-tag churn:** during iteration we reused/clobbered **v1.0.2** then **v1.0.3** repeatedly (the
  "reuse until correct" convention). `gh release delete --cleanup-tag` silently skips the tag when no
  release exists — delete the remote tag explicitly (`git push origin :refs/tags/vX`) to avoid orphans.
- **Reconcile edge:** auto-fit uses `limit − current`, slightly conservative on a **re-provision** (excludes
  the existing FLUX deployment's own capacity) — the manual override covers it.
- The **temporary diagnostic toast** in `App.ShowSceneAsync` still fires on render failures but is transient
  ("nothing indicating why") — consider surfacing render failures in the Vision overlay status.
- Carried: installer no longer needs the interim `HARK_AOAI_IMAGE_PROVIDER` in dev user-secrets for a
  provisioned install (it's written now); the engine boundary + diarization Fork A remain from earlier.
