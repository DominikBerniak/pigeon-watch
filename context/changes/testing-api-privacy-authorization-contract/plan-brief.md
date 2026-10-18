# API Privacy and Authorization Contract Tests — Plan Brief

> Full plan: `context/changes/testing-api-privacy-authorization-contract/plan.md`
> Research: `context/changes/testing-api-privacy-authorization-contract/research.md`

## What & Why

Rollout Phase 2 of the test plan covers three risks: another user's email in a response (#2), cross-user access or a stale session (#3), and email enumeration or brute force (#6). The plan builds a contract suite on the real HTTP pipeline that fails when any of these regress, and it ships one small fix for an enumeration oracle that research found. It matters because new surfaces (messaging, comments, photos) will land, and today nothing would notice a response that carries an email.

## Starting Point

The Phase 1 integration harness exists (`WebApplicationFactory<Program>` on SQLite, a distinct client address per client, three register tests) but has no tokens, no bearer client, no endpoint enumeration and no clock control. Privacy rests on response models that carry no email, checked by one repeat-register assertion and one reflection test. Ownership is enforced by claim-scoped queries, there is no server-side logout, and a taken display name answers differently for a new and an existing email.

## Desired End State

A suite in `PigeonWatch/Api/IntegrationTests` that enumerates every routed endpoint (including the 404-stubbed Identity routes) and fails on any leaked email, any endpoint without a fixture or exemption, and any endpoint that is neither `[Authorize]`-protected nor on an explicit anonymous list. It also pins the ownership rules, the password-change token lifecycle, the generic registration and login failures, lockout across addresses and the 11th-request 429. `test-plan.md` §6.4 tells the next author how to add a test for a new endpoint.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| --- | --- | --- | --- |
| Display-name oracle | Fix it: register returns the existing generic `RegistrationFailed` for a taken display name; rename keeps `DuplicateDisplayName` | The same answer for new and existing emails removes the zero-cost probe without reordering checks or adding a code | Plan |
| Logout clause of Risk #3 | Drop logout; prove password-change lifecycle (old refresh 401, old access 200 until expiry then 401) | No server logout exists (D1), so only the lifecycle that exists can be proven | Plan |
| Free-text contact rule | No rule; record self-disclosure in §7 | Keeps the phase test-only and avoids a false-positive-prone heuristic | Plan |
| Framework unknowns | Resolve with six throwaway probes in the first sub-phase, record in research.md | Clock control, stamp rotation and lockout timing were unverified | Research |
| Endpoint enumeration | `EndpointDataSource`, not `ApiExplorer`, with a coverage guard | `ApiExplorer` hides the stubbed Identity routes | Research |
| Leak search | Decoded JSON, headers and cookies, with a self-test and a positive control | A raw search misses `+` escaped as `+` and could pass vacuously | Research |
| Rate-limit budget | Fresh address per sweep request; at most 10 `auth` requests per address except in the limiter test | The shared 10-per-minute bucket would otherwise give 429s instead of leak checks | Research |
| Clock seam | Chosen by probe: DI `TimeProvider`, bearer options, or a short real delay (≤ 2 s); else drop the expiry step and note it in §7 | Bearer expiry may not use the DI provider | Plan |
| §2 wording backport | Rewrite the Risk #2, #3 and #6 wording in the final phase (wording only, no anchors) | Keeps the contract truthful once tests prove otherwise | Plan |

## Scope

**In scope:** harness helpers and probes; the leak sweep and coverage guard; ownership and endpoint-protection tests; token lifecycle after a password change; the register oracle fix (repository, SPA register mapping, unit tests, `PigeonWatch/CLAUDE.md`); enumeration, lockout and limiter tests; cookbook §6.3, §6.4, §6.7, §7, §8 and the §2 backport.

**Out of scope:** server-side logout or token revocation; sighting write-ownership tests (S-09 to S-11); a contact-detail rule on free text; timing and spoof-resistance assertions; removing the accepted `LockedOut` detail; SQL-Server-only paths; new CI YAML; an AI-native layer.

## Architecture / Approach

Everything runs through real register and login, so ownership uses real tokens and the claim path is never mocked. Phase 1 resolves the framework unknowns and adds a `TestUser` helper, per-request address sending, an audit-column reader and the clock seam. Phases 2 to 4 cover Risks #2 and #3, Phase 5 is the only production change and is test-first, Phase 6 covers Risk #6 on top of the fix, and Phase 7 writes the cookbook. Each test builds its own factory; expected values come from the documented contract, never from production constants.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Probes and harness seams | Six framework questions answered, `TestUser` helper, clock decision | Bearer expiry ignores the DI clock, so the expiry assertion falls back or is dropped |
| 2. Risk #2 leak sweep | Enumerated sweep as anonymous, B and A, scanner, coverage guard, audit check | Vacuous pass or 429 noise; mitigated by positive control and fresh addresses |
| 3. Risk #3 ownership and protection | Two-user ownership tests, anonymous-list guard | Documenting open reads as by-design without blessing a leak |
| 4. Risk #3 token lifecycle | Refresh 401, access 200 then 401, rename control | Clock seam may force a short real delay |
| 5. Close the display-name oracle | Red test, repository collapse, SPA and doc changes | Honest users lose the "name taken" hint on register; deploy gap shows a generic error |
| 6. Risk #6 enumeration and limiter | Identical failure answers, distributed lockout, 11th-request 429, password-guess bound | Request budget per address; lockout expiry may not be drivable |
| 7. Cookbook | §6.3, §6.4, §6.7, §7, §8 and the §2 wording backport | Wording drifting into file anchors |

**Prerequisites:** the Phase 1 harness (complete), `dotnet` and `npm` toolchains, and a working-tree `test-plan.md` edit that marks rollout Phase 1 `complete`.
**Estimated effort:** about seven short sessions across seven sub-phases; Phase 5 is the largest because it touches API, SPA and docs.

## Open Risks & Assumptions

- Six framework behaviours (clock control, stamp rotation, stub statuses, lockout timing, case variants) are assumed until the Phase 1 probes observe them; the decision rules for each failure are in the plan.
- The register success (200) versus failure (400) split is inherent without email confirmation, so the fix removes a non-mutating probe, not email existence.
- After the fix a taken display name on register shows only the generic message; this was accepted in planning.
- A user can still paste their own email into a sighting description; recorded in §7 as self-disclosure.

## Success Criteria (Summary)

- A new endpoint or response field that exposes an email, or an endpoint shipped without authorization, fails the suite without anyone writing a new assertion.
- B cannot read or change A's account data, and the password-change session lifecycle is pinned exactly as designed.
- Registration and login failures look the same for existing and unknown emails, lockout stops distributed guessing, and the 11th request from one address gets 429.
