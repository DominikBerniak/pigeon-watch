# Persistence Wiring Smoke Test Implementation Plan

## Overview

Wire the PigeonWatch API to the already-provisioned Azure SQL database with EF Core, apply a first schema migration from the existing GitHub Actions deploy workflow, and prove the live path end to end with a transactional `GET /health/db` probe. This is roadmap item F-01 and unlocks S-01 (register and log in), the first slice that stores user records.

## Current State Analysis

- The API (`PigeonWatch/Api`, net10.0) is the stock `dotnet new webapi` scaffold, including the `WeatherForecast` sample (`Controllers/WeatherForecastController.cs`, `WeatherForecast.cs`, the `GET /weatherforecast` request in `PigeonWatchApi.http`): no EF Core or SQL packages, no `DbContext`, no migrations, no tests, no health endpoint (`PigeonWatchApi.csproj:10-13`, `Program.cs:12-53`).
- Azure SQL `pigeonwatch-sql` / `pigeonwatch-db` (serverless free tier, auto-pauses, `swedencentral`) is provisioned. The App Service already has `ConnectionStrings__Default` set to `Authentication=Active Directory Managed Identity` (`context/deployment/deploy-plan.md:75`).
- The API's managed identity is a database user with only `db_datareader` and `db_datawriter` (`deploy-plan.md:74`). It cannot create or alter tables.
- `deploy-api.yml` is a single `build-and-deploy` job that logs in with the OIDC service principal `pigeon-watch-api-github-oidc` (Website Contributor on the web app only, no SQL access) and deploys with `azure/webapps-deploy@v3` (`.github/workflows/deploy-api.yml:20-47`).
- The SQL firewall only has the `AllowAzureServices` (`0.0.0.0`) rule; GitHub-hosted runners are not covered by it (`deploy-plan.md:62`).
- The federated credential subject only matches `ref:refs/heads/main`, so Azure login works only from runs on `main` (`deploy-plan.md:88`). `main` is protected and accepts only PR merges, so the migrate job cannot be exercised before the merge.
- The Angular app (`PigeonWatch/Frontend`) is the stock scaffold with one API connectivity page: `src/app/app.ts` calls `/weatherforecast` and `src/app/app.html` renders the result. The page is live on Static Web Apps today, so removing the API sample without changing it would break the deployed frontend. `src/index.html` still has the title `Frontend` and `README.md` is the stock Angular CLI text.
- `dotnet-ef` is not installed; `sqlcmd` and the `MSSQLLocalDB` instance are available locally.
- `deploy-plan.md:76` carries an open task: the data layer must use Azure AD token auth for SQL.

## Desired End State

- `PigeonWatchDbContext` with a throwaway `SmokeChecks` table exists, with one initial EF Core migration checked in.
- Every merge to `main` touching `PigeonWatch/Api/**` runs a `migrate` job before the deploy job. The job applies the migration to Azure SQL as the CI service principal, and a failed migration blocks the deploy.
- `GET https://pigeonwatch-api.azurewebsites.net/health/db` returns 200 after inserting a `SmokeChecks` row in a transaction, reading it back and rolling back. This is verified by CI after deploy and by hand.
- The default scaffolding junk is gone from both projects: no `WeatherForecast` code or requests, and the Angular app is a clean placeholder shell that no longer calls the API.
- Local development runs against SQL LocalDB.
- The runtime identity (the API's managed identity) still has no schema-change rights.

### Key Discoveries:

- The CI identity has no database rights and the runner has no network path to the database; both must be added by hand (role and permission grants are reserved for Dominik in this project, `deploy-plan.md:72-74`).
- Azure SQL free tier auto-pauses, so the first connection after idle can fail while it resumes; the DbContext needs connection retry, which in turn forces user transactions through an execution strategy.
- `Authentication=Active Directory Default` in `Microsoft.Data.SqlClient` picks up the Azure CLI login that `azure/login@v2` leaves behind, so the CI migration needs no secret.
- Production runs `Production` environment, so Swagger is off; the probe is the only live verification surface.
- The API uses MVC controllers (`AddControllers`/`MapControllers` in `Program.cs`), so the probe follows that style even after the sample controller is removed.

## What We're NOT Doing

- No real domain schema (users, sightings); each later slice adds its own tables and migrations.
- No auth, no authorization on `/health/db`, no rate limiting. The probe is anonymous and does not commit data.
- No committed-row smoke endpoint and no migrate-on-startup. Both were considered and declined.
- No dedicated migrator service principal; the existing OIDC service principal is reused.
- No Blob Storage wiring (the `deploy-plan.md:76` Blob half is for S-04).
- No test project, no new frontend features: the Angular changes are limited to removing the scaffold's weather call and leaving a placeholder shell.
- No removal of the Angular `environments/` files, `provideHttpClient()` or the `fileReplacements` wiring; later slices need them.
- No automated migration-down in CI; rollback stays a manual runbook step.
- No deployment slots, no changes to the SQL tier or its firewall baseline.

## Implementation Approach

Start by removing the default scaffolding from both projects in a single phase, so the data work starts from a clean base and the API and frontend change together (the live frontend depends on the weather endpoint). Then build the data layer and probe and prove them against LocalDB, because that needs no Azure access. Then Dominik runs the Azure grants as a gated phase. Then change the workflow so it migrates before it deploys and smoke-tests after. Finally open a pull request from a feature branch and verify live after it merges. `main` accepts only PR merges, so all work lives on a feature branch (for example `feature/persistence-wiring-smoke`). Commits are pushed to that branch as each phase lands, which is safe because `deploy-api.yml` only triggers on pushes to `main` and nothing deploys until the PR merges.

## Critical Implementation Details

- **Timing & lifecycle**: `EnableRetryOnFailure` makes EF reject user-initiated transactions unless they run inside the context's execution strategy. The probe's insert, read-back and rollback must all be wrapped in `CreateExecutionStrategy().ExecuteAsync(...)`, with the transaction created inside the delegate.
- **State sequencing**: the migrate job runs before the deploy job, so a migration can reach the database while the deploy that follows it fails. Migrations must therefore stay additive and backward compatible with the previously deployed code. The reverse (revert the commit, schema stays) is the known manual migration-down gap.
- **Debug & observability**: the new firewall rule can take up to a few minutes to take effect after it is created. The CI step that runs the bundle must retry on connection failure rather than fail on the first attempt.

## Phase 0: Scaffold cleanup

### Overview

Remove the default `dotnet new webapi` and `ng new` sample code from both projects, including the weather forecast connectivity page, and leave a minimal clean base. Both projects change together because the deployed frontend calls `/weatherforecast`.

### Changes Required:

#### 1. Remove the API sample

**File**: `PigeonWatch/Api/Controllers/WeatherForecastController.cs`, `PigeonWatch/Api/WeatherForecast.cs`, `PigeonWatch/Api/PigeonWatchApi.http`

**Intent**: Delete the sample controller and model, and drop the weather request from the HTTP scratch file so nothing references them.

**Contract**: Both source files are deleted. `PigeonWatchApi.http` keeps only the `@PigeonWatchApi_HostAddress` variable. `Program.cs` already contains no sample-specific code, and `AddControllers`/`MapControllers` stay because Phase 1 adds a controller. `GET /weatherforecast` returns 404 afterwards.

#### 2. Replace the Angular connectivity page with a placeholder shell

**File**: `PigeonWatch/Frontend/src/app/app.ts`, `PigeonWatch/Frontend/src/app/app.html`, `PigeonWatch/Frontend/src/app/app.scss`, `PigeonWatch/Frontend/src/index.html`

**Intent**: Remove the weather forecast model, HTTP call and rendering, and leave the root component as a minimal shell that renders the app name and the router outlet.

**Contract**: `App` no longer injects `HttpClient` or imports `environment`, and no `WeatherForecast` interface remains. The template shows the `PigeonWatch` heading and `<router-outlet />` only. `app.scss` keeps the layout rule for `main` and drops the `.error` rule. The `<title>` in `index.html` becomes `PigeonWatch`. `app.config.ts`, `app.routes.ts` and the `environments/` files are left untouched.

#### 3. Replace stock docs

**File**: `PigeonWatch/Frontend/README.md`, `PigeonWatch/Api/CLAUDE.md`, `PigeonWatch/Frontend/CLAUDE.md`

**Intent**: Remove the stock Angular CLI README text and update the two project guides, which still describe the sample code and the pure scaffold state.

**Contract**: `README.md` becomes a short PigeonWatch-specific stub that points to `Frontend/CLAUDE.md` for commands. `Api/CLAUDE.md` drops the sentence telling readers to remove the weather sample. `Frontend/CLAUDE.md` no longer says it is at default `ng new` scaffold state with a connectivity page; it states the current minimal shell. Both guides keep their commands sections.

### Success Criteria:

#### Automated Verification:

- API builds: `dotnet build PigeonWatch/Api/PigeonWatchApi.csproj`
- Frontend builds for production: `npm run build --prefix PigeonWatch/Frontend`
- No weather references remain in source: `git grep -n -i "weather\|forecast" -- PigeonWatch ':!PigeonWatch/Frontend/package-lock.json'` prints nothing
- Running API no longer serves the sample: `curl -s -o /dev/null -w "%{http_code}" http://localhost:5285/weatherforecast` prints `404`

#### Manual Verification:

- `npm start` serves the placeholder at `http://localhost:4200/` with the `PigeonWatch` heading and no errors in the browser console
- The browser tab title reads `PigeonWatch`

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from Dominik that the manual testing was successful before proceeding to the next phase. Phase blocks use plain bullets — the corresponding `- [ ]` checkboxes for these items live in the `## Progress` section at the bottom of the plan. Merging this change to `main` replaces the live connectivity page, so the deployed frontend and API briefly disagree if their deploy workflows finish at different times; this is acceptable because the page has no users yet.

---

## Phase 1: Data layer and local loop

### Overview

Add EF Core, the `DbContext`, the throwaway `SmokeChecks` entity, the initial migration and the `/health/db` probe, and prove them against LocalDB.

### Changes Required:

#### 1. Packages and local tooling

**File**: `PigeonWatch/Api/PigeonWatchApi.csproj`, `.config/dotnet-tools.json` (repo root)

**Intent**: Add the EF Core SQL Server provider and design-time package, and pin `dotnet-ef` as a local tool so CI and the developer use the same version.

**Contract**: `Microsoft.EntityFrameworkCore.SqlServer` and `Microsoft.EntityFrameworkCore.Design` (design-time assets private) on the 10.0 line, matching the project's `net10.0`. Tool manifest pins `dotnet-ef` on the same major version. The manifest lives at the repo root because `dotnet` searches for it upward from the current directory, and every success criterion and the CI migrate job run from the repo root; `dotnet tool restore` then `dotnet ef ... --project PigeonWatch/Api/PigeonWatchApi.csproj` works from there.

#### 2. DbContext and smoke entity

**File**: `PigeonWatch/Api/Data/PigeonWatchDbContext.cs`, `PigeonWatch/Api/Data/SmokeCheck.cs`

**Intent**: Introduce the data layer namespace with the context and one minimal entity used only to prove the persistence path.

**Contract**: `SmokeCheck` has an integer `Id` primary key and a `CreatedAtUtc` timestamp. The context exposes `DbSet<SmokeCheck> SmokeChecks`. Entity and table are marked for removal by a later migration once real tables exist.

#### 3. Registration and connection resilience

**File**: `PigeonWatch/Api/Program.cs`

**Intent**: Register the context against the `ConnectionStrings:Default` configuration key, which is the same key as the existing `ConnectionStrings__Default` App Service setting, so production needs no configuration change.

**Contract**: `AddDbContext<PigeonWatchDbContext>` with the SQL Server provider and `EnableRetryOnFailure` so the first request after a serverless auto-pause survives the resume. No change to the CORS or auth pipeline.

#### 4. Local connection string

**File**: `PigeonWatch/Api/appsettings.Development.json` (or user-secrets if the file cannot be edited)

**Intent**: Point local development at the `MSSQLLocalDB` instance using integrated security.

**Contract**: `ConnectionStrings:Default` targeting `(localdb)\MSSQLLocalDB` with a database named for the project. Read the file before editing; it was not readable during planning because of a deny rule. If editing is blocked, use `dotnet user-secrets` (the project already has a `UserSecretsId`) and note it in the plan's progress.

#### 5. Initial migration

**File**: `PigeonWatch/Api/Migrations/*`

**Intent**: Generate the first migration creating `SmokeChecks` and the `__EFMigrationsHistory` baseline.

**Contract**: One migration named for the smoke table, checked in with its model snapshot. No data seeding.

#### 6. Health probe controller

**File**: `PigeonWatch/Api/Controllers/HealthController.cs`

**Intent**: Expose an anonymous `GET /health/db` that proves the database can be written to and read from, without leaving data behind.

**Contract**: Route `health/db`. Inside the context's execution strategy it begins a transaction, inserts one `SmokeCheck`, reads it back by id, rolls the transaction back, and returns 200 with the read-back values. Any database failure returns 503 with a short status body and no exception detail. The endpoint never commits.

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build PigeonWatch/Api/PigeonWatchApi.csproj`
- Tools restore and EF model matches migrations: `dotnet tool restore && dotnet ef migrations has-pending-model-changes --project PigeonWatch/Api/PigeonWatchApi.csproj`
- Migration applies to LocalDB: `dotnet ef database update --project PigeonWatch/Api/PigeonWatchApi.csproj`
- Local probe returns 200 from the running API: `curl -f http://localhost:5285/health/db`
- Local probe leaves no rows behind: `sqlcmd -S "(localdb)\MSSQLLocalDB" -d PigeonWatch -Q "SELECT COUNT(*) FROM SmokeChecks"` returns 0
- Dependency audit is clean: `dotnet list PigeonWatch/Api/PigeonWatchApi.csproj package --vulnerable --include-transitive`

#### Manual Verification:

- Running the API with `ConnectionStrings__Default` overridden to `Server=(localdb)\NoSuchInstance;Database=PigeonWatch;Trusted_Connection=True` and calling `/health/db` returns 503 with no stack trace or connection string in the body (LocalDB auto-starts on connect, so stopping it does not exercise this path; real transient outages return 503 only after the `EnableRetryOnFailure` budget runs out)
- The database name in the local connection string is the intended one and no secret is committed

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from Dominik that the manual testing was successful before proceeding to the next phase. Phase blocks use plain bullets — the corresponding `- [ ]` checkboxes for these items live in the `## Progress` section at the bottom of the plan. Work on a feature branch, never on `main`. Pushing the branch is safe, since `deploy-api.yml` only runs on `main`; the PR is opened in Phase 4.

---

## Phase 2: Azure access grants (manual)

### Overview

Give the existing OIDC service principal the database and firewall rights it needs to run migrations. These are role and permission grants, which this project reserves for Dominik.

### Changes Required:

#### 1. SQL user and roles for the CI identity

**File**: none (run by Dominik with `sqlcmd -G` as the SQL Azure AD admin; temporary client-IP firewall rule removed afterwards, as in `deploy-plan.md:74`)

**Intent**: Create a database user for the `pigeon-watch-api-github-oidc` service principal that can create and alter schema and read and write data.

**Contract**: `CREATE USER [pigeon-watch-api-github-oidc] FROM EXTERNAL PROVIDER;` followed by membership in `db_ddladmin`, `db_datareader` and `db_datawriter` on `pigeonwatch-db`. The API's managed identity roles stay unchanged.

#### 2. Firewall-management role for the CI identity

**File**: none (run by Dominik with `az role definition create` and `az role assignment create`)

**Intent**: Let the migrate job add and remove its own runner IP firewall rule, and nothing else on the server.

**Contract**: A custom role (for example `PigeonWatch SQL Firewall Operator`) whose `Actions` are only `Microsoft.Sql/servers/read`, `Microsoft.Sql/servers/firewallRules/read`, `Microsoft.Sql/servers/firewallRules/write` and `Microsoft.Sql/servers/firewallRules/delete`, with `AssignableScopes` set to the `pigeonwatch-sql` server resource. It is assigned to the service principal at that server scope only, not the resource group. Built-in `SQL Server Contributor` is not used because it can also delete or rescale `pigeonwatch-db` and change server settings.

#### 3. Record the grants

**File**: `context/deployment/deploy-plan.md`

**Intent**: Document the two grants next to the existing Phase 3 and Phase 4 entries so the access model is recorded in one place.

**Contract**: A short entry listing the SQL user, its three roles and the scoped firewall role, with the verification commands used.

### Success Criteria:

#### Automated Verification:

- SQL user and roles exist: `sqlcmd -S pigeonwatch-sql.database.windows.net -d pigeonwatch-db -G -Q "SELECT dp.name, r.name FROM sys.database_role_members m JOIN sys.database_principals r ON m.role_principal_id = r.principal_id JOIN sys.database_principals dp ON m.member_principal_id = dp.principal_id WHERE dp.name = 'pigeon-watch-api-github-oidc'"`
- Firewall role assignment exists at server scope: `az role assignment list --assignee d5757dbf-4a84-4d27-99e0-edd83841cdb9 --scope <pigeonwatch-sql resource id>`

#### Manual Verification:

- The temporary client-IP firewall rule used for the `sqlcmd` session has been deleted
- The managed identity's roles on the database are unchanged (still reader and writer only)

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from Dominik that the manual testing was successful before proceeding to the next phase. Phase blocks use plain bullets — the corresponding `- [ ]` checkboxes for these items live in the `## Progress` section at the bottom of the plan.

---

## Phase 3: CI migrate job

### Overview

Extend `deploy-api.yml` so schema changes are applied before the application is deployed, and so the live probe runs after it.

### Changes Required:

#### 1. Migrate job

**File**: `.github/workflows/deploy-api.yml`

**Intent**: Add a `migrate` job that builds an EF Core migration bundle and runs it against Azure SQL as the CI service principal, with the runner temporarily allowed through the SQL firewall.

**Contract**: Job keeps `permissions: id-token: write, contents: read`. Steps: checkout, setup-dotnet, `dotnet tool restore`, `azure/login@v2` (same OIDC secrets), discover the runner's public IP, create a uniquely named firewall rule for it with `az sql server firewall-rule create`, build a self-contained linux bundle with `dotnet ef migrations bundle`, run it as `./efbundle --connection "$SQL_CONN"` (the CI host starts as Production and `appsettings.json` has no `ConnectionStrings:Default`, so the connection must be passed explicitly), where `SQL_CONN` uses `Authentication=Active Directory Default` against `pigeonwatch-sql.database.windows.net` / `pigeonwatch-db`, inside a bounded shell retry loop (5 attempts, 30 seconds apart) that fails the job after the last attempt. A final step with `if: always()` deletes the firewall rule. The connection string contains no secret and may live in workflow `env`.

#### 2. Deploy ordering and post-deploy probe

**File**: `.github/workflows/deploy-api.yml`

**Intent**: Make deploy wait on migration success and fail the run if the live API cannot reach its database afterwards.

**Contract**: `build-and-deploy` declares `needs: migrate`. After `azure/webapps-deploy`, a step runs `curl -f --retry 10 --retry-delay 15 --retry-connrefused --max-time 120 https://pigeonwatch-api.azurewebsites.net/health/db`, because the F1 container takes about 32 seconds to start and the serverless database may be resuming. The existing triggers (`push` to `main` on `PigeonWatch/Api/**`, plus `workflow_dispatch`) stay as they are. The workflow gains a top-level `concurrency: { group: deploy-api, cancel-in-progress: false }` so two merges in quick succession run migrate-then-deploy one after the other instead of interleaving.

### Success Criteria:

#### Automated Verification:

- Workflow file is valid YAML with both jobs and the `needs` edge: `python -c "import yaml,sys; d=yaml.safe_load(open('.github/workflows/deploy-api.yml')); n=d['jobs']['build-and-deploy']['needs']; assert 'migrate' in (n if isinstance(n, list) else [n])"`
- No secret value appears in the workflow file: `git grep -n -i "password\|secret" .github/workflows/deploy-api.yml` lists only the three existing `secrets.AZURE_*` references
- Migration bundle builds locally the same way CI builds it: `dotnet ef migrations bundle --project PigeonWatch/Api/PigeonWatchApi.csproj --self-contained -r linux-x64 --output <scratch>/efbundle`

#### Manual Verification:

- Workflow diff reviewed by Dominik, including that the firewall rule name is unique per run and is deleted under `if: always()`

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from Dominik that the manual testing was successful before proceeding to the next phase. Phase blocks use plain bullets — the corresponding `- [ ]` checkboxes for these items live in the `## Progress` section at the bottom of the plan.

---

## Phase 4: Live verification and close-out

### Overview

Open a pull request from the feature branch, have Dominik merge it into `main`, confirm the live path works, and leave the project documentation accurate.

### Changes Required:

#### 1. Open the PR, Dominik merges, and observe the first migrating deploy

**File**: none (push the feature branch and `gh pr create`; Dominik merges the PR; then `gh run watch`)

**Intent**: Run the new workflow for real. `main` only accepts PR merges, and Azure login only works from runs on `main` because the federated credential subject is `ref:refs/heads/main`, so the merge is the first possible live run. The PR itself cannot exercise the migrate job; Phase 3's local bundle build and Dominik's review of the workflow diff are the pre-merge checks.

**Contract**: Push the feature branch with the Phase 0 to 3 commits and open a PR to `main`; the agent creates the PR but never merges it. Dominik reviews and merges the PR, and the agent resumes after the merge to watch the run. Merging triggers `deploy-api.yml` on `main`; `migrate` succeeds, `build-and-deploy` succeeds, the post-deploy probe succeeds. If the OIDC federated-credential propagation or firewall delay bites, rerun with `workflow_dispatch` on `main`. The agent does not enable auto-merge. A failed first run leaves `main` carrying the change, so the fix goes in a follow-up PR from a new branch.

#### 2. Confirm cleanup and runtime privilege

**File**: none (`az sql server firewall-rule list`, `sqlcmd`)

**Intent**: Verify the run left no runner firewall rule behind and that the schema is where it should be.

**Contract**: Only `AllowAzureServices` remains on the server. `__EFMigrationsHistory` lists the initial migration and `SmokeChecks` exists in `pigeonwatch-db`.

#### 3. Update deployment docs and runbook

**File**: `context/deployment/deploy-plan.md`

**Intent**: Close the open Phase 3 data-layer task and record how migrations are applied and rolled back by hand.

**Contract**: Tick the data-layer line in Phase 3 with a pointer to this change. Add a short note on the migrate job, the additive-migrations rule, and the manual migration-down step (run the bundle or `dotnet ef database update <previous>` with the same service principal and a temporary firewall rule).

### Success Criteria:

#### Automated Verification:

- Latest `deploy-api.yml` run on `main` concluded successfully: `gh run list --workflow deploy-api.yml --branch main --limit 1 --json conclusion --jq '.[0].conclusion'` prints `success`
- Live probe returns 200: `curl -f https://pigeonwatch-api.azurewebsites.net/health/db`
- Only the baseline firewall rule remains: `az sql server firewall-rule list --resource-group pigeon-watch-rg --server pigeonwatch-sql --query "[].name" -o tsv` prints only `AllowAzureServices`
- Migration history applied in the live database: `sqlcmd -S pigeonwatch-sql.database.windows.net -d pigeonwatch-db -G -Q "SELECT MigrationId FROM __EFMigrationsHistory"` lists one row

#### Manual Verification:

- `SELECT COUNT(*) FROM SmokeChecks` in the live database returns 0 after several probe calls
- Calling `/health/db` after the database has been idle long enough to auto-pause eventually returns 200 rather than 503
- `deploy-plan.md` reads accurately: Phase 3 data-layer item ticked, runbook note present

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from Dominik that the manual testing was successful before proceeding to the next phase. Phase blocks use plain bullets — the corresponding `- [ ]` checkboxes for these items live in the `## Progress` section at the bottom of the plan.

---

## Testing Strategy

### Unit Tests:

- None. The repository has no test project and this change adds no business logic; adding one is out of scope.

### Integration Tests:

- The migrate job, the post-deploy `curl` probe and the live `/health/db` call together are the end-to-end test of the persistence path.

### Manual Testing Steps:

1. Run the API locally against LocalDB and call `/health/db`; confirm 200 and zero leftover rows.
2. Override `ConnectionStrings__Default` to a non-existent LocalDB instance and call `/health/db`; confirm 503 with no sensitive detail.
3. After the live deploy, call the live probe twice and confirm `SmokeChecks` stays empty.
4. After an idle period, call the live probe to confirm the auto-pause resume path works.

## Performance Considerations

The probe performs one insert, one select and a rollback per call; it is anonymous and unauthenticated, so it is cheap but not free on the shared F1 compute cap. It does not grow the database. No indexing or latency work belongs to this change; the PRD's volume NFR is only exercised by proving the connection path.

## Migration Notes

The first migration creates the throwaway `SmokeChecks` table and the EF history table on an empty database, so there is no existing data to carry. Later migrations must stay additive so the previous deployed build keeps working if a deploy fails after migration. Rollback is manual: use the service principal path in the Phase 4 runbook note.

## References

- Roadmap item: `context/foundation/roadmap.md` (F-01)
- Deployment state: `context/deployment/deploy-plan.md`
- Infrastructure risks: `context/foundation/infrastructure.md`
- Scaffold files removed in Phase 0: `PigeonWatch/Api/Controllers/WeatherForecastController.cs`, `PigeonWatch/Frontend/src/app/app.ts`
- Similar workflow pattern: `.github/workflows/deploy-api.yml`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 0: Scaffold cleanup

#### Automated

- [x] 0.1 API builds: `dotnet build PigeonWatch/Api/PigeonWatchApi.csproj`
- [x] 0.2 Frontend builds for production: `npm run build --prefix PigeonWatch/Frontend`
- [x] 0.3 No weather references remain in source: `git grep -n -i "weather\|forecast" -- PigeonWatch ':!PigeonWatch/Frontend/package-lock.json'` prints nothing
- [x] 0.4 Running API no longer serves the sample: `curl -s -o /dev/null -w "%{http_code}" http://localhost:5285/weatherforecast` prints `404`

#### Manual

- [x] 0.5 `npm start` serves the placeholder at `http://localhost:4200/` with the `PigeonWatch` heading and no errors in the browser console
- [x] 0.6 The browser tab title reads `PigeonWatch`

### Phase 1: Data layer and local loop

#### Automated

- [ ] 1.1 Solution builds: `dotnet build PigeonWatch/Api/PigeonWatchApi.csproj`
- [ ] 1.2 Tools restore and EF model matches migrations: `dotnet tool restore && dotnet ef migrations has-pending-model-changes --project PigeonWatch/Api/PigeonWatchApi.csproj`
- [ ] 1.3 Migration applies to LocalDB: `dotnet ef database update --project PigeonWatch/Api/PigeonWatchApi.csproj`
- [ ] 1.4 Local probe returns 200 from the running API: `curl -f http://localhost:5285/health/db`
- [ ] 1.5 Local probe leaves no rows behind: `sqlcmd -S "(localdb)\MSSQLLocalDB" -d PigeonWatch -Q "SELECT COUNT(*) FROM SmokeChecks"` returns 0
- [ ] 1.6 Dependency audit is clean: `dotnet list PigeonWatch/Api/PigeonWatchApi.csproj package --vulnerable --include-transitive`

#### Manual

- [ ] 1.7 Running the API with `ConnectionStrings__Default` overridden to `Server=(localdb)\NoSuchInstance;Database=PigeonWatch;Trusted_Connection=True` and calling `/health/db` returns 503 with no stack trace or connection string in the body (LocalDB auto-starts on connect, so stopping it does not exercise this path; real transient outages return 503 only after the `EnableRetryOnFailure` budget runs out)
- [ ] 1.8 The database name in the local connection string is the intended one and no secret is committed

### Phase 2: Azure access grants (manual)

#### Automated

- [ ] 2.1 SQL user and roles exist: `sqlcmd -S pigeonwatch-sql.database.windows.net -d pigeonwatch-db -G -Q "SELECT dp.name, r.name FROM sys.database_role_members m JOIN sys.database_principals r ON m.role_principal_id = r.principal_id JOIN sys.database_principals dp ON m.member_principal_id = dp.principal_id WHERE dp.name = 'pigeon-watch-api-github-oidc'"`
- [ ] 2.2 Firewall role assignment exists at server scope: `az role assignment list --assignee d5757dbf-4a84-4d27-99e0-edd83841cdb9 --scope <pigeonwatch-sql resource id>`

#### Manual

- [ ] 2.3 The temporary client-IP firewall rule used for the `sqlcmd` session has been deleted
- [ ] 2.4 The managed identity's roles on the database are unchanged (still reader and writer only)

### Phase 3: CI migrate job

#### Automated

- [ ] 3.1 Workflow file is valid YAML with both jobs and the `needs` edge: `python -c "import yaml,sys; d=yaml.safe_load(open('.github/workflows/deploy-api.yml')); n=d['jobs']['build-and-deploy']['needs']; assert 'migrate' in (n if isinstance(n, list) else [n])"`
- [ ] 3.2 No secret value appears in the workflow file: `git grep -n -i "password\|secret" .github/workflows/deploy-api.yml` lists only the three existing `secrets.AZURE_*` references
- [ ] 3.3 Migration bundle builds locally the same way CI builds it: `dotnet ef migrations bundle --project PigeonWatch/Api/PigeonWatchApi.csproj --self-contained -r linux-x64 --output <scratch>/efbundle`

#### Manual

- [ ] 3.4 Workflow diff reviewed by Dominik, including that the firewall rule name is unique per run and is deleted under `if: always()`

### Phase 4: Live verification and close-out

#### Automated

- [ ] 4.1 Latest `deploy-api.yml` run on `main` concluded successfully: `gh run list --workflow deploy-api.yml --branch main --limit 1 --json conclusion --jq '.[0].conclusion'` prints `success`
- [ ] 4.2 Live probe returns 200: `curl -f https://pigeonwatch-api.azurewebsites.net/health/db`
- [ ] 4.3 Only the baseline firewall rule remains: `az sql server firewall-rule list --resource-group pigeon-watch-rg --server pigeonwatch-sql --query "[].name" -o tsv` prints only `AllowAzureServices`
- [ ] 4.4 Migration history applied in the live database: `sqlcmd -S pigeonwatch-sql.database.windows.net -d pigeonwatch-db -G -Q "SELECT MigrationId FROM __EFMigrationsHistory"` lists one row

#### Manual

- [ ] 4.5 `SELECT COUNT(*) FROM SmokeChecks` in the live database returns 0 after several probe calls
- [ ] 4.6 Calling `/health/db` after the database has been idle long enough to auto-pause eventually returns 200 rather than 503
- [ ] 4.7 `deploy-plan.md` reads accurately: Phase 3 data-layer item ticked, runbook note present
