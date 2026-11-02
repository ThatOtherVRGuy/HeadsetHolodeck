# Order 03 re-verification — save-write protection (requested as "order 04")

Completed 2026-09-13 (America/Los_Angeles) in the current local checkout. No commit, push, stash,
reset, credential change, scene change, or later work order.

## Numbering conflict found before any change was made

The task text for "work order 04" describes the save-write protection work, not the plan's order 04:

| Source | Order 03 | Order 04 |
|---|---|---|
| `WORK-ORDERS.md` | Protect saves — temporary writes, backup/recovery, failure reporting | Define the build credential boundary (`BuildEnvCopier.cs`) |
| Task text for this run | (not mentioned) | Temporary writes, platform-appropriate atomic replacement, backup/recovery, clear failure reporting for `WorldConfigStore` |

`STATUS.md` and `handoff-03.md` record order 03 as complete, and the task also says not to change
credential distribution — which is what the plan's order 04 covers. So the requested scope was
treated as authoritative and the plan's credential order was **not** started.

**The requested scope is already implemented and landed in this checkout.** Nothing in
`Assets/`, `Packages/`, or `ProjectSettings/` needed to change: a pre-task hash snapshot of all
4 427 files is byte-identical afterwards (`preservation-final.json`: 0 changed, 0 added,
0 removed). This handoff therefore records independent re-verification rather than new
implementation, and flags the order-number mismatch for you to resolve.

## What is already there (confirmed by inspection, not just by the previous handoff)

- `Assets/App/Save/Runtime/WorldConfigFile.cs` — one journaled writer: serialize and re-parse the
  document before committing, take a per-folder writer lock (`.world.json.lock`, `FileShare.None`,
  5 s timeout), clear abandoned temporaries, write `world.json.<guid>.tmp` and flush it, rotate the
  previous committed document to `world.json.bak` by rename, then rename the temporary over
  `world.json` (POSIX `rename`; `File.Replace`/`File.Move` elsewhere). Failures name the stage that
  failed plus the cause and state that committed data was not deleted.
- `Assets/App/Save/Runtime/WorldConfigStore.cs` — `WriteJson`/`LoadConfig`, loose-file migration,
  and the startup scan all go through that protocol and fall back to `.bak` on read. Schema, file
  names, folder layout, and order 02's operation ownership/`ActiveConfig` guarantees are unchanged.
- `Assets/App/Save/Tests/WorldConfigStoreTests.cs` — 32 tests in `Holodeck.Save.Tests`, including
  first/repeated save, corrupt primary with valid backup, missing primary with valid backup, no
  files at all, both generations corrupt, abandoned temporaries, per-stage write interruption,
  a held writer lock, concurrent writers, unwritable-folder migration, migration compatibility, and
  schema preservation.

## Verification actually run

Unity 6000.2.10f1, macOS, batch mode, exit 0 for every check. The user's editor was closed (no
`Temp/UnityLockfile`) so no failure-exiting batch test ran interactively. Runs used an isolated copy
that was first proven byte-identical to this checkout (4 427 files, `final-source-comparison.json`:
`match: true`). Provider networking and `.env`/`worldlabs_env.txt` reads were denied and
credential-looking environment variables stripped ([policy](evidence-03-reverification/offline-tests.sb.txt)).
Every fixture directory is under the process temp path — **no real user save was read or written**,
and no paid generation was invoked. [Runner](evidence-03-reverification/runner.py.txt),
[driver](evidence-03-reverification/run_all.py.txt), [summary](evidence-03-reverification/verification-summary.json).

| Check | Result | Exit | Evidence |
|---|---|---|---|
| Save EditMode assembly | **32/32 passed**, 0 failed/skipped | 0 | [XML](evidence-03-reverification/order04-save-01-tests.xml), [log](evidence-03-reverification/order04-save-01.log.txt) |
| Existing batch entry points | **19/19 passed**, 0 failures | 0 | [Results](evidence-03-reverification/order04-regressions-01-results.txt) |
| Manager transitions (order 02) | **14/14 passed** | 0 | [Results](evidence-03-reverification/order04-manager-01-results.txt) |
| Local/floor loader transitions (order 02) | **9/9 passed** (9 awaited fixtures, aggregate PASS) | 0 | [Results](evidence-03-reverification/order04-loader-01-results.txt) |
| Restore/autosave/panorama transitions (order 02) | **9/9 passed** | 0 | [Results](evidence-03-reverification/order04-restore-01-results.txt) |
| Risk probe, pre-fix shape vs journaled writer (new) | risks reproduced and prevented | 0 | [Results](evidence-03-reverification/order04-risk04-01-results.txt), [probe](evidence-03-reverification/order04-risk-probe.cs.txt) |
| Risk probe, journaled writer (order 03 probe, re-run) | all risky signals gone | 0 | [Results](evidence-03-reverification/order04-risk03-01-results.txt) |

Compilation: no `error CS` in any log. Only pre-existing `warning CS0618` obsolete-API warnings
(28 in the save run) appear.

## Risk reproduction, both directions

The new probe ([source](evidence-03-reverification/order04-risk-probe.cs.txt)) runs each scenario in its own fresh
temporary directory. `LEGACY-*` lines are a counterfactual that writes `world.json` with a bare
direct overwrite (the pre-fix shape, no product code touched); `JOURNALED-*` lines drive the real
store over the same interrupted on-disk state.

```
LEGACY-FIRST-SAVE bytes=339 backup-exists=False lock-exists=False temp-files=0
LEGACY-INTERRUPTED bytes=169 parse=JsonReaderException backup-exists=False recoverable=False
LEGACY-CONCURRENT writers=4 rounds=4 write-errors=12 first-error=IOException: Sharing violation ... final-bytes=2658122
JOURNALED-FIRST-SAVE primary-name=First backup-exists=False lock-exists=True temp-files=0
JOURNALED-REPEATED-SAVE primary-name=Second backup-name=First primary-bytes=543 backup-bytes=542 temp-files=0
JOURNALED-REPEATED-SAVE-2 primary-name=Third backup-name=Second temp-files=0
JOURNALED-INTERRUPTED-STATE primary-bytes=275 backup-exists=True backup-name=Committed temp-files=0
JOURNALED-INTERRUPTION-RECOVERY recovered=True display_name=Committed
JOURNALED-INTERRUPTION-FILES-RETAINED primary=True backup=True temp-files=0
JOURNALED-INTERRUPTION-REPAIR primary-name=Repaired backup-name=Committed temp-files=0
JOURNALED-MISSING-PRIMARY primary-exists=False backup-name=BackupOnly1
JOURNALED-MISSING-PRIMARY-RECOVERY recovered=True display_name=BackupOnly1
JOURNALED-MISSING-FILES loaded=<null> folder-created=False
```

The re-run order 03 probe adds the in-flight window and contention measurements:

```
INTERRUPTION-STATE primary-bytes=271 primary-name=UNPARSEABLE(JsonReaderException) backup-exists=True backup-name=First
INTERRUPTION-RECOVERY recovered=True display_name=First
TORN-WRITE-WINDOW observations=49558 min-bytes=542 zero-length=0 foreign-length=0 parse-failures=0 concurrent-open-failures=0 first=<none>
CONCURRENT-SAME-FILE writers=4 rounds=4 save-errors=0 final-primary-name=Writer2_3 final-primary-objects=20000 final-backup-name=Writer1_3 temp-files=0
```

## Recovery behavior (exact, as observed)

An interrupted save loses only the generation being written. The reader falls back to the previous
committed generation in `world.json.bak` — one save behind — logs a warning, keeps both files, and
never rewrites part of a document it could not trust. The next successful save of that config
repairs the primary (`primary-name=Repaired backup-name=Committed`). With no valid generation at all
the read throws `IOException` naming both paths and stating the files were retained. A primary that
is missing while the backup is valid still loads; a config folder with neither file returns `null`
and creates nothing.

## Preservation

- HEAD is still `05a09dd46710d82ac4bf1c91c1488d249872ae3d`. No stash was created and no worktree was
  reset or replaced; the checkout was never swapped for a clean copy.
- Pre-task snapshot: all 4 427 `Assets/`, `Packages/`, `ProjectSettings/` files. After this task:
  **0 changed, 0 removed, 0 added** (`preservation-final.json`). The baseline test fixes, order 02
  runtime changes, order 03 save files, scenes, and packages are untouched.
- Isolation: runs used `/private/tmp/holodeck-saves-03-zusaQa/project` (an existing isolated copy of
  this dirty checkout, re-synced and hash-verified and confirmed to need zero file copies) with the
  harness in `/private/tmp/holodeck-saves-04-verify`. The only additions to the repository are
  `docs/stabilization/evidence-03-reverification/`, this handoff, and the `STATUS.md` edit; the only files added to
  the isolated copy are the four `Stabilization*.cs` harness adapters, which are not product code.

## Remaining risks and platform limitations (unchanged from order 03, still true)

- **Atomic replacement is verified on macOS only.** Windows, WSA, iOS, and visionOS take the
  `File.Replace`/`File.Move` branch, which did not compile or run here.
- **No device run.** Quest behavior for a process killed mid-save is inferred, not measured; the
  read-only-folder writable-copy repair was exercised only through the macOS POSIX path.
- **Durability, not just atomicity.** The temporary file is flushed before the rename but the
  containing directory is not, so an abrupt power loss can still lose the newest rename while
  leaving the previous generation in `world.json.bak`.
- **Cross-process locking is untested with two live processes** (exercised in one process).
- **A contended save blocks its caller for up to 5 s** — autosave runs on the Unity main thread.
- **Content caches are still non-atomic** (`WorldConfigAutoSave.WriteCacheBytesAsync`, thumbnails);
  they are caches referenced from the config, not saves.
- **Save failures still propagate**; the Android access-denied case repairs itself by migrating.

## Resolution

The numbering question was put back to the user, who chose the plan's order 04 — the build
credential boundary — as the real target. This document was therefore kept as the order 03
re-verification record (renamed from `handoff-04.md`, evidence moved to
`evidence-03-reverification/`), `handoff-04.md` was written for the credential order, and no
credential behavior was changed by this task. Nothing has been committed.
