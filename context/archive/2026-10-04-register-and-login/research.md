---
date: 2026-10-04T12:05:38+02:00
researcher: Dominik Berniak (via Claude Code /10x-research)
git_commit: de34346c120570aad3d378c76ac9fef355c42f97
branch: chore/archive-persistence-wiring-smoke
repository: DominikBerniak/pigeon-watch
topic: "Internal codebase evidence for S-01 register-and-login (ASP.NET Core Identity bearer tokens + Angular SPA)"
tags: [research, codebase, identity, auth, architecture-tests, ef-core, angular, cors, ci]
status: complete
last_updated: 2026-10-04
last_updated_by: Dominik Berniak (via Claude Code /10x-research)
---

# Research: internal evidence for S-01 register-and-login

**Date**: 2026-10-04T12:05:38+02:00
**Researcher**: Dominik Berniak (via Claude Code /10x-research)
**Git Commit**: de34346c120570aad3d378c76ac9fef355c42f97
**Branch**: chore/archive-persistence-wiring-smoke
**Repository**: DominikBerniak/pigeon-watch

All `path:line` anchors are relative to the repo root and refer to the commit above (working tree clean under `PigeonWatch/`). No builds, tests or migrations were run; behaviour predictions about Identity are inferred from the inspected code, not executed.

## Research Question

What does the existing codebase already do, and what does it constrain, for S-01 (register, log in, log out) given the binding decisions D1–D5 in `external-research.md` (Identity `MapIdentityApi` bearer tokens, email + password, derived display name, composition password rules, no email confirmation, `ClientConfiguration` + `GeneralConfiguration` endpoints)? Specifically: integration points in the layered API, conflicts with the architecture tests, data/audit/migration pipeline, frontend starting point, deploy/CORS topology, and prior commitments that S-01 must honour.

## Summary

1. **The architecture tests are the main planning risk.** Adding stock Identity to `PigeonWatchDbContext` is predicted to fail 4 test methods across 2 files (`DatabaseModelTests`: singular names, `ID uniqueidentifier` PK, `AuditableEntity`; `TypeLocationTests`: entities internal and in Data), plus `LayerDependencyTests` if `MapIdentityApi<ApplicationUser>()` is called from `WebApi.Host`. No test has an allow-list or exclusion hook for framework entity types today. The plan has to choose between scoped test exclusions for Identity types and re-modelling Identity to fit; the composite/int keys of `IdentityUserRole/Login/Token/Claim` cannot reasonably be reshaped, so some exclusion is unavoidable (inference).
2. **Layering fixes where code can live.** `ApplicationUser` must live in `Data` (the only layer that both may use `Microsoft.AspNetCore.*` and is visible to the DbContext). `WebApi` and `WebApi.Host` may not name `ApplicationUser`, `UserManager<ApplicationUser>` or `PigeonWatchDbContext`; `BusinessLogic`/`BusinessObjects` may not use any `Microsoft.AspNetCore.Identity` type. Identity registration therefore belongs in `DependencyInjection`, and endpoint mapping needs either a Host rule exception or a static `IEndpointRouteBuilder` extension class (which `StaticClassTests` currently forbids).
3. **Naming convention already covers Identity tables.** `UpperSnakeCaseNamingConvention` is a model-finalizing convention over all entity types, so Identity tables/columns/keys/indexes become UPPER_SNAKE (e.g. `ASP_NET_USERS`), but table names stay plural unless `ToTable(...)` overrides are added after `base.OnModelCreating`.
4. **Auditing needs a real current-user provider with a fallback.** `AuditSaveChangesInterceptor` calls `ICurrentUserProvider.GetCurrentUserName()` on every save, before filtering entries; registration and failed-login lockout writes happen without an authenticated user, so the new provider must fall back (e.g. `SYSTEM`). Identity entities are not `AuditableEntity`, so they are not stamped.
5. **Prior commitments that land in S-01:** remove or secure `GET /health/db` (accepted risk with exit "no later than S-01"), but `deploy-api.yml` uses it as the post-deploy probe, so the probe must be replaced in the same change; migrations must be additive; no runtime schema creation (runtime identity is reader/writer only).
6. **Frontend is a bare shell.** `provideHttpClient()` is the only HTTP wiring; routes are empty; `environment.apiUrl` exists but is unused; there is no `staticwebapp.config.json`, so SPA deep links (e.g. `/login`) and CSP `globalHeaders` both need that file.
7. **CORS is split between platform and app.** Production SWA origin is enabled through App Service platform CORS (`az webapp cors add`), while the in-app policy hardcodes only `http://localhost:4200`. The App Service is Linux, which affects forwarded-headers handling for IP-partitioned rate limiting.

## Detailed Findings

### 1. Architecture tests vs Identity (`PigeonWatch/Api/ArchitectureTests/`)

How the tests discover things:
- Reflection over the 6 PigeonWatch assemblies, skipping compiler-generated types (`PigeonWatchAssemblies.cs:15-47`). Framework types (`IdentityUser<Guid>`, `MapIdentityApi` endpoints, `RegisterRequest`) are not reflected over.
- The EF model is built from a bare `ServiceCollection` that calls only `AddPigeonWatch(configuration)` with an in-memory connection string (`PigeonWatchServices.cs:12-25`). Anything registered only in `Program.cs` is invisible to the tests; anything the DbContext needs to resolve (interceptor → `ICurrentUserProvider` → its dependencies) must be registered inside `AddPigeonWatch`.
- Entity enumeration reads the EF model, so Identity entity types **are** included: `DatabaseModelTests.cs:174-184` filters only `!IsOwned() && GetTableName() is not null`; `TypeLocationTests.cs:96-103` takes all `context.Model.GetEntityTypes()` CLR types.

Predicted outcome per test with stock `IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>`:

| Test | Anchor | Prediction |
|---|---|---|
| `Model_identifiers_are_upper_snake_case` | `DatabaseModelTests.cs:17-45`, regex `:225` | Pass — the naming convention rewrites Identity names (see §3). |
| `Table_and_view_names_are_singular` | `DatabaseModelTests.cs:69-89` | Fail for the 7 Identity tables (`ASP_NET_USERS`, `_ROLES`, `_USER_ROLES`, `_USER_CLAIMS`, `_USER_LOGINS`, `_USER_TOKENS`, `_ROLE_CLAIMS`). Fixable with `ToTable("User")` etc. after `base.OnModelCreating`. |
| `Entities_have_a_single_uniqueidentifier_ID_primary_key` | `DatabaseModelTests.cs:107-136` | Pass for user and role (Guid key). Fail for `IdentityUserClaim`/`IdentityRoleClaim` (`int` Id) and `IdentityUserRole`/`Login`/`Token` (composite keys). |
| `Entities_derive_from_AuditableEntity_and_map_required_audit_columns` | `DatabaseModelTests.cs:138-172` | Fail for all 7 Identity entity types; `ApplicationUser : IdentityUser<Guid>` cannot also derive from `AuditableEntity` (single inheritance). |
| `Entities_live_only_in_Data_and_are_not_public` | `TypeLocationTests.cs:76-94` | Fail for the 6 framework Identity types (public, outside Data) and for `ApplicationUser` (must be public: an internal type argument on the public `PigeonWatchDbContext` base is a CS0060 error; no `InternalsVisibleTo` exists in the repo). |
| Host layer rule | `LayerDependencyTests.cs:60-68` | Fail if `MapIdentityApi<ApplicationUser>()`, `AddIdentityApiEndpoints<ApplicationUser>()` or `AddEntityFrameworkStores<PigeonWatchDbContext>()` appear in `WebApi.Host` (NetArchTest prefix match includes generic method arguments; worker-inspected `:86-89`). |
| WebApi layer rule | `LayerDependencyTests.cs:51-58` | Fail if a controller uses `UserManager<ApplicationUser>`/`SignInManager<ApplicationUser>`. |
| BusinessLogic / BusinessObjects rules | `LayerDependencyTests.cs:16-27`, `:39-48` | Both forbid `Microsoft.AspNetCore`; every `Microsoft.AspNetCore.Identity.*` type is off-limits there. |
| `No_static_classes_except_const_holders_and_service_collection_extensions` | `StaticClassTests.cs:12-20`, `:38-58` | Fail for a static class holding an `IEndpointRouteBuilder`/`WebApplication` extension or `ClaimsPrincipal` helpers; const-only claim/role name holders pass. |
| `Controller_actions_return_task_of_action_result_of_api_model` | `ControllerActionTests.cs:10-28`, `:65-81` | New actions (logout, configuration) must return exactly `Task<ActionResult<T>>` with `T` in `PigeonWatch.WebApi.Models`; `Task<IActionResult>`, `Task<ActionResult>` and synchronous results fail. `MapIdentityApi` endpoints are not controllers and are not checked. |
| `Api_models_expose_no_business_object_or_data_types` | `ControllerActionTests.cs:30-47`, `:83-119` | `GeneralConfigurationModel` / current-user model must be built from primitives or other `*Model` types. |
| Service registration rules | `ServiceRegistrationTests.cs:7`, `:15-27`, `:58-63`, `:80-105` | Any class ending in `Service`/`Provider`/`Repository`/`ViewModelCreator`/`Mapper` needs a PigeonWatch `I<Name>` interface and a non-factory registration inside `AddPigeonWatch`. |
| Location rules | `TypeLocationTests.cs:9-30` | `*Model` → `WebApi.Models`, `*Service` → `BusinessLogic.Services`, `*Repository` → `Data.Repositories`, `*ViewModelCreator` → `WebApi.ViewModelCreators`. |
| `CancellationTokenTests` | `CancellationTokenTests.cs:10-50` (worker-inspected `:30-41`) | Every `Task`-returning method in PigeonWatch assemblies needs a `CancellationToken` (`= default`, except controller actions which take it without default). Framework overrides without a token fail: a custom `UserClaimsPrincipalFactory<ApplicationUser>` (`CreateAsync`/`GenerateClaimsAsync`), `IEmailSender<ApplicationUser>`, middleware `InvokeAsync(HttpContext)`, `IAuthorizationHandler.HandleAsync`. |

Existing exclusion hooks: none explicit. Implicit ones are the owned/table-less filter (`DatabaseModelTests.cs:178`), compiler-generated skip, `[NonAction]` (`ControllerActionTests.cs:58`), const-only and `IServiceCollection`-extension static exemptions, and the absence of any rule for `DependencyInjection` in `LayerDependencyTests`.

### 2. Backend runtime integration points

- **Pipeline** (`PigeonWatch/Api/WebApi.Host/Program.cs`): `AddControllers` `:11`, Swashbuckle `:12-20`, `AddPigeonWatch` `:22`, CORS policy `"Frontend"` with hardcoded `WithOrigins("http://localhost:4200")`, `AllowAnyHeader`, `AllowAnyMethod`, no credentials `:24-32`; Swagger in Development / `UseHttpsRedirection` otherwise `:36-44`; `UseCors("Frontend")` `:46`; `UseAuthorization()` `:48`; `MapControllers()` `:50`. Not present (whole file inspected): `UseAuthentication`, `AddRateLimiter`/`UseRateLimiter`, explicit `UseRouting`, exception handler / ProblemDetails, Swagger bearer security definition. `AllowAnyHeader` already admits the `Authorization` header.
- **Config**: `appsettings.json` holds only `Logging` and `AllowedHosts` (`:1-9`); no CORS, auth or rate-limit keys. Connection string name `Default` (`DependencyInjection/DataServiceCollectionExtensions.cs:27`), locally from user-secrets. Local API URL `http://localhost:5285` (`WebApi.Host/Properties/launchSettings.json:11`).
- **DI composition** (`DependencyInjection/PigeonWatchServiceCollectionExtensions.cs:8-15`): `AddData(configuration)` → `AddBusinessLogic()` → `AddWebApi()`. `AddData` registers `TimeProvider.System` (`DataServiceCollectionExtensions.cs:19`), scoped `ICurrentUserProvider → SystemCurrentUserProvider` (`:20`), scoped `ISaveChangesInterceptor → AuditSaveChangesInterceptor` (`:21`), and the DbContext resolving interceptors from the request scope (`:23-31`), so a scoped HTTP-based provider will flow into the interceptor. `IHttpContextAccessor` is not registered.
- **Project references / packages**: only `WebApi` has `FrameworkReference Microsoft.AspNetCore.App` (`WebApi/PigeonWatch.WebApi.csproj:12`); `Data` has only `Microsoft.EntityFrameworkCore.SqlServer` 10.0.12 (`Data/PigeonWatch.Data.csproj:12`). `Data` would need `Microsoft.AspNetCore.Identity.EntityFrameworkCore` (10.0.12 to match EF). `DependencyInjection` gets ASP.NET Core transitively via its `WebApi` reference. No `Directory.Packages.props`; `Directory.Build.props` only sets `EnforceCodeStyleInBuild`.
- **Existing vertical slice to copy**: `HealthController` — `[ApiController]`, literal `[Route("health/db")]` (no `api/` prefix), primary-constructor injection, `Task<ActionResult<DatabaseHealthModel>> Get(CancellationToken)`, `StatusCode(503, model)` / `Ok(model)` (`WebApi/Controllers/HealthController.cs:10-27`). Models are `sealed` classes with `required`/`init` properties (`WebApi/Models/DatabaseHealthModel.cs:5-14`). Services return result records rather than throwing (`BusinessLogic/Services/DatabaseHealthService.cs:13-22`; `BusinessObjects/DatabaseHealthResult.cs:3-9`). Repositories wrap user transactions in `CreateExecutionStrategy().ExecuteAsync` because of `EnableRetryOnFailure` (`Data/Repositories/SmokeCheckRepository.cs:13-32`).
- **Implication for logout/display-name code paths (inference from the layer rules)**: a controller cannot touch `UserManager`; the path is WebApi controller → BusinessLogic service → Data repository (which uses `UserManager<ApplicationUser>` or the DbContext) → BusinessObjects record. Id, email and roles for `GeneralConfiguration` can come from `ControllerBase.User` claims (`System.Security.Claims` is not forbidden); the display name needs a lookup unless it is put into claims, and a claims-factory override conflicts with `CancellationTokenTests`.

### 3. Data layer: DbContext, naming, auditing, migrations

- `PigeonWatchDbContext` derives from plain `DbContext` (`Data/PigeonWatchDbContext.cs:7`), adds the naming convention in `ConfigureConventions` (`:11-14`), and its `OnModelCreating` (`:16-23`) does not call `base.OnModelCreating`; switching to `IdentityDbContext<...>` requires calling base first.
- `UpperSnakeCaseNamingConvention` is an `IModelFinalizingConvention` over `GetEntityTypes()` that renames tables, columns, keys, FKs and index names, overriding explicit names (`Data/Conventions/UpperSnakeCaseNamingConvention.cs:9-60`; evidence: `ToTable("SmokeCheck")` became `SMOKE_CHECK` in `Data/Migrations/PigeonWatchDbContextModelSnapshot.cs:52-55`). Index and FK renaming has never been exercised by a real migration (the only migration, `20261003175108_AddSmokeCheck.cs:14-27`, has neither).
- `AuditSaveChangesInterceptor` (`Data/Auditing/AuditSaveChangesInterceptor.cs`) calls `currentUserProvider.GetCurrentUserName()` at `:34` before iterating `ChangeTracker.Entries<AuditableEntity>()` at `:37`; Identity entries are skipped by that filter. Audit user columns are `nvarchar(128)` (`Data/Entities/AuditableEntity.cs:5-16`).
- `ICurrentUserProvider` is public, synchronous, `string GetCurrentUserName()` (`Data/Auditing/ICurrentUserProvider.cs:3-6`); `SystemCurrentUserProvider` returns `"SYSTEM"` (`Data/Auditing/SystemCurrentUserProvider.cs:5-7`). Because the interface lives in `Data` and `WebApi` may not reference `Data`, an `IHttpContextAccessor`-based implementation must live in `Data` (adding the ASP.NET Core framework reference) or `DependencyInjection`, or the interface must move to `BusinessObjects` (a string-returning interface has no ASP.NET dependency) with the implementation in `WebApi`.
- Entities are `internal` with `Guid Id` from `internal abstract class Entity` (`Data/Entities/Entity.cs:3-6`).

### 4. CI/CD and migrations

- PR gate (`.github/workflows/api-pr-checks.yml`): build `:30`, architecture tests `:33`, `dotnet tool restore` `:36`, `dotnet ef migrations has-pending-model-changes --no-build` `:38-43` (so the Identity migration and snapshot must be committed and match the model), frontend build + tests `:45-66`. No integration/API tests.
- Deploy (`.github/workflows/deploy-api.yml`): `test` → `migrate` (self-contained linux-x64 EF bundle `:63-71`, OIDC login `:73-78`, temporary firewall rule `:83-92`, `./efbundle` with 5 retries `:94-104`) → `build-and-deploy` (`azure/webapps-deploy` with `clean: true` `:140-145`, then probe of `/health/db` `:148`). The workflow sets no App Service app settings; they are set manually (`context/deployment/deploy-plan.md:75`, pattern `Section__Key`).
- Migration rules: additive and backward compatible only (`context/deployment/deploy-plan.md:109`); CI principal has DDL rights, the runtime managed identity is reader/writer only, migrate-on-startup was rejected (archived plan, `context/archive/2026-10-03-persistence-wiring-smoke/plan.md:30,48,299`).

### 5. Frontend starting point (`PigeonWatch/Frontend/`)

- Versions: Angular `^22.2.0` (`package.json:14-19,24-26`), TypeScript `~6.0.2` (`:29`), Vitest `^5.0.3` (`:30`), jsdom `^30.1.1` (`:27`). No UI library (Material/CDK/Tailwind not in `package.json` or `node_modules/@angular`). No `zone.js` dependency.
- `app.config.ts:7-11`: `provideBrowserGlobalErrorListeners()`, `provideRouter(routes)`, plain `provideHttpClient()`. Not present: `withInterceptors`, `provideAppInitializer`, router feature options.
- `app.routes.ts:3`: empty `routes`. `app.ts:4-10`: standalone root with `RouterOutlet`, no signals/OnPush. `app.html:1-5`: heading in `<main>`, `<router-outlet />` outside it. `index.html`: no CSP meta.
- `environment.ts:1-4` `apiUrl: 'http://localhost:5285'`; `environment.prod.ts:1-4` `apiUrl: 'https://pigeonwatch-api.azurewebsites.net'`; `fileReplacements` only in the production configuration (`angular.json:74-79`). Nothing imports `environment` yet.
- Tests: one spec `app.spec.ts:5-19` using Vitest globals (`tsconfig.spec.json:6-8`); schematics default `skipTests: true` (`angular.json:12-37`).
- Strictness: `noPropertyAccessFromIndexSignature`, `noImplicitReturns`, `noImplicitOverride` set (`tsconfig.json:5-15`); `strictTemplates` not set (`:17-21`); `strict` not set explicitly (whether TS 6 defaults it on is unverified). No ESLint.
- SWA: `staticwebapp.config.json` absent from the repo (searched `git ls-files` and filesystem); `public/` holds only `favicon.ico`. Workflow `azure-static-web-apps-wonderful-sea-07000d90f.yml` triggers only on push to `main` with path filter `PigeonWatch/Frontend/**` (`:3-8`), `app_location: PigeonWatch/Frontend`, `api_location: ""`, `output_location: dist/Frontend/browser` (`:30-32`); no PR preview environments. A config file placed in `public/` is copied to the output (inference from `angular.json:49-54`).

### 6. Deployment topology relevant to auth

- API `https://pigeonwatch-api.azurewebsites.net` (`context/deployment/deploy-plan.md:13`); SPA `https://wonderful-sea-07000d90f.6.azurestaticapps.net` (`deploy-plan.md:19`).
- Production CORS for the SPA origin is configured at the App Service platform level (`deploy-plan.md:125`), not in `Program.cs`. When platform CORS is enabled it handles CORS instead of the app (external behaviour, not verified here).
- App Service plan is **Linux** F1 (`deploy-plan.md:54`, `"tier": "LinuxFree"`). `external-research.md` notes Linux may need `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` for client-IP rate-limit partitioning (listed there as unverified).
- No slots (`context/foundation/infrastructure.md:61,94`), so no slot-swap Data Protection key loss; deploys use `clean: true` on `wwwroot` (`deploy-plan.md:103`). No Data Protection configuration exists in the repo.
- Cold start ~32 s after idle and a possible 503 on the first DB call after auto-pause (`deploy-plan.md:134,138`); login will be the first user-facing request after idle.

## Code References

- `PigeonWatch/Api/WebApi.Host/Program.cs:24-32,46-50` - CORS policy and middleware order; insertion points for authentication and rate limiting.
- `PigeonWatch/Api/DependencyInjection/DataServiceCollectionExtensions.cs:17-37` - DbContext, interceptor, current-user provider registration.
- `PigeonWatch/Api/Data/PigeonWatchDbContext.cs:7-23` - base class and `OnModelCreating` without base call.
- `PigeonWatch/Api/Data/Conventions/UpperSnakeCaseNamingConvention.cs:9-60` - model-wide SQL naming.
- `PigeonWatch/Api/Data/Auditing/AuditSaveChangesInterceptor.cs:34-37` - unconditional user lookup, `AuditableEntity`-only stamping.
- `PigeonWatch/Api/Data/Auditing/ICurrentUserProvider.cs:3-6` - provider contract.
- `PigeonWatch/Api/ArchitectureTests/DatabaseModelTests.cs:69-89,107-136,138-172,174-184` - rules Identity tables break; entity enumeration.
- `PigeonWatch/Api/ArchitectureTests/TypeLocationTests.cs:76-103` - internal-entity rule.
- `PigeonWatch/Api/ArchitectureTests/LayerDependencyTests.cs:16-68` - forbidden dependencies per layer.
- `PigeonWatch/Api/ArchitectureTests/StaticClassTests.cs:12-58` - static-class allow-list.
- `PigeonWatch/Api/ArchitectureTests/ControllerActionTests.cs:10-81` - action return-type rule.
- `PigeonWatch/Api/WebApi/Controllers/HealthController.cs:10-27` - controller pattern.
- `.github/workflows/api-pr-checks.yml:38-43` - pending-model-changes gate.
- `.github/workflows/deploy-api.yml:63-104,148` - migration bundle and `/health/db` post-deploy probe.
- `PigeonWatch/Frontend/src/app/app.config.ts:7-11` - current providers.
- `PigeonWatch/Frontend/src/environments/environment.prod.ts:1-4` - production API URL.

## Architecture Insights

- Rules are enforced by name suffix and namespace, so naming choices decide which rules apply: e.g. a class named `*Provider` must have a PigeonWatch `I*Provider` interface and DI registration, while a `*Model` must live in `WebApi.Models`.
- `DependencyInjection` is the only one of the 6 layers without a rule in `LayerDependencyTests.cs:13-70` and, per `PigeonWatch/Api/CLAUDE.md` and `ServiceRegistrationTests.cs:58-63`, the place application services are registered (Host is exempt from the registration-API test but limited by CLAUDE.md to `AddPigeonWatch` plus framework setup), which makes it the natural home for `AddIdentityApiEndpoints<ApplicationUser>().AddRoles<...>().AddEntityFrameworkStores<PigeonWatchDbContext>()` and Identity options (password, lockout, unique email). Endpoint mapping (`MapIdentityApi`) is an app-builder call, not a service registration, so it needs a deliberate rule change wherever it goes.
- Errors are modelled as result records, not exceptions; controllers translate results to status codes. Identity's `IdentityResult` errors would need translating into BusinessObjects results inside `Data` before they reach `BusinessLogic`.
- The architecture tests build the DbContext offline from `AddPigeonWatch`, so a broken DI graph for the interceptor (e.g. missing `IHttpContextAccessor`) fails `DatabaseModelTests` and `TypeLocationTests`, not just runtime.

## Historical Context (from prior changes)

- `context/archive/2026-10-03-persistence-wiring-smoke/reviews/impl-review.md:79-96` (F3) and `context/deployment/deploy-plan.md:139` — "remove or secure `/health/db` no later than S-01". Still open; S-01 must also replace the post-deploy probe at `deploy-api.yml:148`.
- `impl-review.md:112-137` (F5) — SqlClient/`System.Data.Common` bans added specifically for S-01; nested business-object check in API models. Verdict: supported, present in `LayerDependencyTests.cs` and `ControllerActionTests.cs`.
- `deploy-plan.md:138` / impl-review F2 — tune retry budget / `Connect Timeout` "before a user-facing slice depends on the first request after idle". Login is that slice; still open.
- `archive/.../plan.md:464-476` — testing strategy for F-01 was architecture tests only; none of the inspected docs forbids S-01 adding unit/integration tests.
- `PigeonWatch/Api/CLAUDE.md:11` — `SMOKE_CHECK` and smoke types are throwaway, "removed once real tables exist". S-01 adds the first real tables; whether to remove them in S-01 is a plan choice.
- `context/foundation/infrastructure.md:79,81` — expects a "JWT signing key" app setting. Verdict: contradicted by D1 for S-01 (Identity bearer tokens are Data Protection-protected, not signed JWTs); the rest of the line (secrets in App Service configuration) still holds.
- `context/foundation/roadmap.md:105-106` — S-01 unknown "email/password or OAuth". Resolved by D2 (email + password).
- `context/foundation/prd.md:133` — flat member permissions plus "a single admin role" for moderation; role assignment is an S-10 unknown (`roadmap.md:218-220`). Supports registering roles now (external-research recommendation 1) without seeding admin in S-01.
- `prd.md:44,116` — contact info "never exposed directly to other users". The email must not appear in other users' payloads; it may appear in the caller's own `GeneralConfiguration`. Writing email into audit columns is not covered by this guardrail, but a user id is the more conservative audit value (inference).
- `context/foundation/lessons.md:5-10` — commands for the user in PowerShell use `curl.exe`.

## Related Research

- `context/changes/register-and-login/external-research.md` — external evidence and binding decisions D1–D5.
- No other `research.md` exists under `context/changes/**` or `context/archive/**`.

## Open Questions

Plan-level choices surfaced by this research (in addition to the three open items in `external-research.md`: refresh-token storage, minimum password length, display-name storage):

1. **Architecture-test strategy for Identity**: scoped exclusions (e.g. skip entity types from Identity assemblies; allow public `ApplicationUser`; exempt from `AuditableEntity`) vs partial re-modelling (singular `ToTable` names on Identity tables — cheap and compatible with both). Composite/int keys need an exclusion either way.
2. **Where `MapIdentityApi<ApplicationUser>()` is called**: Host with a targeted Host-rule exception, or a DependencyInjection endpoint extension with a `StaticClassTests` allow-list change. Also whether to expose all `MapIdentityApi` endpoints or only `/register`, `/login`, `/refresh` (D4 makes `/confirmEmail`, `/forgotPassword`, `/resetPassword` unused; `MapIdentityApi` maps them all).
3. **`ICurrentUserProvider` home and value**: implementation in `Data` (add ASP.NET Core framework reference) vs move interface to `BusinessObjects` and implement in `WebApi`; audit value = user id (recommended by privacy reasoning) vs email; fallback `SYSTEM` for anonymous saves.
4. **Should `ApplicationUser` carry audit columns?** It cannot derive from `AuditableEntity`; auditing it would need an interface-based interceptor change.
5. **CORS ownership**: keep App Service platform CORS or move to config-driven app CORS (e.g. `Cors:AllowedOrigins`) and remove the platform rule; new app settings must be added manually (no workflow step sets them).
6. **`/health/db` fate and post-deploy probe replacement** (e.g. anonymous `ClientConfiguration` as the probe, noting it would no longer prove DB connectivity).
7. **Frontend `staticwebapp.config.json`** with `navigationFallback` (needed for `/login` deep links) and the CSP `globalHeaders` from D1.

Unverified technical facts (verify during implementation, e.g. in the generated migration):
- Whether SQL Server filtered unique-index filters (`[NormalizedUserName] IS NOT NULL`) are regenerated with the renamed `NORMALIZED_USER_NAME` column; the naming convention does not touch filters.
- Which Identity schema version is active by default in 10.0.12 stores; if Version3 (passkeys) is on, an extra passkey table with a `byte[]` key appears.
- Whether `AddIdentityApiEndpoints` registers `IHttpContextAccessor` itself (registering it explicitly in `AddPigeonWatch` is safe either way).
- Data Protection key persistence path on Linux App Service F1 and its interaction with `clean: true`.
