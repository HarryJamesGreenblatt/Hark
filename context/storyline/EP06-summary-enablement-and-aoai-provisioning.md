# 🎬 Episode 6 — Summary Enablement: Docs & Azure OpenAI Provisioning

> **Date:** 2026-08-19 · **Branch:** `main` · **Commits:** `90cfbc8`, `78fb515` (+ Azure provisioning, no code)
> **One-liner:** Stood up the Azure OpenAI resource behind the SUMMARY feature and documented the whole setup — turning Episode 5's recap code from "not configured" into a working, keyless deployment.

## 🎯 Intent
Episode 5 shipped the recap **code**, but the model it talks to didn't exist yet ("not sure what it's connecting to"). This session: pick the right model, provision it, wire it up via user-secrets, and update the README so anyone can reproduce the setup.

## 🛠️ What changed

**Docs (`90cfbc8`, `78fb515`)**
- `README.md` — added a **Desktop overlay** section (Ctrl+Win+H, diarization, speaker pages, CAPTIONS/SUMMARY), an **optional Azure OpenAI** prerequisite, the **Summaries** config block (user-secrets, variable names only), provisioning steps for a chat deployment, and the `Azure.AI.OpenAI` dependency row. Corrected the deployment example to `gpt-4.1-mini` after discovering the `gpt-4o-mini` version was deprecated.

**Azure provisioning (via `az`, no repo changes)**
- Created resource group `rg-hark` (eastus2) and a dedicated **Azure OpenAI** account.
- Deployed a **`gpt-4.1-mini`** chat model (GlobalStandard).
- Assigned the signed-in identity the **Cognitive Services OpenAI User** role on the resource (keyless).
- Stored the endpoint + deployment name in `dotnet user-secrets` for `Hark.App`
  (`HARK_AOAI_ENDPOINT`, `HARK_AOAI_DEPLOYMENT`) — never committed.

## 🧠 Decisions
- **AOAI (standalone) over AI Foundry, for now** — **because** the recap only needs a chat-completions
  endpoint; a standalone Azure OpenAI resource is the smallest thing that satisfies it. Noted that our
  `AzureOpenAIClient` code works against a Foundry (`AIServices`) resource too, so this is a
  user-secrets swap, not a code change, if we migrate later.
- **`gpt-4.1-mini` as the model** — **because** the task is summarization + light structuring, not heavy
  reasoning: a fast, inexpensive "mini" chat model is the sweet spot for latency and cost, and it's
  fully compatible with our default chat-completions call. (Reasoning/o-series models were rejected as
  overkill and API-incompatible with the current code.) The deployment name lives in user-secrets, so
  swapping models needs no rebuild.
- **Host in the enterprise non-prod subscription for now** — **because** it unblocks testing; a personal
  subscription remains the eventual target (see open threads).

## 🚧 Problems & resolutions
- **Symptom:** `az cognitiveservices account create` failed `CustomDomainInUse` for `aoai-hark` →
  **Root cause:** the subdomain is globally unique and already taken → **Fix:** used a distinct
  resource name.
- **Symptom:** deployment failed `ServiceModelDeprecated` for `gpt-4o-mini` `2024-07-18` →
  **Root cause:** that model version is past its deprecation date in this environment →
  **Fix:** `az cognitiveservices account list-models` showed `gpt-4.1-mini` `2025-04-14` as available
  and default; deployed that instead (and updated the README example).

## ✅ Verification
- `az role assignment list` confirmed **Cognitive Services OpenAI User** on the resource scope.
- `dotnet user-secrets set` confirmed both secrets saved to the `Hark.App` store.
- Live recap smoke test (click SUMMARY in the running app) still to be done by the user.

## 🔓 Open threads
- **Live recap smoke test** — run `Hark.App`, capture dialogue, click SUMMARY, confirm a recap returns
  and that the revision-based cache reuses it until new speech arrives.
- **Infra as Code + CI/CD (nice-to-have, not now):** formalize the Azure provisioning (resource group,
  Azure OpenAI account + `gpt-4.1-mini` deployment, role assignment) as **Bicep/Terraform** driven by a
  **GitHub Actions** workflow, so the whole stack (Speech + Azure OpenAI) can be stood up in another
  environment/subscription with one run. Would pair well with parameterizing region/model and keeping
  secrets in the target env rather than local user-secrets.
- **Personal-subscription move (deferred):** re-provision Speech **and** Azure OpenAI under a personal
  subscription to fully decouple from the enterprise account.
- **Cost hygiene:** the Azure OpenAI resource is billable; remember to delete/purge when experimentation
  winds down.
