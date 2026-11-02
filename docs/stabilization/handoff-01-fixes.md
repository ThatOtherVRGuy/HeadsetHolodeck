# Baseline 01 — Regression fixes

Date: 2026-09-08. Follow-up authorized by “Resolve those failures.” All three failures recorded in [handoff 01](handoff-01.md) are resolved. Work orders 02–07 remain unstarted. No commit or push performed.

## Changes

- `Assets/App/Editor/CachedObjectBatchTests.cs`: changed the stale “no location must ask where” fixture to an explicit `PointingHit` request with no hit. The existing speech test retains coverage of default placement one meter ahead when location is omitted. Removed the misleading fake provider-key component and assert that the pose is unresolved before dispatch. The dispatcher must reply “Where?” and leave the generation service uncreated.
- `Assets/App/Save/Tests/WorldConfigStoreTests.cs`: create the temporary root before writing the loose-splat fixture. Track and destroy every store created by the fixture during teardown. Backdate the original config's creation timestamp so fork timestamp renewal is tested deterministically, without requiring two immediate operations to fall in different seconds; also verify the source timestamp remains unchanged and fork creation/modification timestamps agree.

No runtime source, serialized assets, package versions, or save schema changed. [Exact task-only patch](evidence-01-fixes/task-diff.patch) is relative to the pre-existing dirty checkout, preserving earlier edits to CachedObjectBatchTests.

## Current verification

| Check | Result | Evidence |
|---|---|---|
| Existing batch regression entry points | **19/19 passed**, Unity exit 0; includes full cached-object suite (including assertions after the previous failure), speech default placement, spawn/load, and migration checks | [Markers](evidence-01-fixes/batch-results.txt), [summary](evidence-01-fixes/batch-summary.json) |
| Save EditMode assembly `Holodeck.Save.Tests` | **15/15 passed**, 0 failures/skips, Unity exit 0; concurrent-migration test now reaches and passes its migration assertions | [NUnit XML](evidence-01-fixes/save-tests.xml), [readable results](evidence-01-fixes/save-results.txt) |
| Compilation | Both isolated Unity 6000.2.10f1 runs compiled/loaded the changed tests; no C# compiler errors observed | [Process results](evidence-01-fixes/process-results.json) |
| Provider isolation | External outbound connections blocked; local IPC/loopback allowed for Unity services. Environment-file/resource reads blocked and inherited credential variables removed. No generation-start log markers; corrected dispatcher logged missing placement “Where?” | [Network/file policy](evidence-01-fixes/offline-tests.sb.txt), [runner](evidence-01-fixes/runner.py.txt), batch summary above |
| Preservation | Only the two intended test files and stabilization documentation/evidence changed; existing implementation/assets/settings retained | [Preservation](evidence-01-fixes/preservation.json) |

The runs used a temporary APFS copy of the current dirty project at `/private/tmp/holodeck-baseline-fixes-rb97gddc/project`, with a temporary adapter invoking the existing entry points. No tests were run in the user's interactive editor. The copy was removed after verification; raw `batch.log` and `save-tests.log`, initial hashes, and execution records remain in its parent directory. In-repository evidence excludes raw Unity logs and credentials.

The initial [Android APK](evidence-01/build-results.json) remains the last verified build. No APK rebuild was necessary for these Editor-only test changes. No headset or live-provider acceptance was performed; cancellation, atomic-save, credential-distribution, and device-measurement work remain outside this follow-up.

## Next action

Review/checkpoint the existing logical change groups before committing implementation changes. The selected baseline regressions are now green; this is not a claim that every package suite or headset scenario has been verified.
