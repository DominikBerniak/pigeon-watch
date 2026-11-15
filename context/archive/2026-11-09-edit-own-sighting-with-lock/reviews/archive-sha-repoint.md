# Archive SHA repoint

## 2026-11-15

- **Target**: `origin/main` (github.com/DominikBerniak/pigeon-watch), snapshot `172968df98aa3830ca8b00821bc53c45e38889fc`.
- **Integration commit**: `172968d` "Edit own sighting with engagement lock (S-09) (#38)". This is the squash merge of https://github.com/DominikBerniak/pigeon-watch/pull/38, merged 2026-11-15T12:02:35Z.
- **Evidence**: the PR commit list is `6899cbc`, `2cbaa07`, `982d296`, `90ced99`, `5f17145`, `4148eb3`. `git diff --quiet 4148eb3 172968d` exits 0, so the squash tree is identical to the PR head. `172968d` is the only commit on `origin/main` after `419ef4f`. None of the old SHAs is an ancestor of the target, and `172968d` is.
- **Scope**: the whole implementation of the change, phases 1–4 plus the implementation-review fixes.
- **Decision**: the user approved "Update and archive" on 2026-11-15.

| Row ID | Old suffix (resolved OID) | New SHA |
| --- | --- | --- |
| 1.1–1.5 | `6899cbc` (6899cbc92d57e0ebc75fa85792c575825b1e1a54) | `172968d` |
| 2.1–2.6 | `2cbaa07` (2cbaa078810912d079eae8dc5651409ff3efa92f) | `172968d` |
| 3.1–3.7 | `982d296` (982d296c5788859a9cb3549a487d803a71aa6300) | `172968d` |
| 4.1–4.3 | `90ced99` (90ced99691ca3fcf99a9a00860868132cba1bf78) | `172968d` |

Affected rows: 21.
