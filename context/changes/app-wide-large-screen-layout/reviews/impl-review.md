<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: App-wide layout for large screens

- **Plan**: context/changes/app-wide-large-screen-layout/plan.md
- **Scope**: Full plan (6 of 6)
- **Reviewed phases**: 1, 2, 3, 4, 5, 6
- **Date**: 2026-10-25
- **Verdict**: NEEDS ATTENTION
- **Findings**: 0 critical, 4 warnings, 5 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | WARNING |
| Scope Discipline | WARNING |
| Safety & Quality | PASS |
| Architecture | PASS |
| Pattern Consistency | WARNING |
| Success Criteria | WARNING |

## Findings

### F1 — Detail and report routes use `wide`, and the wide card is 90rem, not `roomy` and 60rem

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Plan Adherence
- **Location**: PigeonWatch/Frontend/src/app/app.routes.ts:39-47, src/styles/_tokens.scss (--pw-page-card-wide-max-width)
- **Detail**: Plan Phases 4 and 5 set `layout: 'roomy'` on `sightings/:id` and `sightings/new` and a 60rem wide card. Shipped: both routes are `wide`, the wide card is 90rem, and the detail page uses `width` wide only while the map is shown. `roomy` is supported by the shell but no route uses it (dead CSS besides a test route). Plan text and Desired End State still say 60rem and roomy.
- **Fix**: Add an addendum to plan.md recording the decision (wide for all three, 90rem card, detail widens with the map) and either keep or remove the unused `roomy` tier.
  - Strength: Source of truth matches the code before archive; CLAUDE.md already documents the shipped behaviour.
  - Tradeoff: Plan becomes a moving target.
  - Confidence: HIGH — manual checks passed on the shipped layout.
  - Blind spot: Whether `roomy` is wanted for a future route.
- **Decision**: FIXED (plan addendum)

### F2 — Initial bundle budget warning raised to 520 kB; gate 6.2 passes only against the raised limit

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Success Criteria
- **Location**: PigeonWatch/Frontend/angular.json, PigeonWatch/Frontend/CLAUDE.md
- **Detail**: Plan criterion 6.2 says the initial bundle stays under 500 kB. Build measures 512.09 kB; Phase 5 raised the warning limit to 520 kB for mat-select and menu theming. CLAUDE.md still says 486.2 kB and about 14 kB under the warning.
- **Fix**: Update the CLAUDE.md bundle sentence to the current figure and the 520 kB limit, and note the deviation on 6.2 in the plan addendum.
- **Decision**: FIXED (CLAUDE.md bundle figure + addendum)

### F3 — Unplanned shell and header changes widen the blast radius

- **Severity**: ⚠️ WARNING
- **Impact**: 🔬 HIGH — architectural stakes; think carefully before deciding
- **Dimension**: Scope Discipline
- **Location**: PigeonWatch/Frontend/src/app/app.scss, app.html, app.ts, src/styles/_common.scss, _material-theme.scss
- **Detail**: Beyond the plan: an inner `.app-body` scroll container with a non-sticky header (page scroll moves from the window to an element), a brand home link, user menu and select panel restyling, a button-width observer publishing `--pw-user-button-width`, mat-select replacing native selects with a null option, a textarea height cap, and global menu/select/option theme overrides. Router scroll restoration, anchor scrolling, mobile browser chrome collapse and Home/End/Page keys now depend on `.app-body`.
- **Fix**: Record these in the plan addendum, and check scroll behaviour in `.app-body` (focusHeading arrival, keyboard scrolling, Leaflet wheel and drag) on the routes.
  - Strength: Keeps the plan an honest record; the checks target the real regression risk.
  - Tradeoff: Manual verification effort.
  - Confidence: MEDIUM — nothing in the diff shows a break, but only the sink and a few routes were exercised.
  - Blind spot: Router scrollPositionRestoration config was not read.
- **Decision**: FIXED (plan addendum; manual scroll pass still to run)

### F4 — Component and common SCSS bypass the Material override rules

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Pattern Consistency
- **Location**: PigeonWatch/Frontend/src/app/app.scss:41-52, src/styles/_common.scss:8-23
- **Detail**: Frontend/CLAUDE.md says Material tweaks go through `mat.<component>-overrides` in `_material-theme.scss` and variants are inputs. `app.scss` sets `--mat-button-outlined-*` custom properties directly (including the new pressed-state opacity of 0), and `_common.scss` uses doubled-class selectors (`.pw-user-menu-panel.pw-user-menu-panel`) as an `!important` substitute and sets `--mat-focus-indicator-display`. The token spec does not catch these.
- **Fix**: Move the shape and opacity to `mat.button-overrides` keyed by a host attribute in `_material-theme.scss`, and the panel rules to `mat.menu-overrides` and `mat.select-overrides`; if the doubled class stays, record it as an accepted exception in CLAUDE.md.
- **Decision**: FIXED (button props moved to _material-theme.scss; panel doubled classes recorded as accepted exception in CLAUDE.md)

### F5 — Detail grid reserves the map column while the map is hidden

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: PigeonWatch/Frontend/src/app/features/sightings/sighting-detail-page/sighting-detail-page.scss:9-14
- **Detail**: From `xl` the grid is 1fr/2fr and `.sighting-detail__map` always renders, while the card is form width when the map is hidden. By CSS reading the details column may be squeezed. Manual checks 4.3 and 6.3 passed, so it may be acceptable or already handled; the reviewer did not render it.
- **Fix**: Apply the two-column grid only while the map is shown, or confirm the hidden-map look at 1280 px and leave as is.
- **Decision**: FIXED (grid only while map shown)

### F6 — `--pw-user-button-width` is set on the document root and never removed

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: PigeonWatch/Frontend/src/app/app.ts:62-77
- **Detail**: The effect writes the property on `document.documentElement` and its cleanup only disconnects the observer, so the stale value outlives logout. It is re-published on the next login, so harmless. The effect and `userMenuOpen` have no spec.
- **Fix**: Call `removeProperty` in `onCleanup`, and add a spec for the effect.
- **Decision**: FIXED (removeProperty on cleanup + spec)

### F7 — The null option in the report selects has no visible text

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: PigeonWatch/Frontend/src/app/features/sightings/report-sighting-page/report-sighting-page.html:54-88
- **Detail**: The clear option is a blank row with an `aria-label` equal to the placeholder, so it reads as a value rather than "none". The form model stays a string and `required` still blocks submit.
- **Fix**: Give it visible text through a resx key, or drop the option.
- **Decision**: SKIPPED

### F8 — Test gaps for the new large-screen behaviour

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Success Criteria
- **Location**: PigeonWatch/Frontend/src/app/shared/ui/page-card/page-card.spec.ts, kitchen-sink.spec.ts
- **Detail**: No assertion that the page-card footer still collapses when only `headerActions` is projected; no spec for the xl grids (jsdom has no layout, so manual evidence only); the sink spec did not change for the Sighting detail group becoming wide.
- **Fix**: Add the page-card footer spec; leave layout to manual evidence.
- **Decision**: FIXED (page-card headerActions footer spec)

### F9 — First paint on a wide route renders narrow, then widens

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: PigeonWatch/Frontend/src/app/app.ts:50-60
- **Detail**: `layout` is null until `NavigationEnd`, as before this change, but the 90rem tier makes the jump more visible. `layoutOf` reads only the deepest child's data.
- **Fix**: None needed now; revisit if the jump is noticeable.
- **Decision**: SKIPPED
