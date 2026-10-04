<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Persistence Wiring Smoke Test

- **Plan**: context/changes/persistence-wiring-smoke/plan.md
- **Scope**: Full plan
- **Reviewed phases**: 0, 1, 2, 3, 4, 5
- **Date**: 2026-10-04
- **Verdict**: NEEDS ATTENTION
- **Findings**: 0 critical, 5 warnings, 5 observations

Diff range: `4ab7e4a..HEAD`. Course-tooling updates under `.claude/**` and the root `CLAUDE.md` are excluded because they are not part of this change. Phase 3 is manual Azure grants, so for that phase only the record in `deploy-plan.md` and the Progress evidence were reviewed.

## Success criteria re-run (2026-10-04)

| Check | Result |
|---|---|
| 0.3 no weather refs | PASS |
| 0.4 `/weatherforecast` gives 404 | PASS |
| 1.1 / 2.2 solution builds | PASS (0 warnings, 0 errors) |
| 1.2 flat project gone | PASS |
| 1.3 no pending model changes | PASS |
| 1.4 drop and recreate LocalDB | NOT RUN (the database drop was blocked by the permission classifier; the existing DB has migration `20261003175108_AddSmokeCheck` applied) |
| 1.5 local probe returns 200 | PASS (2 calls) |
| 1.6 local `SMOKE_CHECK` count is 0 | PASS |
| 1.7 / 2.3 vulnerability audit | PASS |
| 2.1 architecture tests | PASS (39/39) |
| 4.1 / 4.6 workflow YAML checks | PASS |
| 4.2 only secret references in deploy-api.yml | PASS (6 references, AZURE_* only) |
| 4.8 frontend build and unit test | PASS (existing node_modules; `npm ci` skipped) |
| 4.11 clean deploy, Node 24 actions, startup command | PASS |
| 5.1 latest deploy run on main | PASS (`success`) |
| 5.2 live probe | PASS with note: the first call returned **503**, the curl retry returned 200 (see F2) |
| 3.1, 3.2, 5.3, 5.4 | NOT RUN (these need Dominik's Entra session and a temporary firewall rule; Progress records them done) |

Manual items 0.5–5.7 are all `[x]` and each cites a commit. No rubber-stamping signals were found, and 5.6 is honestly documented as an accepted risk in `deploy-plan.md`.

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | WARNING |
| Scope Discipline | PASS |
| Safety & Quality | WARNING |
| Architecture | WARNING |
| Pattern Consistency | PASS |
| Success Criteria | PASS |

## Findings

### F1 — Firewall-rule cleanup hides a failed delete

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: .github/workflows/deploy-api.yml:104-110
- **Detail**: The plan requires an `if: always()` step that deletes the runner firewall rule. The step runs `az sql server firewall-rule delete ... --name "$FIREWALL_RULE_NAME" || true`. The `|| true` covers the case where no rule was created (for example when login failed), but it also hides a real failed delete, such as an ARM error or an expired token. The run then stays green while the SQL server keeps allowing a shared GitHub-hosted runner IP. Check 5.3 confirmed no leak this time, but nothing would catch a future one.
- **Fix**: Make the step tolerate only "rule not found": skip the delete when `az sql server firewall-rule show` fails, and otherwise let a failed delete fail the job.
- **Decision**: FIXED. Applied as a variant of the fix: the create step got `id: allow-firewall`, the cleanup now runs on `if: always() && steps.allow-firewall.outcome == 'success'`, and `|| true` is gone. This avoids a transient `show` failure silently skipping the delete.

### F2 — Probe swallows exceptions unlogged; first call after auto-pause returns 503

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: PigeonWatch/Api/BusinessLogic/Services/DatabaseHealthService.cs:15-18, PigeonWatch/Api/DependencyInjection/DataServiceCollectionExtensions.cs:29
- **Detail**: `catch (Exception) when (!cancellationToken.IsCancellationRequested) { return DatabaseHealthResult.Unhealthy(); }` logs nothing. The plan's Key Discovery said `EnableRetryOnFailure` would let "the first request after a serverless auto-pause survive the resume". That did not hold. It was observed in 5.6 and reproduced in today's re-run of 5.2: the first live call returned 503 and the retry returned 200. `deploy-plan.md:138` says the cause "is not visible because DatabaseHealthService swallows the exception". The default retry budget (6 retries) plus `Connect Timeout=30` also leaves a failing probe unbounded beyond the F1 front-end timeout of about 230 s. Keeping detail out of the 503 body is correct; the missing piece is the server-side log.
- **Fix A ⭐ Recommended**: Inject `ILogger<DatabaseHealthService>`, log the swallowed exception at Warning, and leave retry tuning to the existing `deploy-plan.md` follow-up.
  - Strength: Small, local change that respects the layer rules. It makes the next post-pause 503 diagnosable, so the later retry tuning rests on evidence instead of a guess.
  - Tradeoff: The first-call 503 remains until the follow-up.
  - Confidence: HIGH. `Microsoft.Extensions.Logging.Abstractions` does not break any BusinessLogic layer rule (no EF, no ASP.NET).
  - Blind spot: Not checked whether App Service log streaming/Application Insights is configured to surface Warning logs.
- **Fix B**: Do Fix A and also set explicit `EnableRetryOnFailure(maxRetryCount, maxRetryDelay)` with a linked-CTS timeout in the service, so the probe gives a bounded answer.
  - Strength: Tackles both the diagnosis and the unbounded latency now.
  - Tradeoff: The retry parameters would be tuned blind, because the cause of the 503 is not yet known.
  - Confidence: MED. The right numbers depend on how long the resume actually takes.
  - Blind spot: Whether the 503 comes from SQL or from the F1 front end.
- **Decision**: FIXED (Fix A). `ILogger<DatabaseHealthService>` was injected and the exception is logged at Warning. Build: 0 warnings. Architecture tests: 39/39. Retry tuning stays in the `deploy-plan.md` follow-up.

### F3 — Anonymous probe can exhaust the free-tier vCore allowance

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: PigeonWatch/Api/WebApi/Controllers/HealthController.cs:10-17
- **Detail**: The plan accepted an anonymous, unthrottled probe as "cheap but not free on F1". It did not consider the database: every call wakes `pigeonwatch-db`, which runs on the free limit with `--free-limit-exhaustion-behavior AutoPause` (`deploy-plan.md:61`). At the 0.5 vCore minimum the 100k vCore-second monthly allowance is about 55 h online. Each wake keeps the database up for at least the auto-pause delay, so one request about every hour, from anyone or from an uptime monitor, uses up the month in a few days. After that the database stays paused until the month ends: a full outage. This is a gap in the plan, not drift.
- **Fix A ⭐ Recommended**: Record it as an accepted risk in `deploy-plan.md` with a hard exit: remove or secure `/health/db` no later than S-01.
  - Strength: Matches the plan's explicit "no auth on /health/db" decision. The endpoint is throwaway anyway, and the risk is now written down instead of hidden.
  - Tradeoff: The exposure lasts until S-01 lands.
  - Confidence: MED. The risk depends on someone finding or polling the URL.
  - Blind spot: Actual auto-pause delay setting on `pigeonwatch-db` not checked.
- **Fix B**: Restrict the endpoint now, for example with a shared header key that the CI probe sends, or an App Service access restriction.
  - Strength: Closes the drain vector immediately.
  - Tradeoff: Adds a secret to the workflow and app settings for a throwaway endpoint, which goes against the plan's NOT list.
  - Confidence: MED.
  - Blind spot: The manual probe steps in the runbooks would need the key too.
- **Decision**: ACCEPTED (Fix A). Recorded under "Accepted risks" in `deploy-plan.md`, with the exit "remove or secure `/health/db` no later than S-01; no uptime monitor on it". The neighbouring auto-pause 503 bullet was updated to reflect the F2 logging fix.

### F4 — Migration-down runbook cannot be run as written

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: context/deployment/deploy-plan.md:115-117
- **Detail**: Step 3 says to run as `pigeon-watch-api-github-oidc` "after `az login --service-principal` or from a `workflow_dispatch` run". That service principal has only a GitHub federated credential, with no secret or certificate, so it cannot sign in from a workstation. No workflow accepts a target migration. A bundle built the CI way (`-r linux-x64`) also does not run on Windows. Step 5 uses bare `curl -f ...`, which breaks the recorded lesson "Call curl.exe, never bare curl, in PowerShell commands". The rollback path is the plan's only answer to the migrate-then-deploy-fails gap, so it has to work on first use.
- **Fix**: Rewrite step 3 so Dominik runs it under his own `az login` as the SQL Entra admin: `dotnet ef database update <previous> ... --connection "<SQL_CONN>"`, or a `win-x64` bundle. Drop the unusable service-principal and workflow_dispatch options. Change step 5 to `curl.exe -f`.
  - Strength: Uses an identity that can actually authenticate today, and fixes the lesson violation in the same edit.
  - Tradeoff: The rollback then runs under an admin identity rather than the least-privileged CI one.
  - Confidence: HIGH. `deploy-plan.md` already records Dominik as the Entra admin who runs `sqlcmd -G`.
  - Blind spot: The rewritten runbook has not been dry-run.
- **Decision**: FIXED. Step 3 now runs under Dominik's own `az login` as the SQL Entra admin (`dotnet ef database update`, or a `win-x64` bundle). Verification uses `sqlcmd -G` plus `curl.exe` and moved ahead of the firewall-rule deletion (steps 4 and 5 swapped). Not yet dry-run.

### F5 — Architecture tests have bypassable gaps

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Architecture
- **Location**: PigeonWatch/Api/ArchitectureTests/LayerDependencyTests.cs:34-58, ControllerActionTests.cs:41-62, ServiceRegistrationTests.cs:80-105
- **Detail**: All 39 tests pass and none passes vacuously, but four gaps let a later slice drift unnoticed:
  - (a) BusinessLogic, WebApi and WebApi.Host forbid only `Microsoft.EntityFrameworkCore`. `Microsoft.Data.SqlClient` reaches them transitively through Data, so `new SqlConnection(...)` or `catch (SqlException)` in a service passes. The plan states BusinessLogic "names no EF Core or SqlClient exception type".
  - (b) The rule that actions return `Task<ActionResult<T>>` checks only the top-level `T`. A `WebApi.Models` type with a `SmokeCheckResult` property still puts a business object on the wire.
  - (c) Only `ControllerBase` subclasses count as controllers. POCO `*Controller` or `[Controller]` classes escape every controller rule.
  - (d) `AcceptedInterfaceNames` accepts any `I` + PascalCase suffix, including a bare `IService` or `IRepository`, which is looser than the plan's `I<TypeName>`. The loosening was partly intended, to allow `ICurrentUserProvider` for `SystemCurrentUserProvider`.
- **Fix A ⭐ Recommended**: Close (a) and (b) now: add `Microsoft.Data.SqlClient` and `System.Data.Common` to the forbidden lists of BusinessObjects, BusinessLogic, WebApi and WebApi.Host, and walk `WebApi.Models` property types recursively to reject BusinessObjects or Data types. List (c) and (d) as known limitations in `Api/CLAUDE.md`.
  - Strength: The two gaps most likely to be hit by S-01 (SQL error handling, nested response models) are closed before S-01 starts.
  - Tradeoff: Two test edits plus a doc note. Rule (d) stays deliberately loose.
  - Confidence: HIGH. Both fit the existing NetArchTest and reflection patterns in the test project.
  - Blind spot: Not verified whether NetArchTest 1.3.2 sees dependencies used only inside async state machines.
- **Fix B**: Record all four as known limitations now and harden the tests as the first task of S-01.
  - Strength: No code change on a merged and closed-out change.
  - Tradeoff: The guardrails stay weaker than the plan's own description of them.
  - Confidence: MED.
  - Blind spot: S-01 planning might not pick this up.
- **Decision**: FIXED (Fix A).
  - (a) `Microsoft.Data.SqlClient` and `System.Data.Common` were added to the forbidden lists of BusinessObjects, BusinessLogic, WebApi and WebApi.Host.
  - (b) New test `Api_models_expose_no_business_object_or_data_types` walks model properties, generic arguments and array elements recursively.
  - (c) and (d) are documented as known limits in `Api/CLAUDE.md`.
  - Tests: 40/40. A negative check (a `List<SmokeCheckResult>` property on the model, plus `catch (DbException)` in the service) failed both new rules as expected, and the files were restored. This also confirmed that NetArchTest sees dependencies inside async state machines.

### F6 — Azure actions pinned by major tag, not SHA

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: .github/workflows/deploy-api.yml:73,139
- **Detail**: `azure/login@v3` and `azure/webapps-deploy@v3` run in jobs with `id-token: write`. If a tag is moved, that action gets an OIDC token with DDL rights on the database, firewall write on the SQL server, and deploy rights on the web app. Other actions (`checkout`, `setup-*`) are lower value.
- **Fix**: Pin the two Azure actions to full commit SHAs with a `# v3` comment.
- **Decision**: FIXED. `azure/login@a641126d…` (v3.1.0) is pinned at both uses and `azure/webapps-deploy@02a81bea…` (v3.0.8) at one. The 4.1 and 4.11 YAML checks still pass. Future bumps are manual until Dependabot for github-actions is added.

### F7 — Stale or inconsistent plan and doc text

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: PigeonWatch/Api/CLAUDE.md:11, context/changes/persistence-wiring-smoke/plan.md:51,80,388, context/deployment/deploy-plan.md (Phase 5 workflow paragraph)
- **Detail**:
  - `Api/CLAUDE.md:11` says "The `SMOKE_CHECKS` table". The real table is `SMOKE_CHECK`, and line 54 of the same file says singular names are required.
  - `plan.md:80` reads "(`SMOKE_CHECK`, not `SMOKE_CHECK`)".
  - `plan.md:388` (change 5) still says `actions/setup-node@v4`, but the code uses `@v7` and check 4.11 forbids v4.
  - `plan.md:51` "the only test project is the architecture test project" was never updated after change 5 added the frontend Vitest setup.
  - `PigeonWatchDbContext.OnModelCreating` uses the documented fallback, but the plan asked for that to be reported and it was not.
  - `deploy-plan.md` names only `architecture-tests` as a required check and leaves out `frontend-build-and-test`.
- **Fix**: One doc sweep correcting these six lines. Note that the plan file is the closed-out record, so its edits would be addenda.
- **Decision**: FIXED. All six corrected:
  - `Api/CLAUDE.md:11` now names `SMOKE_CHECK`.
  - `plan.md:80` typo fixed.
  - `plan.md:388` now says `setup-node@v7`.
  - `plan.md:51` gained an amendment note about the frontend Vitest setup.
  - `plan.md:90` records that the `OnModelCreating` fallback was used.
  - `deploy-plan.md:105` lists both required checks.

### F8 — No time bounds on migrate job and post-deploy probe

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: .github/workflows/deploy-api.yml:38,146
- **Detail**: No job sets `timeout-minutes`, so the default is 360. A hung `efbundle`, for example one waiting on EF's migration lock, would keep the runner firewall rule open for up to 6 h. The probe has `--max-time 120` per attempt but no `--retry-max-time`, so the worst case is about 24 min.
- **Fix**: Add `timeout-minutes: 15` to `migrate` and `--retry-max-time 600` to the probe curl.
- **Decision**: FIXED. `migrate` now has `timeout-minutes: 15` (the last run took 44 s), and the probe curl has `--retry-max-time 600`. The YAML is valid.

### F9 — CI does not check that migrations match the model

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: .github/workflows/api-pr-checks.yml
- **Detail**: Criterion 1.3 (`dotnet ef migrations has-pending-model-changes`) runs only locally. A PR that changes the model without adding a migration passes the gate, deploys, and fails at runtime. The check needs no database.
- **Fix**: Add `dotnet tool restore` and the `has-pending-model-changes` command, both with the existing `--project`/`--startup-project` flags, to the `architecture-tests` job after the build.
- **Decision**: FIXED. The `architecture-tests` job gained `Restore .NET tools` and `Migrations match the model` (`has-pending-model-changes --no-build`). The YAML is valid. It has not run in CI yet.

### F10 — SWA workflow has no permissions block and passes an unused token

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: .github/workflows/azure-static-web-apps-wonderful-sea-07000d90f.yml:1-24
- **Detail**: This change removed the PR trigger. The workflow still passes `repo_token: ${{ secrets.GITHUB_TOKEN }}`, which is used only for PR comments, and it runs with default token permissions.
- **Fix**: Add `permissions: contents: read` and drop `repo_token`.
- **Decision**: FIXED. A top-level `permissions: contents: read` was added and `repo_token` removed (it is `required: false` in the action's `action.yml`). The YAML is valid.

## Triage summary (2026-10-04)

| Outcome | Findings |
|---|---|
| Fixed | F1 (variant: cleanup gated on create success), F2 (Fix A), F4, F5 (Fix A), F6, F7, F8, F9, F10 |
| Accepted as risk | F3 (Fix A: recorded in `deploy-plan.md`, exit by S-01) |

Final local verification after all fixes: solution build has 0 warnings and 0 errors; architecture tests pass 40/40; YAML checks 4.1, 4.6 and 4.11 (YAML part) pass. The workflow changes (F1, F6, F8, F9, F10) have not yet run in CI.
