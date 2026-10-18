# Cold-Start Registration Resilience — Plan Brief

> Full plan: `context/changes/testing-cold-start-registration-resilience/plan.md`
> Research: `context/changes/testing-cold-start-registration-resilience/research.md`

## What & Why

Rollout Phase 1 of the test plan (Risk #1): a new user who registers while the free-tier backend is waking up must end with exactly one usable account and an honest message. Research showed the real defect is a false "failed": the retry resends the register POST blindly, so a created account whose response was lost comes back as the generic "We couldn't create an account" 400. The plan pins that behaviour with tests, fixes it without breaking the non-enumeration rule, and builds the API integration harness that rollout Phases 2 and 3 reuse.

## Starting Point

The warm-up interceptor resends any request after status `0` or `503` for up to 120 s, with no idempotency. No test runs a register POST through it, the backend has no integration test project, and the PR workflow runs only the architecture and unit tests plus the frontend suite.

## Desired End State

A registration that was created but not acknowledged ends with the user signed in, because the SPA logs in with the credentials they just typed. A genuine duplicate email still gets the generic message with no extra login request. If the warm-up ends with no answer at all, the register form says it couldn't confirm whether the account was created and invites a resubmit or a login. The API integration suite runs in every PR, and the test plan's cookbook explains how to add both kinds of test.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| --- | --- | --- | --- |
| Fix or only pin | Fix: scoped recovery login | Keeps the hard non-enumeration rule and the cold-start wait; a login with the typed credentials proves nothing new | Plan (research option) |
| Recovery trigger | Only after an inconclusive attempt: the register was resent, or the email matches an earlier unanswered register (email only, in memory) | Ordinary duplicates cost no extra login that would count toward lockout and the limiter | Plan |
| Message for "no answer" | New register-only text via a resx key | The old "still waking up" text is false when the real state is unknown | Plan |
| Harness database | SQLite in-memory, gaps recorded in test-plan §7 | Matches the existing unit tests, needs no Docker, covers the sequential repeat register | Plan |
| Rate limiter in tests | Real limiter kept; per-client test IP via a header | Phase 2's rate-limit assertions stay realistic and tests do not share one partition | Plan |
| CI step | One `dotnet test` line in Phase 1; reword §3 Phase 5 | Resolves the §3 vs §5 contradiction in favour of the gate "required after Phase 1" | Research + Plan |
| Server 503 test | None | The handler unit test plus the frontend script cover it; SQL-only branches are accepted gaps | Plan |
| Test order | Backend contract first, frontend spec before the fix | Frontend scripts assert an observed server fact; recovery scenarios must fail before the fix | Plan |

## Scope

**In scope:** `IntegrationTests` project, SQLite factory and register contract tests, CI step; resend token, recovery login and unanswered-email marker; real-chain frontend spec; register-only unconfirmed message; test-plan §6.2, §6.3, §7 and §3/§5 wording; related CLAUDE.md notes.

**Out of scope:** idempotency key or any API contract change; e2e and real Azure cold start; server-side 503 and SQL Server container tests; `429` on resend and two-tab races; privacy, ownership and enumeration tests (rollout Phase 2); other cookbook entries.

## Architecture / Approach

Phase 1 runs the real host in-process (`WebApplicationFactory<Program>`) on SQLite, with the real `Auth` limiter and a per-client IP so tests stay independent, and records what a repeat register returns. Phase 2 writes a spec that composes the real interceptors, `AuthApi` and `SessionService.register` over a fake network, then adds a request-context flag set by the interceptor on its first resend, an in-memory marker for an unanswered email, and the recovery login in `SessionService.register`. Phase 3 maps register transport failures to a new form error and resx key, and syncs the docs.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. API integration harness and server contract | New test project, factory, register contract tests, CI step, §6.3 | Two inferred facts (limiter partition, SQLite swap) need a spike; Mvc.Testing needs network on first restore |
| 2. Scoped recovery (frontend) | Real-chain spec, resend token, marker, recovery login | Largest phase; must not change the retry policy or leak anything new |
| 3. Unconfirmed message and close-out | resx key, register-only form error, specs, test-plan and CLAUDE.md updates | Editing frozen test-plan sections §3/§5 for the contradiction |

**Prerequisites:** network for the first NuGet restore; local LocalDB API for the manual checks.
**Estimated effort:** about 3 sessions, one per phase.

## Open Risks & Assumptions

- Assumes a repeat register returns `RegistrationFailed` alone; Phase 1 observes this, and the Phase 2 trigger must be adjusted if the real response differs.
- The app's own 503 may follow a committed insert on SQL Server; not testable on SQLite and recorded as an accepted gap.
- The recovery login adds one request inside the 10-per-minute per-IP window; acceptable because it follows only an inconclusive attempt.
- The `0` status still cannot distinguish "never reached the app" from "response lost"; the plan recovers from the outcome rather than preventing the ambiguity.

## Success Criteria (Summary)

- A created-but-unacknowledged registration ends with the user signed in; a genuine duplicate still shows the generic message with no extra login call.
- A no-answer ending tells the user the account state is unconfirmed instead of "still waking up".
- Integration tests for the register contract run green in PR checks, and test-plan §6.2 and §6.3 let someone add the next test without rediscovery.
