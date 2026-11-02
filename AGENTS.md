# AGENTS.md

This file provides guidance to WARP (warp.dev) when working with code in this repository.

## Project Overview

HeadsetHolodeck is a Unity 6 (6000.2.10f1) XR prototype for creating, revisiting, and modifying
immersive worlds in a headset. Two pipelines live in the same scene and are both current:

- **Direct prototype pipeline** — `Holodeck.*` under `Assets/App/Shared/Scripts/`: wake button →
  microphone capture → OpenAI transcription → World Labs world generation → the vendored World Labs
  plugin loads the world at runtime.
- **Speech-intent pipeline** — `SpeechIntent.*` under `Assets/App/Shared/Command/SpeechIntent/Runtime/`:
  local Sherpa-ONNX VAD/ASR wake detection → OpenAI structured intent (with a local parser that
  short-circuits reserved commands) → `VoiceCommandRouter` → `WorldActionDispatcher`, which owns
  world creation/loading, saved worlds, generated objects, audio, lighting, view mode, UI, and
  manipulation.

The project is an integrated prototype undergoing stabilization. Before changing behaviour, read
[`docs/stabilization/STATUS.md`](docs/stabilization/STATUS.md) and
[`docs/stabilization/handoff-06.md`](docs/stabilization/handoff-06.md): they record what is verified,
what is planned, and the open device findings.

## Shared XR and platform folders

Read [`docs/xr/PLAN.md`](docs/xr/PLAN.md) before Meta/Pico integration work. Common code lives in
`Assets/App/Shared/{Scripts,Command,Save,Editor}`. Keep common content in the existing App asset
folders. Put vendor SDK adapters and target settings in `Assets/App/Platforms/Meta` or
`Assets/App/Platforms/Pico`; do not fork gameplay or provider code per headset. The Pico target is
provisionally Swan/PICO OS 6. Its existing profile is now
`Assets/App/Platforms/Pico/Settings/Pico.asset` with its original GUID.

The existing `Assets/Scenes/Holodeck.unity`, global XR settings, and Android manifest retain the
Meta baseline. Platform folders and the Pico profile do not yet implement target isolation;
reproducible XR/manifest selection and Pico SDK integration are planned in XR-02. Move Unity
assets with their `.meta` files. Keep namespaces and assembly names stable during folder moves.

## Unity Version and installation

**6000.2.10f1** — open the project folder in Unity Hub and ensure this editor version is installed.

## Build & Run

There are no shell build scripts; the Unity editor owns every build path.

- **Run in Editor**: Open `Assets/Scenes/Holodeck.unity`, press Play.
- **Android headset build (menu)**: File > Build Profiles, switch to Android, then Build & Run.
- **Android build (unattended)**: the command-line entry point is
  `Holodeck.Editor.CommandLineAndroidBuild.BuildApk`:

  ```bash
  Unity -batchmode -quit -projectPath <repo> \
    -executeMethod Holodeck.Editor.CommandLineAndroidBuild.BuildApk \
    -outputPath Builds/<name>.apk \
    -credentialMode device \
    -logFile <path>
  ```

  `-outputPath` is optional (default `Builds/Holodeck.apk`).
  `-credentialMode device|private|distribution` declares what the artifact may embed; an unknown
  value stops the build. A headset build takes roughly 15–35 minutes and several GB of disk.
- **Platform rendering requirements** (from the World Labs plugin):
  - URP is required.
  - PC: D3D12 or Vulkan (not D3D11).
  - Android headset: Vulkan.
  - OpenXR Render Mode: Multi-pass.
  - `GaussianSplatURPFeature` must be added to the active URP renderer asset.

## Required Setup Before Running

Provider keys live in a project-root `.env` (gitignored), never in source.
`Holodeck.Direct.HolodeckDirectSecrets.OpenAiApiKey` is an empty `const string` and is unused — do
not type a key into it.

```
OPENAI_API_KEY=your_openai_key
WORLDLABS_API_KEY=your_worldlabs_key
```

A build must declare what it is for before any `.env` entry can reach an artifact:

| Intent | What the artifact embeds | How to declare |
|---|---|---|
| `device` | Nothing | `-credentialMode device`, `HOLODECK_CREDENTIAL_MODE=device`, or `Headset Holodeck > Build Credentials > Device` |
| `private` | Every `.env` entry | `-credentialMode private`, the environment variable, or the menu's Private entry |
| `distribution` | Only non-secret `*_BASE_URL` / `*_SEARCH_URL` entries | `-credentialMode distribution`, the environment variable, or the menu's Distribution entry |
| undeclared | Nothing; a credential-bearing `.env` stops the build with instructions | the state until someone chooses |

Resolution is command line → environment → saved per-machine choice (`EditorPrefs`, never committed).

- **`device` is the intended headset build.** The APK carries no keys; the app reads the headset's
  own file at `Application.persistentDataPath/.env` — on Quest
  `/sdcard/Android/data/com.QLC.HeadsetHolodeck/files/.env`. Place it once with
  `adb push .env /sdcard/Android/data/com.QLC.HeadsetHolodeck/files/.env`.
- **A private build embeds a copy** at `Assets/Resources/WorldLabs/worldlabs_env.txt`, which
  `EnvLoader` reads through `Resources.Load`. That copy is written atomically and removed after the
  build, on editor quit, and at editor startup after a crash; `Assets/Resources/WorldLabs/` is
  gitignored. There is **no `StreamingAssets` copy step any more.** The device `.env` is consulted
  before a baked copy, so a private build can never shadow the headset's own keys.
- **Runtime precedence** (`WorldLabs.API.EnvLoader`): explicit path → device `.env` → project-root
  `.env` (editor/desktop only) → embedded copy → process environment. `SpeechIntent.RuntimeDotEnv`
  loads the device/project `.env` for the other providers and exports it into the process
  environment before the first scene (`[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]`), so both
  readers agree.
- **Logging:** key *names* may appear in logs and build output; values never do. Keep it that way in
  code, logs, and evidence.
- `OpenAITranscriptionClient` rejects an empty key or one containing `REPLACE_WITH` and invokes its
  error callback immediately.

Policy, fixtures, and end-to-end checks: [`handoff-04.md`](docs/stabilization/handoff-04.md) and
[`handoff-05b-device-env.md`](docs/stabilization/handoff-05b-device-env.md).

## Headset workflow

1. Build with `-credentialMode device` (or the menu's Device entry) and install with
   `adb install -r <apk>` — updates in place and keeps saved worlds.
2. `adb push .env /sdcard/Android/data/com.QLC.HeadsetHolodeck/files/.env` once per headset.
3. Launch and say the wake word (`computer`, or one of the deliberate aliases) plus a command.
4. Load cached content first: "computer, load `<saved world name>`".
5. Say "save this world" or use the Ops panel; the save is journaled to disk.
6. Force-stop and relaunch to confirm the world, saved spawn point, objects, and audio return.
7. Read the log with `adb logcat -v time -s Unity` (add `VrApi` for frame timing). Save and restore
   decisions appear under `[WorldConfigStore]`, `[WorldConfigRestorer]`, and
   `[WorldConfigAutoSave]`; key resolution appears under `[RuntimeDotEnv]` and `[EnvLoader]`.

Saves live at `Application.persistentDataPath/Worlds/<config_id>/world.json` (on Quest
`/sdcard/Android/data/com.QLC.HeadsetHolodeck/files/Worlds/<config_id>/world.json`) beside
`world.json.bak` and `.world.json.lock`. Cached splats/panoramas are under `<Worlds>/CachedWorlds`
and cached generated objects under `<Worlds>/CachedObjects`.

## Editor Test Flow

1. Confirm the project-root `.env` has the keys you need; `Headset Holodeck > Validate Install`
   reports presence only, never values.
2. Open `Assets/Scenes/Holodeck.unity` and press Play.
3. Say the wake word plus a prompt, e.g. `Computer, create a castle on a snowy mountain at sunset.`
4. Push-to-talk fallback: press **Space** (or right controller secondary button) once to start
   recording, again to stop and process.
5. Monitor progress in the Console and the `HolodeckStateMachine` component's Inspector.

The wake-word list is a **deliberate alias set**:
`computer, pewter, pudor, peter, impewter, pumpor`
(`Assets/App/Shared/Command/SpeechIntent/VoiceActivationConfig.asset`). The VAD/ASR front end frequently
hears only the tail of "computer"; those accepts are by design, not a bug.

## Architecture

The Direct prototype lives under `Holodeck.*` in `Assets/App/Shared/Scripts/`; the speech/command stack
lives under `SpeechIntent.*` in `Assets/App/Shared/Command/SpeechIntent/Runtime/`; the save layer lives
under `Holodeck.Save` in `Assets/App/Shared/Save/`.

### State Machine (`Holodeck.State`)

`HolodeckStateMachine` is the single source of truth for app state. The primary state flow is:

```
Idle → ListeningForCommand → Interpreting → Generating → Ready
 ↑                                                          |
 └──────────────────────── (any state) ← Error ────────────┘
```

Additionally, `Ready` and `Idle` may transition to each other directly for testing/reset purposes.

Use `TryTransitionTo()` for guarded transitions (logs warnings on invalid moves) or `ForceState()` to bypass guards. `SetError(message)` jumps directly to the `Error` state and fires the `ErrorRaised` event. `ClearErrorAndReturnToIdle()` resets.

### Input (`Holodeck.Input`)

`IWakeTrigger` is the abstraction for the wake signal (single `WakeTriggered` event). `ControllerWakeTrigger` implements it using Unity Input System — bound to `Assets/App/Input/HolodeckInputActions.inputactions`, action `Holodeck/WakeCommand` (right controller secondary button + keyboard Space).

To add a new trigger source, implement `IWakeTrigger` on a `MonoBehaviour` and assign it to the coordinator's `Wake Trigger Behaviour` field.

### Voice Pipeline (`Holodeck.Voice`)

`VoiceCaptureManager` wraps Unity's `Microphone` API. `StartCapture()` / `StopCapture()` are toggle-style; `StopCapture()` returns a `VoiceCaptureResult` containing the trimmed `AudioClip` and raw WAV bytes (encoded by the nested `WavUtility` class at 16-bit PCM). Max recording is 15 s at 16 kHz by default. If no preferred device is specified, the system default microphone is used. On Android, the coroutine `EnsureMicrophonePermissionCoroutine()` is triggered on `Start()`; if permission is still pending when `StartCapture()` is called, capture will fail with a message in `LastFailureMessage`.

`OpenAITranscriptionClient` POSTs WAV bytes to the OpenAI `/v1/audio/transcriptions` endpoint using `UnityWebRequest` multipart form. Model is `gpt-4o-transcribe` (configurable via `HolodeckDirectSecrets`). It exposes a coroutine-friendly API: `TranscribeWav(wavBytes, onSuccess, onError)`.

### Coordinator (`Holodeck.Direct`)

`VoiceToWorldLabsPluginCoordinator` is the top-level orchestrator. It subscribes to `IWakeTrigger.WakeTriggered` and toggles recording on successive presses (first press = begin, second press = stop + generate). The generation coroutine (`RunVoiceToWorldFlow`) drives the state machine through its full arc and bridges into the official World Labs plugin:

1. Calls `WorldLabsClient.GenerateWorldFromTextAsync()` with the transcript as the prompt.
2. Polls `WaitForOperationAsync()` until the world is ready (default 5 s poll interval, 600 s timeout).
3. Calls `worldManager.RestoreDefaultWorld()` then `worldManager.LoadWorldAsync()`.

`generationModel` (Inspector field) sets the active `MarbleModel` tier (Draft / Fast / Standard / High). Call `SetGenerationModel()` to change it at runtime.

**Thread-safety note:** `RestoreDefaultWorld()` and `LoadWorldAsync()` are called from within the async generation task. These calls must be thread-safe or marshalled back to the main thread depending on the World Labs plugin's implementation.

**In-flight task handling:** If the coordinator is disabled or the scene unloads while generation is in progress, the active coroutine is stopped immediately (via `OnDisable`), but the background `GenerateAndLoadWorldAsync()` task may continue to completion. No cancellation token is currently passed to World Labs API calls.

### Voice Activation (`SpeechIntent.VoiceActivation`)

`VadAsrWakeTrigger` is the live wake trigger: Ponyu/Sherpa `MicrophoneSource` + `VadService` +
offline `AsrService`, requiring an ONNX VAD model and suppressing recognition while TTS plays.
`KwsWakeTrigger` is a placeholder for a future native keyword spotter and currently does nothing.
`VoiceActivationConfig.asset` sets `StartsWith` matching, a `0.171` confidence floor, an 8 s wake
phrase cap, a 7 s command window, and a 0.75 s cooldown. `HeadsetHolodeckVoiceController` and
`HeadsetHolodeckCommandRouter` run the listening loop and hand recognitions to `VoiceCommandRouter`.

### Command Routing (`SpeechIntent`)

`OpenAiSpeechIntentService` transcribes (`whisper-1` by default in `OpenAiSpeechIntentConfig`) and
returns reserved local commands (`load <name>` and friends) before calling the structured-intent
model (`gpt-4o` by default). `VoiceCommandRouter` applies clarification and safety rules, then hands
the command to `WorldActionDispatcher`; `LocalTypedIntentParser` holds the local grammar. The Direct
pipeline is separate and uses `Holodeck.Voice.OpenAITranscriptionClient` with `gpt-4o-transcribe`.

`WorldActionDispatcher` (~2,700 lines) and `LocalTypedIntentParser` (~2,100 lines) are large: extract
one bounded responsibility at a time (order 07 in `docs/stabilization/WORK-ORDERS.md`) and keep the
speech regression suites green. The intent contract, the full type catalog, worked examples, and the
add-an-intent checklist are in [`docs/intent.md`](docs/intent.md).

### World Transitions (ownership)

`WorldLabs.Runtime.WorldTransitionOperation` is the shared ownership token. A new request invalidates
its predecessor; only the current operation may register a renderer, publish success, or commit the
saved `ActiveConfig`. Local cancellation is cooperative — a remote provider job may still finish, but
its late result cannot activate a stale operation. Saved world transforms and spawn overrides are
applied before renderer activation, and placement roots stay inactive while they are prepared.
Details and limits: [`handoff-02.md`](docs/stabilization/handoff-02.md).

### Save Journal (`Holodeck.Save`)

`WorldConfigStore` writes every `world.json` through `WorldConfigFile`: serialize and re-parse, take
a per-folder writer lock (`.world.json.lock`), clear abandoned temporaries, write
`world.json.<guid>.tmp` and flush it, rotate the previous committed document to `world.json.bak`,
then rename the temporary over `world.json`. Reads fall back to `.bak` and log the recovery. An
interrupted save therefore loses only the generation being written, and the next successful save
repairs the primary. Startup never auto-loads a saved world: loading is always user-initiated (voice
`load <name>` or the My Worlds panel) so that a damaged or unsatisfiable config cannot produce a
never-ending retry loop at boot. Details and platform limits:
[`handoff-03.md`](docs/stabilization/handoff-03.md) and
[`handoff-03-reverification.md`](docs/stabilization/handoff-03-reverification.md).

### Object Cache and Object Generation (`Holodeck.Direct`)

`CachedObjectStore` keeps generated models, thumbnails, and metadata under `<Worlds>/CachedObjects`
and re-imports them by cache key. `ObjectGenerationService` drives `IObjectGenerationProvider`
implementations:

| Provider | Capabilities | Keys |
|---|---|---|
| `ThreeDAIStudioObjectGenerationProvider` ("3dAIStudio Tripo") | image-to-3D and text-to-3D, with a credit estimate/preflight | `THREEDAISTUDIO_API_KEY`, `THREEDAISTUDIO_BASE_URL` |
| `HitemObjectGenerationProvider` ("Hitem") | image-to-3D | `HITEM_ACCESS_KEY` + `HITEM_SECRET_KEY`, `HITEM_BASE_URL` |

`MESHY_API_KEY` and `TRIPO_API_KEY` are **reserved**: they only feed the "any provider configured"
gate and the install validator. There is no Meshy or standalone Tripo provider class today.

### Hand Interaction

Hand tracking uses XR Hands + XR Interaction Toolkit through `XRHandsGestureSource`,
`HandPointingSourceDriver`, and `HandOnlyInputCoordinator` (grip/pinch → pointing source and
held-object context). `WorldManipulationController` owns "grab world" and one-/two-hand world move,
rotate, and uniform two-hand scale; `SnapWhileGrabbed` and `TargetTransformController` apply the
persisted snap increments to grabbed and voice-moved objects. An editor-only `HolodeckRigTestHarness`
poses a semantic test rig for gaze/hand pointing regressions without hardware.

### Scene Wiring (`Assets/Scenes/Holodeck.unity`)

Root objects: `Me` (the XR Origin hands-variant prefab instance), `Systems`, `Interactables`,
`Environment`, `Teleport Area Setup`, `Lighting`, `floorcollider`, `EventSystem`,
`Hands Permissions Manager`, `GSplatRenderer` (inactive prefab instance), `MeshRoot`, and
`GeneratedWorldRoot`.

- `Systems` hosts the Direct prototype pipeline: `HolodeckStateMachine`, `VoiceCaptureManager`,
  `ControllerWakeTrigger`, `OpenAITranscriptionClient`, `VoiceToWorldLabsPluginCoordinator`,
  `WorldLabsWorldManager`, `RuntimeSplatFloorLoader`, `WorldConfigStore`,
  `ThumbnailSkyboxController`, `HolodeckModelController`, and `HolodeckAudioFeedback`.
- `SpeechIntent` hosts the command stack: `VoiceCommandRouter`, `WorldActionDispatcher`,
  `OpenAiSpeechIntentService`, `VadAsrWakeTrigger`, `HeadsetHolodeckVoiceController`,
  `HeadsetHolodeckCommandRouter`, the audio providers, the splat/pano loaders,
  `WorldConfigRestorer`, `WorldConfigAutoSave`, `HandOnlyInputCoordinator`,
  `WorldManipulationController`, and the UI controllers.
- `CachedObjectStore` hosts the generated-object cache.

Some serialized `m_EditorClassIdentifier` strings in the scene still read `WorldLens.*` from an
earlier namespace while the scripts now say `Holodeck.*` / `SpeechIntent.*`. Unity resolves
components by script GUID, so this is a stale serialized string, not a wiring problem, and Unity
rewrites it on the next scene save. Do not "fix" it by hand.

### External Dependencies (Unity Packages)

Key packages from `Packages/manifest.json`:
- `com.unity.xr.interaction.toolkit` 3.5.0 — XR rig and input
- `com.unity.xr.openxr` 1.17.0-pre.2 — OpenXR runtime
- `com.unity.xr.hands` 1.7.3 — hand tracking
- `com.unity.xr.arfoundation` 6.3.4 — AR Foundation
- `com.unity.render-pipelines.universal` 17.2.0 — URP
- `com.unity.inputsystem` 1.15.0 — new Input System
- `com.unity.xr.androidxr-openxr` 1.0.2 — Android XR/OpenXR support

Two modified packages are **vendored in this repo** as `file:` dependencies, so a tester gets the
exact code the app uses rather than an upstream release:

- `Packages/com.worldlabs.gaussian-splatting` — WorldLabs Gaussian Splatting 1.1.0 (MIT): the World
  Labs API/runtime, the splat renderer, and the build credential boundary. Modified locally.
- `Packages/com.ponyudev.sherpa-onnx` — PonyuDev Sherpa-ONNX 0.1.0 (Apache-2.0): local VAD/ASR/TTS.
  Runtime libraries are under `Assets/Plugins/SherpaOnnx/`; the int8 Zipformer ASR, Silero VAD, and
  Kristin Piper/VITS TTS models are under `Assets/StreamingAssets/SherpaOnnx/`.

## Documentation Map

- [`docs/stabilization/STATUS.md`](docs/stabilization/STATUS.md) — current verified state, findings,
  and the order queue. Start here.
- [`docs/intent.md`](docs/intent.md) — the speech-intent contract: pipeline, the
  `VoiceIntentCommand` fields, all 55 intent types with example utterances, worked examples,
  routing/override rules, how to add an intent, and how to debug one.
- [`docs/stabilization/handoff-06.md`](docs/stabilization/handoff-06.md) — documentation
  reconciliation: the single setup path, verified-versus-planned table, and the superseded-handoff
  index.
- `docs/stabilization/handoff-NN.md` — per-order records with their evidence. Historical; superseded
  by reference in handoff 06.
- `docs/superpowers/plans/` and `docs/superpowers/specs/` — historical design records. Unchecked
  boxes are **not** a backlog.
- [`README.md`](README.md) — tester setup and install path. [`USER_MANUAL.md`](USER_MANUAL.md) —
  feature manual. [`spz-renderer-handoff-notes.md`](spz-renderer-handoff-notes.md) — historical SPZ
  renderer investigation.
- [`docs/agent-worklog.md`](docs/agent-worklog.md) — cross-agent ledger. Records what the OpenAI
  (Codex) session changed and left uncommitted, what the DeepSeek session is doing next, and the
  local toolchain facts (Unity CLI live control, adb, emulator, pico-cli limitations). **Read this
  before assuming uncommitted work is yours to overwrite.**

<!-- pico-cli:plugin-context:pico-unity-agentic-tools:start -->
## Plugin Context

Also read `./PICO-UNITY-AGENTIC-TOOLS.AGENTS.md` for PICO Spatial plugin guidance.
<!-- pico-cli:plugin-context:pico-unity-agentic-tools:end -->

<!-- pico-cli:plugin-context:pico-general-agentic-tools:start -->
## Plugin Context

Also read `./PICO-GENERAL-AGENTIC-TOOLS.AGENTS.md` for PICO Spatial plugin guidance.
<!-- pico-cli:plugin-context:pico-general-agentic-tools:end -->
