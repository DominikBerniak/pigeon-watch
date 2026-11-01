---
type: observability-audit
date: 2026-10-30 18:10
mode: verify
commit: c56a3aa
branch: main
dirty_tree: false
areas: [auth, report-browse-sightings]
area_source: arguments
runtime_proof: not-run
error_tracker: none (ILogger to stdout only; App Service log stream assumed)
previous_report: context/audits/observability/2026-10-26_verify-auth-report-browse-sightings.md
findings: { critical: 0, high: 0, medium: 2, low: 3 }
---

# Observability audit (verify): auth, report/browse sightings (2026-10-30)

Verify run of `2026-10-26_verify-auth-report-browse-sightings.md` after the `spa-error-reporting-seam` change (PR #26, squash commit `f38669f`) landed on `main`. Every open finding of that report was re-checked by re-reading the cited code on `c56a3aa`. No probe harness exists, so there is no scripted before/after suite; evidence is the code plus the automated tests that passed during the change (see section 8).

## 1. TL;DR

- **All 4 remaining high findings are closed.** A5, A6, S1 and P4 are fixed by one SPA reporting seam: `ErrorReportingService`, a global `ErrorHandler`, a router navigation error handler, a DI-free bootstrap beacon and an anonymous `POST client-errors` endpoint that logs one allowlisted line per report.
- **Fixed in total: A5, A6, A10, A11, S1, S4, S5, S6, P4 (9 findings).** Changed: P1 residual and P2. Still open: A13, P5, P6 (the separate "polish" step, untouched on purpose).
- **P1 residual and P2 are only partly closed.** The exhausted warm-up budget is now reported once with its last status (0 or 503), so 503 and status 0 are distinguishable. The retry behaviour is unchanged by design (about 12 identical Error lines per user in a non-cold-start outage, status 0 retried for 120 s), and a status-0 report cannot reach an unreachable API.
- **A user can now quote a reference.** The generic unexpected-error states render `Reference: <traceId>` when the failed response carried one, and that id matches the `TraceId` scope of the API log line.
- **Most consequential scenario now:** a deploy that breaks the SPA bundle (chunk load, bootstrap, swallowed API error) leaves a Warning line (`navigation`, `bootstrap`, `uncaught`, `api-failure`) in the API log stream. It stays invisible when the API itself is unreachable or when production CORS does not admit the SPA origin (assumption).

## 2. Capture model

Changed since the previous report:

- **Reporter (`Frontend/src/app/core/error-reporting/error-reporting.service.ts`):** builds a closed allowlist payload (`kind`, `context`, `httpStatus`, `errorName`, `traceId`, `route` as a template, `sessionId`), dedupes on kind, context, status and error name, caps at 20 reports per page load, sends with `SKIP_AUTH` and `SKIP_WARMUP`, and turns a failed send into one `console.warn`.
- **Global handlers:** `app.config.ts` registers `AppErrorHandler` (console.error, then `uncaught`) and `withNavigationErrorHandler(reportNavigationError)`; `main.ts` calls `reportBootstrapFailure`, a `navigator.sendBeacon` post that needs no DI.
- **Server (`Api/WebApi/Controllers/ClientErrorsController.cs`):** anonymous, 2 KB cap, `clientErrors` rate limit of 20 per minute per client IP (`Program.cs:76-84`), regex-constrained fields, level chosen by the server from a closed `kind` map (Warning, or Information for `fallback`, never Error). The request `TraceId` stays in the scope.
- **Unchanged:** no error tracker, no deploy identity in logs, `UiResourceService` culture fallback.

**Assumptions (outside the repo, not findings), still unconfirmed:** App Service collects stdout; no alert rules were seen; production CORS admits the production SPA origin (without it browsers block every report); the host forwards the client IP (otherwise all clients share one 20 per minute bucket); the Data Protection key ring survives restarts.

## 3. What reaches the logs now

Status labels: **tested** = asserted by integration, unit or Vitest specs that passed on the change; **inferred** = from reading the code.

| Failure shape | Response | Platform logs (stdout) | Verdict | Basis |
|---|---|---|---|---|
| Uncaught SPA error | console | `uncaught` Warning (first error per name and context per page load) | good | tested |
| Lazy chunk load or navigation error | router default | `navigation` Warning | good | tested |
| Bootstrap failure | blank page | `bootstrap` Warning through the beacon | good | tested (beacon unit spec) |
| API failure shown as unexpected UI | generic state plus reference | `api-failure` Warning with status and client trace id | good | tested (page specs, e2e) |
| Expected user condition (400 validation, login 401, 404, 429, cooldown) | explained in UI | nothing, no reference | by design | tested |
| Forced logout (refresh rejected or missing), restore failure | session cleared | `session-expired` Warning with status and trace id | good | tested |
| Config, labels, storage, geolocation, map import, tile error | fallback | `fallback` Information | good | tested |
| Warm-up budget exhausted with 503 | UI flag | `warmup-exhausted` Warning with status 503 | acceptable | tested |
| Warm-up budget exhausted with status 0 | UI flag | usually nothing: the API is unreachable, so the report fails to send | residual | inferred |
| Client report endpoint abuse | 429 or 400 | existing rate-limit Warning, or nothing for a rejected body | good | integration-tested |

## 4. Systemic root causes (status)

1. Result-style failures converted to responses and never logged: **fixed** (previous report).
2. Log configuration and lack of correlation: **fixed** except version stamping (P5).
3. One broad "database unavailable" classifier plus an SPA that retries it: **partly fixed.** The SPA now reports exhaustion with its status; the retry loop and the unreachable-API blind spot remain.
4. The SPA swallows failures at the edge and has no reporting seam: **fixed.** Every swallow site listed in the audit now goes through `ErrorReportingService` or is rendered with a reference.
5. No auth security events: **fixed** (previous report).

## 5. Findings by area (current state)

Only open and partly fixed findings are listed. Fixed findings are in section 7.

### Auth

| # | Location | Category | Severity | What happens in production | Fix direction |
|---|---|---|---|---|---|
| A13 | `Api/BusinessLogic/Services/UiResourceService.cs:37` | logged-not-captured | low | Culture fallback is a bare catch; a missing embedded resource is visible only in the framework 500 log. | Log explicitly at first use. |

### Platform / plumbing

| # | Location | Category | Severity | What happens in production | Fix direction |
|---|---|---|---|---|---|
| P1 (residual) | `warmup.interceptor.ts:58-66,89-97` | flattened-response | medium | Exhaustion is now reported once, but a non-cold-start 503 outage is still retried every 10 s for 120 s, so about 12 identical Error lines per user. | Back off the retry for non-cold-start 503s, or accept it. |
| P2 | `warmup.interceptor.ts:89-91` | flattened-response | medium | Status 0 is still retried for 120 s. The final failure is reported with status 0, but the report cannot reach an unreachable API. | Accept (expected), or distinguish an offline browser from an API outage. |
| P5 | `Api/WebApi.Host/Program.cs`, `PigeonWatch.WebApi.Host.csproj` | config | low | Still no version, commit or environment in logs or responses (only auto-generated assembly info). | Log the informational version at startup. |
| P6 | `.github/workflows/deploy-api.yml:148` | coverage-gap | low | The only probe still never touches the database (documented free-tier decision). | Optional second probe. |

## 6. Recommended fix order (remaining)

1. **Polish (P5, P6, A13).** Version stamp at startup so a report can be tied to a deploy; optional second probe; first-use log for the label resource.
2. **Optional: warm-up retry back-off (P1 residual, P2).** Only if the duplicated Error lines or the 120 s status-0 retry become a real cost.

## 7. Changes since last audit

| Previous id | Status | Evidence |
|---|---|---|
| A5 | fixed | `auth.interceptor.ts:28,37` report `session-expired` with status and trace id before `session.expire`; `session.service.ts:216` reports a non-session restore failure. Retry and navigation unchanged. |
| A6 | fixed | `unexpectedTraceId` in `shared/http/problem-details.ts` feeds an `app-error-reference` shown in the unexpected states of the nearby map, My sightings, detail, report form, profile, password dialog and the auth forms. Absent for status 0 and for expected statuses. |
| A10 | fixed | `session.service.ts:70` (config), `resource.service.ts:72,90` (labels, snapshot), `profile-page.ts:188` (reload) report `fallback`. |
| A11 | fixed | `token-store.ts:61` and `culture-store.ts:46` report `storage.token` and `storage.culture`, once per session through the dedupe, no values. |
| S1 | fixed | `api-failure` reports at `nearby-map-page.ts:269`, `my-sightings.ts:77`, `sighting-detail-page.ts:193`, `report-sighting-page.ts:177`; expected statuses are filtered by `isExpectedClientStatus`. |
| S4 | fixed | `nearby-map-page.ts` sets `sightings` only for a `loaded` state (around line 262), so a failed refresh keeps the previous markers and still shows the error. |
| S5 | fixed | `geolocation.service.ts:49` reports `geolocation.exception` with the error name only. |
| S6 | fixed | `map-view.ts:141` reports `map.import.failed`; `map-view.ts:186` reports the first tile error once. |
| P4 | fixed | `app.config.ts` (`AppErrorHandler`, `withNavigationErrorHandler`), `main.ts` and `bootstrap-report.ts`. |
| P1 residual | changed | `warmup.interceptor.ts:63` reports exhaustion once; retry timing unchanged. |
| P2 | changed | Status 0 and 503 are now distinguishable in the report; status 0 is still retried and its report is expected to fail to send. |
| A13, P5, P6 | still open | No change to `UiResourceService.cs`, version stamping or the deploy probe. |
| A1-A4, A7, A8, A12, S2, S3, S7, S8, P3 | not re-checked | Fixed in the previous report; no server logging code changed except the new endpoint. |

Totals of the 13 open findings in the previous report: 9 fixed (A5, A6, A10, A11, S1, S4, S5, S6, P4), 2 changed (P1 residual, P2), 3 still open (A13, P5, P6). Open severities (frontmatter, including the two changed): medium 2 (P1 residual, P2), low 3 (A13, P5, P6).

New observations (not findings):

- Client-supplied strings reach the log only through regex-constrained fields (`ClientErrorReportModel`), so the log-forging concern from the previous report does not extend to the new endpoint.
- Rate limiting keys on `RemoteIpAddress`; without forwarded-header handling a proxy may collapse all clients into one 20 per minute bucket. The change excluded this on purpose; it stays an unconfirmed hosting assumption (impl review F5, skipped).
- The implementation review found and fixed one weakness: the dedupe key ignored the error name, so only the first uncaught error per page load was reported. Fixed in `4932669`.

## 8. Method and limits

- **Agents run:** none. Verify run done by reading the cited code on `c56a3aa` and the implementation commits (`5b851a7`, `09edf30`, `f3a54ed`, `7bd0bef`, `4e9e54b`, review fixes `4932669`, squash `f38669f`). The change was separately reviewed (`context/archive/2026-10-27-spa-error-reporting-seam/reviews/impl-review.md`).
- **Runtime proof: not-run in this audit.** No probe harness exists. During the implementation review the full suites passed: .NET unit 230, architecture 42, integration 124; frontend 652 specs; production build within budgets. The plan's manual steps 1.6, 2.5, 2.6, 3.6, 4.6 and 4.7 were recorded as done by the implementer; step 3.5 was accepted on specs (see the archived `change.md`). This audit did not re-run the e2e test or a browser session.
- **Verified by hand:** every swallow site and report call above by grep and reading; the `warmup.interceptor.ts` retry logic; `nearby-map-page.ts` marker handling; absence of changes to `UiResourceService.cs` and version stamping.
- **Not verified:** how App Service renders stdout, log retention, whether anyone reads the logs, live CORS for the production SPA origin, forwarded-IP behaviour, and whether the bundle budget holds on the deployed build.
- **No harness files or worktrees were created.**
