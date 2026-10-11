---
change_id: profile-ui-contract
title: Profile ui contract
status: implemented
created: 2026-10-11
updated: 2026-10-11
archived_at: null
---

## Notes

UI change run through `/10x-ui`.

- **View:** Profile (`PigeonWatch/Frontend/src/app/features/profile/profile-page`, route `/profile`) including the `password-dialog` it opens. One view only.
- **Token source:** `PigeonWatch/Frontend/src/styles/_tokens.scss` (`--pw-*`), mapped onto Material in `_material-theme.scss`; common classes in `_common.scss`.
- **Contract variant:** existing design system. Extend it; do not fork a second palette, add a UI library, or run `ng add`.
- **Pre-audit:** 0 hardcoded colour / palette / arbitrary-value hits in the 6 view files; shared components from `shared/ui` already used; `style-token-coverage.spec.ts` already guards stylesheets. Expected charges lean towards states (loading, empty, error, focus-visible, disabled), layout hierarchy and entry points rather than missing tokens.
