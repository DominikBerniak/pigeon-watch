---
change_id: sighting-detail-ui-contract
title: Sighting detail ui contract
status: implemented
created: 2026-10-18
updated: 2026-10-18
archived_at: null
---

## Notes

UI change run through `/10x-ui`.

- **View:** Sighting detail page (`PigeonWatch/Frontend/src/app/features/sightings/sighting-detail-page`, route `/sightings/:id`, behind `authGuard`) including the opt-in map it shows behind "Show map". One view only.
- **Token source:** `PigeonWatch/Frontend/src/styles/_tokens.scss` (`--pw-*`), mapped onto Material in `_material-theme.scss`; common classes in `_common.scss`. Components in `src/app/shared/ui/` and `src/app/shared/map/`.
- **Contract variant:** existing design system. Extend it; do not fork a second palette, add a UI library, or run `ng add`.
- **Pre-audit:** 0 hardcoded colour / palette / arbitrary-value hits in the view files (`sighting-detail-page.html`, `.ts`, `.spec.ts`); the view has no stylesheet of its own; it already uses `app-page-card`, `app-alert`, `app-error-retry`, `app-loading-indicator`, `dl[app-detail-list]` and `app-criticality-badge` from `shared/ui` (promoted by the archived `nearby-map-ui-contract`); `style-token-coverage.spec.ts` already guards stylesheets; the kitchen sink (`src/app/dev/kitchen-sink`) has Profile and Nearby map cells and none for the detail page. Expected charges lean towards states (loading, not found, error, map toggle on and off), the entry point (a straight link, a malformed or unknown id), keyboard and focus behaviour, and layout hierarchy rather than missing tokens or components.
- **Origin:** deliberately left out of `nearby-map-ui-contract` (one view per change); see `context/archive/2026-10-11-nearby-map-ui-contract/change.md`, "Follow-up change".
- **Contract guard to keep:** the cost guardrail in `PigeonWatch/CLAUDE.md` (no map tile is fetched until "Show map" is pressed) must survive any layout change.
