# AGENTS.md

This file provides guidance to WARP (warp.dev) when working with code in this repository.

## Project Overview

HeadsetHolodeck is a Unity 6 (6000.2.10f1) XR project that lets a user speak a prompt into a headset microphone and generates an immersive 3D world in real time via the World Labs API. The pipeline is: wake button press → microphone capture → OpenAI transcription → World Labs world generation → official World Labs Unity plugin loads the world at runtime.

## Unity Version

**6000.2.10f1** — open the project folder in Unity Hub and ensure this editor version is installed.

## Build & Run

There are no CLI build scripts. All building is done through the Unity Editor:

- **Run in Editor**: Open `Assets/Scenes/Holodeck.unity`, press Play.
- **Android headset build**: File > Build Settings > Android, switch platform, then Build & Run.
- **Platform rendering requirements** (from the World Labs plugin):
  - URP is required.
  - PC: D3D12 or Vulkan (not D3D11).
  - Android headset: Vulkan.
  - OpenXR Render Mode: Multi-pass.
  - `GaussianSplatURPFeature` must be added to the active URP renderer asset.

## Required Setup Before Running

**OpenAI key** — open `Assets/App/Scripts/Direct/HolodeckDirectSecrets.cs` and replace `REPLACE_WITH_YOUR_OPENAI_KEY` with a real key. This is hardcoded intentionally for prototype/hackathon use; do not ship.

**World Labs key** — create a `.env` file in the project root (next to `Assets/`):
```
WORLDLABS_API_KEY=your_worldlabs_key
```
The official World Labs plugin reads this in Editor mode and copies it to `StreamingAssets/.env` for builds.

**OpenAI key validation:** The code checks that the key is not empty and does not contain the placeholder string "REPLACE_WITH". If either check fails, transcription will immediately invoke the error callback.

## Editor Test Flow

1. Confirm both keys are set (see above).
2. Open `Assets/Scenes/Holodeck.unity` and press Play.
3. Press **Space** (or right controller secondary button) once to start recording.
4. Speak a world prompt.
5. Press **Space** again to stop recording and trigger generation.
6. Monitor progress in the Console and the `HolodeckStateMachine` component's Inspector.

## Architecture

All app code lives under the `Holodeck.*` namespace in `Assets/App/Scripts/`.

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

### Scene Hierarchy

```
XR Origin
Systems
  ├── HolodeckStateMachine
  ├── VoiceCaptureManager
  ├── ControllerWakeTrigger
  ├── OpenAITranscriptionClient
  ├── WorldLabsWorldManager       ← from official plugin
  └── VoiceToWorldLabsPluginCoordinator
GeneratedWorldRoot                ← assigned to WorldLabsWorldManager.WorldParent
```

### External Dependencies (Unity Packages)

Key packages from `Packages/manifest.json`:
- `com.unity.xr.interaction.toolkit` 3.3.0 — XR rig and input
- `com.unity.xr.openxr` 1.15.1 — OpenXR runtime
- `com.unity.xr.hands` 1.7.1 — hand tracking
- `com.unity.render-pipelines.universal` 17.2.0 — URP
- `com.unity.inputsystem` 1.15.0 — new Input System
- `com.unity.xr.androidxr-openxr` 1.0.2 — Android XR/OpenXR support

The World Labs plugin (`WorldLabs.API`, `WorldLabs.Runtime`) is a separate Unity plugin not tracked in this repo.
