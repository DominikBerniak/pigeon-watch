---
change_id: spa-error-reporting-seam
title: Spa error reporting seam
status: implemented
created: 2026-10-27
updated: 2026-10-30
archived_at: null
---

## Notes

<!-- Free-form notes for this change: links, ad-hoc context, decisions that don't belong in research/frame/plan. -->
- Phase 3 step 3.5 (manual): not runnable in the browser. Clearing the refresh token in local storage fires the cross-tab storage logout in SessionService (no 401, no report), and the API Data Protection keys persist across restarts so a restart does not invalidate tokens. Accepted on the session-expired-reporting specs, which cover both the missing and the rejected refresh path.
