# Archive SHA repoint: register-and-login

## 2026-10-05

- **Target**: `origin/main`, snapshot `f38cc14116ad2b1f121fffa8939a6f45f50f8da2`
- **Integration commit**: `f524feae7bdd9a7879bc135d916ba1a65454d709` — S-01: Register and log in (Identity bearer auth, SPA pages, warm-up panel) (#6)
- **PR**: https://github.com/DominikBerniak/pigeon-watch/pull/6 (base `main`, squash-merged)
- **Evidence**:
  - The PR commit list contains all five old SHAs.
  - The tree of `f524fea` is identical to the PR head `fc118fb`.
  - None of the old SHAs is an ancestor of the target snapshot; `f524fea` is.
- **Scope**: the full S-01 implementation (Phases 1–4) plus the merge of `origin/main` into the feature branch.
- **Decision**: the user chose "Update and archive" during `/10x-archive register-and-login`.

| Row ID | Old suffix (resolved OID) | New SHA |
|---|---|---|
| 1.1–1.10 | `6bf182a` (`6bf182a04624bac68dfe43cb977609bb767c8c93`) | `f524fea` |
| 2.1–2.7, 2.14, 2.16, 2.17 | `5273c29` (`5273c29d536ebd654106d2a597b9d056583b10b1`) | `f524fea` |
| 3.1–3.8 | `31c62ba` (`31c62bada5d66ca236b75c32618e806d435b3964`) | `f524fea` |
| 4.1, 4.2, 4.4, 4.5, 4.6, 4.10, 4.12, 4.13 | `6e96dd8` (`6e96dd8cde8b412ccbddc6041eff4e19111e07de`) | `f524fea` |
| 4.3 | `34c3f4a` (`34c3f4a5197dc29068e02b3b48764b55c2bde336`) | `f524fea` |

Rows repointed: 37. The 11 rows already ending in `f524fea` (live checks) were unchanged.
