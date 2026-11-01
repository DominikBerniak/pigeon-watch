# Archive SHA repoint

## 2026-11-01

- **Target**: `origin/main` (`DominikBerniak/pigeon-watch`), snapshot `d3f10b157053a934a3f17b06a785cd3b7587b853`
- **Integration commit**: `d3f10b1` "Urgency-ranked sighting list (S-06) (#29)"
- **PR**: https://github.com/DominikBerniak/pigeon-watch/pull/29 (merged by squash, base `main`)
- **Evidence**: the PR commit list contains all five phase SHAs. The squash commit's parent is the PR base `0bc71b1` and its tree is identical to the PR head `f6f9370` (`git diff f6f9370 d3f10b1` is empty). Both `0bc71b1..f6f9370` and `d3f10b1^..d3f10b1` change the same 91 files (+4606 −178). None of the phase SHAs is an ancestor of the target snapshot (`git merge-base --is-ancestor` exit 1, full clone).
- **Decision**: the user chose "Update and archive" in `/10x-archive`.
- **Rows repointed**: 24. Rows 4.2 and 4.6 also carry a review note after the SHA; only the SHA was replaced.

| Row ID | Old suffix (resolved OID) | New SHA |
|---|---|---|
| 1.1–1.5 | `4638951` (`4638951991f972c2d8867ba60d96510ff65b0d91`, feat … API data … (p1)) | `d3f10b1` |
| 2.1–2.6 | `a45e796` (`a45e796fe90bf3be17d304bce8bb974733e02710`, feat … API endpoint … (p2)) | `d3f10b1` |
| 3.1–3.4 | `340ef79` (`340ef79e226840dfbf1f9fac12258c8c97277302`, refactor … SPA shared components … (p3)) | `d3f10b1` |
| 4.1–4.7 | `d89abf2` (`d89abf27152ebcdea77e7e2fd69d12e0d02c9e15`, feat … SPA list page (p4)) | `d3f10b1` |
| 5.1–5.2 | `eec602e` (`eec602eb0cc5d31ab5aed0b60d720ad9f08ca2eb`, test … E2E list opened from the map (p5)) | `d3f10b1` |
