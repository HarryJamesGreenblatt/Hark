# 🎬 Episode 23 — The Second Machine: Auth, the Installer, One Endpoint, and gpt-image-2's Catch

> **Date:** 2026-08-28 · **Branch:** `main` · **Commits:** `6b6a84b`, `c524ed9` (tag `v1.0.1`)
> **One-liner:** Took HARK 1.0.0 to a **second machine** and ran a gauntlet: `AzureCliCredential` needs
> **elevation** there (`WinError 5`, worked only when HARK ran **as admin**), **stale pre-Vision
> user-secrets** hid the installer's config fields, the installer window renders **scrunched** on
> high-DPI, and Vision requires the chat + image models on **one endpoint** (the user had them split
> across **two foundries**). Shipped **v1.0.1** (always-prefilled config panel + PerMonitorV2 DPI),
> **consolidated to one foundry** instead of decoupling the image endpoint (decoupling was written, then
> reverted), and learned that **gpt-image-2 is slower and more abstract** than gpt-image-1 with HARK's
> current prompt/quality — the pipeline is tuned for gpt-image-1.

## 🎯 Intent
Get 1.0.0 to actually **run on a machine that isn't the dev box**, and settle the image-model question.
User's thread across the session: *"couldn't start captions — CLI authentication failed… access is
denied"* → *"the installer window is scrunched"* → *"I wasn't prompted for Azure creds… it's reading a
stale user secret"* → *"how do I check/set the RBAC?"* → *"we never passed the [OpenAI] endpoint, only
the speech endpoint"* → *"the Speech and Foundry stuff are in different RGs"* → *"isn't image-2 better?"*
→ *"just deploy image-2 to the existing foundry rather than decouple"* → *"switching to gpt-2 came with a
time cost… images are better looking but take significantly longer and seem really abstract; our
implementation works better for gpt-1 than for 2."*

## 🛠️ What changed
- **Installer — always show the config panel, prefilled (`Hark.Installer/InstallerForm.cs`, `6b6a84b`)**
  — dropped the `IsAlreadyConfigured()` "skip if detected" gate (which let **stale pre-Vision
  user-secrets** mask the new `HARK_AOAI_IMAGE_DEPLOYMENT` field). The panel now **always** shows on
  install, **prefilled** from the app's real precedence (`env → %APPDATA%\Hark\config.json →
  user-secrets`); the user confirms/updates every install, and saving writes `config.json`, which
  **outranks** user-secrets and so overrides anything stale. Replaced `IsAlreadyConfigured`/
  `JsonHasSpeechConfig`/`HasSpeechConfig` with `PrefillConfigFields`/`DetectConfigValue`/`JsonValue`.
- **Installer — per-monitor DPI awareness (`Hark.Installer.csproj`, `InstallerForm.cs`, `c524ed9`)** —
  `<ApplicationHighDpiMode>PerMonitorV2</ApplicationHighDpiMode>` + `AutoScaleMode = AutoScaleMode.Dpi`
  (baseline `AutoScaleDimensions = 96,96`), because the hand-coded form did **no** auto-scaling
  (`AutoScaleMode.Inherit` ≈ `None`) and rendered the 96-dpi layout at physical size. **(Did not fully
  fix it — see Problems.)**
- **Released `v1.0.1`** — annotated tag → the `release.yml` pipeline published `Hark-Setup.zip`.
- **Image-endpoint decoupling — written, then reverted (no commit).** Added an optional
  `HARK_AOAI_IMAGE_ENDPOINT` (defaulting to `HARK_AOAI_ENDPOINT`) across `App`/installer, then
  `git restore`d it once the user chose to **consolidate to one foundry** instead. HARK stays
  single-endpoint.

## 🧠 Decisions
- **Consolidate to one foundry, don't decouple the image endpoint** — **because** the actual problem was
  a **two-foundry split** (gpt-4.1 on one, gpt-image-2 on another); HARK's design uses **one**
  `HARK_AOAI_ENDPOINT` for both the concept (chat) and the render (image), and deploying gpt-image-2
  **onto the gpt-4.1 foundry** fits that cleanly and keeps config simple. The decoupling code was built
  and then reverted per the user's call.
- **Always-show prefilled installer config, not hide-when-detected** — **because** hiding on detection
  masks **stale** config; the app reads env/config.json/user-secrets in precedence, so prefill mirrors
  that and a saved `config.json` transparently overrides stale user-secrets.
- **Auth: accept run-as-admin; don't build key-auth or interactive Entra** — **because** the target
  sub enforces a **disable-local-auth** policy (resource keys are dead sub-wide), and the only key-free
  alternative — an **interactive Entra sign-in** (WAM broker + a **multi-tenant** app registration +
  per-tenant consent, tenant/client id via config) — is far too heavy for a personal tool used on
  machines the owner controls. `AzureCliCredential` works when HARK runs **elevated** (that machine's
  `az` needs admin), so **running as admin is the accepted answer**; revisit interactive Entra only if
  HARK is ever distributed to machines/people the owner doesn't control.
- **gpt-image-2 is NOT a free upgrade with the current implementation** — **because** live testing showed
  it renders **noticeably slower** and **more abstract** than gpt-image-1; HARK's prompt composer +
  `"medium"` quality tier are effectively tuned for gpt-image-1. Treat model choice as a **tuning + A/B**
  question, not a version bump. (Research: the Foundry image family is `gpt-image-1`, `-1-mini`, `-1.5`,
  `-2`, all on the same Image API / 4,000-char cap; **FLUX.2** (BFL) and **MAI-Image-2.5** explicitly
  target concept-visualization / prompt-adherence — candidates for a bake-off. No published head-to-head.)

## 🚧 Problems & resolutions
- **Symptom:** on the 2nd machine, *"couldn't start captions — your CLI authentication failed… `WinError
  5`, access is denied through the Azure CLI."* → **Root cause:** `AzureCliCredential` shells out to
  `az`, and on that machine **`az` itself needs elevation** (`az account show` was denied unelevated).
  RBAC was **fine** — sub-scope **Cognitive Services Speech User** + **OpenAI User** inherit to the
  resources. → **Fix (accepted):** run **HARK as admin** → it can launch `az` → captions go green. Keys
  can't help (the sub enforces **disable-local-auth**) and interactive Entra was judged overkill, so
  run-as-admin is the decision.
- **Symptom:** installer didn't prompt for Azure creds; *"it's reading a stale user secret"* /  *"we
  never passed the [OpenAI] endpoint."* → **Root cause:** stale pre-Vision **user-secrets** satisfied
  `IsAlreadyConfigured()` so the panel was **hidden**; and the AOAI endpoint field **existed** but was
  hidden by the scrunched window and mislabeled *"enables SUMMARY"* (it also enables Vision). → **Fix:**
  always-show **prefilled** panel (`6b6a84b`).
- **Symptom:** installer window **scrunched / unreadable** on high-DPI. → **Root cause:** hand-coded
  WinForms form did no DPI auto-scaling. → **Fix attempt:** PerMonitorV2 + `AutoScaleMode.Dpi`
  (`c524ed9`). **Still scrunched after v1.0.1** → deeper WinForms DPI issue; a **WPF rewrite** of the
  setup UI is the durable path (open thread).
- **Symptom:** Vision failed *"deployment not found / 403"* even pointing at an endpoint that hosts
  gpt-image-2. → **Root cause:** HARK uses **one** `HARK_AOAI_ENDPOINT` for chat **and** image; the
  user's gpt-4.1 and gpt-image-2 lived on **different foundries**, so the image deployment wasn't on the
  chat endpoint (different RGs are fine; **different resources** are not). → **Fix:** deploy gpt-image-2
  onto the gpt-4.1 foundry and set all three fields (`HARK_AOAI_ENDPOINT`, `HARK_AOAI_DEPLOYMENT`,
  `HARK_AOAI_IMAGE_DEPLOYMENT`) to that one resource.
- **Finding (not a bug):** after consolidating, images **generate** on the 2nd machine and look **better**
  — but **significantly slower** and **"really abstract."** HARK's pipeline works **better for
  gpt-image-1 than gpt-image-2** as currently tuned.

## ✅ Verification
- `v1.0.1` built green and published `Hark-Setup.zip` via the release pipeline.
- **Captions confirmed working** on the second machine — once HARK was run **as admin** (the `az`
  elevation workaround).
- **Vision confirmed rendering** on the second machine after **consolidating** chat + image onto one
  foundry.
- **gpt-image-2 slower + more abstract** observed live vs gpt-image-1.
- RBAC verified in the portal: sub-scope **Speech User** + **OpenAI User** (+ **OpenAI Contributor**),
  which inherit to the resources — confirming the failure was **auth delivery**, not authorization.

## 🔓 Open threads
- **Auth — RESOLVED as "run as admin" (no code):** the target sub's **disable-local-auth** policy kills
  resource keys sub-wide, and the key-free alternative (interactive Entra: WAM broker + **multi-tenant**
  app reg + per-tenant consent, tenant/client id via config) is overkill for a personal tool. Decision:
  **run HARK elevated** (that machine's `az` needs admin). Only revisit interactive Entra if HARK is
  distributed to machines the owner doesn't control.
- **Installer → WPF:** PerMonitorV2 + `AutoScaleMode.Dpi` did **not** resolve the scrunched window;
  rebuild the setup UI in WPF (DPI-independent by default).
- **gpt-image-2 tuning / model bake-off:** the pipeline is tuned for gpt-image-1. If adopting gpt-image-2,
  revisit the prompt composer + make quality configurable (`HARK_AOAI_IMAGE_QUALITY`, currently hard-coded
  `"medium"`) + weigh the latency cost; A/B gpt-image-2 vs `1.5` vs **FLUX.2** vs **MAI-Image-2.5**. The
  new abstractness may need a stronger literal steer for image-2 specifically.
- **Installer label:** the "Azure OpenAI endpoint (optional — enables SUMMARY)" field also enables
  **Vision** — relabel to stop the "we never passed the endpoint" confusion.
- **Bicep `gpt-image-1` → `gpt-image-2`:** only if gpt-image-2 becomes the default after the bake-off.
