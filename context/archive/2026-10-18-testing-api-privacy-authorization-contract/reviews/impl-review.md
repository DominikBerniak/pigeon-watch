<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: API Privacy and Authorization Contract Tests

- **Plan**: context/changes/testing-api-privacy-authorization-contract/plan.md
- **Scope**: Full plan (7 of 7 phases)
- **Reviewed phases**: 1, 2, 3, 4, 5, 6, 7
- **Date**: 2026-10-18
- **Verdict**: NEEDS ATTENTION
- **Findings**: 0 critical, 4 warnings, 6 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | WARNING |
| Scope Discipline | PASS |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | PASS |
| Success Criteria | PASS |

Automated verification re-run at review time: solution build 0 errors; integration 59, unit 192, architecture 42, frontend 533 tests all pass. Rule 7.4 (no `w/lf` among touched files) holds for every file this change touched except `change.md`, see F10.

## Findings

### F1 — Signed-in sweeps cannot detect that the token stopped working

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: PigeonWatch/Api/IntegrationTests/PrivacySweep.cs:29
- **Detail**: The sweep fails on 429 and on 5xx but never on 401/403 for a request that carries a bearer token. If the token stopped validating (clock mismatch, Data Protection key change, header not applied), every protected endpoint would answer 401 with an empty body and both signed-in sweeps would pass while testing nothing. The only positive control is a separate test in a different factory.
- **Fix**: When the caller has a token, fail on a 401 for any endpoint that requires authorization, and/or assert at least one 200 inside the sweep run.
  - Strength: Closes the vacuous-pass hole in the highest-signal Risk #2 test; a few lines in `PrivacySweep`.
  - Tradeoff: The stub `auth/manage/*` routes answer 404 when signed in, so the check must target only real protected endpoints.
  - Confidence: HIGH — grep confirms `PrivacySweep.cs` contains no 401 check at all.
  - Blind spot: Whether any protected fixture legitimately answers 401 to a signed-in caller.
- **Decision**: FIXED via Fix now — PrivacySweep fails a signed-in caller's 401 on any protected endpoint; deliberate bad-token mutation turned 2 of 4 sweeps red, then restored

### F2 — Leak scanner covers full-address forms only

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: PigeonWatch/Api/IntegrationTests/ResponseLeakScanner.cs
- **Detail**: Needles are the raw, upper-case and two URL-encoded forms of the whole email. It misses base64, HTML entities, double percent-encoding, the local part or domain alone, and fields that split them; trailing headers are not scanned; `\uXXXX` decoding only happens when the body parses as JSON.
- **Fix A ⭐ Recommended**: Document the limit in test-plan §6.4/§7 and add one cheap needle (the local part, when long enough to be unique).
  - Strength: Costs almost nothing and states honestly what "no email leak" proves; matches the plan's "decoded text, not a field whitelist" intent.
  - Tradeoff: Still does not catch base64 or split fields.
  - Confidence: MED — a local-part needle could false-positive on short names.
  - Blind spot: Whether any response legitimately contains the seeded local part (for example in a display name).
- **Fix B**: Also add a base64 needle and scan trailing headers.
  - Strength: Covers the next most likely encodings.
  - Tradeoff: More scanner code and self-tests for encodings no endpoint produces today.
  - Confidence: MED — speculative value.
  - Blind spot: None significant.
- **Decision**: FIXED via Fix A — scanner adds a local-part needle (8+ chars) with two self-tests; limits documented in test-plan §6.4 and §7; integration suite 61 pass

### F3 — test-plan §4 still says the API integration harness does not exist

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: context/foundation/test-plan.md:95-104
- **Detail**: The test-base profile says "No API integration harness, e2e, lint step or accessibility tooling yet" and the stack table row reads "none yet — see Phase 1 / Phase 1 chooses and bootstraps the harness". The harness shipped in Phase 1 and is documented in §6.3. The file counts (43 spec files, 21 xUnit files) are also out of date (now 44 and 24). `/10x-tdd` reads this file next.
- **Fix**: Update the profile sentence and the stack row (xUnit v3 on `WebApplicationFactory<Program>` with SQLite in-memory) and refresh the counts.
- **Decision**: FIXED via Fix now — test-plan §4 profile sentence, stack row and counts (44 spec, 24 xUnit, 9 integration classes) updated

### F4 — §6.4 says three `Sweep_as_*` tests, four match the filter

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: context/foundation/test-plan.md (§6.4, "Three guards" bullet and the `~Sweep_as_` filter hint)
- **Detail**: `Sweep_as_bob_with_a_closed_database_finds_no_email_in_the_server_error_response` also matches `~Sweep_as_`, so the filter runs four tests and "the three `Sweep_as_*` tests" is inaccurate.
- **Fix**: Reword to "the anonymous, Bob and Alice sweeps (and the closed-database variant)".
- **Decision**: FIXED via Fix now — §6.4 guard bullet reworded: three sweeps plus the closed-database variant that also matches the filter; F1's 401 rule added to the description

### F5 — An exemption can mask a protected endpoint only by accident of another test

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: PigeonWatch/Api/IntegrationTests/EndpointCoverageTests.cs; EndpointFixtureRegistry.cs
- **Detail**: `Exemptions` is empty. A future exemption of a protected endpoint would drop it from the sweep and the coverage guard and is caught only because `BuildRequests` throws in the anonymous-calls test. The `anonymousEndpoints` list and `registry.Exemptions` share a record type and are easy to confuse.
- **Fix**: Add an assertion that no exemption key is a protected endpoint.
- **Decision**: FIXED via Fix now — Every_exemption_is_an_unprotected_endpoint added to EndpointCoverageTests; mutation (exempting GET sightings/nearby) turned it red, registry restored; integration suite 62 pass

### F6 — Alice's sweep never sees a 201 from `POST sightings`

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: PigeonWatch/Api/IntegrationTests/PrivacySweepTests.cs (world seeding); EndpointFixtureRegistry.cs
- **Detail**: Alice already has one sighting from seeding, so the "valid sighting" fixture hits the 60 s cooldown and answers 400. Bob's sweep does get a 201. Not vacuous for leaks, but weaker than intended for Alice.
- **Fix**: Advance the `ManualTimeProvider` past 60 s before Alice's sweep, or accept as is.
- **Decision**: FIXED via Fix now — PrivacySweepTests keeps the ManualTimeProvider in a field and advances it 2 min before Alice's sweep; a throwaway probe confirmed both signed-in sweeps now get 201 from POST sightings (probe removed)

### F7 — Frontend recovery rule now also fires for a taken display name

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: PigeonWatch/Frontend/CLAUDE.md:52
- **Detail**: A taken display name is now `RegistrationFailed`, so a resent register with a taken name makes one extra recovery login with the typed credentials (a 401, or one lockout-counter increment on an existing account). Harmless in practice but not documented. The register form also gives no actionable hint for a taken name; that is the intended trade-off.
- **Fix**: Add one sentence to the Frontend doc, or accept.
- **Decision**: FIXED via Fix now — one sentence added to PigeonWatch/Frontend/CLAUDE.md recovery rule

### F8 — Small test helpers are copied between classes

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: RegisterContractTests.cs:114, EnumerationContractTests.cs:266 and siblings
- **Detail**: `ReadBodyWithoutTraceIdAsync`, error-code reading, detail reading and `SeedUserAsync` are re-implemented per class. Also minor: undisposed response messages in `CreateUserAsync`, a swallowed `JsonException` in the scanner, a 429 test that asserts an empty body (couples to the framework default), and an obscure `(char)92` literal in the scanner self-test.
- **Fix**: Move the shared readers to `AccountApiClient` when a next test class needs them.
- **Decision**: FIXED via Fix now — ReadBodyWithoutTraceIdAsync and ReadDetailAsync moved to AccountApiClient (static), three private copies and two unused usings removed, scanner self-test literal simplified; SeedUserAsync, the swallowed JsonException, undisposed responses and the empty-429-body assertion left as is; integration suite 62 pass

### F9 — §3 Phase 2 row still reads `change opened`

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: context/foundation/test-plan.md:77
- **Detail**: Intentional per the plan ("mark the §3 Phase 2 row `complete` through the orchestrator" after human confirmation). The human has confirmed Phase 7, so it can now move.
- **Fix**: Set the row to `complete` and add a §8 ledger line (via `/10x-test-plan`).
- **Decision**: FIXED via Fix now — §3 Phase 2 row set to complete and §8 ledger line added

### F10 — `change.md` is LF in the working copy

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Success Criteria
- **Location**: context/changes/testing-api-privacy-authorization-contract/change.md
- **Detail**: `git ls-files --eol` shows `w/lf`; every other touched file is `w/crlf`. It was already LF before this phase and git warns it will convert it on the next touch.
- **Fix**: Convert to CRLF in the next commit that touches it.
- **Decision**: FIXED via Fix now — change.md converted to CRLF
