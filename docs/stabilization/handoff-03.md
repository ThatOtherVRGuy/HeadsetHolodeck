# Work order 03 — Protect saves

Completed 2026-09-13 (America/Los_Angeles) in the current local checkout. Scope: order 03 only.
**32/32 save EditMode tests, 19/19 batch regression entry points, 14/14 manager transitions, 9/9
loader transitions, and 9/9 restore/autosave transitions pass**, with Unity exit 0 for every final
run. No commit, push, credential change, scene change, or later work order.

## Outcome

A save can no longer destroy the last valid one. `WorldConfigStore` now writes every `world.json`
through one journaled protocol instead of `File.WriteAllText`:

1. serialize the config and reject any document the loader would not read back;
2. open the per-folder writer lock (`.world.json.lock`, `FileShare.None`, 5 s timeout) so two
   processes cannot interleave bytes into one file;
3. delete temporaries abandoned by a killed process;
4. write the new document to `world.json.<guid>.tmp`, flush it to disk;
5. rotate the **previous committed** document to `world.json.bak` by atomic rename;
6. rename the temporary over `world.json` (atomic on POSIX, `File.Replace` elsewhere);
7. remove the temporaries, including on any failure.

Reads try the primary and fall back to one generation earlier in `world.json.bak`. A torn, empty,
missing, or unparseable primary therefore still yields the last save; recovery logs a warning, keeps
both files, and never rewrites a document it could not trust. When neither candidate is a valid
save the read throws `IOException` naming both paths and states that the files were retained.
Failures during a write report the stage that failed plus the underlying cause and state that
committed data was not deleted.

Schema, file names, and order 02 behavior are unchanged: no field, id, or folder layout moved, and
`ActiveConfig` is still only committed by the current transition's owner. The write path only
changed *how* bytes reach `world.json`.

**Recovery semantics, stated precisely:** an interrupted save loses only the generation being
written. Recovery returns the previous committed generation — one save behind — and the primary is
repaired by the next successful save of that config. That is asserted directly in
`SaveConfig_WhenPrimaryIsCorrupt_RecoversPreviousGenerationAndRepairsOnNextSave`.

## Reproduction before the fix

The store wrote `world.json` with a direct `File.WriteAllText`; the untracked `WorldConfigFile.cs`
left by the interrupted order-03 attempt was not wired to anything. An isolated probe
(`StabilizationSaveRisk03.Run`) exercised first/repeated saves, an interrupted-primary fixture, a
large-payload overwrite observed from another thread, and four same-file concurrent writers.
[Probe source](evidence-03/pre-fix-risk-probe.cs.txt), [results](evidence-03/risk-before-results.txt),
[second run](evidence-03/risk-before-v2-results.txt).

Observed pre-fix (Unity 6000.2.10f1, macOS, exit 0):

- No recovery copy and no writer lock ever existed: `backup-name=<missing>`, `lock-exists=False`.
- Interrupted primary: `LoadConfig` **threw `JsonReaderException`** — the last valid save was
  unrecoverable, and the next save silently wrote over the truncated file.
- Torn-write window: while one 8 MB save ran, the primary was observed at 0 bytes and at partial
  lengths (`min-bytes=0 zero-length=3`), i.e. an interruption in that window leaves no save at all.
  An earlier probe revision additionally showed the writer itself failing with a sharing violation
  while a reader sampled the file (`final-bytes=0`); that raw output was overwritten when the probe
  revision changed and is not claimed as retained evidence.
- Concurrent same-file writers: 2–9 saves raised `IOException: Sharing violation`, and one run left
  the primary `UNPARSEABLE` with no backup.

Post-fix, the same script reports `lock-exists=True`, `backup-name=First`, `recovered=True
display_name=First`, `min-bytes=542 zero-length=0 foreign-length=0 parse-failures=0`, and
`save-errors=0` with a complete 20 000-object primary and a valid backup.
[Results](evidence-03/final-risk-01-results.txt), [earlier post-fix run](evidence-03/risk-after-results.txt).

## Files changed

| File | Change |
|---|---|
| `Assets/App/Save/Runtime/WorldConfigFile.cs` | Completed (file was untracked and unreferenced): temporary write + flush, rotating backup, atomic replacement, blocking writer lock with timeout, stale-temporary cleanup, backup-aware read, stage-named failure reporting, test-only fault-injection checkpoint |
| `Assets/App/Save/Runtime/WorldConfigFile.cs.meta` | New (Unity meta for the file above) |
| `Assets/App/Save/Runtime/AssemblyInfo.cs` + `.meta` | New: `InternalsVisibleTo("Holodeck.Save.Tests")` so fixtures can drive the file protocol's failure stages without widening the public API |
| `Assets/App/Save/Runtime/WorldConfigStore.cs` | `WriteJson` → `WorldConfigFile.Write`; `LoadConfig` → `WorldConfigFile.Read` with backup fallback; loose-file migration and startup scan use the same protocol and recover from `.bak`; `ForkConfig` rejects a null source and keeps a usable display name |
| `Assets/App/Save/Tests/WorldConfigStoreTests.cs` | 17 new tests (32 in the assembly) |
| `docs/stabilization/handoff-03.md`, `docs/stabilization/STATUS.md`, `docs/stabilization/evidence-03/*` | This record |

No other original file was modified. `WorldConfigFile.cs` came from the interrupted attempt; its
design (temp + backup + atomic replace) was kept and completed rather than replaced.

## Tests added

| Scenario | Fixture |
|---|---|
| First save | `FirstSave_WritesPrimaryWithoutBackupAndRetainsWriterLock` |
| Repeated saves / backup rotation | `RepeatedSave_KeepsPreviousGenerationAsBackup` |
| Interrupted write, corrupt primary + valid backup | `SaveConfig_WhenPrimaryIsCorrupt_RecoversPreviousGenerationAndRepairsOnNextSave` |
| Missing primary with valid backup | `LoadConfig_WhenPrimaryIsMissing_RecoversFromBackup` |
| Missing files | `LoadConfig_WhenNoSaveFileExists_ReturnsNull`, `LoadConfig_IgnoresAbandonedTemporaryFiles` |
| No valid generation at all | `LoadConfig_WhenPrimaryAndBackupAreBothCorrupt_ThrowsAndRetainsFiles` |
| Startup scan recovery | `ScanAndMigrateAsync_RecoversConfigFromBackupWhenPrimaryIsCorrupt` |
| Failed write at each journal stage | `SaveConfig_WhenPrimaryCommitFails_KeepsLastValidSave`, `..._WhenBackupWriteIsInterrupted_...`, `..._WhenTemporaryWriteIsInterrupted_...` |
| Abandoned temporaries | `SaveConfig_ClearsTemporariesAbandonedByAPreviousProcess` |
| Concurrent writes | `SaveConfig_ConcurrentWriters_CommitWholeDocuments` (four writers, two store instances, 500-object payloads) |
| Another writer holds the lock | `SaveConfig_WhenAnotherWriterHoldsTheLock_ReportsTimeoutAndKeepsCommittedData` |
| Unwritable folder (Android repair path) | `SaveConfig_WhenFolderIsNotWritable_MigratesToWritableCopyAndKeepsOriginalFiles` |
| Migration compatibility | `SaveConfig_LegacyConfigWithoutBackupLoadsScansAndSavesInPlace` (hand-written schema-1 file with no backup/lock: loads, scans once, keeps its folder id, saves in place, keeps the old bytes in `.bak`) |
| Schema preservation | `SaveConfig_FileProtocolPreservesEverySchemaField` |

Existing tests, including both baseline test fixes and every order 02 fixture, were not weakened;
all 15 pre-existing save tests still pass unchanged.

## Verification actually run

Every run used Unity 6000.2.10f1 in an isolated APFS copy of the dirty checkout at
`/private/tmp/holodeck-saves-03-zusaQa/project`. The user's editor was closed (no
`Temp/UnityLockfile`), so no failure-exiting batch test ran interactively. External networking was
denied except loopback, `.env`/`worldlabs_env.txt` reads were denied, and credential-looking
environment variables were stripped. All fixtures are directories under the process temp path —
**no real user save was read or written**, and no paid generation was invoked.
[Policy](evidence-03/offline-tests.sb.txt), [runner](evidence-03/runner.py.txt),
[summary](evidence-03/verification-summary.json).

| Check | Final result | Retained evidence |
|---|---|---|
| Save EditMode assembly | **32/32** passed, 0 failed/skipped, exit 0 | [NUnit XML](evidence-03/final-save-01-tests.xml), [Unity log](evidence-03/final-save-01.log.txt) |
| Existing batch entry points | **19/19** passed, exit 0 | [Results](evidence-03/final-regressions-01-results.txt), [log](evidence-03/final-regressions-01.log.txt) |
| Manager transitions (order 02) | **14/14** passed, exit 0 | [Results](evidence-03/final-manager-01-results.txt) |
| Local/floor loader transitions (order 02) | **9/9** passed, exit 0 | [Results](evidence-03/final-loader-01-results.txt) |
| Restore/autosave/panorama transitions (order 02) | **9/9** passed, exit 0 | [Results](evidence-03/final-restore-01-results.txt) |
| Risk probe, pre-fix | risks reproduced, exit 0 | [Results](evidence-03/risk-before-results.txt), [second run](evidence-03/risk-before-v2-results.txt) |
| Risk probe, post-fix | all risky signals gone, exit 0 | [Results](evidence-03/final-risk-01-results.txt) |

Compilation: no C# errors in any final run; only pre-existing warnings
(`FindObjectOfType` deprecations, TMP `enableWordWrapping`, one hiding warning) appear in the logs.

Iteration record: `save-fix-02` failed 1 of 32 — the new writer-lock timeout fixture asserted on the
outer message before the underlying cause was inlined into it. The runtime message was improved
(the assertion was kept), after which `save-fix-03` and `final-save-01` both passed 32/32. Earlier
`save-fix-01` (31/31) predates the timeout fixture, and `risk-before-v2` is a superseded probe
sampling revision. All iteration artifacts remain in this directory.

## Preservation

- HEAD is still `05a09dd46710d82ac4bf1c91c1488d249872ae3d`, nothing is staged, no stash was created,
  and no worktree was reset or replaced. 97 dirty entries versus 94 at task start: the difference is
  exactly the three added save files/metas.
- Source vs isolated copy: **all 4 427** `Assets/`, `Packages/`, and `ProjectSettings/` files match
  byte for byte, so the final runs validated this exact source.
  [Comparison](evidence-03/final-source-comparison.json), [hashes](evidence-03/verification-source-hashes.json).
- Against the 4 748-file hash snapshot taken at the start of the interrupted attempt, exactly three
  files changed — `WorldConfigStore.cs`, `WorldConfigStoreTests.cs`, and this status document — and
  nothing was removed. Every other original file, including all baseline test fixes, order 02
  runtime files, scenes, prefabs, and packages, is byte-identical.
  [Preservation](evidence-03/preservation-final.json).
- Scoped whitespace check (`git diff --check` on the two changed files) is clean.

## Remaining risks and platform limitations

- **Atomic replacement is only verified on macOS.** macOS/Linux editor+player and Android use
  `libc rename`; Windows, WSA, iOS, and visionOS fall back to `File.Replace`/`File.Move`. That
  branch did not compile or run on this machine, and `File.Replace` availability in Unity's
  iOS/visionOS profile is unverified.
- **No device run.** Android's atomic rename and the read-only folder writable-copy repair were
  exercised only through the macOS POSIX path (the read-only fixture really did hit
  `UnauthorizedAccessException` and migrated). Quest behavior for a killed process mid-save is
  inferred, not measured.
- **Durability, not just atomicity.** The temporary file is fsynced before the rename, but the
  containing directory is not, so an abrupt power loss can still lose the newest rename while
  leaving the previous generation in `world.json.bak`.
- **Cross-process locking is untested with two live processes.** The lock, its 5-second timeout,
  and the whole-document guarantee were exercised in one process (a deliberately held lock handle
  for the timeout path); two simultaneous Unity/app instances were not launched.
- **A contended save blocks its caller for up to 5 seconds.** Autosave calls `SaveConfig` from the
  Unity main thread, so an unreachable holder of the writer lock (for example a second app instance
  that never finished a save) stalls that thread until the timeout instead of failing fast.
- **`world.json.bak` costs a second write per save.** Autosave on each object mutation now writes
  roughly twice the bytes; a save also does one extra directory scan to clear abandoned temps.
- **Cache files remain non-atomic.** `WorldConfigAutoSave.WriteCacheBytesAsync` (SPZ/pano bytes)
  and the thumbnail writer still use `File.WriteAllBytes`. They are content caches referenced from
  the config, not saves, and order 02 already deferred them.
- **Save failures still propagate.** `SaveConfig` throws (as before) rather than retrying; the
  Android access-denied case still repairs itself by migrating to a writable copy.
- **Documented read-side guard:** a file that is not a world-config object (for example `{}`) is no
  longer loaded as a nameless world; it is skipped with a warning. That guard came with the
  interrupted-attempt file and is intentional.
- The `checkpoint` fault-injection parameter exists in production code but is inert unless a caller
  passes a delegate; only the test assembly can reach it (`InternalsVisibleTo`).

## Next action

Orders 04–07 remain unstarted; do not begin them from this handoff. If order 05 reaches a device
first, the two things worth confirming on hardware are (a) a save interrupted by an app kill leaves
a recoverable `world.json.bak` and (b) the read-only legacy-folder migration still repairs itself
after the write path change. Consider checkpointing the logical change group (baseline fixes →
order 02 → order 03) before further work; nothing has been committed.
