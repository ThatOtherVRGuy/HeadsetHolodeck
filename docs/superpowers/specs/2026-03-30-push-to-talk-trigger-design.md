# Push-to-Talk Trigger — Design Spec

## Goal

Wire the existing `WakeCommand` InputAction (right controller B button + Space) to `VoiceCommandRouter.BeginRecording()` / `EndRecordingAndProcess()` via a new `PushToTalkTrigger` component, and remove the stale V-key keyboard polling from `VoiceCommandRouter`.

---

## Context

`VoiceCommandRouter` has its own `Update()` loop that polls `KeyCode.V` for push-to-talk. This is redundant now that the project has `HolodeckInputActions.inputactions` with a `WakeCommand` Button action already bound to:
- `<XRController>{RightHand}/secondaryButton` (B button)
- `<Keyboard>/space`

The old voice pipeline used `ControllerWakeTrigger` which only handled `performed` (single press). Push-to-talk requires both `started` (begin recording) and `canceled` (stop recording and process).

`VoiceToWorldLabsPluginCoordinator` and the state machine are deliberately **not** involved — `VoiceCommandRouter._isRecording` already prevents re-entrant presses, and `TriggerWorldGeneration.isBusy` blocks mid-generation triggers downstream.

---

## Architecture

### New component: `PushToTalkTrigger`

**File:** `Assets/App/Command/SpeechIntent/Runtime/PushToTalkTrigger.cs`
**Namespace:** `SpeechIntent`

Single-responsibility component that bridges Unity Input System to `VoiceCommandRouter`.

```
[SerializeField] InputActionReference pushToTalkAction
[SerializeField] VoiceCommandRouter   router
```

Lifecycle:
- `OnEnable`: subscribe `action.started` → `router.BeginRecording()`, `action.canceled` → `router.EndRecordingAndProcess()`, call `action.Enable()`
- `OnDisable`: unsubscribe both callbacks (does **not** call `action.Disable()` — the action is shared and may be used by other components)
- Missing-reference guard: log a warning and return early from `OnEnable` if either reference is null

No `Update()`. No state machine coupling. No audio feedback.

### Modified: `VoiceCommandRouter`

**File:** `Assets/App/Command/SpeechIntent/Runtime/VoiceCommandRouter.cs`

Remove:
- `[Header("Debug Controls")]` block: `enableKeyboardDebug`, `pushToTalkKey`
- `_wasRecordingFromKeyboard` field
- Entire `Update()` method

`BeginRecording()` and `EndRecordingAndProcess()` remain public and unchanged.

### Modified: `SpeechIntentSceneSetup`

**File:** `Assets/App/Editor/SpeechIntentSceneSetup.cs`

Add to `SetupSpeechIntent()`:
1. `GetOrAdd<PushToTalkTrigger>(speechRoot)`
2. Wire `trigger.router = router`
3. Load `HolodeckInputActions.inputactions` via `AssetDatabase.LoadAssetAtPath<InputActionAsset>`, find action `"Holodeck/WakeCommand"`, create `InputActionReference.Create(action)`, assign via `SerializedObject`

Warn if the InputActions asset is not found (directs user to check the path).

---

## InputActions Asset

**Path:** `Assets/App/Input/HolodeckInputActions.inputactions`

Existing `WakeCommand` bindings — no changes required:
- `<XRController>{RightHand}/secondaryButton`
- `<Keyboard>/space`

---

## Known Constraints

- `InputActionReference.Create(action)` creates an in-memory reference that stores the InputActionAsset GUID and action ID. It serializes correctly with the scene when saved.
- `PushToTalkTrigger` calls `action.Enable()` in `OnEnable` but does **not** call `action.Disable()` in `OnDisable` — the `WakeCommand` action is defined in the shared `HolodeckInputActions` asset and may be enabled by other consumers.
- The Space binding replaces the old V key. Users who were using V must switch to Space.

---

## What This Does NOT Do

- No state machine integration (`HolodeckStateMachine` transitions for Listening/Interpreting are out of scope)
- No audio or visual feedback for the recording state
- No changes to `ControllerWakeTrigger` (left in place for potential future use)
- No changes to `HolodeckInputActions.inputactions`
