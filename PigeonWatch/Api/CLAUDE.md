# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

Scope: `PigeonWatch/Api/` only — the ASP.NET Core Web API. See `@PigeonWatch/CLAUDE.md` for what PigeonWatch is and how Api/Frontend fit together.

## What this owns

ASP.NET Core Web API (.NET 10). Owns auth and the status-workflow/urgency-ranking business logic for sightings (spotted → contacted → taken to vet → healed/returned). Solution: `PigeonWatchApi.slnx` with six layered projects plus the `ArchitectureTests/PigeonWatch.ArchitectureTests.csproj` test project.

Data layer: EF Core (SQL Server) via `Data/PigeonWatchDbContext.cs`, migrations in `Data/Migrations/`. `GET /health/db` is a transactional smoke probe (insert, read back, roll back). The `SMOKE_CHECK` table and the `SmokeCheckResult`/`DatabaseHealthResult` types are throwaway and will be removed once real tables exist. No auth and no business logic yet; the only tests are the architecture tests.

## Architecture

Six projects under `PigeonWatch/Api/`, each with matching root namespace and assembly name `PigeonWatch.<Project>`:

| Project | May contain | Must not contain or use |
| --- | --- | --- |
| `WebApi.Host` | The deployed ASP.NET Core host and EF startup project: `Program.cs`, appsettings, launch settings, user-secrets id, pipeline, MVC, Swagger, CORS. Calls only `AddPigeonWatch(builder.Configuration)` for application services. | Any `PigeonWatch.Data` or `PigeonWatch.BusinessLogic` type, EF Core. |
| `WebApi` | Controllers, API models, view model creators, mappers. | `PigeonWatch.Data` types, `PigeonWatch.DependencyInjection`, EF Core, DI registration code, startup code. |
| `DependencyInjection` | The `IServiceCollection` extension classes: `AddData`, `AddBusinessLogic`, `AddWebApi`, composed by the single public entry point `AddPigeonWatch(IConfiguration)`. DbContext registration (SQL Server, `EnableRetryOnFailure`, upper-snake-case migrations history). | Business logic, controllers. |
| `BusinessLogic` | Services and providers. Depends on repository interfaces from `Data` and on `BusinessObjects`. | EF Core, ASP.NET, `WebApi`, `DependencyInjection`, `WebApi.Host`, DI registration code. |
| `Data` | `PigeonWatchDbContext`, internal EF entities, conventions, auditing, repositories and their interfaces, entity-to-BO mappers, migrations. | `BusinessLogic`, `WebApi`, `DependencyInjection`, `WebApi.Host`, DI registration code, public entities. |
| `BusinessObjects` | Plain domain and result types shared across layers. | Any other PigeonWatch project, EF Core, ASP.NET. |

Reference direction: `WebApi.Host -> {WebApi, DependencyInjection}`; `DependencyInjection -> {WebApi, BusinessLogic, Data, BusinessObjects}`; `WebApi -> {BusinessLogic, BusinessObjects}`; `BusinessLogic -> {Data, BusinessObjects}`; `Data -> BusinessObjects`.

Registration: only `DependencyInjection` registers services (`IServiceCollection` APIs). No other layer wires itself; `WebApi.Host` only calls `AddPigeonWatch` plus framework setup.

Data flow: a repository in `Data` loads an entity and maps it to a business object through a `Data/Mappers` mapper, so entities never leave `Data`; a service in `BusinessLogic` works on business objects; a controller in `WebApi` calls the service and turns the business object into an API model through a view model creator. Controllers return only API models, never business objects or entities.

Folder and namespace conventions:

- Controllers (types deriving from `ControllerBase`): `WebApi/Controllers/` (`PigeonWatch.WebApi.Controllers`). Every public action returns exactly `Task<ActionResult<T>>` with `T` from `PigeonWatch.WebApi.Models`.
- API models (types ending in `Model`): `WebApi/Models/`.
- View model creators: `WebApi/ViewModelCreators/`. Request-model mappers: `WebApi/Mappers/`.
- Services: `BusinessLogic/Services/`.
- Repositories: `Data/Repositories/`. Entity-to-BO mappers: `Data/Mappers/`. Entities: `Data/Entities/` (internal). Migrations: `Data/Migrations/`.

The architecture tests in `ArchitectureTests/` enforce the layer dependencies, the registration rule, the folder and namespace conventions, the controller return type, internal entities, and the coding rules below that the build does not check (static classes, interfaces and registration, cancellation tokens, SQL naming, primary keys, audit columns). A violation fails `dotnet test`; the failure message lists the offending types. Run them with:

```
dotnet test PigeonWatch/Api/ArchitectureTests/PigeonWatch.ArchitectureTests.csproj
```

Known limits of the architecture tests: only classes deriving from `ControllerBase` count as controllers, so do not write POCO or `[Controller]`-attributed controllers. The interface-name rule accepts `I` plus any PascalCase suffix of the type name (so `SystemCurrentUserProvider : ICurrentUserProvider` passes), which means a bare `IService` or `IRepository` would also pass; always name the interface after the full role.

## Coding rules

- No `var`; every local declares its type explicitly (generated migration code is exempt).
- No static classes, except classes that only hold `const` values and the `IServiceCollection` extension classes in `DependencyInjection`. Static members on non-static classes are allowed.
- Classes with constructor dependencies use primary constructors.
- Private fields are camelCase with no `_` prefix.
- Every service, provider, repository, view model creator and mapper has a matching interface (`I<TypeName>`), is registered in `DependencyInjection`, and consumers depend on the interface. A strategy implementation may prefix the interface name (`SystemCurrentUserProvider : ICurrentUserProvider`).
- Every async method takes a `CancellationToken cancellationToken = default` parameter (interface declarations included). Controller actions are the only exception: they take `CancellationToken cancellationToken` without a default, bound by MVC to the request. Lambdas passed to framework APIs are not methods and are exempt.
- Every SQL identifier the application owns (tables, views, columns, keys, foreign keys, indexes, including EF's migrations history table `EF_MIGRATIONS_HISTORY` and its `MIGRATION_ID`/`PRODUCT_VERSION` columns) is UPPER_SNAKE_CASE. C# names stay PascalCase; the model-wide `UpperSnakeCaseNamingConvention` in `Data` derives the SQL names, so entities do not map names by hand. Table and view names are singular (`SMOKE_CHECK`, not `SMOKE_CHECKS`), so map each entity with a singular `ToTable` name. The database name (`PigeonWatch`, `pigeonwatch-db`) is exempt.
- Every entity derives from `AuditableEntity` and so carries the audit columns `CREATE_USER`, `CREATE_DATE`, `UPDATE_USER`, `UPDATE_DATE` (all NOT NULL, dates UTC `datetime2`, users `nvarchar(128)`). `AuditSaveChangesInterceptor` fills them on every save: an insert sets all four from the current user and time, an update sets only `UPDATE_*` and never changes `CREATE_*`. The user comes from `ICurrentUserProvider`, which returns `SYSTEM` until authentication arrives in S-01; time comes from the injected `TimeProvider`. Repositories never set audit columns themselves.
- Every table's primary key is a single `ID` column of type `UNIQUEIDENTIFIER`: entities inherit `Guid Id` from the internal abstract `Entity` base (which `AuditableEntity` derives from). Values are generated by EF on insert as sequential GUIDs (EF Core's default for `Guid` keys on SQL Server), which keeps the clustered index from fragmenting; code never assigns `Id` by hand.
- No comments in code.

The `var`, primary-constructor and field-naming rules fail the build (`.editorconfig` + `EnforceCodeStyleInBuild` in `Directory.Build.props`, which also covers the test project). The other rules are enforced by the architecture tests.

## Commands

Run from the repo root:

```
dotnet build PigeonWatch/Api/PigeonWatchApi.slnx
dotnet run --project PigeonWatch/Api/WebApi.Host/PigeonWatch.WebApi.Host.csproj
dotnet test PigeonWatch/Api/ArchitectureTests/PigeonWatch.ArchitectureTests.csproj   # architecture tests
dotnet list PigeonWatch/Api/WebApi.Host/PigeonWatch.WebApi.Host.csproj package --vulnerable --include-transitive   # dependency audit
dotnet list PigeonWatch/Api/ArchitectureTests/PigeonWatch.ArchitectureTests.csproj package --vulnerable --include-transitive   # test project dependency audit
dotnet tool restore   # pins dotnet-ef
dotnet ef migrations add <Name> --project PigeonWatch/Api/Data/PigeonWatch.Data.csproj --startup-project PigeonWatch/Api/WebApi.Host/PigeonWatch.WebApi.Host.csproj --output-dir Migrations
dotnet ef migrations has-pending-model-changes --project PigeonWatch/Api/Data/PigeonWatch.Data.csproj --startup-project PigeonWatch/Api/WebApi.Host/PigeonWatch.WebApi.Host.csproj
dotnet ef database update --project PigeonWatch/Api/Data/PigeonWatch.Data.csproj --startup-project PigeonWatch/Api/WebApi.Host/PigeonWatch.WebApi.Host.csproj   # applies to LocalDB
```

Local development uses SQL LocalDB (`PigeonWatch` database); the `ConnectionStrings:Default` value lives in user-secrets on the `WebApi.Host` project, not in the repo.
