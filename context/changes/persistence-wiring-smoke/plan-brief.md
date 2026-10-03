# Persistence Wiring Smoke Test — Plan Brief

> Full plan: `context/changes/persistence-wiring-smoke/plan.md`

## What & Why

Connect the PigeonWatch API to the already-provisioned Azure SQL database with EF Core. Apply a first migration from the existing GitHub Actions deploy workflow and prove a record can be written and read in the live environment. This is roadmap item F-01. Without a proven live path, S-01 (register and log in) would fail on infrastructure instead of behaviour.

## Starting Point

Both projects are still stock scaffolds: the API has the `WeatherForecast` sample and no data layer, and the Angular app is a single page that calls that sample endpoint. The App Service already has a managed-identity connection string, but the API's identity can only read and write rows, and the CI identity has no database access at all. The SQL firewall does not admit GitHub runners.

## Desired End State

Every merge to `main` that touches the API migrates the database before deploying, and a post-deploy check confirms the live API can reach its database. `GET /health/db` returns 200 after a write, read-back and rollback. The default scaffolding is removed from both projects, leaving a clean placeholder Angular shell. Local development runs against LocalDB. The runtime identity still cannot change the schema.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) |
| --- | --- | --- |
| How migrations are applied | EF migration bundle in a CI `migrate` job before deploy | Matches the roadmap's "applied through CI" outcome and keeps the runtime identity read/write only. |
| CI identity | Reuse the existing OIDC service principal, with a SQL user in `db_ddladmin`, reader and writer, plus a server-scoped custom role limited to firewall-rule read/write/delete | Avoids repeating the painful OIDC federated-credential setup; accepts one identity both deploying and migrating. |
| Smoke record | Anonymous `GET /health/db` that inserts, reads back and rolls back in a transaction | Proves write and read in the live database with no row growth and no public write surface. |
| Local development DB | SQL LocalDB via `appsettings.Development.json` | Already installed here and uses the same SQL Server engine as Azure SQL. |
| Connection resilience | EF `EnableRetryOnFailure`, probe wrapped in the execution strategy | The serverless database auto-pauses and the first connection can fail while it resumes. |
| Scaffold cleanup | New Phase 0 removes the weather sample from the API and the Angular connectivity page in the same change | The live frontend calls `/weatherforecast`, so the two must change together. |
| Branching | Work on a feature branch; the agent opens the PR and Dominik merges it, since `main` accepts only PR merges | Nothing deploys until the merge, and the OIDC login only works from `main` anyway. |

## Scope

**In scope:** removal of the API weather sample and the Angular connectivity page (placeholder shell, stock README and guide updates), EF Core wiring, `DbContext`, throwaway `SmokeChecks` table and initial migration, `/health/db` probe, CI migrate job with temporary firewall rule, post-deploy probe, manual Azure grants, deployment docs and rollback runbook note.

**Out of scope:** domain schema, auth, rate limiting on the probe, committed-row smoke endpoint, migrate-on-startup, a dedicated migrator identity, Blob Storage wiring, a test project, new frontend features, removing Angular environments/HttpClient wiring, automated migration-down.

## Architecture / Approach

Phase 0 removes the API and Angular scaffold junk so the work starts from a clean base. Phase 1 adds the data layer and probe and proves them on LocalDB. Phase 2 is a gate Dominik runs by hand that gives the CI service principal a SQL user and a scoped firewall role. Phase 3 adds a `migrate` job (login, temporary firewall rule, bundle, apply with retry, rule cleanup under `if: always()`) that `build-and-deploy` depends on, plus a post-deploy `curl` of `/health/db`. Phase 4 opens a PR from the feature branch, Dominik merges it, then the agent watches the first live run on `main` and updates the deployment docs.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 0. Scaffold cleanup | Weather sample removed from the API; Angular placeholder shell; stock docs updated | Deployed frontend calls the removed endpoint until its own deploy finishes. |
| 1. Data layer and local loop | EF Core, `SmokeChecks`, initial migration, `/health/db`, LocalDB setup | Retry strategy rejects user transactions unless the probe uses the execution strategy. |
| 2. Azure access grants (manual) | SQL user and firewall role for the CI service principal | Grants Dominik runs by hand; a temporary client firewall rule must be removed afterwards. |
| 3. CI migrate job | `migrate` job before deploy, post-deploy probe | Firewall rule propagation delay; leaking a rule if cleanup fails. |
| 4. Live verification and close-out | First live migrating deploy, cleanup check, docs update | Azure AD or firewall propagation delay causing a first-run failure. |

**Prerequisites:** Azure CLI logged in, `sqlcmd` available, SQL Azure AD admin access for the grants, and permission to open and merge a PR into `main`.
**Estimated effort:** ~4-5 sessions across 5 phases, with one manual gate in Phase 2.

## Open Risks & Assumptions

- Assumes `Authentication=Active Directory Default` in the bundle connection picks up the Azure CLI login from `azure/login@v2`; Phase 3 and 4 verification will confirm it.
- The migrate job can only run for real after the PR merges, because the OIDC login is limited to `main`; a failed first run is fixed in a follow-up PR.
- A migration can land while the following deploy fails, so migrations must stay additive; rollback is a manual runbook step.
- `appsettings.Development.json` was unreadable during planning because of a deny rule; user-secrets is the fallback.
- The anonymous probe costs a little of the F1 daily compute cap per call.

## Success Criteria (Summary)

- A merge to `main` migrates Azure SQL through CI and deploys only if the migration succeeds.
- `curl https://pigeonwatch-api.azurewebsites.net/health/db` returns 200 on the live API, including after a database auto-pause.
- No runner firewall rule remains, and the runtime identity is still read/write only.
