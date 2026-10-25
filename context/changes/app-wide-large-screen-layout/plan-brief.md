# App-wide layout for large screens — Plan Brief

> Full plan: `context/changes/app-wide-large-screen-layout/plan.md`
> Research: `context/changes/app-wide-large-screen-layout/research.md`

## What & Why

Make the whole PigeonWatch app use large screens (about 1280 to 2000 px). Raised during the `sighting-detail-ui-contract` manual check: at about 2000 px the detail page is a 28rem card in a mostly empty page, and the same happens on every card page.

## Starting Point

Four separate caps set width: the shell (40rem, or 64rem on the map home), `app-page-card` (28rem), the header (40rem) and the map (24rem tall). Nothing uses the `xl` breakpoint, so 1280 px and 2000 px look the same, and the map has no resize handling.

## Desired End State

From `xl`, the map home is wider and taller, the detail page shows details beside the toggle and map, the report page shows its fields beside the location picker, and login, register and profile cards are a bit larger. The header follows the page width and My sightings matches the profile card. Below `xl` the layouts are as today.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| --- | --- | --- | --- |
| Which routes | Every route takes part | User wants the whole app to work on large screens. | Plan |
| Form pages | Slightly larger card from `xl` via a responsive `--pw-form-max-width` | Scales without stretching inputs and needs no per-page work. | Plan |
| Detail page | Details left, toggle and map right from `xl` | Uses the width and keeps the map out of the DOM until "Show map". | Plan |
| Report page | Fields left, location picker right from `xl` | The picker is the one form element that benefits from width. | Plan |
| Map home | Wider (90rem) and taller (viewport-based) from `xl` | Both extra width and height reach the map. | Plan |
| Tiers | Route `layout` is `wide` (map home) or `roomy` (detail, report) | Follows the route-flag rule instead of page-level widths. | Plan |
| Header, My sightings | Header takes the main width; My sightings takes the card width | Removes both known mismatches. | Research |
| Map resize | `ResizeObserver` calls `invalidateSize` | Closes the deferred D5 item once layout changes the map box. | Research |
| Delivery | One change with six phases | One plan, with the shell first so views reuse it. | Plan |

## Scope

**In scope:** shell tiers and tokens, header width, `app-page-card` width input, My sightings alignment, map resize and tall map, detail and report two-column layouts, kitchen-sink cells, `Frontend/CLAUDE.md` rules, guard specs.

**Out of scope:** an ultra-wide tier above 90rem, a login/register redesign, new copy or resx keys, a report kitchen-sink cell, a screenshot-test runner, any CI change.

## Architecture / Approach

Bottom-up, everything switching on from `xl`: tokens and shell tier classes first, then the shared card input and form width, then the map component and map home, then the two two-column pages, then the sink, rules and gate. Raw lengths stay in `_tokens.scss`; the shell reads the route value, never a page override.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Shell tiers and header | Tokens, `wide`/`roomy` values, header follows the tier | Initial bundle budget (486.2 of 500 kB) |
| 2. Page card and form pages | `width` input, responsive form width, My sightings aligned | Warm-up overlay also grows |
| 3. Map component and map home | Resize observer, tall map, map home on the new tier | Resize behaviour is untested in a real browser |
| 4. Sighting detail two-column | `roomy` route, details beside toggle and map | Map must stay out of the DOM until toggled |
| 5. Report two-column | `roomy` route, fields beside picker | Tab order and form behaviour after the restructure |
| 6. Sink, docs and gate | Sink cells, rule file, guard specs, budget, manual checks | The in-app pane may not reach 2000 px |

**Prerequisites:** the API running for manual checks of detail and report; a real browser at 1280 to 2000 px.
**Estimated effort:** about 3 to 4 sessions across 6 phases.

## Open Risks & Assumptions

- Research read source only; nothing was seen in a browser at 1280 to 2000 px.
- The 34rem form width, 60rem card, 90rem wide tier and 48rem map maximum are starting values to tune during the manual passes.
- Below `xl`, the header and My sightings change width, which is the one deliberate change under `xl`.

## Success Criteria (Summary)

- At 1600 and 2000 px the map home, detail and report pages fill the width sensibly, and the other cards are slightly larger.
- Below `xl`, nothing looks different except the header and My sightings alignment.
- Tests and the production build pass within the bundle budget.
