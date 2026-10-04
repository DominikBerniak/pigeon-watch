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
- Plan revision (2026-10-04, during Phase 2, user decision): registration never confirms a registered email. The API maps `DuplicateEmail`/`DuplicateUserName` to a generic `RegistrationFailed` (Phase 2 item 7), the resx key `auth.errors.duplicateEmail` became `auth.errors.registrationFailed`, and Phase 4's register page shows it as a form-level alert. `DuplicateDisplayName` stays specific (display names are public). Login keeps `LockedOut`. Without email confirmation (D4), a failed registration can still hint at an existing account; the rate limiter and lockout bound that.
- Convention added (2026-10-04): API interfaces live in an `Interfaces/` subfolder next to their implementations, keeping the parent namespace; enforced by `ArchitectureTests/InterfaceLocationTests.cs`. Landed as a separate refactor commit after Phase 2.
- Revision (2026-10-04, during Phase 3, user decision): the label pipe is `translate` (`TranslatePipe`, `translate.pipe.ts`), not `t`; read every `| t` in plan.md as `| translate` (Phase 4 pages and the label-coverage spec scan `'<key>' | translate`). `ResourceService.t()` keeps its name. Components use `templateUrl` with a sibling `.html` file. Async APIs return RxJS Observables, not Promises: `SessionService.whenReady()`, `login()`, `register()`, `ConfigurationService.loadClient()/loadGeneral()`, `ResourceService.load()` and the guards; `login()`/`register()` are cold, so callers subscribe. Rules recorded in `PigeonWatch/Frontend/CLAUDE.md`.
