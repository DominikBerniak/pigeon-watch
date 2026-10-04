---
change_id: register-and-login
title: Register and login
status: implementing
created: 2026-10-04
updated: 2026-10-04
archived_at: null
---

## Notes

- External research: `external-research.md` (see "Decisions" D1-D5 — binding input for /10x-plan).
- D1 bearer tokens; D2 email + password only, display name derived from email local part (editable in S-02) — revised in plan.md: display name entered at registration (3–30, unique, no '@') to keep email-derived data private; D3 composition password rules, length below NIST; D4 no email confirmation for MVP; D5 two config endpoints instead of `/me`: anonymous `ClientConfiguration` + authorized `GeneralConfiguration` (includes current user).
- Plan revision (2026-10-04, before implementation): first real UI, so the plan now adds Angular Material 22.2 (MIT; PrimeNG 22 rejected over its key-based PrimeUI license), `--pw-*` design tokens driving the Material theme, `pw-*` common classes, a style-token coverage spec (Phase 3 item 10) and reusable `shared/ui` components (Phase 4 item 1). The rules are also in `PigeonWatch/Frontend/CLAUDE.md`.
