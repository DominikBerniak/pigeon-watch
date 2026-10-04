# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

Scope: `PigeonWatch/Api/` only — the ASP.NET Core Web API. See `@PigeonWatch/CLAUDE.md` for what PigeonWatch is and how Api/Frontend fit together.

## What this owns

ASP.NET Core Web API (.NET 10). Owns auth and the status-workflow/urgency-ranking business logic for sightings (spotted → contacted → taken to vet → healed/returned). Solution: `PigeonWatchApi.slnx` with six layered projects plus two test projects: `ArchitectureTests/PigeonWatch.ArchitectureTests.csproj` and `UnitTests/PigeonWatch.UnitTests.csproj` (xUnit, NSubstitute, SQLite in-memory for the user store).

Data layer: EF Core (SQL Server) via `Data/PigeonWatchDbContext.cs`, migrations in `Data/Migrations/`. The only table is `USER_ACCOUNT`, persisted for ASP.NET Core Identity through the custom `Data/Identity/PigeonWatchUserStore.cs` (no `IdentityDbContext`, no stock Identity tables).

Auth: Identity bearer tokens (`MapIdentityApi`, no cookies). The bearer and refresh tokens are protected by the Data Protection key ring, so there is no signing key to configure. The endpoint surface is:

- `POST account/register` (anonymous, rate limited): our own action, because the built-in `/register` cannot carry a display name.
- `POST auth/login` (rate limited) and `POST auth/refresh`: the only Identity endpoints exposed. `MapPigeonWatch` maps `MapIdentityApi` under `auth` and its filter returns 404 for every other Identity route and 400 for `useCookies`/`useSessionCookies`.
- `GET configuration/client` (anonymous, `Cache-Control: public, max-age=300`): validation rules from `BusinessObjects/AccountRules.cs`, never user data.
- `GET configuration/general` (authorized, `Cache-Control: private, no-store`): the current user (id, email, display name, roles from token claims). There is no `/me` endpoint.
- `GET resources/{culture}` (anonymous, `Cache-Control: public, max-age=300`, `Content-Language`): the UI label map; see "UI labels" below.

The `auth` rate-limit policy (`WebApi/RateLimiting/RateLimitPolicyNames.cs`, configured in `Program.cs`) allows 10 requests per minute per client IP for login and register; `auth/refresh` is exempt. A database that is paused or unreachable surfaces as `503` ProblemDetails with `Retry-After: 10` through `Data/Diagnostics/DatabaseUnavailableExceptionHandler.cs`; other exceptions stay `500`.

Auditing records the user id: `Data/Auditing/HttpContextCurrentUserProvider.cs` returns the `ClaimTypes.NameIdentifier` of the current request, or `SYSTEM` for anonymous saves (registration, failed-login lockout counters).

## Architecture

Six projects under `PigeonWatch/Api/`, each with matching root namespace and assembly name `PigeonWatch.<Project>`:

| Project | May contain | Must not contain or use |
| --- | --- | --- |
| `WebApi.Host` | The deployed ASP.NET Core host and EF startup project: `Program.cs`, appsettings, launch settings, user-secrets id, pipeline, MVC, Swagger, CORS, rate limiter. Calls only `AddPigeonWatch(builder.Configuration)` for application services. | Any `PigeonWatch.Data` or `PigeonWatch.BusinessLogic` type, EF Core. |
| `WebApi` | Controllers, API models, view model creators, mappers. | `PigeonWatch.Data` types, `PigeonWatch.DependencyInjection`, EF Core, DI registration code, startup code. |
| `DependencyInjection` | The `IServiceCollection` extension classes: `AddData`, `AddBusinessLogic`, `AddWebApi`, `AddAccounts` (Identity options and user store), composed by the single public entry point `AddPigeonWatch(IConfiguration)`. DbContext registration (SQL Server, `EnableRetryOnFailure`, upper-snake-case migrations history), ProblemDetails and the exception handler. The `IEndpointRouteBuilder` extension class with `MapPigeonWatch()`, which maps the filtered Identity endpoints, because the Host may not name `Data` types. | Business logic, controllers. |
| `BusinessLogic` | Services and providers. Depends on repository interfaces from `Data` and on `BusinessObjects`. | EF Core, ASP.NET, `WebApi`, `DependencyInjection`, `WebApi.Host`, DI registration code. |
| `Data` | `PigeonWatchDbContext`, internal EF entities, conventions, auditing, repositories and their interfaces, entity-to-BO mappers, migrations. | `BusinessLogic`, `WebApi`, `DependencyInjection`, `WebApi.Host`, DI registration code, public entities. |
| `BusinessObjects` | Plain domain and result types shared across layers. | Any other PigeonWatch project, EF Core, ASP.NET. |

Reference direction: `WebApi.Host -> {WebApi, DependencyInjection}`; `DependencyInjection -> {WebApi, BusinessLogic, Data, BusinessObjects}`; `WebApi -> {BusinessLogic, BusinessObjects}`; `BusinessLogic -> {Data, BusinessObjects}`; `Data -> BusinessObjects`.

Registration: only `DependencyInjection` registers services (`IServiceCollection` APIs). No other layer wires itself; `WebApi.Host` only calls `AddPigeonWatch` and `MapPigeonWatch` plus framework setup.

Data flow: a repository in `Data` loads an entity and maps it to a business object through a `Data/Mappers` mapper, so entities never leave `Data`; a service in `BusinessLogic` works on business objects; a controller in `WebApi` calls the service and turns the business object into an API model through a view model creator. Controllers return only API models, never business objects or entities.

Folder and namespace conventions:

- Controllers (types deriving from `ControllerBase`): `WebApi/Controllers/` (`PigeonWatch.WebApi.Controllers`). Every public action returns exactly `Task<ActionResult<T>>` with `T` from `PigeonWatch.WebApi.Models`.
- API models (types ending in `Model`): `WebApi/Models/`.
- View model creators: `WebApi/ViewModelCreators/`. Request-model mappers: `WebApi/Mappers/`.
- Services: `BusinessLogic/Services/`.
- Repositories: `Data/Repositories/`. Entity-to-BO mappers: `Data/Mappers/`. Entities: `Data/Entities/` (internal). Migrations: `Data/Migrations/`.
- Interfaces: an `Interfaces/` subfolder of the folder that holds their implementations (`BusinessLogic/Services/Interfaces/IAccountService.cs`, `Data/Repositories/Interfaces/IAccountRepository.cs`, `WebApi/ViewModelCreators/Interfaces/…`). The namespace stays the parent folder's namespace (`PigeonWatch.BusinessLogic.Services`, not `….Services.Interfaces`), so consumers need no extra `using`. An `Interfaces/` folder contains only interfaces.

The architecture tests in `ArchitectureTests/` enforce the layer dependencies, the registration rule, the folder and namespace conventions (including `Interfaces/` folders, checked by scanning the source files, since the namespace does not show the folder), the controller return type, internal entities, and the coding rules below that the build does not check (static classes, interfaces and registration, cancellation tokens, SQL naming, primary keys, audit columns). A violation fails `dotnet test`; the failure message lists the offending types. Run them with:

```
dotnet test PigeonWatch/Api/ArchitectureTests/PigeonWatch.ArchitectureTests.csproj
```

Known limits of the architecture tests: only classes deriving from `ControllerBase` count as controllers, so do not write POCO or `[Controller]`-attributed controllers. The interface-name rule accepts `I` plus any PascalCase suffix of the type name (so `SystemCurrentUserProvider : ICurrentUserProvider` passes), which means a bare `IService` or `IRepository` would also pass; always name the interface after the full role.

## Coding rules

- No `var`; every local declares its type explicitly (generated migration code is exempt).
- No static classes, except classes that only hold `const` values and the `IServiceCollection` or `IEndpointRouteBuilder` extension classes in `DependencyInjection` (the `StaticClassTests` allow-list). An extension class contains only extension methods: no private helpers, fields or nested types; inline lambdas are fine. Static members on non-static classes are allowed, so a `static readonly` lookup set lives in the non-static class that uses it.
- Classes with constructor dependencies use primary constructors.
- Private fields are camelCase with no `_` prefix.
- Every `if` and `return` statement is preceded by a blank line, unless it is the first statement of its enclosing block or body (method, constructor, accessor, lambda, `if`/`else` or loop body).
- An `if`, `else`, `for`, `foreach` or `while` body that is a single statement on one line has no braces. A body with more than one statement, or a single statement wrapped over several lines, keeps its braces.

  ```csharp
  UserAccountEntity? entity = await db.UserAccounts.FindAsync([id], cancellationToken);

  if (entity is null)
      return null;

  ApplicationUser user = userAccountMapper.ToUser(entity);

  return user;
  ```
- Every service, provider, repository, view model creator and mapper has a matching interface (`I<TypeName>`), is registered in `DependencyInjection`, and consumers depend on the interface. A strategy implementation may prefix the interface name (`SystemCurrentUserProvider : ICurrentUserProvider`). The interface file goes in the `Interfaces/` subfolder next to the implementation and keeps the implementation's namespace (see "Folder and namespace conventions").
- Every async method takes a `CancellationToken cancellationToken = default` parameter (interface declarations included). Controller actions are the only exception: they take `CancellationToken cancellationToken` without a default, bound by MVC to the request. Lambdas passed to framework APIs are not methods and are exempt.
- Every SQL identifier the application owns (tables, views, columns, keys, foreign keys, indexes, including EF's migrations history table `EF_MIGRATIONS_HISTORY` and its `MIGRATION_ID`/`PRODUCT_VERSION` columns) is UPPER_SNAKE_CASE. C# names stay PascalCase; the model-wide `UpperSnakeCaseNamingConvention` in `Data` derives the SQL names, so entities do not map names by hand. Table and view names are singular (`USER_ACCOUNT`, not `USER_ACCOUNTS`), so map each entity with a singular `ToTable` name. The database name (`PigeonWatch`, `pigeonwatch-db`) is exempt.
- Every entity derives from `AuditableEntity` and so carries the audit columns `CREATE_USER`, `CREATE_DATE`, `UPDATE_USER`, `UPDATE_DATE` (all NOT NULL, dates UTC `datetime2`, users `nvarchar(128)`). `AuditSaveChangesInterceptor` fills them on every save: an insert sets all four from the current user and time, an update sets only `UPDATE_*` and never changes `CREATE_*`. The user comes from `ICurrentUserProvider`, implemented by `HttpContextCurrentUserProvider`: the authenticated user's id (`ClaimTypes.NameIdentifier`), never the email, or `SYSTEM` for anonymous saves; time comes from the injected `TimeProvider`. Repositories never set audit columns themselves.
- Every table's primary key is a single `ID` column of type `UNIQUEIDENTIFIER`: entities inherit `Guid Id` from the internal abstract `Entity` base (which `AuditableEntity` derives from). Values are generated by EF on insert as sequential GUIDs (EF Core's default for `Guid` keys on SQL Server), which keeps the clustered index from fragmenting; code never assigns `Id` by hand.
- No comments in code.

The `var`, primary-constructor and field-naming rules fail the build (`.editorconfig` + `EnforceCodeStyleInBuild` in `Directory.Build.props`, which also covers the test project). The blank-line and brace rules are not checked by the build or the tests; follow them by hand. The other rules are enforced by the architecture tests.

## UI labels

- Every UI text the SPA shows is a key in `BusinessObjects/Resources/UiLabels.resx` (English, the neutral culture). Never hardcode UI text in the SPA; add a key here instead. The SPA bundles snapshots generated from these files and fetches `GET resources/{culture}` at runtime.
- Keys are lowercase dot paths: `<area>.<group>.<name>` in camelCase segments (`common.appName`, `auth.login.title`, `auth.errors.invalidCredentials`, `warmup.messages.1`). Values use positional placeholders only (`{0}`, `{1}`), no ICU or plural syntax.
- The resx is an embedded resource with no designer class, read through `ResourceManager` with `UiResources.BaseName`. `UiResourceService` serves `en` for an unknown, unsupported or malformed culture, and falls back to English per key.
- A new culture is added as `UiLabels.<culture>.resx` next to the neutral file (for example `UiLabels.pl.resx`); it becomes supported as soon as its satellite assembly exists, and missing keys fall back to English.
- API-side texts (ProblemDetails titles, Identity error descriptions) stay English diagnostics; the SPA maps error codes to label keys.

## Commands

Run from the repo root:

```
dotnet build PigeonWatch/Api/PigeonWatchApi.slnx
dotnet run --project PigeonWatch/Api/WebApi.Host/PigeonWatch.WebApi.Host.csproj
dotnet test PigeonWatch/Api/ArchitectureTests/PigeonWatch.ArchitectureTests.csproj   # architecture tests
dotnet test PigeonWatch/Api/UnitTests/PigeonWatch.UnitTests.csproj   # unit tests
dotnet list PigeonWatch/Api/WebApi.Host/PigeonWatch.WebApi.Host.csproj package --vulnerable --include-transitive   # dependency audit
dotnet list PigeonWatch/Api/ArchitectureTests/PigeonWatch.ArchitectureTests.csproj package --vulnerable --include-transitive   # test project dependency audit
dotnet tool restore   # pins dotnet-ef
dotnet ef migrations add <Name> --project PigeonWatch/Api/Data/PigeonWatch.Data.csproj --startup-project PigeonWatch/Api/WebApi.Host/PigeonWatch.WebApi.Host.csproj --output-dir Migrations
dotnet ef migrations has-pending-model-changes --project PigeonWatch/Api/Data/PigeonWatch.Data.csproj --startup-project PigeonWatch/Api/WebApi.Host/PigeonWatch.WebApi.Host.csproj
dotnet ef database update --project PigeonWatch/Api/Data/PigeonWatch.Data.csproj --startup-project PigeonWatch/Api/WebApi.Host/PigeonWatch.WebApi.Host.csproj   # applies to LocalDB
```

Local development uses SQL LocalDB (`PigeonWatch` database); the `ConnectionStrings:Default` value lives in user-secrets on the `WebApi.Host` project, not in the repo.
