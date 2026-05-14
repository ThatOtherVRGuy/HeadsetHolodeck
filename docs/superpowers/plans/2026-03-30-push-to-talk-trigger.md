# Push-to-Talk Trigger Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Wire the existing `WakeCommand` InputAction (right controller B button + Space) to `VoiceCommandRouter` via a new `PushToTalkTrigger` component, and remove the stale V-key keyboard polling from `VoiceCommandRouter`.

**Architecture:** A new `PushToTalkTrigger` MonoBehaviour in the `SpeechIntent` namespace subscribes to `InputAction.started` / `InputAction.canceled` and calls `router.BeginRecording()` / `router.EndRecordingAndProcess()`. `VoiceCommandRouter`'s `Update()` and keyboard fields are removed. `SpeechIntentSceneSetup` is extended to add and wire the new component automatically.

**Tech Stack:** Unity 6, Unity Input System 1.15.0, `UnityEngine.InputSystem.InputActionReference`, `UnityEditor.AssetDatabase`, C#

---

## File Map

| Action | Path |
|--------|------|
| **Create** | `Assets/App/Command/SpeechIntent/Runtime/PushToTalkTrigger.cs` |
| **Modify** | `Assets/App/Command/SpeechIntent/Runtime/VoiceCommandRouter.cs` |
| **Modify** | `Assets/App/Editor/SpeechIntentSceneSetup.cs` |

---

### Task 1: Create PushToTalkTrigger

**Files:**
- Create: `Assets/App/Command/SpeechIntent/Runtime/PushToTalkTrigger.cs`

- [ ] **Step 1: Create the file with the full implementation**

```csharp
using UnityEngine;
using UnityEngine.InputSystem;

namespace SpeechIntent
{
    /// <summary>
    /// Bridges the WakeCommand InputAction to VoiceCommandRouter push-to-talk.
    /// action.started  → router.BeginRecording()
    /// action.canceled → router.EndRecordingAndProcess()
    /// </summary>
    public class PushToTalkTrigger : MonoBehaviour
    {
        public InputActionReference pushToTalkAction;
        public VoiceCommandRouter   router;

        private void OnEnable()
        {
            if (pushToTalkAction == null || pushToTalkAction.action == null)
            {
                Debug.LogWarning("[PushToTalkTrigger] pushToTalkAction is not assigned.", this);
                return;
            }

            if (router == null)
            {
                Debug.LogWarning("[PushToTalkTrigger] router is not assigned.", this);
                return;
            }

            pushToTalkAction.action.started  += OnActionStarted;
            pushToTalkAction.action.canceled += OnActionCanceled;
            pushToTalkAction.action.Enable();
        }

        private void OnDisable()
        {
            if (pushToTalkAction?.action == null) return;

            pushToTalkAction.action.started  -= OnActionStarted;
            pushToTalkAction.action.canceled -= OnActionCanceled;
            // Do NOT call action.Disable() — the action is shared and may be
            // used by other components (e.g. ControllerWakeTrigger).
        }

        private void OnActionStarted(InputAction.CallbackContext context)  => router?.BeginRecording();
        private void OnActionCanceled(InputAction.CallbackContext context) => router?.EndRecordingAndProcess();
    }
}
```

- [ ] **Step 2: Verify it compiles**

Open Unity and wait for the status bar to show no errors. The Console should be clear of compile errors related to `PushToTalkTrigger`.

Expected: 0 compiler errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/App/Command/SpeechIntent/Runtime/PushToTalkTrigger.cs
git add Assets/App/Command/SpeechIntent/Runtime/PushToTalkTrigger.cs.meta
git commit -m "feat: add PushToTalkTrigger component for controller/space push-to-talk"
```

---

### Task 2: Remove keyboard polling from VoiceCommandRouter

**Files:**
- Modify: `Assets/App/Command/SpeechIntent/Runtime/VoiceCommandRouter.cs`

`VoiceCommandRouter` currently polls `KeyCode.V` in `Update()`. This is replaced by `PushToTalkTrigger` + the `WakeCommand` InputAction (Space + controller B). Remove the self-contained keyboard input entirely.

Current state of the file around the fields and Update():
```csharp
// Lines 26-47 — to be removed entirely:
[Header("Debug Controls")]
public bool enableKeyboardDebug = true;
public KeyCode pushToTalkKey = KeyCode.V;

// ...

private bool _wasRecordingFromKeyboard;
private bool _isRecording;

private void Update()
{
    if (!enableKeyboardDebug)
    {
        return;
    }

    if (Input.GetKeyDown(pushToTalkKey))
    {
        _wasRecordingFromKeyboard = true;
        BeginRecording();
    }

    if (_wasRecordingFromKeyboard && Input.GetKeyUp(pushToTalkKey))
    {
        _wasRecordingFromKeyboard = false;
        EndRecordingAndProcess();
    }
}
```

- [ ] **Step 1: Remove the keyboard fields and Update() method**

Delete these from `VoiceCommandRouter.cs`:
- The `[Header("Debug Controls")]` attribute and the two fields beneath it (`enableKeyboardDebug`, `pushToTalkKey`)
- The `_wasRecordingFromKeyboard` field
- The entire `Update()` method (lines 29–47 of current file)

Keep `_isRecording` — it is used by `BeginRecording()` and `EndRecordingAndProcess()`.

After the edit, the field section should look like:

```csharp
private bool _isRecording;
```

And the first method after the event fields should be `BeginRecording()` directly.

- [ ] **Step 2: Verify it compiles**

Wait for Unity to recompile. Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/App/Command/SpeechIntent/Runtime/VoiceCommandRouter.cs
git commit -m "refactor: remove keyboard push-to-talk from VoiceCommandRouter (replaced by PushToTalkTrigger)"
```

---

### Task 3: Update SpeechIntentSceneSetup and verify end-to-end

**Files:**
- Modify: `Assets/App/Editor/SpeechIntentSceneSetup.cs`

This task wires `PushToTalkTrigger` into the scene setup script and verifies the full flow.

The existing `SetupSpeechIntent()` method (read the file first — it's ~100 lines):
- Step 2 adds components with `GetOrAdd<T>`
- Step 5 calls `Undo.RecordObjects` on `{ service, dispatcher, memory, router }` then wires fields directly
- Step 6 wires cross-system UnityEvents

Changes needed:
1. Add `InputActionsPath` constant
2. Add `using UnityEngine.InputSystem;` at the top
3. Add `GetOrAdd<PushToTalkTrigger>` in step 2
4. Add `trigger` to `Undo.RecordObjects` in step 5
5. Wire `trigger.router = router` in step 5
6. Wire `trigger.pushToTalkAction` via `AssetDatabase.LoadAllAssetsAtPath` in step 5

- [ ] **Step 1: Add the `InputActionsPath` constant and using directive**

At the top of the class, alongside `ConfigAssetPath`:
```csharp
private const string InputActionsPath =
    "Assets/App/Input/HolodeckInputActions.inputactions";
```

At the top of the file, add:
```csharp
using UnityEngine.InputSystem;
```

- [ ] **Step 2: Add `GetOrAdd<PushToTalkTrigger>` in the component section (step 2 of `SetupSpeechIntent`)**

After `VoiceCommandRouter router = GetOrAdd<VoiceCommandRouter>(speechRoot);`, add:
```csharp
PushToTalkTrigger trigger = GetOrAdd<PushToTalkTrigger>(speechRoot);
```

- [ ] **Step 3: Add `trigger` to `Undo.RecordObjects` and wire its fields**

Replace the existing `Undo.RecordObjects` call:
```csharp
Undo.RecordObjects(
    new Object[] { service, dispatcher, memory, router },
    "Wire SpeechIntent Components");
```
With:
```csharp
Undo.RecordObjects(
    new Object[] { service, dispatcher, memory, router, trigger },
    "Wire SpeechIntent Components");
```

After `router.dispatcher = dispatcher;`, add:
```csharp
trigger.router = router;
```

- [ ] **Step 4: Wire `trigger.pushToTalkAction` via AssetDatabase**

After `trigger.router = router;`, add:

```csharp
// Wire PushToTalkTrigger.pushToTalkAction — load the InputActionAsset, find the
// WakeCommand action, and create an InputActionReference from it.
// InputActionReference.Create() stores the asset GUID + action ID, so it serializes
// correctly as an embedded object in the scene when the user saves.
InputActionAsset actionAsset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath);
if (actionAsset != null)
{
    InputAction wakeAction = actionAsset.FindAction("Holodeck/WakeCommand");
    if (wakeAction != null)
    {
        InputActionReference wakeRef = InputActionReference.Create(wakeAction);
        SerializedObject triggerSo = new SerializedObject(trigger);
        triggerSo.FindProperty("pushToTalkAction").objectReferenceValue = wakeRef;
        triggerSo.ApplyModifiedProperties();
    }
    else
    {
        Debug.LogWarning("[SpeechIntentSceneSetup] 'Holodeck/WakeCommand' action not found in " +
                         InputActionsPath + ". Assign PushToTalkTrigger.pushToTalkAction manually.");
    }
}
else
{
    Debug.LogWarning("[SpeechIntentSceneSetup] InputActions asset not found at " +
                     InputActionsPath + ". Assign PushToTalkTrigger.pushToTalkAction manually.");
}
```

- [ ] **Step 5: Verify it compiles**

Wait for Unity to recompile. Expected: 0 errors.

- [ ] **Step 6: Run the setup and verify in the Inspector**

1. Run **Holodeck > Setup SpeechIntent** from the menu
2. In the Hierarchy, select **Systems > SpeechIntent**
3. Verify `PushToTalkTrigger` component is present with:
   - `Push To Talk Action` → `HolodeckInputActions - Holodeck/WakeCommand` (or similar)
   - `Router` → the `VoiceCommandRouter` on the same GameObject
4. Verify `VoiceCommandRouter` no longer has `Enable Keyboard Debug` or `Push To Talk Key` fields

If `pushToTalkAction` is null (the sub-asset lookup failed), assign it manually: drag `HolodeckInputActions` asset → expand → drag `WakeCommand` into the `Push To Talk Action` field.

- [ ] **Step 7: Verify push-to-talk in Play mode**

1. Enter Play mode
2. Hold **Space** (or the right controller B button on device)
3. Speak a world description
4. Release Space/B
5. Confirm: transcription fires, `WorldActionDispatcher` routes to `TriggerWorldGeneration`, world loads

Expected: world generates from voice command without touching V key.

- [ ] **Step 8: Commit**

```bash
git add Assets/App/Editor/SpeechIntentSceneSetup.cs
git commit -m "feat: wire PushToTalkTrigger in SpeechIntentSceneSetup"
```
