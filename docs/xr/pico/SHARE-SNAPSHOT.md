# Share snapshot — 2026-11-01

What this repository is, what state it is in, and what a partner needs to know to build and run it.

## What it is

A Unity 6 (6000.2.10f1) XR prototype for the PICO Swan (PICO OS 6): speech-driven creation, loading and
editing of immersive worlds in a headset. Users talk to it ("create a world of a castle at sunset", "load
martian outpost", "show panorama", "save this world"); it talks back through an on-device voice.

Two speech paths coexist and both are current:

- **Local-first transcription** — an on-device Sherpa-ONNX recogniser runs first; the reserved command
  grammar answers offline for free, and only free-form speech is sent to a cloud transcriber.
- **Speech-intent pipeline** — local VAD/ASR wake detection → intent (local grammar or OpenAI structured
  intent) → `VoiceCommandRouter` → `WorldActionDispatcher`, which owns world creation, saved worlds,
  generated objects, audio, lighting, view modes, UI and manipulation.

Worlds come from World Labs (Gaussian splats); objects from 3dAIStudio/Tripo or Hitem; audio from Freesound
/ Pixabay / Openverse. Generated content is cached and reused before anything is regenerated.

## Building

Requires Unity **6000.2.10f1** with Android modules, and a PICO OS 6 device or the PICO Emulator.

- Menu: **Headset Holodeck → Build → Build Pico APK (device)**. Takes 15–25 minutes, writes
  `Builds/Pico-Swan-parity.apk`.
- Command line: `Holodeck.Editor.CommandLineAndroidBuild.BuildApk` with `-credentialMode device`.
- Output: arm64-v8a, Vulkan, OpenXR MultiPass, IL2CPP, min SDK 32 / target 36.

**No API keys are compiled in.** The `device` credential mode is deliberate: the app reads
`Application.persistentDataPath/.env` at launch, so keys live on the headset only. A build with keys
embedded requires the `private` mode and is not how this ships.

### Keys the app looks for

`OPENAI_API_KEY`, `WORLDLABS_API_KEY`, `THREEDAISTUDIO_API_KEY`, `THREEDAISTUDIO_BASE_URL`,
`FREESOUND_API_KEY`, `PIXABAY_API_KEY`, `OPENVERSE_AUDIO_SEARCH_URL`, `HITEM_ACCESS_KEY`,
`HITEM_SECRET_KEY`, `HITEM_BASE_URL`, `XENO_CANTO_API_KEY`, `XENO_CANTO_SEARCH_URL`,
`OPENVERSE_AUDIO_SEARCH_URL`. Only OpenAI and World Labs are required for the core loop.

## Running

```powershell
adb install -r Builds/Pico-Swan-parity.apk
adb shell monkey -p com.QLC.HeadsetHolodeckPico -c android.intent.category.LAUNCHER 1
```

The app logs one self-contained `[Diag]` line every five seconds — state, view, world, hands, controllers,
pointer, microphone, STT availability, camera. It is the fastest way to tell whether it is healthy.

Commands can be driven without voice by writing to `files/debug_command.txt` in the app's data directory;
this is how the emulator is tested, since its microphone is silent.

## State of verification (2026-11-01)

| Capability | Status |
|---|---|
| Launches on hardware (PICO Swan, OS 6.2.0) at 90 FPS | verified |
| Controllers optional (hand tracking declared, no controller requirement) | verified |
| Eye tracking device reaches the app | verified |
| **Controller ray hover + UI select** | verified in the emulator after a fix (see below) |
| Master volume / mute by voice | verified in the emulator via logs |
| Camera capture (WebXR-style `WebCamTexture`) | verified on hardware |
| 3D ↔ panorama switching, metric scale, 1 m placement, cache reuse, saves | verified |
| Gaze→UI hover | implemented against OpenXR `gazePosition`/`gazeRotation`; **cannot be verified in the emulator**, which supplies no gaze pose — see known issues. Needs hardware |
| Hand pose tracking on the emulator | not supported by this emulator image |
| Wake-word recall | measured at 1 in 15 on hardware; recording fixtures exist to improve it |

### The two bugs worth knowing about

**UI clicking never worked and never warned.** `XRUIToolkitManager` was absent from the scene, so
`XRUIToolkitHandler.uiToolkitSupportEnabled` stayed false and every XRI interactor's UI interaction was
silently disabled: rays tracked and rendered while nothing was clickable. Now installed at runtime by
`XRUIToolkitSupport`, and the beacon reports `uiToolkit=true`.

**Gaze was aimed from the head, not the eyes.** `XRGazeInteractor` derives from `XRRayInteractor` and never
reads an eye pose, and the original rig pointed it at a stabilised transform whose stabiliser targeted the
interactor itself. It now reads OpenXR's `EyeTrackingUsages.gazePosition/gazeRotation` from the device
carrying the `EyeTracking` characteristic, with centre-eye and head-gaze fallbacks.

## Known issues

- **Wake word is unreliable** (~1/15 on hardware). Push-to-talk (right controller secondary button, or Space
  in the editor) is the dependable path. The alias list is a deliberate set of mishears; the measurement and
  the plan are in `docs/xr/pico/WAKE-WORD-EVIDENCE.md`.
- **Any system dialog kills the microphone** until the app is relaunched. The wake trigger does not
  re-initialise after a permission prompt or an app pause.
- **The emulator microphone is silent.** Voice must be tested with recorded fixtures (`replay` debug
  command) or on hardware.
- **The emulator supplies no gaze pose.** Measured 2026-11-01: the eye device reports `isTracked` and a
  static centre-eye pose at `(0.00,1.65,0.00)` with identity rotation, and never exposes OpenXR's
  `gazePosition`/`gazeRotation`; moving the mouse in Eye Gesture Mode changes nothing. So gaze cannot be
  tested in the emulator at all — a gaze failure there says nothing about the app. Gaze needs a headset,
  and the controller ray is the emulator's only usable pointer.
- **Android broadcast registration fails on API 36** (`registerReceiver` signature change). The file-based
  debug channel is used instead.
- Ten everyday commands still require the cloud because the app has no capability for them (global mute was
  added; world cycling, help and brightness are not implemented). Details in
  `docs/xr/pico/LOCAL-FIRST-TRANSCRIPTION.md`.

## Repository size

~551 MB of tracked content, ~4,950 files. The largest files are speech models (87 MB fp32 encoder, 24.8 MB
int8 encoder, 18.5 MB voice), macOS ONNX runtime binaries (2 × 33.5 MB, irrelevant to a headset build) and
Unity sample content. Everything is under GitHub's 100 MB per-file limit, so **no Git LFS is required**.

If a smaller tree is wanted for partners, the safe savings without touching app code are the `osx-arm64`
native libraries (~67 MB) and unused Unity samples (~40 MB). The fp32 encoder is a fallback for the int8
one and can go if the int8 path is trusted.

## Where to read next

| Document | Contents |
|---|---|
| `docs/intent.md` | The speech-intent contract, all intent types, worked examples |
| `docs/xr/pico/INTERACTION-TEST-RESULTS.md` | Emulator interaction testing, round 1 and 2 |
| `docs/xr/pico/WAKE-WORD-EVIDENCE.md` | Wake-word measurements and alias rationale |
| `docs/xr/pico/LOCAL-FIRST-TRANSCRIPTION.md` | The local-vs-cloud decision and its limits |
| `docs/xr/pico/FRIDAY-LOG-GUIDE.md` | How to read the logs and what each field means |
| `docs/xr/pico/CODEX-INTERACTION-TESTING-BRIEF.md` | Environment, traps, and how to test interaction |
| `docs/agent-worklog.md` | Cross-session ledger: what was done, by whom, what is open |

## Ownership and history

Developed across two agent sessions plus owner review. The private development repository also carries a
Meta Quest target; a shared snapshot may or may not include it, which is a packaging decision rather than a
technical one — see `docs/xr/REPO-MIGRATION.md`.
