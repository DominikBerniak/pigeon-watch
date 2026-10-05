# Register and Log In (S-01) Implementation Plan

## Overview

Deliver FR-001: a user can register with email, password and a public display name, log in, stay logged in across reloads, and log out on this device. The API uses ASP.NET Core Identity bearer tokens (`MapIdentityApi`, D1) on top of our own `USER_ACCOUNT` table and a custom user store, so Identity fits the existing architecture tests instead of being exempted from them. The Angular SPA gets a token store, a refresh-on-401 interceptor, route guards with `returnUrl` redirects, the D5 configuration endpoints, and a warm-up panel that replaces "broken app" errors while the free-tier API or database wakes up. The slice also closes the prior commitment to remove `/health/db` (F-01 review F3). It also introduces resource management: every UI label comes from one source, the `UiLabels` resx file(s) in the API. The SPA fetches the labels from `GET /resources/{culture}` and bundles build-time snapshots so it can render before the API wakes. S-01 ships English only; switching languages is roadmap slice S-12. Because this is the first real UI, the slice also lays the UI foundation every later slice builds on: Angular Material (MIT, first-party, v22.2) as the component library, one set of CSS custom properties (design tokens) for all common values that also drives the Material theme, one global stylesheet of common classes, and a small set of reusable app components for patterns Material does not cover, which the pages compose instead of writing their own markup and styles.

## Current State Analysis

- The API has no authentication: `Program.cs` has no `UseAuthentication`, no rate limiter, no exception handler (`PigeonWatch/Api/WebApi.Host/Program.cs:11-50`). The only endpoint is the throwaway `GET /health/db` smoke probe (`WebApi/Controllers/HealthController.cs:10-27`). `deploy-api.yml:148` uses it as the post-deploy probe.
- `PigeonWatchDbContext` is a plain `DbContext` with one throwaway entity (`Data/PigeonWatchDbContext.cs:7-23`). `UpperSnakeCaseNamingConvention` renames every table, column, key and index (`Data/Conventions/UpperSnakeCaseNamingConvention.cs:9-60`).
- The architecture tests build the EF model from `AddPigeonWatch` alone (`ArchitectureTests/PigeonWatchServices.cs:12-25`). Stock Identity would fail them:
  - singular table names (`DatabaseModelTests.cs:69-89`)
  - the single `ID uniqueidentifier` PK (`:107-136`)
  - `AuditableEntity` (`:138-172`)
  - internal entities (`TypeLocationTests.cs:76-94`)
  - the Host layer rule (`LayerDependencyTests.cs:60-68`)
  - static classes (`StaticClassTests.cs:12-58`)
- Auditing calls `ICurrentUserProvider.GetCurrentUserName()` on every save (`Data/Auditing/AuditSaveChangesInterceptor.cs:34`). The only implementation returns `SYSTEM` (`Data/Auditing/SystemCurrentUserProvider.cs:5-7`).
- The frontend is a bare shell:
  - plain `provideHttpClient()` (`Frontend/src/app/app.config.ts:7-11`)
  - empty routes (`app.routes.ts:3`)
  - an unused `environment.apiUrl` (`environments/environment.ts:1-4`, `environment.prod.ts:1-4`)
  - no `staticwebapp.config.json`
- Topology: the SPA on `wonderful-sea-07000d90f.6.azurestaticapps.net` and the API on `pigeonwatch-api.azurewebsites.net` are cross-site. Production CORS is configured on the App Service platform (`context/deployment/deploy-plan.md:125`). The App Service is Linux F1: it cold-starts in ~32 s, and the serverless DB auto-pauses (`deploy-plan.md:54,134,138`).

## Desired End State

- A visitor opening any page while logged out lands on `/login?returnUrl=<page>`. The login page links to registration and keeps the `returnUrl`.
- Registration takes email, display name (3–30 characters, unique case-insensitively, no `@`) and password (at least 8 characters with lower, upper, digit and symbol). It logs the user in automatically and returns them to `returnUrl`.
- A logged-in user sees "Logged in as <display name>". Reloading or opening a new tab keeps them logged in (refresh token in `localStorage`). "Log out" ends the session on this device.
- Access tokens expire after 1 h and refresh silently. A failed refresh returns the user to login with the current page as `returnUrl`.
- When the API cold-starts or the database is paused, the user sees a full-screen warm-up panel with light-hearted copy, and requests retry automatically for up to ~2 minutes. A paused database surfaces as 503 + `Retry-After`, never 500.
- No UI text is hardcoded in the SPA.
  - Every label, message and warm-up line is a key in `PigeonWatch/Api/BusinessObjects/Resources/UiLabels.resx`.
  - The SPA renders instantly from a bundled snapshot generated from the resx, then swaps to the map from `GET /resources/{culture}`.
  - The UI culture is the one saved under `pigeonwatch.culture` in `localStorage` when that culture is supported, otherwise `en`.
- No style value is hardcoded in the SPA, and UI building blocks are reused, not rewritten.
  - Every color, spacing, font, radius, shadow, size, duration and z-index is a `--pw-*` CSS custom property in `src/styles/_tokens.scss`.
  - Angular Material provides the primitives (form fields, inputs, buttons, cards, progress spinner). Its theme in `src/styles/_material-theme.scss` maps our tokens onto Material's `--mat-sys-*` system tokens, so changing a token restyles Material components too.
  - Shared layout and typography patterns are `pw-*` classes in `src/styles/_common.scss`, loaded once through `src/styles.scss`.
  - Patterns Material lacks are reusable app components in `src/app/shared/ui/`: page card, alert, field errors and submit button. Login, register, home and the warm-up panel are composed from Material and these components.
  - Component stylesheets use only tokens and never restyle Material internals, and a spec enforces it.
- `/health/db`, the smoke code and the `SMOKE_CHECK` table are gone. The post-deploy probe calls `GET /configuration/client`.
- Verify the end state with the API unit tests, the architecture tests, the Vitest specs and the PR workflow (all green), plus the live manual checks in Phases 2 and 4.

### Key Discoveries:

- `MapIdentityApi` maps register, login, refresh, confirmEmail, resendConfirmationEmail, forgotPassword, resetPassword and `/manage/*`. There is no built-in way to select endpoints (dotnet/aspnetcore#55792). An endpoint filter on the returned builder applies to all of them, which is how we allow only login and refresh. `/manage/2fa` would let a user enable TOTP, after which `/login` needs a `twoFactorCode` the SPA doesn't support, so it must be blocked (aspnetcore `release/10.0` `IdentityApiEndpointRouteBuilderExtensions.cs#L57-L348`).
- `/login` honours `?useCookies` / `?useSessionCookies`, and `AddIdentityApiEndpoints` registers the Identity cookie schemes anyway. The filter must reject those flags to keep D1 cookie-free (`#L91-97`).
- `AddIdentityApiEndpoints` registers `IHttpContextAccessor` through `AddSignInManager` (`IdentityBuilderExtensions.cs#L41-L43`). It also registers a no-op email sender (`#L101-L102`), so no `IEmailSender` work is needed (D4).
- The bearer and refresh token protectors come from the app's Data Protection key ring (`BearerTokenConfigureOptions.cs#L9-L21`). On App Service the keys go to `$HOME/ASP.NET/DataProtection-Keys` when `WEBSITE_INSTANCE_ID` and `HOME` are set (`DefaultKeyStorageDirectories.cs#L87-L110`). That is `/home/...` on Linux, outside the `clean: true` wwwroot. This is unverified on built-in Linux images, so Phase 2 has a restart test.
- `/login` signs in by **user name** (`PasswordSignInAsync(login.Email, …)`), so registration must set `UserName = Email`.
- `UserManager<TUser>` works with any `class, new()` user type through store interfaces. Our own `IUserStore` over an internal entity removes every test exemption. `ApplicationUser` is a public non-entity type in `Data`; `TypeLocationTests` only checks `Entity` subclasses and EF model types.
- Layer rules decide placement:
  - Host may not name `Data` types, so endpoint mapping goes in a `DependencyInjection` `IEndpointRouteBuilder` extension. `StaticClassTests` needs one allow-list addition for that.
  - `BusinessLogic` may not touch `Microsoft.AspNetCore.*`, so `UserManager` calls live in a `Data` repository.
  - `WebApi` may not use EF or SqlClient, so recognising transient SQL failures lives in `Data`.
- The existing patterns to copy:
  - result records instead of exceptions (`BusinessLogic/Services/DatabaseHealthService.cs:13-22`)
  - sealed `required`/`init` API models (`WebApi/Models/DatabaseHealthModel.cs:5-14`)
  - the `Task<ActionResult<T>>` action shape (`ArchitectureTests/ControllerActionTests.cs:10-28`)
  - `CancellationToken cancellationToken = default` on every async method (`ArchitectureTests/CancellationTokenTests.cs:10-50`)
- `context/foundation/lessons.md`: every command handed to the user in PowerShell uses `curl.exe`, not `curl`.
- The SPA deploy workflow triggers only on `PigeonWatch/Frontend/**` (`.github/workflows/azure-static-web-apps-wonderful-sea-07000d90f.yml:3-8`). Its build runs in the full repo checkout (`app_location: PigeonWatch/Frontend`, `:30-32`), so a prebuild script can read the API's resx, but a resx-only change would not redeploy the SPA snapshot without a path-filter change.
- `System.Resources.ResourceManager.GetResourceSet(culture, createIfNotExists: true, tryParents: true)` enumerates every key of an embedded resx, and per-key lookups fall back to the neutral (English) resource. No `IStringLocalizer` or ASP.NET dependency is needed, so the service can live in `BusinessLogic`.

## What We're NOT Doing

- No email confirmation, password reset or account recovery (D4). `/confirmEmail`, `/resendConfirmationEmail`, `/forgotPassword` and `/resetPassword` are blocked (404).
- No 2FA, passkeys, external logins or account self-service. `/manage/*` is blocked.
- No "log out everywhere" and no server-side logout endpoint. Logout discards the tokens on this device only. A stolen refresh token stays valid until it expires (14 days; accepted in D1).
- No role tables or role store. `GeneralConfiguration.currentUser.roles` is read from token claims and is always empty until S-10.
- No profile editing. Display-name edits are S-02.
- No breached-password check and no NIST-length policy (D3 deviation, accepted).
- No cookie auth and no SWA Standard linked backend. Option B stays the documented upgrade path (D1).
- No move of production CORS from the App Service platform into app config. Only the local `http://localhost:4200` app policy stays.
- No tuning of EF's retry budget or `Connect Timeout` (F-01 review F2). The warm-up panel covers the user experience, and retry tuning stays a follow-up in `deploy-plan.md`.
- No API integration test project and no SQL Server container in CI. Testing is unit tests with mocks/SQLite (user decision).
- No Swagger bearer security definition.
- No language switcher UI and no `UiLabels.pl.resx`. S-01 implements reading the saved culture with an `en` fallback, but nothing writes `pigeonwatch.culture` until roadmap slice S-12.
- No localization of API error descriptions or ProblemDetails titles. The SPA maps error **codes** to resource keys, and API-side texts stay English diagnostics.
- No PrimeNG: v22 (the only line supporting Angular 22) ships under the PrimeUI License (license key, yearly eligibility renewal, compiled-only package, license notice when the key lapses), not MIT.
- No `ng add @angular/material`: its schematic injects Google Fonts (Roboto, Material Symbols) `<link>`s that the Phase 3 CSP blocks. Material is installed with npm and themed by hand, with a system font stack and no icon font. S-01 needs no icons; a later slice that does self-hosts the font.
- No CSS framework (Tailwind, Bootstrap), no Storybook and no dark theme. The tokens make a dark theme a tokens-and-theme-file change later.
- No full protection against account enumeration (revised 2026-10-04, user decision). Registration never says an email is taken (`RegistrationFailed`, Phase 2 item 7), but without email confirmation (D4) a registration that fails for no visible reason can still hint that an account exists. The rate limiter and lockout bound that probing. Login keeps the `LockedOut` detail and its specific message, and there is no dummy-hash fix for the timing difference between known and unknown emails.
- No pluralisation or ICU message formatting; only positional `{0}` placeholders. No Angular `@angular/localize` (compile-time, one build per language) and no Transloco.

## Implementation Approach

Build from the database out:
1. Persist users in our own audited `USER_ACCOUNT` table behind a custom Identity store, and expose login/refresh through a filtered `MapIdentityApi`, plus our own register action (the display name makes the built-in `/register` unusable).
2. Add the D5 configuration endpoints, the UI label resources endpoint with the full English label set, the IP rate limiter, the 503 mapping for a paused database, and the smoke-probe removal, then verify everything live.
3. Build the SPA plumbing (resource snapshots and service, token store, interceptors, guards, non-blocking startup restore, SWA config, Angular Material with a token-driven theme, design tokens and common styles) with Vitest specs.
4. Add the reusable app components, compose the pages and the warm-up panel from them and Material, and verify end to end, live and in privacy-strict browsers.

Every new backend type follows the existing layer path: controller → view model creator / request mapper → service → repository → BusinessObjects record.

## Critical Implementation Details

- **Startup must not block rendering.** `provideAppInitializer` holds bootstrap until it resolves, so a blocking restore during a ~32 s cold start would show a blank page and no warm-up panel. Start the D5 restore sequence from the initializer without awaiting it. Guards await `SessionService.whenReady()`. The initializer must never reject.
- **Warm-up retries vs the rate limiter.** The `auth` limiter allows 10 requests per minute per IP. The warm-up interceptor honours `Retry-After` (the API sends 10 s, with a 5 s floor), so a login retried through a 2-minute warm-up stays under the limit. A 429 is never retried and never shows the panel.
- **Interceptor order.** Use `withInterceptors([authInterceptor, warmupInterceptor])`. Warm-up sits closer to the network, so a 503 retry resends the same authorised request. Auth sees the final status, refreshes on 401 and retries once.
- **CORS on error responses.** `UseExceptionHandler` clears response headers before writing the 503. Production CORS is added by the App Service platform, outside the app. For the local app policy, check that a 503 still carries `Access-Control-Allow-Origin`. If it doesn't, move `UseCors` so it applies to the handler's response. The SPA treats status 0 like a 503 inside the warm-up window either way.
- **Labels must exist before the API does.** The warm-up panel and login page render during a cold start, when `GET /resources/{culture}` cannot answer. The `ResourceService` is seeded synchronously from the bundled snapshot for the selected culture before first render, and the API fetch only replaces the map when it succeeds. A failed or slow fetch never blanks labels and never shows the warm-up panel on its own (the fetch sets a `SKIP_WARMUP` context token).
- **CSP vs Angular's critical-CSS inlining.** Production builds can emit `<link … media="print" onload="this.media='all'">`, which `script-src 'self'` blocks. Check `dist/Frontend/browser/index.html`. If `onload=` is present, set `optimization.styles.inlineCritical: false` in the production configuration.

## Phase 1: User store and auth endpoints (API)

### Overview

Users can register (`POST /account/register`), log in (`POST /auth/login`) and refresh (`POST /auth/refresh`) against LocalDB with `curl.exe`. Identity data lives in an audited `USER_ACCOUNT` table that passes every architecture test unchanged, apart from the endpoint-extension allow-list. A new unit-test project covers the store, service, repository and current-user provider.

### Changes Required:

#### 1. User entity and DbContext mapping

**File**: `PigeonWatch/Api/Data/Entities/UserAccountEntity.cs`, `PigeonWatch/Api/Data/PigeonWatchDbContext.cs`

**Intent**: Add the first real table: one internal, audited entity that holds exactly the Identity state the store needs plus the display name.

**Contract**:
- `internal class UserAccountEntity : AuditableEntity`, mapped with `ToTable("UserAccount")` → `USER_ACCOUNT`. `UserAccount` is used because `USER` is a T-SQL reserved word.
- Properties (all required unless marked otherwise): `Email`, `NormalizedEmail`, `UserName`, `NormalizedUserName` (max 256 each), `DisplayName`, `NormalizedDisplayName` (max 30 each), `PasswordHash`, `SecurityStamp`, `ConcurrencyStamp` (concurrency token), `LockoutEnd` (`DateTimeOffset?`, optional), `LockoutEnabled`, `AccessFailedCount`.
- Unique indexes on `NormalizedEmail`, `NormalizedUserName` and `NormalizedDisplayName`.
- The DbContext exposes `internal DbSet<UserAccountEntity> UserAccounts`.

#### 2. Identity user type and custom store

**File**: `PigeonWatch/Api/Data/Identity/ApplicationUser.cs`, `PigeonWatch/Api/Data/Identity/PigeonWatchUserStore.cs`, `PigeonWatch/Api/Data/Mappers/IUserAccountMapper.cs`, `PigeonWatch/Api/Data/Mappers/UserAccountMapper.cs`, `PigeonWatch/Api/Data/PigeonWatch.Data.csproj`

**Intent**: Give Identity a public, non-entity user type and a store that persists it through `UserAccountEntity`, so `UserManager`, `SignInManager` and `MapIdentityApi` work without `IdentityDbContext` or the stock tables.

**Contract**:
- `public class ApplicationUser` (parameterless constructor): `Guid Id` plus the entity's Identity fields and `DisplayName`. It is not an EF type.
- `public class PigeonWatchUserStore` implements `IUserPasswordStore<ApplicationUser>`, `IUserEmailStore<ApplicationUser>`, `IUserSecurityStampStore<ApplicationUser>` and `IUserLockoutStore<ApplicationUser>`. It does not implement the role, claim, login, token or two-factor stores.
- Every async member declares `CancellationToken cancellationToken = default` (implicit interface implementations, not explicit ones).
- `GetEmailConfirmedAsync` returns `false` and `SetEmailConfirmedAsync` is a no-op, because there is no email confirmation (D4) and no column for it. `SignIn.RequireConfirmedEmail = false` means sign-in never reads it.
- Lookups use the normalized columns. On create and update the store sets `NormalizedDisplayName` through `ILookupNormalizer.NormalizeName`.
- `CreateAsync` adds the entity, lets EF generate `Id`, and copies it back to `ApplicationUser.Id`.
- `UpdateAsync` checks `ConcurrencyStamp`, rotates it, and returns `IdentityErrorDescriber.ConcurrencyFailure()` on `DbUpdateConcurrencyException`.
- `CreateAsync` and `UpdateAsync` first check `UserAccounts.AnyAsync(u => u.NormalizedDisplayName == normalized && u.Id != user.Id, cancellationToken)` and return a failed `IdentityResult` with code `DuplicateDisplayName` without saving. Identity's `UserValidator` already pre-checks email and user name; display name has no built-in check. Not an `IUserValidator<ApplicationUser>`: its `ValidateAsync` has no `CancellationToken`, which fails `CancellationTokenTests`.
- Unique-index violations (SQL errors 2601/2627) map to the `DuplicateEmail`, `DuplicateUserName` or `DuplicateDisplayName` error codes by index name. This is a SQL-Server-only backstop for concurrent registrations; SQLite raises `SqliteException` (code 19) and is not covered by unit tests, only by the Phase 1 LocalDB checks.
- `UserAccountMapper` maps entity ↔ `ApplicationUser`, with its own interface and registration.
- `PigeonWatch.Data.csproj` gains `<FrameworkReference Include="Microsoft.AspNetCore.App" />` (this covers Identity core and `Microsoft.AspNetCore.Http`). No new NuGet package.

#### 3. Current-user provider for auditing

**File**: `PigeonWatch/Api/Data/Auditing/HttpContextCurrentUserProvider.cs`, delete `PigeonWatch/Api/Data/Auditing/SystemCurrentUserProvider.cs`

**Intent**: Audit columns record who changed a row. The user id is used rather than the email, to keep contact info out of data other features may read. Anonymous saves (registration, failed-login lockout counters) record `SYSTEM`.

**Contract**: `HttpContextCurrentUserProvider(IHttpContextAccessor) : ICurrentUserProvider`. `GetCurrentUserName()` returns the `ClaimTypes.NameIdentifier` value of the current request's user, or `"SYSTEM"` when there is no HTTP context or no authenticated user.

#### 4. Account registration path

**File**: `PigeonWatch/Api/BusinessObjects/AccountRules.cs`, `PigeonWatch/Api/BusinessObjects/NewAccount.cs`, `PigeonWatch/Api/BusinessObjects/AccountCreationResult.cs`, `PigeonWatch/Api/BusinessObjects/AccountError.cs`, `PigeonWatch/Api/BusinessLogic/Services/IAccountService.cs`, `PigeonWatch/Api/BusinessLogic/Services/AccountService.cs`, `PigeonWatch/Api/Data/Repositories/IAccountRepository.cs`, `PigeonWatch/Api/Data/Repositories/AccountRepository.cs`

**Intent**: Register a user with a display name. The built-in `/register` DTO can't carry one. Display-name rules are business rules and live in the service. Identity calls stay in `Data`.

**Contract**:
- `AccountRules` is a const-only holder: `PasswordMinLength = 8`, `DisplayNameMinLength = 3`, `DisplayNameMaxLength = 30`, `DisplayNameForbiddenCharacter = '@'`. It is the single source for the Identity options and `ClientConfiguration`.
- `NewAccount(string Email, string Password, string DisplayName)`.
- `AccountCreationResult` has `Succeeded` and `IReadOnlyList<AccountError> Errors`. `AccountError(string Code, string Description)` uses Identity's error codes (`DuplicateEmail`, `InvalidEmail`, `PasswordTooShort`, `PasswordRequiresUpper`, …) plus `DisplayNameLength`, `DisplayNameInvalidCharacter` and `DuplicateDisplayName`.
- `IAccountService.RegisterAsync(NewAccount, CancellationToken = default)` trims email and display name. If the display name is shorter than 3 or longer than 30 characters after trimming, or contains `@`, it returns a failed result without calling the repository.
- `IAccountRepository.CreateAsync(NewAccount, CancellationToken = default)` uses `UserManager<ApplicationUser>.CreateAsync(user, password)` with `UserName = Email`, and maps `IdentityResult` errors to `AccountError`s. Since the user name is the email, `DuplicateUserName` and `InvalidUserName` are dropped when `DuplicateEmail` / `InvalidEmail` are also present, and are otherwise renamed to them, so the SPA only ever sees email codes. *Revised in Phase 2 (item 7): `DuplicateEmail` and `DuplicateUserName` both become one generic `RegistrationFailed`, so the API never confirms a registered email.*

#### 5. Register endpoint

**File**: `PigeonWatch/Api/WebApi/Controllers/AccountController.cs`, `PigeonWatch/Api/WebApi/Models/RegisterRequestModel.cs`, `PigeonWatch/Api/WebApi/Models/RegisteredAccountModel.cs`, `PigeonWatch/Api/WebApi/Mappers/IRegisterRequestMapper.cs`, `PigeonWatch/Api/WebApi/Mappers/RegisterRequestMapper.cs`, `PigeonWatch/Api/WebApi/ViewModelCreators/IRegisteredAccountViewModelCreator.cs`, `PigeonWatch/Api/WebApi/ViewModelCreators/RegisteredAccountViewModelCreator.cs`

**Intent**: Expose registration over HTTP, with an error shape the SPA can map to form fields.

**Contract**:
- `[Route("account")]`, `[HttpPost("register")]`, `[AllowAnonymous]`.
- `Task<ActionResult<RegisteredAccountModel>> Register(RegisterRequestModel request, CancellationToken cancellationToken)`.
- `RegisterRequestModel` has required `Email`, `Password` and `DisplayName`. `RegisteredAccountModel` has `Email` and `DisplayName`.
- On failure: `400` `ValidationProblemDetails` whose `errors` dictionary is keyed by error code. This mirrors Identity's own `/register` shape.
- The route is `account/register`, not `auth/register`, to avoid an ambiguous match with the filtered Identity route.

#### 6. Identity registration and filtered endpoint mapping

**File**: `PigeonWatch/Api/DependencyInjection/AccountServiceCollectionExtensions.cs`, `PigeonWatch/Api/DependencyInjection/PigeonWatchEndpointRouteBuilderExtensions.cs`, `PigeonWatch/Api/DependencyInjection/PigeonWatchServiceCollectionExtensions.cs`, `PigeonWatch/Api/DependencyInjection/DataServiceCollectionExtensions.cs`, `PigeonWatch/Api/DependencyInjection/BusinessLogicServiceCollectionExtensions.cs`, `PigeonWatch/Api/DependencyInjection/WebApiServiceCollectionExtensions.cs`, `PigeonWatch/Api/WebApi.Host/Program.cs`

**Intent**: Wire Identity in the only layer allowed to name `Data` types, and expose only the two Identity endpoints we use.

**Contract**:
- `AddAccounts(this IServiceCollection)` is called from `AddPigeonWatch`. It does `AddIdentityApiEndpoints<ApplicationUser>(…)` then `.AddUserStore<PigeonWatchUserStore>()`.
- Identity options:
  - password: digit, lowercase, uppercase and non-alphanumeric required; `RequiredLength = AccountRules.PasswordMinLength`
  - lockout: `MaxFailedAccessAttempts = 5`, `DefaultLockoutTimeSpan = 5 min`, `AllowedForNewUsers = true`
  - `User.RequireUniqueEmail = true`
  - `User.AllowedUserNameCharacters = ""` (disables the character check; the user name is the email, whose format the email validator already checks, so `o'brien@…` and accented addresses register)
  - `SignIn.RequireConfirmedEmail = false`
- `HttpContextCurrentUserProvider` replaces `SystemCurrentUserProvider`, registered as `AddScoped<ICurrentUserProvider, HttpContextCurrentUserProvider>()` (by type, not a factory, per `ServiceRegistrationTests`). `AddData` also calls `services.AddHttpContextAccessor()` explicitly, because the architecture tests resolve the DbContext (and so the audit interceptor) from `AddPigeonWatch`. Mapper, repository, service, request mapper and view model creator are registered with their interfaces.
- `public static class PigeonWatchEndpointRouteBuilderExtensions` with `MapPigeonWatch(this IEndpointRouteBuilder)`:
  - `MapGroup("auth").MapIdentityApi<ApplicationUser>()` plus an endpoint filter.
  - The filter allows only `POST auth/login` and `POST auth/refresh`, and returns 404 for every other Identity route.
  - It returns 400 when `useCookies` or `useSessionCookies` is present.
  - The filter is an inline lambda passed to `AddEndpointFilter`, never an `IEndpointFilter` class: `InvokeAsync` has no `CancellationToken`, so a class fails `CancellationTokenTests`. The extension class has no private helpers, fields or nested types, because `StaticClassTests` rejects any member that is not an extension method; compiler-generated lambda closures are exempt.
- `Program.cs` adds `app.UseAuthentication()` before `UseAuthorization()` and calls `app.MapPigeonWatch()` after `MapControllers()`.

#### 7. Architecture test allow-list

**File**: `PigeonWatch/Api/ArchitectureTests/StaticClassTests.cs`

**Intent**: Allow endpoint-mapping extension classes in `DependencyInjection` the same way `IServiceCollection` extension classes are allowed today.

**Contract**: `IsServiceCollectionExtensionClass` accepts a `DependencyInjection` static class whose members are all extension methods whose first parameter is `IServiceCollection` **or** `IEndpointRouteBuilder`. The failure message is updated to match. No other test changes.

#### 8. Migration

**File**: `PigeonWatch/Api/Data/Migrations/<timestamp>_AddUserAccount.cs` (generated) + snapshot

**Intent**: Create `USER_ACCOUNT`. This migration is purely additive.

**Contract**: Generated with the `dotnet ef migrations add AddUserAccount …` command from `PigeonWatch/Api/CLAUDE.md`. Check the generated names: `USER_ACCOUNT`, `PK_USER_ACCOUNT`, and `IX_USER_ACCOUNT_NORMALIZED_EMAIL` etc. are upper snake case, and no index filters are generated, because the normalized columns are NOT NULL.

#### 9. Unit-test project and CI step

**File**: `PigeonWatch/Api/UnitTests/PigeonWatch.UnitTests.csproj` (+ test classes), `PigeonWatch/Api/PigeonWatchApi.slnx`, `.github/workflows/api-pr-checks.yml`

**Intent**: Cover the custom security code with fast tests: mocks for collaborators, SQLite in-memory for the store.

**Contract**:
- xUnit (same versions as `ArchitectureTests`), `NSubstitute`, and `Microsoft.EntityFrameworkCore.Sqlite` 10.0.12.
- References `Data`, `BusinessLogic`, `BusinessObjects` and `WebApi`. Added to the `.slnx`.
- The store tests build `PigeonWatchDbContext` with SQLite options and the audit interceptor, and call `EnsureCreated()`.
- `api-pr-checks.yml` gets a `Unit tests` step after `Architecture tests`: `dotnet test PigeonWatch/Api/UnitTests/PigeonWatch.UnitTests.csproj --no-build`.

### Success Criteria:

#### Automated Verification:

- Solution builds with no warnings: `dotnet build PigeonWatch/Api/PigeonWatchApi.slnx`
- Architecture tests pass with only the `StaticClassTests` allow-list change: `dotnet test PigeonWatch/Api/ArchitectureTests/PigeonWatch.ArchitectureTests.csproj`
- Unit tests pass, covering the following:
  - display-name rules: `ab` rejected, `abc` accepted, 30 characters accepted, 31 rejected, `a@b` rejected, surrounding whitespace trimmed, repository not called on rejection
  - store create/find by normalized email, user name and display name
  - case-insensitive duplicate display name → `DuplicateDisplayName`
  - stale `ConcurrencyStamp` → `ConcurrencyFailure`
  - lockout fields persisted
  - repository maps `IdentityResult` errors to `AccountError`s, and `DuplicateUserName` + `DuplicateEmail` collapse to a single `DuplicateEmail`
  - current-user provider returns the user id, and `SYSTEM` when anonymous
  - command: `dotnet test PigeonWatch/Api/UnitTests/PigeonWatch.UnitTests.csproj`
- Migration matches the model: `dotnet ef migrations has-pending-model-changes --project PigeonWatch/Api/Data/PigeonWatch.Data.csproj --startup-project PigeonWatch/Api/WebApi.Host/PigeonWatch.WebApi.Host.csproj` reports no changes
- Migration applies to LocalDB: `dotnet ef database update --project PigeonWatch/Api/Data/PigeonWatch.Data.csproj --startup-project PigeonWatch/Api/WebApi.Host/PigeonWatch.WebApi.Host.csproj`

#### Manual Verification:

- Against the local API (`http://localhost:5285`) using `curl.exe` in PowerShell, `POST /account/register` returns 200 for a valid body. A second call with the same email in different case returns 400 with `DuplicateEmail`, and a different email with the same display name in different case returns 400 with `DuplicateDisplayName`.
- `POST /auth/login` returns `accessToken`, `refreshToken` and `expiresIn: 3600`, and `POST /auth/refresh` with the refresh token returns a new pair.
- Five wrong passwords lock the account; the sixth attempt with the correct password returns 401 with detail `LockedOut`.
- `POST /auth/register`, `POST /auth/forgotPassword`, `GET /auth/manage/info` (with a token) and `POST /auth/manage/2fa` (with a token) return 404, and `POST /auth/login?useCookies=true` returns 400 with no `Set-Cookie` header.
- The `USER_ACCOUNT` row shows `PASSWORD_HASH` filled (not plaintext), `CREATE_USER = SYSTEM`, and UTC `CREATE_DATE`.

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 2: Configuration endpoints, hardening and deploy (API)

### Overview

Add the D5 configuration endpoints, rate limiting, and the 503 mapping for an unavailable database. Remove the smoke probe and switch the deploy probe, update docs and app settings, and verify the API live.

### Changes Required:

#### 1. Client and general configuration

**File**: `PigeonWatch/Api/WebApi/Controllers/ConfigurationController.cs`, `PigeonWatch/Api/WebApi/Models/ClientConfigurationModel.cs`, `PigeonWatch/Api/WebApi/Models/PasswordRulesModel.cs`, `PigeonWatch/Api/WebApi/Models/DisplayNameRulesModel.cs`, `PigeonWatch/Api/WebApi/Models/GeneralConfigurationModel.cs`, `PigeonWatch/Api/WebApi/Models/CurrentUserModel.cs`, view model creators with interfaces in `PigeonWatch/Api/WebApi/ViewModelCreators/`, `PigeonWatch/Api/BusinessObjects/ClientConfiguration.cs`, `PigeonWatch/Api/BusinessObjects/CurrentUser.cs`, `PigeonWatch/Api/BusinessLogic/Services/IClientConfigurationService.cs`, `PigeonWatch/Api/BusinessLogic/Services/ClientConfigurationService.cs`, `PigeonWatch/Api/BusinessLogic/Services/IGeneralConfigurationService.cs`, `PigeonWatch/Api/BusinessLogic/Services/GeneralConfigurationService.cs`, `PigeonWatch/Api/Data/Repositories/IAccountRepository.cs`, `PigeonWatch/Api/Data/Repositories/AccountRepository.cs`

**Intent**: Implement D5. The anonymous client config gives the SPA the validation rules before login. The general config is the per-user source of current-user data and replaces a `/me` endpoint.

**Contract**:
- `[Route("configuration")]`.
- `GET client` is `[AllowAnonymous]` and returns `Task<ActionResult<ClientConfigurationModel>>`:
  - body: `{ passwordRules: { minLength, requireDigit, requireLowercase, requireUppercase, requireNonAlphanumeric }, displayNameRules: { minLength, maxLength } }`, built from `AccountRules`
  - `Cache-Control: public, max-age=300`
  - never contains user data
- `GET general` is `[Authorize]` and returns `Task<ActionResult<GeneralConfigurationModel>>`:
  - body: `{ currentUser: { id, email, displayName, roles: [] } }`
  - `Cache-Control: private, no-store`
- The controller reads the user id from `ClaimTypes.NameIdentifier` and the roles from `ClaimTypes.Role` claims.
- `IGeneralConfigurationService.GetAsync(Guid userId, CancellationToken = default)` → `IAccountRepository.GetCurrentUserAsync(Guid, CancellationToken = default)` returns `CurrentUser?`. An unknown id → 401.

#### 2. Database-unavailable → 503

**File**: `PigeonWatch/Api/Data/Diagnostics/DatabaseUnavailableExceptionHandler.cs`, `PigeonWatch/Api/DependencyInjection/DataServiceCollectionExtensions.cs`, `PigeonWatch/Api/WebApi.Host/Program.cs`

**Intent**: A paused serverless database currently surfaces as a 500 after EF's retries are exhausted. Turn it into a retryable 503 that the SPA's warm-up panel recognises.

**Contract**:
- `public class DatabaseUnavailableExceptionHandler : IExceptionHandler` with `TryHandleAsync(HttpContext, Exception, CancellationToken cancellationToken = default)`.
- It handles `RetryLimitExceededException`, and any exception whose inner chain contains a `SqlException` whose `Number` is in the handler's own private `static readonly` set of unavailability numbers: `40613, 40197, 40501, 49918, 49919, 49920` (Azure SQL paused, resuming or busy) and `-2, 53, 258, 10053, 10054, 10060, 233, 64, 121` (connect timeout or network failure). It does not use EF's internal `SqlServerTransientExceptionDetector` (EF1001, `.Internal` namespace). The set lives in the non-static handler class, because a `static readonly` field in a static holder fails `StaticClassTests`. Response: `503` ProblemDetails (`title: "Service warming up"`) with `Retry-After: 10`.
- Other exceptions are not handled, so they stay 500.
- Registered with `AddProblemDetails()` + `AddExceptionHandler<DatabaseUnavailableExceptionHandler>()` in `AddData`. `Program.cs` calls `app.UseExceptionHandler()` first in the pipeline.

#### 3. Rate limiting and forwarded headers

**File**: `PigeonWatch/Api/WebApi/RateLimiting/RateLimitPolicyNames.cs`, `PigeonWatch/Api/WebApi.Host/Program.cs`, `PigeonWatch/Api/WebApi/Controllers/AccountController.cs`, `PigeonWatch/Api/DependencyInjection/PigeonWatchEndpointRouteBuilderExtensions.cs`

**Intent**: Limit password spraying and sign-up spam per client IP, on top of Identity's per-account lockout.

**Contract**:
- Const holder `RateLimitPolicyNames.Auth = "auth"`.
- `Program.cs` registers `AddRateLimiter` with a fixed-window policy: 10 permits per 1 minute, partitioned by `HttpContext.Connection.RemoteIpAddress`, queue 0, `RejectionStatusCode = 429`.
- The policy returns `RateLimitPartition.GetNoLimiter` for requests whose path ends with `/auth/refresh`, so it limits only login and register. Every reload and new tab costs one refresh (the access token is memory-only), and an unguessable, Data-Protection-encrypted refresh token gains nothing from IP limiting.
- Pipeline order: `UseExceptionHandler` → `UseHttpsRedirection` (non-dev) → `UseRouting` → `UseCors` → `UseAuthentication` → `UseRateLimiter` → `UseAuthorization` → `MapControllers` → `MapPigeonWatch`.
- `[EnableRateLimiting(RateLimitPolicyNames.Auth)]` on `Register`, and `.RequireRateLimiting(RateLimitPolicyNames.Auth)` on the `auth` group.
- Forwarded headers come from the App Service app setting `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`, set manually (no code).

#### 4. Remove the smoke probe

**File**: delete `WebApi/Controllers/HealthController.cs`, `WebApi/Models/DatabaseHealthModel.cs`, `WebApi/ViewModelCreators/{I,}DatabaseHealthViewModelCreator.cs`, `BusinessLogic/Services/{I,}DatabaseHealthService.cs`, `BusinessObjects/DatabaseHealthResult.cs`, `BusinessObjects/SmokeCheckResult.cs`, `Data/Entities/SmokeCheckEntity.cs`, `Data/Mappers/{I,}SmokeCheckMapper.cs`, `Data/Repositories/{I,}SmokeCheckRepository.cs` (all under `PigeonWatch/Api/`). Edit `Data/PigeonWatchDbContext.cs` and the DI extensions. New migration `<timestamp>_RemoveSmokeCheck.cs`.

**Intent**: Close the F-01 review F3 exit: an anonymous probe that wakes the database on every call. `CLAUDE.md` says the smoke types go once real tables exist.

**Contract**:
- The `SmokeChecks` DbSet and its mapping are removed. The generated migration only drops `SMOKE_CHECK`.
- This is a deliberate one-off non-additive migration. The table's only consumer is removed in the same change, and the old build's `/health/db` fails only between the migrate and deploy jobs.

#### 5. Deploy probe, app settings and docs

**File**: `.github/workflows/deploy-api.yml`, `PigeonWatch/Api/CLAUDE.md`, `PigeonWatch/CLAUDE.md`, `context/deployment/deploy-plan.md`, `context/foundation/infrastructure.md`

**Intent**: Keep the deploy probe, runbooks and agent guidance true after S-01.

**Contract**:
- `deploy-api.yml:148`: the step is renamed to `Probe API` and calls `https://pigeonwatch-api.azurewebsites.net/configuration/client` with the same retry flags.
- `Api/CLAUDE.md`:
  - auth exists; the endpoint surface is `account/register`, `auth/login`, `auth/refresh`, `configuration/client`, `configuration/general`
  - `HttpContextCurrentUserProvider` and audit by user id
  - the `IEndpointRouteBuilder` allow-list
  - the unit-test command
  - smoke text removed
- `PigeonWatch/CLAUDE.md`: API surface updated; the contact-info rule notes that display names reject `@`.
- `deploy-plan.md`:
  - F3 accepted risk closed
  - probe URL updated
  - app setting `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` documented
  - Data Protection key location `/home/ASP.NET/DataProtection-Keys` noted, with the warning that slots would log users out
- `infrastructure.md:79,81`: "JWT signing key" replaced with "no signing key: Identity bearer tokens are protected by the Data Protection key ring".
- `Api/CLAUDE.md` and `Frontend/CLAUDE.md` also gain the resource rule:
  - every UI text is a `UiLabels.resx` key
  - key format
  - a new culture is added as `UiLabels.<culture>.resx`

#### 6. UI label resources

**File**: `PigeonWatch/Api/BusinessObjects/Resources/UiLabels.resx`, `PigeonWatch/Api/BusinessObjects/UiResources.cs`, `PigeonWatch/Api/BusinessObjects/UiLabelSet.cs`, `PigeonWatch/Api/BusinessLogic/Services/IUiResourceService.cs`, `PigeonWatch/Api/BusinessLogic/Services/UiResourceService.cs`, `PigeonWatch/Api/WebApi/Controllers/ResourcesController.cs`, `PigeonWatch/Api/WebApi/Models/UiResourcesModel.cs`, `PigeonWatch/Api/WebApi/ViewModelCreators/IUiResourcesViewModelCreator.cs`, `PigeonWatch/Api/WebApi/ViewModelCreators/UiResourcesViewModelCreator.cs`

**Intent**: Keep one source of truth for every UI label, and serve it per culture so S-12 only has to add `UiLabels.pl.resx` and a switcher.

**Contract**:
- `UiLabels.resx` is an embedded resource with no designer class. Keys are lowercase dot paths (`common.appName`, `auth.login.title`, `auth.errors.invalidCredentials`, `warmup.messages.1` …). Values are the English texts, with positional `{0}` placeholders where needed (e.g. `auth.validation.passwordMinLength` = `Use at least {0} characters.`).
- The resx holds the complete S-01 label set: every text named in Phase 4 and the warm-up messages, plus navigation, buttons, field labels and validation messages.
- `UiResources` is a const holder: `BaseName = "PigeonWatch.BusinessObjects.Resources.UiLabels"`, `DefaultCulture = "en"`.
- `IUiResourceService.GetAsync(string culture, CancellationToken = default)` returns `UiLabelSet(string Culture, IReadOnlyDictionary<string, string> Labels)`.
  - An unknown or unsupported culture resolves to `en`. Supported means `UiResources.DefaultCulture` (the neutral resx) or a culture for which `ResourceManager.GetResourceSet(culture, createIfNotExists: true, tryParents: false)` returns a non-null set, i.e. a `UiLabels.<culture>.resx` satellite exists. A malformed name (`CultureNotFoundException`) resolves to `en`. Whether the OS (ICU) recognises the name plays no part.
  - Keys missing in a specific culture fall back to English per key.
  - The returned culture is the one actually served.
- `[Route("resources")]`, `GET {culture}` is `[AllowAnonymous]` and returns `Task<ActionResult<UiResourcesModel>>`: `{ culture, labels: { key: text } }`, with `Cache-Control: public, max-age=300` and `Content-Language: <served culture>`. It does not touch the database.

#### 7. Registration never confirms a registered email

**File**: `PigeonWatch/Api/BusinessObjects/AccountErrorCodes.cs`, `PigeonWatch/Api/Data/Repositories/AccountRepository.cs`, `PigeonWatch/Api/BusinessObjects/Resources/UiLabels.resx`, `PigeonWatch/Api/UnitTests/AccountRepositoryTests.cs`, `PigeonWatch/CLAUDE.md`

**Intent**: Added 2026-10-04 (user decision). A `DuplicateEmail` answer tells anyone with `curl.exe` which emails have accounts, and a confirmed email list makes targeted password guessing cheaper.

**Contract**:
- `AccountErrorCodes.RegistrationFailed = "RegistrationFailed"`.
- `AccountRepository` maps Identity's `DuplicateEmail` and `DuplicateUserName` to a single `RegistrationFailed` with the description `The account could not be created.` (never Identity's own description, which names the email). `InvalidUserName` still becomes `InvalidEmail`. `DuplicateDisplayName` stays specific, because display names are public and not a login identifier.
- The resx drops `auth.errors.duplicateEmail` and adds `auth.errors.registrationFailed` = `We couldn't create an account with these details.`
- `PigeonWatch/CLAUDE.md` adds this as a hard rule, with the remaining limits (no email confirmation; `LockedOut` kept on login).

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build PigeonWatch/Api/PigeonWatchApi.slnx`
- Architecture tests pass: `dotnet test PigeonWatch/Api/ArchitectureTests/PigeonWatch.ArchitectureTests.csproj`
- Unit tests pass, adding the following:
  - the repository maps `DuplicateEmail` and `DuplicateUserName` to one `RegistrationFailed` whose code and description name neither the email nor "duplicate"/"taken", and keeps `DuplicateDisplayName` beside it
  - the exception handler maps `RetryLimitExceededException` and a wrapped `SqlException` with numbers `40613` and `53` to 503 with `Retry-After: 10`, and leaves `InvalidOperationException` and a `SqlException` with number `2627` unhandled
  - the general configuration service returns `null` for an unknown id
  - the client configuration matches `AccountRules`
  - command: `dotnet test PigeonWatch/Api/UnitTests/PigeonWatch.UnitTests.csproj`
- No pending model changes, and the `RemoveSmokeCheck` migration contains only `DropTable` for `SMOKE_CHECK`: `dotnet ef migrations has-pending-model-changes --project PigeonWatch/Api/Data/PigeonWatch.Data.csproj --startup-project PigeonWatch/Api/WebApi.Host/PigeonWatch.WebApi.Host.csproj`
- No reference to `health/db` remains in the repo: `git grep -n "health/db" -- PigeonWatch .github` returns nothing
- Unit tests cover the UI resource service:
  - `en` returns every key in the resx
  - `xx` and `pl` resolve to `en` with the served culture `en`
  - `ResourcesController` returns the culture and the map
  - command: `dotnet test PigeonWatch/Api/UnitTests/PigeonWatch.UnitTests.csproj`

#### Manual Verification:

- Locally with `curl.exe`:
  - `GET /configuration/client` returns the rules with `Cache-Control: public, max-age=300`
  - `GET /configuration/general` with a bearer token returns the user with `roles: []` and `Cache-Control: private, no-store`, and without a token returns 401
  - the 11th `POST /auth/login` within a minute returns 429
- Locally, with `$env:ConnectionStrings__Default` pointing at an unreachable host (e.g. `Server=10.255.255.1;Database=PigeonWatch;Connect Timeout=5;TrustServerCertificate=True`), `POST /auth/login` returns 503 with `Retry-After: 10`, not 500. Sent with `-H "Origin: http://localhost:4200"`, the response carries `Access-Control-Allow-Origin`.
- `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` is set on the App Service before merge, and after merge `deploy-api.yml` succeeds, including the migrate job and the new probe step.
- Live with `curl.exe`: register, login, `GET /configuration/general` and refresh all work, and blocked Identity routes return 404.
- Live rate limiting is per client: 11 quick logins from the laptop return 429, while a phone on mobile data can still log in during the same minute.
- Live preflight from the SPA origin succeeds: `curl.exe -i -X OPTIONS https://pigeonwatch-api.azurewebsites.net/configuration/general -H "Origin: https://wonderful-sea-07000d90f.6.azurestaticapps.net" -H "Access-Control-Request-Method: GET" -H "Access-Control-Request-Headers: authorization"` returns 2xx and allows the origin.
- Data Protection keys survive a restart: after `az webapp restart --name pigeonwatch-api --resource-group <rg>`, a refresh token issued before the restart still works on `POST /auth/refresh`.
- `GET /health/db` returns 404 live.
- Locally and live, `curl.exe -i <api>/resources/en` returns 200 with `Content-Language: en`, `Cache-Control: public, max-age=300` and the label map, and `/resources/xx` returns the same English map.

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 3: SPA auth infrastructure

### Overview

The non-visual plumbing: API base URL, token storage, session state, the D5 startup restore, interceptors for auth and warm-up, route guards with safe `returnUrl` handling, and the SWA config with SPA fallback and CSP. It also lays the UI foundation that Phase 4 consumes: Angular Material, design tokens, a token-driven Material theme, and base and common styles. Everything is covered by Vitest specs.

### Changes Required:

#### 1. Token store

**File**: `PigeonWatch/Frontend/src/app/core/auth/token-store.ts`

**Intent**: Keep the access token in memory only, and the refresh token in `localStorage` so sessions survive reloads and new tabs.

**Contract**: An injectable with an `accessToken` signal, and `refreshToken()`, `setTokens(accessResponse)` and `clear()`. The refresh token is stored under `pigeonwatch.refreshToken`. Every storage access is wrapped in try/catch and falls back to memory, so private modes that throw still work for the session.

#### 2. API clients and configuration state

**File**: `PigeonWatch/Frontend/src/app/core/auth/auth-api.ts`, `PigeonWatch/Frontend/src/app/core/configuration/configuration.service.ts`, `PigeonWatch/Frontend/src/app/core/http/http-context-tokens.ts`

**Intent**: Give every API call a typed client built from `environment.apiUrl`, and hold the two D5 configuration payloads as signals.

**Contract**:
- `AuthApi` methods:
  - `login(email, password)` → `POST {apiUrl}/auth/login`, returns `{ tokenType, accessToken, expiresIn, refreshToken }`
  - `refresh(refreshToken)` → `POST {apiUrl}/auth/refresh`
  - `register({ email, password, displayName })` → `POST {apiUrl}/account/register`
- All three set the `SKIP_AUTH` `HttpContextToken`.
- `ConfigurationService` exposes `clientConfig` and `generalConfig` signals, plus `loadClient()`, `loadGeneral()` and `clearGeneral()`. `loadClient()` uses `SKIP_AUTH`.
- `http-context-tokens.ts` exports `SKIP_AUTH`, `AUTH_RETRIED` and `SKIP_WARMUP`.

#### 3. Session service and non-blocking startup

**File**: `PigeonWatch/Frontend/src/app/core/auth/session.service.ts`, `PigeonWatch/Frontend/src/app/app.config.ts`

**Intent**: One owner of "who is logged in", which runs the D5 startup sequence without blocking first render.

**Contract**:
- `currentUser` is computed from `generalConfig().currentUser`; `isAuthenticated` is computed from it.
- `login(email, password)` stores the tokens, then `loadGeneral()`.
- `register(request)` calls the register API, then `login(...)`.
- `logout()` clears the tokens and `generalConfig`, then navigates to `/login`.
- `expire(returnUrl)` clears the session and navigates to `/login?returnUrl=…`.
- Cross-tab logout: the service listens to the `window` `storage` event. When `pigeonwatch.refreshToken` is removed (`newValue === null`) in another tab, it calls `expire(router.url)`. The listener lives here, not in `TokenStore`, so the token store does not depend on the session.
- `refreshTokens()` returns one shared in-flight observable (`shareReplay({ bufferSize: 1, refCount: false })`, reset on completion or error).
- `initialize()` runs the D5 sequence without letting client config gate routing. It starts `loadClient()` in parallel and does not await it; the register page already falls back to 8 / 3–30. `whenReady()` resolves immediately when no refresh token is stored; otherwise it resolves when `loadGeneral()` settles. It never rejects.
- `app.config.ts`:
  - `provideHttpClient(withInterceptors([authInterceptor, warmupInterceptor]))`
  - `provideAppInitializer(() => { inject(SessionService).initialize(); })`, which deliberately does not return the promise (see Critical Implementation Details)

#### 4. Auth interceptor

**File**: `PigeonWatch/Frontend/src/app/core/auth/auth.interceptor.ts`

**Intent**: Attach the bearer token to API requests, and recover from expired access tokens transparently.

**Contract**:
- Acts only on URLs that start with `environment.apiUrl` and lack `SKIP_AUTH`; it adds `Authorization: Bearer <accessToken>` when a token exists.
- On 401 for a request without `AUTH_RETRIED`, when a refresh token exists: await `session.refreshTokens()`, then retry once with `AUTH_RETRIED`.
- When the refresh fails with 400/401, or no refresh token exists: `session.expire(router.url)`.
- A refresh that fails with 503/0 propagates without logging out (warm-up handles it). A refresh that fails with 429 also propagates and keeps the tokens.

#### 5. Warm-up interceptor and state

**File**: `PigeonWatch/Frontend/src/app/core/warmup/warmup.interceptor.ts`, `PigeonWatch/Frontend/src/app/core/warmup/warmup-state.ts`

**Intent**: Turn cold starts and a paused database into an automatic, visible wait instead of errors.

**Contract**:
- `WarmupState` has a signal `status: 'idle' | 'warming' | 'failed'` and tracks the number of requests currently in warm-up.
- For API requests, `status` becomes `warming` when either:
  - a request is still pending after 3 s, or
  - a response is 503 or status 0.
- Retries: 503/0 retries after `Retry-After` seconds (default 10, minimum 5) until 120 s have passed since the request started.
- After 120 s: `status` becomes `failed` and the error propagates.
- `status` returns to `idle` when no request is warming.
- `startedDuringStartup` records whether the warm-up began before `whenReady()` resolved.
- 429 and other statuses pass through untouched.

#### 6. Guards and safe return URLs

**File**: `PigeonWatch/Frontend/src/app/core/auth/auth.guards.ts`, `PigeonWatch/Frontend/src/app/core/auth/safe-return-url.ts`, `PigeonWatch/Frontend/src/app/app.routes.ts`

**Intent**: Redirect logged-out visitors from any protected page to login with a way back, without creating an open redirect.

**Contract**:
- `authGuard: CanActivateFn` awaits `whenReady()`, then returns `true` or `UrlTree('/login', { returnUrl: state.url })`.
- `guestGuard` sends authenticated users to `safeReturnUrl(returnUrl)`.
- `safeReturnUrl(value)` returns `value` only if it starts with `/` and not with `//` or `/\`; otherwise it returns `/`.
- Routes, each page lazy-loaded:
  - `''` → home, with `authGuard`
  - `login` and `register`, with `guestGuard`
  - `'**'` → `redirectTo: ''`

#### 7. Static Web Apps config

**File**: `PigeonWatch/Frontend/public/staticwebapp.config.json`, `PigeonWatch/Frontend/angular.json` (only if needed)

**Intent**: Serve deep links like `/login` and `/register`, and add the CSP that D1 relies on to limit XSS against the stored refresh token.

**Contract**:
- `navigationFallback.rewrite = "/index.html"`, excluding static asset extensions.
- `globalHeaders` sets these headers:
  - `Content-Security-Policy: default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self'; connect-src 'self' https://pigeonwatch-api.azurewebsites.net; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'`
  - `X-Content-Type-Options: nosniff`
  - `Referrer-Policy: strict-origin-when-cross-origin`
- `angular.json` changes only if the built `index.html` contains an `onload=` handler (see Critical Implementation Details).

#### 8. Resource snapshot generator

**File**: `PigeonWatch/Frontend/scripts/generate-resources.mjs`, `PigeonWatch/Frontend/scripts/generate-resources.d.mts`, `PigeonWatch/Frontend/package.json`, `PigeonWatch/Frontend/tsconfig.spec.json`, `PigeonWatch/Frontend/.gitignore`, `.github/workflows/azure-static-web-apps-wonderful-sea-07000d90f.yml`

**Intent**: Bundle every culture's labels into the SPA from the same resx files the API serves, so the first render never waits for the API and the resx stays the single source.

**Contract**:
- A dependency-free Node script reads every `PigeonWatch/Api/BusinessObjects/Resources/UiLabels*.resx`. The culture is `en` for the neutral file and `<culture>` for `UiLabels.<culture>.resx`.
- It parses the `<data name><value>` pairs, decoding XML entities.
- It writes `src/app/core/resources/generated/ui-labels.<culture>.json` and `src/app/core/resources/generated/snapshots.ts`, which exports `supportedCultures` and a `culture → () => import(json)` map. `en` is imported statically so it is always in the main bundle.
- The script fails the build on a duplicate key or a non-`en` key missing from `en`.
- `prestart`, `prebuild` and `pretest` npm scripts run it. `generated/` is gitignored, so snapshots can't go stale in git.
- Testability under `ng test` (`@angular/build:unit-test` bundles specs for the browser platform; `node:` builtins are externalized and run in Node + jsdom):
  - `@types/node` is added as a dev dependency and `"node"` is added to `types` in `tsconfig.spec.json`; without them `node:fs` imports fail with TS2591 and the `.mjs` import with TS7016.
  - The script exports `parseResx(xml)` and `generate({ resxDir, outDir })`, typed by `generate-resources.d.mts`; the CLI entry only runs when invoked directly.
  - All paths resolve from `process.cwd()` (`PigeonWatch/Frontend` locally and in CI), never from `import.meta.url` or `__dirname`, because the spec bundle inlines the script under the spec's own module id. The Phase 4 label-coverage spec follows the same rule.
- The SWA workflow `paths` filter adds `PigeonWatch/Api/BusinessObjects/Resources/**`, so a label-only change redeploys the SPA.

#### 9. Resource service, culture selection and `t` pipe

**File**: `PigeonWatch/Frontend/src/app/core/resources/resource.service.ts`, `PigeonWatch/Frontend/src/app/core/resources/t.pipe.ts`, `PigeonWatch/Frontend/src/app/core/resources/culture-store.ts`, `PigeonWatch/Frontend/src/app/core/warmup/warmup.interceptor.ts`, `PigeonWatch/Frontend/src/app/app.config.ts`

**Intent**: Give components one way to read labels, choose the culture from the browser-saved preference with an English fallback, and replace the snapshot with the API map when it arrives.

**Contract**:
- `CultureStore.selected()` reads `localStorage` key `pigeonwatch.culture` (try/catch). It returns that value when it is in `supportedCultures`, otherwise `en`. It exposes `save(culture)` for S-12, unused in S-01.
- `ResourceService`:
  - `culture` and `labels` signals, seeded synchronously from the snapshot for `CultureStore.selected()` before first render; the `en` snapshot is used while a lazy snapshot loads
  - `t(key, ...args)` substitutes `{0}`…, and a missing key returns the key itself and logs `console.warn` once per key
  - `load()` fetches `GET {apiUrl}/resources/{culture}` with `SKIP_AUTH` + `SKIP_WARMUP` and replaces `labels` only on success
  - sets `document.documentElement.lang` to the served culture
- `TPipe` (`name: 't'`, `pure: false`, reads the `labels` signal): `{{ 'auth.login.title' | t }}` and `{{ 'auth.validation.passwordMinLength' | t: 8 }}`.
- The warm-up interceptor ignores requests carrying `SKIP_WARMUP`.
- The app initializer starts `ResourceService.load()` alongside the session restore. It does not await it.

#### 10. Angular Material, design tokens, theme, base and common styles

**File**: `PigeonWatch/Frontend/package.json`, `PigeonWatch/Frontend/src/styles/_tokens.scss`, `PigeonWatch/Frontend/src/styles/_material-theme.scss`, `PigeonWatch/Frontend/src/styles/_breakpoints.scss`, `PigeonWatch/Frontend/src/styles/_base.scss`, `PigeonWatch/Frontend/src/styles/_common.scss`, `PigeonWatch/Frontend/src/styles.scss`, `PigeonWatch/Frontend/src/app/app.scss`, `PigeonWatch/Frontend/angular.json`, `PigeonWatch/Frontend/src/styles/style-token-coverage.spec.ts`, `PigeonWatch/Frontend/CLAUDE.md`

**Intent**: This is the first real UI, so every common value is defined once, every shared pattern is written once, and the component primitives come from a maintained library instead of hand-rolled markup. Later slices restyle the app by changing tokens, not by hunting literals across components.

**Contract**:
- Angular Material:
  - `npm install @angular/material@~22.2.1 @angular/cdk@~22.2.1` (MIT, version-locked to our Angular 22.2). Not `ng add` (see What We're NOT Doing). No `@angular/animations`; Material 22 does not need it.
  - Feature components import Material modules per component (`MatFormFieldModule`, `MatInputModule`, `MatButtonModule`, `MatCardModule`, `MatProgressSpinnerModule`), never a shared "material module" barrel.
  - `matInput` binds to Signal Forms natively (it reads `FORM_FIELD` from `@angular/forms/signals` in 22.2.1), so no `ControlValueAccessor` glue is written.
- `_tokens.scss` declares CSS custom properties on `:root`, all prefixed `--pw-`. Together with `_material-theme.scss` and `_breakpoints.scss`, it is the only place that may contain raw color and length values. The S-01 set (values chosen at implementation, phone-first, WCAG AA contrast for text on its background):
  - color: `primary`, `on-primary`, `background`, `surface`, `text`, `text-muted`, `border`, `focus`, `overlay`, and `danger`, `success`, `info` each with a `-surface` pair (e.g. `--pw-color-danger`, `--pw-color-danger-surface`)
  - spacing scale: `--pw-space-1` … `--pw-space-8` (0.25rem → 4rem)
  - typography: `--pw-font-family` (system font stack; no web font, per CSP), `--pw-font-size-sm|md|lg|xl|2xl`, `--pw-font-weight-regular|semibold|bold`, `--pw-line-height-tight|normal`
  - shape: `--pw-radius-sm|md|lg|full`, `--pw-border-width`, `--pw-shadow-sm|md`
  - layout: `--pw-content-max-width`, `--pw-form-max-width`, `--pw-control-height` (at least 44px tap target)
  - motion: `--pw-duration-fast|normal`, `--pw-easing-standard`
  - stacking: `--pw-z-header`, `--pw-z-overlay`
- `_material-theme.scss` is the single bridge between our tokens and Material:
  - `html { @include mat.theme((color: (theme-type: light, primary: <closest mat palette>), typography: <same stack as --pw-font-family>, density: 0)); }`
  - then `@include mat.theme-overrides((...))` maps our tokens onto Material system tokens, at least `primary`, `on-primary`, `surface`, `on-surface`, `on-surface-variant`, `outline`, `error` and the corner shapes, as `var(--pw-…)` values, so `--mat-sys-primary` resolves to `--pw-color-primary`.
  - Any per-component tweak goes through the component's `mat.<component>-overrides(...)` mixin here, never through CSS on `.mat-*` / `.mdc-*` classes.
- `_breakpoints.scss` holds the breakpoints as SCSS variables plus a `from($name)` mixin, because media queries cannot read custom properties. It emits no CSS.
- `_base.scss`: a minimal reset (`box-sizing`, margins), `body` font, color and background from tokens, a `:focus-visible` ring using `--pw-color-focus`, and a `prefers-reduced-motion` rule that disables animations and transitions.
- `_common.scss` holds shared classes for what Material does not style, all prefixed `pw-` and built only from tokens. It never styles buttons or inputs (Material owns those):
  - layout: `pw-container` (centered, `--pw-content-max-width`), `pw-stack` (vertical flex with gap, overridable through `--pw-stack-gap`), `pw-cluster` (wrapping horizontal flex), `pw-center-screen`, `pw-full-width` (for `mat-form-field`)
  - typography: `pw-title`, `pw-subtitle`, `pw-text-muted`, `pw-text-small`
  - accessibility: `pw-visually-hidden`
- `styles.scss` only does `@use 'styles/tokens'; @use 'styles/material-theme'; @use 'styles/base'; @use 'styles/common';`. `angular.json` adds `stylePreprocessorOptions.includePaths: ["src/styles"]` to the build options so component stylesheets can `@use 'breakpoints'`.
- `app.scss` drops its literals (`640px`, `3rem`, `1rem`, `system-ui`) in favour of tokens or `pw-container`.
- Component stylesheets (`src/app/**/*.scss`) use `var(--pw-*)` tokens and `pw-*` classes only. They never redefine a `pw-*` class, never target `.mat-*` / `.mdc-*` classes, and never use `::ng-deep` or `!important`.
- `style-token-coverage.spec.ts` (paths from `process.cwd()`, like the generator spec) scans `src/app/**/*.scss`, `_base.scss` and `_common.scss` and fails on:
  - color literals: `#` hex, `rgb(`, `rgba(`, `hsl(`, `hsla(`
  - length literals in `px`, `rem` or `em`, except values listed in an explicit allow-list in the spec (e.g. `0`)
  - selectors containing `.mat-` or `.mdc-`, `::ng-deep` or `!important`
  - any `var(--pw-…)` that `_tokens.scss` does not define
  - `style="` attributes in `src/app/**/*.html`
- CSP: Material styles are compiled into the global stylesheet, and its overlays set inline `style` attributes, which `style-src 'self' 'unsafe-inline'` already allows. No new `connect-src` or `font-src` entries.
- Budgets: the production `initial` budget (500 kB warning) stays. If Material pushes the initial bundle over it, the pages keep lazy loading their Material imports rather than raising the budget.
- `Frontend/CLAUDE.md` already carries the UI library, styling and reusable-component rules (added with this plan revision). Once the files exist, this phase updates the paths in that section if they changed.

### Success Criteria:

#### Automated Verification:

- Production build succeeds and copies the SWA config: `npm run build` in `PigeonWatch/Frontend`, then `dist/Frontend/browser/staticwebapp.config.json` exists and `dist/Frontend/browser/index.html` contains no `onload=`
- Vitest specs pass, covering the following:
  - token store keeps working when `localStorage` throws
  - auth interceptor adds the header only for `apiUrl` requests and never for `SKIP_AUTH` requests
  - two concurrent 401s trigger exactly one `/auth/refresh` and both requests are retried once
  - a failed refresh calls `expire` with the current URL
  - a 503 during refresh does not log out
  - warm-up interceptor (fake timers): `warming` after 3 s pending; a 503 retries after `Retry-After`; `failed` after 120 s; a 429 is not retried
  - guards: a logged-out visit to `/sightings/1` → `/login?returnUrl=%2Fsightings%2F1`
  - `safeReturnUrl`: `//evil.com`, `/\evil.com` and `https://evil.com` → `/`; `/a?b=1` is kept
  - session `initialize()` with no refresh token never calls `/configuration/general`, and `whenReady()` resolves while `/configuration/client` is still pending
  - command: `npm test -- --watch=false` in `PigeonWatch/Frontend`
- Resource snapshot generation and resource specs pass, covering the following:
  - the generator produces `ui-labels.en.json` with every resx key and decodes `&amp;`/`&lt;`
  - the generator fails on a duplicate key
  - `CultureStore` returns `en` for no value, for an unsupported `xx`, and when `localStorage` throws, and returns a saved supported culture
  - `ResourceService.t` substitutes `{0}` and returns the key for a missing key
  - a failed `/resources` fetch keeps the snapshot labels and does not set warm-up
  - command: `npm test -- --watch=false` in `PigeonWatch/Frontend`
- UI foundation is in place and enforced:
  - the style-token coverage spec passes, and goes red when a hex color is added to `app.scss` (deliberate-break check)
  - the built global stylesheet (`dist/Frontend/browser/styles-*.css`) defines `--pw-color-primary`, `.pw-stack`, and `--mat-sys-primary` resolving to `var(--pw-color-primary)`
  - `dist/Frontend/browser/index.html` references no `fonts.googleapis.com` or `fonts.gstatic.com`
  - `package.json` lists `@angular/material` and `@angular/cdk` at `~22.2.1` and no `@angular/animations`
  - command: `npm test -- --watch=false` and `npm run build` in `PigeonWatch/Frontend`

#### Manual Verification:

- With the local API running and `npm start`, opening `http://localhost:4200/anything` while logged out lands on `/login?returnUrl=%2Fanything`. The pages themselves are placeholders until Phase 4.
- In devtools, `:root` shows the `--pw-*` custom properties, and changing `--pw-color-primary` live recolors a placeholder `mat-flat-button` on the login placeholder page.
- The browser devtools network tab shows the first request to `configuration/client` without an `Authorization` header.
- With the API stopped, the SPA still renders the English placeholder labels (from the snapshot), and `<html lang="en">` is set. After the API is started and the page reloaded, the network tab shows `GET /resources/en` returning 200.

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 4: SPA pages, warm-up panel and end-to-end

### Overview

The user-visible slice: login, registration, the protected home page with logout, and the warm-up panel, verified locally, live and in privacy-strict browsers. Every quoted text below is the English **value** of a `UiLabels.resx` key that the template reads through the `t` pipe. Templates and components contain no literal UI text. Keys that Phase 2 missed are added to the resx in this phase. Pages are composed from Angular Material primitives and the reusable app components below; a page writes its own markup and styles only for what is genuinely page-specific.

### Changes Required:

#### 1. Reusable app components

**File**: `PigeonWatch/Frontend/src/app/shared/ui/page-card/`, `PigeonWatch/Frontend/src/app/shared/ui/alert/`, `PigeonWatch/Frontend/src/app/shared/ui/field-errors/`, `PigeonWatch/Frontend/src/app/shared/ui/submit-button/` (component, template, styles, spec each), `PigeonWatch/Frontend/src/app/shared/ui/index.ts`

**Intent**: Write each UI pattern once. Login and register are the first two forms, and every later slice (sightings, messaging, profile) repeats the same card, message, error and submit patterns. Material covers the primitives; these components cover the app-level compositions Material does not ship.

**Contract**:
- Standalone, `ChangeDetectionStrategy.OnPush`, signal `input()`s, selector prefix `app-`, exported from `shared/ui/index.ts`.
- They contain no literal UI text and no resource keys of their own. Callers pass already translated text (through the `t` pipe in the caller's template) or project content, so the label-coverage spec still sees every key where it is used.
- Styling uses tokens and `pw-*` classes only; variations are inputs, never outside CSS on the component.
- `app-page-card`: a centered `mat-card` limited to `--pw-form-max-width`, with a `title` input rendered as the page `<h1>` and content projection for the body and a `[actions]` slot. Used by login and register, and by the warm-up panel's failed state.
- `app-alert`: `kind: 'error' | 'info' | 'success'` (token colors `--pw-color-<kind>` on `--pw-color-<kind>-surface`). `error` renders `role="alert"`, the others `role="status"`. Content is projected. Used for form-level errors (401, 429, warm-up failed) and the "Account created" notice.
- `app-field-errors`: takes a Signal Forms field and a `messages` map from error kind to translated text, and renders the first error of a touched invalid field. It is placed inside `<mat-error>` of a `mat-form-field`. Server errors set on a field (e.g. `DuplicateDisplayName`) render through the same component.
- `app-submit-button`: a `mat-flat-button` with `type="submit"`, a `busy` input that disables it, sets `aria-busy="true"` and shows a small `mat-progress-spinner`, and a projected label.
- Promotion rule (also in `Frontend/CLAUDE.md`): a pattern that appears in a second feature becomes a `shared/ui` component in the same change, not a copy.

#### 2. Login page

**File**: `PigeonWatch/Frontend/src/app/features/auth/login-page/` (component, template, styles, spec)

**Intent**: Let a user log in and return to where they were going.

**Contract**:
- Signal Forms with `email` (required, email format) and `password` (required).
- Composition: `app-page-card` → `mat-form-field` + `matInput` per field with `app-field-errors` in `<mat-error>`, `app-alert` for form-level errors and the notice, `app-submit-button` (busy while the request runs).
- On success: navigate to `safeReturnUrl(returnUrl)`.
- Error messages:
  - 401 → "Invalid email or password."
  - 401 with `detail: "LockedOut"` → "Too many failed attempts. Try again in a few minutes."
  - 429 → "Too many attempts from this device. Wait a minute and try again."
  - warm-up `failed` → "PigeonWatch is still waking up. Try again in a moment."
- A "Create account" link goes to `/register` and keeps `returnUrl`.
- If the router navigation state carries `{ email, notice }` (set when register succeeded but auto-login failed), prefill the email and show the notice. Personal data never goes in the URL.

#### 3. Registration page

**File**: `PigeonWatch/Frontend/src/app/features/auth/register-page/` (component, template, styles, spec)

**Intent**: Create an account and log the user straight in.

**Contract**:
- Signal Forms with `email`, `displayName`, `password` and `confirmPassword`.
  - The confirmation field is there because there is no password recovery (D4), so a mistyped password would otherwise lock the user out for good.
- Composition: the same building blocks as the login page (`app-page-card`, `mat-form-field` + `matInput`, `app-field-errors`, `app-alert`, `app-submit-button`). Password rules are shown as a `mat-hint`.
- Client-side rules come from `clientConfig` (with fallback to 8 / 3–30 if the config is missing): length, the four character classes, no `@` in the display name, and matching passwords.
- Server `errors` keys map to fields:
  - `RegistrationFailed` → a form-level `app-alert`: "We couldn't create an account with these details." It is never attached to the email field and never says the email is taken (Phase 2 item 7).
  - `DuplicateDisplayName` → "This name is already taken."
  - `Password*` → the password field
  - `DisplayName*` → the display-name field
  - `InvalidEmail` → the email field
- On success, `session.register` (which auto-logs in) is followed by navigation to `safeReturnUrl(returnUrl)`.
- If the auto-login fails, navigate to `/login` with navigation state `{ email, notice: "Account created — please log in." }` and the same `returnUrl`.

#### 4. Home page and app shell

**File**: `PigeonWatch/Frontend/src/app/features/home/home-page/` (component, template, styles), `PigeonWatch/Frontend/src/app/app.ts`, `PigeonWatch/Frontend/src/app/app.html`, `PigeonWatch/Frontend/src/app/app.scss`

**Intent**: Show who is logged in and offer logout.

**Contract**:
- When authenticated, the header shows "Logged in as {displayName}" and a "Log out" `mat-button` that calls `session.logout()`. The header layout uses `pw-cluster` and tokens, with `--pw-z-header`.
- The home page body is a placeholder for S-03 content.
- The root `App` renders `<app-warmup-panel />` above the router outlet.

#### 5. Warm-up panel

**File**: `PigeonWatch/Frontend/src/app/core/warmup/warmup-panel/` (component, template, styles, spec)

**Intent**: Make the free-tier wake-up feel deliberate and friendly, not broken.

**Contract**:
- A full-screen overlay (`--pw-color-overlay`, `--pw-z-overlay`) is shown while `status` is `warming` or `failed`, with `role="status"` and `aria-live="polite"` and a CSS-only animation whose timing uses the motion tokens (disabled under `prefers-reduced-motion` by `_base.scss`).
- While `warming`, messages rotate every ~6 s, starting with:
  1. "Congratulations! You're the first rescuer today. Wait for the application to load."
  2. "Our server was napping on a warm windowsill. Waking it up…"
  3. "Rounding up the pigeons — this only happens to the early birds."
  4. "Brewing coffee for the database. Hang tight!"
- When `failed`: "Still asleep. Give it another minute." inside an `app-page-card`, with a "Try again" `mat-flat-button`. If the warm-up began during startup the button reloads the page; otherwise it dismisses the panel so form input is kept.
- The rotation iterates over the `warmup.messages.*` keys present in the label map, so adding a message is a resx-only change.

#### 6. Label key coverage check

**File**: `PigeonWatch/Frontend/src/app/core/resources/label-coverage.spec.ts`

**Intent**: Catch any template or component that uses a key missing from the resx, or that slips in literal text.

**Contract**:
- The spec scans `src/app/**/*.html` and `*.ts` (excluding specs and `generated/`) for `'<key>' | t` and `t('<key>'…)` usages, and asserts each key exists in the generated `en` snapshot.
- It also asserts that HTML templates contain no text nodes other than whitespace and interpolations. Allowed exceptions are listed explicitly in the spec, e.g. `·`.

### Success Criteria:

#### Automated Verification:

- Production build succeeds: `npm run build` in `PigeonWatch/Frontend`
- Vitest specs pass, covering the following:
  - login page maps 401, `LockedOut` and 429 to the specified messages and navigates to a safe `returnUrl`
  - register page blocks submission for `ab`, `a@b`, a 7-character password and mismatched passwords
  - register page maps `RegistrationFailed` to the generic form-level alert and `DuplicateDisplayName` to the display-name field
  - register page falls back to `/login` with navigation state when the auto-login fails
  - warm-up panel shows the first message while `warming` and the "Try again" button when `failed`
  - command: `npm test -- --watch=false` in `PigeonWatch/Frontend`
- Reusable app component specs pass, covering the following:
  - `app-alert` renders `role="alert"` for `error` and `role="status"` for `info` and `success`, with the projected text
  - `app-field-errors` renders nothing for an untouched or valid field, and the mapped message for the first error of a touched invalid field, including a server-set error
  - `app-submit-button` with `busy` is disabled, has `aria-busy="true"` and shows the spinner
  - `app-page-card` renders the `title` as the `<h1>`
  - the login and register templates use `app-page-card`, `app-submit-button` and `app-field-errors` (asserted in their page specs), so the forms cannot drift back to hand-rolled markup
  - command: `npm test -- --watch=false` in `PigeonWatch/Frontend`
- Style token coverage spec (from Phase 3) still passes with all Phase 4 stylesheets in scope
- PR workflow green: `api-pr-checks.yml` (architecture tests, unit tests, migrations check, frontend build and tests)
- Label coverage spec passes: every used key exists in the `en` snapshot and no template contains literal text (`npm test -- --watch=false` in `PigeonWatch/Frontend`)

#### Manual Verification:

- Locally (API + `npm start`):
  1. Open `/` while logged out; you land on login with `returnUrl`.
  2. "Create account" → register `Jan K` → you are logged in and land on `/` with "Logged in as Jan K".
  3. Reload: still logged in.
  4. Open a second tab: still logged in.
  5. Log out: you are on `/login`, and `localStorage` has no `pigeonwatch.refreshToken`. The second tab also moves to `/login`.
  6. Log in again: you return to the original `returnUrl`.
- Locally, with the API's access token lifetime temporarily shortened, or the in-memory token cleared via devtools reload, a protected call refreshes silently with no visible error.
- Locally, with the API pointed at an unreachable SQL host (as in Phase 2), submitting login shows the warm-up panel with the first message, then the "Still asleep" state after ~2 minutes. After restoring the connection string, "Try again" and resubmitting logs in.
- Live after merge (SWA + App Service): the full register → reload → logout → login flow works in Chrome, in Chrome Incognito, and in Safari (macOS or iOS), with no cookies set by the API domain.
- Live: deep link `https://wonderful-sea-07000d90f.6.azurestaticapps.net/register` loads, the response headers include the CSP, and the console shows no CSP violations.
- Live cold start: the first visit after the API has idled (or after the DB auto-paused) shows the warm-up panel instead of an error, and the app loads on its own.
- Login, register and the warm-up failed state look like one app on a phone-width viewport (same card, field, button and message styling), and changing `--pw-color-primary` in `_tokens.scss` recolors the buttons, focused fields and links on all of them.
- Changing one value in `UiLabels.resx` and merging triggers both the API and the SPA deploy workflows, and the new text shows live after a hard reload.

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Testing Strategy

### Unit Tests:

- API (`PigeonWatch/Api/UnitTests`, xUnit + NSubstitute + SQLite in-memory):
  - display-name rules at the 2/3/30/31 boundaries and `@`
  - store CRUD and lookups by normalized columns
  - duplicate and concurrency error mapping
  - lockout persistence
  - repository mapping of `IdentityResult`
  - current-user provider fallback
  - exception-handler classification
  - the configuration services
  - the UI resource service: full `en` key set, per-key and per-culture fallback to `en`
- SPA (Vitest + `HttpTestingController` + fake timers):
  - token store resilience
  - single in-flight refresh under concurrent 401s
  - expiry redirect with `returnUrl`
  - warm-up timing and retry budget
  - guard redirects
  - `safeReturnUrl` open-redirect cases
  - page error mapping
  - the resource generator (entity decoding, duplicate keys)
  - culture selection (saved, unsupported, storage throws)
  - `t` placeholder substitution and missing-key behaviour
  - label key coverage with no literal template text
  - style token coverage (no color or length literals outside the token, theme and breakpoint files, no Material internals restyled, no undefined `--pw-*` token)
  - reusable app components: alert roles, field-error rendering, submit-button busy state, page-card title
- SQLite does not reproduce SQL Server collation and filtered indexes. Unique-index behaviour against SQL Server is covered by the Phase 1 manual checks on LocalDB.

### Integration Tests:

- None automated (user decision). The API flows are verified with `curl.exe` against LocalDB (Phase 1–2) and live (Phase 2). The SPA flows are verified end to end, locally and live (Phase 4).

### Manual Testing Steps:

1. Register, then reload, open a new tab, log out and log in again, locally and live.
2. Lock out an account with 5 wrong passwords, and trigger the rate limit with 11 quick logins.
3. Confirm blocked Identity routes return 404 and cookie flags return 400.
4. Force a 503 locally with an unreachable SQL host, and confirm the warm-up panel and its recovery.
5. Live: Safari and Chrome Incognito login, CSP headers, and DP-key survival across `az webapp restart`.

## Performance Considerations

- Login is the first request after idle. A cold start (~32 s) plus an auto-paused DB (resume up to ~1 min) is expected, and the warm-up panel makes it visible.
- `configuration/client` is cacheable (5 min) and does not touch the database, so the deploy probe and the startup call no longer wake the DB.
- PBKDF2 hashing (100k iterations) costs tens of milliseconds per login or register on F1. That's acceptable, and the rate limiter bounds abuse.

## Migration Notes

- `AddUserAccount` (Phase 1) is additive.
- `RemoveSmokeCheck` (Phase 2) drops `SMOKE_CHECK`. It is the one approved exception to the additive-only rule (`deploy-plan.md:109`): the table is throwaway, and its only consumer is removed in the same deploy.
- Both migrations run through the existing `deploy-api.yml` migrate job (EF bundle, CI principal with DDL rights). The runtime identity stays reader/writer only.
- Rollback: redeploy the previous build. `USER_ACCOUNT` is ignored by older code. Re-creating `SMOKE_CHECK` is not needed, because no rollback target before S-01 is expected.

## References

- Research: `context/changes/register-and-login/research.md`
- External research and binding decisions D1–D5: `context/changes/register-and-login/external-research.md`
- Lessons: `context/foundation/lessons.md` (`curl.exe` in PowerShell)
- Roadmap: `context/foundation/roadmap.md` S-12 `switch-ui-language` (EN/PL switcher building on this slice's resource management)
- Prior change: `context/archive/2026-10-03-persistence-wiring-smoke/` (F3 `/health/db` exit, F2 retry tuning)
- Pattern: `PigeonWatch/Api/WebApi/Controllers/HealthController.cs:10-27`, `PigeonWatch/Api/Data/Repositories/SmokeCheckRepository.cs:13-32`
- Identity source (release/10.0): `src/Identity/Core/src/IdentityApiEndpointRouteBuilderExtensions.cs`, `src/Identity/Core/src/IdentityBuilderExtensions.cs`, `src/Security/Authentication/BearerToken/src/BearerTokenConfigureOptions.cs`, `src/DataProtection/DataProtection/src/Repositories/DefaultKeyStorageDirectories.cs`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: User store and auth endpoints (API)

#### Automated

- [x] 1.1 Solution builds with no warnings — 6bf182a
- [x] 1.2 Architecture tests pass with only the StaticClassTests allow-list change — 6bf182a
- [x] 1.3 Unit tests pass (display-name rules, store, repository mapping, current-user provider) — 6bf182a
- [x] 1.4 Migration matches the model — 6bf182a
- [x] 1.5 Migration applies to LocalDB — 6bf182a

#### Manual

- [x] 1.6 Register returns 200; duplicate email and duplicate display name return 400 with codes — 6bf182a
- [x] 1.7 Login returns token pair with expiresIn 3600; refresh returns a new pair — 6bf182a
- [x] 1.8 Five wrong passwords lock the account (LockedOut) — 6bf182a
- [x] 1.9 Blocked Identity routes return 404; useCookies returns 400 without Set-Cookie — 6bf182a
- [x] 1.10 USER_ACCOUNT row has hashed password, CREATE_USER SYSTEM, UTC CREATE_DATE — 6bf182a

### Phase 2: Configuration endpoints, hardening and deploy (API)

#### Automated

- [x] 2.1 Solution builds — 5273c29
- [x] 2.2 Architecture tests pass — 5273c29
- [x] 2.3 Unit tests pass (exception handler, configuration services) — 5273c29
- [x] 2.4 No pending model changes; RemoveSmokeCheck only drops SMOKE_CHECK — 5273c29
- [x] 2.5 No health/db reference remains — 5273c29
- [x] 2.14 Unit tests cover the UI resource service — 5273c29
- [x] 2.16 Registration maps duplicate email to generic RegistrationFailed — 5273c29

#### Manual

- [x] 2.6 Local configuration endpoints return expected bodies and cache headers; 11th login returns 429 — 5273c29
- [x] 2.7 Unreachable DB returns 503 with Retry-After and CORS header locally — 5273c29
- [x] 2.8 Forwarded-headers app setting set; deploy workflow succeeds with new probe — f524fea
- [x] 2.9 Live register, login, general configuration and refresh work; blocked routes 404 — f524fea
- [x] 2.10 Live rate limiting is per client IP — f524fea
- [x] 2.11 Live CORS preflight with Authorization header succeeds — f524fea
- [x] 2.12 Data Protection keys survive app restart — f524fea
- [x] 2.13 Live /health/db returns 404 — f524fea
- [x] 2.15 Resources endpoint returns English labels with fallback — f524fea
- [x] 2.17 Registering a taken email returns only RegistrationFailed without the email — 5273c29

### Phase 3: SPA auth infrastructure

#### Automated

- [x] 3.1 Production build succeeds with SWA config and no onload handler — 31c62ba
- [x] 3.2 Vitest specs pass (token store, interceptors, guards, safeReturnUrl, session initialize) — 31c62ba
- [x] 3.5 Resource snapshot generation and resource specs pass — 31c62ba
- [x] 3.7 Angular Material installed and themed from tokens; style token coverage spec passes — 31c62ba

#### Manual

- [x] 3.3 Logged-out deep link redirects to login with returnUrl — 31c62ba
- [x] 3.4 configuration/client request carries no Authorization header — 31c62ba
- [x] 3.6 SPA renders snapshot labels with the API stopped — 31c62ba
- [x] 3.8 Tokens visible on :root and drive the Material theme live — 31c62ba

### Phase 4: SPA pages, warm-up panel and end-to-end

#### Automated

- [x] 4.1 Production build succeeds — 6e96dd8
- [x] 4.2 Vitest specs pass (login, register, warm-up panel) — 6e96dd8
- [x] 4.3 PR workflow green — 34c3f4a
- [x] 4.10 Label coverage spec passes — 6e96dd8
- [x] 4.12 Reusable app component specs pass and pages use them — 6e96dd8

#### Manual

- [x] 4.4 Local register, reload, new tab, logout and login with returnUrl — 6e96dd8
- [x] 4.5 Silent token refresh shows no error — 6e96dd8
- [x] 4.6 Warm-up panel appears on 503 and recovers — 6e96dd8
- [x] 4.7 Live flow works in Chrome, Chrome Incognito and Safari without API cookies — f524fea
- [x] 4.8 Live deep link loads with CSP and no violations — f524fea
- [x] 4.9 Live cold start shows warm-up panel and loads — f524fea
- [x] 4.11 Label-only resx change redeploys API and SPA — f524fea
- [x] 4.13 Pages share one visual system and a token change restyles all of them — 6e96dd8
