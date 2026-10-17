---
change_id: nearby-map-ui-contract
title: Nearby map ui contract
status: archived
created: 2026-10-11
updated: 2026-10-17
archived_at: 2026-10-17T17:45:25Z
---

## Notes

UI change run through `/10x-ui`.

- **View:** Nearby map home (`PigeonWatch/Frontend/src/app/features/sightings/nearby-map-page`, route `/`) including the selected-sighting card it renders below the map. One view only.
- **Token source:** `PigeonWatch/Frontend/src/styles/_tokens.scss` (`--pw-*`, including `--pw-color-criticality-*` and `--pw-map-*`), mapped onto Material in `_material-theme.scss`; common classes in `_common.scss`. Components in `src/app/shared/ui/` and `src/app/shared/map/`.
- **Contract variant:** existing design system. Extend it; do not fork a second palette, add a UI library, or run `ng add`.
- **Pre-audit:** 0 hardcoded colour / palette / arbitrary-value / `px` hits in the 4 view files; 3 `var(--pw-*)` uses in the view stylesheet; 1 `shared/ui` import; `style-token-coverage.spec.ts` already guards stylesheets; the kitchen sink (`src/app/dev/kitchen-sink`) covers Profile only. Expected charges lean towards states, a shared component and entry points rather than missing tokens.
- **Candidates to confirm in research:** the status/criticality `dl` duplicated with `sighting-detail-page` (`.nearby-selected__details` vs `.detail-list`); the spinner wrapper duplicated (`.nearby-spinner` vs `.detail-spinner`); criticality shown as plain text although `--pw-color-criticality-*` tokens exist; no kitchen-sink cells for the map states (locating, position unknown, loading, error, empty, selected).
- **Follow-up change:** `sighting-detail-ui-contract` for `/sightings/:id`, after this one is archived, reusing what this change promotes to `shared/ui`.
