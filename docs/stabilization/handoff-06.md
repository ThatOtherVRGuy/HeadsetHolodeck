# Order 06 — Refresh documentation

> **Machine-handoff addendum (2026-10-26):** this page was produced before order 06 was committed.
> The completed stabilization checkout has since been committed and pushed on `main`. The
> preservation statements below describe the pre-packaging dirty state and remain historical
> evidence; use [STATUS.md](STATUS.md) for the committed handoff snapshot.

Completed 2026-09-13 (America/Los_Angeles) in the current local checkout. Scope: order 06 only.
**Documentation only** — no code, scene, prefab, package, or project-setting change; no APK rebuild,
no paid generation, no commit, push, stash, reset, or credential change. HEAD is still
`05a09dd46710d82ac4bf1c91c1488d249872ae3d`.

Numbering: order 05b was a bounded follow-up to order 04's credential boundary run between 05 and 06.
It does not renumber anything.

## Outcome

The current setup and architecture instructions now agree with verified behaviour:

- **One setup path.** `README.md` (with the same path restated below and in `AGENTS.md`) is the only
  place a new tester needs to follow: device credential intent → `adb push .env` → install → wake
  word → cached-content load → save/restart/restore → read the logs.
- **Implemented, verified, and planned are separated.** The verified-versus-planned table below is the
  single index of what has been demonstrated and where; `STATUS.md` carries the live queue.
- **Claims were reconciled against code, not summarised from prose.** Every architecture, credential,
  package, provider, and file-path statement in the rewritten docs is matched to a source file or
  evidence artifact, and the matching is executable
  ([audit harness](evidence-06/docs-claim-audit.sh)).
- **Historical material is superseded by reference, not rewritten.** Every `handoff-NN.md` and its
  evidence directory are untouched; `handoff-06` is the index that supersedes them. The direct-plugin
  README, the SPZ notes, and the plans directory now carry explicit historical/superseded banners that
  point at current sources; the plans directory also states that unchecked boxes are not a backlog.
- **Known-stale points fixed** — `AGENTS.md`'s `StreamingAssets/.env` copy and hardcoded-key
  instructions, three stale package versions, the "wake alias list looks like a bug" framing, and the
  "object generation is not connected"/"hand interaction is only pointing" understatements in
  `README.md` and `USER_MANUAL.md`.

No discrepancy required a code change, so nothing was stopped or escalated; two documentation
discrepancies that must **not** be fixed by editing (a broken link inside a handoff and a stale
serialized identifier in the scene) are recorded under *Remaining gaps*.

## Files changed

| File | Change |
|---|---|
| `AGENTS.md` | Rewritten stale sections: two current pipelines, real build entry point and arguments, the credential-intent table with the device `.env` path, the headset workflow and save/log locations, the deliberate wake-alias set, new architecture sections (voice activation, command routing, transition ownership, save journal, object cache/generation, hand interaction), corrected scene wiring, corrected package versions, vendored-package statement, documentation map |
| `README.md` | Single setup path; corrected object-generation keys (3dAIStudio Tripo + HiTem implemented; Meshy/Tripo reserved); new "Building For The Headset" and "Headset Run Flow" sections (device build, `adb push`, install, wake, cached load, save/restart/restore, save layout, log commands); "Current Verified State" with the headset-off caveat and the unverified list; feature inventory that names hand input and generated objects |
| `USER_MANUAL.md` | Corrected wake-alias list and its deliberate rationale; corrected object-generation capabilities and reserved keys; new "How Saves Are Written" (journal, backup recovery, save path, restart restore); new "Hands And Direct Manipulation" (hand pointing, grab world, two-hand scale, snapping); corrected limitations (headset `.env`, unverified worn timing, unverified kill-mid-write, F1/F2 by reference) |
| `README_Holodeck_DirectPlugin.md` | Superseded banner with the current credential behaviour and pointers to current docs; body left as the historical record |
| `spz-renderer-handoff-notes.md` | Status banner: historical, changes landed (`m_SHOrder` 0 for loose splats via `RuntimeSplatFloorLoader.GetSHOrderForSource`, `m_OptimizeForQuest` on Android), remaining ideas not a backlog, no fidelity claim |
| `docs/superpowers/plans/README.md` | New: plans/specs are historical design records; unchecked boxes are not a backlog; `docs/stabilization/` owns current status |
| `docs/project-learning-log.md` | New entry recording the drift, the one-document-owns-verified-state decision, and the executable claim audit |
| `docs/stabilization/handoff-06.md`, `STATUS.md`, `docs/stabilization/evidence-06/` | This record, the updated queue, and the audit output |

Nothing in `Assets/`, `Packages/`, `ProjectSettings/`, scenes, prefabs, or `.env` was modified.

## The single setup path for a new tester

Editor-side preparation (once):

1. Install Unity **6000.2.10f1** through Unity Hub and open this folder.
2. Copy `.env.example` to `.env` in the project root and fill in the keys you have. Required for the
   main path: `OPENAI_API_KEY`, `WORLDLABS_API_KEY`. Optional: `PIXABAY_API_KEY`,
   `FREESOUND_API_KEY`, `XENO_CANTO_API_KEY`, `THREEDAISTUDIO_API_KEY`,
   `HITEM_ACCESS_KEY`+`HITEM_SECRET_KEY`. `MESHY_API_KEY`/`TRIPO_API_KEY` are reserved and enable
   nothing on their own; Openverse audio search needs no key.
3. Run `Headset Holodeck > Validate Install`. It checks the scene, packages, Sherpa models,
   manifest permissions, and key presence, and never prints a value.
4. Press Play to use the Editor, or continue below for the headset.

Headset, end to end:

1. **Declare intent.** `Headset Holodeck > Build Credentials > Device`, or build with
   `-credentialMode device`. Device builds embed **nothing**.
2. **Build.**

   ```bash
   Unity -batchmode -quit -projectPath <repo> \
     -executeMethod Holodeck.Editor.CommandLineAndroidBuild.BuildApk \
     -outputPath Builds/Holodeck-device.apk \
     -credentialMode device \
     -logFile /tmp/holodeck-device-build.log
   ```

   This is the project's only command-line build entry point. `-outputPath` defaults to
   `Builds/Holodeck.apk`. A build takes roughly 15–35 minutes.
3. **Install.** `adb install -r Builds/Holodeck-device.apk` — an update in place keeps saved worlds.
4. **Give the headset its keys, once:**

   ```bash
   adb push .env /sdcard/Android/data/com.QLC.HeadsetHolodeck/files/.env
   ```

   Startup confirms it with `[RuntimeDotEnv] Loaded <n> variable(s) from …` and
   `[EnvLoader] Loaded <n> variables from '…'`.
5. **Launch and wake it.** Say "Computer, …". The accepted wake list is the deliberate alias set
   `computer`, `pewter`, `pudor`, `peter`, `impewter`, `pumpor`; push-to-talk (Space or the right
   controller secondary button) still works.
6. **Load cached content first.** "Computer, load `<saved world name>`" restores the cached splat,
   world transform, objects, audio, and spawn point.
7. **Save.** "Computer, save this world" (or the Ops panel). The save is journaled: temporary file →
   rotate the previous generation to `world.json.bak` → atomic rename over `world.json`.
8. **Restart and restore.**

   ```bash
   adb shell am force-stop com.QLC.HeadsetHolodeck
   adb shell am start -n com.QLC.HeadsetHolodeck/com.unity3d.player.UnityPlayerGameActivity
   ```

   The activity name is the one declared in `Assets/Plugins/Android/AndroidManifest.xml`; opening the
   app from the headset's launcher does the same thing.

   Loading the same world again restores transform, spawn point, objects, and audio. If the primary
   save was interrupted, startup logs `recovered the previous generation from '…/world.json.bak'`.
9. **Where saves live.** `…/files/Worlds/<config_id>/world.json`, beside `world.json.bak` and
   `.world.json.lock`; cached splats/panoramas under `<Worlds>/CachedWorlds`, cached generated
   objects under `<Worlds>/CachedObjects`.
10. **Read the logs.**

    ```bash
    adb logcat -v time -s Unity          # [WorldConfigStore], [WorldConfigRestorer],
                                         # [WorldConfigAutoSave], [RuntimeDotEnv], [EnvLoader], speech
    adb logcat -v time -s Unity VrApi    # adds 1 Hz compositor frame timing
    adb shell dumpsys meminfo com.QLC.HeadsetHolodeck
    ```

## Verified versus planned

Status vocabulary: **Verified** = demonstrated in this checkout with retained evidence;
**Host-verified** = demonstrated on macOS in an isolated copy, not on headset hardware;
**Implemented, unverified** = code present and locally exercised but the claim has not been
demonstrated end to end; **Planned/deferred** = agreed intent, no implementation.

| Area | Status | Evidence |
|---|---|---|
| Unity version 6000.2.10f1 and `Assets/Scenes/Holodeck.unity` entry scene | Verified | `ProjectSettings/ProjectVersion.txt`; [handoff 01](handoff-01.md), [order-05 device context](evidence-05/device-context.txt) |
| Command-line build `Holodeck.Editor.CommandLineAndroidBuild.BuildApk` with `-outputPath` and `-credentialMode device\|private\|distribution` | Verified | `Assets/App/Editor/CommandLineAndroidBuild.cs`; `Packages/.../BuildEnvCredentials.cs`; [order-05 build](evidence-05/device-context.txt), [order-05b build](handoff-05b-device-env.md) |
| Vendored `com.worldlabs.gaussian-splatting` 1.1.0 and `com.ponyudev.sherpa-onnx` 0.1.0 (`file:` deps) | Verified | `Packages/manifest.json`; [order-01 static inventory](evidence-01/static-inventory.txt) |
| Credential boundary: private embeds all, distribution embeds only non-secret URL config, undeclared stops, device embeds nothing | Verified | 28/28 credential fixtures + 18/18 end-to-end build-callback checks; device APK carries 0 of 10 real values — [handoff 04](handoff-04.md), [handoff 05b](handoff-05b-device-env.md) |
| Headset alone supplies the keys: `RuntimeDotEnv` + `EnvLoader` read `Application.persistentDataPath/.env` first | Verified on hardware | 15 variables loaded from the device path by both readers; spoken commands and a live provider call succeeded — [handoff 05b](handoff-05b-device-env.md), [device log](evidence-05b/device-credential-resolution.txt) |
| State machine, voice activation, command routing, hand input, object generation, object cache, save journal, transition ownership | Implemented and locally exercised | Code + device scene wiring (see the reconciliation table below); transition and save suites green |
| World transitions: newest request wins, no stale activation or `ActiveConfig` overwrite | Host-verified | 32/32 transition fixtures — [handoff 02](handoff-02.md) |
| Save journal: temp + rotating backup + atomic replace + writer lock + backup-aware read | Host-verified (recovery also Verified on hardware) | 32/32 save tests, 19/19 batch entries; on-device truncation recovery — [handoff 03](handoff-03.md), [handoff 03 re-verification](handoff-03-reverification.md), [handoff 05](handoff-05.md) |
| Headset acceptance: install, wake word, cached load, manipulate, add audio, save, restart, restore, backup recovery | Verified on hardware (one Quest 3 session) | [handoff 05](handoff-05.md), [save/restore evidence](evidence-05/save-restore-device.txt) |
| Performance targets listed in order 05 | **Planned / unagreed** — proposals only | [handoff 05](handoff-05.md); the measurement table below |
| Kill-mid-write on headset hardware | Planned (host only) | 6/6 host `SIGKILL` trials left a loadable save — [verify.txt](evidence-05/verify.txt) |
| Read-only legacy-folder migration on hardware | Planned (host only) | fixture passes on macOS; headset attempt not completed — [handoff 05](handoff-05.md) |
| Offline/network-failure behaviour; tracking loss; worn-headset timing; rapid A→B/cancel under load | Planned | [handoff 05 device checklist](handoff-05.md) |
| Distribution APK actually built and inspected | Planned | boundary verified at the embed level; no distribution artifact produced — [handoff 04](handoff-04.md) |
| Atomic replacement on Windows/iOS/visionOS | Planned | `File.Replace`/`File.Move` branch not compiled or run here |
| F1–F4 device findings | Open findings, observed on hardware (F3 is an observation, not a defect) | below |
| Provider key rotation after the deleted key-bearing APKs | Recommended, developer action | [historical APK cleanup](evidence-05b/historical-apk-cleanup.txt) |
| Order 07 command-handler extraction | Optional, not started | [WORK-ORDERS.md](WORK-ORDERS.md) |

### Reconciliation notes (claim → verified behaviour)

| Claim | Reconciliation |
|---|---|
| `.env` "is copied to `StreamingAssets/.env` for builds" | **Removed.** `BuildEnvCopier` writes `Assets/Resources/WorldLabs/worldlabs_env.txt` (atomically, then removes it) only for `private`/`distribution` intent; `EnvLoader` resolves explicit path → device `.env` → project-root `.env` (desktop) → embedded copy → process environment. No `StreamingAssets` reference remains in either file. |
| "Replace `REPLACE_WITH_YOUR_OPENAI_KEY` in `HolodeckDirectSecrets.cs`" | **Removed.** `HolodeckDirectSecrets.OpenAiApiKey` is an empty `const string` and unused; keys come from `.env`. `OpenAITranscriptionClient` still rejects an empty key or one containing `REPLACE_WITH`. |
| Wake-word list | **Re-framed as deliberate.** `VoiceActivationConfig.asset` ships exactly `computer`, `pewter`, `pudor`, `peter`, `impewter`, `pumpor` (StartsWith, 0.171 confidence). Accepted aliases are by design; the residual issue is the *rejected* near-misses listed under F3. |
| "Object generation from image is scaffolded but not connected" | **Corrected.** `ObjectGenerationService` drives two providers: 3dAIStudio Tripo (image-to-3D + text-to-3D, credit preflight) and HiTem (image-to-3D), with a persistent object cache. `MESHY_API_KEY`/`TRIPO_API_KEY` are reserved and enable nothing alone. |
| Hand interaction described only as pointing/gaze | **Corrected.** XR Hands + XRI hand-only input (pinch/grip), hand pointing sources, `WorldManipulationController` one-/two-hand world move-rotate-scale, and snap-while-grabbed are implemented and present in the scene. |
| Package versions in `AGENTS.md` | **Corrected** to the manifest: XR Interaction Toolkit 3.5.0, OpenXR 1.17.0-pre.2, XR Hands 1.7.3, URP 17.2.0, Input System 1.15.0, Android XR/OpenXR 1.0.2; AR Foundation 6.3.4 added. |
| "The World Labs plugin is not tracked in this repo" | **Corrected.** Both modified packages are vendored under `Packages/` and referenced as `file:` dependencies. |
| In-repo scene hierarchy | **Corrected** to the actual roots (`Me`, `Systems`, `Interactables`, `Environment`, `Teleport Area Setup`, `Lighting`, `floorcollider`, `EventSystem`, `Hands Permissions Manager`, `GSplatRenderer`, `MeshRoot`, `GeneratedWorldRoot`) and the two component hosts (`Systems`, `SpeechIntent`) plus `CachedObjectStore`. |

## Carried forward: order 05 measurements (method and caveat)

Method is recorded per number; the full record is [measurements.txt](evidence-05/measurements.txt).
Tools: `adb logcat -v time` (app `Unity` lines and the app process's own `VrApi` 1 Hz summaries),
`adb shell dumpsys meminfo`, and the app-kill probe's own timing lines. **Every device sample was
taken with the headset sitting on a desk, headset-off**, so the frame-rate figures bound the app's own
cost and are *not* a worn-headset result; load stalls and memory are affected less by that.

| Measurement | Value | Method / sample count |
|---|---|---|
| Cached World Labs world load stall | 837 ms | app log `Restoring`→`Restore complete`, 6.3 MB spz, n=1 |
| Cached loose-splat load stall | median 950 ms (903–1001 ms) | same, 11 MB spz, n=4 |
| Frame rate, headset-off | median 48 fps, p95 72, max 73; 88 % of samples below 60 fps | app `VrApi` 1 Hz lines, n=472, ~8 min |
| App frame submit time | median 17.6 ms, p95 85.4 ms, max 865.5 ms | same samples |
| CPU&GPU frame time | median 29.4 ms, p95 92 ms, max 100 ms | same samples |
| Predicted display period | median 57 ms (compositor throttled headset-off) | same samples |
| Cold start PSS | 277 MB after install; 2.23 GB after a force-stop restart | `dumpsys meminfo`, single samples |
| Idle PSS (listening) | ~2.35 GB, stable to <1 MB | `dumpsys meminfo`, 14 samples every 25 s |
| PSS after loading the fixtures | ~2.66 GB / ~2.64 GB; sampled peak 2.67 GB | `dumpsys meminfo`, sampled |
| Save cost, 20,000-object / 5.7 MB document | median 2.9 s (1.9–8.1 s) on the host | journaled writer's own timings, n=21 |

**Proposed performance targets — unagreed.** They are reproduced here so they are not mistaken for
agreements; the developer has not confirmed any of them.

| Proposed target | Value | Why it is proposed |
|---|---|---|
| Cached splat load stall (≤12 MB spz, worn) | ≤ 1.5 s to first rendered frame, no single frame > 100 ms after it | current 0.84–1.0 s headset-off; synchronous SPZ decode + floor analysis dominates |
| Sustained frame rate, worn | ≥ 68 fps average, ≤ 5 % of frames > 16.7 ms | headset-off medians cannot be judged without a worn run |
| Peak memory | ≤ 2.8 GB PSS, ≤ +150 MB growth across 5 consecutive switches | idle 2.35 GB, loaded 2.66 GB sampled; switch slope not isolated |
| Save latency | ≤ 500 ms at 2,000 objects, ≤ 2 s at 20,000, never on the frame that consumes a voice command | host measures 1.9–8.1 s at 20,000 because each save writes two durable copies |
| Recovery | 100 % of interrupted saves load the previous generation; zero unrecoverable states | already met in host kill trials and the on-device recovery test |

Rendering fidelity and speed stay separate: no fidelity claim is made. The only fidelity artifact
attempted (a captured panorama) failed for the reason in F4.

## Carried forward: findings F1–F4 (with repro steps)

All four were observed on hardware and are recorded in full in [handoff 05](handoff-05.md); they
remain open (none was in order 06's scope to fix). F3 is a wake-word observation rather than a defect.

**F1 — a loose splat cannot be loaded by voice by bare filename.**
`LocalTypedIntentParser.TryParseContentPath` only accepts a content path that *starts with*
`http://`, `https://`, `file://`, or `/`, so `load yoyo.spz` never reaches `LoadSplat` and falls into
the saved-world name lookup. The documented `WorldContent` fallback directory is therefore
unreachable by voice.
*Repro:* put `WorldContent/yoyo.spz` on the device, say "computer, load yoyo.spz".
*Observed:* `[WorldActionDispatcher] LoadWorldConfig: no saved config matching 'yoyo.spz'`, no load.

**F2 — one ASR word error defeats a saved-world load.**
`FindBestDisplayNameMatch` requires an exact, prefix, or substring hit on the normalised display
name, with no fallback or clarification.
*Repro:* say a saved world's name with one word mis-heard ("Zebra World Lab Test." for
"Zebra World Labs Test").
*Observed:* `LoadWorldConfig: no saved config matching 'Zebra World Lab Test.'`, nothing loaded.

**F3 — wake-word near-misses are rejected (the alias list itself is deliberate).**
Accepted aliases are working as designed (`pewter` ×13, `impewter` ×6, `pudor` ×2, `computer` ×1 in
the order-05 session). The residual issue is the opposite direction: words the speaker intended as
"computer" that were rejected — `FUDOR` ×6, `LETTER` ×4, `FUTURE` ×3, `PUDER` ×2, `FUDER` ×2, plus
`TUDOR`, `THEOR`, `MUTOR`, `LITER`. Widening the alias list or replacing the STT front end is the
fix; both are deferred by agreement.

**F4 — "capture panorama" routes to the panorama view, so no fidelity artifact is produced.**
`Panorama.` parses as `ShowPanoWorld` → `[ViewModeController] RequestPanoView — no panorama available`
and "Panorama is not available for this world." A locally cached splat world has no `cached_pano`.
*Repro:* say "computer, capture panorama" on a cached splat world. Say "capture the panorama" on a
world that has one to exercise the capture path.

## Carried forward: pending device checks

Each still needs an awake or worn headset, and each is independent:

1. **Offline behaviour** — disable Wi-Fi (`adb shell svc wifi disable`), speak a command, expect a
   visible/spoken error rather than a hang; re-enable Wi-Fi afterwards.
2. **Kill mid-save on hardware** — with a decorated world loaded, `am force-stop` inside the save
   window, then restart and confirm the world still loads. Host-verified only today.
3. **Read-only legacy migration** — `chmod 555` a fixture folder with no writable copy, then save;
   expect `Migrated read-only config …` and the original files retained. Host-verified only today.
4. **Rapid loads and cancel** — trigger A→B within a second, then cancel an in-flight load and
   confirm the finished world is the one that stays.
5. **Tracking loss** — cover the front cameras ~5 s, uncover, confirm world/spawn/audio survive.
6. **Suspend/resume** — system menu and power button; confirm the app resumes into the same world.
7. **Worn-headset performance** — repeat one cached load per fixture worn and record frame timing and
   memory (OVR Metrics Tool or in-app metrics); capture a fidelity panorama on a world that has one.

Also unverified: performance target agreement, a real `distribution` APK, atomic replacement on
Windows/iOS/visionOS, wider package tests, PlayMode suites, and audio playback audibly confirmed after
a restart (the persisted object and completed restore were observed).

## Carried forward: credential hygiene

- On 2026-09-13, at the developer's request, **seven key-bearing APKs were deleted** after each was
  scanned for the real `.env` values (names and counts only): `Builds/Holodeck.apk`,
  `Builds/stabilization-01-20260908/Holodeck-baseline-01.apk`,
  `Builds/Holodeck-CodexBuildCheck.apk`, `Builds/Holodeck-CodexTest.apk`,
  `Builds/Holodeck-XRITK350.apk`, `Builds/Holodeck-main-xritk350.apk`, and `Builds/HolodeckPico.apk`.
  About 2 GB was freed. The retained artifact is the key-free device build
  `Builds/stabilization-05b-20260913/Holodeck-order05b-device.apk` (0 of 10 real values; two public
  base URLs that are also compiled code defaults). Record:
  [historical-apk-cleanup.txt](evidence-05b/historical-apk-cleanup.txt).
- **Key rotation is still recommended** for the OpenAI, World Labs, Pixabay, Freesound, xeno-canto,
  Meshy, Tripo/3dAIStudio, and HiTem credentials, because those artifacts may have been copied. This
  is outside the repository's control.
- The checkout's only key-bearing file remains the gitignored project-root `.env`; the headset has its
  own copy. No credential value appears in any document or evidence file in this order (checked by the
  audit harness, which compares by key name and count only).

## Superseded handoff index

Every handoff keeps its original text and evidence; this table is the superseding reference. Nothing
below was edited.

| Handoff | Original scope | Standing now |
|---|---|---|
| [handoff-01](handoff-01.md) + [evidence-01](evidence-01/) | Baseline inventory, first build, first regression failures | Historical. Superseded for current status by this handoff and `STATUS.md`. Its captured baseline failures are fixed; its artifact reference (`Builds/stabilization-01-20260908/Holodeck-baseline-01.apk`) was deleted in the 05b cleanup, so the link intentionally does not resolve. |
| [handoff-01-fixes](handoff-01-fixes.md) + [evidence-01-fixes](evidence-01-fixes/) | Regression fixes for the three baseline failures | Historical, and incorporated. |
| [handoff-02](handoff-02.md) + [evidence-02](evidence-02/) | Transition ownership, stale-result rejection, `ActiveConfig` guards | Historical record of work that is current. Architecture summary now lives in `handoff-06` and `AGENTS.md`. |
| [handoff-03](handoff-03.md) + [evidence-03](evidence-03/) | Journaled save writer | Historical record of work that is current. Recovery semantics restated in `handoff-06`, `AGENTS.md`, and `USER_MANUAL.md`. |
| [handoff-03-reverification](handoff-03-reverification.md) + [evidence-03-reverification](evidence-03-reverification/) | Independent re-verification that save protection was already present | Historical; resolves the order-numbering conflict. |
| [handoff-04](handoff-04.md) + [evidence-04](evidence-04/) | Build credential boundary (private / distribution / undeclared) | Historical record of work that is current, and extended by 05b. Superseded as the operating guide by `README.md` and `AGENTS.md`. |
| [handoff-05](handoff-05.md) + [evidence-05](evidence-05/) | Quest acceptance, measurements, findings F1–F4 | Historical for method and raw evidence. Its findings, measurements, caveats, unagreed targets, and pending checks are carried forward above; treat `handoff-06` as the index. |
| [handoff-05b-device-env](handoff-05b-device-env.md) + [evidence-05b](evidence-05b/) | Provider keys in the headset's own `.env`, never in the APK | Historical record of work that is current. The headset `.env` workflow is restated in `README.md`, `AGENTS.md`, and above. |

## Verification actually run

Documentation-only audit; **no Unity run, no batch test, no build, and no device access** in this
order.

| Check | Result | Evidence |
|---|---|---|
| Docs-versus-code claim audit, final | **84 checks, 0 failures** | [docs-claim-audit.txt](evidence-06/docs-claim-audit.txt) |
| Docs-versus-code claim audit, before the edits | **84 checks, 17 failures**, every one a doc-text check in the stale points this order fixed: 5 in `AGENTS.md` (three package versions, the `StreamingAssets/.env` claim, the missing three-intent table), 3 alias-set checks (`README.md`, `AGENTS.md`, `USER_MANUAL.md`), 3 in `USER_MANUAL.md`, 1 in the direct-plugin README, and 5 in documents this order created or rewrote. No code-side check failed — the drift was entirely in the prose. This pre-edit run was observed in-session and is recorded in the negative-control file rather than retained as its own artifact | [negative control](evidence-06/docs-claim-audit-negative-control.txt) |
| Negative control (does the audit detect drift?) | In a scratch tree, reverting only `AGENTS.md` and `README_Holodeck_DirectPlugin.md` to HEAD produces **7 failures**, exactly the stale claims in those two files; all other checks pass. `.env` was deliberately not copied, so the value scan reports "no .env present" (76 checks instead of 84) | [negative control](evidence-06/docs-claim-audit-negative-control.txt) |
| Markdown link check (all in-repo `.md`, local targets) | One broken reference, pre-existing and inside a handoff that must stay as-is: `handoff-05b-device-env.md` → `evidence-05b/cred-e2e-02-results.txt` (the retained file is `cred-e2e-02-summary.json`) | [link-check.txt](evidence-06/link-check.txt) |
| Secret-value scan of the changed documents (values read in memory; key names and counts only) | No credential value present in `AGENTS.md`, `README.md`, `USER_MANUAL.md`, `README_Holodeck_DirectPlugin.md`, `spz-renderer-handoff-notes.md`, `.env.example`, `STATUS.md`, `handoff-06.md`, or the learning log | Section "Secret hygiene of the changed documents" in [docs-claim-audit.txt](evidence-06/docs-claim-audit.txt) |
| Harness | Kept so the audit can be re-run: `bash docs/stabilization/evidence-06/docs-claim-audit.sh claims\|links\|all` | [docs-claim-audit.sh](evidence-06/docs-claim-audit.sh) |

## Remaining gaps and known documentation issues

- **Broken link inside a handoff (not fixed by design).** `handoff-05b-device-env.md` links
  `evidence-05b/cred-e2e-02-results.txt`; the retained artifact is
  `evidence-05b/cred-e2e-02-summary.json`. Handoffs stay as-is per the order, so this is recorded
  here rather than edited. The e2e result itself is intact in the summary and log.
- **Stale serialized namespace in the scene (not fixed by design).** `Assets/Scenes/Holodeck.unity`
  contains `m_EditorClassIdentifier` strings like `Assembly-CSharp::WorldLens.State.HolodeckStateMachine`
  while the scripts now declare `Holodeck.State` / `SpeechIntent.*`. Unity resolves components by
  script GUID, so the wiring is intact and the string is rewritten on the next scene save; editing it
  by hand is out of scope and could corrupt the scene.
- **No code was changed, so no code discrepancy remains outstanding.** The reconciliation found no
  claim that required a behaviour change.
- **Still unverified:** the seven device checks above, performance-target agreement, a real
  distribution APK, Windows/iOS/visionOS atomic replacement, wider package and PlayMode suites, and
  audible audio after restart. These are labelled **Planned** in the verified-versus-planned table,
  not claimed.
- **Open findings F1–F4** block two workflow steps outright (loose-splat filename by voice; saved
  world name match after an ASR error). Whether they become a follow-up order is a developer decision.
- **The order queue's "later orders" note still applies:** do not start order 07 automatically.

## Preservation

- HEAD is still `05a09dd46710d82ac4bf1c91c1488d249872ae3d`. No commit, push, stash, reset, or worktree
  replacement; nothing staged.
- `git status --porcelain` reported **112** entries at the start of this order and **116** at the end.
  The four added entries are exactly the four previously-clean tracked files this order had to edit —
  `AGENTS.md`, `USER_MANUAL.md`, `README_Holodeck_DirectPlugin.md`, and
  `docs/superpowers/plans/README.md` — all documentation. Every pre-existing entry is still present
  and unchanged by this order.
- No file was removed or renamed. `git status --porcelain` contains no `D` entries before or after.
- **The handoffs and their evidence are untouched.** File modification times for `handoff-01.md` …
  `handoff-05b-device-env.md` and `evidence-01` … `evidence-05b` are unchanged from their original
  orders (latest 2026-09-13 22:58); the only files in `docs/stabilization/` written by this order are
  `STATUS.md`, the new `handoff-06.md`, and `evidence-06/`. No handoff text or evidence file was
  edited, renamed, or removed.
- Nothing under `Assets/`, `Packages/`, `ProjectSettings/`, `Builds/`, scenes, or prefabs was touched.
- No credential value was printed, logged, or copied anywhere. The audit harness reads the
  project-root `.env` values into memory only to count how many times each one appears in the
  documentation set, and prints just the key name and the count; the headset's own copy was not
  touched.
- Evidence contains no credential values.

## Next action

Order 06 is complete; order 07 (incremental command-handler extraction) remains optional and
unstarted.
Two developer decisions are still open from order 05 and are unaffected by this documentation work:
which of the proposed performance targets to agree, and whether F1/F2 (loose-splat filename and
saved-world name matching by voice) get their own order — they are the only device findings that block
a workflow step outright. Nothing has been committed.
