# Server-side observability logging — Plan Brief

> Full plan: `context/changes/server-side-observability-logging/plan.md`
> Research: `context/audits/observability/2026-10-25_auth-sightings.md` (the audit this change implements)

## What & Why

Add structured, privacy-safe server logs to the API so production failures carry context and leave a signal. Today the whole API has one log call, so validation, lost-user, concurrency, lockout and rate-limit failures end as bare 400/401/404/429 with nothing in stdout, no line has a user id or trace id, and a database outage looks like a cold start.

## Starting Point

Pipeline in `Program.cs` with `UseExceptionHandler`, a rate limiter without `OnRejected`, and `Microsoft.AspNetCore` capped at Warning (no request lines). Controllers duplicate a Result-to-400 helper and return bare 401s; `AccountRepository` and `PigeonWatchUserStore` drop Identity codes and concurrency causes; `DatabaseUnavailableExceptionHandler` logs one constant Warning. No test captures logs.

## Desired End State

A responder sees one summary line per request, every line carrying trace id and (when known) user id, a log for every unexplained 400/401/404, the original Identity code behind `RegistrationFailed`, cold-start 503s (Warning) separated from real DB outages (Error) with the SQL number, and login, 429 and stubbed-route events. No client-facing response changes.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| --- | --- | --- | --- |
| Which fixes | Audit steps 1-5, API only | SPA seam and polish are separate later changes | Plan |
| Request summary | Own inline summary line (method, route template, status, duration) | Framework request lines would log the nearby-search coordinates in the query string | Plan |
| 429 logging | Policy, route and a missing-IP flag, no IP | Avoids personal data in logs of unknown retention | Plan |
| Unexpected Identity failures | Log them, keep the 400 response | No contract change; existing contract tests stay valid | Plan |
| 503 handler | Keep 503, cold-start numbers Warning, other causes Error, add SQL number and route | Outage stands out without risking the SPA warm-up retry | Plan |
| Log tests | Add Microsoft.Extensions.Diagnostics.Testing fake logger to IntegrationTests | Only the real pipeline can exercise the inline lambdas | Plan |
| Scope placement | Scope and summary middleware before `UseExceptionHandler`, user id filled after authentication | The exception middleware logs outside inner scopes and clears the endpoint | Research |
| Architecture constraints | Inline lambdas for middleware, filter and `OnRejected` | Named `InvokeAsync` classes fail the CancellationToken rule | Research |

## Scope

**In scope:** scope + summary middleware, console scopes, EF to Warning, SYSTEM-fallback warning, shared controller failure logging, Identity code and user-store logging, 503 handler split, login/refresh outcome logging, 429 and stubbed-route logging.

**Out of scope:** SPA changes, version stamping, extra health probe, error tracker or DB log table, any response/status/code change, IP, email, token or query-string logging.

## Architecture / Approach

Inline middleware in `Program.cs` opens a mutable scope (trace id now, user id after authentication) and writes the summary line in a `finally`, placed before the exception handler so the 500 log and final status are covered. Each later phase adds `ILogger` use in the layer that owns the failure (WebApi controllers, Data repository/store/handler, Host rate limiter, DependencyInjection auth group) with structured placeholders and a fixed level policy. Every phase is test-first through the repo's existing styles plus the new fake logger.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Log capture and baseline logging | Fake logger, scope + summary middleware, EF Warning, SYSTEM warning | 500-path scope and route template after the exception middleware clears the endpoint |
| 2. Controller failure logging | Shared helper; logged bare 401s, invalid-input 400s, 404 misses | Base-class shape vs architecture tests |
| 3. Identity and user-store logging | Original Identity codes and concurrency branches logged, no PII | Leaking email or display name into log state |
| 4. 503 handler | Warning/Error split with SQL number and route | Misclassifying a slow cold-start resume as an outage |
| 5. Auth events and 429 logging | Login/refresh outcomes, `OnRejected`, stub hits | Reading Identity's result inside an endpoint filter without changing it |

**Prerequisites:** none beyond the current branch; new test package needs the vulnerability audit.
**Estimated effort:** about 5 sessions across 5 phases.

## Open Risks & Assumptions

- Framework behaviour (exception middleware clearing the endpoint, Identity login returning a problem result with the outcome as detail) is reasoned from knowledge and must be confirmed by the first failing tests.
- Production still needs the platform to collect stdout; this change assumes it does (unconfirmed).
- `appsettings.Development.json` was not readable during planning; local runs may override levels there.

## Success Criteria (Summary)

- Every failure family in the audit's steps 1-5 leaves a structured log line at the intended level, asserted by tests through the real pipeline.
- No log line or state contains email, display name, password, token, query string or IP.
- All existing unit, integration and architecture tests pass, with only constructor-helper updates, and every response is unchanged.
