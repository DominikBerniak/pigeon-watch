# Persistence Wiring Smoke Test — Plan Brief

> Full plan: `context/changes/persistence-wiring-smoke/plan.md`

## What & Why

Connect the PigeonWatch API to the already-provisioned Azure SQL database with EF Core. Apply a first migration from the existing GitHub Actions deploy workflow and prove a record can be written and read in the live environment. Build it on a layered six-project API (WebApi.Host, WebApi, DependencyInjection, BusinessLogic, Data, BusinessObjects) guarded by build-time coding rules and architecture tests, so every later slice follows the same rules. This is roadmap item F-01. Without a proven live path, S-01 (register and log in) would fail on infrastructure instead of behaviour.

## Starting Point

Phase 0 has landed (`bb9952b`): the weather sample and the Angular connectivity page are gone. The API is still one flat project, and an uncommitted flat-layout draft of the data layer sits on the feature branch. The App Service already has a managed-identity connection string, but the API's identity can only read and write rows, and the CI identity has no database access at all. The SQL firewall does not admit GitHub runners.

## Desired End State

The API is split into `PigeonWatch.WebApi.Host`, `PigeonWatch.WebApi`, `PigeonWatch.DependencyInjection`, `PigeonWatch.BusinessLogic`, `PigeonWatch.Data` and `PigeonWatch.BusinessObjects` under `PigeonWatch/Api/`. Controllers return API models built from business objects by view model creators. Every service, repository, creator and mapper sits behind an interface registered by the DependencyInjection project. Every merge to `main` that touches the API runs architecture tests, migrates the database, then deploys, and a post-deploy check confirms the live API can reach its database. `GET /health/db` returns 200 after a write, read-back and rollback. Local development runs against LocalDB. The runtime identity still cannot change the schema.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) |
| --- | --- | --- |
| Project layout | Six projects as subfolders of `PigeonWatch/Api/`, assemblies named `PigeonWatch.*`, old flat project becomes the deployed `WebApi.Host` | Keeps the workflow path filter, publish path and Api guide scope working. |
| Reference direction | `WebApi -> BusinessLogic -> Data -> BusinessObjects`, WebApi also -> BusinessObjects; `DependencyInjection` references all layers and is the only place services are registered; `WebApi.Host` references WebApi and DependencyInjection and calls only `AddPigeonWatch` | One registration point, and the host split avoids a WebApi <-> DI circular reference. |
| Coding rules | No `var`, no static classes (except const holders and DI extension classes), primary constructors, no `_` field prefix, interface + DI registration for every service/provider/repository/creator/mapper, `CancellationToken cancellationToken = default` on every async method except controller actions, UPPER_SNAKE_CASE SQL names via a model-wide convention (EF history table included), audit columns `CREATE_USER`/`CREATE_DATE`/`UPDATE_USER`/`UPDATE_DATE` on every table filled by a SaveChanges interceptor, `ID UNIQUEIDENTIFIER` primary key on every table (EF sequential GUIDs) | Enforced by `.editorconfig` with `EnforceCodeStyleInBuild` and by the architecture tests. |
| Entities vs business objects | EF entities internal to Data (DbContext and repositories public, `DbSet` properties internal), repositories map to and return BusinessObjects | Persistence shape can change without touching logic or controllers. |
| Wire types | Controllers return API models only, built by view model creators from BOs | Business objects and entities never cross the HTTP boundary. |
| Guardrail enforcement | Project references plus an architecture test project (xUnit, NetArchTest) run in CI, plus a guardrails section in `Api/CLAUDE.md` | A layer breach fails the build instead of relying on convention. |
| How migrations are applied | EF migration bundle in a CI `migrate` job before deploy | Matches the roadmap's "applied through CI" outcome and keeps the runtime identity read/write only. |
| CI identity | Reuse the existing OIDC service principal, with a SQL user in `db_ddladmin`, reader and writer, plus a server-scoped custom role limited to firewall-rule read/write/delete | Avoids repeating the OIDC federated-credential setup; accepts one identity both deploying and migrating. |
| Smoke record | Anonymous `GET /health/db` that inserts, reads back and rolls back in a transaction inside the repository | Proves write and read in the live database with no row growth and no public write surface. |
| Local development DB | SQL LocalDB via user-secrets | Already installed here, same engine as Azure SQL, and the deny rule blocks editing `appsettings.Development.json`. |
| Connection resilience | EF `EnableRetryOnFailure`, probe wrapped in the execution strategy | The serverless database auto-pauses and the first connection can fail while it resumes. |
| Branching | Feature branch; the agent opens the PR and Dominik merges, since `main` accepts only PR merges | Nothing deploys until the merge, and the OIDC login only works from `main` anyway. |

## Scope

**In scope:** the six-project split with central DI and build-time coding rules with moved draft data layer, API models and view model creators, EF Core wiring, `SMOKE_CHECK` table and regenerated initial migration, `/health/db` probe split across layers, architecture tests and guardrail docs, CI test and migrate jobs with temporary firewall rule, post-deploy probe, manual Azure grants, deployment docs and rollback runbook note.

**Out of scope:** domain schema, auth, rate limiting on the probe, committed-row smoke endpoint, migrate-on-startup, a dedicated migrator identity, Blob Storage wiring, unit and integration test projects, generic repository abstractions, request-model mappers, new frontend features, automated migration-down.

## Architecture / Approach

Phase 0 is done. Phase 1 creates the six projects and the central DI registration, moves the draft data layer into them (regenerating the migration under the new namespace), splits the probe into controller, service and repository, and proves it on LocalDB. Phase 2 adds the architecture test project and writes the guardrails into the two CLAUDE.md files. Phase 3 is a gate Dominik runs by hand that gives the CI service principal a SQL user and a scoped firewall role. Phase 4 adds a `test` job, a `migrate` job (login, temporary firewall rule, bundle with `--project`/`--startup-project`, apply with retry, cleanup under `if: always()`) and a post-deploy `curl`, chained `test -> migrate -> build-and-deploy`. Phase 5 opens a PR, Dominik merges it, then the agent watches the first live run and updates the deployment docs.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 0. Scaffold cleanup (done) | Weather sample removed; Angular placeholder shell; stock docs updated | Deployed frontend calls the removed endpoint until its own deploy finishes. |
| 1. Layered API and data layer | Six projects, central DI, build-time coding rules, EF Core in Data, `SMOKE_CHECK`, regenerated migration, `/health/db` across layers, LocalDB proof | Internal entities and the EF tools; MVC discovering controllers from the WebApi library; moving a file the agent cannot read. |
| 2. Architecture tests and guardrails | Test project asserting layer rules, guardrail docs | Rules too loose to catch real drift, or so strict they block valid code. |
| 3. Azure access grants (manual) | SQL user and firewall role for the CI service principal | Grants Dominik runs by hand; a temporary client firewall rule must be removed afterwards. |
| 4. CI test and migrate jobs | `test` and `migrate` jobs before deploy, retargeted paths, post-deploy probe | Firewall rule propagation delay; leaking a rule if cleanup fails. |
| 5. Live verification and close-out | First live migrating deploy, cleanup check, docs update | Azure AD or firewall propagation delay, or the renamed host assembly failing to start. |

**Prerequisites:** Azure CLI logged in, `sqlcmd` available, SQL Azure AD admin access for the grants, and permission to open and merge a PR into `main`.
**Estimated effort:** ~5-6 sessions across 6 phases (Phase 0 done), with one manual gate in Phase 3.

## Open Risks & Assumptions

- Assumes `Authentication=Active Directory Default` in the bundle connection picks up the Azure CLI login from `azure/login@v2`; Phase 4 and 5 verification will confirm it.
- The migrate job can only run for real after the PR merges, because the OIDC login is limited to `main`; a failed first run is fixed in a follow-up PR.
- A migration can land while the following deploy fails, so migrations must stay additive; rollback is a manual runbook step.
- The host assembly is renamed to `PigeonWatch.WebApi.Host`; App Service should detect it from the runtime config, and the first live deploy confirms it.
- `appsettings.Development.json` is unreadable to the agent, so moving it may need Dominik's hand; the local connection string is already in user-secrets.
- The anonymous probe costs a little of the F1 daily compute cap per call.

## Success Criteria (Summary)

- A merge to `main` passes the architecture tests, migrates Azure SQL through CI and deploys only if both succeed.
- `curl https://pigeonwatch-api.azurewebsites.net/health/db` returns 200 on the live API, including after a database auto-pause.
- A layer-rule violation fails the architecture tests, and no runner firewall rule remains after a run.
