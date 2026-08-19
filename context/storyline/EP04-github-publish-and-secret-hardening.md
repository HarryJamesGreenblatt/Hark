# 🎬 Episode 4 — GitHub Publish & Secret Hardening

> **Date:** 2026-08-18 · **Branch:** `main` · **Commits:** `a4c4d12`, `ddd0d8b`, `c6c3354` (history rewritten)
> **One-liner:** Published HARK to a personal public GitHub repo, then discovered and fixed a
> leaked subscription id — converted resource-id config to `dotnet user-secrets` and scrubbed
> git history.

## 🎯 Intent
Push the local-only repo to `github.com/HarryJamesGreenblatt/Hark` (personal account, distinct
from the enterprise `gh` login already active). Once published, audit what got exposed and lock
down anything sensitive.

## 🛠️ What changed
- **GitHub auth** — logged in a second `gh` account via device flow, switched active account to
  `HarryJamesGreenblatt`, created `HarryJamesGreenblatt/Hark` (public) and pushed `main`.
- **Repo metadata** — set repo description + topics (`dotnet`, `windows`, `speech-to-text`,
  `azure`, `wasapi`, `transcription`, `accessibility`, `csharp`) via `gh repo edit`.
- **`.gitignore`** — removed the `context/` exclusion so the storyline log itself is tracked and
  published (commit `a4c4d12`); `artifacts/` stays ignored.
- **Leak found:** the Speech resource ARM id (which embeds the Azure **subscription id**) was
  hardcoded in `run.ps1`, `Hark.Cli/Properties/launchSettings.json`, and
  `Hark.App/Properties/launchSettings.json` — present since the very first commit and now public.
- **`Hark.Cli/Program.cs`** — config resolution is now CLI flag → env var → `dotnet user-secrets`
  (via `ConfigurationBuilder().AddUserSecrets(Assembly.GetExecutingAssembly())`).
- **`Hark.App/App.xaml.cs`** — same fallback chain (env var → user-secrets) for `_region`/`_resourceId`.
- **`Hark.Cli.csproj` / `Hark.App.csproj`** — added `UserSecretsId`, plus
  `Microsoft.Extensions.Configuration.UserSecrets` / `.EnvironmentVariables` package refs.
- **`launchSettings.json` (both projects)** — dropped the resource-id env var entirely; region
  (non-sensitive) stays as a plain env var.
- **`run.ps1`** — no longer sets `HARK_SPEECH_RESOURCE_ID`; only region. Comment points at the
  one-time `dotnet user-secrets set ... --project Hark.Cli` setup.
- **README** — documented the `dotnet user-secrets` setup step under Configuration.
- **Local user-secrets** — populated for both projects so this machine keeps working unchanged.
- **History scrub** — installed `git-filter-repo` (via `pip`), took a mirror-clone backup to
  `%TEMP%`, ran `--replace-text` to swap the literal subscription-id GUID for
  `REDACTED-SUBSCRIPTION-ID` across all 8 commits, re-added `origin` (filter-repo drops it), and
  force-pushed `--all`/`--tags`. Verified with `git log --all -p` that the real id no longer
  appears anywhere in history, then confirmed via `git ls-remote origin`.

## 🧠 Decisions
- **Personal vs enterprise `gh` account:** kept both logged in; use `gh auth switch` to flip the
  active account rather than logging out, since this machine is used for both contexts.
- **Key Vault was attempted first, then abandoned:** created `kv-hark` in `rg-hark`, but an
  org-level Azure Policy silently forces `publicNetworkAccess` back to `Disabled` (the update API
  call reports success while the value reverts), even after adding an IP allow-list rule. No
  private-endpoint/VPN path exists from this environment, so Key Vault access was infeasible here.
- **`dotnet user-secrets` over Key Vault or GH Secrets:** GH Secrets are write-only /
  CI-run-only (no API to read a value back locally), so they can't back a locally-run app's
  config. `dotnet user-secrets` is the standard .NET mechanism for exactly this — per-machine,
  outside the repo, no cloud dependency, no extra infra.
- **Region stays a plain env var / launch-profile value** — it's not sensitive on its own (only
  the ARM id, which embeds the subscription id, needed hardening).
- **History rewrite chosen over "leave it forever"** — since the repo was public (even briefly),
  the id could have been scraped; scrubbing was worth the one-time hash rewrite (solo repo, no
  other clones/collaborators to coordinate with).

## 🚧 Problems & resolutions
- **Key Vault `Forbidden: Public network access is disabled...`** even after
  `az keyvault update --public-network-access Enabled` and an IP allow rule → root cause: an
  Azure Policy enforcing network restriction at the org level overrides both. Confirmed by
  re-querying `properties.publicNetworkAccess`, which silently reverted to `Disabled` despite a
  200 response. Resolved by switching approach entirely (user-secrets, no Key Vault).
- **`git-filter-repo` not installed / not a git subcommand** → installed via
  `pip install git-filter-repo`; PATH didn't pick up the pip Scripts dir automatically, so it was
  appended to `$env:PATH` per-session.
- **Filesystem `Copy-Item -Recurse .git` → "Permission denied and could not request permission
  from user"** when backing up outside the repo folder → worked around by backing up via
  `git clone --mirror . "$env:TEMP\Hark-git-backup.git"` instead (git handles it fine; the
  restriction seemed to be on raw recursive filesystem copy of `.git`, not on git itself).
- **`git-filter-repo --force` removes the `origin` remote** by design (safety) → re-added it
  manually before force-pushing.

## ✅ Verification
- `dotnet build` (full solution) green after the config-resolution changes.
- `dotnet user-secrets list --project Hark.Cli` / `--project Hark.App` show both keys set.
- `git log --all -p | Select-String "<the real subscription id>"` → **no matches** post-scrub.
- `git grep` across all rewritten commits shows `REDACTED-SUBSCRIPTION-ID` in place of the old
  value in `run.ps1` and both `launchSettings.json` files.
- `git ls-remote origin` confirms `origin/main` now points at the rewritten history tip.

## 🔓 Open threads
- **Personal Azure Speech resource (deferred to next session, on the personal machine):**
  provision a Speech resource under a personal Azure subscription, `az login` as that identity,
  assign it the `Cognitive Services Speech User` role, and point `dotnet user-secrets` at its
  region/ARM id — this fully decouples running HARK from the enterprise account/resource.
- **`gh` active account** was left switched to the personal account (`HarryJamesGreenblatt`) for
  the push/scrub work in this episode — needs switching back to the enterprise account
  (`hgreenblatt_microsoft`) for normal day-to-day work once this episode's follow-ups are done.
- (Carried) speaker diarization; permanent `az` ACL fix; credential-convention memory; overlay
  polish (drag-from-anywhere, click-through toggle, persistent settings).
