---
date: 2026-10-18T14:30:00+02:00
researcher: Claude (Sonnet 5.5) for Dominik Berniak
git_commit: 10b9c9d7498ce349e678eb8a4c7d7ab793669e04
branch: main
repository: 10xDevs
topic: "Rollout Phase 1 (test-plan.md Risk #1): registration during cold start, retry and the API integration harness"
tags: [research, registration, warm-up, retry, identity, integration-harness, vitest, xunit]
status: complete
last_updated: 2026-10-18
last_updated_by: Claude (Sonnet 5.5)
---

# Research: Cold-start registration resilience (Risk #1) and the API integration harness

**Date**: 2026-10-18T14:30:00+02:00
**Researcher**: Claude (Sonnet 5.5) for Dominik Berniak
**Git Commit**: 10b9c9d7498ce349e678eb8a4c7d7ab793669e04 (working tree has only untracked `context/` files)
**Branch**: main
**Repository**: 10xDevs (product code under `PigeonWatch/`)

## Research Question

Ground Risk #1 of `context/foundation/test-plan.md` §2 in code: a first registration that is slow, fails at the network level or is retried must end in exactly one usable account and an honest message, never a false "failed" for an account that was created. Verify or correct the response guidance ("retry logic exists, so cold start is covered"; "retrying a POST is safe"), locate existing tests, name the cheapest useful layer, flag speculative risks and misleading hot-spot evidence, and identify which API integration harness fits this stack and what the Phase 1 plan must decide to bootstrap it.

## Summary

- **Both challenged beliefs are wrong for the inspected path.** A retry mechanism exists, but it is a blind resend of the same request. On `POST /account/register` it resends after status `0` or `503` (`PigeonWatch/Frontend/src/app/core/warmup/warmup.interceptor.ts:312-314`, `:273-300`), and nothing on the client or the server makes that resend idempotent. A resend is safe only when the earlier attempt never reached the application.
- **The real failure is a false "failed", not a duplicate and not a half-created row.** If the first attempt creates the account and its response is lost (browser status `0`), the resend hits Identity's unique-email check. The API collapses that into one generic `RegistrationFailed` 400 (`PigeonWatch/Api/Data/Repositories/AccountRepository.cs:85-91`). The register page then shows "We couldn't create an account with these details." (`PigeonWatch/Api/BusinessObjects/Resources/UiLabels.resx`, key `auth.errors.registrationFailed`) for an account that exists. This path was reasoned from code; no test or run has exercised it (see Existing Tests and Open Questions).
- **Status `0` conflates two different situations.** The warm-up design treats `0` like `503` because production CORS headers are added by the platform and a platform error page has none (`context/archive/2026-10-04-register-and-login/plan.md:103`). So `0` means either "request never reached the app" (safe to resend) or "response lost after the app handled it" (unsafe to resend). The client cannot tell them apart.
- **After the 120 s budget the user is invited to resubmit into the same trap.** When the budget runs out the form shows `auth.errors.warmupFailed` ("PigeonWatch is still waking up. Try again in a moment."), and resubmitting the same details after an unobserved success yields the false "failed".
- **Existing tests cover the pieces and never the combination.** The interceptor is tested only against `/auth/login`; `SessionService.register` is tested with `authInterceptor` alone; the register page is tested with `SessionService` replaced by a mock. No test runs a register POST through the warm-up interceptor, and the backend has no test that drives `POST /account/register` through the HTTP pipeline.
- **Cheapest layers.** (1) A frontend test composing the real `warmupInterceptor`, `AuthApi` and `SessionService.register` over `HttpTestingController` with fake timers, scripting "status 0, then 400 `RegistrationFailed`", to pin what the user is told. (2) One API integration test through `WebApplicationFactory<Program>` pinning the server half of the contract (second identical register is a 400 `RegistrationFailed`, exactly one row, the first password still logs in). The two tests must share the same contract, otherwise the frontend fake just encodes an assumption.
- **Harness fit.** `Microsoft.AspNetCore.Mvc.Testing` 10.0.x with SQLite in-memory replacing the SQL Server `DbContext` registration is the cheapest fit and mirrors the SQLite use already in `UnitTests`. It cannot exercise the SQL-Server-specific duplicate-insert branch and the `SqlException`-based 503 mapping. The plan must decide how to treat those gaps, how to isolate the per-IP rate limiter between tests, and whether the CI step lands in Phase 1 or Phase 5.

## Detailed Findings

### 1. Client: what retries, and what the user is told

- `appConfig` registers `withInterceptors([authInterceptor, warmupInterceptor])`; the warm-up interceptor is the one closest to the network (`PigeonWatch/Frontend/src/app/app.config.ts:19`).
- `AuthApi.register` posts to `${environment.apiUrl}/account/register` with only `SKIP_AUTH` in its context, so the warm-up interceptor applies to it (`PigeonWatch/Frontend/src/app/core/auth/auth-api.ts:43-47`; the interceptor skips only requests carrying `SKIP_WARMUP`, `warmup.interceptor.ts:243`). Inspected scope: this one call site; no other code sets `SKIP_WARMUP` on register.
- The interceptor treats exactly `503` and `0` as warm-up errors and nothing else:

  ```ts
  function isWarmupError(error: unknown): error is HttpErrorResponse {
    return error instanceof HttpErrorResponse && (error.status === 503 || error.status === 0);
  }
  ```
  (`warmup.interceptor.ts:312-314`)
- On such an error it schedules another `attempt()` of the same `request` after `Retry-After` (default 10 s, floor 5 s) until 120 s have passed since the request started (`:273-300`, `:237-240`, `:316-320`). Only one attempt is in flight at a time, because the next attempt is scheduled from the error callback of the previous one (`:274-293`). A 429 is not retried (`warmup.interceptor.spec.ts:188-198`).
- `SessionService.register` is register then login; a failed login is wrapped in `AutoLoginError` (`PigeonWatch/Frontend/src/app/core/auth/session.service.ts:89-99`). `RegisterPage.handleFailure` turns `AutoLoginError` into a navigation to `/login` with the notice `auth.notices.accountCreated` ("Account created — please log in."), which is an honest message for a created account (`PigeonWatch/Frontend/src/app/features/auth/register-page/register-page.ts:183-188`, `:203-211`).
- Every other failure goes through `registerFailure`: a 400 carrying the code `RegistrationFailed` becomes the form-level `registrationFailed` message; status `0` or `503` becomes `warmupFailed`; anything else is `unexpected` (`PigeonWatch/Frontend/src/app/shared/auth/auth-errors.ts:42-56`, `:104-110`, `:112-116`).
- The submit handler blocks a second in-flight submit with a `busy` flag (`register-page.ts:165`), so a double click does not create overlapping requests from one tab.
- When the warm-up fails, the panel's "Try again" button reloads the page if the warm-up began during startup and otherwise only dismisses the panel, which keeps the form input (`PigeonWatch/Frontend/src/app/core/warmup/warmup-panel/warmup-panel.ts:60-61`). So after a failed register warm-up the user is expected to resubmit the same form.

### 2. Server: what a repeated register does

- `POST /account/register` is anonymous and rate limited with the `Auth` policy. It maps the request, calls `RegisterAsync` and returns 200 with the registered account, or a validation problem keyed by error code (`PigeonWatch/Api/WebApi/Controllers/AccountController.cs:23-35`, `:74-80`).
- `AccountService.RegisterAsync` trims, validates length and `@` rules, then delegates to the repository (`PigeonWatch/Api/BusinessLogic/Services/AccountService.cs:8-22`). It carries no request identity or idempotency key.
- `AccountRepository.CreateAsync` calls `userManager.CreateAsync` and maps failures; `DuplicateEmail` and `DuplicateUserName` both become `RegistrationFailed` with the fixed description "The account could not be created." (`PigeonWatch/Api/Data/Repositories/AccountRepository.cs:13-30`, `:85-91`). Identity is configured with `RequireUniqueEmail = true` (`PigeonWatch/Api/DependencyInjection/AccountServiceCollectionExtensions.cs:24`).
- Consequence for a resend of an already-created registration, **inferred** from Identity's behaviour (its user validator runs inside `UserManager.CreateAsync` before the store is called; that framework code is outside this repo and was not run): the same email is found, `DuplicateEmail`/`DuplicateUserName` is returned and the client receives the generic 400. The display-name duplicate check lives in the store (`PigeonWatch/Api/Data/Identity/PigeonWatchUserStore.cs:26-31`) and would not be reached, so the resend does not surface the specific `DuplicateDisplayName` message. The Phase 1 integration test should observe this, not assume it.
- Persistence is a single table row: `ApplicationUser` carries `PasswordHash` itself and `PigeonWatchUserStore.CreateAsync` performs one `SaveChanges` (`PigeonWatch/Api/Data/Identity/ApplicationUser.cs`, `PigeonWatchUserStore.cs:26-52`). The database also enforces unique indexes on normalized email, user name and display name (`PigeonWatch/Api/Data/PigeonWatchDbContext.cs:25-27`).
- The only 503 the application itself produces comes from `DatabaseUnavailableExceptionHandler`: `Retry-After: 10` for `RetryLimitExceededException` or a wrapped `SqlException` whose number is in its fixed set (`PigeonWatch/Api/Data/Diagnostics/DatabaseUnavailableExceptionHandler.cs:14-22`, `:45-55`). In production the context uses `UseSqlServer` with `EnableRetryOnFailure()` (`PigeonWatch/Api/DependencyInjection/DataServiceCollectionExtensions.cs:27-33`).

### 3. Verdict on the response guidance

| Guidance | Verdict | Evidence |
|---|---|---|
| "A first registration that is slow, fails at the network level, or is retried ends in exactly one usable account" | Needs splitting. "Exactly one row" is already enforced by unique indexes and the validator for this path (the duplicate-row outcome cannot happen for the same normalized email). "Usable" and "honest message" are the parts not protected. | `PigeonWatchDbContext.cs:25-27`; `AccountRepository.cs:85-91`; `auth-errors.ts:112-116` |
| Challenge: "retry logic exists, so cold start is covered" | **Confirmed as a false belief.** The retry covers the wait (warm-up panel, `Retry-After`, 120 s budget) but not the outcome of a request whose result was lost. It also does not distinguish a request that never ran from one that ran. | `warmup.interceptor.ts:273-300`, `:312-314` |
| Challenge: "retrying a POST is safe" | **Confirmed false in the status-`0` case, unverified for the app-generated 503 case.** A platform `503` that never reached the app is safe to resend. For the app's own 503 (`RetryLimitExceededException` or a database-unavailable `SqlException`), whether the insert could already be committed is not established by anything inspected; it depends on EF's execution-strategy commit handling and was not run. | `DatabaseUnavailableExceptionHandler.cs:45-55`; `DataServiceCollectionExtensions.cs:27-33` |
| Anti-pattern: "mocking the retry itself" | Valid, and the current frontend specs already contain that pattern: `RegisterPage` is tested with `SessionService` replaced by a mock, so the retry and the real error mapping never meet. | `register-page.spec.ts:36-43` |
| Anti-pattern: "asserting only the happy path" | Valid. The one existing register-through-HTTP spec asserts the 200 path, a 400 `RegistrationFailed` and a 503 on login, all without the warm-up interceptor. | `session.service.spec.ts:192-242` |

### 4. Constraints any fix or test must respect

- **Non-enumeration is a hard rule.** The API must never confirm that an email is registered; `DuplicateEmail` is deliberately collapsed into one generic error (`PigeonWatch/CLAUDE.md`, "Hard rules"). Any message such as "this email already exists, try logging in" for a repeated register would break it for a stranger submitting someone else's email, and is also Risk #6 territory (test-plan §2 row 6).
- A recovery that uses only the credentials the submitter just typed does not reveal anything beyond what that submitter already proves: login succeeds only when the typed password matches the account that exists. Whether to build such a recovery is a plan decision (see "Decisions for the Phase 1 plan"); this research only records that it does not conflict with the hard rule.
- The `Auth` limiter allows 10 requests per minute per remote IP address (fixed window, queue limit 0) on register and login (`PigeonWatch/Api/WebApi.Host/Program.cs:37-54`; login group in `PigeonWatch/Api/DependencyInjection/PigeonWatchEndpointRouteBuilderExtensions.cs:17-19`). A retry that reaches the app and is limited shows `tooManyRequests` (`auth-errors.ts:104-106`), which is another way to show a failure for an account that may already exist. This is an edge case, reached only after an earlier attempt completed without the client knowing.

### 5. Existing tests (located)

Frontend (Vitest via `ng test`; `PigeonWatch/Frontend/package.json:12`, `@angular/core ^22.2.0`, `vitest ^5.0.3`):

- `core/warmup/warmup.interceptor.spec.ts` covers the 3 s pending threshold, `Retry-After` handling, the 5 s floor and 10 s default, status `0` treated like `503`, failure after 120 s, recovery, 429 not retried and `SKIP_WARMUP`. All requests use the `/auth/login` URL and an empty body (`:51`, `:85`). It never uses the register URL, and it never asserts that the resent body equals the original or what an application error on the resend becomes.
- `core/auth/session.service.spec.ts:192-242` covers register then login, a 400 passthrough and `AutoLoginError`. Its `TestBed` provides only `authInterceptor` (`:33`), so a `0` or `503` on register propagates immediately and the warm-up path is not involved.
- `features/auth/register-page/register-page.spec.ts` mocks `SessionService` entirely (`:36-43`) and covers the `RegistrationFailed` mapping, `DuplicateDisplayName` and the `AutoLoginError` fallback to `/login` (`:150`, `:163`, `:195`).
- `shared/auth/auth-errors.spec.ts` exists; a case-insensitive search for "register" in it found no match, so the register mapping is covered only through the page spec.

Backend (xUnit with NSubstitute and SQLite in-memory; `PigeonWatch/Api/UnitTests/PigeonWatch.UnitTests.csproj`, `xunit.v3.mtp-off` 4.0.1):

- `AccountRepositoryTests` mocks `UserManager` and asserts the duplicate collapse and that the failure never names the email (`:50-74`).
- `PigeonWatchUserStoreTests` runs the real store on SQLite for display-name uniqueness, concurrency and lockout fields; it has no duplicate-email or unique-index violation case (a search for `Duplicate`, `2601` and `2627` found only the display-name cases).
- `AccountControllerTests` covers profile and password only; there is no `Register` action test there.
- `DatabaseUnavailableExceptionHandlerTests` covers the 503 mapping in isolation.
- Not found: any test that starts the application, calls an endpoint over HTTP, registers the same email twice, or combines a failed attempt with a later one. Inspected scope: `PigeonWatch/Api/UnitTests` and `PigeonWatch/Api/ArchitectureTests` file lists, and a search for `account/register` across `PigeonWatch/Frontend/src/app` and `PigeonWatch/Api` (only `auth-api.ts:44` and three `session.service.spec.ts` lines matched).

### 6. Cheapest useful layers

1. **Frontend, real chain over a fake HTTP boundary** (cost: low, one spec file). Compose `provideHttpClient(withInterceptors([authInterceptor, warmupInterceptor]))` with `provideHttpClientTesting()`, the real `AuthApi` and the real `SessionService.register`, with fake timers as in `warmup.interceptor.spec.ts`. Script: first attempt answered with status `0`, resend answered with a 400 `RegistrationFailed`. Assert (a) the resend carries the identical body, (b) what `registerFailure` and the page present to the user, (c) the cases where the first attempt returned `503` with `Retry-After` and the resend succeeds (one account, then login). Keep the retry real; script only the network.
2. **API integration, one request pipeline** (cost: medium once, then cheap). Register the same payload twice and read back state: second call is a 400 with only the `RegistrationFailed` code and no email in the body, the first password logs in, exactly one `USER_ACCOUNT` row exists. This pins the "400 on repeat" fact that layer 1 assumes. Also assert the happy path first, so the test cannot pass on a server that fails everything.
3. **A stateful fake backend in the frontend spec** is optional: a small in-test fake that records created emails, answers 200 then 400 on a repeat and can drop a response, proves the client sequence without scripting each status by hand. It still only reflects an assumption about the server, so it is worth building only if the plan adopts a client-side recovery that issues a follow-up login.
4. **Not recommended for Phase 1:** e2e through a browser (Phase 3 owns the one e2e flow), and a real cold start of Azure (not reproducible in CI; the symptoms are reproduced by status `0` and `503` + `Retry-After`).

### 7. API integration harness: fit and what the plan must decide

**Fit (observed).** `PigeonWatch.WebApi.Host` is an ASP.NET Core `Sdk.Web` project whose entry point is a public, non-static `Program` class with a `Main` method (`PigeonWatch/Api/WebApi.Host/Program.cs:8-10`; assembly name `PigeonWatch.WebApi.Host`). The host builds the whole pipeline itself (`AddPigeonWatch`, exception handler, CORS, authentication, rate limiter, `MapControllers`, `MapPigeonWatch`; `Program.cs:25`, `:58-80`). `Microsoft.AspNetCore.Mvc.Testing` is the standard in-process harness for that shape, with `WebApplicationFactory<Program>`. The package has 10.0.x releases on nuget.org (latest seen in the flat container listing: 10.0.13, checked 2026-10-18) but is **not in the local NuGet cache**, so the first restore needs network. `UnitTests` already pins `Microsoft.EntityFrameworkCore.Sqlite` 10.0.12 and `Microsoft.NET.Test.Sdk` 18.10.1, and `Directory.Build.props:5` enforces code style in build, which a new test project would inherit.

**Database substitution (observed + inferred).** Production registers the context with `UseSqlServer(..., EnableRetryOnFailure())` inside `AddDbContext` (`DataServiceCollectionExtensions.cs:27-33`). A factory override has to remove that registration and its options configuration and add a SQLite one on a held-open `SqliteConnection`, as `PigeonWatchUserStoreTests` does today (`PigeonWatchUserStoreTests.cs:19-25`: `new("DataSource=:memory:")`, `EnsureCreated`). Whether the removal must also cover the options-configuration descriptor that EF Core 9+ adds (to avoid a "two providers" error) is **inferred from EF behaviour and untested here**; a spike should settle it.

**Fidelity gaps of SQLite (observed).**

- `DuplicateIndexError` only recognises `SqlException` numbers 2601 and 2627 (`PigeonWatchUserStore.cs:20-21`, `:298-300`). On SQLite a real unique-index violation raises a different exception type and would not be translated. A sequential repeat register is still translated by Identity's validator before the insert, but a true concurrent duplicate insert is not reproducible on SQLite.
- `DatabaseUnavailableExceptionHandler` maps `RetryLimitExceededException` or a `SqlException` number from its fixed set (`DatabaseUnavailableExceptionHandler.cs:14-22`, `:45-55`). `RetryLimitExceededException` is the usable fake on SQLite (a failing seam, for example an EF save-changes interceptor, can throw it); a `SqlException` is not realistically constructible in a test. This is an inference about constructibility, not run.
- `EnableRetryOnFailure()` and its commit-ambiguity behaviour are SQL Server execution-strategy behaviour and are absent on SQLite.

**Alternatives.** A SQL Server container (Testcontainers) gives full fidelity for the two gaps above, at the cost of Docker in CI and slower tests; `ubuntu-latest` runners are what the PR workflow uses (`.github/workflows/api-pr-checks.yml:20-21`). That is a cost × signal choice for the plan. Hosting the API for real and calling it with a standalone HTTP client adds a process and a port with no benefit over `WebApplicationFactory` for this stack.

**Rate limiter (inferred, needs a spike).** The `Auth` limiter partitions by `RemoteIpAddress?.ToString() ?? string.Empty` (`Program.cs:45-46`). `TestServer` normally leaves the remote address unset, so every request in one factory would likely share the single `""` partition and hit 10 requests per minute. A register-plus-login test pair, repeated across a test class, could trip 429 and make tests order- or time-dependent. The plan must choose between a fresh factory per test, a test middleware that sets a distinct `RemoteIpAddress`, or replacing the limiter (which would make Phase 2's rate-limit assertions unrealistic).

**CI and project wiring (observed).** `api-pr-checks.yml` builds `PigeonWatchApi.slnx` once and then runs `dotnet test` per named project (`:29-36`); a new test project would be built by the solution build only if it is added to the `.slnx`, and would not run until a step names it. Test-plan §5 says the API integration gate is "required after §3 Phase 1" while §3 Phase 5 says "wire the new suites into PR checks"; the two statements disagree on which phase adds the CI step. The architecture tests enumerate only the six production assemblies (`PigeonWatch/Api/ArchitectureTests/PigeonWatchAssemblies.cs:25-26`), so a new test project and its references do not enter their rules, and `WebApiHost` is already forbidden from referencing `Data`, `BusinessLogic` and EF (`LayerDependencyTests.cs:54-64`); the new project is a separate assembly and is not subject to that rule.

### 8. Speculative risks and misleading evidence

- **Misleading hot-spot evidence for Risk #1.** The likelihood does not come from churn on the register path. Over the last 30 days (`git log --since=30.days` at `10b9c9d`), `core/warmup/` was touched by one commit (`f524fea`, 2026-10-05, S-01), `Data/Identity/` by the same single commit, and `AccountController.cs` by three commits of which two add profile and password endpoints. `core/` (6 commits) and `features/` (9 commits) churn is mostly profile, sighting and UI work. The risk's likelihood is carried by the environment (F1 sleep, serverless database) and the untested retry combination, not by recent change.
- **Counting discrepancy in the plan.** Test-plan §2 row 3 cites `PigeonWatch/Api/WebApi` at "about 20 commits/30d"; this research counted 5 commits touching that path in the same window (`git log --since=30.days --format=%h -- PigeonWatch/Api/WebApi | wc -l`). Different metrics (file touches versus commits) may explain it; flagged for the next `--refresh`, not relied on here.
- **"Duplicate or half-created account" is not supported by the inspected code.** Duplicates are prevented by unique indexes and the validator (`PigeonWatchDbContext.cs:25-27`), and creation is one row in one `SaveChanges` (`PigeonWatchUserStore.cs:26-52`). The only multi-step state is "registered but auto-login failed", which the SPA already reports honestly via `AutoLoginError`. The plan's risk text should be read as "false failure for a created account" plus "gives up".
- **Overlapping attempts are unlikely from the retry itself.** One interceptor instance never has two attempts in flight (`warmup.interceptor.ts:274-293`) and the form blocks a second submit (`register-page.ts:165`). Two attempts can still overlap from two browser tabs; this was not inspected beyond the form and the interceptor and is treated as low priority, plus not reproducible on SQLite.
- **Speculative: "the app's own 503 may follow a committed insert."** No evidence either way in the inspected code; it depends on EF execution-strategy commit behaviour against SQL Server. Kept as an open question rather than a test target.
- **Lessons:** `context/foundation/lessons.md` has one entry (use `curl.exe` in PowerShell commands); it applies to any manual verification steps the plan hands to the user, and does not change the findings.

## Code References

- `PigeonWatch/Frontend/src/app/app.config.ts:19` - interceptor order `[authInterceptor, warmupInterceptor]`
- `PigeonWatch/Frontend/src/app/core/auth/auth-api.ts:43-47` - register POST, only `SKIP_AUTH` set
- `PigeonWatch/Frontend/src/app/core/warmup/warmup.interceptor.ts:237-240` - thresholds (3 s pending, 120 s budget, 10 s default, 5 s floor)
- `PigeonWatch/Frontend/src/app/core/warmup/warmup.interceptor.ts:273-300` - attempt loop that resends the same request
- `PigeonWatch/Frontend/src/app/core/warmup/warmup.interceptor.ts:312-314` - `503` or `0` are warm-up errors
- `PigeonWatch/Frontend/src/app/core/auth/session.service.ts:89-99` - register then login, `AutoLoginError`
- `PigeonWatch/Frontend/src/app/features/auth/register-page/register-page.ts:162-211` - submit, failure handling, fallback to `/login`
- `PigeonWatch/Frontend/src/app/shared/auth/auth-errors.ts:42-56`, `:104-122` - register failure mapping
- `PigeonWatch/Frontend/src/app/core/warmup/warmup-panel/warmup-panel.ts:60-61` - reload or dismiss after a failed warm-up
- `PigeonWatch/Api/WebApi/Controllers/AccountController.cs:23-35` - register action
- `PigeonWatch/Api/BusinessLogic/Services/AccountService.cs:8-22` - validation then repository
- `PigeonWatch/Api/Data/Repositories/AccountRepository.cs:13-30`, `:85-91` - create and duplicate collapse
- `PigeonWatch/Api/Data/Identity/PigeonWatchUserStore.cs:20-21`, `:26-52`, `:298-300` - single-row create and SQL-Server-only duplicate detection
- `PigeonWatch/Api/DependencyInjection/AccountServiceCollectionExtensions.cs:24` - `RequireUniqueEmail`
- `PigeonWatch/Api/DependencyInjection/DataServiceCollectionExtensions.cs:27-33` - SQL Server with retry-on-failure
- `PigeonWatch/Api/Data/Diagnostics/DatabaseUnavailableExceptionHandler.cs:14-22`, `:45-55` - 503 mapping
- `PigeonWatch/Api/WebApi.Host/Program.cs:8-10`, `:37-54`, `:58-80` - entry point, limiter, pipeline
- `.github/workflows/api-pr-checks.yml:29-36` - build and per-project test steps

## Architecture Insights

- Cold-start handling is a client concern implemented once, generically, in an HTTP interceptor; the server contributes only the `503` + `Retry-After: 10` mapping for a paused database. The interceptor has no notion of method or idempotency.
- The enumeration defence (one generic `RegistrationFailed`) is what turns a harmless resend into a misleading message: the same property that protects Risk #6 hides the fact that the account exists. A fix has to be found that keeps the hard rule; "tell the user the email exists" is excluded.
- `SessionService.register` already encodes one correct honesty pattern (account created, next step failed, say so), which is the shape a recovery for "register resend failed" can follow.

## Historical Context (from prior changes)

- `context/archive/2026-10-04-register-and-login/plan.md:32` - the cold-start decision: warm-up panel, automatic retries for about 2 minutes, a paused database surfaces as 503 + `Retry-After`. **Supported** by the interceptor and handler code above.
- `plan.md:101` - "a login retried through a 2-minute warm-up stays under the limit" (10 per minute, 10 s `Retry-After`, 5 s floor). **Partial**: holds for `Retry-After: 10` from the app (6 per minute); the interceptor would send up to 12 per minute if a response advertised the 5 s floor, and the claim says "login", not register plus login.
- `plan.md:103` - status `0` is treated like `503` because platform error responses lack CORS headers. **Supported by design intent**; it is also the source of the `0` ambiguity described above. Not verified against a live platform response here.
- `plan.md:611` - interceptor spec scope "warming after 3 s; a 503 retries after Retry-After; failed after 120 s; a 429 is not retried". **Supported** by `warmup.interceptor.spec.ts`; it was never extended to a non-idempotent request or to what happens after a response is lost.
- `context/archive/2026-10-04-register-and-login/plan.md:77` - EF retry tuning explicitly out of scope for S-01; still unaddressed.

## Related Research

- `context/archive/2026-10-04-register-and-login/research.md` - original S-01 research; not re-read in full for this pass.

## Decisions for the Phase 1 plan

These are not settled by research; they change what gets built.

1. **Is the false-failure a product bug to fix in Phase 1, or only to pin with a test?** The rollout goal says "prove". If a failing test is written first, the plan must either include the fix or record the test as an expected-failure with an owner. Candidate fixes that keep the non-enumeration rule: do not resend the register POST on status `0` (retry only on an app or platform `503`); warm the backend with a cheap anonymous GET before the POST; or, after a retried register ends in `RegistrationFailed`, attempt a login with the submitted credentials and treat success as "account created". A server-side idempotency key is heavier and probably out of proportion.
2. **What message is honest when it is truly unknown?** The three states are: created (go to login with the existing notice), not created (retry), unknown after the budget. The plan must name the user-facing text for the third state through the resx file, as the project rule requires.
3. **Harness database:** SQLite in-memory (recommended default by cost × signal) versus a SQL Server container. Record which of the SQL-Server-only branches stay uncovered (duplicate-insert translation, `SqlException` 503 mapping, commit ambiguity) and where they are noted in test-plan §7.
4. **Rate-limiter isolation** for tests that call register and login repeatedly (see section 7).
5. **Where the 503 is faked:** a failing save-changes seam throwing `RetryLimitExceededException`, or only the existing handler unit test plus a frontend script. Decide whether Phase 1 needs a server-side 503 integration test at all.
6. **CI step ownership:** add the `dotnet test` line for the new project in Phase 1 (consistent with §5) or defer to Phase 5 (consistent with §3). Resolve the contradiction in test-plan §3 and §5 when the plan lands, and respect the lesson boundary that pipeline authoring is not a Module 3 topic: adding one test step is a configuration choice for the plan, not new YAML design.
7. **Cookbook entries to fill in** at the end of the phase: §6.2 (frontend test for slow or failing HTTP, with the "keep the retry real, script the network" rule) and §6.3 (adding an API integration test: factory, database swap, limiter isolation, how to read back state).

## Open Questions

- Does a resend after a lost-response success really return the generic 400 on the real pipeline (inferred above from Identity's validator order; to be observed by the Phase 1 integration test)?
- Can the app's own 503 follow a committed insert on SQL Server with `EnableRetryOnFailure`? Not established; needs either a SQL Server run or an accepted gap.
- Does `TestServer` really put all requests in one limiter partition (inferred; needs a one-line spike before the harness design is fixed)?
- Does removing the SQL Server `DbContext` registration in a `WebApplicationFactory` override need more than removing `DbContextOptions<PigeonWatchDbContext>` on EF Core 10.0.12 (inferred; needs a spike)?
- How does the browser report a real cold-start platform response to the SPA on the deployed topology (status `0` versus `503`)? Not inspected on the live site; the plan's CORS note says both are possible.
