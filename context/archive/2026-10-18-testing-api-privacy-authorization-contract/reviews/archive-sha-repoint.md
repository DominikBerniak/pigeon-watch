# Archive SHA repoint: testing-api-privacy-authorization-contract

- **Date**: 2026-10-18
- **Target**: `origin/main` in `DominikBerniak/pigeon-watch`, snapshot `fb129249d2aba7e5bf602549dcaea762919d00e0`
- **Integration commit**: `fb12924` "Test: API privacy and authorization contract (risks #2, #3, #6) (#18)"
- **Association**: https://github.com/DominikBerniak/pigeon-watch/pull/18 (merged, base `main`); its commit list contains all seven old SHAs below
- **Evidence**: the squash commit's changed-file set equals the PR's 43 files, and the `PigeonWatch/` and `context/foundation/` trees at `fb12924` equal the PR branch tip `e5fe2b1`. None of the old SHAs is an ancestor of the target (`git merge-base --is-ancestor` exit 1 for each), because the PR was squash-merged.
- **Decision**: "Update and archive" approved by the user in chat, 2026-10-18
- **Rows repointed**: 54

| Row IDs | Old suffix (resolved OID) | New SHA |
|---------|---------------------------|---------|
| 1.1, 1.2, 1.3, 1.4, 1.5 | `8009738` (`800973876e3be00148c7536cf8a04fdbc744b7ec`) | `fb12924` |
| 2.1, 2.2, 2.3, 2.4, 2.5, 2.6, 2.7, 2.8, 2.9 | `ce7085d` (`ce7085d628f91f25322c3135909b06df2f1bf849`) | `fb12924` |
| 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7, 3.8, 3.9 | `7d634e4` (`7d634e437c32745a44dbfe8f8aba993b1f1b404f`) | `fb12924` |
| 4.1, 4.2, 4.3, 4.4, 4.5, 4.6, 4.7 | `3d35f22` (`3d35f2258bb09a238458ed2e4ef3f8932abde4dd`) | `fb12924` |
| 5.1, 5.2, 5.3, 5.4, 5.5, 5.6, 5.7, 5.8, 5.9 | `bde4734` (`bde473438a2f496cc7011f8af312a2b9e7cc1b08`) | `fb12924` |
| 6.1, 6.2, 6.3, 6.4, 6.5, 6.6, 6.7, 6.8, 6.9 | `53d6510` (`53d6510d0952c0178a9c7e961f3acacf74223dfa`) | `fb12924` |
| 7.1, 7.2, 7.3, 7.4, 7.5, 7.6 | `bdf0d7f` (`bdf0d7f1f80fc307930bf9b4f2127b703331e948`) | `fb12924` |
