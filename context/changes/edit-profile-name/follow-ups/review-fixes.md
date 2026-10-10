# Review Follow-ups: edit-profile-name

Queued from `reviews/impl-review.md` triage on 2026-10-10.

## F6 — Current-password guessing is limited only per IP (accepted for MVP)

- **Source**: impl-review F6 (Safety & Quality, OBSERVATION)
- **Location**: `PigeonWatch/Api/Data/Repositories/AccountRepository.cs:69`, `PigeonWatch/Frontend/src/app/core/auth/session.service.ts` (`changePassword`)
- **Risk**: `UserManager.ChangePasswordAsync` does not increment `AccessFailedCount` on `PasswordMismatch`, so lockout never applies. With a stolen bearer token an attacker can guess the current password at 10 attempts per minute per IP (the shared `Auth` policy), more from several IPs. The follow-up `auth/login` shares that per-IP bucket, so a successful change after a few wrong attempts can hit 429 on re-login and log the user out (handled: `ReLoginError` -> `/login` with a notice).
- **Options when revisited**: partition the password endpoint's limiter by user id; or call `AccessFailedAsync` on mismatch (weigh the self-DoS: lockout also blocks the real user's login); exempt or separately budget the follow-up re-login.
- **Check first**: configured access-token lifetime and whether Identity lockout is enabled.
