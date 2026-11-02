# Headset Holodeck stabilization status

Updated: 2026-09-13, after order 06; packaged for machine handoff on 2026-10-26. This describes the
committed stabilization checkout rather than the original dirty working tree. **Start with this file,
then read [handoff 06](handoff-06.md)** — it is the documentation index: the single tester setup
path, the verified-versus-planned table, the carried forward measurements/findings/pending checks,
and the superseded-handoff index.

## Goal

Make the Quest workflow dependable: launch → speak → create/load → manipulate → save → restart → restore. Prioritize recovery and repeatability before new features.

## Machine handoff snapshot

The handoff commit is the tip of `main` at the time this file was committed; use `git log -1` and
confirm its SHA with `git rev-parse HEAD`.

- The commit includes the accumulated application source, scene/prefab/project settings, vendored
  World Labs changes, tests, setup documentation, stabilization handoffs, and their retained
  evidence.
- It intentionally excludes the project-root `.env`, generated Unity/MCP install files, agent state,
  editor scratch assets, and local device-log exports via `.gitignore`. Those are reproducible or
  machine-specific and are not part of the handoff.
- On the new machine: clone `origin/main`, copy `.env.example` to `.env`, install Unity
  **6000.2.10f1**, open the project, and run `Headset Holodeck > Validate Install`. Then follow
  [README](../../README.md) for the device build and headset workflow.
- No APK is committed. Build a new `-credentialMode device` artifact on the destination machine and
  push the destination machine's own `.env` to the headset.
- Packaging validation on 2026-10-26 reran the documentation claim audit (**84 checks, 0 failures**),
  project whitespace checks, and a value-based staged-secret scan. A fresh Unity EditMode run could
  not start because this source machine has no valid Unity Editor license, so this snapshot relies
  on the retained test evidence in orders 01–05b rather than claiming a new Unity test pass.

## Where we are

- Implemented: text/image world generation, panorama/splat loading, floor/spawn placement, spatial voice commands, hand interaction, object generation/cache, behaviors, audio, and saved worlds.
- Unity: 6000.2.10f1. Main build scene: `Assets/Scenes/Holodeck.unity`. Modified World Labs and Sherpa packages are vendored.
- Baseline 01: HEAD `05a09dd46710d82ac4bf1c91c1488d249872ae3d`; initially 47 modified tracked files, 282 untracked files, no staged files; 9,384 additions / 1,241 deletions. Existing implementation changes were preserved in the local checkout; verification used a temporary copy of this dirty state.
- Current evidence (2026-09-13): order 03 gives every save a journaled write path — temporary file, rotating `world.json.bak`, platform-appropriate atomic replacement, a cross-process writer lock, backup recovery on read, and stage-named failure reporting. Failed and interrupted writes were reproduced against the old direct overwrite (corrupt primary, no backup, 0-byte primary, concurrent "sharing violation" saves) and no longer occur. **32/32 save EditMode tests, 19/19 existing batch entry points, 14/14 manager transitions, 9/9 loader transitions, and 9/9 restore/autosave transitions pass**, with source/copy hash equality, in isolated Unity 6000.2.10f1 runs with external provider networking and credential-file reads blocked. See [handoff 03](handoff-03.md).
- Order 03 re-verification (2026-09-13): the save-write protection requested as "order 04" is this plan's order 03 scope and was already implemented in this checkout. It was independently re-verified rather than re-implemented: **32/32 save EditMode tests, 19/19 batch entry points, 14/14 manager transitions, 9/9 loader transitions, and 9/9 restore/autosave transitions pass**, plus a new legacy-versus-journaled risk probe that reproduces the pre-fix failure modes in isolated fixture directories (truncated primary unrecoverable, 12 concurrent-writer `IOException`s, no backup or lock) and shows none of them on the current writer. See [order 03 re-verification](handoff-03-reverification.md).
- Order 04 (2026-09-13): builds must now declare intent before the project-root `.env` reaches an artifact. **Private** embeds the whole file; **distribution** embeds only an allowlisted set of non-secret `*_BASE_URL`/`*_SEARCH_URL` entries and never a credential-shaped key; **undeclared** stops the build with an actionable error instead of guessing. Excluded key names are listed in the build log, values never are. The embedded copy is written atomically and removed after the build, at editor quit, at editor startup after a crashed build, and before any distribution build. **22/22 credential EditMode fixtures and an 18-check end-to-end fixture through the real build callbacks pass**, alongside **32/32 save tests, 19/19 batch entries, 14/14 manager, 9/9 loader and 9/9 restore/autosave transitions**. The isolated copy was found to still hold a verbatim duplicate of the developer's `.env` from the earlier isolation step; it was deleted unread and the sync step now strips such files. See [handoff 04](handoff-04.md).
- Order 05 (2026-09-13): a **current** build (orders 02–04 included, built `-credentialMode private`) was installed in place on the Quest 3 and driven through the cached-content workflow on hardware: launch → wake word → load cached World Labs world → manipulate → add audio → save + save spawn point → restart → restore. On-device results: the journaled writer produced `world.json` + `world.json.bak` + `.world.json.lock`; a deliberately truncated primary was **recovered on the headset from `world.json.bak`** at startup; a saved spawn point was re-applied after a force-stop restart; the saved audio object and its clips persisted; cached loads stalled **837 ms (6.3 MB spz, n=1)** and **median 950 ms (11 MB spz, n=4)**; idle PSS ~2.35 GB, loaded ~2.66 GB (sampled); host save cost for a 20,000-object document median **2.9 s** because each save writes two durable copies. Frame timing was captured headset-off (median 48 fps) and is **not** a worn result. Read the pending list before treating this as full acceptance. See [handoff 05](handoff-05.md).
- Order 05b (2026-09-13, requested directly): **provider keys now live in the headset's own `.env`, not in the APK.** A third build intent — `device` (`-credentialMode device`, menu `Headset Holodeck > Build Credentials > Device`) — embeds nothing at all, and the runtime reads `Application.persistentDataPath/.env` first: `RuntimeDotEnv` now loads it before the first scene, and the World Labs plugin's `EnvLoader` consults the device file ahead of any baked copy (a baked copy can no longer shadow it). Verified: **28/28 credential fixtures (up from 22), 18/18 end-to-end build-callback checks, 19/19 batch regression entries and 32/32 save tests pass**; the device APK contains **0 of the 10 real credential values** from `.env` (the previous private APK contained all 10 and has been deleted); on the headset both readers logged loading 15 variables from `/storage/emulated/0/Android/data/com.QLC.HeadsetHolodeck/files/.env`; a spoken "open my world(s)" and "load yoyo loose splat test" both worked with no key-missing or auth errors, and the headset's key returned **HTTP 200** from `worlds:list`. See [handoff 05b](handoff-05b-device-env.md).
- Order 06 (2026-09-13): **documentation now matches verified behaviour, and the drift is checkable.** Every claim in `AGENTS.md`, `README.md`, and `USER_MANUAL.md` was reconciled against source and evidence: the build entry point and its arguments, the credential intents and the headset `.env` path (no more `StreamingAssets` copy), the vendored World Labs 1.1.0 and Sherpa 0.1.0 packages, current package versions, the runtime architecture (state machine, Sherpa voice activation, command routing, transition ownership, save journal, object cache/object generation, hand interaction), provider capabilities, and the real limitation list. Historical material is superseded by reference rather than rewritten: every `handoff-NN.md` and its evidence are untouched, and `README_Holodeck_DirectPlugin.md`, `spz-renderer-handoff-notes.md`, and `docs/superpowers/plans/` now point at current sources (the plans directory states that unchecked boxes are not a backlog). A re-runnable audit harness in [evidence-06](evidence-06/) asserts each documented claim against the code — **84/84 checks pass after the edits; the same harness reported 17 failures before them**, which is the measured extent of the drift. Documentation only: no code, scene, prefab, package, or project-setting change. See [handoff 06](handoff-06.md).
- Earlier evidence (2026-09-10): order 02 implements a shared newest-request-wins transition policy, stale-result rejection, owned cleanup, and guarded ActiveConfig commits; **32/32 transition fixtures, 19/19 existing batch entry points, and 15/15 save EditMode tests passed**. See [handoff 02](handoff-02.md). Baseline test fixes remain unchanged; [regression-fix handoff](handoff-01-fixes.md) retains their history. Initial install validation remains 24 checks, 0 failures, 2 optional-provider warnings; it has not been rerun for orders 02–03.
- Android builds **succeed** (Unity exit 0). The current headset artifact is `Builds/stabilization-05b-20260913/Holodeck-order05b-device.apk` (313,580,753 bytes, 15 m 04 s, `-credentialMode device`): it embeds no keys and reads the headset's own `.env`. Every earlier APK in `Builds/` embedded the developer's live keys, so on 2026-09-13 **the order 05 private APK and the six pre-change APKs were deleted** at the developer's request — including `Builds/stabilization-01-20260908/Holodeck-baseline-01.apk`, so the artifact reference in [handoff 01](handoff-01.md) and its build-results record no longer resolve on disk. A value-based sweep found no credential value anywhere in the working tree or the `Library/` cache afterwards; only `.env` itself holds them. Older learning-log results and logcat files remain historical, not current passes. Consider rotating the provider keys that travelled in those artifacts.

## Findings to verify and address

| Priority | Finding | Evidence |
|---|---|---|
| Addressed in 02 | Stale loads/restores could replace the world or ActiveConfig | Both risks reproduced; shared operation ownership/cancellation and 32 passing local transition fixtures now guard activation, cleanup, retries, and config commits. Remote jobs may continue; synchronous local work is cooperative, as documented |
| Addressed in 03 | Interrupted writes may damage a save | `WorldConfigStore` wrote `world.json` with a direct overwrite (no backup, no lock, no recovery). Reproduced: corrupt primary made the save unrecoverable, a torn write was observed at 0 bytes, concurrent writers raised sharing violations. Now temp write + atomic replace + `world.json.bak` recovery; failure-reporting and platform limits documented in [handoff 03](handoff-03.md). Recovery now also observed **on the headset** in [handoff 05](handoff-05.md) |
| Addressed in 04 | Developer credentials enter APKs | `BuildEnvCopier` embedded the whole project-root `.env` in every build. Now gated by declared intent: distribution builds embed non-secret configuration only and can never carry a credential-shaped key; an undeclared build with credentials stops. Reproduced with fake-value fixtures; see [handoff 04](handoff-04.md) |
| 1 | A loose splat file cannot be loaded by voice by bare filename | `TryParseContentPath` only accepts `http(s)://`, `file://` or `/`-rooted paths, so "load yoyo.spz" becomes a saved-world name lookup and fails. The `WorldContent` directory is unreachable by voice. Observed on device in [handoff 05](handoff-05.md) (F1) |
| 2 | One ASR word error defeats a saved-world load | Name matching needs an exact/prefix/substring hit on the normalised display name; "Zebra World Lab Test." (missing "s") matched nothing, with no fallback or clarification. [handoff 05](handoff-05.md) (F2) |
| 3 | Wake-word misses (the alias list itself is deliberate) | `VoiceActivationConfig.asset` intentionally aliases `computer`/`pewter`/`pudor`/`peter`/`impewter`/`pumpor` from real-world testing, so those accepts are by design. The residual issue is near-misses that were **rejected**: `FUDOR` 6×, `LETTER` 4×, `FUTURE` 3×, `PUDER`/`FUDER` 2× each, plus `TUDOR`/`THEOR`/`MUTOR`/`LITER`. A more robust STT front end is deferred by agreement. [handoff 05](handoff-05.md) (F3) |
| 3 | "Capture panorama" routes to the panorama view, so no fidelity artifact is produced for local splat worlds | `Panorama.` parsed as `ShowPanoWorld` → "Panorama is not available for this world."; a cached splat world has no `cached_pano`. [handoff 05](handoff-05.md) (F4) |
| Addressed in 05b | Credentials were baked into the APK | `-credentialMode private` embedded the whole `.env` (12 `<KEY>=` markers, all 10 real values). A `device` intent now embeds nothing and the runtime reads the headset's own `.env` first; the device APK carries 0 of 10 real values. [handoff 05b](handoff-05b-device-env.md) |
| 4 | Rendering has device measurements, but only headset-off | Cached loads stall 0.84–1.0 s; app frame submit median 17.6 ms, CPU&GPU median 29.4 ms, 88 % of 1 Hz samples below 60 fps **with the headset on a desk** (compositor throttled, `Prd` 57 ms). SPZ decode and floor analysis still run synchronously on the Unity thread. Worn-headset timing, peak memory (sampled max 2.67 GB PSS) and switches-under-load remain to be measured. [handoff 05](handoff-05.md) |
| 4 | Save cost on large worlds | Each save writes two durable copies: a 20,000-object (5.7 MB) document took median 2.9 s (max 8.1 s) on the host, and autosave runs on the main thread. [handoff 05](handoff-05.md) |
| Addressed in 06 | Documentation was stale | `AGENTS.md` described a hardcoded OpenAI key, a `StreamingAssets/.env` copy and three outdated package versions; the direct-plugin README repeated the same credential story; the manual called object generation "not connected" after two providers shipped; the deliberate wake-alias set read like a bug. Reconciled against code, superseded by reference, and now guarded by a re-runnable audit: **17 failures before the edits, 0 after**. See [handoff 06](handoff-06.md) |
| 6 | Command routing is expensive to maintain | Dispatcher approximately 2,700 lines; local parser approximately 2,100 lines |

Baseline regression failures are resolved, and the transition, save and credential risks were reproduced and fixed with local fixture verification. Order 05 then ran a current build on the Quest 3 and produced the first headset acceptance and performance numbers, plus four reproducible device findings (above). Save atomicity is now verified on macOS and, for the recovery path, on Android; the write-side kill, the read-only migration, offline behaviour, tracking loss and worn-headset timing remain unverified. The current project is an integrated prototype undergoing stabilization.

## Execution order

See [work orders](WORK-ORDERS.md). **Orders 01–06 are complete**, including the baseline
regression-fix follow-up and the order 05b credential follow-up. Order 07 is optional and has not
been started. Order 05's untested device items and order 06's carried-forward gaps are listed in
[handoff 06](handoff-06.md).

| Order | Task | Effort | Status |
|---|---|---|---|
| 01 | Establish current baseline | High | Complete — build passed; regression follow-up green |
| 02 | Stabilize world transitions | Ultra | Complete — 32 transition fixtures and existing regressions pass locally |
| 03 | Protect saves | High | Complete — 32 save tests, 19 batch entries, and 32 order 02 transition fixtures pass locally; failed/interrupted writes reproduced and fixed |
| 04 | Define distributed-build credentials | High | Complete — 22 credential fixtures and an 18-check end-to-end fixture pass; distribution builds provably exclude credentials and unrelated entries |
| 05 | Quest acceptance and performance measurement | High | Complete — current private build installed on Quest 3; cached-content workflow, journaled save, on-device backup recovery and restart-restore observed; measurements taken headset-off. Pending: offline behaviour, tracking loss, kill mid-write, read-only migration, worn-headset timing |
| 05b | Put provider keys in the headset's `.env`, not the APK | Medium | Complete and verified end to end — `device` intent embeds nothing; runtime reads the device `.env` first; 28/28 credential fixtures, 18/18 e2e, 19/19 batch entries, 32/32 save tests; device APK carries 0 of 10 real values; on-device voice flow and a live provider call both succeeded from the headset's own `.env` |
| 06 | Refresh documentation | Medium | Complete — docs reconciled against code; every handoff superseded by reference; audit harness 84/84 after 17 failures before; documentation only. See [handoff 06](handoff-06.md) |
| 07 | Extract command handlers | High | Optional after stability |

Defer new providers, multiplayer, and web-port expansion during stabilization. Performance fixes should follow measurements.

**Order numbering note:** the task that requested "order 04" first described the save-write protection this plan lists as order 03; that scope was verified as already complete ([re-verification](handoff-03-reverification.md)) and the plan's order 04 was then confirmed and completed. Order 05b was a bounded follow-up to order 04 run between orders 05 and 06 and does not renumber anything. **Order 07 remains unstarted.** Save, transition, and credential fixtures plus the selected local regressions are green, and one headset acceptance pass has run against a current build. Still unverified: the order 05 pending list, wider package tests, PlayMode suites, atomic replacement on Windows/iOS, and any **distribution** build with the credential boundary applied. The proposed performance targets in [handoff 05](handoff-05.md) are **unagreed**. Do not automatically begin later orders.

## How to use this with minimal repeated context

1. Start a fresh task from the latest `origin/main` handoff commit for the selected work order. Use
   `git log -1` to pin the revision the task actually ran against.
2. Give it this file and its work-order number. Add [handoff 06](handoff-06.md) when the task needs the
   setup path, the verified-versus-planned table, the carried-forward measurements/findings/pending
   checks, or the superseded-handoff index. No need to paste the previous conversation or the design
   documents.
3. **Next order: 07 only, and only if 07 is explicitly requested.** Order 06 is complete; there is no
   queued successor. The open order-05 items are a device checklist, not an order, and the proposed
   performance targets are unagreed. Nothing starts automatically.
4. Reading order for a new tester or a new task: this file → [handoff 06](handoff-06.md) → the
   `handoff-NN.md` that owns the relevant area. Treat older handoffs as historical: their text and
   evidence are frozen and superseded by reference, so a statement that contradicts `handoff-06`,
   `README.md`, or `AGENTS.md` is a stale record, not current behaviour.
5. Run overlapping code changes sequentially. Use subagents only for independent, bounded questions,
   not duplicate project reviews.
6. Each task reads only relevant code, runs required checks, and updates this queue plus a short
   handoff. Re-run `bash docs/stabilization/evidence-06/docs-claim-audit.sh all` after any change that
   touches setup, architecture, package versions, or the credential path.
7. Do not discard/stash unrelated work, print credentials, or invoke paid generation during routine
   checks. Record blockers rather than claiming unrun checks passed.

Handoff file: `docs/stabilization/handoff-NN.md`. Include outcome, revision/dirty-state context, files
changed, checks actually run with log paths, remaining risks, and next action. Include a commit ID
only if actually committed.

## Supporting context

- [Handoff 06](handoff-06.md): the documentation index — single setup path, verified-versus-planned
  table, carried-forward measurements/F1–F4/pending device checks, and the superseded-handoff index.
- [Learning log](../project-learning-log.md): recent decisions and reported verification.
- [README](../../README.md): the tester setup and install path (device credential intent → headset
  `.env` → install → wake → cached load → save → restart → restore → logs).
- [AGENTS.md](../../AGENTS.md): architecture map for agents working in this repo.
- [Intent](../intent.md): the speech-intent contract — command fields, every `VoiceIntentType` with
  an example utterance, worked examples, the routing/override rules, and the add-an-intent
  checklist.
- [User manual](../../USER_MANUAL.md): feature overview. Its limitations section was reconciled in
  order 06, so it is no longer the stale document it used to be.
- [SPZ handoff](../../spz-renderer-handoff-notes.md): historical investigation with a status banner;
  its agreed changes are already implemented and the rest is unstarted ideas, not a backlog.
- Reference docs: [`README_Holodeck_DirectPlugin.md`](../../README_Holodeck_DirectPlugin.md) is
  superseded as a setup guide (historical banner inside). Consult individual
  `docs/superpowers/specs/` and `plans/` only as needed — unchecked boxes are not a reliable backlog
  ([plans README](../superpowers/plans/README.md)).
