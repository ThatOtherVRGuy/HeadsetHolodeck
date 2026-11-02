# Work order 01 — Current baseline

Date: 2026-09-08 (America/Los_Angeles). Scope: order 01 only. No implementation fixes, upgrades, scene repair menus, commits, or later work orders.

## Follow-up

The three regression failures below were subsequently resolved on 2026-09-08: **19/19 batch entry points and 15/15 save tests now pass**. See [regression-fix handoff](handoff-01-fixes.md). The original baseline results and evidence below are retained as the before-fix record.

## Original outcome

The current dirty checkout loads/compiles in Unity 6000.2.10f1 and passes install validation with optional-provider warnings. The regression baseline is **not green**: 18/19 batch entry points passed; save EditMode tests passed 13/15. The Android build **succeeded** (exit 0); the APK is retained as a private developer artifact.

## Revision and preservation

- HEAD: `05a09dd46710d82ac4bf1c91c1488d249872ae3d`; initially 47 modified tracked files, 282 untracked files, no staged files. Tracked diff: 9,384 additions / 1,241 deletions. These results include existing uncommitted implementation, not just HEAD.
- [Initial inventory](evidence-01/git-status-before.txt), [grouped inventory](evidence-01/dirty-inventory-summary.txt), [diff summary](evidence-01/diff-stat-before.txt), and [run context](evidence-01/baseline-context.json).
- No Unity editor was running; no project lock was present. Tests/build ran in `/private/tmp/holodeck-baseline-01-xddejnxt/project`, an APFS copy of Assets, Packages, ProjectSettings, UserSettings, and Library, plus README, environment example, and private local environment file. No clean worktree, stash, or reset was used. This is a warm-cache baseline, not a clean import/build reproducibility claim.
- A temporary adapter was added only in the copy to invoke the existing validator and batch entry points. During the build, Unity added XR preloaded-asset references to the copy's ProjectSettings and temporarily removed/moved XRSimulationRuntimeSettings; [snapshot comparison](evidence-01/snapshot-comparison.txt) and [settings diff](evidence-01/build-settings-diff.txt) record these build-hook effects. It starts regression fixtures in an empty scene. Failure-exiting batch tests were never run in an interactive editor.
- **Preservation passed:** 4,641 original files hashed; only STATUS.md changed, no original files disappeared, HEAD unchanged, index unstaged. All original Assets/Packages/ProjectSettings files also matched in the temporary copy after build hooks finished. [Preservation results](evidence-01/preservation-results.json).

## Original baseline verification

| Check | Current result | Evidence |
|---|---|---|
| Unity availability/state | 6000.2.10f1; batch, not playing/compiling/updating at validation; Android active; startup/validation exit 0, no C# compiler errors observed | [State](evidence-01/validation-results.txt) |
| Install validator | 24 checks; 0 failures; 2 warnings: optional `MESHY_API_KEY`, `TRIPO_API_KEY` missing. Key checks establish presence only, not authentication | [Report](evidence-01/install-validation.txt) |
| Required files/settings | Validator-required files present and not LFS pointers; direct manifest/lock versions match; ARM64, IL2CPP, Vulkan, OpenXR Multi-pass; Android URP renderer includes GaussianSplatURPFeature | [Inventory](evidence-01/static-inventory.txt) |
| Script asset references | Holodeck scene 750, splat prefab 1, ChakraTest scene 24; zero missing/null script GUIDs against local assets/packages/cache; runtime wiring not exercised | [Reference check](evidence-01/asset-reference-check.txt) |
| Existing batch regressions | 18 entry points passed, 1 failed. First process exit 1 at cached-object suite; second resumed with later entries and exited 0. Passing entries include speech, hands, audio persistence, generated physics/texture repair, PLY, SPZ compatibility/source checks, spawn/orientation, view mode, and save migration | [Per-entry markers](evidence-01/batch-results.txt), [failure](evidence-01/failure-excerpts.txt) |
| Save EditMode tests | 15 discovered; 13 passed; 2 failed; exit 2 | [NUnit XML](evidence-01/save-tests.xml), [readable results](evidence-01/save-test-results.txt) |
| Android APK | **Succeeded**, exit 0; Gradle release build; 16m 35.7s. APK: `Builds/stabilization-01-20260908/Holodeck-baseline-01.apk` (313,495,873 bytes). ZIP integrity and APK signature passed; no device deployment | [Build report](evidence-01/build-results.txt), [artifact/hash metadata](evidence-01/build-results.json) |

[Executed commands and adapter context](evidence-01/execution.txt); [entry-point safety review](evidence-01/test-entrypoint-review.txt); [temporary adapter source](evidence-01/temporary-validation-adapter.cs.txt). Raw Unity logs are under `/private/tmp/holodeck-baseline-01-xddejnxt/`; curated evidence is retained here without secret values. The temporary project copy (including its copied environment file) was removed after verification to reclaim disk space; the APK, logs, and initial source hashes were retained. Cached assemblies may retain original source paths in stack traces. MCP connection-refusal and shutdown-disposal warnings occurred in batch logs; they are not counted as test assertion failures.

## Prioritized blockers and limits

1. **Unsafe cached-object regression path / conflicting expectations.** `CachedObjectBatchTests.RunCachedObjectStoreTests` fails at `TestDispatcherAsksWhereBeforeGeneratingWithoutPlacement` (line 796): “Expected unresolved generated placement to ask for clarification.” Current `ObjectPlacementController.TryResolvePlacementPose` accepts `PlaceObject` with `None` at line 422, while the passing speech regression explicitly expects default creation one meter ahead. Reconcile the intended contract before changing runtime behavior. The failing test unexpectedly entered generation/credit preflight before Unity exited. No provider generation result was observed; this is not paid-generation verification. Its fixture `test-key` only satisfies configuration-presence logic; future runs must isolate provider credentials and block outbound provider calls. Assertions after this failure in the cached-object suite did not run.
2. **Save test expectation and fixture defects.** Fork test fails at line 112 because source and fork have equal second-resolution `created_at` values; both operations occurred in the same second. Concurrent-migration test fails at line 152 writing its fixture into a missing temporary root, before reaching migration. `CreateForTesting` does not create that directory. This does not reproduce migration concurrency failure; the existing migration batch entry points passed. These two tests must be made meaningful before treating them as coverage for later save work.
3. **Build/device result boundary.** Android build passed; artifact identity and integrity results are in the build evidence. No headset run, microphone test, tracking/suspend/resume test, rendering measurement, real save restart/restore, or live paid-generation acceptance was performed. Do not infer device readiness from local checks.
4. **Carry-forward static risks remain unverified.** Cancellation/overlapping restore, interrupted writes, and credential distribution risks were not fixed. The build hook embeds the project-root environment file; any resulting APK is a private developer artifact. No credential-boundary changes or distribution were performed.

The Cozy Cabin diagnostic was not run: `/tmp/headsetholodeck-quest-cache/CozyCabin.spz` is absent. Other package/sample suites and PlayMode/device acceptance were not part of this bounded baseline. Earlier learning-log passes, pre-existing APKs in `Builds/`, and logcat files remain historical; none count as today's passes.

## Logical checkpoint groups (review/commit separately later)

1. Command targeting/clarification and world manipulation: SpeechIntent runtime changes, new SceneFocusContext/TargetSelectionUtility/SnapWhileGrabbed/WorldManipulationController, and SpeechIntentBatchTests.
2. Generated objects/cache: ObjectGenerationService, new GeneratedObjectGlbTextureRepair, interactable/choice changes, CachedObjectBatchTests.
3. Splat loading/rendering/spawn and restore: RuntimeSplatFloorLoader, LocalRemoteSplatLoader, vendored SPZ/world manager/browser changes, splat prefab, WorldConfigRestorer, WorldConfigStore, related regressions. Coordinate shared files with groups 1–2.
4. UI/rig integration and assets: ArchOperationsPanel, UI setup helpers, new rig harness/scenarios, Holodeck scene, ChakraTest scene, XR temporary settings, sample lightmap metadata. Review scene serialization together with its required scripts; separate incidental asset churn.
5. Tooling/dependency setup: package manifests/settings, 42 untracked NuGet DLLs and metadata, agent/tool configuration directories. Review deliberately; no upgrades or blanket checkpoint commit performed.
6. Documentation/evidence: existing learning-log/SPZ notes separately from this stabilization handoff, STATUS update, and evidence directory.

## Files changed by order 01 / next action

Changed/created: `docs/stabilization/STATUS.md`, this handoff, `docs/stabilization/evidence-01/`, and the ignored build artifact `Builds/stabilization-01-20260908/Holodeck-baseline-01.apk`. `WORK-ORDERS.md` and all implementation/assets/settings remain unchanged by this task. No commit was created.

Repository `git diff --check` still reports existing scene/sample-metadata whitespace (exit 2); these files were preserved byte-for-byte. [Whitespace report](evidence-01/diff-check.txt).

Next action: review the recorded failures and establish safe fixture/test expectations before claiming a green baseline. Later work orders remain unstarted and require a separate task; retain these baseline failures when evaluating subsequent changes.
