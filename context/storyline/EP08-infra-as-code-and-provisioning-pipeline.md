# ?? Episode 8 — Infrastructure as Code + Provisioning Pipeline

> **Date:** 2026-08-20 · **Branch:** `main` · **Commits:** `<short>..<short>`
> **One-liner:** Formalized HARK's Azure provisioning as Bicep IaC driven by a keyless GitHub Actions pipeline, so the full stack stands up on a fresh subscription with one run.

## ?? Intent
A fresh clone couldn't run because the required Azure resources only existed on a different
subscription. Rather than repeat the manual `az` setup by hand, capture the infra as code so it can
be reproduced on any subscription (including a personally-owned one) via an Actions pipeline.

## ??? What changed

- `infra/main.bicep` — subscription-scoped entry point: creates the resource group and wires up the
  Speech (required) and Azure OpenAI (optional) modules, emitting outputs that map to the app's
  config settings.
- `infra/modules/speech.bicep` — Azure AI Speech account (keyless custom subdomain) + the
  `Cognitive Services Speech User` data-plane role assignment.
- `infra/modules/openai.bicep` — optional Azure OpenAI account + chat deployment + the
  `Cognitive Services OpenAI User` role assignment; gated behind a `deployOpenAi` flag.
- `infra/main.parameters.json` — sample parameters (region, model, optional name overrides).
- `.github/workflows/provision-infra.yml` — OIDC (federated-credential) login, validate + deploy,
  and a job summary that prints the exact `dotnet user-secrets` values. Triggers on pushes touching
  `infra/**` and on manual dispatch.
- `README.md` — replaced the manual `az` provisioning snippet with an IaC section (pipeline +
  local-deploy options) and a note on the auto-unique naming.
- `Hark.Core/HarkConfig.cs` — new shared helper exposing the external config path
  `%APPDATA%\Hark\config.json`.
- `Hark.Cli/Program.cs`, `Hark.App/App.xaml.cs` — added the external-file config layer so a
  **published exe** (where `dotnet user-secrets` isn't available) has a repo-clean, publish-safe
  config home. Precedence: flags ? env vars ? `%APPDATA%\Hark\config.json` ? user-secrets.

## ?? Decisions

- **Decision:** Bicep over Terraform — **because** it's first-party, needs no state backend, and
  the whole team target is Azure-only.
- **Decision:** Keyless OIDC for the pipeline — **because** it mirrors HARK's existing keyless
  (Entra ID) philosophy; no secrets/keys are stored in GitHub.
- **Decision:** Auto-generate globally-unique resource names from a subscription-derived suffix,
  with optional explicit overrides — **because** Cognitive Services custom subdomains are global,
  so fixed defaults collide when the same names already exist on another subscription.
- **Decision:** Create resources and their RBAC role assignments together in one template —
  **because** keyless auth is useless without the data-plane role, and coupling them prevents drift.
- **Decision:** Add an external `%APPDATA%\Hark\config.json` config layer rather than
  `appsettings.json`/committed env — **because** it survives publishing (user-secrets is
  Development-only) while staying out of the repo, matching the "no infra details in source" stance.
  Key Vault was rejected as overkill: keyless HARK stores only resource *locations*, not secrets.

## ?? Problems & resolutions

- **Symptom:** `CustomDomainInUse — the subdomain name is not available` on `what-if`. ?
  **Root cause:** Cognitive Services custom subdomains are globally unique; the default name was
  already taken by a resource on another subscription. ? **Fix:** derive a stable suffix from the
  subscription id and make names optional overrides.
- **Symptom:** `BCP072 — this symbol cannot be referenced here` when a parameter default referenced
  a `var`. ? **Root cause:** Bicep parameter defaults may only reference other parameters. ?
  **Fix:** default the name params to empty and compute the effective name in a `var`.

## ? Verification
- `az bicep build` compiles clean (exit 0, no warnings).
- `az deployment sub what-if` against a fresh subscription reports **3 resources to create**
  (resource group, Speech account, role assignment) with no errors — confirming auth, template
  structure, and the RBAC logic. Real deploy tracked via the Actions run on push (`gh run watch`).

## ?? Open threads
- Live end-to-end run against the newly-provisioned resources, then point `dotnet user-secrets` at
  them.
- Optionally extend the pipeline to also provision Azure OpenAI by default once recaps are needed on
  the target subscription.
