# Sub-project 5: 3D/Pano View Mode Toggle — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `Show3dWorld` and `ShowPanoWorld` voice commands that toggle between the splat world and the panoramic sphere, with deferred fulfillment and error fallback.

**Architecture:** `ViewModeController` (new `SpeechIntent` component) tracks `DesiredMode` and reacts to world-load and state-machine events; `ThumbnailSkyboxController` gains `IsReady`, `IsShowing`, `OnReady`, `SuppressNextFadeOut`, and `ShowStored()` to support deferred display and suppressed auto-fade; two new `VoiceIntentType` values and one new dispatcher field complete the command pipeline.

**Tech Stack:** Unity 2022+, C# runtime scripting, `UnityEngine.Events`, `System.Action`.

---

## Files

| File | Change |
|---|---|
| `Assets/App/Scripts/Direct/ThumbnailSkyboxController.cs` | Add `using System;`, add `IsReady`/`IsShowing`/`OnReady`/`SuppressNextFadeOut`/`ShowStored()` |
| `Assets/App/Command/SpeechIntent/Runtime/VoiceIntentSchemas.cs` | Add `Show3dWorld = 11`, `ShowPanoWorld = 12` |
| `Assets/App/Command/SpeechIntent/Runtime/ViewModeController.cs` | New file — `ViewMode` enum + `ViewModeController` MonoBehaviour |
| `Assets/App/Command/SpeechIntent/Runtime/WorldActionDispatcher.cs` | Add `viewModeController` field + two case handlers |
| `Assets/App/Command/SpeechIntent/Runtime/OpenAiSpeechIntentService.cs` | Add two intent values to JSON schema enum; add two developer prompt lines |
| `Assets/App/Editor/SpeechIntentSceneSetup.cs` | `GetOrAdd<ViewModeController>`, dispatcher wire, scene-ref wiring block |

---

### Task 1: Extend ThumbnailSkyboxController

**Spec:** `docs/superpowers/specs/2026-03-31-subproject5-view-mode-toggle-design.md` — "Changes to Existing Files → ThumbnailSkyboxController.cs"

**File:** `Assets/App/Scripts/Direct/ThumbnailSkyboxController.cs`

**Context:** `ThumbnailSkyboxController` is in namespace `Holodeck.Direct`. It has a private `bool _isShowing` field (line 23) that is set `true` at lines 128 and 157 (end of sphere/skybox paths in `Show()`), and `false` at line 254 (end of `FadeOutCoroutine`). It also has a `StartFadeOut()` method (line 167) and an `Awake()` method (line 42). Currently uses `using System.Collections;` and `using UnityEngine;` — no `using System;`.

---

- [ ] **Step 1: Read the file**

Open and read `Assets/App/Scripts/Direct/ThumbnailSkyboxController.cs` in full. Confirm:
- Line 1: `using System.Collections;`
- Line 23: `private bool _isShowing;`
- Line 128: `_isShowing = true;` (sphere path end)
- Line 157: `_isShowing = true;` (skybox fallback end)
- Line 169: `if (!_isShowing)` (StartFadeOut guard)
- Line 203: start of `FadeOutCoroutine`
- Line 254: `_isShowing = false;`
- Line 296: `if (!_sphereMode && _isShowing)` in `OnDestroy`

- [ ] **Step 2: Add `using System;`**

After line 1 (`using System.Collections;`), add:

```csharp
using System;
```

- [ ] **Step 3: Add public properties and event after the field block**

After line 33 (`private Texture2D _thumbnailTexture;`), add:

```csharp
        // ── View mode integration ──────────────────────────────────────────
        public bool IsReady   { get; private set; }
        public bool IsShowing => _isShowing;   // read-only wrapper — _isShowing remains the backing field
        public bool SuppressNextFadeOut { get; set; }
        public event Action OnReady;
```

`IsShowing` wraps the existing private `_isShowing` field rather than replacing it. This avoids touching the many existing `_isShowing` usages in `Show()`, `StartFadeOut()`, `FadeOutCoroutine()`, and `OnDestroy()` — functionally equivalent to a replacement with far fewer changes.

- [ ] **Step 4: Set `IsReady = false` in `Awake()`**

In `Awake()` (line 42), add `IsReady = false;` as the first statement in the method body:

```csharp
        private void Awake()
        {
            IsReady = false;
            _spritesDefaultShader = Shader.Find("Sprites/Default");
        }
```

This is self-documenting and consistent with the spec — auto-properties default to `false` anyway, but the explicit assignment makes the intent clear.

- [ ] **Step 5: Add `IsReady = true` and `OnReady` invocation after each `_isShowing = true` set point**

In `Show()`, the sphere path ends at line 128 (`_isShowing = true;`). After that line (line 128) add:

```csharp
                IsReady = true;
                OnReady?.Invoke();
```

In `Show()`, the skybox fallback path ends at line 157 (`_isShowing = true;`). After that line add:

```csharp
                IsReady = true;
                OnReady?.Invoke();
```

- [ ] **Step 6: Add `SuppressNextFadeOut` check to `StartFadeOut()`**

In `StartFadeOut()`, after the `if (!_isShowing) return;` guard (line 169), add:

```csharp
            if (SuppressNextFadeOut)
            {
                SuppressNextFadeOut = false;
                return;
            }
```

- [ ] **Step 7: Set `IsReady = false` at the start of `FadeOutCoroutine`**

`FadeOutCoroutine` begins at line 203. The first line after the method signature is line 205 (`float elapsed = 0f;`). Add `IsReady = false;` before `float elapsed = 0f;`:

```csharp
        private IEnumerator FadeOutCoroutine()
        {
            IsReady = false;
            float elapsed = 0f;
```

- [ ] **Step 8: Add `ShowStored()` method**

Add after `StartFadeOut()` (before `ExpandCoroutine`):

```csharp
        /// <summary>
        /// Re-displays the stored panorama texture.
        /// Saves the reference, nulls the field to prevent Show() from destroying it, then calls Show().
        /// No-op with a warning if no texture is stored.
        /// </summary>
        public void ShowStored()
        {
            if (_thumbnailTexture == null)
            {
                Debug.LogWarning("[ThumbnailSkyboxController] ShowStored: no stored texture.", this);
                return;
            }
            Texture2D stored = _thumbnailTexture;
            _thumbnailTexture = null;   // prevent Show() from destroying it before use
            Show(stored);
        }
```

- [ ] **Step 9: Verify the file compiles**

Save. Confirm no compiler errors in the Unity Console.

Common issues:
- Missing `using System;` → `Action` type not found
- Duplicate `IsShowing` name conflict → `IsShowing` is a new property; `_isShowing` stays as the field

- [ ] **Step 10: Commit**

```bash
cd /Users/davidarendash/Documents/Projects/Unity/HeadsetHolodeck
git add Assets/App/Scripts/Direct/ThumbnailSkyboxController.cs
git commit -m "$(cat <<'EOF'
feat: add IsReady, IsShowing, OnReady, SuppressNextFadeOut, ShowStored to ThumbnailSkyboxController

Enables ViewModeController to defer pano display, suppress the
coordinator auto-fade, and re-display a stored texture on demand.

Co-Authored-By: Claude Sonnet 4.6 <noreply@anthropic.com>
EOF
)"
```

---

### Task 2: Add Show3dWorld and ShowPanoWorld to VoiceIntentSchemas

**Spec:** `docs/superpowers/specs/2026-03-31-subproject5-view-mode-toggle-design.md` — "VoiceIntentSchemas.cs"

**File:** `Assets/App/Command/SpeechIntent/Runtime/VoiceIntentSchemas.cs`

**Context:** `VoiceIntentType` enum ends at line 20 with `RotateTarget = 10`.

---

- [ ] **Step 1: Read the file**

Open `Assets/App/Command/SpeechIntent/Runtime/VoiceIntentSchemas.cs`. Confirm line 20: `RotateTarget = 10`.

- [ ] **Step 2: Add two new enum values**

After `RotateTarget = 10` (line 20), add:

```csharp
        Show3dWorld   = 11,
        ShowPanoWorld = 12,
```

The enum block becomes:
```csharp
    public enum VoiceIntentType
    {
        Unknown = 0,
        AskClarification = 1,
        GenerateWorld = 2,
        SwitchToStaticWorld = 3,
        ShowUi = 4,
        SetSunDirection = 5,
        SetLightingPreset = 6,
        PlaceObject = 7,
        MoveTarget = 8,
        ScaleTarget = 9,
        RotateTarget = 10,
        Show3dWorld   = 11,
        ShowPanoWorld = 12,
    }
```

Note: add a trailing comma after `RotateTarget = 10` when inserting after it.

- [ ] **Step 3: Verify**

Save. Confirm no compiler errors.

- [ ] **Step 4: Commit**

```bash
git add Assets/App/Command/SpeechIntent/Runtime/VoiceIntentSchemas.cs
git commit -m "$(cat <<'EOF'
feat: add Show3dWorld and ShowPanoWorld to VoiceIntentType enum

Co-Authored-By: Claude Sonnet 4.6 <noreply@anthropic.com>
EOF
)"
```

---

### Task 3: Create ViewModeController

**Spec:** `docs/superpowers/specs/2026-03-31-subproject5-view-mode-toggle-design.md` — "New File: ViewModeController.cs" and "State Machine"

**File:** `Assets/App/Command/SpeechIntent/Runtime/ViewModeController.cs` (new)

**Context:**
- `HolodeckStateMachine` is in namespace `Holodeck.State`. Its `StateChanged` event signature is `event Action<HolodeckState, HolodeckState>` (previousState, newState). `CurrentState` property returns `HolodeckState`.
- `WorldLabsWorldManager` is in namespace `WorldLabs.Runtime`. `OnWorldLoaded` is `event Action<string, GaussianSplatRenderer>` (worldId, renderer). `OnWorldUnloaded` is `event Action<string>` (worldId). `GaussianSplatRenderer` is in `WorldLabs.Runtime` or a sub-namespace — use `using WorldLabs.Runtime;`.
- `ThumbnailSkyboxController` is in namespace `Holodeck.Direct`.
- `InteractionMemory`, `StringEvent` are in namespace `SpeechIntent` (same as this new file).
- `HolodeckState.Ready = 4`, `HolodeckState.Error = 5`.
- `StateChanged` fires synchronously inside `TryTransitionTo`, **before** the coordinator calls `thumbnailSkybox.StartFadeOut()`. The handler MUST be synchronous — no coroutine wrapping, no `NextFrame` deferral.

---

- [ ] **Step 1: Create the file**

Create `Assets/App/Command/SpeechIntent/Runtime/ViewModeController.cs`:

```csharp
using System;
using Holodeck.Direct;
using Holodeck.State;
using UnityEngine;
using WorldLabs.Runtime;

namespace SpeechIntent
{
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

        public ViewMode DesiredMode { get; private set; }

        private bool _isSplatReady;
        private bool _isPanoReady;  // re-synced from thumbnailSkybox.IsReady at start of every TryApply()

        private void OnEnable()
        {
            if (worldManager != null)
            {
                worldManager.OnWorldLoaded   += OnWorldLoaded;
                worldManager.OnWorldUnloaded += OnWorldUnloaded;
            }
            if (stateMachine != null)
                stateMachine.StateChanged += OnStateChanged;
            if (thumbnailSkybox != null)
                thumbnailSkybox.OnReady += TryApply;
        }

        private void OnDisable()
        {
            if (worldManager != null)
            {
                worldManager.OnWorldLoaded   -= OnWorldLoaded;
                worldManager.OnWorldUnloaded -= OnWorldUnloaded;
            }
            if (stateMachine != null)
                stateMachine.StateChanged -= OnStateChanged;
            if (thumbnailSkybox != null)
                thumbnailSkybox.OnReady -= TryApply;
        }

        public void RequestPanoView()
        {
            DesiredMode = ViewMode.Pano;
            TryApply();
        }

        public void RequestSplatView()
        {
            DesiredMode = ViewMode.Splat3D;
            TryApply();
        }

        // ── Event handlers ────────────────────────────────────────────────

        private void OnWorldLoaded(string worldId, GaussianSplatRenderer renderer)
        {
            _isSplatReady = true;
            TryApply();
        }

        private void OnWorldUnloaded(string worldId)
        {
            _isSplatReady = false;
        }

        // IMPORTANT: Must remain synchronous. StateChanged fires inside TryTransitionTo,
        // before the coordinator calls thumbnailSkybox.StartFadeOut(). SuppressNextFadeOut
        // must be set here to reliably suppress that call.
        private void OnStateChanged(HolodeckState previousState, HolodeckState newState)
        {
            if (newState == HolodeckState.Ready || newState == HolodeckState.Error)
                TryApply();
        }

        // ── Core logic ────────────────────────────────────────────────────

        private void TryApply()
        {
            if (thumbnailSkybox == null) return;

            // Re-sync pano readiness every call — handles the case where a fade completed
            // and destroyed _thumbnailTexture after we last cached isPanoReady = true.
            _isPanoReady = thumbnailSkybox.IsReady;

            if (DesiredMode == ViewMode.Pano)
            {
                if (_isPanoReady && !thumbnailSkybox.IsShowing)
                {
                    // Set SuppressNextFadeOut BEFORE ShowStored(), because ShowStored() → Show()
                    // fires OnReady → TryApply() re-entrantly. The IsShowing guard handles that
                    // re-entrant call, but we also want to suppress any upcoming StartFadeOut()
                    // from the coordinator (which fires after StateChanged returns).
                    thumbnailSkybox.SuppressNextFadeOut = true;
                    HideCurrentWorldRoot();
                    thumbnailSkybox.ShowStored();
                }
                else if (_isPanoReady && thumbnailSkybox.IsShowing)
                {
                    // Pano already showing — ensure splat is hidden but no display work needed.
                    HideCurrentWorldRoot();
                }
                // else: pano not yet loaded — wait for OnReady
            }
            else if (DesiredMode == ViewMode.Splat3D)
            {
                if (_isSplatReady)
                {
                    ShowCurrentWorldRoot();
                    thumbnailSkybox.StartFadeOut();
                }
                else if (stateMachine != null && stateMachine.CurrentState == HolodeckState.Error)
                {
                    // Splat failed. Fall back to pano if available.
                    if (_isPanoReady)
                    {
                        DesiredMode = ViewMode.Pano;
                        // No SuppressNextFadeOut here — no StartFadeOut() is pending in this error path.
                        HideCurrentWorldRoot();
                        thumbnailSkybox.ShowStored();
                        onViewModeError?.Invoke("3D not available, falling back to panorama");
                    }
                    else
                    {
                        onViewModeError?.Invoke("3D not available");
                    }
                }
                // else: splat not yet loaded — wait for OnWorldLoaded / OnStateChanged(Ready)
            }
        }

        private void HideCurrentWorldRoot()
        {
            if (interactionMemory != null && interactionMemory.currentWorldRoot != null)
                interactionMemory.currentWorldRoot.SetActive(false);
        }

        private void ShowCurrentWorldRoot()
        {
            if (interactionMemory != null && interactionMemory.currentWorldRoot != null)
                interactionMemory.currentWorldRoot.SetActive(true);
        }
    }
}
```

- [ ] **Step 2: Verify the file compiles**

Save. Confirm no compiler errors in the Unity Console.

Common issues:
- `GaussianSplatRenderer` not found → add `using WorldLabs.Runtime;` (already present) or use the fully qualified name
- `HolodeckState` not found → check `using Holodeck.State;` is present
- `StringEvent` not found → ensure it is in the `SpeechIntent` namespace (it is — defined in `VoiceIntentSchemas.cs`)

- [ ] **Step 3: Commit**

```bash
git add Assets/App/Command/SpeechIntent/Runtime/ViewModeController.cs
git commit -m "$(cat <<'EOF'
feat: add ViewModeController for 3D/pano view mode toggle

Tracks desired view mode (None/Pano/Splat3D), defers display to
world-load and state-machine events, and falls back to pano on
splat failure.

Co-Authored-By: Claude Sonnet 4.6 <noreply@anthropic.com>
EOF
)"
```

---

### Task 4: Update WorldActionDispatcher

**Spec:** `docs/superpowers/specs/2026-03-31-subproject5-view-mode-toggle-design.md` — "WorldActionDispatcher.cs"

**File:** `Assets/App/Command/SpeechIntent/Runtime/WorldActionDispatcher.cs`

**Context:** The `[Header("Scene Controllers")]` block currently ends at line 14 (`public InteractionMemory interactionMemory;`). The `switch` statement in `Execute()` ends at line 76 (default case) with `onUnhandledAction?.Invoke(command.rawSummary());`.

---

- [ ] **Step 1: Read the file**

Open `Assets/App/Command/SpeechIntent/Runtime/WorldActionDispatcher.cs`. Confirm line 14: `public InteractionMemory interactionMemory;` and that the last named case before `default:` is `case VoiceIntentType.RotateTarget:`.

- [ ] **Step 2: Add `viewModeController` field**

After line 14 (`public InteractionMemory interactionMemory;`), add:

```csharp
        public ViewModeController viewModeController;
```

- [ ] **Step 3: Add two case handlers**

In the `switch` statement in `Execute()`, after the `case VoiceIntentType.RotateTarget:` block (before `default:`), add:

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

- [ ] **Step 4: Verify**

Save. Confirm no compiler errors.

- [ ] **Step 5: Commit**

```bash
git add Assets/App/Command/SpeechIntent/Runtime/WorldActionDispatcher.cs
git commit -m "$(cat <<'EOF'
feat: add Show3dWorld and ShowPanoWorld handlers to WorldActionDispatcher

Routes voice commands to ViewModeController.RequestSplatView() and
RequestPanoView().

Co-Authored-By: Claude Sonnet 4.6 <noreply@anthropic.com>
EOF
)"
```

---

### Task 5: Update OpenAiSpeechIntentService

**Spec:** `docs/superpowers/specs/2026-03-31-subproject5-view-mode-toggle-design.md` — "OpenAiSpeechIntentService.cs"

**File:** `Assets/App/Command/SpeechIntent/Runtime/OpenAiSpeechIntentService.cs`

**Context:**
- The JSON schema `intent` enum ends at line 425 with `""RotateTarget""`. The closing `]` is on line 426, `}` on line 427.
- `BuildDeveloperInstructions()` has the `status` panel line at line 346. The next line (347) is the `SetSunDirection` instruction.

---

- [ ] **Step 1: Read the relevant sections**

Open `Assets/App/Command/SpeechIntent/Runtime/OpenAiSpeechIntentService.cs`.

Confirm:
- Around line 425: `""RotateTarget""` as the last item in the JSON enum array
- Line 346: `sb.AppendLine("For 'show status', 'show loading', or 'show progress', use intent=ShowUi and ui_panel='status'.");`

- [ ] **Step 2: Add two values to the JSON intent enum**

Find `""RotateTarget""` (the last item in the enum array). After it, add:

```
        ""Show3dWorld"",
        ""ShowPanoWorld""
```

The enum section becomes:
```
      ""enum"": [
        ""Unknown"",
        ""AskClarification"",
        ""GenerateWorld"",
        ""SwitchToStaticWorld"",
        ""ShowUi"",
        ""SetSunDirection"",
        ""SetLightingPreset"",
        ""PlaceObject"",
        ""MoveTarget"",
        ""ScaleTarget"",
        ""RotateTarget"",
        ""Show3dWorld"",
        ""ShowPanoWorld""
      ]
```

Note: Add a trailing comma after `""RotateTarget""` when inserting after it.

- [ ] **Step 3: Add two developer prompt lines**

Find the `status` panel line in `BuildDeveloperInstructions()`:
```csharp
sb.AppendLine("For 'show status', 'show loading', or 'show progress', use intent=ShowUi and ui_panel='status'.");
```

After it, add:

```csharp
            sb.AppendLine("For '3d', 'show 3d', or 'show splat', use intent=Show3dWorld.");
            sb.AppendLine("For 'pano', 'panorama', or 'show panorama', use intent=ShowPanoWorld.");
```

- [ ] **Step 4: Verify**

Save. Confirm no compiler errors. This file has a raw string literal for the schema — confirm the JSON is valid (no missing commas or brackets).

- [ ] **Step 5: Commit**

```bash
git add Assets/App/Command/SpeechIntent/Runtime/OpenAiSpeechIntentService.cs
git commit -m "$(cat <<'EOF'
feat: add Show3dWorld and ShowPanoWorld to OpenAI schema and developer prompt

Teaches GPT-4o to use the new intents for '3d'/'pano' utterances.

Co-Authored-By: Claude Sonnet 4.6 <noreply@anthropic.com>
EOF
)"
```

---

### Task 6: Update SpeechIntentSceneSetup

**Spec:** `docs/superpowers/specs/2026-03-31-subproject5-view-mode-toggle-design.md` — "SpeechIntentSceneSetup.cs"

**File:** `Assets/App/Editor/SpeechIntentSceneSetup.cs`

**Context:** The current file has:
- Line 55: `StaticWorldController staticWorld = GetOrAdd<StaticWorldController>(speechRoot);` — end of `GetOrAdd` block
- Line 75: `new Object[] { service, dispatcher, memory, router, trigger, semantic, entityResolver, targetTransform, lightRig, uiPanels, staticWorld },` — `Undo.RecordObjects` batch (11 objects)
- Line 89: `dispatcher.staticWorldController = staticWorld;` — last `dispatcher.*` assignment in section 5
- Lines 136–160: Scene reference wiring block (lighting, static world, UI panels)

`viewMode` does NOT need to be added to the `Undo.RecordObjects` batch — none of `viewMode`'s own fields are set in section 5. Its fields are set in a separate scene-ref block with an individual `Undo.RecordObject` call, consistent with the existing pattern for `lightRig`, `staticWorld`, and `uiPanels`.

The `systems` variable is the `GameObject` from `EnsureRootObject("Systems")` — already defined at the top of `SetupSpeechIntent()`.

The `memory` variable is the `InteractionMemory` local from the `GetOrAdd` block — already defined.

---

- [ ] **Step 1: Read the file**

Open `Assets/App/Editor/SpeechIntentSceneSetup.cs` in full. Confirm:
- Line 55 ends the `GetOrAdd` block with `StaticWorldController`
- Line 89 is the last `dispatcher.*` assignment before `router.*` wires
- Lines 136–160 contain the scene-ref wiring block

- [ ] **Step 2: Add `GetOrAdd<ViewModeController>`**

After line 55 (`StaticWorldController staticWorld = GetOrAdd<StaticWorldController>(speechRoot);`), add:

```csharp
            ViewModeController             viewMode           = GetOrAdd<ViewModeController>(speechRoot);
```

- [ ] **Step 3: Add dispatcher wire assignment**

After line 89 (`dispatcher.staticWorldController = staticWorld;`), add:

```csharp
            dispatcher.viewModeController = viewMode;
```

- [ ] **Step 4: Add scene-ref wiring block**

After line 160 (`WireUiPanel(uiPanels, "status", "UI/WorldLabs_Status");`) and before the `// ── 6. Wire cross-system UnityEvents` comment, add:

```csharp
            Undo.RecordObject(viewMode, "Wire ViewModeController");
            viewMode.thumbnailSkybox   = systems.GetComponentInChildren<ThumbnailSkyboxController>(true);
            viewMode.interactionMemory = memory;
            viewMode.worldManager      = systems.GetComponentInChildren<WorldLabsWorldManager>(true);
            viewMode.stateMachine      = systems.GetComponentInChildren<HolodeckStateMachine>(true);

            if (viewMode.thumbnailSkybox == null)
                Debug.LogWarning("[SpeechIntentSceneSetup] ThumbnailSkyboxController not found under Systems. Assign viewMode.thumbnailSkybox manually.");
            if (viewMode.worldManager == null)
                Debug.LogWarning("[SpeechIntentSceneSetup] WorldLabsWorldManager not found under Systems. Assign viewMode.worldManager manually.");
            if (viewMode.stateMachine == null)
                Debug.LogWarning("[SpeechIntentSceneSetup] HolodeckStateMachine not found under Systems. Assign viewMode.stateMachine manually.");

```

- [ ] **Step 5: Add missing `using` directives**

The editor script already imports `using SpeechIntent;` (line 10) which covers `ViewModeController`. The following three directives are NOT currently in the file and must be added:

```csharp
using Holodeck.Direct;        // ThumbnailSkyboxController
using WorldLabs.Runtime;      // WorldLabsWorldManager
using Holodeck.State;         // HolodeckStateMachine
```

Add them after the existing `using` block at the top of the file.

- [ ] **Step 6: Verify the file compiles**

Save. Confirm no compiler errors in the Unity Console.

- [ ] **Step 7: Run Setup SpeechIntent and verify**

Menu: `Holodeck > Setup SpeechIntent`

Expected Console: `[SpeechIntentSceneSetup] Done. Set your OpenAI API key...`

Select the `SpeechIntent` GameObject and confirm in the Inspector:
1. `ViewModeController` component present
2. `thumbnailSkybox` field → `ThumbnailSkyboxController` component
3. `interactionMemory` field → `InteractionMemory` on the same GameObject
4. `worldManager` field → `WorldLabsWorldManager` component
5. `stateMachine` field → `HolodeckStateMachine` component
6. `WorldActionDispatcher.viewModeController` → `ViewModeController` on the same GameObject

If any scene-ref fields are null, check the console for the appropriate warning and verify the component exists under `Systems` in the scene hierarchy.

- [ ] **Step 8: Commit**

```bash
git add Assets/App/Editor/SpeechIntentSceneSetup.cs
git commit -m "$(cat <<'EOF'
feat: wire ViewModeController in SpeechIntentSceneSetup

Auto-finds ThumbnailSkyboxController, WorldLabsWorldManager, and
HolodeckStateMachine under Systems; wires InteractionMemory and
connects dispatcher.viewModeController.

Co-Authored-By: Claude Sonnet 4.6 <noreply@anthropic.com>
EOF
)"
```

---

### Task 7: Verify end-to-end in Play mode and commit scene

**Spec:** `docs/superpowers/specs/2026-03-31-subproject5-view-mode-toggle-design.md` — "Testing"

---

- [ ] **Step 1: Save the scene in Unity**

`File > Save` (or Cmd+S). The scene was marked dirty by `Setup SpeechIntent`.

- [ ] **Step 2: Verify deferred pano**

Say "pano" before a world loads. Generate a world. Expected: when the world finishes loading, the panorama sphere shows and the splat world root is hidden.

- [ ] **Step 3: Verify deferred 3d**

Say "3d" before a world loads (no world yet). Generate a world. Expected: when the world finishes loading, the splat appears and the pano fades out.

- [ ] **Step 4: Verify immediate pano after world loaded**

Generate a world and let it load normally (splat showing). Then say "pano". Expected: pano sphere shows immediately, splat root becomes hidden.

- [ ] **Step 5: Verify 3d command when already in pano**

With pano showing, say "3d". Expected: splat root becomes active, pano sphere fades out.

- [ ] **Step 6: Verify error-fallback path**

With `desiredMode == Splat3D` and a pano texture available, trigger a world load failure (e.g., use an invalid world ID or disconnect from network). Expected: `onViewModeError` fires ("3D not available, falling back to panorama"), pano sphere shows, splat root hidden. The `onViewModeError` event can be verified by wiring a Debug.Log listener in the Inspector or checking the Console for the message if any listener is already wired.

- [ ] **Step 7: Verify persistence across generations**

Say "pano". Generate a new world. Expected: pano sphere shows on load (mode was persisted).

- [ ] **Step 8: Verify SuppressNextFadeOut is consumed**

Say "pano" (pano shows). Say "3d" (splat shows, pano fades out normally). This verifies `SuppressNextFadeOut` was reset to `false` and does not suppress the fade triggered by "3d".

- [ ] **Step 9: Commit scene**

```bash
git add Assets/Scenes/
git commit -m "$(cat <<'EOF'
scene: wire view mode toggle controllers via SpeechIntentSceneSetup

Co-Authored-By: Claude Sonnet 4.6 <noreply@anthropic.com>
EOF
)"
```
