# Archive SHA repoint

## 2026-10-11

- **Target**: `origin/main` (base branch of the implementation PR), snapshot `3ba50582cead3f47ce50a30d60438ff84412deac`
- **Integration commit**: `3ba5058` "Report a sighting and see it on the map (S-03) (#10)"
- **PR**: https://github.com/DominikBerniak/pigeon-watch/pull/10 (squash-merged into `main`)
- **Evidence**:
  - The PR's commit list contains every old SHA: 1b75b8f, d771c16, 5ae461e, ccf30ff, 2700afe, fa8b75e, bdadba3, 1ea65bc.
  - The tree of `3ba5058` is identical to the branch tip `1ea65bc` (`git diff 1ea65bc 3ba5058` is empty).
  - Each old SHA resolves locally and is not an ancestor of the target (exit 1).
  - `3ba5058` is an ancestor of the target (exit 0).
- **Scope**: all five phases of the plan (Phases 1-5 Progress rows).
- **Decision**: the user chose "Update and archive" in `/10x-archive`.
- **Note**: row 4.9 has text after its SHA (the impl-review F6 note). Only its SHA token was replaced.

| Row ID | Old suffix (resolved OID) | New SHA |
| --- | --- | --- |
| 1.1 | d771c16 (d771c16f0e64d4b5fed2e83945fe1fe6b85c0e37) | 3ba5058 |
| 1.2 | d771c16 (d771c16f0e64d4b5fed2e83945fe1fe6b85c0e37) | 3ba5058 |
| 1.3 | d771c16 (d771c16f0e64d4b5fed2e83945fe1fe6b85c0e37) | 3ba5058 |
| 1.4 | d771c16 (d771c16f0e64d4b5fed2e83945fe1fe6b85c0e37) | 3ba5058 |
| 1.5 | d771c16 (d771c16f0e64d4b5fed2e83945fe1fe6b85c0e37) | 3ba5058 |
| 2.1 | 5ae461e (5ae461e8a45da0307d012e5210c8ab882ae13924) | 3ba5058 |
| 2.2 | 5ae461e (5ae461e8a45da0307d012e5210c8ab882ae13924) | 3ba5058 |
| 2.3 | 5ae461e (5ae461e8a45da0307d012e5210c8ab882ae13924) | 3ba5058 |
| 2.4 | 5ae461e (5ae461e8a45da0307d012e5210c8ab882ae13924) | 3ba5058 |
| 2.5 | 5ae461e (5ae461e8a45da0307d012e5210c8ab882ae13924) | 3ba5058 |
| 3.1 | ccf30ff (ccf30ffb36ed51c31181270b31f4a66ba0168e01) | 3ba5058 |
| 3.2 | ccf30ff (ccf30ffb36ed51c31181270b31f4a66ba0168e01) | 3ba5058 |
| 3.3 | ccf30ff (ccf30ffb36ed51c31181270b31f4a66ba0168e01) | 3ba5058 |
| 3.4 | ccf30ff (ccf30ffb36ed51c31181270b31f4a66ba0168e01) | 3ba5058 |
| 3.5 | ccf30ff (ccf30ffb36ed51c31181270b31f4a66ba0168e01) | 3ba5058 |
| 4.1 | 2700afe (2700afee946c2d9d67db54b991c988778549b093) | 3ba5058 |
| 4.2 | 2700afe (2700afee946c2d9d67db54b991c988778549b093) | 3ba5058 |
| 4.3 | 2700afe (2700afee946c2d9d67db54b991c988778549b093) | 3ba5058 |
| 4.4 | 2700afe (2700afee946c2d9d67db54b991c988778549b093) | 3ba5058 |
| 4.5 | 2700afe (2700afee946c2d9d67db54b991c988778549b093) | 3ba5058 |
| 4.6 | 2700afe (2700afee946c2d9d67db54b991c988778549b093) | 3ba5058 |
| 4.7 | 2700afe (2700afee946c2d9d67db54b991c988778549b093) | 3ba5058 |
| 4.8 | 2700afe (2700afee946c2d9d67db54b991c988778549b093) | 3ba5058 |
| 4.9 | 2700afe (2700afee946c2d9d67db54b991c988778549b093) | 3ba5058 |
| 5.1 | fa8b75e (fa8b75e554bf29a29fbb297c0ca86b56808f5576) | 3ba5058 |
| 5.2 | fa8b75e (fa8b75e554bf29a29fbb297c0ca86b56808f5576) | 3ba5058 |
| 5.3 | fa8b75e (fa8b75e554bf29a29fbb297c0ca86b56808f5576) | 3ba5058 |
| 5.4 | fa8b75e (fa8b75e554bf29a29fbb297c0ca86b56808f5576) | 3ba5058 |
| 5.5 | fa8b75e (fa8b75e554bf29a29fbb297c0ca86b56808f5576) | 3ba5058 |
| 5.6 | fa8b75e (fa8b75e554bf29a29fbb297c0ca86b56808f5576) | 3ba5058 |

**Affected rows: 30.**
