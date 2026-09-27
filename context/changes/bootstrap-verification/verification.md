---
bootstrapped_at: 2026-09-27T11:03:49Z
starter_id: dotnet
starter_name: .NET (ASP.NET Core webapi)
project_name: pigeon-watch
language_family: dotnet
package_manager: dotnet
cwd_strategy: subdir-then-move
bootstrapper_confidence: verified
phase_3_status: ok
audit_command: dotnet list package --vulnerable --include-transitive
---

## Superseded by manual scaffold (2026-09-27)

The bootstrapper run recorded below was **deleted by the user** after completion. They created a new ASP.NET Core Web API project manually via Visual Studio instead, at a different path and with different naming:

| | Bootstrapper run (deleted) | Actual project (current) |
| --- | --- | --- |
| Location | repo root | `PigeonWatch/Api/` |
| Project/csproj name | `pigeon-watch` | `PigeonWatchApi` |
| Solution file | none | `PigeonWatch/Api/PigeonWatchApi.slnx` |
| Frontend companion | not yet created | `PigeonWatch/Frontend/` (empty placeholder, Angular app not yet scaffolded) |

The rest of this log (hand-off, pre-scaffold checks, scaffold log, original audit) describes the **deleted** run and is kept for audit-trail completeness — it is not a record of what's on disk today. A fresh vulnerability audit was run against the actual project for accuracy:

**Tool**: `dotnet list package --vulnerable --include-transitive` (run from `PigeonWatch/Api/`)
**Summary**: 0 CRITICAL, 0 HIGH, 0 MODERATE, 0 LOW

```
The given project `PigeonWatchApi` has no vulnerable packages given the current sources.
```

No `.NET+Angular` bundled starter exists in the registry (see `## Why this stack` below), so the manual Visual Studio scaffold for the API and the still-empty `PigeonWatch/Frontend/` placeholder are consistent with the hand-off's original plan — only the path (`PigeonWatch/Api/` instead of repo root) and naming convention (`PigeonWatchApi` instead of `pigeon-watch`) changed, both human decisions made outside the bootstrap chain.

## Hand-off

```yaml
starter_id: dotnet
package_manager: dotnet
project_name: pigeon-watch
hints:
  language_family: dotnet
  team_size: solo
  deployment_target: azure-app-service
  ci_provider: github-actions
  ci_default_flow: auto-deploy-on-merge
  bootstrapper_confidence: verified
  path_taken: standard
  quality_override: false
  self_check_answers: null
  has_auth: true
  has_payments: false
  has_realtime: false
  has_ai: false
  has_background_jobs: false
```

### Why this stack

PigeonWatch is a medium-scale web app with a 3-week timeline, targeting a solo full-stack .NET/Angular developer new to agent-assisted delivery — the user deliberately chose to build on familiar technology (.NET + Angular) so the project's learning focus stays on supervising an AI agent, not on also learning a new framework. ASP.NET Core webapi is the registry's recommended default for (web, dotnet) and clears all four agent-friendly gates (typed, convention-based, popular in training, well-documented), with verified bootstrapper confidence, so scaffolding should be smooth. It anchors the backend, which owns auth (FR-001) and the status-workflow/urgency-ranking business logic. Because the schema records one starter_id per hand-off and no bundled .NET+Angular starter exists in this registry, the Angular frontend is a manual companion: after bootstrapper scaffolds this ASP.NET Core API, run `npx @angular/cli new pigeon-watch-web --defaults --routing --style scss --skip-tests --ssr false` alongside it as a separate project consuming the API. Deployment targets Azure App Service's free (F1) tier — a deliberate cost tradeoff: it sleeps after idle time, caps around 60 min/day of compute, and has weak custom-domain support, accepted knowingly to keep hosting free for a small community project; upgrading to a paid tier or switching to Fly.io remains a low-effort escape hatch if these limits bite. CI runs on GitHub Actions with auto-deploy-on-merge, the standard shape for a solo builder. Payments, realtime, AI, and background jobs are all out of scope per the PRD.

## Pre-scaffold verification

| Signal             | Value                              | Severity | Notes                              |
| ------------------ | ----------------------------------- | -------- | ----------------------------------- |
| npm package        | not run                             | n/a      | non-JS starter (language_family: dotnet) |
| GitHub repo        | not run                             | n/a      | `docs_url` (`https://learn.microsoft.com/aspnet/core`) is not a GitHub repo URL |

## Scaffold log

**Resolved invocation**: `dotnet new webapi -n .bootstrap-scaffold --no-restore`
**Strategy**: subdir-then-move
**Exit code**: 0
**Files moved**: 6 (`.bootstrap-scaffold.csproj` → `pigeon-watch.csproj`, `.bootstrap-scaffold.http` → `pigeon-watch.http`, `Program.cs`, `Properties/launchSettings.json`, `appsettings.Development.json`, `appsettings.json`)
**Conflicts (.scaffold siblings)**: none
**.gitignore handling**: absent in scaffold (template did not generate one)
**.bootstrap-scaffold cleanup**: deleted

Note: the two files the CLI named after the temp scaffold directory (`.bootstrap-scaffold.csproj`, `.bootstrap-scaffold.http`) were renamed to `pigeon-watch.*` during move-up so the project's csproj/assembly name matches `project_name` from the hand-off rather than the internal temp-directory name.

## Post-scaffold audit

**Tool**: `dotnet list package --vulnerable --include-transitive`
**Summary**: 0 CRITICAL, 0 HIGH, 0 MODERATE, 0 LOW
**Direct vs transitive**: not distinguished — tool reported no vulnerable packages given the current sources (direct or transitive)

Raw output:

```
The given project `pigeon-watch` has no vulnerable packages given the current sources.
```

## Hints recorded but not acted on

| Hint                       | Value                              |
| -------------------------- | ----------------------------------- |
| bootstrapper_confidence    | verified                            |
| quality_override           | false                               |
| path_taken                 | standard                            |
| self_check_answers         | null                                |
| team_size                  | solo                                |
| deployment_target          | azure-app-service                   |
| ci_provider                | github-actions                      |
| ci_default_flow            | auto-deploy-on-merge                |
| has_auth                   | true                                 |
| has_payments               | false                                |
| has_realtime               | false                                |
| has_ai                     | false                                |
| has_background_jobs        | false                                |

## Next steps

Next: a future skill will set up agent context (CLAUDE.md, AGENTS.md). For now, your project — `PigeonWatch/Api/` — is scaffolded (manually, via Visual Studio) and verified — happy hacking.

Useful manual steps in the meantime:
- `git init` (if you have not already) to start your own repo history.
- Address audit findings per your project's risk tolerance — the full breakdown is in this log (clean tree as of the latest check).
- `PigeonWatch/Frontend/` is still an empty placeholder. Per the hand-off's `## Why this stack`, scaffold the Angular companion there, e.g. from `PigeonWatch/`: `npx @angular/cli new Frontend --defaults --routing --style scss --skip-tests --ssr false` (adjust flags/name to match the `PigeonWatch/Frontend` path already created).
