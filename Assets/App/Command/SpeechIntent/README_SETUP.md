# Unity VR Speech Intent Package

This package implements a speech-to-command pipeline for Unity XR apps.

## What it does

1. Records microphone input to WAV.
2. Sends the WAV to OpenAI `/audio/transcriptions`.
3. Sends the resulting transcript plus spatial/scene context to OpenAI `/responses` with Structured Outputs.
4. Converts the model result into a typed `VoiceIntentCommand`.
5. Dispatches the command to scene controllers.
6. Tracks recent interaction context so pronouns like `it` and `that` can refer to the last created or last interacted target.

## Command coverage in this revision

Supported commands now include:

- world generation
- static world switch
- UI display
- sun alignment
- lighting preset changes
- object placement
- target move
- target scale
- target rotation
- clarification / inquiry responses when the request is underspecified

Examples:

- `Put me on a beach on a sunny day` -> `GenerateWorld`
- `End program` -> `SwitchToStaticWorld`
- `Arch` -> `ShowUi`
- `Put the sun there` -> `SetSunDirection`
- `Make it night time` -> `SetLightingPreset`
- `Place tree here` -> `PlaceObject`
- `Make it bigger` -> `ScaleTarget`
- `Make it twice as big` -> `ScaleTarget` with multiplier `2.0`
- `Make it 10% smaller` -> `ScaleTarget` with multiplier `0.9`
- `Rotate it by 45 degrees` -> `RotateTarget` on `Y` by default
- `Flip it upside down` -> `RotateTarget` on `X` by `180`
- `Move that here` -> `MoveTarget`
- `Rotate it` -> likely `AskClarification`

## Important security note

For local prototypes, direct OpenAI mode works.

For production, do **not** ship the OpenAI API key inside the client. Use a small backend or relay instead.

## Unity package dependency

Install **Newtonsoft JSON for Unity** if your project does not already have it.

Package name:
`com.unity.nuget.newtonsoft-json`

## Suggested scene wiring

Create an empty GameObject called `SpeechSystem` and add:

- `MicrophoneWavRecorder`
- `SpatialContextProvider`
- `SceneSemanticContextProvider`
- `OpenAiSpeechIntentService`
- `VoiceCommandRouter`
- `WorldActionDispatcher`
- `InteractionMemory`
- `SceneEntityResolver`
- `TargetTransformController`

Optional helpers:

- `LightRigController`
- `UiPanelController`
- `ObjectPlacementController`
- `StaticWorldController`

## Basic hookup

### 1. Config asset
Create:
`Assets/Create/Speech Intent/OpenAI Config`

Assign it to `OpenAiSpeechIntentService.config`.

### 2. Pointer sources
Add `PointingSource` to left and right controller or hand-anchor objects.
Assign those to `SpatialContextProvider.leftHandSource` and `.rightHandSource`.

If you already have custom hand-pointing logic, just drive:

- `PointingSource.isAvailable`
- `PointingSource.isPointing`

### 3. Head transform
Assign the XR camera or head transform to `SpatialContextProvider.headTransform`.

### 4. Recording control
Call:

- `VoiceCommandRouter.BeginRecording()`
- `VoiceCommandRouter.EndRecordingAndProcess()`

Add a `PushToTalkTrigger` component to the same GameObject, assign the `WakeCommand` InputActionReference, and it will call these automatically on button press/release. The `WakeCommand` action is bound to the right controller B button and Space.

### 5. Context memory
Assign the same `InteractionMemory` reference to:

- `SceneSemanticContextProvider.interactionMemory`
- `SceneEntityResolver.interactionMemory`
- `TargetTransformController.interactionMemory`
- `WorldActionDispatcher.interactionMemory`

This is what makes `it` and `that` work across turns.

### 6. Generated world registration
When your WorldLabs world finishes loading, call one of these on `WorldActionDispatcher`:

- `RegisterGeneratedWorld(GameObject worldRoot)`
- `RegisterGeneratedWorldWithDescription(GameObject worldRoot, string worldDescription)`

That lets later commands like `make it bigger` refer to the current world root.

### 7. Entity resolution
Assign `SceneEntityResolver` to:

- `SceneSemanticContextProvider.entityResolver`
- `TargetTransformController.entityResolver`

For important scene objects, add `SpeechIntentTrackable` and set a canonical name plus aliases.
This improves resolution for commands like `move lighthouse here`.

### 8. World generation integration
The dispatcher exposes:

- `onGenerateWorldPrompt` (`string`)

Wire that UnityEvent to your WorldLabs entry point.

### 9. Clarification / TTS integration
`VoiceCommandRouter` now exposes:

- `onAssistantResponse`

Use that for TTS or floating text when the model asks a follow-up like:
`About which axis would you like to rotate it?`

### 10. UI integration
Populate `UiPanelController.panels` with keys like:

- `arch_menu`
- `main_menu`

### 11. Object placement integration
Populate `ObjectPlacementController.namedPrefabs` for objects you already have.
Unknown objects fall back to a debug cube placeholder.
Placed objects are automatically tagged with `SpeechIntentTrackable` unless you disable it.

## Recommended production backend contract

If `config.useProxyServer = true`, Unity will POST multipart form data to:

`config.proxyInterpretUrl`

Fields:

- `file` = wav bytes
- `spatial_context_json`
- `scene_context_json`

Expected response:
`SpeechIntentResult` JSON

## Notes on ambiguity

The model is instructed to default when the UX is usually obvious:

- `rotate it by 45 degrees` -> default axis `Y`
- `make it bigger` -> modest scale up
- `that is too big` -> modest scale down

It is also instructed to ask when a missing detail would make the result too arbitrary:

- `rotate it`
- `move it a bit`
- `make that look better`

## Extending it further

Natural next steps are:

- selection commands like `select the palm tree`
- relative transforms like `move it two meters left`
- multi-object commands like `place three rocks over there`
- edit sessions like `keep making it darker`
- tool calls to your object-generation backend when a named object does not already exist in the scene
