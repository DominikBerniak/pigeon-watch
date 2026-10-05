# Register and Log In (S-01) — Plan Brief

> Full plan: `context/changes/register-and-login/plan.md`
> Research: `context/changes/register-and-login/research.md`
> External research: `context/changes/register-and-login/external-research.md`

## What & Why

FR-001: users register (email, password, public display name), log in, and log out. Login stands in front of every must-have in the milestone, so it goes first after F-01. Cross-site hosting (SWA ↔ App Service, both on the Public Suffix List) rules out cookies, so the slice uses Identity bearer tokens.

## Starting Point

The API is a layered .NET 10 template with no auth and only the throwaway `/health/db` smoke probe. Strict architecture tests currently reject stock Identity tables. The Angular 22 SPA is a bare shell: empty routes, no interceptors, no SWA config.

## Desired End State

- Any protected page sends logged-out visitors to `/login?returnUrl=…`, with a link to registration.
- Registering logs the user straight in and returns them where they were going. Sessions survive reloads and new tabs. Logout ends the session on this device.
- Every UI text comes from one source, `UiLabels.resx` in the API. The SPA bundles snapshots for instant render and fetches `GET /resources/{culture}`. The culture is taken from `localStorage` (`pigeonwatch.culture`) and falls back to EN; S-01 ships EN only.
- The SPA is built on Angular Material, themed from one set of `--pw-*` CSS custom properties (design tokens), with a common stylesheet of `pw-*` layout and typography classes and reusable app components (page card, alert, field errors, submit button) that every form composes.
- When the free-tier API or database is waking up, users see a friendly full-screen warm-up panel and requests retry on their own ("Congratulations! You're the first rescuer today…") instead of seeing errors.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| --- | --- | --- | --- |
| Token transport | Identity bearer tokens via `MapIdentityApi`, no cookies | Cross-site cookies break in Safari, Brave and Incognito | Research (D1) |
| Refresh-token storage | `localStorage`; access token in memory | Rescuers on phones must stay logged in across reloads and tabs | Plan |
| Password policy | Composition rules, minimum 8 characters | Classic rules kept (D3); 8 is the common floor | Research (D3) + Plan |
| Email confirmation | None | MVP speed; existence disclosure and no recovery accepted | Research (D4) |
| Display name | Entered at registration: 3–30 characters, unique (case-insensitive), no `@` | Deriving it from the email (D2) would leak contact info that the PRD forbids exposing | Plan (revises D2) |
| Current-user source | `configuration/client` (anon) + `configuration/general` (auth), no `/me` | Stable split between public and per-user config | Research (D5) |
| Identity storage | Own `USER_ACCOUNT` table + custom `IUserStore`; no `IdentityDbContext` | The architecture tests pass unchanged; one allow-list line for endpoint extensions | Plan |
| Roles | Deferred to S-10; `roles: []` from claims | No role consumer in S-01 | Plan |
| Identity endpoints | Only `auth/login` and `auth/refresh`; everything else 404; cookie flags 400 | `/manage/2fa` would break login, the reset endpoints are unused | Plan |
| Logout | This device only, client discards tokens | Matches D1; bearer tokens can't be revoked anyway | Plan |
| Abuse protection | Identity lockout (5 → 5 min) + IP rate limiter (10/min) on login and register (refresh exempt) | Stops password spraying and sign-up spam that keeps waking the DB | Plan |
| `/health/db` | Removed with all smoke code; `SMOKE_CHECK` dropped; deploy probe → `configuration/client` | Closes the F-01 F3 exit; the probe no longer wakes the DB | Plan |
| Paused DB | API maps transient SQL failures to 503 + `Retry-After`; SPA warm-up panel | Cold start must not look broken | Plan |
| After sign-up | Auto-login, then `returnUrl` | One step for the user | Plan |
| UI labels | `UiLabels*.resx` in the API is the single source; `GET /resources/{culture}` | One place to edit text; S-12 only adds `UiLabels.pl.resx` + a switcher | Plan |
| Labels at cold start | Build-time JSON snapshots of every resx culture bundled in the SPA; culture from `localStorage`, fallback EN | The warm-up panel and login page render while the API sleeps | Plan |
| SPA label access | Small signal `ResourceService` + `t` pipe, `{0}` placeholders | No third-party dependency on Angular 22 | Plan |
| UI library | Angular Material 22.2 (MIT), installed without `ng add`, system fonts, no icon font | First-party and version-locked to Angular 22; PrimeNG 22 moved to a key-based PrimeUI license; `ng add` injects Google Fonts that the CSP blocks | Plan (user choice) |
| Styling | `--pw-*` tokens are the single source of values; `_material-theme.scss` maps them onto `--mat-sys-*`; `pw-*` common classes; a spec rejects literals | First real UI: later slices restyle by changing tokens, not components | Plan (user request) |
| Reuse | `shared/ui` components for patterns Material lacks; a pattern used by a second feature is promoted, not copied | Login and register are the first of many forms | Plan (user request) |
| Testing | Unit tests (xUnit + NSubstitute + SQLite) + Vitest; no integration project | Fast, no CI containers | Plan |

## Scope

**In scope:**
- Custom user store and the `USER_ACCOUNT` migration
- `account/register` and the filtered Identity login/refresh endpoints
- The two configuration endpoints, rate limiter and 503 handler
- Smoke-probe removal and the deploy probe swap
- Token store, auth and warm-up interceptors, guards and SWA config with CSP
- UI foundation: Angular Material, design tokens, token-driven Material theme, base and common styles, the style-token coverage spec, and the reusable app components
- Resource management: the `UiLabels.resx` English label set, the resources endpoint, the snapshot generator, the `t` pipe, saved-culture selection with EN fallback, and the label-coverage spec
- Login, register and home pages with logout, and the warm-up panel
- A new API unit-test project and its CI step

**Out of scope:**
- Email confirmation, password reset, 2FA and passkeys
- Logout from all devices
- Language switcher and Polish labels (roadmap S-12)
- Roles and the admin account (S-10)
- Display-name editing (S-02)
- Cookie / SWA Standard upgrade
- Breached-password check
- API integration tests
- EF retry-budget tuning
- Dark theme, icon font, CSS frameworks, Storybook

## Architecture / Approach

Requests pass through `UseExceptionHandler` (paused DB → 503), routing, CORS, authentication, the rate limiter, authorization, then controllers plus a filtered `MapIdentityApi` group under `auth/`. Identity runs on `UserManager<ApplicationUser>` with `PigeonWatchUserStore` (in `Data`), which persists an internal, audited `UserAccountEntity`. Registration follows the house path: `AccountController` → `AccountService` (display-name rules) → `AccountRepository` (`UserManager`). Labels live in `BusinessObjects/Resources/UiLabels.resx` and are served by `ResourcesController` → `UiResourceService` (`ResourceManager`). An npm prebuild step turns the same files into bundled JSON snapshots. In the SPA, a non-blocking initializer seeds labels from the snapshot, then fetches `/resources/{culture}` and runs the D5 restore sequence. `authInterceptor` (bearer header, one shared refresh, single retry) wraps `warmupInterceptor` (3 s / 503 → panel, retries up to 2 min), and guards await session readiness.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. User store and auth endpoints (API) | Register, login and refresh working against LocalDB; unit-test project | Custom store correctness (concurrency, unique indexes, lockout) |
| 2. Configuration, hardening and deploy (API) | D5 endpoints, `resources/{culture}` + English resx, 429 limiter, 503 mapping, smoke removal, live verification | Forwarded headers on Linux; DP keys across restarts; CORS on 503 |
| 3. SPA auth infrastructure | Resource snapshots + `t` pipe + culture selection, token store, interceptors, guards, non-blocking startup, SWA config + CSP, Angular Material + tokens + theme + common styles | Refresh race under concurrent 401s; CSP vs inlined critical CSS; Material vs the 500 kB initial budget |
| 4. SPA pages, warm-up panel and end-to-end | Reusable app components; login, register, home, logout, warm-up panel composed from them; live Safari/Incognito check | Real cold-start timing vs the 2-minute warm-up window |

**Prerequisites:**
- F-01 done (it is).
- App Service setting `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` added manually before the Phase 2 merge.

**Estimated effort:** ~4 sessions, one per phase. Each phase is a separate PR because deploys run on merge.

## Open Risks & Assumptions

- Linux App Service is assumed to set `WEBSITE_INSTANCE_ID`, so the Data Protection keys persist under `/home`. If they don't, every restart logs everyone out. The Phase 2 restart test catches it.
- The refresh token in `localStorage` is readable by any XSS for 14 days (accepted in D1). The CSP is the mitigation.
- Registration reveals whether an email or display name is taken (accepted with D4 and the uniqueness choice).
- The password policy deviates from NIST SP 800-63B-4 (accepted in D3).
- SQLite store tests don't reproduce SQL Server collation. The Phase 1 manual checks on LocalDB cover it.
- The SWA build (Oryx) is assumed to see `PigeonWatch/Api/...` from the full checkout, because the snapshot generator reads the resx there. The first Phase 3/4 deploy confirms it.
- If forwarded headers don't apply, the rate limiter partitions every user under one IP, which becomes a global 10/min limit. The Phase 2 per-client check catches it.

## Success Criteria (Summary)

- A new user can sign up on a phone, stay logged in across reloads, log out, and log back in, including in Safari and Incognito.
- Opening any protected page while logged out leads through login (or sign-up) and back to that page.
- The first visit of the day shows the friendly warm-up panel and then the app, never an error or a blank screen.
