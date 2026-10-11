# Archive SHA repoint

## 2026-10-11

- **Target**: `origin/main` (base branch of PR #9), snapshot `5b8c307c3d35f8af458cc6540116352b674c96f7`
- **Integration commit**: `5b8c307` — Profile UI contract: shared form actions, single notice, kitchen sink and token guard (#9)
- **PR**: https://github.com/DominikBerniak/pigeon-watch/pull/9 (merged by squash)
- **Evidence**: PR #9's commit list contains `aa482fe`, `206d30a`, `2dc22de` and `3cf9873`. None of them is an ancestor of the target snapshot (`git merge-base --is-ancestor` exit 1, full history). The squash commit's tree is identical to the PR head `c56fe99`, so it integrates all four phases' code, including the kitchen sink, shared UI components, Profile page and dialog, and token-coverage spec. The squash also carries `8bef79c` (the edit-profile-name archive), which no Progress row references.
- **Decision**: user chose "Update and archive" during `/10x-archive`.
- **Rows repointed**: 25. Row 4.9 has no SHA suffix and was left unchanged.

| Row ID | Old suffix (resolved OID) | New SHA |
|---|---|---|
| 1.1–1.6 | `aa482fe` (`aa482fe20e15e973cd95fe93269a1593630608c9`) | `5b8c307` |
| 2.1–2.6 | `206d30a` (`206d30acdbc43c80799ccf6e1144c878d9fd7d88`) | `5b8c307` |
| 3.1–3.5 | `2dc22de` (`2dc22de1ad8cdc5370a02b2b0b73d628af09946a`) | `5b8c307` |
| 4.1–4.8 | `3cf9873` (`3cf98733dbfaf392138fb568cc70604586157465`) | `5b8c307` |
