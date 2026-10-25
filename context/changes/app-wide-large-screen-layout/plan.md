# App-wide layout for large screens Implementation Plan

## Overview

Make the PigeonWatch Angular app use large screens (about 1280 to 2000 px). The shell gets width tiers that switch on from `xl`, the header follows the tier, short forms get a slightly larger card, the map home gets wider and taller, and the sighting detail and report pages become two-column layouts. Shared pieces stay shared: one `width` input on `app-page-card`, responsive tokens, and a resize observer in `app-map-view`.

## Current State Analysis

Width is set by four independent caps: the shell `<main>` (40rem, or 64rem from `md` with `data: { layout: 'wide' }`), `app-page-card` (28rem), the header bar (`pw-container`, 40rem on every route) and the map (24rem tall, 18rem aside). No layout rule under `src/app` uses the `xl` breakpoint, so 1280 px and 2000 px render the same layout. Only the map home is wide. The detail page is a 28rem card with a single-column list and a toggled map. `app-my-sightings` is uncapped and fills the 40rem column under the 28rem profile card. `MapView` has no resize handling (deferred as D5 by `nearby-map-ui-contract`).

## Desired End State

- At `xl` and above, the map home fills up to a 90rem column with a map whose height follows the viewport, and its aside stays beside it.
- At `xl` and above, the detail page is a 60rem card with details on the left and the map toggle and map on the right; the map is still not created until "Show map".
- At `xl` and above, the report page is a 60rem card with the text fields on the left and the location picker on the right.
- At `xl` and above, login, register and profile cards are about 34rem; the My sightings card has the same width as the profile card.
- The header bar has the width of the map home column in every tier (40rem, 64rem from `md`, 90rem from `xl`) on every route, so its contents never move between pages.
- Below `xl`, every route renders exactly as it does today, except the header and My sightings alignment described above (My sightings and the header may change below `xl`: see Phase 1 and 2).
- Verified by the unit specs, a production build within budget, and a manual pass at 1280, 1600 and 2000 px.

### Key Discoveries:

- `app/app.scss:22-32` and `app/app.ts:40-63`: the shell reads a binary `wide` flag from the deepest route's `data['layout']`.
- `app/app.spec.ts:109-132`: specs pin the wide layout to the home route only and `/profile` to no wide class; they change with the tiers.
- `shared/ui/page-card/page-card.scss:3-10`: the 28rem cap is a host constant; a variation must be an input (`Frontend/CLAUDE.md`, "A variant is a component input").
- `styles/_common.scss:1-5`, `app/app.html:5`: the header bar uses `pw-container` at 40rem, so it cannot follow the main width without its own rule.
- `shared/map/map-view/map-view.ts`: no resize handling; Leaflet only tracks window resizes. `map-view.scss:5-7` fixes the height to `--pw-map-height`.
- `app/core/warmup/warmup-panel/warmup-panel.scss:12-14` also uses `--pw-form-max-width`, so a responsive token grows that overlay too.
- Raw lengths are only allowed in `_tokens.scss`, `_material-theme.scss` and `_breakpoints.scss`; `style-token-coverage.spec.ts` enforces it.
- The initial bundle was measured at 486.2 kB against a 500 kB budget; the shell and tokens are in it.

## What We're NOT Doing

- Redesigning login and register beyond the card growth, and any change to their copy or flow.
- New breakpoints beyond using the existing `xl` (1280px); no ultra-wide tier above 90rem.
- Dark mode, new libraries, new UI text or resx keys.
- App-wide route titles or focus on navigation.
- Moving the map home's aside, changing its legend, or changing search behaviour.
- Preserving map state on Back, an origin-aware Back label, or other detail charges listed as out of scope by `sighting-detail-ui-contract`.
- A report kitchen-sink cell: the sink has no Report cells today; the report page is checked manually.
- Installing a screenshot-test runner or any CI change.

## Implementation Approach

Build bottom-up so each phase leaves the app working: first the shell tiers, tokens and header, then the shared card and form-page width, then the map component and map home, then the two two-column pages, and last the kitchen sink, rule file and manual gate. Everything switches on from `xl` with `bp.from(xl)` or a responsive token, so below `xl` the layouts only differ where this plan says so.

Decisions already made with the user: every route takes part; detail has details beside the map; the map home grows wider and taller; the tier is a route value with an `xl` rule; report has the form beside the pin map; the header and My sightings follow the page; delivery is one phased change.

## Phase 1: Shell tiers and header

### Overview

Introduce the width tokens and the two layout values, and make the header follow the main width.

### Changes Required:

#### 1. Width tokens

**File**: `PigeonWatch/Frontend/src/styles/_tokens.scss`

**Intent**: Add the new large-screen widths as tokens and make `--pw-form-max-width` larger from `xl`, in the token file where raw lengths are allowed.

**Contract**: New tokens for the 90rem wide-tier cap (`--pw-content-wide-xl-max-width`) and for the larger form width (about 34rem) applied by redefining `--pw-form-max-width` inside an `xl` media query; `--pw-content-wide-max-width` (64rem) is reused as the `roomy` cap. Raw values only in this file.

#### 2. Shell tier signal and classes

**File**: `PigeonWatch/Frontend/src/app/app.ts`, `app.html`, `app.scss`

**Intent**: Replace the boolean `wide` signal with a layout signal that reads the route's `data['layout']` as `'wide'`, `'roomy'` or the default, and apply a matching modifier class to both `<main>` and the header bar.

**Contract**: `layoutOf` returns the value only when it is `'wide'` or `'roomy'`. `wide`: 64rem from `md`, `--pw-content-wide-xl-max-width` from `xl`. `roomy`: 40rem below `xl`, 64rem from `xl`. The header bar stops using `pw-container` and takes the `wide` tier widths (40rem, 64rem from `md`, `--pw-content-wide-xl-max-width` from `xl`) and the same padding as `<main>` on every route; it carries no per-route class. Imports from `shared/ui` in `app.ts` stay by file path.

#### 3. Shell specs

**File**: `PigeonWatch/Frontend/src/app/app.spec.ts`

**Intent**: Update the wide-layout specs for the tiers and the header class.

**Contract**: One spec per tier checks the modifier class on `<main>` after navigation; the "wide only on home" guard spec is rewritten once routes are assigned (Phases 3 to 5 set the route values; the guard spec is finalised in Phase 6, and until then the expected set matches the routes edited so far).

### Success Criteria:

#### Automated Verification:

- Shell specs pass: `npm test -- --no-watch` (from `PigeonWatch/Frontend`)
- Production build passes within the bundle budget: `npm run build`

#### Manual Verification:

- At 1024 px and below, the header and page look as they did before this phase on the map home and on profile
- The header bar width matches the map home column, and its contents stay in the same place on profile and the map home

---

## Phase 2: Page card and form pages

### Overview

Add the wide card width to `app-page-card` and align My sightings with the card.

### Changes Required:

#### 1. Page-card width input

**File**: `PigeonWatch/Frontend/src/app/shared/ui/page-card/page-card.ts`, `page-card.scss`, `page-card.spec.ts`

**Intent**: Let a page ask for a larger card without a page-level override. Default keeps the form width.

**Contract**: Signal input `width` with values `'form'` (default, `--pw-form-max-width`) and `'wide'` (a new token, about 60rem, applied only from `xl`; below `xl` it behaves like `form`). Reflected as a host class or attribute, following the `detail-list` pattern; the new token lives in `_tokens.scss`. Spec covers both values.

#### 2. My sightings card width

**File**: `PigeonWatch/Frontend/src/app/features/sightings/my-sightings/my-sightings.scss`

**Intent**: Give the My sightings card the same width and centring as the page card above it.

**Contract**: `:host` uses `max-width: var(--pw-form-max-width)` and auto inline margins; no literal values.

### Success Criteria:

#### Automated Verification:

- Page-card and my-sightings specs pass: `npm test -- --no-watch`
- Style-token spec passes (included in the full run)

#### Manual Verification:

- Login, register and profile cards are larger from `xl` and unchanged below it
- The My sightings card has the same width as the profile card at every width
- The warm-up overlay text still looks right at 2000 px

---

## Phase 3: Map component and map home

### Overview

Make the map follow its container and the viewport, and move the map home to the new wide tier.

### Changes Required:

#### 1. Map resize handling

**File**: `PigeonWatch/Frontend/src/app/shared/map/map-view/map-view.ts`, `map-view.spec.ts`

**Intent**: Call `invalidateSize` when the canvas size changes, so a container that resizes without a window resize still renders tiles correctly.

**Contract**: A `ResizeObserver` on the canvas element, created after Leaflet loads and disconnected in `ngOnDestroy`/`DestroyRef`; guarded when `ResizeObserver` is undefined (jsdom). The stub in `shared/map/testing/map-view-stub.ts` needs no change. Spec checks the guard and that disconnect runs on destroy.

#### 2. Taller map from `xl`

**File**: `PigeonWatch/Frontend/src/styles/_tokens.scss`, `shared/map/map-view/map-view.scss`

**Intent**: Let the map height grow with the viewport on large screens, everywhere a map renders.

**Contract**: New token `--pw-map-height-xl` using a viewport-based clamp (minimum at the current 24rem, maximum about 48rem); `map-view.scss` uses it from `xl`. Below `xl` the height is unchanged.

#### 3. Map home route tier

**File**: `PigeonWatch/Frontend/src/app/app.routes.ts`

**Intent**: Keep `layout: 'wide'` on the home route so it takes the 90rem cap from `xl`.

**Contract**: No new value; the existing flag now maps to the extended tier from Phase 1. The aside width stays `--pw-map-aside-width`.

### Success Criteria:

#### Automated Verification:

- Map-view and map-home specs pass: `npm test -- --no-watch`
- Production build passes: `npm run build`

#### Manual Verification:

- At 1600 and 2000 px the map home column is wider than 64rem and the map is taller than 24rem
- Resizing the browser window across `xl` keeps the map tiles aligned with its box
- Below `xl` the map home looks as it did before

---

## Phase 4: Sighting detail two-column

### Overview

Use the width on the detail page and keep the map behind its toggle.

### Changes Required:

#### 1. Detail route tier

**File**: `PigeonWatch/Frontend/src/app/app.routes.ts`

**Intent**: Give `sightings/:id` the `roomy` tier so the shell can hold a 60rem card from `xl`.

**Contract**: `data: { layout: 'roomy' }` on the route.

#### 2. Detail page layout

**File**: `PigeonWatch/Frontend/src/app/features/sightings/sighting-detail-page/sighting-detail-page.html`, new `sighting-detail-page.scss`, `.ts` imports

**Intent**: Use `app-page-card width="wide"`; from `xl` place the detail list in a left column and the toggle button with the map container in a right column. Below `xl` keep the current single column.

**Contract**: A grid wrapper with two columns from `xl`. The map container keeps its id `sighting-detail-map` and `aria-controls` wiring; `app-map-view` is still created only while `mapShown()` is true. The right column holds the toggle even when the map is hidden, so toggling does not reflow the left column. No new resx keys, no inline styles.

### Success Criteria:

#### Automated Verification:

- Detail page spec passes, including that no map exists before "Show map": `npm test -- --no-watch`
- Production build passes: `npm run build`

#### Manual Verification:

- At 1600 and 2000 px the details are on the left and the toggle and map on the right
- The map appears only after pressing Show map, and Hide map removes it
- Below `xl` the detail page is unchanged

---

## Phase 5: Report two-column

### Overview

Use the width on the report page with the picker beside the fields.

### Changes Required:

#### 1. Report route tier

**File**: `PigeonWatch/Frontend/src/app/app.routes.ts`

**Intent**: Give `sightings/new` the `roomy` tier.

**Contract**: `data: { layout: 'roomy' }` on the route.

#### 2. Report page layout

**File**: `PigeonWatch/Frontend/src/app/features/sightings/report-sighting-page/report-sighting-page.html`, new `report-sighting-page.scss`, `.ts` imports

**Intent**: Use `app-page-card width="wide"`; from `xl` the text and select fields sit in a left column and `app-location-picker` in a right column, with the form actions below both. Below `xl` keep the current order.

**Contract**: The fields go into one stack wrapper and the picker into its own cell of a grid; the form actions span both columns. Field order and Tab order stay: fields, picker, actions. Form behaviour, validation and `FormSubmitState` are unchanged.

### Success Criteria:

#### Automated Verification:

- Report page spec passes: `npm test -- --no-watch`
- Production build passes: `npm run build`

#### Manual Verification:

- At 1600 and 2000 px the fields are on the left, the picker on the right, and the actions below
- Dropping a pin and submitting still works with the new structure
- Below `xl` the page is unchanged

---

## Phase 6: Kitchen sink, docs and gate

### Overview

Show the large-screen layouts in the sink, update the rules, finalise the guard specs and run the gate.

### Changes Required:

#### 1. Kitchen sink at large widths

**File**: `PigeonWatch/Frontend/src/app/dev/dev-routes.ts`, `dev/kitchen-sink/kitchen-sink.scss`, `kitchen-sink.html`

**Intent**: Let sink cells for the map home and the detail page render at the wide tier, while the Profile group stays narrow.

**Contract**: The sink route keeps `layout: 'wide'`, so the Nearby map cells use the new tier; the Sighting detail group is allowed to use the wide card width. `.sink__group--narrow` stays on the Profile group only. The sink spec keeps passing.

#### 2. Guard specs

**File**: `PigeonWatch/Frontend/src/app/app.spec.ts`

**Intent**: Pin the final route assignments.

**Contract**: The home route is `wide`; `sightings/:id` and `sightings/new` are `roomy`; no other product route declares a layout; `/profile` gets no tier class.

#### 3. Rule file

**File**: `PigeonWatch/Frontend/CLAUDE.md`

**Intent**: Update "Route width", "Design tokens" and the `app-page-card` description for the tiers, the `width` input, the responsive form width, the `xl` rule, the header following the tier, and the map resize handling.

**Contract**: The old "header keeps the narrow width" statement and "today only the map home" wording are replaced; the kitchen-sink description notes the wide cells.

### Success Criteria:

#### Automated Verification:

- Full frontend test run passes: `npm test -- --no-watch`
- Production build passes and the initial bundle stays under 500 kB: `npm run build`

#### Manual Verification:

- At 1280, 1600 and 2000 px each of the six routes (map home, detail, report, profile, login, register) looks right
- At 1024 px and below nothing regressed (except the header and My sightings alignment)
- Console shows no errors at 2000 px on the map home and detail page

---

## Testing Strategy

### Unit Tests:

- `app.spec.ts`: tier classes on `<main>` and the header bar, and the route-assignment guard.
- `page-card.spec.ts`: `width` input values.
- `map-view.spec.ts`: resize observer present and absent, and disconnect on destroy.
- Existing page specs: detail page still has no map before the toggle; report fields and validation unchanged.
- `style-token-coverage.spec.ts` catches any literal length or colour in the new SCSS.

### Integration Tests:

- None added; there is no layout test runner, and a screenshot runner is not being installed.

### Manual Testing Steps:

1. Run `npm start` in `PigeonWatch/Frontend`, open the kitchen sink at `/dev/kitchen-sink`, and check the Nearby map and Sighting detail cells at 1280, 1600 and 2000 px.
2. With the API running, check the six routes at the same widths in a real browser (the in-app pane may not reach 2000 px).
3. Resize the window across 1280 px on the map home and detail page with the map shown, and confirm the tiles stay aligned.

## Performance Considerations

The shell and tokens are in the initial bundle, so `npm run build` is checked after Phases 1 and 6 against the 500 kB budget. The resize observer is in the lazy map chunk. No tiles are requested earlier than today; the detail map is still created only after "Show map".

## References

- Related research: `context/changes/app-wide-large-screen-layout/research.md`
- Shell width: `PigeonWatch/Frontend/src/app/app.scss:22-32`, `app.ts:40-63`
- Page card: `PigeonWatch/Frontend/src/app/shared/ui/page-card/page-card.scss:3-10`
- Earlier decisions: `context/archive/2026-10-11-nearby-map-ui-contract/plan.md:268-274`
- Deferred map resize item D5: `context/archive/2026-10-11-nearby-map-ui-contract/research.md:92`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Shell tiers and header

#### Automated

- [x] 1.1 Shell specs pass — 796781f
- [x] 1.2 Production build passes within the bundle budget — 796781f

#### Manual

- [x] 1.3 At 1024 px and below, the header and page look as they did before on the map home and on profile — 796781f
- [x] 1.4 The header bar width matches the map home column, and its contents stay in the same place on profile and the map home — 796781f

### Phase 2: Page card and form pages

#### Automated

- [x] 2.1 Page-card and my-sightings specs pass — b4023e3
- [x] 2.2 Style-token spec passes — b4023e3

#### Manual

- [x] 2.3 Login, register and profile cards are larger from xl and unchanged below it — b4023e3
- [x] 2.4 The My sightings card has the same width as the profile card at every width — b4023e3
- [x] 2.5 The warm-up overlay text still looks right at 2000 px — b4023e3

### Phase 3: Map component and map home

#### Automated

- [x] 3.1 Map-view and map-home specs pass — d2f03fa
- [x] 3.2 Production build passes — d2f03fa

#### Manual

- [x] 3.3 At 1600 and 2000 px the map home column is wider than 64rem and the map is taller than 24rem — d2f03fa
- [x] 3.4 Resizing the browser window across xl keeps the map tiles aligned with its box — d2f03fa
- [x] 3.5 Below xl the map home looks as it did before — d2f03fa

### Phase 4: Sighting detail two-column

#### Automated

- [x] 4.1 Detail page spec passes, including that no map exists before Show map
- [x] 4.2 Production build passes

#### Manual

- [x] 4.3 At 1600 and 2000 px the details are on the left and the toggle and map on the right
- [x] 4.4 The map appears only after pressing Show map, and Hide map removes it
- [x] 4.5 Below xl the detail page is unchanged

### Phase 5: Report two-column

#### Automated

- [ ] 5.1 Report page spec passes
- [ ] 5.2 Production build passes

#### Manual

- [ ] 5.3 At 1600 and 2000 px the fields are on the left, the picker on the right, and the actions below
- [ ] 5.4 Dropping a pin and submitting still works with the new structure
- [ ] 5.5 Below xl the report page is unchanged

### Phase 6: Kitchen sink, docs and gate

#### Automated

- [ ] 6.1 Full frontend test run passes
- [ ] 6.2 Production build passes and the initial bundle stays under 500 kB

#### Manual

- [ ] 6.3 At 1280, 1600 and 2000 px each of the six routes looks right
- [ ] 6.4 At 1024 px and below nothing regressed except the header and My sightings alignment
- [ ] 6.5 Console shows no errors at 2000 px on the map home and detail page
