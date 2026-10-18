# Archive SHA repoint

## Entry 2026-10-18

- Date: 2026-10-18
- Target: origin/main, snapshot OID c5f28892dae72b4a5a65796f7ab261ef0f920c01
- Integration commit: b57b24c "Cold-start registration resilience: API integration harness and scoped recovery login (#16)" (full OID b57b24ccd7522539098f69824b420a3bbe241dd0), verified ancestor of the target snapshot
- PR: https://github.com/DominikBerniak/pigeon-watch/pull/16 (state MERGED, base main)
- Evidence: the PR commit list contains all three old SHAs; `gh pr diff 16` and `git diff b57b24c^ b57b24c` are byte-identical (25 files, +1576/-22), so the squash commit integrates all implementation covered by the rows below. The old SHAs are not ancestors of the target snapshot.
- Decision: user chose "Update and archive" in the /10x-archive confirmation.
- Affected rows: 23

| Row ID | Old suffix (resolved OID) | New SHA |
|--------|---------------------------|---------|
| 1.1 | 9adf35d (9adf35de2ce3d3d23b132227fd870d8a6a50439b) | b57b24c |
| 1.2 | 9adf35d (9adf35de2ce3d3d23b132227fd870d8a6a50439b) | b57b24c |
| 1.3 | 9adf35d (9adf35de2ce3d3d23b132227fd870d8a6a50439b) | b57b24c |
| 1.4 | 9adf35d (9adf35de2ce3d3d23b132227fd870d8a6a50439b) | b57b24c |
| 1.5 | 9adf35d (9adf35de2ce3d3d23b132227fd870d8a6a50439b) | b57b24c |
| 1.6 | 9adf35d (9adf35de2ce3d3d23b132227fd870d8a6a50439b) | b57b24c |
| 1.7 | 9adf35d (9adf35de2ce3d3d23b132227fd870d8a6a50439b) | b57b24c |
| 1.8 | 9adf35d (9adf35de2ce3d3d23b132227fd870d8a6a50439b) | b57b24c |
| 2.1 | 53a04cb (53a04cb7005fb24db4b6ab1072344d421311d390) | b57b24c |
| 2.2 | 53a04cb (53a04cb7005fb24db4b6ab1072344d421311d390) | b57b24c |
| 2.3 | 53a04cb (53a04cb7005fb24db4b6ab1072344d421311d390) | b57b24c |
| 2.4 | 53a04cb (53a04cb7005fb24db4b6ab1072344d421311d390) | b57b24c |
| 2.5 | 53a04cb (53a04cb7005fb24db4b6ab1072344d421311d390) | b57b24c |
| 2.6 | 53a04cb (53a04cb7005fb24db4b6ab1072344d421311d390) | b57b24c |
| 2.7 | 53a04cb (53a04cb7005fb24db4b6ab1072344d421311d390) | b57b24c |
| 3.1 | 2ee6dc5 (2ee6dc55855cc81e7c75b49268ed6f81b0d80bb2) | b57b24c |
| 3.2 | 2ee6dc5 (2ee6dc55855cc81e7c75b49268ed6f81b0d80bb2) | b57b24c |
| 3.3 | 2ee6dc5 (2ee6dc55855cc81e7c75b49268ed6f81b0d80bb2) | b57b24c |
| 3.4 | 2ee6dc5 (2ee6dc55855cc81e7c75b49268ed6f81b0d80bb2) | b57b24c |
| 3.5 | 2ee6dc5 (2ee6dc55855cc81e7c75b49268ed6f81b0d80bb2) | b57b24c |
| 3.6 | 2ee6dc5 (2ee6dc55855cc81e7c75b49268ed6f81b0d80bb2) | b57b24c |
| 3.7 | 2ee6dc5 (2ee6dc55855cc81e7c75b49268ed6f81b0d80bb2) | b57b24c |
| 3.8 | 2ee6dc5 (2ee6dc55855cc81e7c75b49268ed6f81b0d80bb2) | b57b24c |
