# Stabilization work orders

Read STATUS.md first. Paths are relative to the project root. Complete one order per task and write its short handoff. Refine later orders using verified findings.

## 01 — Establish the baseline

**Outcome:** establish what today's working tree builds and passes before refactoring.

**Inspect:** git status; package/project settings; `Assets/App/Editor/HeadsetHolodeckInstallValidator.cs`, `CommandLineAndroidBuild.cs`, relevant `*BatchTests.cs`; `Assets/App/Save/Tests`.

**Do:** inventory modified/untracked implementation and required assets without exposing secrets. Check Unity availability/state; run install validation and applicable existing regressions; build Android if available without disrupting the user's editor. Inspect test entry points first: some batch tests exit the editor on failure. Do not run those in the interactive editor. Preserve uncommitted changes if isolation is needed.

**Done when:** handoff-01 records revision/dirty state, current results, unrun checks and reasons, build artifact if produced, and prioritized blockers. Identify logical checkpoint groups; do not commit unrelated work. Historical logs remain labeled historical.

**Exclude:** architecture changes, dependency upgrades, paid generation, scene repair menus, unsupported headset claims.

## 02 — Stabilize world transitions

**Outcome:** only the current operation can activate a world; canceled or superseded work cannot leave orphaned content or the wrong active config.

**Inspect:** `VoiceToWorldLabsPluginCoordinator.cs`, `RuntimeSplatFloorLoader.cs`, `LocalRemoteSplatLoader.cs`, `MyWorldsPanel.cs`, `WorldConfigRestorer.cs`, `WorldConfigAutoSave.cs`, vendored `WorldLabsWorldManager.cs` and client polling.

**Do:** reproduce risks; choose a shared reject-or-supersede policy; track operation identity; prevent stale completion; define cleanup and failure recovery. Preserve transform-before-spawn ordering. Distinguish local cancellation from stopping a remote provider job.

**Done when:** tests cover rapid A→B loading, cancellation during download/placement, disable/unload, late completion, failed restore, and retry. Existing spawn/load regressions pass. No stale activation or incorrect ActiveConfig. Document any remote cancellation limitation.

**Exclude:** parser/UI redesign and provider replacement.

## 03 — Protect saves

**Outcome:** a failed write does not destroy the last valid save.

**Inspect:** `Assets/App/Save/Runtime/WorldConfigStore.cs`, Save tests, migration tests; autosave/restorer only as necessary.

**Do:** use temporary writes and platform-appropriate replacement, backup/recovery, and clear failure reporting. Check concurrent writes and migration behavior; preserve the schema. Coordinate shared edits with 02.

**Done when:** tests cover failed/interrupted writes, corrupt primary with valid backup, first/repeated saves, missing files, and migration compatibility. State remaining platform verification. Use fixture directories, never real user saves.

**Exclude:** cloud sync or a new save format.

## 04 — Define the build credential boundary

**Outcome:** builds intended for others cannot accidentally embed the developer's entire `.env`.

**Inspect:** vendored `BuildEnvCopier.cs`, runtime environment readers, `.env.example`, README.

**Do:** distinguish private developer builds from distribution. If distribution intent is unknown, prepare a concrete policy decision before changing credential behavior. Implement the selected boundary without silently breaking private builds.

**Done when:** fixture-based checks demonstrate exclusion of developer credentials/unrelated entries from distribution builds; private setup and failed-build cleanup are documented and checked. Never expose real secret values in evidence.

**Exclude:** an automatic backend rewrite or publishing an APK.

## 05 — Verify Quest behavior and measure performance

**Outcome:** observed device acceptance results and a measured optimization backlog.

**Inspect:** handoffs 01–03 and current APK; relevant runtime code only as failures or measurements warrant.

**Do:** exercise launch → voice → create/load → manipulate → save → restart → restore. Start with cached content. Include network failure, rapid loads, cancel, tracking loss, suspend/resume, and restored objects/audio/spawns. Live paid generation needs authorization. Measure load stalls, frame timing, peak memory, and repeated-switch memory behavior for representative World Labs and loose-SPZ files.

**Done when:** record build/device/file context, observed vs untested scenarios, reproducible failures, and measurements. Agree performance targets before claiming a pass. Separate rendering fidelity from speed. If no device access, prepare the checklist and mark device checks pending.

**Exclude:** speculative threading changes or broad rendering rewrites. Scope fixes from evidence.

## 06 — Refresh documentation

**Outcome:** current setup and architecture instructions agree with verified behavior.

**Inspect:** handoffs, README, direct-plugin README, AGENTS.md, USER_MANUAL.md, learning log, SPZ notes.

**Do:** reconcile credentials, package versions, build entry points, architecture, provider capabilities, and limitations. Mark historical material as superseded with links; distinguish implemented, verified, and planned.

**Done when:** a new tester has one consistent setup path; unresolved failures remain explicit. Update relevant plan status without rewriting every historical plan.

## 07 — Extract command handlers incrementally

**Outcome:** one coherent handler extraction with unchanged behavior.

**Inspect:** `VoiceCommandRouter.cs`, `WorldActionDispatcher.cs`, `LocalTypedIntentParser.cs`, speech regression tests.

**Do:** pick one bounded responsibility after the baseline is stable. Preserve reserved verbs, units, targeting precedence, clarification state, and safeguards.

**Done when:** relevant tests pass, ownership is clearer, and no duplicated fallback logic or unrelated scene changes appear. Record the next candidate and stop.

**Exclude:** simultaneous schema/dispatcher redesign or an unlimited refactor.
