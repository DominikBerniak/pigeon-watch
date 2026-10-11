# Frame Brief: S-03 Report a sighting and see it on the map — "the map itself"

> Framing step before /10x-plan. This document captures what is *actually*
> at issue, separated from what was initially assumed.

## Reported Observation

Roadmap slice S-03 (`context/foundation/roadmap.md:124-134`): a logged-in user reports a sighting (location, description, behaviour, optional name, initial status Spotted or Caught, criticality Monitor / Needs help soon / Urgent), sees it on the nearby map and on their own profile, and opens its detail page; submissions are throttled by the FR-019 cooldown. Nothing sighting-related exists yet, and `research.md` leaves nine choices open (map library/tiles, location capture, lat/lng storage, "nearby" in S-03, cooldown, enum wire format, detail-page location, home vs `/map`, visual-gate scope).

## Initial Framing (preserved)

- **User's stated cause or approach**: S-03 is one vertical slice carrying many firsts (domain table, map integration, throttle, list/detail views, full status/criticality types and transition graph); the research frames the map risk primarily as a library/tile choice (Leaflet 1.9.4 vs MapLibre 6, `research.md:124-133`, Open Question 1).
- **User's proposed direction**: plan the slice as written with `/10x-plan report-sighting-on-map`, using `research.md` as input.
- **Pre-dispatch narrowing**: leading concern is **the map itself**; "done" means **the roadmap outcome as written** (report, map with others' sightings, own list on profile, detail page, cooldown message); reports happen **equally often on a phone at the bird and later from memory**.

## Dimension Map

1. **Library & build integration** — Angular 22 esbuild vs CommonJS/worker, vendor CSS under the style guard and budgets, jsdom tests.  ← initial framing (research)
2. **Tile source & cost guardrail** — what "cost-conscious map-provider usage" constrains; CSP and key exposure.
3. **Map as a location input on a phone** — the report form needs a pin-picker that is a form control, phone-usable, with and without geolocation.
4. **Data behind the S-03 map** — which sightings the map shows, centred and bounded how, before S-05 defines "nearby".

## Hypothesis Investigation

| Hypothesis | Evidence | Verdict |
| --- | --- | --- |
| 1. Library/build integration is the risky unknown | Style guard scans only `src/app/**/*.scss` + two partials, bans only `.mat-`/`.mdc-`/`::ng-deep`/`!important` (`Frontend/src/styles/style-token-coverage.spec.ts:16,33-39`); no `allowedCommonJsDependencies` but CommonJS only warns; every route is lazy `loadComponent` (`app.routes.ts:5-34`); label coverage does not see library DOM (`label-coverage.spec.ts:24-28`). Research claims spot-checked accurate. One trap: `auth.guards.spec.ts:92-99` renders `HomePage` in jsdom, so a map on home would be constructed in that test. | NONE (known steps) |
| 2. Cost guardrail is the real constraint | Guardrail is vendor-neutral and only limits *where* maps render (`prd.md:45,121`, origin `shape-notes.md:36`); no provider, key or map budget in any foundation doc; hosting stance is "$0 on F1" (`infrastructure.md:43`). But the CSP is a recorded security decision protecting the `localStorage` refresh token (`archive/2026-10-04-register-and-login/plan.md:513`), every prior change kept to `'self'` (`plan.md:83,572,596`), and any tile host widens it (`Frontend/public/staticwebapp.config.json:10`). | WEAK on cost; STRONG on the CSP trade-off |
| 3. The input role is under-framed | No `FormValueControl`/`ControlValueAccessor` in the SPA; every `[formField]` binds `matInput` (`login-page.html:14,25`, `register-page.html:11,18,33`); `FieldErrors` and `SubmitErrorStateMatcher` are generic (`shared/ui/field-errors/field-errors.ts:15-23`, `form-field/form-submit-state.ts:25-29`). No doc states how location is captured; research treats it as a library/geolocation question (`research.md:148-155,223`) and never as a form control. | PARTIAL |
| 4. What the S-03 map shows is undefined | S-03 says "nearby map" (`roadmap.md:47,126`) but its Unknowns omit it (`:132`); S-05 is where the map becomes "scoped to their nearby area by default" (`:150`) with "nearby" open (`:157`), and S-06 needs the same rule (`:158`). PRD makes the map always nearby-scoped and an unscoped feed a Non-Goal (`prd.md:131,166`; `roadmap.md:290`). FR-004 bundles map + scope + filters (`prd.md:72`), split across S-03/S-05 without saying what S-03's half is. Research leaves it open (`research.md:225`). Every map load can wake the paused DB (`research.md:192`, `deploy-plan.md:147`). | STRONG |

## Narrowing Signals

- On a first release with ~20 sightings the user expects the map to show sightings **around me**. That makes scoping part of seeing the S-03 map at all: an unscoped "show everything" map is ruled out, and S-05's open unknown ("radius vs named city", `roadmap.md:157`) is effectively answered now as "around the user's position".
- People reporting from memory **recognise the spot on a map**. This confirms the map as the location input (no address/geocoding path), so the pin-picker is a real second map role, not an optional fallback.
- On widening the `'self'`-only CSP for a tile host the user **hadn't considered it**. The map silently reopens a recorded security decision that no S-03 artifact names as a decision.
- Inverse check: "around me" needs the user's position at browse time, not only at report time. There is no geolocation or `Permissions-Policy` precedent in `PigeonWatch/Frontend` (grep), and geolocation may be denied or approximate (`research.md:148-155`), so "around me" also needs a defined behaviour when position is unknown.

## Cross-System Convention

Prior slices name CSP changes explicitly as security trade-offs with a live "no CSP violations" check (`archive/2026-10-04-register-and-login/plan.md:518,596,775,928`) and keep heavy code lazy rather than raising budgets (`plan.md:597`). The roadmap convention is "settle a shared definition once" (`roadmap.md:158`). The reframe follows both: name the CSP trade-off, and settle "nearby" where it first becomes user-visible.

## Reframed Problem Statement

> **The actual problem to plan around is**: S-03's map rests on three undecided contracts — what "around me" means when the map opens (including when position is unknown), how a map pin becomes a required, phone-usable form value, and which external host the `'self'`-only CSP will admit — not on which map library to use.

The library choice is a known, low-risk set of build steps once those contracts are fixed; the contracts decide the CSP additions, the query shape, the DB wake-up cost and the form design. The "nearby" definition moves forward from S-05 into S-03 in its first form (around the user's position), so S-05 becomes the filters plus any refinement, and S-06 inherits the same rule. The original framing of one slice with the roadmap outcome as written still holds; only the centre of risk moves.

## Confidence

**HIGH** — strong file-level evidence for dimension 4 and the CSP part of dimension 2, two decisive user signals ("around me", "recognise the spot on a map"), the "hadn't considered it" answer confirming the CSP decision was unrecognised, and the library hypothesis found no evidence of a blocker.

## What Changes for /10x-plan

The plan should open with explicit decisions on (a) the S-03 "around me" scope and its fallback when location is unavailable, recorded so S-05/S-06 reuse it (and the roadmap S-03/S-05 boundary updated), (b) the location pin-picker as the repo's first custom Signal Forms control with required-field errors at 375 px, and (c) the CSP widening as a named security trade-off with a live CSP check, before choosing a library. Also carry the `auth.guards.spec.ts:92-99` trap into the home-vs-`/map` decision.

## References

- Source files: `context/foundation/roadmap.md:47,124-134,150-158,290`; `context/foundation/prd.md:45,72,121,131,166`; `PigeonWatch/Frontend/public/staticwebapp.config.json:10`; `PigeonWatch/Frontend/src/styles/style-token-coverage.spec.ts:16,33-39`; `PigeonWatch/Frontend/src/app/shared/ui/field-errors/field-errors.ts:15-23`; `PigeonWatch/Frontend/src/app/shared/ui/form-field/form-submit-state.ts:25-29`; `PigeonWatch/Frontend/src/app/core/auth/auth.guards.spec.ts:92-99`; `context/archive/2026-10-04-register-and-login/plan.md:83,513,596`
- Related research: `context/changes/report-sighting-on-map/research.md`
- Investigation tasks: four parallel sub-agents (H1 library/build, H2 tiles/cost/CSP, H3 map as phone input, H4 S-03 map data scope)
