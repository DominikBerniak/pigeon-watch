---
change_id: testing-api-privacy-authorization-contract
title: API privacy and authorization contract tests
status: impl_reviewed
created: 2026-10-18
updated: 2026-10-18
archived_at: null
---

## Notes

Open a change folder for rollout Phase 2 of context/foundation/test-plan.md: "API privacy and authorization contract".
Risks covered: #2, #3, #6. Test types planned: integration (contract).
Risk response intent:
- #2: prove no response from any user-reachable endpoint carries another user's email, and the check extends to every new endpoint; challenge "display name rejects @, so it is safe".
- #3: prove user B cannot read or modify user A's records and that old tokens stop working after logout or a password change once the access token expires; challenge "authenticated means authorized".
- #6: prove registration and login responses are indistinguishable for existing and new emails and that the rate limit and lockout actually fire; challenge "the generic error message means no leak".
Reuse the API integration harness from Phase 1 (test-plan.md §6.3).
After creating the folder, follow the downstream continuation rule.
