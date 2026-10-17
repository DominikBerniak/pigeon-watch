<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Report a sighting and see it on the map

- **Plan**: context/changes/report-sighting-on-map/plan.md
- **Scope**: Full plan
- **Reviewed phases**: 1, 2, 3, 4, 5
- **Date**: 2026-10-11
- **Verdict**: NEEDS ATTENTION
- **Findings**: 0 critical, 6 warnings, 4 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | WARNING |
| Scope Discipline | WARNING |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | WARNING |
| Success Criteria | WARNING |

## Automated verification (re-run during review)

- `dotnet test PigeonWatch/Api/PigeonWatchApi.slnx`: PASS. Unit tests 190/190, architecture tests 42/42. A running `PigeonWatch.WebApi.Host` (PID 23440) locks `WebApi.Host/bin`, so the run used `--artifacts-path PigeonWatch/Api/UnitTests/bin/review-artifacts`. That folder is gitignored. An artifacts path outside the repo breaks `InterfaceLocationTests` and `UiResourceTests`, because both search upward for repo files.
- `dotnet ef migrations has-pending-model-changes`: PASS ("No changes have been made to the model since the last migration"). It ran with `--no-build` against the existing Host build from 20:00, because the Host output is locked.
- `dotnet list ... package --vulnerable --include-transitive`: PASS. No vulnerable packages.
- `npm run build`: PASS with no budget warning. The initial total is 496.46 kB. Leaflet is only in the lazy chunk `leaflet-src`, which is 149 kB.
- `npm test -- --watch=false`: PASS. 38 files, 382 tests.
- `npm audit --omit=dev`: PASS. 0 vulnerabilities.

## Findings

### F1 — Silent Leaflet load failure lets "Place pin at map centre" submit a made-up location

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: PigeonWatch/Frontend/src/app/shared/map/map-view/map-view.ts:66-71, PigeonWatch/Frontend/src/app/shared/map/location-picker/location-picker.ts:41,82-84
- **Detail**: `load()` swallows a failed `import('leaflet')` with `catchError(() => EMPTY)`. That happens with a stale chunk hash after a redeploy, or when the network drops. The map region then stays blank, with no message and no retry. In the picker, `mapCentre` starts at `neutralMapView.centre` (central Poland) and only moves on `centerChange`. So "Place pin at map centre" still sets a value, and the user can submit a sighting at a point they never saw on a map.
- **Fix**: Give `app-map-view` a load-failed state (an output or signal). It renders an `app-alert` with a retry and uses a new resx key. The picker disables "Place pin at map centre" until the map reports ready.
  - Strength: Fixes the problem in the shared adapter, so all three pages get it, and closes the made-up-location path in the picker.
  - Tradeoff: One new label key, one new output, spec updates for map-view, the picker and `MapViewStub`.
  - Confidence: HIGH — read from the code; `mapCentre` only changes in `trackCentre`.
  - Blind spot: Whether the stub needs a matching `ready` output, which every page spec would then have to emit.
- **Decision**: FIXED. New `LEAFLET_LOADER` token, a `loadFailed` alert with retry and a `mapReady` output in `map-view`. The picker's centre button stays disabled until `mapReady`. New key `map.loadFailed`. Stub and specs updated; 385/385 tests pass and the build is within budget (496.54 kB). The browser check was not run because the pane was refused for localhost:4200.

### F2 — The geolocation Observable can hang forever while the permission prompt is open

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: PigeonWatch/Frontend/src/app/core/geolocation/geolocation.service.ts:20-48
- **Detail**: The service relies on the `PositionOptions.timeout` of 10 s. Browsers start that timer only after permission is granted. If the prompt is never answered, or is dismissed without calling either callback, nothing is emitted. The map home then shows "Locating…" indefinitely, and the picker's "Use my location" stays disabled. This breaks the plan's contract: "Always sets a timeout (10 s) … never errors".
- **Fix**: Pipe the inner Observable through an RxJS backstop, `timeout({ first: geolocationTimeoutMs + 5000, with: () => of({ status: 'timeout' }) })`, and add a spec that uses fake timers.
- **Decision**: FIXED. New `geolocationBackstopMs` (15 s) and an RxJS `timeout` that emits `{ status: 'timeout' }`, plus a spec with fake timers; 386/386 tests pass.

### F3 — The nearby bounding-box query loads every open sighting in the box before the cap

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: PigeonWatch/Api/Data/Repositories/SightingRepository.cs:35-56, PigeonWatch/Api/BusinessLogic/Services/SightingService.cs (nearby)
- **Detail**: `ListInBoxAsync` runs `Include(Reporter)`, the box filter, the status `Contains` and `OrderByDescending`, then `ToListAsync()` with no `Take`. The cap of 200 is applied only in memory, after the haversine filter. So each `GET sightings/nearby` reads and maps every open sighting in the roughly 20×20 km box. The endpoint has no rate limit, and the target is an F1 App Service with a CPU quota. The plan's wording permits this ("bounding-box SQL read … capped at 200"), so it is partly a plan gap. It is harmless at today's volumes.
- **Fix A ⭐ Recommended**: Add `int take` to `ListInBoxAsync` and pass `SightingRules.NearbyMaxResults * 2` from the service. The take goes after the existing `OrderByDescending(CreateDate)`, and the service keeps its exact filter and final `Take(200)`.
  - Strength: One parameter and one `Take`. It bounds the worst case. The headroom covers the box corners, which make up about 21% of the box area.
  - Tradeoff: In a box with more than 400 open sightings, some sightings inside the circle can be dropped in favour of newer ones in the corners. That is acceptable for "newest 200".
  - Confidence: HIGH — standard EF `Take`; the repository tests only need an extra argument.
  - Blind spot: No volume data; the `* 2` factor is a guess.
- **Fix B**: Leave the code as it is and record "nearby box query is uncapped in SQL" as an accepted limit in the plan's Performance Considerations.
  - Strength: No code change, and it matches the "small volume" assumption already in the plan.
  - Tradeoff: The risk stays latent until volumes grow, and someone has to remember it.
  - Confidence: MED — depends on how soon real usage arrives.
  - Blind spot: Load from scripted or abusive clients.
- **Decision**: FIXED via Fix A. `ListInBoxAsync` takes an `int take` applied in SQL after the order by `CreateDate`, and the service passes the new `SightingRules.NearbyBoxMaxRows` (`NearbyMaxResults * 2`). Repository test for the take and an updated service test added; unit tests 191/191, architecture tests 42/42.

### F4 — A whitespace-only description or behaviour gets the generic error with no field marked

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: PigeonWatch/Frontend/src/app/features/sightings/report-sighting-page/report-sighting-page.ts:91-94, PigeonWatch/Frontend/src/app/features/sightings/sighting-errors.ts:13-23, PigeonWatch/Api/WebApi/Models/CreateSightingRequestModel.cs:11-16
- **Detail**: On the client, Signal Forms `required` accepts `"   "`. On the server, `[Required]` rejects it, and the 400 response is keyed by property name (`Description` or `Behaviour`), not by a `SightingErrorCodes` value. `sightingFailure` recognises only `cooldownactive`, so the user sees the generic message. `CreateSightingRequestModel` is also the only request model with DataAnnotations. The plan said "the same attribute approach as `UpdateProfileRequestModel`", but that model has none. As a result, sighting failures arrive in two error shapes.
- **Fix**: Add a trim-aware validator for `description` and `behaviour` in `reportForm`, so blank text shows the existing required error on submit, and add a spec case.
- **Decision**: FIXED. `validate(..., isBlank ? requiredError() : undefined)` on description and behaviour, plus a spec case; 387/387 tests pass. The two server error shapes were not unified.

### F5 — Out-of-plan PRD v2 and roadmap edits were committed in p5, and one roadmap row is stale

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Scope Discipline
- **Location**: context/foundation/prd.md, context/foundation/roadmap.md:47,257
- **Detail**: The p5 commit (fa8b75e) also commits PRD v2.
  - **Related to S-03:** the status workflow table, the criticality levels and the resolved-status wording. The plan cites these from `prd.md:133-156`, but they did not exist in git before p5.
  - **Unrelated to S-03:** FR-021 (EN/PL language switch), FR-022 (password change), the flock layer deferred to M-02, and Open Questions 2-3 resolved.
  - **Roadmap:** p5 also changes S-03's status from `blocked` to `in-progress` and its readiness to `yes`, although the plan says "Status fields are untouched here". The readiness row's next step (line 257) still says "Run `/10x-new report-sighting-on-map`, then `/10x-plan`", which is stale.
  - These look like edits that were already in the working tree when this change started (research.md:18) and were swept into p5. The content matches the shipped code.
- **Fix**: Update roadmap.md:257 so its next step reads `/10x-archive report-sighting-on-map`, and add a one-line note to the plan that PRD v2 and the roadmap alignment were committed with p5.
- **Decision**: FIXED. roadmap.md:257 now reads "Implemented; run `/10x-archive report-sighting-on-map`", and the plan's References note that PRD v2 and the roadmap alignment were committed with Phase 5 (fa8b75e).

### F6 — The visual gate is ticked as complete but has documented gaps

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Success Criteria
- **Location**: context/changes/report-sighting-on-map/visual-notes.md, plan.md Progress 4.9
- **Detail**: Progress 4.9 says `visual-notes.md` holds desktop and 375 px screenshots of all four views.
  - **Profile, My sightings:** no images were committed, because they show the account email.
  - **Other cells not captured:** map-selected at 375, report form untouched at 375, and detail not-found on desktop.
  - These gaps are honestly documented, but the checkbox overstates them.
  - Manual items 5.4 and 5.5 (live CSP check, end-to-end on emulated or deployed build) have no recorded artefact. The plan gives them no file, so they rest on the user's word.
- **Fix**: Take the Profile screenshots with the email masked (for example, a throwaway account whose email is a placeholder) and add the three missing cells, or reword 4.9 to describe the gaps.
- **Decision**: FIXED. Progress 4.9 keeps its title (the Progress convention forbids renaming) and now lists the gaps after its SHA. No screenshots were retaken.

### F7 — Small deviations from the plan are not recorded in it

- **Severity**: 💬 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: PigeonWatch/Frontend/src/app/shared/map/map-view/map-view.ts:125, PigeonWatch/Api/WebApi/Controllers/SightingsController.cs:33-34, PigeonWatch/Api/BusinessLogic/Services/SightingService.cs:14-19,33
- **Detail**: Each of these is safe and tested. None appears in the plan.
  - **Marker title:** markers set `title: marker.label`, the user-typed name. The plan says "static class name only" and no tooltips with user text. Leaflet assigns it as a DOM property, so it is not an XSS sink.
  - **ReporterNotFound:** returns 401 rather than a coded 400. This mirrors `AccountController`.
  - **Service extras:** the service trims text, turns a blank name into null, and caps `retryAfterSeconds` at 60.
  - **Cancel button:** the report form has a Cancel button.
- **Fix**: Add a short "Deviations" addendum to plan.md listing these, so later reviews treat them as intended.
- **Decision**: FIXED. A `## Deviations` section was added to plan.md before Progress, also covering the review fixes F1-F4.

### F8 — The initial bundle has 3.5 kB of headroom left

- **Severity**: 💬 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: PigeonWatch/Frontend/angular.json:59
- **Detail**: The initial total went from about 463 kB to 496.46 kB, against the 500 kB warning. The global Leaflet CSS accounts for a large part of that. It is within budget, and Frontend/CLAUDE.md records the fallback. The next initial-bundle addition from any slice will trip the warning.
- **Fix**: No action now. Optionally move the Leaflet CSS to an `inject: false` bundle loaded by `map-view` before the next slice.
- **Decision**: FIXED. The Leaflet CSS is now a non-injected `leaflet` styles bundle (unhashed `leaflet.css`), added once as a `<link>` by the default `LEAFLET_LOADER`. Initial total 486.15 kB (was 496.54). Spec added; 388/388 tests pass. Frontend/CLAUDE.md updated. The loader does not wait for the stylesheet to load (jsdom never fires the event). Not yet checked with the CSP headers in a browser.

### F9 — Status and criticality values the SPA does not know have no fallback

- **Severity**: 💬 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: PigeonWatch/Frontend/src/app/features/sightings/nearby-map-page/nearby-map-page.ts:82, sighting-detail-page.ts:98, shared/map/map-view/map-view.ts:122
- **Detail**: A value the SPA does not know makes the marker class `undefined` and passes `undefined` to the translate pipe. This only happens if the API adds a value before the SPA does. Values are append-only per Api/CLAUDE.md.
- **Fix**: Fall back to the `monitor` marker class and a `sighting.unknown` label in the lookup helpers.
- **Decision**: FIXED. `sightingStatusLabel`, `sightingCriticalityLabel` and `sightingCriticalityMarker` in `sighting.types.ts` fall back to `sighting.unknown` ("Unknown") and `monitor`. My sightings, the nearby map and the detail page use them, and the report form keeps the maps for its known options. Spec added; 389/389 tests pass.

### F10 — The location picker ignores the form's disabled state

- **Severity**: 💬 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: PigeonWatch/Frontend/src/app/shared/map/location-picker/location-picker.ts:33
- **Detail**: The control implements only `value` and `touch` from `FormValueControl`. While the form is busy, the other fields and Cancel are disabled, but the pin can still be moved and "Use my location" still runs. The payload is already captured, so no data is corrupted; the behaviour is just inconsistent.
- **Fix**: Add a `disabled` input from `FormValueControl` and use it to disable the buttons and map interaction.
- **Decision**: DISMISSED. The premise was inaccurate: while the form is busy only Cancel is disabled, and the text fields and selects stay editable too, so the picker is consistent with them.

## Triage summary

- **Fixed**: F1, F2, F3 (Fix A), F4, F5, F6, F7, F8, F9 (9)
- **Dismissed**: F10 (1)
- **Verification after fixes**: unit tests 191/191, architecture tests 42/42, Vitest 389/389. `npm run build` passes with initial total 486.18 kB, and the Leaflet JS and CSS load lazily. Not yet checked in a browser: the pane was refused for localhost:4200. Re-check the CSP live once (served with the headers, or after deploy), because the Leaflet CSS is now a separate `leaflet.css` file.
- **User confirmation (2026-10-11)**: after the fixes, the user reported that everything works.
