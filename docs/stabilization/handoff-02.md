# Work order 02 — Stabilize world transitions

Completed 2026-09-10 (America/Los_Angeles), in the current local checkout. Scope: order 02 only. **32/32 transition tests, 19/19 existing batch entry points, and 15/15 save EditMode tests pass**, with Unity exit 0 for every final run. No commit, push, new APK, or later work order.

## Outcome and operation policy

**Newest request wins.** A manager-owned `WorldTransitionOperation` follows generation, browser selection, saved-world restore, loose splat loading, and panorama loading. A new request invalidates its predecessor. Cancellation stops local waiting, cleans up owned content, and observes late tasks; stale results cannot register a renderer, publish success, or replace `ActiveConfig`. Completed operations retain content ownership until replacement or unload.

Replacement removes the outgoing world; a failed replacement leaves no newly loaded world or active saved config. There is no automatic rollback to the outgoing world. A loader participating in restore leaves its caller's operation current after an ordinary source failure so restore can try its next source. Explicit cancel, disable, unload, and terminal restore failure invalidate the whole operation.

`ActiveConfig` commits only for the current successfully loaded source. Object-mutation autosave is suppressed during restoration. Async cache writes capture their destination config/version; late completion cannot attach A's bytes to B. Disabling autosave clears active/pending bindings so missed transitions cannot leave a stale writable config.

Saved root transforms and spawn overrides still run **before renderer creation/activation and loaded/spawn consumers**. Placement roots remain inactive during preparation; throwing callbacks destroy their roots. Cancellation/unload/load callbacks may start B while A unwinds without A deleting B or later A listeners publishing stale state.

## Reproduction and changes

Before the fix, isolated fixtures reproduced both risks: late A replaced already-loaded B and `LastLoadedWorld`; a failed restore replaced `ActiveConfig` with the failed config. [Results](evidence-02/risk-before-fixed-probe-results.txt), [probe source](evidence-02/pre-fix-risk-probe.cs.txt), [context](evidence-02/pre-fix-reproduction.json). Original pre-fix raw logs in `/private/tmp/holodeck-transitions-02-w462lgrl` were lost during the interrupted task; the in-repository results, process records, and probe remain available.

Changed runtime files (paths relative to the checkout):

| Area | Files |
|---|---|
| Shared ownership, registration, cleanup, reentrancy | New `Packages/com.worldlabs.gaussian-splatting/Runtime/WorldLabs/WorldTransitionOperation.cs`; vendored `WorldLabsWorldManager.cs` |
| Cancellation-aware provider reads, downloads, polling | Vendored `WorldLabsClient.cs`, `WorldLabsClientExtensions.cs` |
| Generation/browser ownership and late completion | `Assets/App/Scripts/Direct/VoiceToWorldLabsPluginCoordinator.cs`; vendored `WorldBrowserController.cs` |
| Owned placement, cancellation, native-array cleanup, per-request settings | `Assets/App/Scripts/Direct/RuntimeSplatFloorLoader.cs`; `Assets/App/Command/SpeechIntent/Runtime/LocalRemoteSplatLoader.cs` |
| Restore identity, config commit, async cache identity | `Assets/App/Save/WorldConfigRestorer.cs`, `WorldConfigAutoSave.cs` |
| Panorama lifetime and saved-world entry point | `Assets/App/Command/SpeechIntent/Runtime/LocalRemotePanoLoader.cs`, `UI/MyWorldsPanel.cs`; `Assets/App/Scripts/Direct/ThumbnailSkyboxController.cs` |

Added `WorldTransitionBatchTests.cs`, `WorldLoaderTransitionBatchTests.cs`, and `WorldRestoreTransitionBatchTests.cs` under `Assets/App/Editor/`, plus metadata. Existing tests, including both baseline test fixes, are unchanged. No save schema, store write protocol, credential distribution, scene, prefab, or package-version changes.

## Current verification

All final runs used Unity 6000.2.10f1 in an isolated APFS copy at `/private/tmp/holodeck-transitions-02-final-sg5r74ko/project`. Fixtures use empty scenes, temporary stores, and controlled I/O. External outbound networking and environment-resource reads were denied; inherited credential variables were removed. No paid generation or failure-exiting tests ran in the interactive editor. [Policy](evidence-02/offline-tests.sb.txt), [runner](evidence-02/final-runner.py.txt), [summary](evidence-02/verification-summary.json).

| Check | Final result | Retained evidence/log |
|---|---|---|
| Manager transitions | **14/14**, exit 0: A→B, download/processing/placement cancel, disable/scene unload, default/unload, same-ID retry, failed placement, ordering, callback reentrancy | [Results](evidence-02/final-manager-03-results.txt), [Unity log](evidence-02/final-manager-03.log.txt) |
| Local/floor loaders | **9/9**, exit 0: canceled downloads, late placement cleanup, failed-source fallback/retry, disable, native-array ownership, throwing preparation callback | [Results](evidence-02/final-loader-01-results.txt), [Unity log](evidence-02/final-loader-01.log.txt) |
| Restore/autosave/panorama | **9/9**, exit 0: overlapping restores, cancel/disable/unload/retry, failed restore, late cache writes, panorama lifetime, reentrant config commits, autosave disable/re-enable | [Results](evidence-02/final-restore-02-results.txt), [Unity log](evidence-02/final-restore-02.log.txt) |
| Existing batch regressions | **19/19**, exit 0; includes spawn/orientation, load failure, splat processing, speech, cached objects, audio, view mode, migration | [Per-entry results](evidence-02/final-regressions-01-results.txt), [Unity log](evidence-02/final-regressions-01.log.txt) |
| Save EditMode assembly | **15/15**, zero failures/skips, exit 0 | [NUnit XML](evidence-02/final-save-01-tests.xml), [readable results](evidence-02/save-results.txt), [Unity log](evidence-02/final-save-01.log.txt) |

No C# compiler errors appeared in the final runs. Source/copy hashes match: [comparison](evidence-02/final-source-comparison.json), [source hashes](evidence-02/verification-source-hashes.json). Raw logs also remain in the temporary run directory; redacted copies above survive its removal.

The new async suites use a temporary bounded main-thread continuation pump and Editor-only `ExecuteAlways` fixture subclasses to deliver lifecycle callbacks in EditMode. They verify state/ownership and async boundaries, not actual rendered frames or PlayMode/device behavior. Early attempts exposed adapter starvation and missing fixture lifecycle delivery; both were corrected. One restore run passed all assertions but exited `-4` during Unity shutdown; the unchanged-source rerun passed with exit 0. These iteration records remain in the [summary](evidence-02/verification-summary.json). Deliberate fixture errors and MCP/Unity shutdown warnings are not assertion failures.

## Preservation and limitations

HEAD remains `05a09dd46710d82ac4bf1c91c1488d249872ae3d`, with no staged changes. All 4,681 original files remain. Only the 12 intended runtime files and `STATUS.md` changed among those originals; prior changes to all other files, including `CachedObjectBatchTests.cs` and `WorldConfigStoreTests.cs`, remain byte-for-byte intact. [Preservation](evidence-02/preservation-final.json). Scoped whitespace checks are clean; existing unrelated scene/metadata whitespace was left intact.

- Cancellation is **local**. An already submitted generation request/provider job may continue; this implementation sends no remote job-stop request. Its late result cannot activate a canceled local operation.
- Existing synchronous SPZ work cannot be interrupted midway. Background parsing may finish after cancellation; completed results are disposed/ignored. No performance improvement or Quest responsiveness claim is made.
- An already-running cache file write can leave an unreferenced file after supersession; stale metadata publication is blocked. Atomic writes/recovery and cache cleanup remain outside this order.
- The existing PLY reader has a separate internal allocation leak on truncated input before its disposal block. Transition code cleans returned arrays; that parser defect was not changed.
- No live-provider, PlayMode, headset, visual-fidelity, performance, or new Android-build acceptance was performed. The baseline APK predates these runtime changes.

Order 02 is complete within this local verification scope. Orders 03–07 remain unstarted. Stop here; no commit or push.
