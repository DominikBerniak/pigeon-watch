# Persistence Wiring Smoke Test Implementation Plan

## Overview

Wire the PigeonWatch API to the already-provisioned Azure SQL database with EF Core, apply a first schema migration from the existing GitHub Actions deploy workflow, and prove the live path end to end with a transactional `GET /health/db` probe. The API is restructured into six layered projects (WebApi.Host, WebApi, DependencyInjection, BusinessLogic, Data, BusinessObjects) with coding rules enforced at build time and architecture tests that guard the layer rules, so every later slice is built on the same guardrails. This is roadmap item F-01 and unlocks S-01 (register and log in), the first slice that stores user records.

## Current State Analysis

- Phase 0 has landed (`bb9952b`): the `WeatherForecast` sample and the Angular connectivity page are gone and the stock docs are updated.
- The API (`PigeonWatch/Api`, net10.0) is still one flat project: `PigeonWatchApi.csproj`, namespace `PigeonWatchApi`, solution `PigeonWatchApi.slnx` with a single project entry. `Program.cs` uses MVC controllers (`AddControllers`/`MapControllers`), CORS for `http://localhost:4200` and Swagger in Development only.
- An uncommitted flat-layout draft of the data layer exists on the feature branch (`Data/PigeonWatchDbContext.cs`, `Data/SmokeCheck.cs`, `Controllers/HealthController.cs`, `Migrations/*`, EF packages in the csproj, `AddDbContext` in `Program.cs`, `.config/dotnet-tools.json` pinning `dotnet-ef` 10.0.12, and a user-secrets LocalDB connection string). It passed the flat-layout gates but was never committed. Phase 1 moves and reshapes it into the layered layout instead of starting over.
- Azure SQL `pigeonwatch-sql` / `pigeonwatch-db` (serverless free tier, auto-pauses, `swedencentral`) is provisioned. The App Service already has `ConnectionStrings__Default` set to `Authentication=Active Directory Managed Identity` (`context/deployment/deploy-plan.md:75`).
- The API's managed identity is a database user with only `db_datareader` and `db_datawriter` (`deploy-plan.md:74`). It cannot create or alter tables.
- `deploy-api.yml` is a single `build-and-deploy` job that logs in with the OIDC service principal `pigeon-watch-api-github-oidc` (Website Contributor on the web app only, no SQL access) and deploys with `azure/webapps-deploy@v3`. It restores and publishes `PigeonWatch/Api/PigeonWatchApi.csproj`, packages from `PigeonWatch/Api/publish`, and triggers on `PigeonWatch/Api/**` (`.github/workflows/deploy-api.yml:7-47`).
- The SQL firewall only has the `AllowAzureServices` (`0.0.0.0`) rule; GitHub-hosted runners are not covered by it (`deploy-plan.md:62`).
- The federated credential subject only matches `ref:refs/heads/main`, so Azure login works only from runs on `main` (`deploy-plan.md:88`). `main` is protected and accepts only PR merges, so the migrate job cannot be exercised before the merge.
- `appsettings.Development.json` cannot be read by the agent because of a deny rule; the local connection string therefore lives in user-secrets under the existing `UserSecretsId`.
- `sqlcmd` and the `MSSQLLocalDB` instance are available locally.
- `deploy-plan.md:76` carries an open task: the data layer must use Azure AD token auth for SQL.

## Desired End State

- The API is split into six projects under `PigeonWatch/Api/`, plus an architecture test project: a thin deployed host `WebApi.Host`, the layers `WebApi -> BusinessLogic -> Data -> BusinessObjects` (WebApi also references BusinessObjects), and a `DependencyInjection` project that owns every service registration. Controllers return API models built from business objects by view model creators; EF entities never leave `Data`.
- Every service, provider, repository, view model creator and mapper has an interface and is registered in DI by the `DependencyInjection` project. The coding rules (no `var`, no static classes except const holders and DI extension classes, primary constructors, no `_` field prefix) fail the build when broken.
- `PigeonWatchDbContext` with a throwaway `SMOKE_CHECK` table exists in `Data`, with one initial EF Core migration checked in there.
- `GET https://pigeonwatch-api.azurewebsites.net/health/db` returns 200 after inserting a `SMOKE_CHECK` row in a transaction, reading it back and rolling back. This is verified by CI after deploy and by hand.
- Architecture tests fail the build when a layer rule is broken, and run in CI before anything migrates or deploys.
- Every merge to `main` touching `PigeonWatch/Api/**` runs `test`, then a `migrate` job, then the deploy job. The migrate job applies the migration to Azure SQL as the CI service principal, and a failed test or migration blocks the deploy.
- Local development runs against SQL LocalDB.
- The runtime identity (the API's managed identity) still has no schema-change rights.

### Key Discoveries:

- The CI identity has no database rights and the runner has no network path to the database; both must be added by hand (role and permission grants are reserved for Dominik in this project, `deploy-plan.md:72-74`).
- Azure SQL free tier auto-pauses, so the first connection after idle can fail while it resumes; the DbContext needs connection retry, which in turn forces user transactions through an execution strategy. That strategy and transaction belong in the Data layer's repository, not in a controller.
- `Authentication=Active Directory Default` in `Microsoft.Data.SqlClient` picks up the Azure CLI login that `azure/login@v2` leaves behind, so the CI migration needs no secret.
- Production runs `Production` environment, so Swagger is off; the probe is the only live verification surface.
- `dotnet ef` needs a startup project that references `Microsoft.EntityFrameworkCore.Design`. With the context in `Data` and the host in `WebApi.Host`, every EF command takes `--project` for `Data` and `--startup-project` for `WebApi.Host`.
- A dedicated DI project that registers WebApi types must reference WebApi, so it cannot also be referenced by a host that lives in WebApi without a circular reference. The host therefore moves into its own `WebApi.Host` project that references both `WebApi` and `DependencyInjection`.
- C# extension methods can only be declared in static classes, so the no-static-classes rule has an explicit exception for the `IServiceCollection` extension classes in `DependencyInjection`.
- Moving the migration files to a new namespace would leave the snapshot and designer inconsistent; because the draft migration was never committed, it is regenerated rather than hand-edited.
- The deployed publish output keeps its location (`PigeonWatch/Api/publish`), which sits outside every project folder, so the package path and the `PigeonWatch/Api/**` trigger filter keep working.

## What We're NOT Doing

- No real domain schema (users, sightings); each later slice adds its own tables and migrations.
- No auth, no authorization on `/health/db`, no rate limiting. The probe is anonymous and does not commit data.
- No committed-row smoke endpoint and no migrate-on-startup. Both were considered and declined.
- No dedicated migrator service principal; the existing OIDC service principal is reused.
- No Blob Storage wiring (the `deploy-plan.md:76` Blob half is for S-04).
- No unit or integration test projects; the only test project is the architecture test project that guards the layer rules.
- No generic repository or unit-of-work abstraction; each repository is written for the queries a slice needs.
- No request-model mappers yet; the first request models arrive with the first slice that accepts input.
- No new frontend features beyond Phase 0's placeholder shell, no removal of the Angular `environments/` files, `provideHttpClient()` or the `fileReplacements` wiring.
- No automated migration-down in CI; rollback stays a manual runbook step.
- No deployment slots, no changes to the SQL tier or its firewall baseline.

## Implementation Approach

Phase 0 is done. Next, Phase 1 builds the layered solution and the data layer and proves them against LocalDB, which needs no Azure access. Phase 2 adds the architecture tests and the written guardrails so the layout cannot drift. Then Dominik runs the Azure grants as a gated phase. Then the workflow changes so it tests and migrates before it deploys and smoke-tests after. Finally a pull request is opened from the feature branch and verified live after it merges. `main` accepts only PR merges, so all work lives on `feature/persistence-wiring-smoke`. Commits are pushed to that branch as each phase lands, which is safe because `deploy-api.yml` only triggers on pushes to `main` and nothing deploys until the PR merges.

### Layer rules (the architectural guardrails)

- **BusinessObjects** (`PigeonWatch.BusinessObjects`): plain domain and result types shared across layers. Depends on no other PigeonWatch project, no EF Core and no ASP.NET.
- **Data** (`PigeonWatch.Data`): DbContext, EF entities (internal), repositories and their interfaces, entity-to-BO mapping, migrations. Depends only on BusinessObjects and EF Core. Repositories return BusinessObjects, never entities. Contains no DI registration code.
- **BusinessLogic** (`PigeonWatch.BusinessLogic`): services and providers implementing the logic. Depends on Data (repository interfaces) and BusinessObjects. Uses no EF Core and no ASP.NET types. Contains no DI registration code.
- **WebApi** (`PigeonWatch.WebApi`): class library with controllers, API models, view model creators and mappers. Depends on BusinessLogic and BusinessObjects. Controllers return only API models and never BusinessObjects or entities. Never uses a Data type or EF Core. Contains no DI registration code and no startup code.
- **DependencyInjection** (`PigeonWatch.DependencyInjection`): the only place services are registered. References WebApi, BusinessLogic, Data and BusinessObjects. Exposes one public entry point `AddPigeonWatch(IConfiguration)`, composed from one registration extension per layer (`AddData`, `AddBusinessLogic`, `AddWebApi`). Registers the DbContext with the SQL Server provider and `EnableRetryOnFailure`. Contains no business logic and no controllers.
- **WebApi.Host** (`PigeonWatch.WebApi.Host`): the deployed ASP.NET Core host and EF startup project. Holds `Program.cs`, the appsettings files, launch settings and the user-secrets id. References WebApi (so MVC discovers its controllers) and DependencyInjection. `Program.cs` configures the pipeline, MVC, Swagger and CORS, and calls only `AddPigeonWatch(builder.Configuration)` for application services. Uses no BusinessLogic, Data or EF Core type in its own code.
- Reference direction: `WebApi.Host -> {WebApi, DependencyInjection}`; `DependencyInjection -> {WebApi, BusinessLogic, Data, BusinessObjects}`; `WebApi -> {BusinessLogic, BusinessObjects}`; `BusinessLogic -> {Data, BusinessObjects}`; `Data -> BusinessObjects`.

### Coding rules

- No `var`; every local declares its type explicitly (generated migration code is exempt).
- No static classes, except classes that only hold `const` values and the `IServiceCollection` extension classes in `DependencyInjection`. Static members on non-static classes (such as `Program.Main`) are allowed.
- Classes with constructor dependencies use primary constructors.
- Private fields are camelCase with no `_` prefix (with primary constructors most classes need no explicit fields).
- Every service, provider, repository, view model creator and mapper has a matching interface (`I<TypeName>`) and is registered in DI by `DependencyInjection`; consumers depend on the interface.
- Every async method takes a `CancellationToken cancellationToken = default` parameter (interface declarations included). Controller actions are the only exception: they take `CancellationToken cancellationToken` without a default, bound by MVC to the request. Lambdas passed to framework APIs are not methods and are exempt.
- Every SQL identifier the application owns (tables, views, columns, keys, foreign keys, indexes, including EF's migrations history table `EF_MIGRATIONS_HISTORY` and its `MIGRATION_ID`/`PRODUCT_VERSION` columns) is UPPER_SNAKE_CASE. C# names stay PascalCase; the model-wide `UpperSnakeCaseNamingConvention` in `Data` derives the SQL names, so entities do not map names by hand. Table and view names are singular (`SMOKE_CHECK`, not `SMOKE_CHECK`), so map each entity with a singular `ToTable` name. The database name (`PigeonWatch`, `pigeonwatch-db`) is exempt.
- Every entity derives from `AuditableEntity` and so carries the audit columns `CREATE_USER`, `CREATE_DATE`, `UPDATE_USER`, `UPDATE_DATE` (all NOT NULL, dates UTC `datetime2`, users `nvarchar(128)`). `AuditSaveChangesInterceptor` fills them on every save: an insert sets all four from the current user and time, an update sets only `UPDATE_*` and never changes `CREATE_*`. The user comes from `ICurrentUserProvider`, which returns `SYSTEM` until authentication arrives in S-01; time comes from the injected `TimeProvider`. Repositories never set audit columns themselves.
- Every table's primary key is a single `ID` column of type `UNIQUEIDENTIFIER`: entities inherit `Guid Id` from the internal abstract `Entity` base (which `AuditableEntity` derives from). Values are generated by EF on insert as sequential GUIDs (EF Core's default for `Guid` keys on SQL Server), which keeps the clustered index from fragmenting; code never assigns `Id` by hand.
- Rules 1, 3 and 4 are enforced at build time by `PigeonWatch/Api/.editorconfig` with `EnforceCodeStyleInBuild`; rules 2, 5, 6, 7, 8 and 9 are enforced by the architecture tests in Phase 2.

## Critical Implementation Details

- **Timing & lifecycle**: `EnableRetryOnFailure` makes EF reject user-initiated transactions unless they run inside the context's execution strategy. The repository's insert, read-back and rollback must all be wrapped in `CreateExecutionStrategy().ExecuteAsync(...)`, with the transaction created inside the delegate.
- **State sequencing**: the migrate job runs before the deploy job, so a migration can reach the database while the deploy that follows it fails. Migrations must therefore stay additive and backward compatible with the previously deployed code. The reverse (revert the commit, schema stays) is the known manual migration-down gap.
- **Debug & observability**: the new firewall rule can take up to a few minutes to take effect after it is created. The CI step that runs the bundle must retry on connection failure rather than fail on the first attempt.
- **Internal entities and EF tooling**: entities are `internal` to `Data`; the DbContext and repositories are `public`, so the EF tools and DI see the context directly. The context's `DbSet` properties are `internal` so no entity type leaks through its public surface. If EF does not pick up the internal `DbSet` properties for the model, configure the entities in `OnModelCreating` instead; treat that as a minor adaptation and report it. WebApi still never uses the context, which the architecture tests enforce.

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

## Phase 1: Layered API and data layer

### Overview

Split the flat API into the six layered projects, move and reshape the uncommitted data layer draft into them, split the `/health/db` probe across the layers, centralise DI registration, enforce the build-time coding rules, and prove everything against LocalDB.

### Changes Required:

#### 1. Project layout, references and solution

**File**: `PigeonWatch/Api/PigeonWatchApi.slnx`, `PigeonWatch/Api/WebApi.Host/PigeonWatch.WebApi.Host.csproj`, `PigeonWatch/Api/WebApi/PigeonWatch.WebApi.csproj`, `PigeonWatch/Api/DependencyInjection/PigeonWatch.DependencyInjection.csproj`, `PigeonWatch/Api/BusinessLogic/PigeonWatch.BusinessLogic.csproj`, `PigeonWatch/Api/Data/PigeonWatch.Data.csproj`, `PigeonWatch/Api/BusinessObjects/PigeonWatch.BusinessObjects.csproj`, removal of `PigeonWatch/Api/PigeonWatchApi.csproj`

**Intent**: Create the six projects as subfolders of `PigeonWatch/Api/` and make the old flat project the `WebApi.Host` project, so history and the existing host configuration carry over.

**Contract**: All six target `net10.0` with nullable and implicit usings on; root namespaces and assembly names are `PigeonWatch.WebApi.Host`, `PigeonWatch.WebApi`, `PigeonWatch.DependencyInjection`, `PigeonWatch.BusinessLogic`, `PigeonWatch.Data` and `PigeonWatch.BusinessObjects`. `WebApi.Host` is the only `Microsoft.NET.Sdk.Web` project; `WebApi` is a class library with a `FrameworkReference` to `Microsoft.AspNetCore.App`; the others are plain class libraries. References follow the reference direction in the layer rules above. The solution file lists all six projects. The tracked host files (`Program.cs`, `appsettings*.json`, `Properties/launchSettings.json`, `PigeonWatchApi.http`) move into `WebApi.Host/` with `git mv` so history is preserved; `appsettings.Development.json` cannot be read by the agent, so if the move is blocked Dominik moves that single file by hand. The `UserSecretsId` stays unchanged on the `WebApi.Host` project so the existing local connection string keeps working, and the launch profile keeps port `5285`. Package pins: `Microsoft.EntityFrameworkCore.SqlServer` 10.0.12 in `Data`; `Microsoft.EntityFrameworkCore.Design` 10.0.12 with private assets in `WebApi.Host` (the EF startup project); the OpenAPI and Swashbuckle packages in `WebApi.Host`. `.config/dotnet-tools.json` at the repo root stays as it is.

#### 2. BusinessObjects

**File**: `PigeonWatch/Api/BusinessObjects/SmokeCheckResult.cs`, `PigeonWatch/Api/BusinessObjects/DatabaseHealthResult.cs`

**Intent**: Define the business types the probe passes between layers, free of any EF or ASP.NET dependency.

**Contract**: `SmokeCheckResult` carries the read-back `Id` (`Guid`) and `CreatedAtUtc`. `DatabaseHealthResult` carries `IsHealthy` plus the optional read-back values. Both are plain immutable types marked for removal together with the smoke table once real types exist.

#### 3. Data layer

**File**: `PigeonWatch/Api/Data/PigeonWatchDbContext.cs`, `PigeonWatch/Api/Data/Entities/Entity.cs`, `PigeonWatch/Api/Data/Entities/AuditableEntity.cs`, `PigeonWatch/Api/Data/Entities/SmokeCheckEntity.cs`, `PigeonWatch/Api/Data/Conventions/UpperSnakeCaseNamingConvention.cs`, `PigeonWatch/Api/Data/Conventions/UpperSnakeCaseMigrationsHistory.cs`, `PigeonWatch/Api/Data/Auditing/ICurrentUserProvider.cs`, `PigeonWatch/Api/Data/Auditing/SystemCurrentUserProvider.cs`, `PigeonWatch/Api/Data/Auditing/AuditSaveChangesInterceptor.cs`, `PigeonWatch/Api/Data/Repositories/ISmokeCheckRepository.cs`, `PigeonWatch/Api/Data/Repositories/SmokeCheckRepository.cs`, `PigeonWatch/Api/Data/Mappers/ISmokeCheckMapper.cs`, `PigeonWatch/Api/Data/Mappers/SmokeCheckMapper.cs`, `PigeonWatch/Api/Data/Migrations/*`

**Intent**: Own everything that touches the database: context, entity, entity-to-BO mapper and the repository that performs the transactional probe.

**Contract**: `SmokeCheckEntity` derives from the internal abstract `AuditableEntity` (`CreateUser`, `CreateDate`, `UpdateUser`, `UpdateDate`) (which derives from the internal abstract `Entity` carrying the `Guid Id` primary key) and adds no columns of its own, mapped to the `SMOKE_CHECK` table with `ID uniqueidentifier` with columns `ID`, `CREATE_USER`, `CREATE_DATE`, `UPDATE_USER`, `UPDATE_DATE`. `PigeonWatchDbContext.ConfigureConventions` adds `UpperSnakeCaseNamingConvention` (internal, `Data/Conventions/`), a model-finalizing convention that rewrites every table, column, key, foreign key and index name to UPPER_SNAKE_CASE. `UpperSnakeCaseMigrationsHistory` (public, `Data/Conventions/`) derives from EF's `SqlServerHistoryRepository` and renames the history columns to `MIGRATION_ID`/`PRODUCT_VERSION`; the history table is `EF_MIGRATIONS_HISTORY`. This uses an EF internal API (warning `EF1001`, suppressed in that one file) and must be rechecked on every EF Core upgrade. `Data/Auditing/` holds `ICurrentUserProvider`, `SystemCurrentUserProvider` (returns `SYSTEM`) and `AuditSaveChangesInterceptor`, which fills the audit columns as the coding rules describe; the mapper returns `CreateDate` as a UTC `DateTime` in `SmokeCheckResult.CreatedAtUtc`. The repository does not set any timestamp itself; the entity is `internal`, while `PigeonWatchDbContext` is `public` and exposes its `DbSet<SmokeCheckEntity>` property as `internal` (a public `DbSet` of an internal entity does not compile). `ISmokeCheckRepository` and its implementation `SmokeCheckRepository` are public (the implementation's primary constructor takes the public context, so `DependencyInjection` can register it and MS DI can activate it) and expose one operation taking `CancellationToken cancellationToken = default` that, inside the context's execution strategy, begins a transaction, inserts one row, reads it back by id, rolls the transaction back, and returns a `SmokeCheckResult`. The entity-to-BO mapping lives in `Data` behind an interface (`ISmokeCheckMapper` / `SmokeCheckMapper`, registered in DI), and the endpoint never commits. `Data` contains no DI registration code. The draft flat-layout migration and the local LocalDB database are discarded and the initial migration is regenerated here under the new namespace (one migration named for the smoke table, checked in with its model snapshot, no seeding). The flat draft files under `PigeonWatch/Api/Data`, `PigeonWatch/Api/Controllers` and `PigeonWatch/Api/Migrations` are removed once their content is moved.

#### 4. BusinessLogic layer

**File**: `PigeonWatch/Api/BusinessLogic/Services/IDatabaseHealthService.cs`, `PigeonWatch/Api/BusinessLogic/Services/DatabaseHealthService.cs`

**Intent**: Hold the logic that decides what a healthy database means, so the controller stays free of it.

**Contract**: `IDatabaseHealthService` exposes one operation returning a `DatabaseHealthResult` and taking `CancellationToken cancellationToken = default`. The implementation calls the smoke check repository, returns a healthy result with the read-back values on success, and returns an unhealthy result with no exception detail on any exception other than request cancellation (`catch (Exception) when (!cancellationToken.IsCancellationRequested)`, as in the draft controller), which propagates. It names no EF Core or SqlClient exception type, so the BusinessLogic layer rule holds; the accepted tradeoff is that a programming bug also surfaces as 503. The service takes `ISmokeCheckRepository` through its primary constructor. `BusinessLogic` contains no DI registration code.

#### 5. WebApi layer

**File**: `PigeonWatch/Api/WebApi/Controllers/HealthController.cs`, `PigeonWatch/Api/WebApi/Models/DatabaseHealthModel.cs`, `PigeonWatch/Api/WebApi/ViewModelCreators/IDatabaseHealthViewModelCreator.cs`, `PigeonWatch/Api/WebApi/ViewModelCreators/DatabaseHealthViewModelCreator.cs`

**Intent**: Expose the anonymous `GET /health/db` and return an API model, never a business object.

**Contract**: Route `health/db`. Controller actions are `async` and return `Task<ActionResult<TApiModel>>` with `TApiModel` from `WebApi.Models`; the health action returns `Task<ActionResult<DatabaseHealthModel>>`. The controller takes `IDatabaseHealthService` and `IDatabaseHealthViewModelCreator` through its primary constructor, calls the service, builds a `DatabaseHealthModel` from the returned `DatabaseHealthResult` through the creator, and returns 200 with the model when healthy and 503 with a short status-only model when not, with no exception detail. `DatabaseHealthModel` is the type that crosses the wire (status plus optional id and timestamp). No `using` of a `PigeonWatch.Data` type appears in `WebApi`, and `WebApi` contains no `Program.cs` and no DI registration code. Folder convention for later slices: API models in `Models/`, creators in `ViewModelCreators/`, mappers in `Mappers/`, controllers in `Controllers/`.

#### 6. DependencyInjection

**File**: `PigeonWatch/Api/DependencyInjection/PigeonWatchServiceCollectionExtensions.cs`, `PigeonWatch/Api/DependencyInjection/DataServiceCollectionExtensions.cs`, `PigeonWatch/Api/DependencyInjection/BusinessLogicServiceCollectionExtensions.cs`, `PigeonWatch/Api/DependencyInjection/WebApiServiceCollectionExtensions.cs`

**Intent**: Hold every service registration in one project, so no layer wires itself and the host has a single entry point.

**Contract**: Static extension classes on `IServiceCollection` (the permitted exception to the no-static-classes rule). `AddData(IConfiguration)` registers `AddDbContext<PigeonWatchDbContext>` against the `ConnectionStrings:Default` key (the same key as the App Service's `ConnectionStrings__Default` setting, so production needs no configuration change) with the SQL Server provider, `EnableRetryOnFailure` so the first request after a serverless auto-pause survives the resume, `MigrationsHistoryTable("EF_MIGRATIONS_HISTORY")`, `ReplaceService<IHistoryRepository, UpperSnakeCaseMigrationsHistory>()` and every registered `ISaveChangesInterceptor`; it registers `TimeProvider.System` (singleton, `TryAdd`), `ICurrentUserProvider` -> `SystemCurrentUserProvider`, `ISaveChangesInterceptor` -> `AuditSaveChangesInterceptor`, `ISmokeCheckRepository` and `ISmokeCheckMapper` as scoped. `AddBusinessLogic()` registers `IDatabaseHealthService` as scoped. `AddWebApi()` registers `IDatabaseHealthViewModelCreator` as scoped. `AddPigeonWatch(IConfiguration)` is the only extension the host calls and composes the three.

#### 7. WebApi.Host

**File**: `PigeonWatch/Api/WebApi.Host/Program.cs` (moved from the flat project), `PigeonWatch/Api/WebApi.Host/appsettings*.json`, `PigeonWatch/Api/WebApi.Host/Properties/launchSettings.json`, `PigeonWatch/Api/WebApi.Host/PigeonWatchApi.http`

**Intent**: Run the API as a thin host that only configures the pipeline and delegates service registration.

**Contract**: `Program.cs` keeps the existing CORS, Swagger-in-Development and pipeline, keeps `AddControllers`/`MapControllers` (controllers come from the referenced `WebApi` assembly; add it as an application part explicitly only if MVC does not discover it), drops the direct `AddDbContext`, and calls `AddPigeonWatch(builder.Configuration)`. It follows the coding rules (no `var`). No `using` of a `PigeonWatch.Data` or `PigeonWatch.BusinessLogic` type appears in `WebApi.Host`.

#### 8. Build-time coding rules

**File**: `PigeonWatch/Api/.editorconfig`, `PigeonWatch/Api/Directory.Build.props`

**Intent**: Make the `var`, primary-constructor and field-naming rules fail the build instead of relying on review.

**Contract**: `Directory.Build.props` sets `EnforceCodeStyleInBuild` to `true` for every project under `PigeonWatch/Api/`. `.editorconfig` sets `csharp_style_var_for_built_in_types`, `csharp_style_var_when_type_is_apparent` and `csharp_style_var_elsewhere` to `false` with `IDE0007`/`IDE0008` at `error`; `csharp_style_prefer_primary_constructors = true` with `IDE0290` at `error`; a naming rule requiring private fields to be camelCase without a prefix with `IDE1006` at `error`. Files under `**/Migrations/` are marked `generated_code = true` so scaffolded migrations are exempt. The solution builds with zero warnings and zero errors under these settings.

#### 9. Local connection string and developer docs

**File**: user-secrets for `PigeonWatch.WebApi.Host` (existing `UserSecretsId`), `PigeonWatch/Api/CLAUDE.md`

**Intent**: Keep local development on LocalDB and make the documented commands and coding rules match the layered layout.

**Contract**: The existing user-secret `ConnectionStrings:Default` targeting `(localdb)\MSSQLLocalDB` and database `PigeonWatch` is kept; nothing secret is committed. `Api/CLAUDE.md` replaces the flat-layout commands: build from the solution, run the `WebApi.Host` project, and every `dotnet ef` command with `--project PigeonWatch/Api/Data/PigeonWatch.Data.csproj --startup-project PigeonWatch/Api/WebApi.Host/PigeonWatch.WebApi.Host.csproj`. It lists the coding rules from this plan.

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build PigeonWatch/Api/PigeonWatchApi.slnx`
- Old flat project is gone: `test ! -e PigeonWatch/Api/PigeonWatchApi.csproj`
- Tools restore and EF model matches migrations: `dotnet tool restore && dotnet ef migrations has-pending-model-changes --project PigeonWatch/Api/Data/PigeonWatch.Data.csproj --startup-project PigeonWatch/Api/WebApi.Host/PigeonWatch.WebApi.Host.csproj`
- Migration applies to a fresh LocalDB database: `dotnet ef database drop --force --project PigeonWatch/Api/Data/PigeonWatch.Data.csproj --startup-project PigeonWatch/Api/WebApi.Host/PigeonWatch.WebApi.Host.csproj && dotnet ef database update --project PigeonWatch/Api/Data/PigeonWatch.Data.csproj --startup-project PigeonWatch/Api/WebApi.Host/PigeonWatch.WebApi.Host.csproj`
- Local probe returns 200 from the running API: `curl -f http://localhost:5285/health/db`
- Local probe leaves no rows behind: `sqlcmd -S "(localdb)\MSSQLLocalDB" -d PigeonWatch -Q "SELECT COUNT(*) FROM SMOKE_CHECK"` returns 0
- Dependency audit is clean: `dotnet list PigeonWatch/Api/WebApi.Host/PigeonWatch.WebApi.Host.csproj package --vulnerable --include-transitive`

#### Manual Verification:

- Running the API with `ConnectionStrings__Default` overridden to `Server=(localdb)\NoSuchInstance;Database=PigeonWatch;Trusted_Connection=True` and calling `/health/db` returns 503 with no stack trace or connection string in the body (LocalDB auto-starts on connect, so stopping it does not exercise this path; real transient outages return 503 only after the `EnableRetryOnFailure` budget runs out)
- The project layout and references match the layer rules in this plan, the response of `/health/db` is the API model (not a business object or entity), the database name in the local connection string is the intended one and no secret is committed
- Adding a `var` local or a `_`-prefixed private field to any non-migration file makes `dotnet build PigeonWatch/Api/PigeonWatchApi.slnx` fail, and removing it makes the build pass again

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from Dominik that the manual testing was successful before proceeding to the next phase. Phase blocks use plain bullets — the corresponding `- [ ]` checkboxes for these items live in the `## Progress` section at the bottom of the plan. Work on a feature branch, never on `main`. Pushing the branch is safe, since `deploy-api.yml` only runs on `main`; the PR is opened in Phase 5. The draft flat-layout code in the working tree is superseded by this phase; the first commit of the layered layout is this phase's commit.

---

## Phase 2: Architecture tests and guardrails

### Overview

Make the layer rules machine-checked and written down, so later slices cannot drift from the layout.

### Changes Required:

#### 1. Architecture test project

**File**: `PigeonWatch/Api/ArchitectureTests/PigeonWatch.ArchitectureTests.csproj`, `PigeonWatch/Api/ArchitectureTests/*.cs`, `PigeonWatch/Api/PigeonWatchApi.slnx`

**Intent**: Add a test project that loads the six assemblies and asserts the layer and coding rules, so a violation fails `dotnet test` locally and in CI.

**Contract**: xUnit test project on `net10.0` with the NetArchTest rules library, referencing all six projects and added to the solution. Rules asserted: BusinessObjects depends on no other PigeonWatch assembly, EF Core or ASP.NET; Data depends on neither BusinessLogic, WebApi, DependencyInjection nor WebApi.Host; BusinessLogic depends on neither WebApi, DependencyInjection, WebApi.Host, EF Core nor ASP.NET; WebApi code depends on neither `PigeonWatch.Data`, `PigeonWatch.DependencyInjection` nor EF Core; WebApi.Host code depends on neither `PigeonWatch.Data`, `PigeonWatch.BusinessLogic` nor EF Core; only DependencyInjection references `Microsoft.Extensions.DependencyInjection` registration APIs (`IServiceCollection`) among the PigeonWatch assemblies other than WebApi.Host; no static classes exist outside const-only classes and the `IServiceCollection` extension classes in DependencyInjection; every type ending in `Service`, `Provider`, `Repository`, `ViewModelCreator` or `Mapper` is non-static and implements an interface named `I<TypeName>`, and that interface is registered in a `ServiceCollection` after calling `AddPigeonWatch` with an in-memory configuration; every table, column, key, foreign key and index name in the built `PigeonWatchDbContext` model (including the migrations history table) matches `^[A-Z][A-Z0-9]*(_[A-Z0-9]+)*$`, and every table and view name (including the migrations history table) ends in a singular word; every entity type has a single-column primary key `ID` of store type `uniqueidentifier`; every entity type derives from `AuditableEntity` and maps `CREATE_USER`, `CREATE_DATE`, `UPDATE_USER` and `UPDATE_DATE` as NOT NULL; every method returning `Task`/`Task<T>`/`ValueTask`/`ValueTask<T>` declared in a PigeonWatch assembly (interfaces included, compiler-generated types excluded) has a `CancellationToken` parameter with a default value, except public actions of types deriving from `ControllerBase`, whose `CancellationToken` parameter has no default; EF entities and the DbContext live only in Data and the entities are not public; controllers (types deriving from `ControllerBase`) live only in `WebApi.Controllers`; types ending in `Repository` live only in `Data.Repositories`, `Service` only in `BusinessLogic.Services`, `ViewModelCreator` only in `WebApi.ViewModelCreators`, and API models only in `WebApi.Models`; every public controller action returns exactly `Task<ActionResult<T>>` with `T` in `WebApi.Models`, which rules out `IActionResult`, non-generic `ActionResult`, synchronous actions and any BusinessObjects or Data type on the wire. The last rule is a reflection-based assertion because the rules library does not inspect return types.

#### 2. Guardrails documentation

**File**: `PigeonWatch/Api/CLAUDE.md`, `PigeonWatch/CLAUDE.md`

**Intent**: Record the layout and layer rules where agents and developers read them first.

**Contract**: `Api/CLAUDE.md` gains an architecture section stating the six projects, the reference direction, the DependencyInjection-only registration rule, what each layer may and may not contain, the data flow (entity to BO in Data, BO to API model in WebApi through view model creators), folder conventions, the fact that the architecture tests enforce it, and the test command. `PigeonWatch/CLAUDE.md` updates its Layout section to the six-project API and points to `Api/CLAUDE.md` for the rules.

### Success Criteria:

#### Automated Verification:

- Architecture tests pass: `dotnet test PigeonWatch/Api/ArchitectureTests/PigeonWatch.ArchitectureTests.csproj`
- Solution still builds: `dotnet build PigeonWatch/Api/PigeonWatchApi.slnx`
- Test project dependency audit is clean: `dotnet list PigeonWatch/Api/ArchitectureTests/PigeonWatch.ArchitectureTests.csproj package --vulnerable --include-transitive`

#### Manual Verification:

- A deliberate violation (a controller action returning a business object, an action declared as `Task<IActionResult>` that returns `Ok(businessObject)`, `WebApi` using a `PigeonWatch.Data` type, a new static helper class, or a service without an interface or registration) makes the architecture tests fail, and removing it makes them pass again
- Both `CLAUDE.md` files describe the layout and the layer rules accurately

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from Dominik that the manual testing was successful before proceeding to the next phase. Phase blocks use plain bullets — the corresponding `- [ ]` checkboxes for these items live in the `## Progress` section at the bottom of the plan.

---

## Phase 3: Azure access grants (manual)

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
- `deploy-plan.md` records the CI SQL user, its three roles and the scoped firewall role with the verification commands used

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from Dominik that the manual testing was successful before proceeding to the next phase. Phase blocks use plain bullets — the corresponding `- [ ]` checkboxes for these items live in the `## Progress` section at the bottom of the plan.

---

## Phase 4: CI test and migrate jobs

### Overview

Extend `deploy-api.yml` so the architecture tests run first, schema changes are applied before the application is deployed, and the live probe runs after it. The workflow is also retargeted to the layered project paths.

### Changes Required:

#### 1. Test job

**File**: `.github/workflows/deploy-api.yml`

**Intent**: Add a `test` job that fails the run before anything touches Azure when a layer rule is broken.

**Contract**: Job `test` on `ubuntu-latest` with `permissions: contents: read`: checkout, setup-dotnet (same `DOTNET_VERSION`), then `dotnet test PigeonWatch/Api/ArchitectureTests/PigeonWatch.ArchitectureTests.csproj`. No Azure login and no secrets.

#### 2. Migrate job

**File**: `.github/workflows/deploy-api.yml`

**Intent**: Add a `migrate` job that builds an EF Core migration bundle and runs it against Azure SQL as the CI service principal, with the runner temporarily allowed through the SQL firewall.

**Contract**: `needs: test`. Job keeps `permissions: id-token: write, contents: read`. Steps, in this order: checkout, setup-dotnet, `dotnet tool restore`, build a self-contained linux bundle with `dotnet ef migrations bundle --project PigeonWatch/Api/Data/PigeonWatch.Data.csproj --startup-project PigeonWatch/Api/WebApi.Host/PigeonWatch.WebApi.Host.csproj`, then `azure/login@v2` (same OIDC secrets), discover the runner's public IP, create a uniquely named firewall rule for it with `az sql server firewall-rule create`, and run the bundle as `./efbundle --connection "$SQL_CONN"` (the CI host starts as Production and `appsettings.json` has no `ConnectionStrings:Default`, so the connection must be passed explicitly), where `SQL_CONN` uses `Authentication=Active Directory Default` against `pigeonwatch-sql.database.windows.net` / `pigeonwatch-db`, inside a bounded shell retry loop (5 attempts, 30 seconds apart) that fails the job after the last attempt. A final step with `if: always()` deletes the firewall rule. The bundle is built before `azure/login` so the slow build does not sit between the OIDC login and the first SQL token request (`Active Directory Default` asks the Azure CLI for a new `database.windows.net` token, which needs a still-valid GitHub OIDC assertion). The connection string contains no secret and may live in workflow `env`.

#### 3. Deploy ordering, retargeted paths and post-deploy probe

**File**: `.github/workflows/deploy-api.yml`

**Intent**: Make deploy wait on migration success, build the layered host, and fail the run if the live API cannot reach its database afterwards.

**Contract**: `build-and-deploy` declares `needs: migrate` and restores and publishes `PigeonWatch/Api/WebApi.Host/PigeonWatch.WebApi.Host.csproj`; the package path stays `PigeonWatch/Api/publish`. After `azure/webapps-deploy`, a step runs `curl -f --retry 10 --retry-delay 15 --retry-connrefused --max-time 120 https://pigeonwatch-api.azurewebsites.net/health/db`, because the F1 container takes about 32 seconds to start and the serverless database may be resuming. The existing triggers (`push` to `main` on `PigeonWatch/Api/**`, plus `workflow_dispatch`) stay as they are. The workflow gains a top-level `concurrency: { group: deploy-api, cancel-in-progress: false }` so two merges in quick succession run test-migrate-deploy one after the other instead of interleaving.

#### 4. PR merge gate (added during implementation)

**File**: `.github/workflows/api-pr-checks.yml`

**Intent**: Catch a broken build or layer rule on the pull request, before it can merge into `main`, instead of only in the post-merge deploy run.

**Contract**: Triggers on `pull_request` targeting `main` with no path filter, because a path-filtered workflow that is a required status check leaves PRs that skip it waiting forever. `permissions: contents: read`, no Azure login and no secrets. One job `architecture-tests`: checkout, setup-dotnet (same `DOTNET_VERSION`), `dotnet build PigeonWatch/Api/PigeonWatchApi.slnx`, then `dotnet test` of the architecture test project with `--no-build`. Concurrency per PR number with `cancel-in-progress: true`. Dominik marks `architecture-tests` as a required status check on `main`'s branch protection by hand (a repository setting, reserved for Dominik).

**Note on change 2 (added during implementation)**: the `migrate` job gains a `Pre-fetch SQL access token` step (`az account get-access-token --resource https://database.windows.net/`) right after `azure/login`, so the SQL token is cached while the GitHub OIDC assertion is still valid; the bundle retries that wait on the firewall rule then reuse the cached token instead of asking for a new one after the assertion may have expired.

### Success Criteria:

#### Automated Verification:

- Workflow file is valid YAML with the job chain `test` -> `migrate` -> `build-and-deploy`: `python -c "import yaml; d=yaml.safe_load(open('.github/workflows/deploy-api.yml')); j=d['jobs']; n=lambda k: (lambda v: v if isinstance(v, list) else [v])(j[k]['needs']); assert 'migrate' in n('build-and-deploy') and 'test' in n('migrate')"`
- No secret value appears in the workflow file: `git grep -n -i "password\|secret" .github/workflows/deploy-api.yml` lists only `secrets.AZURE_CLIENT_ID`, `secrets.AZURE_TENANT_ID` and `secrets.AZURE_SUBSCRIPTION_ID` references (one set in each job that logs in)
- Migration bundle builds locally the same way CI builds it: `dotnet ef migrations bundle --project PigeonWatch/Api/Data/PigeonWatch.Data.csproj --startup-project PigeonWatch/Api/WebApi.Host/PigeonWatch.WebApi.Host.csproj --self-contained -r linux-x64 --output <scratch>/efbundle`
- The deploy host publishes locally the same way CI publishes it: `dotnet publish PigeonWatch/Api/WebApi.Host/PigeonWatch.WebApi.Host.csproj -c Release -o <scratch>/publish`
- PR gate workflow is valid YAML, triggers on pull requests to `main` and has the `architecture-tests` job: `python -c "import yaml; d=yaml.safe_load(open('.github/workflows/api-pr-checks.yml')); assert 'main' in d[True]['pull_request']['branches'] and 'architecture-tests' in d['jobs']"`

#### Manual Verification:

- Workflow diff reviewed by Dominik, including that the firewall rule name is unique per run and is deleted under `if: always()`
- `architecture-tests` is a required status check in `main`'s branch protection, and the Phase 5 PR shows it passing before merge

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from Dominik that the manual testing was successful before proceeding to the next phase. Phase blocks use plain bullets — the corresponding `- [ ]` checkboxes for these items live in the `## Progress` section at the bottom of the plan.

---

## Phase 5: Live verification and close-out

### Overview

Open a pull request from the feature branch, have Dominik merge it into `main`, confirm the live path works, and leave the project documentation accurate.

### Changes Required:

#### 1. Open the PR, Dominik merges, and observe the first migrating deploy

**File**: none (push the feature branch and `gh pr create`; Dominik merges the PR; then `gh run watch`)

**Intent**: Run the new workflow for real. `main` only accepts PR merges, and Azure login only works from runs on `main` because the federated credential subject is `ref:refs/heads/main`, so the merge is the first possible live run. The PR itself cannot exercise the migrate job; Phase 4's local bundle build and publish and Dominik's review of the workflow diff are the pre-merge checks.

**Contract**: Push the feature branch with the Phase 0 to 4 commits and open a PR to `main`; the agent creates the PR but never merges it. Dominik reviews and merges the PR, and the agent resumes after the merge to watch the run. Merging triggers `deploy-api.yml` on `main`; `test` succeeds, `migrate` succeeds, `build-and-deploy` succeeds, the post-deploy probe succeeds. If the OIDC federated-credential propagation or firewall delay bites, rerun with `workflow_dispatch` on `main`. The agent does not enable auto-merge. A failed first run leaves `main` carrying the change, so the fix goes in a follow-up PR from a new branch.

#### 2. Confirm cleanup and runtime privilege

**File**: none (`az sql server firewall-rule list`, `sqlcmd -G` run by Dominik with a temporary client-IP firewall rule, as in `deploy-plan.md:74`)

**Intent**: Verify the run left no runner firewall rule behind and that the schema is where it should be.

**Contract**: Dominik creates a temporary client-IP firewall rule, runs the `sqlcmd` checks (5.4 and 5.5), then deletes the rule. `EF_MIGRATIONS_HISTORY` lists the initial migration and `SMOKE_CHECK` exists in `pigeonwatch-db`. The firewall check (5.3) runs last, after the temporary rule is gone, and only `AllowAzureServices` remains on the server.

#### 3. Update deployment docs and runbook

**File**: `context/deployment/deploy-plan.md`

**Intent**: Close the open Phase 3 data-layer task and record how migrations are applied and rolled back by hand, and how the layered layout changed the deployed project.

**Contract**: Tick the data-layer line in Phase 3 with a pointer to this change. Add a short note on the layered API (the deployed host project is now `PigeonWatch/Api/WebApi.Host/PigeonWatch.WebApi.Host.csproj`), the migrate job, the additive-migrations rule, and the manual migration-down step (run the bundle or `dotnet ef database update <previous>` with the same `--project`/`--startup-project` flags, the same service principal and a temporary firewall rule).

### Success Criteria:

#### Automated Verification:

- Latest `deploy-api.yml` run on `main` concluded successfully: `gh run list --workflow deploy-api.yml --branch main --limit 1 --json conclusion --jq '.[0].conclusion'` prints `success`
- Live probe returns 200: `curl -f https://pigeonwatch-api.azurewebsites.net/health/db`
- Only the baseline firewall rule remains: `az sql server firewall-rule list --resource-group pigeon-watch-rg --server pigeonwatch-sql --query "[].name" -o tsv` prints only `AllowAzureServices` (checked after the temporary client-IP rule for 5.4 and 5.5 is deleted)
- Migration history applied in the live database: `sqlcmd -S pigeonwatch-sql.database.windows.net -d pigeonwatch-db -G -Q "SELECT MIGRATION_ID FROM EF_MIGRATIONS_HISTORY"` lists one row

#### Manual Verification:

- `SELECT COUNT(*) FROM SMOKE_CHECK` in the live database returns 0 after several probe calls
- Calling `/health/db` after the database has been idle long enough to auto-pause eventually returns 200 rather than 503
- `deploy-plan.md` reads accurately: Phase 3 data-layer item ticked, layered-layout and runbook note present

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from Dominik that the manual testing was successful before proceeding to the next phase. Phase blocks use plain bullets — the corresponding `- [ ]` checkboxes for these items live in the `## Progress` section at the bottom of the plan.

---

## Testing Strategy

### Unit Tests:

- None beyond the architecture tests. The repository has no business logic yet; unit and integration test projects are out of scope.

### Architecture Tests:

- `PigeonWatch.ArchitectureTests` asserts the layer rules in Phase 2 and runs in CI before migrate and deploy.

### Integration Tests:

- The migrate job, the post-deploy `curl` probe and the live `/health/db` call together are the end-to-end test of the persistence path.

### Manual Testing Steps:

1. Run the API locally against LocalDB and call `/health/db`; confirm 200 and zero leftover rows.
2. Override `ConnectionStrings__Default` to a non-existent LocalDB instance and call `/health/db`; confirm 503 with no sensitive detail.
3. Introduce a deliberate layer violation and confirm the architecture tests fail.
4. After the live deploy, call the live probe twice and confirm `SMOKE_CHECK` stays empty.
5. After an idle period, call the live probe to confirm the auto-pause resume path works.

## Performance Considerations

The probe performs one insert, one select and a rollback per call; it is anonymous and unauthenticated, so it is cheap but not free on the shared F1 compute cap. It does not grow the database. No indexing or latency work belongs to this change; the PRD's volume NFR is only exercised by proving the connection path. The layering adds only in-process calls.

## Migration Notes

The first migration creates the throwaway `SMOKE_CHECK` table and the `EF_MIGRATIONS_HISTORY` table on an empty database, so there is no existing data to carry. Later migrations must stay additive so the previous deployed build keeps working if a deploy fails after migration. Rollback is manual: use the service principal path in the Phase 5 runbook note. The assembly name of the deployed host changes from `PigeonWatchApi` to `PigeonWatch.WebApi.Host`; App Service detects the entry assembly from the published runtime config, so no App Service setting changes, and the first live deploy confirms it.

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

- [x] 0.1 API builds: `dotnet build PigeonWatch/Api/PigeonWatchApi.csproj` — bb9952b
- [x] 0.2 Frontend builds for production: `npm run build --prefix PigeonWatch/Frontend` — bb9952b
- [x] 0.3 No weather references remain in source: `git grep -n -i "weather\|forecast" -- PigeonWatch ':!PigeonWatch/Frontend/package-lock.json'` prints nothing — bb9952b
- [x] 0.4 Running API no longer serves the sample: `curl -s -o /dev/null -w "%{http_code}" http://localhost:5285/weatherforecast` prints `404` — bb9952b

#### Manual

- [x] 0.5 `npm start` serves the placeholder at `http://localhost:4200/` with the `PigeonWatch` heading and no errors in the browser console — bb9952b
- [x] 0.6 The browser tab title reads `PigeonWatch` — bb9952b

### Phase 1: Layered API and data layer

#### Automated

- [x] 1.1 Solution builds: `dotnet build PigeonWatch/Api/PigeonWatchApi.slnx` — 4448c4f
- [x] 1.2 Old flat project is gone: `test ! -e PigeonWatch/Api/PigeonWatchApi.csproj` — 4448c4f
- [x] 1.3 Tools restore and EF model matches migrations: `dotnet tool restore && dotnet ef migrations has-pending-model-changes --project PigeonWatch/Api/Data/PigeonWatch.Data.csproj --startup-project PigeonWatch/Api/WebApi.Host/PigeonWatch.WebApi.Host.csproj` — 4448c4f
- [x] 1.4 Migration applies to a fresh LocalDB database: `dotnet ef database drop --force --project PigeonWatch/Api/Data/PigeonWatch.Data.csproj --startup-project PigeonWatch/Api/WebApi.Host/PigeonWatch.WebApi.Host.csproj && dotnet ef database update --project PigeonWatch/Api/Data/PigeonWatch.Data.csproj --startup-project PigeonWatch/Api/WebApi.Host/PigeonWatch.WebApi.Host.csproj` — 4448c4f
- [x] 1.5 Local probe returns 200 from the running API: `curl -f http://localhost:5285/health/db` — 4448c4f
- [x] 1.6 Local probe leaves no rows behind: `sqlcmd -S "(localdb)\MSSQLLocalDB" -d PigeonWatch -Q "SELECT COUNT(*) FROM SMOKE_CHECK"` returns 0 — 4448c4f
- [x] 1.7 Dependency audit is clean: `dotnet list PigeonWatch/Api/WebApi.Host/PigeonWatch.WebApi.Host.csproj package --vulnerable --include-transitive` — 4448c4f

#### Manual

- [x] 1.8 Running the API with `ConnectionStrings__Default` overridden to `Server=(localdb)\NoSuchInstance;Database=PigeonWatch;Trusted_Connection=True` and calling `/health/db` returns 503 with no stack trace or connection string in the body (LocalDB auto-starts on connect, so stopping it does not exercise this path; real transient outages return 503 only after the `EnableRetryOnFailure` budget runs out) — 4448c4f
- [x] 1.9 The project layout and references match the layer rules in this plan, the response of `/health/db` is the API model (not a business object or entity), the database name in the local connection string is the intended one and no secret is committed — 4448c4f
- [x] 1.10 Adding a `var` local or a `_`-prefixed private field to any non-migration file makes `dotnet build PigeonWatch/Api/PigeonWatchApi.slnx` fail, and removing it makes the build pass again — 4448c4f

### Phase 2: Architecture tests and guardrails

#### Automated

- [x] 2.1 Architecture tests pass: `dotnet test PigeonWatch/Api/ArchitectureTests/PigeonWatch.ArchitectureTests.csproj` — 8186b77
- [x] 2.2 Solution still builds: `dotnet build PigeonWatch/Api/PigeonWatchApi.slnx` — 8186b77
- [x] 2.3 Test project dependency audit is clean: `dotnet list PigeonWatch/Api/ArchitectureTests/PigeonWatch.ArchitectureTests.csproj package --vulnerable --include-transitive` — 8186b77

#### Manual

- [x] 2.4 A deliberate violation (a controller action returning a business object, an action declared as `Task<IActionResult>` that returns `Ok(businessObject)`, `WebApi` using a `PigeonWatch.Data` type, a new static helper class, or a service without an interface or registration) makes the architecture tests fail, and removing it makes them pass again — 8186b77
- [x] 2.5 Both `CLAUDE.md` files describe the layout and the layer rules accurately — 8186b77

### Phase 3: Azure access grants (manual)

#### Automated

- [x] 3.1 SQL user and roles exist: `sqlcmd -S pigeonwatch-sql.database.windows.net -d pigeonwatch-db -G -Q "SELECT dp.name, r.name FROM sys.database_role_members m JOIN sys.database_principals r ON m.role_principal_id = r.principal_id JOIN sys.database_principals dp ON m.member_principal_id = dp.principal_id WHERE dp.name = 'pigeon-watch-api-github-oidc'"` — 9ec7e6f
- [x] 3.2 Firewall role assignment exists at server scope: `az role assignment list --assignee d5757dbf-4a84-4d27-99e0-edd83841cdb9 --scope <pigeonwatch-sql resource id>` — 9ec7e6f

#### Manual

- [x] 3.3 The temporary client-IP firewall rule used for the `sqlcmd` session has been deleted — 9ec7e6f
- [x] 3.4 The managed identity's roles on the database are unchanged (still reader and writer only) — 9ec7e6f
- [x] 3.5 `deploy-plan.md` records the CI SQL user, its three roles and the scoped firewall role with the verification commands used — 9ec7e6f

### Phase 4: CI test and migrate jobs

#### Automated

- [x] 4.1 Workflow file is valid YAML with the job chain `test` -> `migrate` -> `build-and-deploy`: `python -c "import yaml; d=yaml.safe_load(open('.github/workflows/deploy-api.yml')); j=d['jobs']; n=lambda k: (lambda v: v if isinstance(v, list) else [v])(j[k]['needs']); assert 'migrate' in n('build-and-deploy') and 'test' in n('migrate')"` — b612afe
- [x] 4.2 No secret value appears in the workflow file: `git grep -n -i "password\|secret" .github/workflows/deploy-api.yml` lists only `secrets.AZURE_CLIENT_ID`, `secrets.AZURE_TENANT_ID` and `secrets.AZURE_SUBSCRIPTION_ID` references (one set in each job that logs in) — b612afe
- [x] 4.3 Migration bundle builds locally the same way CI builds it: `dotnet ef migrations bundle --project PigeonWatch/Api/Data/PigeonWatch.Data.csproj --startup-project PigeonWatch/Api/WebApi.Host/PigeonWatch.WebApi.Host.csproj --self-contained -r linux-x64 --output <scratch>/efbundle` — b612afe
- [x] 4.4 The deploy host publishes locally the same way CI publishes it: `dotnet publish PigeonWatch/Api/WebApi.Host/PigeonWatch.WebApi.Host.csproj -c Release -o <scratch>/publish` — b612afe

- [x] 4.6 PR gate workflow is valid YAML, triggers on pull requests to `main` and has the `architecture-tests` job: `python -c "import yaml; d=yaml.safe_load(open('.github/workflows/api-pr-checks.yml')); assert 'main' in d[True]['pull_request']['branches'] and 'architecture-tests' in d['jobs']"` — b612afe

#### Manual

- [x] 4.5 Workflow diff reviewed by Dominik, including that the firewall rule name is unique per run and is deleted under `if: always()` — b612afe
- [ ] 4.7 `architecture-tests` is a required status check in `main`'s branch protection, and the Phase 5 PR shows it passing before merge

### Phase 5: Live verification and close-out

#### Automated

- [ ] 5.1 Latest `deploy-api.yml` run on `main` concluded successfully: `gh run list --workflow deploy-api.yml --branch main --limit 1 --json conclusion --jq '.[0].conclusion'` prints `success`
- [ ] 5.2 Live probe returns 200: `curl -f https://pigeonwatch-api.azurewebsites.net/health/db`
- [ ] 5.3 Only the baseline firewall rule remains: `az sql server firewall-rule list --resource-group pigeon-watch-rg --server pigeonwatch-sql --query "[].name" -o tsv` prints only `AllowAzureServices` (checked after the temporary client-IP rule for 5.4 and 5.5 is deleted)
- [ ] 5.4 Migration history applied in the live database: `sqlcmd -S pigeonwatch-sql.database.windows.net -d pigeonwatch-db -G -Q "SELECT MIGRATION_ID FROM EF_MIGRATIONS_HISTORY"` lists one row

#### Manual

- [ ] 5.5 `SELECT COUNT(*) FROM SMOKE_CHECK` in the live database returns 0 after several probe calls
- [ ] 5.6 Calling `/health/db` after the database has been idle long enough to auto-pause eventually returns 200 rather than 503
- [ ] 5.7 `deploy-plan.md` reads accurately: Phase 3 data-layer item ticked, layered-layout and runbook note present
