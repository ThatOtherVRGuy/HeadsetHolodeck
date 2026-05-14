# Sub-project 5: 3D/Pano View Mode Toggle — Design

## Goal

Add `Show3dWorld` and `ShowPanoWorld` voice commands that let the user toggle between the splat world and the panoramic sphere at any time. The desired view mode persists across world generations and resolves deferred/error states when world or panorama load results arrive.

## Background

All display infrastructure is already implemented:
- `ThumbnailSkyboxController` — manages the panoramic sphere; `Show(Texture2D)` takes ownership; `StartFadeOut()` hides and destroys the texture
- `WorldLabsWorldManager` — `OnWorldLoaded` / `OnWorldUnloaded` events for splat state
- `HolodeckStateMachine` — `StateChanged` event; `Ready` = splat available; `Error` = splat failed
- `InteractionMemory.currentWorldRoot` — splat world root `GameObject` for `SetActive()` toggling

The gaps:
1. No `Show3dWorld` / `ShowPanoWorld` in `VoiceIntentType`
2. No controller that tracks desired view mode, defers to load completion, or handles error fallback
3. `ThumbnailSkyboxController` cannot report whether it holds a valid texture, show a stored texture, suppress an auto-fade, or notify listeners when it becomes ready
4. `WorldActionDispatcher` has no handlers for the two new intents
5. `SpeechIntentSceneSetup` does not create or wire a view mode controller
6. `OpenAiSpeechIntentService` developer prompt does not mention the two new intents

## State Machine

```
Private state in ViewModeController:
  desiredMode:  ViewMode    (None | Pano | Splat3D)
  isSplatReady: bool        (set by OnWorldLoaded / OnWorldUnloaded)
  isPanoReady:  bool        (re-synced from thumbnailSkybox.IsReady at start of TryApply)

Subscriptions (wired in OnEnable, unwired in OnDisable):
  worldManager.OnWorldLoaded    → isSplatReady = true,  TryApply()
  worldManager.OnWorldUnloaded  → isSplatReady = false
  stateMachine.StateChanged     → OnStateChanged()
  thumbnailSkybox.OnReady       → TryApply()   // OnReady fires when new texture arrives

OnAwake: (no field init needed — isPanoReady is always re-synced in TryApply())

RequestPanoView()   → desiredMode = Pano,    TryApply()
RequestSplatView()  → desiredMode = Splat3D, TryApply()

OnStateChanged(state):
  // IMPORTANT: this handler MUST be synchronous (no coroutine, no NextFrame).
  // StateChanged fires synchronously inside TryTransitionTo, before the
  // coordinator reaches thumbnailSkybox.StartFadeOut(). SuppressNextFadeOut
  // must be set here — before that call — to reliably suppress the fade.
  if state == Ready:  TryApply()
  if state == Error:  TryApply()

TryApply():
  // Re-sync pano readiness each call so stale _isPanoReady can't cause a
  // ShowStored() call on a destroyed texture.
  _isPanoReady = thumbnailSkybox.IsReady

  if desiredMode == Pano:
    if _isPanoReady && !thumbnailSkybox.IsShowing:
      // IsShowing guard prevents re-entrant ShowStored() calls:
      // OnReady fires after Show() sets IsShowing=true, so this branch
      // is skipped on the redundant TryApply() call from OnReady.
      thumbnailSkybox.SuppressNextFadeOut = true
      hide splat: if interactionMemory.currentWorldRoot != null:
                      interactionMemory.currentWorldRoot.SetActive(false)
      thumbnailSkybox.ShowStored()
    else if _isPanoReady && thumbnailSkybox.IsShowing:
      // Already showing — pano is already the active view, nothing to do.
      pass
    // else: wait — pano not yet loaded; TryApply() called again via OnReady

  if desiredMode == Splat3D:
    if isSplatReady:
      if interactionMemory.currentWorldRoot != null:
          interactionMemory.currentWorldRoot.SetActive(true)
      thumbnailSkybox.StartFadeOut()
    else if stateMachine.CurrentState == Error:
      if _isPanoReady:
        desiredMode = Pano
        // No SuppressNextFadeOut here — no StartFadeOut() is pending in this path.
        if interactionMemory.currentWorldRoot != null:
            interactionMemory.currentWorldRoot.SetActive(false)
        thumbnailSkybox.ShowStored()
        onViewModeError.Invoke("3D not available, falling back to panorama")
      else:
        onViewModeError.Invoke("3D not available")
    // else: wait — splat not yet loaded; TryApply() called again via OnWorldLoaded
```

On world generation start: do NOT reset `desiredMode`. The user's preference persists until they explicitly say "3d" or "pano".

## New File: `ViewModeController.cs`

**Path:** `Assets/App/Command/SpeechIntent/Runtime/ViewModeController.cs`
**Namespace:** `SpeechIntent`

`ViewMode` is declared at **namespace scope** (not nested inside `ViewModeController`) within this file, so call sites reference it as `ViewMode.Pano` (not `ViewModeController.ViewMode.Pano`).

```csharp
public enum ViewMode { None, Pano, Splat3D }

public class ViewModeController : MonoBehaviour
{
    [Header("Scene References")]
    public ThumbnailSkyboxController thumbnailSkybox;
    public InteractionMemory         interactionMemory;
    public WorldLabsWorldManager     worldManager;
    public HolodeckStateMachine      stateMachine;

    [Header("Events")]
    public StringEvent onViewModeError;

    // Public read-only state
    public ViewMode DesiredMode { get; private set; }   // = ViewMode.None

    // Private tracked state
    private bool _isSplatReady;
    private bool _isPanoReady;  // re-synced from thumbnailSkybox.IsReady at start of each TryApply()

    // Unity lifecycle: Awake(), OnEnable(), OnDisable()
    // Public: RequestPanoView(), RequestSplatView()
    // Private: TryApply(), OnStateChanged(HolodeckState)
}
```

`StringEvent` is already defined in `VoiceIntentSchemas.cs`.

## Changes to Existing Files

### `Assets/App/Scripts/Direct/ThumbnailSkyboxController.cs`

Five additions. Requires `using System;` at the top of the file (for `Action`).

| Addition | Purpose |
|---|---|
| `public bool IsReady { get; private set; }` | Set `true` at end of `Show()`, `false` in `Awake()` and at the **beginning** of `FadeOutCoroutine` (before the fade animation begins) so any check after `StartFadeOut()` sees `false` immediately |
| `public bool IsShowing { get; private set; }` | Replaces the existing private `_isShowing` field — same set points (`true` at end of `Show()`, `false` when `FadeOutCoroutine` clears it), now exposed publicly. `ViewModeController` uses this to guard against re-entrant `ShowStored()` calls in `TryApply()`. |
| `public event Action OnReady` | Fired at end of `Show()`, after `IsReady = true` and `IsShowing = true`. Signals "a new texture has arrived." **NOT fired by `ShowStored()`** — re-displaying does not count as a new-ready event. |
| `public bool SuppressNextFadeOut { get; set; }` | Checked at the start of `StartFadeOut()`; if true, skips the fade and resets the flag to false |
| `public void ShowStored()` | See below |

**`ShowStored()` implementation detail:**
`Show(Texture2D tex)` destroys `_thumbnailTexture` before assigning the new one. Calling `Show(_thumbnailTexture)` would destroy the texture before using it. To avoid this, `ShowStored()` must save the reference, null the field, then call `Show()`. `Show()` will fire `OnReady` as usual, which causes `TryApply()` to run. At that point `IsShowing` is already `true` (set by `Show()`), so `TryApply()` hits the "already showing — nothing to do" branch and returns immediately. This is the intended behavior — `ShowStored()` is safe precisely because the `IsShowing` guard in `TryApply()` absorbs the re-entrant call.

```csharp
public void ShowStored()
{
    if (_thumbnailTexture == null)
    {
        Debug.LogWarning("[ThumbnailSkyboxController] ShowStored: no stored texture.");
        return;
    }
    Texture2D stored = _thumbnailTexture;
    _thumbnailTexture = null;   // prevent Show() from destroying it
    Show(stored);
}
```

**Texture lifetime note:** `FadeOutCoroutine` destroys `_thumbnailTexture` at completion. `ShowStored()` is only safe if the fade was suppressed (texture still alive). `SuppressNextFadeOut` is the mechanism that keeps it alive. The `_isPanoReady` re-sync in `TryApply()` from `thumbnailSkybox.IsReady` handles the case where a previous fade completed and destroyed the texture before the user says "pano" again — `IsReady` will be false, so `_isPanoReady` will be false, and `TryApply()` will wait for a new `OnReady` event.

### `Assets/App/Command/SpeechIntent/Runtime/VoiceIntentSchemas.cs`

Add two values to `VoiceIntentType`:

```csharp
Show3dWorld  = 11,
ShowPanoWorld = 12,
```

### `Assets/App/Command/SpeechIntent/Runtime/OpenAiSpeechIntentService.cs`

**Schema:** Add `"Show3dWorld"` and `"ShowPanoWorld"` to the JSON `intent` enum in `BuildIntentSchema()`.

**Prompt:** Add two lines in `BuildDeveloperInstructions()` after the `status` panel line:

```csharp
sb.AppendLine("For '3d', 'show 3d', or 'show splat', use intent=Show3dWorld.");
sb.AppendLine("For 'pano', 'panorama', or 'show panorama', use intent=ShowPanoWorld.");
```

### `Assets/App/Command/SpeechIntent/Runtime/WorldActionDispatcher.cs`

Add field:

```csharp
public ViewModeController viewModeController;
```

Add two case handlers in `Dispatch()`. The null-check pattern (warn in dispatch only, no `Awake()` assertion) is consistent with how other optional controllers are handled in this class:

```csharp
case VoiceIntentType.Show3dWorld:
    if (viewModeController != null)
        viewModeController.RequestSplatView();
    else
        Debug.LogWarning("[WorldActionDispatcher] viewModeController is null.");
    break;

case VoiceIntentType.ShowPanoWorld:
    if (viewModeController != null)
        viewModeController.RequestPanoView();
    else
        Debug.LogWarning("[WorldActionDispatcher] viewModeController is null.");
    break;
```

### `Assets/App/Editor/SpeechIntentSceneSetup.cs`

In `SetupSpeechIntent()`, `systems` is the `GameObject` returned by `EnsureRootObject("Systems")` — already defined at the top of the method.

- `GetOrAdd<ViewModeController>` — add after the last `GetOrAdd<StaticWorldController>` call in the component-creation block (section 2 of `SetupSpeechIntent()`)
- Wire `dispatcher.viewModeController = viewMode` — add after the last `dispatcher.*` assignment in the field-assignment block (section 5, where `Undo.RecordObjects` is called and `dispatcher.lightRig`, `dispatcher.uiPanels`, etc. are set). This mutates `dispatcher`, which is already in the `Undo.RecordObjects` batch — no need to add `viewMode` to the batch since `viewMode`'s own fields are set in the scene-ref block below
- Scene-ref wiring block — uses separate `Undo.RecordObject` for `viewMode`'s fields, consistent with the existing pattern for `lightRig`, `staticWorld`, and `uiPanels`:

```csharp
Undo.RecordObject(viewMode, "Wire ViewModeController");
viewMode.thumbnailSkybox   = systems.GetComponentInChildren<ThumbnailSkyboxController>(true);
viewMode.interactionMemory = memory;  // same InteractionMemory local already used in section 5
viewMode.worldManager      = systems.GetComponentInChildren<WorldLabsWorldManager>(true);
viewMode.stateMachine      = systems.GetComponentInChildren<HolodeckStateMachine>(true);

if (viewMode.thumbnailSkybox == null)
    Debug.LogWarning("[SpeechIntentSceneSetup] ThumbnailSkyboxController not found under Systems.");
if (viewMode.worldManager == null)
    Debug.LogWarning("[SpeechIntentSceneSetup] WorldLabsWorldManager not found under Systems.");
if (viewMode.stateMachine == null)
    Debug.LogWarning("[SpeechIntentSceneSetup] HolodeckStateMachine not found under Systems.");
```

## Out of Scope

- Resetting `desiredMode` on scene load or world unload (mode persists)
- Blending or cross-fade between pano and splat
- Adding "3d" / "pano" to the `status` panel key group
- `objectPlacement` or other dispatcher fields (separate sub-projects)

## Testing

After running `Holodeck > Setup SpeechIntent`:
1. `ViewModeController` present on `SpeechIntent` GameObject with all four fields assigned
2. `WorldActionDispatcher.viewModeController` assigned

In Play mode:
- Say "pano" before any texture has been passed to `ThumbnailSkyboxController` → when `Show()` is called (on pano load), the pano sphere shows (splat hidden)
- Say "3d" before a world loads → when world finishes loading, splat shows (pano fades out)
- Say "pano" after a world is loaded → pano sphere shows immediately
- Say "3d" after a world is loaded → splat shows, pano fades out
- Trigger a load failure with desired mode = 3d and pano available → error message fired, pano shown
- Say "3d" → "pano" → generate new world → pano sphere shows on load (mode persisted)
- Say "pano" (pano shows) → say "3d" (splat shows, pano fades out normally) — verifies `SuppressNextFadeOut` was cleared after suppression and does not block the subsequent fade
