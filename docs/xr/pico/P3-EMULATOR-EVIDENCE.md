# P3 — emulator observations (not acceptance)

Date: 2026-10-27. Author: DeepSeek session. Evidence for the hardware session's starting point.
**Emulator results are intermediate only** — microphone input, tracking, passthrough and room
alignment cannot be accepted here, and world-space UI cannot be driven by Android touch injection.

Artifact: `Builds/Pico-Swan-parity.apk` (309 MB, release, IL2CPP, Vulkan, MultiPass, Burst AOT,
`com.QLC.HeadsetHolodeckPico`, `-credentialMode device`).

## Confirmed

| Observation | Evidence |
|---|---|
| Boots to the app, not a splash | `[BootProbe] AfterSceneLoad scene=Holodeck roots=13` |
| OpenXR session begins | `[PICOOpenXRExtensions] OnSessionBegin` |
| XR device active before scene load | `[BootProbe] BeforeSceneLoad graphics=Vulkan xrActive=True` |
| Renders the LCARS panel | screenshot `Logs/pico-auth-01.png` |
| Reads its own device `.env` | `[RuntimeDotEnv] Loaded 15 variable(s) from …/com.QLC.HeadsetHolodeckPico/files/.env`; `[EnvLoader] Loaded 15 variables` from the same path |
| Credentials resolve into the client | `WorldLabsClient:.ctor` → `EnvLoader:Get`, no "No API key found" |
| **Authenticated WorldLabs call succeeds** | `ListWorldsAsync` runs and the panel renders an account world ("Serene Japanese garden") with Load / Save As / Delete |
| Microphone permission granted | `PrivacyDataManager, activeTypes change: [] -> [MICROPHONE]`; `MicrophoneSource` diagnostics run |

That last row is the important one for the deadline: the provider credential path works end to end on
Pico, so Friday's risk is platform features, not keys.

## Voice-driven world load — confirmed

The owner spoke a wake word plus a load command; the app did the rest. Log evidence from the run:

```
[VadAsrWakeTrigger] VAD segment drained. duration=2.82s, samples=45152
[SherpaOnnx] ASR recognized: " … JAPANESE GARDEN"
[GaussianSplatRenderSystem] Gathered runtime splat 'World_serene Japanese garden'
    for camera 'Main Camera'; count=500000
[ViewModeController] OnWorldLoaded worldId=613bf0bd-6461-46e1-8412-f4c32eb8e701,
    renderer=World_serene Japanese garden
[PlayerOriginController] Resetting player to anchor 'Teleport Anchor'.
[WorldConfigRestorer] Restore complete for 'serene Japanese garden'
[WorldConfigAutoSave] Cached SPZ for 'serene Japanese garden' →
    sereneJapanesegarden_613bf0bd-….spz (7,835,xxx bytes)
[AudioWorldActionController] Searching freesound for 'serene Japanese garden'…
```

Screenshot: `Logs/pico-garden-loaded.png` — the garden with its bridge and stream, rendered in stereo.

So on Pico, in one flow: **wake word → on-device ASR → command routing → WorldLabs
download → 500k-point splat render → spawn restored → SPZ cached → audio search**. That is the
core of the "load cached content" workflow, and it exercises the device `.env` credential path,
the credential boundary, the splat renderer, the transition ownership and the save-restore layer at
once.

The same session also produced a failed utterance — `Wake word not matched.
transcript='THE WORLD SERIOUS JAPANESE GARDEN'` — which is the known ASR fragility recorded on
Quest (FUDOR/LETTER/PUDER), not a Pico regression. Wake detection is probabilistic, so tests should
retry an utterance once before reporting a failure.

## Push-to-talk works on the emulator

World-space UI ignores Android touch, but the Input System path is live:

```
SpeechIntent.PushToTalkTrigger:OnActionCanceled
SpeechIntent.VoiceCommandRouter:EmitError("No audio was recorded.")
```

`adb shell input keyevent 62` reaches the app, so push-to-talk can drive commands on the emulator
without the wake word. The error above is only because nothing was spoken in that window.

## Save → restart → restore — confirmed

The owner spoke a save command (TTS replied "saving this world"), then the app was force-stopped and
relaunched, then a spoken load restored the world **from local storage**:

```
21:32:43.264 [WorldActionDispatcher] Loading saved world 'serene Japanese garden'
             from local storage before any World Labs fallback.
21:32:45.101 [GaussianSplatRenderSystem] Gathered runtime splat 'serene Japanese garden'
             for camera 'Main Camera'; count=500000
21:32:45.526 [WorldConfigRestorer] Restore complete for 'serene Japanese garden'
```

- **1.84 s** from the routing decision to the splat being gathered; **2.26 s** to restore complete.
- No new `Cached SPZ` write appeared, confirming the cached asset was reused rather than re-downloaded.
- The journaled save on device: `world.json` (778 B) + `world.json.bak` + `.world.json.lock`, with
  `schema_version, config_id, display_name, world_source, world_transform, prompts, spawn_points,
  objects, lighting`.

Note the design: there is **no cold-start auto-restore**, and that is deliberate. After a relaunch the
app shows an empty room until the user says `load <name>` (or presses Load in My Worlds). The owner's
reason, recorded so nobody "improves" it later: an automatic load at startup can enter a
never-ending failure loop — a damaged or unsatisfiable config would be retried on every boot with no
way for the user to intervene. World loading is always user-initiated; a failed name match must never
silently start a generation either. An acceptance checklist that expects a world to reappear on launch
is testing behaviour the app intentionally does not have.

Screenshot: `Logs/pico-garden-restored.png`.

## Confirmed → generated → loaded — the loop is closed

The full path was exercised on 2026-10-28:

1. `load purple elephant` missed, so the app asked *"No saved world called purple elephant. Shall I create it?"*
2. `yes` completed the confirmation and the app announced it was creating the world.
3. WorldLabs accepted the generation and the client polled it. The wait was then **cancelled locally**.
4. On the next launch, `WorldConfigStore.ReconcileWithWorlds` imported the world from the account (it
   creates a config for any WorldLabs world that has none), so **the generation had in fact succeeded
   server-side** and cancelling the wait lost nothing.
5. `load purple elephant` then matched that imported config, downloaded the world and rendered it:
   `[WorldConfigRestorer] Restore complete`, `Cached SPZ (7,752,536 bytes)`,
   `Cached pano (11,919,767 bytes)`.

Screenshot: `Logs/pico-elephant-loaded.png`.

What this settles, and what it exposes:

- Confirm → generate → load works end to end. The earlier `Poll error: code=, message=''` warnings were
  the logging bug fixed in `db340fc`, not a failed generation.
- Cancelling the wait does not discard the world, because reconcile re-imports it later. Good for not
  losing paid work, but it means a cancelled generation still costs whatever the API charged — worth
  confirming against the account.
- **A world can be listed before it is downloaded.** The imported config carries a `world_id` and no
  cached splat, so the first load pays the download (7.8 MB splat + 11.9 MB panorama here). The list
  does not distinguish "ready offline" from "needs download", which is why newest-first sorting put an
  un-downloaded world at the top.

## Static room saved and restored

The built-in room is now a saveable world (`world_source.type = "static"`), which closed the gap where
only a loaded splat could be saved. Verified on the emulator on 2026-10-28:

- A sound added in the empty room was recorded against an in-memory draft and written on save:
  `objects = 1`, `Audio_Rain_3_wav`, and a new folder `Holodeck_2026-10-28_14_50_…` appeared at the top
  of My Worlds.
- `load` of that world logged `Restoring '…'` → `Restore complete` in **27 ms**, with no splat load,
  which is the static branch working.
- The rain was audible again, and the clip was served from `Worlds/CachedWorlds/` rather than
  re-downloaded.

Two bugs were found and fixed on the way:

- The static restore **cancelled itself**: `RestoreDefaultWorld()` unloads worlds and invalidates the
  current transition, which was the restore's own operation. Every later `ThrowIfStale` threw, and the
  cancel handler logged nothing. Unloading now happens in the dispatcher before the operation exists,
  and a cancelled restore logs a warning.
- The draft config only existed from a default-world load or at save time, so anything added right after
  launch was recorded against nothing. It is now created in `Start`.

Caveat on the audio evidence: the owner heard rain **before** the "Looking for saved world" line, so the
sound may have been carried over from an earlier partially restored scene. A clean confirmation is
force-stop → relaunch → `load` → listen only after `Restoring '…'` appears.

## Driving the app without a microphone

Voice could not be used to test restore while the emulator's microphone delivered silence, so a test
channel now exists:

```
adb shell "echo load rain room > /sdcard/Android/data/com.QLC.HeadsetHolodeckPico/files/debug_command.txt"
```

The app polls every 500 ms and dispatches through `VoiceCommandRouter.SubmitTypedCommand`, the same path
the LCARS keyboard uses, so the real parser/router/dispatcher/restore are exercised.

A runtime `BroadcastReceiver` was attempted first and **does not work here**: targetSdk is 36 so the
two-argument `registerReceiver` throws, and the flagged overload fails `AndroidJavaObject`'s JNI
signature check because a proxy is not declared as `BroadcastReceiver`. The file channel needs no
Android API at all. Both are test affordances reachable by anything with adb —
`DebugCommandIntentBridge.enableInReleaseBuilds` must be turned off before shipping.

## Microphone silence on the emulator

From 15:00 onwards every utterance transcribed as `"you"` — the recorder delivered silence, not a
misheard phrase. The recorder's own diagnostic is the useful part:

```
[SherpaOnnx] MicrophoneSource: all native sources silent.
Diag: GlobalMicToggle=ERROR(null); MicMute=false; AppOps=ALLOWED; Permission=GRANTED
```

Permission is granted, AppOps allows it and the stream opens (`AAUDIO_OK`), so the app is not the
blocker; the host/emulator audio input is. `GlobalMicToggle=ERROR(null)` is the diagnostic's reflection
into `SensorPrivacyManager` failing, so it says nothing either way. A reboot restored capture once and it
was lost again later in the same session, which points at the host capture device rather than the app.

## Blocked on the emulator (deferred to hardware)

- **Interaction.** The LCARS panel is world-space UI driven by XR rays; `adb shell input tap` delivers
  Android touch and produces no response. No controllers are paired and hands cannot be tracked, so
  panel interaction, world Load, object manipulation and voice-triggered UI are all untestable here.
- **Persistence.** Save / force-stop / restore all need an interaction to change state first. The save
  layer itself is unchanged from the verified Quest baseline.
- **Voice.** Wake/VAD/ASR need real microphone input; TTS output needs a headset to judge.
- **Performance.** Everything runs ARM64 under Houdini translation on an x86_64 emulator; any timing
  measured here is not representative.

## Object generation, cache reuse, placement and view switching (28–29 Oct)

**Generation end to end.** A text-to-3D request against 3dAIStudio produced, downloaded, imported and
placed a model:

```
19:04:03  Task 0c030ea1-… status=IN_PROGRESS
19:05:52  [ObjectGenerationService] Importing generated object: dragon statue.
19:05:59  material repaired → 'Generated Object Texture Repair' (Universal)
19:06:00  [ObjectGenerationService] Generated object is ready: dragon statue.
```

Screenshot `Logs/pico-dragon.png`. Cost: 40 credits for a standard text-to-3D, which matched the
vendor's published example exactly (20 base + 20 standard texture).

**Cache reuse.** The app already offers a choice when a prompt matches a cached model — "I found a saved
dragon statue. Use it, or create a new one?" — but "use it" used to throw:

```
InvalidOperationException: [WorldConfigStore] Config '' is not tracked.
  at WorldConfigAutoSave.OnObjectMutated
  at WorldActionDispatcher.ImportSavedCachedObject
```

Placing an object while only the in-memory draft existed tried to save a config with no id. Materialisation
now lives in `WorldConfigAutoSave.EnsureMaterialisedActiveConfig` and every active-config save routes
through it, so a save, a capture or a mutation can all turn the draft into a real world. Re-tested: the
cached model imported, no exception, `objects=1` written, **no credits spent**. Screenshot
`Logs/pico-dragon-cached.png`. An explicit "create new" passes `allowCacheReuse: false` so it cannot be
satisfied by the cache.

**Placement.** A generated object with no voiced or gestured location used to land at world origin,
sometimes at the user's feet. `defaultDistanceMeters` was 2 rather than 1, and the viewer reference
depended on `Camera.main`, which an XR rig usually leaves untagged — the lookup fell through to null and
placement collapsed to `Vector3.forward * distance` from world origin. Placement now prefers the live head
pose from the spatial snapshot, falls back through a camera chain, and turns the object to face the
viewer. Verified: position ≈ (0.32, 0.99, 1.36), i.e. in front at head height.

**Settling.** Gravity then dropped the object from head height, so it landed wherever a bounce left it.
A downward raycast from above the object now finds the highest non-self surface and moves the object onto
it before handoff:

```
[ObjectGenerationService] Settled 'GeneratedObject_dragon_statue' onto the surface at y=-0.01 (moved -0.665 m).
```

Saved transform `y 0.986 → 0.321`. Screenshot `Logs/pico-dragon-settled.png`.

**Interactable wrapping** needed no change and was confirmed from the scene and the diagnostics: trackable
component, root box collider, rigidbody, XR grab interactable, gravity and a 1 kg mass
(`colliders=1 rootRigidbody=True rootRbGravity=True`).

**View switching, 3D ↔ panorama.** Both directions work:

```
[ViewModeController] TryApply — DesiredMode=Pano, _isPanoReady=True
[ThumbnailSkyboxController] Panorama sphere shown. Texture=4608x2304
[ViewModeController] TryApply: Splat3D path — showing world root, fading out pano.
```

Screenshots `Logs/pico-view-pano.png` and `Logs/pico-view-splat.png`. The splat view landed with the
camera inside vegetation — a player-origin/spawn issue, not a switching one.

**Panorama availability.** Restores preloaded a pano; generation and gallery loads did not, so switching
failed the first time for a world that had never been viewed in panorama. `WorldConfigAutoSave` now
preloads `assets.imagery.pano_url` in the background after any load, skipping restores (which already
preload) and using the config's real `world_id` because a cached-splat restore reports a synthetic
`local_<name>_<worldId>` renderer id. Verified on the restore path that a stripped `cached_pano` is
re-downloaded and re-cached (11,919,767 bytes); the generation path is implemented but unverified.

**View-switch phrases** (`show panorama`, `show 3d`, `show mesh`) were added to the local grammar and
allowed to short-circuit the intent model. The typed-command path parses locally, which is how they were
verified; the voice path cannot be exercised while the emulator's microphone returns silence.

**First-load stall — observed once, not reproducible.** At 01:00 the app was killed eight seconds into
fetching a world that had never been loaded, and the guest rebooted shortly afterwards:

```
01:00:32  ActivityTaskManager: Force removing ActivityRecord{… com.QLC.HeadsetHolodeckPico …}
01:00:31  flags_health_check: ServerConfigurableFlagsReset updatable crashing detected
```

There was no crash log and no OOM (1.9 GB free). Two attempts the next morning, on two different
never-loaded worlds, both succeeded — the beach world restored in 6 s and the park in 12 s, each caching
both its splat and its panorama:

```
09:25:44  Restore complete for 'A sunny day at the beach.'
09:27:16  Cached pano for 'A large park in autumn, with paths, park benches, trees, bus' …
```

The emulator was demonstrably unhealthy around the original failure: it then crash-looped
(`crashing detected` every 5 s) and rebooted its guest, and `monkey` could not find the activity until the
APK was reinstalled. **Conclusion: emulator instability, not a deterministic app defect** — but a first
load of a new world should still be watched on hardware, because a slow fetch that tripped the OS
watchdog once could do so again under different load.

**Panorama availability verified on the gallery path.** The two never-loaded worlds above are the case
the preload was added for, and both cached a panorama automatically (2,482,795 bytes for the beach), so
the view switch is available immediately after a first load.

## Emulator stability

The host crashed twice at 6 GB guest RAM (`bms`… dumps 19:06 and 20:06, ~101–103 MB), both while the
app was running and with the guest reporting ~500 MB free:

```
Mem: 6,065,916K total, 5,563,876K used, 502,040K free
```

The AVD was raised to `hw.ramSize=8192`, `vm.heapSize=2048`, and the emulator is now started with
`pico-cli emulator start --watch --log-file Logs/pico-emulator.log`, which restarts it automatically
and records the host-side reason. No crash has occurred since the increase.

## Reproduce

```powershell
$env:PICO_HOME = "C:\Users\daren\AppData\Local\Packages\OpenAI.Codex_2p2nqsd0c76g0\LocalCache\Local\PICO\sdk"
pico-cli emulator start --avd Holodeck_Pico_Swan --watch --log-file Logs/pico-emulator.log
```

`am start` is blocked by the launcher (`Activity not started because the current activity is being
kept for the user`). Launch through the launcher path instead:

```powershell
adb -s emulator-5554 shell monkey -p com.QLC.HeadsetHolodeckPico -c android.intent.category.LAUNCHER 1
```

If the "Controllers Required" dialog appears, its *Continue Anyway* button is at roughly (360, 540) in
the emulator's 720x764 input space.
