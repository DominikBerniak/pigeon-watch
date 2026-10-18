# Test Plan

> Phased test rollout for this project. Strategy is frozen at the top
> (§1–§5); cookbook patterns at the bottom (§6) fill in as phases ship.
> Read before writing any new test.
>
> Refresh: re-run `/10x-test-plan --refresh` when stale (see §8).
>
> Last updated: 2026-10-18

## 1. Strategy

Tests follow three non-negotiable principles for this project:

1. **Cost × signal.** The cheapest test that gives a real signal for the
   risk wins. Do not promote to e2e because e2e "feels safer." Do not put a
   vision model on top of a deterministic visual diff that already catches
   the regression.
2. **User concerns are first-class evidence.** Risks anchored in "the
   team is worried about X, and the failure would surface somewhere in
   <area>" carry the same weight as PRD lines or hot-spot data.
3. **Risks are scenarios, not code locations.** This plan documents *what
   could fail* and *why we believe it's likely* — drawn from documents,
   interview, and codebase *signal* (churn, structure, test base). It does
   NOT claim to know which line owns the failure. That knowledge is
   produced by `/10x-research` during each rollout phase. If the plan and
   research disagree about where the failure lives, research is the
   ground truth.

Hot-spot scope used for likelihood weighting: `PigeonWatch/Api` (all layered
projects) and `PigeonWatch/Frontend/src`, excluding docs, archive, migrations,
lockfiles, build output and `node_modules`. Window: last 30 days, 30 commits.

## 2. Risk Map

The top failure scenarios this project must protect against, ordered by
risk = impact × likelihood. Risks are failure scenarios in user / business
terms, not test names. The Source column cites the *evidence that surfaced
this risk* — never a specific file as "where the failure lives" (that is
research's job, see §1 principle #3).

| # | Risk (failure scenario) | Impact | Likelihood | Source (evidence — not anchor) |
|---|-------------------------|--------|------------|--------------------------------|
| 1 | A new user registers while the Azure free-tier backend is cold-starting and ends up with a failure message, a duplicate or half-created account, or gives up | High | High | interview Q1; tech-stack.md (App Service F1 sleeps on idle); PRD FR-001 (login wall in front of every feature); archive `register-and-login` (cold-start retry decision) |
| 2 | Another user's raw email or contact detail appears in some response as new surfaces land (messaging, comments, photos) | High | Medium | PRD Guardrails + NFR (contact info never exposed); interview Q4; roadmap S-04, S-07, S-08 (new surfaces); `PigeonWatch/CLAUDE.md` hard rule |
| 3 | A logged-in user reads or changes another user's records, or a logged-out or stale session keeps access (e.g. after a password change) | High | Medium | PRD Access Control; roadmap S-09, S-10, S-11 (ownership, admin); archive `edit-profile-name` (session rotation on password change); hot-spot dir `PigeonWatch/Api/WebApi` (about 20 commits/30d) |
| 4 | A reported sighting does not appear on the nearby map, appears in the wrong place, or the wrong set shows (resolved ones, outside the radius) | High | Medium | PRD US-01 and first primary success criterion; archive `report-sighting-on-map`, `nearby-map-review-fixes`; hot-spot dir `PigeonWatch/Frontend/src/app/features` (87 file touches/30d) |
| 5 | A change to a shared UI component silently breaks behaviour or accessibility on several screens | Medium | High | interview Q3; hot-spot dir `PigeonWatch/Frontend/src/app/shared` (94 file touches/30d); archive UI-contract slices; open change `app-wide-large-screen-layout` |
| 6 | An attacker probes registration or login to learn which emails are registered, or brute-forces accounts | Medium | Medium | `PigeonWatch/CLAUDE.md` hard rule (no email confirmation, generic registration failure); archive `register-and-login`; abuse lens |

Out of the map by design: "deploy goes green while the database is broken"
is high impact, low likelihood and not a test; it is handled as a gate in §5
(post-deploy smoke), not as a risk. "Photo upload accepts hostile files" is
not mapped because the slice does not exist yet; any new response it adds is
covered by the Risk #2 leak check.

### Risk Response Guidance

| Risk | What would prove protection | Must challenge | Context `/10x-research` must ground | Likely cheapest layer | Anti-pattern to avoid |
|------|-----------------------------|----------------|--------------------------------------|-----------------------|-----------------------|
| #1 | A first registration that is slow, fails at the network level, or is retried ends in exactly one usable account and an honest message, never a false "failed" for an account that was created | "Retry logic exists, so cold start is covered"; "retrying a POST is safe" | Warm-up and retry behaviour on the client; what the server does on a repeated register; how a failed-then-succeeded request is reported to the user | unit (fake HTTP: network failure, delay, then success) + API integration (repeated register) | Mocking the retry itself; asserting only the happy path |
| #2 | No response from any user-reachable endpoint carries another user's email, and the check extends to every new endpoint | "Display name rejects `@`, so it is safe"; "a few spot checks cover it" | Every response contract that names a user; persisted audit fields; error bodies | API integration (contract-wide leak check seeded with a known test email) | Asserting only the fields the code currently returns |
| #3 | User B cannot read or modify user A's records; after logout or a password change, old tokens stop working once the access token expires | "Authenticated means authorized"; "a 401 without a token covers ownership" | Ownership checks per endpoint; token and security-stamp lifecycle | API integration (two users, real HTTP pipeline) | Mocking the current-user provider so ownership can never fail |
| #4 | A reported sighting shows up where the reporter put it, within the 10 km rule, and open and resolved sightings are separated per the PRD | "Unit-tested distance maths equals map correctness"; "the map stub equals the real map" | Report to persistence to nearby query to map render path; status and radius rules; what the map stub hides | integration for the rules, one e2e for the full flow | Expected values copied from production code; e2e where integration is enough |
| #5 | Shared components keep their behaviour (focus, states, keyboard, accessibility) when edited, checked against the screens that consume them | "Component specs pass, so consumers are fine"; "a snapshot diff means something" | Which screens consume each shared component; existing spec coverage and gaps | component-level specs + accessibility check; selective multimodal review on 1–3 screens | Pixel or markup snapshots (excluded, §7) |
| #6 | Registration and login responses are indistinguishable for existing and new emails, and the rate limit and lockout actually fire | "The generic error message means no leak"; "the rate limit is configured, so it works" | Error shapes and timing; per-IP limiter and per-account lockout behaviour | API integration | Asserting configuration values instead of observed behaviour |

## 3. Phased Rollout

Each row is a discrete rollout phase that will open its own change folder
via `/10x-new`. Status moves left-to-right through the values below; the
orchestrator updates Status as artifacts appear on disk.

| # | Phase name | Goal (one line) | Risks covered | Test types | Status | Change folder |
|---|------------|-----------------|---------------|------------|--------|---------------|
| 1 | Cold-start registration resilience | Prove one usable account and an honest message when registration is slow or retried; bootstrap the API integration harness | #1 | unit (fake HTTP) + integration | change opened | testing-cold-start-registration-resilience |
| 2 | API privacy and authorization contract | Prove no email leak, ownership enforced, sessions invalidated, no email enumeration | #2, #3, #6 | integration (contract) | not started | — |
| 3 | Report-to-map flow | Prove a reported sighting lands on the nearby map under the PRD rules, plus one critical-path e2e | #4 | integration + 1 e2e | not started | — |
| 4 | Shared UI behaviour guard | Prove shared components keep behaviour and accessibility; add a selective multimodal review on 1–3 screens | #5 | component spec, accessibility, multimodal review (selective) | not started | — |
| 5 | Quality-gates wiring | Wire the new suites into PR checks and make the post-deploy smoke touch the database | cross-cutting | gates | not started | — |

Order rationale: Phase 1 attacks the top risk and builds the API integration
harness that Phases 2 and 3 reuse. Phase 4 has the highest likelihood but the
lowest impact, so it follows the integrity risks. Phase 5 locks the floor once
the suites exist.

**Status vocabulary** (fixed — parser literals): `not started`,
`change opened`, `researched`, `planned`, `implementing`, `complete`.

## 4. Stack

The classic test base for this project. AI-native tools carry a `checked:`
date so future readers can see which lines need re-verification.

Test-base profile: **meaningful** — 43 Angular spec files and 21 xUnit files
(unit and architecture tests), all run in PR checks. No API integration
harness, e2e, lint step or accessibility tooling yet.

| Layer | Tool | Version | Notes |
|-------|------|---------|-------|
| frontend unit + component | Vitest via `ng test` | ^5.0.3 (Angular 22.2) | Wired in PR checks; runs with `--watch=false` in CI |
| backend unit | xUnit + NSubstitute, SQLite in-memory for the user store | .NET 10 | Wired in PR checks |
| backend architecture | xUnit architecture tests | .NET 10 | Enforces layer and coding rules; do not extend for behaviour |
| API integration (real HTTP pipeline) | none yet — see Phase 1 | — | Phase 1 chooses and bootstraps the harness |
| API mocking (frontend) | none yet — fake HTTP at the Angular HTTP boundary | — | Decided in Phase 1 research |
| e2e | none yet — see Phase 3 | — | One critical-path flow only |
| accessibility | none yet — see Phase 4 | — | Chosen in Phase 4 research |
| (optional) AI-native | in-app browser tool for multimodal review of 1–3 screens — checked: 2026-10-18 | n/a | When NOT to use: any screen a deterministic assertion already covers; layout facts that can be measured; every-page review |

**Stack grounding tools (current session):**
- Docs: none (Context7 or framework docs MCP not available in current session) — local manifests and configs used instead; checked: 2026-10-18
- Search: generic web search only, no Exa.ai-style MCP — not used in this pass; checked: 2026-10-18
- Runtime/browser: in-app browser tool available — possible e2e or verification layer (Phase 3, Phase 4); not used yet; checked: 2026-10-18
- Provider/platform: Azure DevOps MCP exposed but CI is GitHub Actions — not relevant to quality gates; not used; checked: 2026-10-18

## 5. Quality Gates

The full set of gates that must pass before a change reaches production.
"Required after §3 Phase <N>" means the gate is enforced once that rollout
phase lands; before that, the gate is `planned`.

| Gate | Where | Required? | Catches |
|------|-------|-----------|---------|
| build + typecheck (both stacks, .NET code style enforced in build) | local + CI | required (wired) | syntactic, type and style drift |
| frontend lint | none | not part of this rollout | — |
| unit + architecture tests (both stacks) | local + CI | required (wired) | logic and layer-rule regressions |
| migrations match the model | CI | required (wired) | schema drift |
| API integration tests | local + CI on PR | required after §3 Phase 1 | cold-start registration, privacy, ownership, enumeration, nearby-rule regressions |
| shared-component behaviour and accessibility specs | local + CI on PR | required after §3 Phase 4 | silent UI regressions across screens |
| e2e on the critical flow | CI on PR | required after §3 Phase 3 | broken register, login, report, map path |
| multimodal visual review (1–3 screens) | local or CI on PR | optional, after §3 Phase 4 | visual issues deterministic specs miss |
| post-deploy smoke that touches the database | between merge and live | required after §3 Phase 5 | deploys that go green with a broken database path |

## 6. Cookbook Patterns

How to add new tests in this project. Each sub-section is filled in once
the relevant rollout phase ships; before that, the sub-section reads
"TBD — see §3 Phase <N>."

### 6.1 Adding a frontend unit or component test

- TBD — see §3 Phase 4 for the shared-component behaviour and accessibility pattern. Existing specs live next to the unit as `*.spec.ts`; run with `ng test` from `PigeonWatch/Frontend`.

### 6.2 Adding a frontend test for slow or failing HTTP (cold start)

- TBD — see §3 Phase 1 for the cold-start registration retry pattern.

### 6.3 Adding an API integration test

- **Where**: `PigeonWatch/Api/IntegrationTests/` (assembly `PigeonWatch.IntegrationTests`, xunit.v3, in `PigeonWatchApi.slnx`, run by the `Integration tests` step of `api-pr-checks.yml`). It is not part of `PigeonWatchAssemblies`, so the architecture rules do not apply to it; the shared style enforcement does.
- **Factory**: `PigeonWatchApiFactory : WebApplicationFactory<Program>` removes `DbContextOptions<PigeonWatchDbContext>` and `IDbContextOptionsConfiguration<PigeonWatchDbContext>`, then registers the context on a held-open `SqliteConnection("DataSource=:memory:")` with the audit interceptors and runs `EnsureCreated`. Each factory instance owns a clean database and is disposed with the test; create one per test (a test class that builds it in its constructor gets one per test method), never a static shared one.
- **Limiter isolation**: the real `Auth` limiter (10 per minute per remote IP) stays in place. `ClientAddressStartupFilter` sets the remote IP from the `X-Test-Client-Address` header; `factory.CreateApiClient()` gives every client a distinct address, so tests never share a partition. A test that wants a 429 reuses one client or passes the same address twice.
- **Read back state**: `factory.CountUserAccounts()` runs a raw `SELECT COUNT(*) FROM USER_ACCOUNT` on the held-open connection (entities are internal to `Data`). `AccountApiClient` wraps register, login and error-code reading.
- **Reference test**: `RegisterContractTests.Register_repeat_returns_only_registration_failed_and_keeps_one_account`. Expected values are the documented contract (error code name, non-enumeration), not production constants.
- **Run**: `dotnet test PigeonWatch/Api/IntegrationTests/PigeonWatch.IntegrationTests.csproj`.
- **Spike outcomes**: (1) without the header every `TestServer` request lands in one limiter partition (the 11th request in a minute gets 429), confirmed; with distinct addresses it does not. (2) Removing only `DbContextOptions<PigeonWatchDbContext>` is not enough: EF throws the "two database providers" error until `IDbContextOptionsConfiguration<PigeonWatchDbContext>` is removed too. (3) The default factory environment is Development, so there is no HTTPS redirect (none observed), and the ephemeral Data Protection keys are enough for register then login inside one factory. A repeat register returns 400 with `RegistrationFailed` alone and no email or display name in the body.
- **SQLite fidelity gaps**: SQL-Server-only branches are not reachable here: the duplicate-insert translation (`SqlException` 2601/2627), the `SqlException` mapping to 503, and the execution-strategy commit ambiguity of `EnableRetryOnFailure`. `RetryLimitExceededException` is the only 503 trigger a test could fake.
- **Dependency note**: the project pins `System.Security.Cryptography.Pkcs` 10.0.13 directly because the version pulled in transitively through `Data` (9.0.11, via the SQL Server client) carries known advisories and the audit command would otherwise flag this project.

### 6.4 Adding a test for a new API endpoint

- TBD — see §3 Phase 2 for the privacy (no email in any response) and ownership (two-user) pattern every new endpoint must follow.

### 6.5 Adding a test for a sighting or map rule

- TBD — see §3 Phase 3 for the report-to-nearby-map rule pattern (oracle from the PRD, not from production code).

### 6.6 Adding an e2e test

- TBD — see §3 Phase 3 (one critical-path flow only).

### 6.7 Per-rollout-phase notes

(Optional. After each phase lands, `/10x-implement` appends a 2–3 line note
here capturing anything surprising the phase taught.)

## 7. What We Deliberately Don't Test

Exclusions agreed during the rollout (Phase 2 interview, Q5). Future
contributors should respect these unless the underlying assumption changes.

- **Pixel-level snapshot tests** — they break on every styling change and catch nothing meaningful. Re-evaluate if the design system freezes and a stable visual baseline exists. (Source: Phase 2 interview Q5.)
- **The dev-only kitchen-sink page** — it ships no user-facing behaviour. Re-evaluate if it becomes reachable by end users. (Source: Phase 2 interview Q5.)

## 8. Freshness Ledger

- Strategy (§1–§5) last reviewed: 2026-10-18
- Stack versions last verified: 2026-10-18
- AI-native tool references last verified: 2026-10-18

Refresh (`/10x-test-plan --refresh`) when:

- a new top-3 risk surfaces from the roadmap or archive,
- a recommended tool's `checked:` date is older than three months,
- the project's tech stack changes (new framework, new test runner),
- §7 negative-space no longer matches what the team believes.
