# Holodeck Direct + Official World Labs Plugin Bundle

This bundle is for the stripped-down prototype path:

wake button -> microphone capture -> OpenAI transcription -> World Labs generation -> official World Labs Unity plugin loads the generated world at runtime.

## What is included
- Wake trigger abstraction and controller implementation
- Simple holodeck state enum + state machine
- Microphone capture manager that returns WAV bytes
- OpenAI transcription client (direct from Unity)
- Coordinator that bridges the transcript into the official World Labs Unity plugin
- Input Actions asset with:
  - `WakeCommand` -> right controller `secondaryButton` + keyboard `Space`

## What is NOT included
- No backend
- No .env loader on your side
- No custom World Labs loader on your side
- No bird features

## Important key setup
### OpenAI
Open `Assets/App/Scripts/Direct/HolodeckDirectSecrets.cs` and replace:

`REPLACE_WITH_YOUR_OPENAI_KEY`

### World Labs
Leave the official plugin unchanged.

The official `worldlabs_unity` plugin expects:

- In the Editor: a `.env` file in the Unity project root
- In builds: the plugin copies that into `StreamingAssets/.env`

Example project-root `.env`:

```env
WORLDLABS_API_KEY=your_worldlabs_key
```

## Scene hierarchy
A minimal scene is:

XR Origin
Systems
GeneratedWorldRoot

## Attach these to Systems
- `HolodeckStateMachine`
- `VoiceCaptureManager`
- `ControllerWakeTrigger`
- `OpenAITranscriptionClient`
- `WorldLabsWorldManager`  (from the official plugin)
- `VoiceToWorldLabsPluginCoordinator`

## Inspector wiring
### ControllerWakeTrigger
- `Wake Action` -> `Assets/App/Input/HolodeckInputActions.inputactions`
- choose `Holodeck / WakeCommand`

### VoiceToWorldLabsPluginCoordinator
- `Wake Trigger Behaviour` -> `ControllerWakeTrigger`
- `State Machine` -> `HolodeckStateMachine`
- `Voice Capture Manager` -> `VoiceCaptureManager`
- `Transcription Client` -> `OpenAITranscriptionClient`
- `World Manager` -> `WorldLabsWorldManager`

### WorldLabsWorldManager
- `World Parent` -> `GeneratedWorldRoot`
- `Preferred Resolution` -> `500k` for a good first pass
- `Quality` -> `Medium`

## Suggested project settings for the official plugin
See the plugin README, but the important ones are:
- URP required
- Windows should use D3D12 or Vulkan, not D3D11
- Android headset builds should use Vulkan
- OpenXR Render Mode should be Multi-pass
- Add `GaussianSplatURPFeature` to the active URP renderer

## Editor test
1. Put `WORLDLABS_API_KEY=...` in your project-root `.env`
2. Put your OpenAI key into `HolodeckDirectSecrets.cs`
3. Press Play
4. Press Space once to start recording
5. Speak a prompt
6. Press Space again to stop and generate
7. Watch the Console and the `HolodeckStateMachine`

## Notes
- This is intentionally not secure. The OpenAI key is hardcoded in the client.
- The World Labs plugin still loads its key from `.env`.
