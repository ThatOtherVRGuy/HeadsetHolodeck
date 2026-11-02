# Headset Holodeck

Shared code is organized under `Assets/App/Shared`, with headset-specific work under
`Assets/App/Platforms/Meta` and `Assets/App/Platforms/Pico`. See the
[Meta/Pico architecture and delivery plan](docs/xr/PLAN.md).

**Current target: PICO Swan (PICO OS 6).** The Meta Quest path is maintained in the same tree and selected
through the build-profile target switch, but the Pico build is the one being verified. For the state of each
capability, what is verified versus not, how to build and run, and the known issues, start with
[docs/xr/pico/SHARE-SNAPSHOT.md](docs/xr/pico/SHARE-SNAPSHOT.md).

Headset Holodeck is a Unity XR project for creating and revisiting immersive worlds in a headset using voice, hands, gaze, image prompts, cached worlds, and spatial audio. The current public build focuses on World Labs world creation, OpenAI-backed command interpretation, Sherpa-ONNX local voice activation/TTS, local or remote splat loading, camera/image prompts, and interactable primitive object creation.

## Quick Start

1. Install Unity `6000.2.10f1` through Unity Hub.
2. Clone this repository:

   ```bash
   git clone https://github.com/ThatOtherVRGuy/HeadsetHolodeck.git
   ```

3. Open the cloned `HeadsetHolodeck` folder in Unity Hub.
4. Let Unity import the project. The first import can take several minutes.
5. Copy `.env.example` to `.env` in the project root and add your own keys.
6. Open `Assets/Scenes/Holodeck.unity`.
7. Run `Headset Holodeck > Validate Install` from the Unity menu. It reports which keys are present
   and never prints a value.
8. Press Play in the Editor, or build for the headset with the **device** credential intent
   (`Headset Holodeck > Build Credentials > Device`, then `File > Build Profiles`) and follow
   [Headset Run Flow](#headset-run-flow) below.

That is the whole setup path. A tester who only wants the headset demo needs steps 1–5, one device
declaration, and the `adb` commands in the headset section.

## API Keys

Create a project-root `.env` file next to `Assets/`, `Packages/`, and `ProjectSettings/`.

```env
OPENAI_API_KEY=your_openai_key
WORLDLABS_API_KEY=your_worldlabs_key
PIXABAY_API_KEY=your_pixabay_key
FREESOUND_API_KEY=your_freesound_key
XENO_CANTO_API_KEY=your_xeno_canto_key
```

Required for the main demo path:

- `OPENAI_API_KEY`: command interpretation and API-backed speech intent.
- `WORLDLABS_API_KEY`: World Labs world generation.

Optional but useful:

- `PIXABAY_API_KEY`: image search prompts.
- `FREESOUND_API_KEY`: Freesound audio search.
- `XENO_CANTO_API_KEY`: bird/wildlife audio search.
- `THREEDAISTUDIO_API_KEY` (+ optional `THREEDAISTUDIO_BASE_URL`): 3dAIStudio Tripo object
  generation — the implemented image-to-3D and text-to-3D provider, with a credit preflight.
- `HITEM_ACCESS_KEY` + `HITEM_SECRET_KEY` (+ optional `HITEM_BASE_URL`): HiTem image-to-3D object
  generation, the second implemented provider.
- `FREESOUND_SEARCH_URL`, `XENO_CANTO_SEARCH_URL`, `OPENVERSE_AUDIO_SEARCH_URL`: endpoint
  overrides. These are non-secret configuration and are the only entries a distribution build
  embeds.
- `MESHY_API_KEY`, `TRIPO_API_KEY`: **reserved**. They only satisfy the "an object provider is
  configured" check and the install validator; there is no Meshy or standalone Tripo provider class,
  so setting them alone enables nothing.

Openverse audio search needs no key. Xeno-canto, Freesound, Pixabay, and 3dAIStudio are optional
provider upgrades: the app runs without them, with the corresponding UI disabled or degraded.

Never commit `.env`. The repo ignores it.

### Build Credential Boundary

A build can embed a copy of `.env` so the app can read keys on platforms without a project folder. Because a build you hand to someone else would otherwise carry your own keys, every build must declare what it is for.

| Intent | What is embedded | How to choose |
|---|---|---|
| **Device** | Nothing. The app reads keys from the `.env` that lives on the device itself, at `Application.persistentDataPath/.env` — on Android `/sdcard/Android/data/<package>/files/.env` | `Headset Holodeck > Build Credentials > Device`, `-credentialMode device`, or `HOLODECK_CREDENTIAL_MODE=device` |
| **Private** | Every `.env` entry, exactly as it is on your machine | `Headset Holodeck > Build Credentials > Private`, `-credentialMode private`, or `HOLODECK_CREDENTIAL_MODE=private` |
| **Distribution** | Only non-secret configuration (`*_BASE_URL` and `*_SEARCH_URL` entries). Credentials and unrelated entries are dropped, and their key names are listed in the build log | `Headset Holodeck > Build Credentials > Distribution`, `-credentialMode distribution`, or `HOLODECK_CREDENTIAL_MODE=distribution` |
| **Not declared** | Nothing — the build **stops** and tells you how to choose | The default until you pick one of the above |

Device builds are the intended way to run on a Quest. Put the file on the headset once per device, then rebuild or reinstall freely — the artifact itself never carries a key:

```bash
adb push .env /sdcard/Android/data/com.QLC.HeadsetHolodeck/files/.env
```

`RuntimeDotEnv` loads that file before the first scene, and the World Labs plugin reads the same file through `EnvLoader` (the device `.env` is consulted first, and a copy baked by a private build is only a last resort, so it can never shadow the device's own file).

Rules the boundary enforces:

- A key whose name looks like a credential (`*_API_KEY`, `*_ACCESS_KEY`, `*_SECRET_KEY`, `*_TOKEN`, ...) is never embedded in a distribution build, even if someone adds it to the allowlist.
- Distribution builds drop every entry that is not explicitly allowlisted, so unrelated things you keep in `.env` do not travel with the artifact.
- The embedded copy is written atomically, deleted after the build, deleted if the editor quits, and deleted at editor startup in case a build crashed before cleanup ran.
- Key names may appear in logs; key values never do.

`Headset Holodeck > Build Credentials > Show Build Credential Status` prints the intent in force and where it came from. The saved choice is per machine and per project (stored in editor preferences, not in the repository).

A distribution build ships without provider keys by design; whoever receives it supplies their own `.env` or environment variables, or uses flows that do not need a key.

## Building For The Headset

Menu build: set `Headset Holodeck > Build Credentials > Device`, then `File > Build Profiles` →
Android → Build (or Build & Run).

Unattended build, using the project's only command-line entry point:

```bash
Unity -batchmode -quit \
  -projectPath <repo> \
  -executeMethod Holodeck.Editor.CommandLineAndroidBuild.BuildApk \
  -outputPath Builds/Holodeck-device.apk \
  -credentialMode device \
  -logFile /tmp/holodeck-device-build.log
```

`-outputPath` defaults to `Builds/Holodeck.apk`. `-credentialMode` accepts `device`, `private`, or
`distribution`; an unknown value stops the build before anything is produced, and a build with no
declared intent refuses to embed credentials. Expect roughly 15–35 minutes and several GB of disk
per build on a laptop-class machine.

## Headset Run Flow

1. **Install.** `adb install -r Builds/Holodeck-device.apk`. Installing over an existing copy keeps
   saved worlds.
2. **Give the headset its keys once.** The device build contains none:

   ```bash
   adb push .env /sdcard/Android/data/com.QLC.HeadsetHolodeck/files/.env
   ```

   The app reads that file at startup; the log confirms it with
   `[RuntimeDotEnv] Loaded <n> variable(s) from …` and `[EnvLoader] Loaded <n> variables from …`.
3. **Launch and wake it.** Say "Computer, …" — the accepted wake list is the deliberate alias set
   `computer, pewter, pudor, peter, impewter, pumpor`, because the VAD/ASR front end often hears only
   the tail of the word. Push-to-talk (Space / right controller secondary button) still works.
4. **Load cached content.** "Computer, load `<saved world name>`" restores a saved world and its
   cached splat, world transform, objects, audio, and spawn point.
5. **Save.** "Computer, save this world" (or the Ops panel) writes the config. The next save rotates
   the previous one into `world.json.bak`.
6. **Restart and restore.** Force-stop and relaunch:

   ```bash
   adb shell am force-stop com.QLC.HeadsetHolodeck
   adb shell am start -n com.QLC.HeadsetHolodeck/com.unity3d.player.UnityPlayerGameActivity
   ```

   (Or simply open HeadsetHolodeck from the headset's app launcher.)

   Loading the same world again should restore the saved transform, spawn point, objects, and audio.
   If the primary save was interrupted, the startup scan logs
   `recovered the previous generation from '…/world.json.bak'`.

Saves live at
`/sdcard/Android/data/com.QLC.HeadsetHolodeck/files/Worlds/<config_id>/world.json`, always beside
`world.json.bak` (the previous committed generation) and `.world.json.lock` (the writer lock).
Cached splats/panoramas are in `<Worlds>/CachedWorlds` and cached generated objects in
`<Worlds>/CachedObjects`.

### Reading the logs

```bash
adb logcat -v time -s Unity          # app behaviour: [WorldConfigStore], [WorldConfigRestorer],
                                     # [WorldConfigAutoSave], [RuntimeDotEnv], [EnvLoader], speech
adb logcat -v time -s Unity VrApi    # adds the compositor's own 1 Hz frame-timing summary
adb shell dumpsys meminfo com.QLC.HeadsetHolodeck   # PSS memory samples
```

## Current Verified State

Verified on hardware (Quest 3, 2026-09-13): device-intent install, keys read from the headset's own
`.env`, wake word, cached world load, manipulation, audio, journaled save, on-device backup recovery,
and restart-restore. Load stalls were 0.84–1.0 s for 6.3–11 MB splats, and idle PSS was ~2.35 GB.

Not yet verified, and not to be claimed: worn-headset frame timing (the order-05 numbers were taken
with the headset on a desk), kill-mid-write on hardware, read-only legacy-folder migration on
hardware, offline/network-failure behaviour, tracking loss, and the posted performance targets.
See [docs/stabilization/STATUS.md](docs/stabilization/STATUS.md) and
[docs/stabilization/handoff-06.md](docs/stabilization/handoff-06.md) for the full
verified-versus-planned table, the four device findings, and what is deliberately deferred.

## What Is Included

This repository intentionally vendors two modified packages so testers get the exact code used by the app:

- `Packages/com.worldlabs.gaussian-splatting`
- `Packages/com.ponyudev.sherpa-onnx`

The repo also includes the currently configured Sherpa-ONNX runtime files and smaller model assets:

- int8 Zipformer ASR model files under `Assets/StreamingAssets/SherpaOnnx/asr-models`
- Silero VAD model under `Assets/StreamingAssets/SherpaOnnx/vad-models`
- Kristin Piper/VITS TTS model under `Assets/StreamingAssets/SherpaOnnx/tts-models`
- Android and macOS Sherpa native libraries under `Assets/Plugins/SherpaOnnx`

This means a tester should not need to install separate package forks or pull pending package PRs.

Feature areas present in the current scene: text/image world generation, cached world and loose-splat
loading, panorama view, floor/spawn estimation, saved worlds with a journaled save format, spatial
voice commands through OpenAI plus a local parser, generated primitive objects with materials and
physics, runtime-generated 3D objects from 3dAIStudio Tripo or HiTem with a local object cache,
spatial audio from Freesound/xeno-canto/Openverse, lighting and view-mode control, LCARS UI with a
virtual keyboard, hand-only input via XR Hands, and one-/two-hand world manipulation with snapping.

Voice commands become structured intents before anything executes. The command fields, the full
intent catalog with example utterances, worked examples, and the add-an-intent checklist are in
[docs/intent.md](docs/intent.md).

## Large Files And Optional Downloads

The required smaller/int8 model assets are already included. The unused full-precision ASR ONNX files and test WAVs are not included to keep the public repo lighter.

If you want the full ASR model package for experimentation, download it from the Sherpa-ONNX model release:

- [sherpa-onnx-zipformer-small-en-2023-06-26.tar.bz2](https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/sherpa-onnx-zipformer-small-en-2023-06-26.tar.bz2)

For the current project configuration, those full-precision files are optional.

## Quest Build Notes

Target device is Meta Quest 3.

Recommended settings are already in the project, but verify:

- Platform: Android
- Graphics API: Vulkan
- XR: OpenXR enabled
- OpenXR render mode: Multi-pass
- URP active
- Microphone permission present
- Camera/headset-camera permissions present if testing real-world image capture

The Android manifest is included at `Assets/Plugins/Android/AndroidManifest.xml`.

## Validation Helper

Use `Headset Holodeck > Validate Install` inside Unity. It checks:

- Unity version
- main scene
- vendored package folders
- Sherpa settings, models, and native libraries
- Android manifest permissions
- `.env` presence and which keys are configured
- common generated folders that should not be committed

The validator does not print secret values.

## Common First-Test Flow

1. Open `Assets/Scenes/Holodeck.unity`.
2. Confirm `Headset Holodeck > Validate Install` has no required failures.
3. Press Play.
4. Say a wake phrase such as `Computer`.
5. Try:

   ```text
   Computer, create a castle on a snowy mountain at sunset.
   ```

6. Try image prompt flows from the LCARS UI:
   - camera capture
   - image search
   - create world from prompt plus image

7. Try audio:

   ```text
   Add sounds of the ocean.
   Make it quieter.
   Stop all sounds.
   ```

## Troubleshooting

- If voice does not respond, run the validator and check microphone permission, Sherpa model files, and `OPENAI_API_KEY`.
- If World Labs creation fails, check `WORLDLABS_API_KEY` and internet access.
- If image search buttons are disabled, add `PIXABAY_API_KEY`.
- If audio library results are weak or unavailable, add `FREESOUND_API_KEY` and/or `XENO_CANTO_API_KEY`.
- If Quest builds fail around Android permissions, confirm `Assets/Plugins/Android/HeadsetCameraPermissions.androidlib/build.gradle` has a namespace and compile SDK.
- If splats do not render on Quest, do not enable URP compatibility mode unless the Gaussian splatting package specifically supports it.

## Developer Notes

Most scripts under `Assets/App/Shared/Editor` are setup and repair utilities used while building the scene. A tester should normally only need:

- `Headset Holodeck > Validate Install`
- optional scene setup utilities if a scene is accidentally damaged

Long term, the vendored packages can move to public package forks pinned by commit. For now, vendoring keeps the tester install path simple and reproducible.
