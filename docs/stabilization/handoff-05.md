# Work order 05 — Quest acceptance and performance measurement

Date: 2026-09-13 (America/Los_Angeles), in the current local checkout. Scope: order 05 only.
No commit, push, stash, reset, or credential-policy change. No paid world generation was invoked;
only transcription and free audio-search calls ran.

## Outcome

A **current** build (orders 02–04 included, declared `-credentialMode private`) was installed in
place on the Quest 3 and driven through the cached-content workflow on hardware:

> launch → wake word → load cached World Labs world → manipulate → add audio → save + save spawn
> point → restart → restore → save again

What the headset itself demonstrated:

- **Journaled saves run on Android.** The isolated fixture folders ended with
  `world.json`, `world.json.bak` and `.world.json.lock`, all written by the app
  (`u0_a207`), and the backup is a genuinely different generation (different `modified_at`).
- **Interrupted-save recovery works on the headset.** With the fixture's `world.json` replaced by
  54 bytes of truncated JSON and a valid `world.json.bak` left in place, the next app start logged
  `Primary save '.../world.json' is missing, unreadable, or corrupt; recovered the previous
  generation from '.../world.json.bak'` twice (two startup scans) and retained both files.
- **Saved state survives a restart.** After `am force-stop` + relaunch, loading the same fixture
  logged `Prepared saved spawn point 1 ... after world transform` before `Restore complete`, i.e. a
  spawn point saved in the earlier process was re-applied by the new one.
- **Objects and audio persist.** The saved config contains one object whose components are
  `AudioSource`, `AudioPlaybackController`, `AudioAttributionMetadata`, `Transform`, and the
  corresponding clips are on the device (`snowcity_ambience...mp3`, `single_bird_chirp...mp3`,
  `Altenthann_Isolated_Bavarian_Church...mp3`). During the session the log shows the audio pipeline
  searching freesound and xeno-canto, downloading a clip and playing it.

Measurements were captured for load stalls, memory, frame timing and save cost, each with its
method (see [measurements](evidence-05/measurements.txt)). **Proposed targets are unagreed** and
listed below.

Two order-03 hardware questions were on the list:

| Question | Result |
|---|---|
| A save interrupted by an app kill leaves a recoverable `world.json.bak` | **Recovery side verified on hardware** (corrupt primary + valid backup → recovered, files retained). **Write-side kill verified only on the host**: 6/6 `SIGKILL` trials against the real journaled writer left a loadable save and no abandoned temporary, 0 unrecoverable. See [verify.txt](evidence-05/verify.txt) |
| The read-only legacy-folder migration still repairs itself | **Not verified on hardware** — it needs a save while the folder is read-only, and the headset fell asleep before that command could be spoken. Still verified only on the host (`SaveConfig_WhenFolderIsNotWritable_MigratesToWritableCopyAndKeepsOriginalFiles`) |

## Build, device and file context

| Item | Value |
|---|---|
| Unity | 6000.2.10f1 (`d3d30d158480`); on-device log confirms `6000.2/staging`, Release, il2cpp, arm64-v8a, stripping enabled |
| Build command | `Unity -batchmode -quit -projectPath <repo> -executeMethod Holodeck.Editor.CommandLineAndroidBuild.BuildApk -outputPath Builds/stabilization-05-20260913/Holodeck-order05-private.apk -credentialMode private -logFile /private/tmp/holodeck-quest-05/build-order05.log` |
| Build result | Succeeded, exit 0, **34 m 28 s** (longer than the 16 m 35 s baseline build), report size 2,254,611,447 bytes |
| Artifact | `Builds/stabilization-05-20260913/Holodeck-order05-private.apk`, 313,579,135 bytes, sha256 `e2ddc68d…c8df1d` — **private, contains live keys** |
| Credential decision | Logged: `Build credential intent: Private build (embeds .env; do not share the artifact) (from -credentialMode)`, `Embedded 15 .env entries`, and the "PRIVATE build … Do not share" warning. `Assets/Resources/WorldLabs/` is absent after the build |
| Headset | Quest 3 (`eureka`), serial `2G0YC1ZF8H0SL0`, Android 14 (API 34), build `UP1A.231005.007.A1`, firmware branch `releases-oculus-14.0-v207`, security patch 2026-06-03 |
| Installed package | `com.QLC.HeadsetHolodeck` 9.2.1 (versionCode 1), updated in place at 17:19:37; the 64 existing world folders survived the update |
| Content loaded | Fixture A "Zebra World Labs Test" (`worldlabs`, cached 6.3 MB `ChristmasattheNorthP_…spz`); fixture B "Yoyo Loose Splat Test" (`local_splat`, cached 11 MB `32_Mermaid_11M.spz`); loose copies `stab05_loose.spz` / `yoyo.spz` under `files/WorldContent/` |
| Pre-existing device content | 64 world folders, `Worlds` 739 MB, `CachedWorlds` 499 MB, `CachedObjects` 229 MB — read but not written (except the shared content caches) |

Full detail and raw `adb` output: [device-context.txt](evidence-05/device-context.txt),
[build excerpts](evidence-05/build-order05-excerpt.txt).

## Scenarios: observed vs untested

| Scenario | Status | Evidence |
|---|---|---|
| Install/update in place, launch, OpenXR/Vulkan bring-up | Observed | app version + Unity startup lines; 64 world folders preserved |
| Wake word + voice command pipeline hands-free | Observed | `ListeningForWake: Listening for "computer"`, 30+ recognised utterances |
| Load cached World Labs world | Observed | `Restoring 'Zebra World Labs Test'` → `Restore complete`, 837 ms |
| Load cached loose-splat world | Observed | `Restoring 'Yoyo Loose Splat Test'` ×4, 903–1001 ms |
| Manipulate (grab/release world, move world) | Observed | `intent=GrabWorld`, TTS "World grabbed.", `Saved world transform for 'Zebra World Labs Test'` |
| Attach audio + play | Observed | freesound/xeno-canto searches, clip download, `Playing 'snowcity ambience …'`, cached `.mp3` on device |
| Save (explicit) and save spawn point | Observed | `Saved manual spawn point`, `Saved current config`, `world.json.bak` written |
| Restart → restore (objects/audio/spawn/floor) | Observed (spawn + config); audio object present in save but not heard after restart | `Prepared saved spawn point 1 … after world transform` in the new process (pid 15733) |
| Backup recovery from a corrupt primary | Observed on hardware | startup recovery warning ×2, both files retained |
| Kill an app mid-save (write side) | **Untested on hardware**; host `SIGKILL` probe instead | 6 trials, all recoverable, 0 unrecoverable |
| Read-only legacy-folder migration | **Untested on hardware** | host fixture only |
| Network failure (provider unreachable) | **Untested** — Wi-Fi was disabled and the app restarted, but the headset slept before any command could be spoken, so no offline command, load or error path was observed | device-logcat2.txt |
| Rapid A→B loads, cancel mid-load | **Partial** — two loads ~18 s apart and a `CancelGeneration` with nothing pending; a true overlapping-load or mid-download cancel was not reproduced | `No pending clarification … 'Cancel generation.'` |
| Tracking loss (camera covered) | **Untested** | — |
| Suspend/resume (home button; headset off/on) | **Partial** — the app was observed pausing/resuming (`OnApplicationPause(True)`, `APP_CMD_PAUSE/STOP`, `Handle cmd APP_CMD_START`) whenever the headset slept or woke, and a full force-stop restart was exercised; deliberate home-button and power-key sequences were not | device-logcat.txt |
| Worn-headset frame timing and eye-level visual fidelity | **Untested** — the headset was on a desk; `adb exec-out screencap` returns a black VR frame ([device-pre-flow.png](evidence-05/device-pre-flow.png)), and "capture panorama" could not produce a fidelity artifact (see F4) | — |
| Paid world/object generation | Excluded by instruction | — |

## Reproducible failures and wake-word observations

**F1 — A loose splat file cannot be loaded by voice by bare filename.**
`LocalTypedIntentParser.TryParseContentPath` only accepts a content path that *starts with*
`http://`, `https://`, `file://` or `/`, so `load yoyo.spz` never reaches `LoadSplat`; it falls into
the saved-world rule and dies as a name lookup. The documented `WorldContent` fallback directory is
therefore unreachable by voice.
Repro: place `WorldContent/yoyo.spz`, say "computer, load yoyo.spz".
Observed: `[WorldActionDispatcher] LoadWorldConfig: no saved config matching 'yoyo.spz'` and no load.
Code: `Assets/App/Command/SpeechIntent/Runtime/LocalTypedIntentParser.cs` (`TryParseContentPath`) vs
the earlier `load <name>` branch (`LoadWorldConfig`).

**F2 — One ASR word error defeats a saved-world load.**
`FindBestDisplayNameMatch` requires an exact, prefix or substring match on the normalised display
name, so "Zebra World Lab Test." (the transcript of "Zebra World Labs Test") matched nothing.
Repro: say a saved world's name with one word mis-heard by ASR.
Observed: `[WorldActionDispatcher] LoadWorldConfig: no saved config matching 'Zebra World Lab Test.'`
— nothing loaded, no fallback and no "did you mean" clarification.

**F3 (observation, not a defect) — Wake-word misses; the alias list is deliberate.**
`Assets/App/Command/SpeechIntent/VoiceActivationConfig.asset` ships an intentional alias list built
from real-world testing — `computer`, `pewter`, `pudor`, `peter`, `impewter`, `pumpor` — because the
VAD/ASR wake trigger often hears only the last syllables of "computer". Those accepts are working as
designed, so they are **not** a defect: in this session `pewter` (13×), `impewter` (6×), `pudor` (2×)
and `computer` (1×) opened command listening as intended.
The residual, smaller issue is the opposite direction — near-misses that were **rejected** and so did
nothing: `FUDOR` (6×), `LETTER` (4×), `FUTURE` (3×), `PUDER` (2×), `FUDER` (2×), `TUDOR`, `THEOR`,
`MUTOR`, `LITER`. Each is a word the speaker intended as "computer". Widening the alias list, or
moving to a more robust STT front end, is the fix; both are deferred by agreement.
Evidence: [voice-commands-observed.txt](evidence-05/voice-commands-observed.txt).

**F4 — "Capture panorama" is routed to the panorama *view* and no fidelity artifact is produced.**
The transcript `Panorama.` parsed as `ShowPanoWorld`, producing
`[ViewModeController] RequestPanoView — no panorama available` and the spoken reply "Panorama is not
available for this world." A locally cached splat world has no `cached_pano`, so no rendered-frame
artifact could be captured for visual-fidelity evidence.

## Measurements (proposed targets below are **unagreed**)

Method is recorded with every number; see [measurements.txt](evidence-05/measurements.txt). Tools
used: `adb logcat -v time` (app `Unity` lines and the app process's own `VrApi` 1 Hz summaries),
`adb shell dumpsys meminfo`, and the app-kill probe's own timing lines. The OVR Metrics *service*
(`com.oculus.ovrmonitormetricsservice`) is present and the app subscribes to
`XR_META_performance_metrics`, but the standalone OVR Metrics Tool is not installed, and Unity
Profiler over adb was not used because it requires an interactive editor session.

| Measurement | Value | Tool / sample count / duration |
|---|---|---|
| Cached World Labs world load stall | 837 ms (n=1) | app log `Restoring`→`Restore complete`, 6.3 MB spz |
| Cached loose-splat load stall | median 950 ms, range 903–1001 ms (n=4) | same, 11 MB spz |
| Frame rate, headset-off | median 48 fps, p95 72, max 73; 88 % of samples < 60 fps | app `VrApi` 1 Hz lines, n=472, ~8 min |
| App frame submit time | median 17.6 ms, p95 85.4 ms, max 865.5 ms | same samples |
| CPU&GPU frame time | median 29.4 ms, p95 92 ms, max 100 ms | same samples |
| Predicted display period | median 57 ms (compositor throttled headset-off) | same samples |
| Cold start PSS | 277,278 KB after install; 2,226,537 KB after force-stop restart | `dumpsys meminfo`, single samples |
| Idle PSS (listening) | ~2,353,454 KB, stable to <1 MB | `dumpsys meminfo`, 14 samples every 25 s |
| PSS after loading fixtures A / B | ~2,660,000 KB / ~2,640,000 KB; peak sampled 2,669,102 KB | `dumpsys meminfo`, sampled |
| Save cost, 20,000-object (5.7 MB) document | median 2,921 ms, min 1,909, max 8,143 ms (n=21) | host, journaled writer's own timings |

### Proposed performance targets (mark **unagreed** until you confirm)

| Target | Proposed value | Why |
|---|---|---|
| Cached splat load stall (≤12 MB spz, worn headset) | ≤ 1.5 s to first rendered frame, no single frame > 100 ms after the first rendered frame | current 0.84–1.0 s headset-off; the synchronous SPZ decode + floor analysis on the Unity thread is the dominant cost |
| Sustained frame rate in a loaded world, worn | ≥ 68 fps average, ≤ 5 % of frames > 16.7 ms | current headset-off medians (17.6 ms app submit, 29.4 ms CPU&GPU) cannot be judged without a worn run |
| Peak memory, worst of the fixtures | ≤ 2.8 GB PSS, and no more than +150 MB growth across 5 consecutive world switches | idle 2.35 GB, loaded 2.66 GB sampled; switching slope not isolated |
| Save latency for a heavily decorated world | ≤ 500 ms at 2,000 objects, ≤ 2 s at 20,000 objects, and never on the frame that consumes a voice command | host measures 1.9–8.1 s at 20,000 objects because each save writes two durable copies |
| Recovery | 100 % of interrupted saves load the previous generation; zero unrecoverable states | already met in the 6 host kill trials and the on-device recovery test |

Rendering fidelity and speed are deliberately kept apart: no fidelity claim is made here. The only
fidelity evidence attempted (a captured panorama) failed for the reason in F4, and a headset-off
`screencap` returns black.

## Preservation

- HEAD is still `05a09dd46710d82ac4bf1c91c1488d249872ae3d`; `git status --porcelain` reported **110**
  entries before the build and **110** after; nothing is staged; no stash, reset or replacement
  worktree was used.
- A checksum comparison of all `Assets/`, `Packages/` and `ProjectSettings/` files against the
  hash-verified isolated copy taken before the build shows **no content differences** afterwards —
  only nine files whose modification time the build touched
  ([before](evidence-05/pre-build-source-vs-isolated-copy.txt),
  [after](evidence-05/post-build-source-vs-isolated-copy.txt)).
- `Assets/Resources/WorldLabs/` (the embedded `.env` copy) is absent after the build; the artifact
  was not published, copied or shared.
- Baseline test fixes, order 02 transition work, order 03 save protection and order 04 credential
  boundary are untouched: no `Assets/`, `Packages/` or `ProjectSettings/` file changed content.
- Device: the only writes were the two isolated fixture folders, the two loose `.spz` copies under
  `files/WorldContent/`, and the app's own content caches. The 64 pre-existing world folders were
  not modified. All fixtures are named `STAB05_*` / `yoyo.spz` / `stab05_loose.spz` and can be
  deleted with no effect on real saves.
- Evidence contains no credential values; the device and build logs were scanned for
  credential-shaped strings before curation and world-name listings were redacted.

## Remaining platform limitations

- **Frame timing is not a worn-headset result.** Headset-off, the Quest compositor throttles
  (`Prd` 57 ms), so the fps figures bound the app's own cost but do not describe normal use. A worn
  run (and ideally the OVR Metrics Tool or in-app `XR_META_performance_metrics` logging) is still
  needed before agreeing targets.
- **No true peak memory.** `/proc/<pid>/status VmHWM` is unreadable for a non-debuggable package, so
  the peak is a 20–25 s sampled maximum.
- **Write-side interruption is host-verified, not headset-verified.** The 6 `SIGKILL` trials landed
  between saves rather than inside the ~ms temp-write/rename window (the "inside a save" flag is a
  logging race), so the exact torn-write window on Quest storage remains inferred.
- **The read-only legacy-folder migration is still host-only.**
- **Atomic replacement on Windows/iOS/visionOS** (`File.Replace`/`File.Move` branch) remains
  uncompiled and unexercised.
- **Offline behaviour is unmeasured.** Cached content is local, but every voice command traverses
  OpenAI transcription, so the practical offline failure mode is unknown.
- **Audio after restart** is evidenced by the persisted audio object plus a completed restore, not
  by an observed playback in the new process.
- **No paid generation acceptance** (Draft/Fast/Standard/High) was performed in this order.
- The build took 34.5 min on this machine and left the host at ~14 GB free disk; plan for that
  before a release-shaped build.

## Device checklist (for the remaining items)

Everything below still needs a worn or awake headset; each is independent.

1. **Network failure.** Disable Wi-Fi (`adb shell svc wifi disable`), then speak a command. Expect a
   visible/spoken error rather than a hang, and confirm a cached world still loads when the command
   text arrives by another path. Re-enable Wi-Fi afterwards.
2. **Kill mid-save on hardware.** With a decorated world (>2,000 objects) loaded, issue "save" and
   `adb shell am force-stop com.QLC.HeadsetHolodeck` inside the save window; then restart and confirm
   the world still loads (primary or `world.json.bak`).
3. **Read-only legacy migration.** `adb shell chmod 555` a fixture folder that has no writable copy,
   then save; expect `Migrated read-only config …` and a new writable folder while the original
   files stay in place.
4. **Rapid loads and cancel.** Trigger A→B within a second, then cancel an in-flight load
   (`Cancel generation`) and confirm the finished world is the one that stays.
5. **Tracking loss.** Cover the front cameras for ~5 s, uncover, and confirm the world, spawn and
   audio survive without a reset.
6. **Suspend/resume.** Press the system menu button and the power button; confirm the app resumes
   into the same world without reloading or losing saved state.
7. **Worn-headset performance.** Repeat one cached load per fixture with the headset worn and record
   frame timing and memory with the OVR Metrics Tool or in-app metrics; add a `capture panorama`
   (say "capture the panorama", not "panorama") on a world that has one for a fidelity artifact.

## Next action

Order 06 (documentation refresh) is next in the plan. Before that, two decisions are worth making:
which of the proposed targets to agree, and whether F1/F2 (loose-splat filename and saved-world name
matching by voice) belong in a follow-up order — they are the only device findings that block a
workflow step outright. Nothing has been committed.
