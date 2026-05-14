# Speech Intent Core Integration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Wire the `SpeechIntent` system to the existing project so voice-interpreted world-generation commands reach `VoiceToWorldLabsPluginCoordinator.TriggerWorldGeneration` and `InteractionMemory` automatically tracks the loaded world.

**Architecture:** `WorldActionDispatcher.onGenerateWorldPrompt` (UnityEvent<string>) is wired in the Inspector to `VoiceToWorldLabsPluginCoordinator.TriggerWorldGeneration`, which replaces the old wake-word/capture/transcription flow with a direct prompt-to-generate coroutine. `InteractionMemory` subscribes to `WorldLabsWorldManager.OnWorldLoaded` / `OnWorldUnloaded` C# events to keep `currentWorldRoot` in sync automatically.

**Tech Stack:** Unity C#, UnityEngine.Events, GaussianSplatting.Runtime, WorldLabs.Runtime

**Spec:** `docs/superpowers/specs/2026-03-30-speech-intent-core-integration-design.md`

---

## File Map

| Action | File | What changes |
|--------|------|-------------|
| **Modify** | `Assets/App/Command/SpeechIntent/Runtime/OpenAiSpeechIntentConfig.cs` | Fix two wrong model name defaults |
| **Modify** | `Assets/App/Command/SpeechIntent/Runtime/VoiceCommandRouter.cs` | Add `_isRecording` guard to `BeginRecording` / `EndRecordingAndProcess` |
| **Modify** | `Assets/App/Command/SpeechIntent/Runtime/InteractionMemory.cs` | Add `worldManager` field + `OnEnable`/`OnDisable` subscription to world lifecycle events |
| **Modify** | `Assets/App/Scripts/Direct/VoiceToWorldLabsPluginCoordinator.cs` | Remove wake/capture/transcription layer; add `TriggerWorldGeneration` + `RunGenerationFlow` |
| **Inspector only** | `Assets/App/Command/SpeechIntent/Runtime/WorldActionDispatcher.cs` | Wire `onGenerateWorldPrompt` → `TriggerWorldGeneration` (Dynamic String) and `onSwitchToStaticWorld` → `RestoreDefaultWorld` (Static) |

---

## Task 1: Fix model names in `OpenAiSpeechIntentConfig`

**Files:**
- Modify: `Assets/App/Command/SpeechIntent/Runtime/OpenAiSpeechIntentConfig.cs:23–26`

### Context

The ScriptableObject has two wrong model name defaults that will cause runtime failures when the speech service is called. `gpt-4o-mini-transcribe` is not a valid OpenAI transcription model — the correct model is `whisper-1`. `gpt-5.4` was unverifiable at spec time; use `gpt-4o` as a safe default (update in the Inspector once the correct model is confirmed on the OpenAI dashboard).

Current lines 23–26:
```csharp
public string transcriptionModel = "gpt-4o-mini-transcribe";

[Tooltip("Structured intent model.")]
public string intentModel = "gpt-5.4";
```

- [ ] **Step 1: Update model defaults**

Replace those two field declarations with:
```csharp
public string transcriptionModel = "whisper-1";

[Tooltip("Structured intent model. Verify the latest available model on the OpenAI dashboard.")]
public string intentModel = "gpt-4o";
```

- [ ] **Step 2: Verify compilation**

Switch to Unity Editor. Check the Console for errors.

Expected: no errors. If `OpenAiSpeechIntentConfig` shows a compile error, confirm you only changed the string literals.

- [ ] **Step 3: Update any saved ScriptableObject assets**

If a `OpenAiSpeechIntentConfig` asset file already exists in the project (check `Assets/` for `.asset` files with this type), open it in the Inspector and confirm the model name fields show the new defaults. Assets that were saved before this change retain the old strings and must be updated manually in the Inspector.

- [ ] **Step 4: Commit**

```bash
git add Assets/App/Command/SpeechIntent/Runtime/OpenAiSpeechIntentConfig.cs
git commit -m "fix: correct OpenAI model names in OpenAiSpeechIntentConfig"
```

---

## Task 2: Add recording guard to `VoiceCommandRouter`

**Files:**
- Modify: `Assets/App/Command/SpeechIntent/Runtime/VoiceCommandRouter.cs:26–83`

### Context

`VoiceCommandRouter.BeginRecording()` and `EndRecordingAndProcess()` are public. A future wake-word component will call these from outside the class. Without a guard, a second `BeginRecording()` call starts a new microphone session on top of an existing one. The existing `_wasRecordingFromKeyboard` field pairs key-down/key-up for the keyboard path — do not remove it.

Current `BeginRecording` (lines 48–57):
```csharp
public void BeginRecording()
{
    if (recorder == null)
    {
        EmitError("Recorder reference is missing.");
        return;
    }

    recorder.BeginRecording();
}
```

Current `EndRecordingAndProcess` (lines 59–83):
```csharp
public void EndRecordingAndProcess()
{
    if (recorder == null)
    {
        EmitError("Recorder reference is missing.");
        return;
    }

    byte[] wavBytes = recorder.EndRecordingToWavBytes();
    ...
```

- [ ] **Step 1: Add `_isRecording` private field**

Insert after the existing `_wasRecordingFromKeyboard` field (line 26):
```csharp
private bool _wasRecordingFromKeyboard;
private bool _isRecording;
```

- [ ] **Step 2: Update `BeginRecording()` with guard**

Replace the entire `BeginRecording()` method with:
```csharp
public void BeginRecording()
{
    if (_isRecording) return;
    _isRecording = true;
    if (recorder == null)
    {
        _isRecording = false;
        EmitError("Recorder reference is missing.");
        return;
    }
    recorder.BeginRecording();
}
```

- [ ] **Step 3: Update `EndRecordingAndProcess()` with guard**

Replace the opening two lines of `EndRecordingAndProcess()`:
```csharp
public void EndRecordingAndProcess()
{
    if (recorder == null)
```

With:
```csharp
public void EndRecordingAndProcess()
{
    if (!_isRecording) return;
    _isRecording = false;
    if (recorder == null)
```

Leave the rest of the method body unchanged (`byte[] wavBytes = recorder.EndRecordingToWavBytes();` through `StartCoroutine(...)`).

- [ ] **Step 4: Verify compilation**

Switch to Unity Editor. Check the Console for errors.

Expected: no errors. If `_isRecording` isn't found, confirm it was added at class scope (not inside a method).

- [ ] **Step 5: Verify keyboard recording still works in Play mode**

Press Play. Hold **V** (or your configured `pushToTalkKey`) and speak a phrase. Release. Verify the transcript fires (check the Console or `onTranscriptReady` event). Press V again and confirm a new recording starts correctly.

- [ ] **Step 6: Commit**

```bash
git add Assets/App/Command/SpeechIntent/Runtime/VoiceCommandRouter.cs
git commit -m "fix: add _isRecording guard to VoiceCommandRouter to prevent double-trigger"
```

---

## Task 3: Subscribe `InteractionMemory` to world lifecycle events

**Files:**
- Modify: `Assets/App/Command/SpeechIntent/Runtime/InteractionMemory.cs:1–4` (add using directives)
- Modify: `Assets/App/Command/SpeechIntent/Runtime/InteractionMemory.cs:5–14` (add field + lifecycle methods)

### Context

`InteractionMemory` currently has no automatic mechanism to track which world is loaded — `currentWorldRoot` must be set by calling `RegisterCurrentWorld()` manually. After this task, it subscribes to `WorldLabsWorldManager.OnWorldLoaded` / `OnWorldUnloaded` so any world load (voice path, browser path, or direct API call) automatically updates the memory.

`OnWorldLoaded` signature: `event Action<string, GaussianSplatRenderer> OnWorldLoaded`
`OnWorldUnloaded` signature: `event Action<string> OnWorldUnloaded`

`GaussianSplatRenderer` is in `GaussianSplatting.Runtime`. `WorldLabsWorldManager` is in `WorldLabs.Runtime`.

Current file header:
```csharp
using UnityEngine;

namespace SpeechIntent
{
    public class InteractionMemory : MonoBehaviour
    {
        [Header("Tracked references")]
        public GameObject currentWorldRoot;
```

- [ ] **Step 1: Add using directives**

Replace the using block at the top of `InteractionMemory.cs`:
```csharp
using UnityEngine;
```
With:
```csharp
using GaussianSplatting.Runtime;
using UnityEngine;
using WorldLabs.Runtime;
```

- [ ] **Step 2: Add the `worldManager` serialized field**

Insert a new header group immediately before the existing `[Header("Tracked references")]` block:
```csharp
        [Header("World Manager")]
        [SerializeField] private WorldLabsWorldManager worldManager;
```

The result at the top of the class body should read:
```csharp
        [Header("World Manager")]
        [SerializeField] private WorldLabsWorldManager worldManager;

        [Header("Tracked references")]
        public GameObject currentWorldRoot;
```

- [ ] **Step 3: Add `OnEnable`, `OnDisable`, and handler methods**

Add the following four methods to `InteractionMemory`, after the existing `RegisterSelection` method and before `GetLastCreatedOrInteracted`:

```csharp
        private void OnEnable()
        {
            if (worldManager != null)
            {
                worldManager.OnWorldLoaded   += HandleWorldLoaded;
                worldManager.OnWorldUnloaded += HandleWorldUnloaded;
            }
        }

        private void OnDisable()
        {
            if (worldManager != null)
            {
                worldManager.OnWorldLoaded   -= HandleWorldLoaded;
                worldManager.OnWorldUnloaded -= HandleWorldUnloaded;
            }
        }

        private void HandleWorldLoaded(string worldId, GaussianSplatRenderer renderer)
        {
            RegisterCurrentWorld(renderer != null ? renderer.gameObject : null);
        }

        private void HandleWorldUnloaded(string worldId)
        {
            if (currentWorldRoot != null)
                currentWorldRoot = null;
        }
```

- [ ] **Step 4: Verify compilation**

Switch to Unity Editor. Check the Console for errors.

Expected: no errors. Common issues:
- `GaussianSplatRenderer not found` → confirm `using GaussianSplatting.Runtime;` was added
- `WorldLabsWorldManager not found` → confirm `using WorldLabs.Runtime;` was added

- [ ] **Step 5: Wire `worldManager` in the Inspector**

Select the `InteractionMemory` component in the scene hierarchy. In the `World Manager` header, drag the `WorldLabsWorldManager` component into the `worldManager` slot.

- [ ] **Step 6: Verify auto-update in Play mode**

Press Play. Load a world via the browser (click a world card) or let one load via voice. Open the `InteractionMemory` component in the Inspector (while in Play mode) and confirm `Current World Root` is automatically populated with the loaded world's `GameObject`.

- [ ] **Step 7: Commit**

```bash
git add Assets/App/Command/SpeechIntent/Runtime/InteractionMemory.cs
git commit -m "feat: subscribe InteractionMemory to WorldLabsWorldManager world lifecycle events"
```

---

## Task 4: Refactor `VoiceToWorldLabsPluginCoordinator` — remove wake layer, add `TriggerWorldGeneration`

**Files:**
- Modify: `Assets/App/Scripts/Direct/VoiceToWorldLabsPluginCoordinator.cs` (major refactor)

### Context

The coordinator currently wires wake-trigger → voice capture → OpenAI transcription → world generation. After this task, the coordinator only handles world generation — it receives an already-interpreted text prompt and generates the world. The wake/capture/transcription pipeline is removed entirely (those belong to `VoiceCommandRouter` + `OpenAiSpeechIntentService`).

**What leaves the file:**
- `using Holodeck.Input;` and `using Holodeck.Voice;` (those types are no longer used)
- Fields: `wakeTriggerBehaviour`, `voiceCaptureManager`, `transcriptionClient` (serialized), `_wakeTrigger` (private), `lastTranscript` (debug field)
- Property: `LastTranscript`
- Methods: `HandleWakeTriggered`, `BeginListening`, `EndListeningAndGenerate`, `RunVoiceToWorldFlow`, `HandleCaptureFailed`

**What arrives:**
- `public void TriggerWorldGeneration(string prompt)` — the new external entry point
- `private IEnumerator RunGenerationFlow(string prompt)` — identical to `RunVoiceToWorldFlow` minus transcription

This is a large change on a single file. Take it step by step and verify compilation after each step.

### Removal steps

- [ ] **Step 1: Remove three serialized fields + `_wakeTrigger` + `lastTranscript` + `LastTranscript`**

Remove from the `[Header("Dependencies")]` block:
```csharp
[SerializeField] private MonoBehaviour wakeTriggerBehaviour;
[SerializeField] private VoiceCaptureManager voiceCaptureManager;
[SerializeField] private OpenAITranscriptionClient transcriptionClient;
```

Remove from the private fields:
```csharp
private IWakeTrigger _wakeTrigger;
```

Remove from the `[Header("Runtime Debug")]` block:
```csharp
[SerializeField, TextArea] private string lastTranscript = string.Empty;
```

Remove the public property:
```csharp
public string LastTranscript => lastTranscript;
```

- [ ] **Step 2: Remove the two unused `using` directives**

Remove from the top of the file:
```csharp
using Holodeck.Input;
using Holodeck.Voice;
```

After removal, the using block should be:
```csharp
using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Holodeck.State;
using WorldLabs.API;
using WorldLabs.Runtime;
using WorldLabs.Runtime.Tools;
```

- [ ] **Step 3: Update `Awake()` — remove three null checks**

In `Awake()`, remove the three null-check blocks for `_wakeTrigger`, `voiceCaptureManager`, and `transcriptionClient`. Keep the null checks for `stateMachine` and `worldManager`.

Before (excerpt showing what to remove):
```csharp
_wakeTrigger = wakeTriggerBehaviour as IWakeTrigger;

if (_wakeTrigger == null)
{
    Debug.LogError($"{nameof(VoiceToWorldLabsPluginCoordinator)} requires a valid IWakeTrigger.", this);
}

if (stateMachine == null) { ... } // KEEP

if (voiceCaptureManager == null)
{
    Debug.LogError($"{nameof(VoiceToWorldLabsPluginCoordinator)} is missing a VoiceCaptureManager.", this);
}

if (transcriptionClient == null)
{
    Debug.LogError($"{nameof(VoiceToWorldLabsPluginCoordinator)} is missing an OpenAITranscriptionClient.", this);
}

if (worldManager == null) { ... } // KEEP
```

After (Awake body should only have):
```csharp
if (stateMachine == null)
{
    Debug.LogError($"{nameof(VoiceToWorldLabsPluginCoordinator)} is missing a HolodeckStateMachine.", this);
}

if (worldManager == null)
{
    Debug.LogError($"{nameof(VoiceToWorldLabsPluginCoordinator)} is missing a WorldLabsWorldManager.", this);
}
```

- [ ] **Step 4: Update `OnEnable()` / `OnDisable()` — remove wake/capture subscriptions**

In `OnEnable()`, remove:
```csharp
if (_wakeTrigger != null)
{
    _wakeTrigger.WakeTriggered += HandleWakeTriggered;
}

if (voiceCaptureManager != null)
{
    voiceCaptureManager.CaptureFailed += HandleCaptureFailed;
}
```

In `OnDisable()`, remove:
```csharp
if (_wakeTrigger != null)
{
    _wakeTrigger.WakeTriggered -= HandleWakeTriggered;
}

if (voiceCaptureManager != null)
{
    voiceCaptureManager.CaptureFailed -= HandleCaptureFailed;
}
```

Leave the coroutine stop block and CancellationToken cancel/dispose in `OnDisable()` — they are still needed.

- [ ] **Step 5: Update `ResetDebugFields()` — remove `lastTranscript` line**

Find `ResetDebugFields()`:
```csharp
private void ResetDebugFields()
{
    lastTranscript = string.Empty;
    lastPromptUsed = string.Empty;
    lastWorldId = string.Empty;
}
```

Remove the `lastTranscript = string.Empty;` line:
```csharp
private void ResetDebugFields()
{
    lastPromptUsed = string.Empty;
    lastWorldId = string.Empty;
}
```

- [ ] **Step 6: Delete the five removed methods**

Delete these methods in their entirety:
- `HandleWakeTriggered()` (starts at approximately line 119)
- `BeginListening()` (starts at approximately line 140)
- `EndListeningAndGenerate()` (starts at approximately line 167)
- `RunVoiceToWorldFlow(VoiceCaptureResult capture)` (starts at approximately line 194, runs to ~line 447)
- `HandleCaptureFailed(string message)` (starts at approximately line 519)

After deletion, the only methods remaining should be: `Awake`, `OnEnable`, `OnDisable`, `ResetDebugFields`, `GenerateWorldAsync`, `BuildDisplayName`, `ResolveSpzUrl`.

### Addition steps

- [ ] **Step 7: Add `TriggerWorldGeneration` public entry point**

Add this method immediately before `GenerateWorldAsync`:

```csharp
        public void TriggerWorldGeneration(string prompt)
        {
            if (isBusy) return;
            if (string.IsNullOrWhiteSpace(prompt)) return;

            if (stateMachine.CurrentState == HolodeckState.Error)
                stateMachine.ClearErrorAndReturnToIdle();

            ResetDebugFields();

            _generationCts?.Cancel();
            _generationCts?.Dispose();
            _generationCts = new CancellationTokenSource();

            if (_activeFlow != null)
                StopCoroutine(_activeFlow);

            _activeFlow = StartCoroutine(RunGenerationFlow(prompt));
        }
```

- [ ] **Step 8: Add `RunGenerationFlow` private coroutine**

Add this method immediately after `TriggerWorldGeneration` and before `GenerateWorldAsync`. This is the full method body — copy it exactly:

```csharp
        private IEnumerator RunGenerationFlow(string prompt)
        {
            isBusy = true;
            lastPromptUsed = prompt;

            if (!stateMachine.TryTransitionTo(HolodeckState.Generating))
            {
                isBusy = false;
                stateMachine.SetError("Could not transition to Generating.");
                yield break;
            }

            Task<World> generationTask = GenerateWorldAsync(lastPromptUsed, _generationCts.Token);
            while (!generationTask.IsCompleted) yield return null;

            if (_generationCts == null || _generationCts.IsCancellationRequested)
            {
                isBusy = false;
                yield break;
            }

            if (generationTask.IsFaulted)
            {
                isBusy = false;
                Exception baseException = generationTask.Exception?.GetBaseException();
                Debug.LogError($"[VoiceToWorldLabsPluginCoordinator] Generation failed.\n{generationTask.Exception}", this);
                stateMachine.SetError(baseException != null ? baseException.Message : "Unknown generation error.");
                yield break;
            }

            World world = generationTask.Result;
            lastWorldId = world != null ? world.world_id : string.Empty;

            // Re-fetch world if pano_url is missing from generation response.
            WorldLabsClient assetClient = new WorldLabsClient();
            if (!string.IsNullOrEmpty(lastWorldId) && string.IsNullOrEmpty(world?.assets?.imagery?.pano_url))
            {
                Debug.Log($"[VoiceToWorldLabsPluginCoordinator] pano_url missing from generation response — re-fetching world '{lastWorldId}'.", this);
                Task<World> refetchTask = assetClient.GetWorldAsync(lastWorldId);
                while (!refetchTask.IsCompleted) yield return null;
                if (!refetchTask.IsFaulted && !refetchTask.IsCanceled && refetchTask.Result != null)
                {
                    world = refetchTask.Result;
                    Debug.Log($"[VoiceToWorldLabsPluginCoordinator] Re-fetched world. pano_url='{world?.assets?.imagery?.pano_url}'", this);
                }
                else
                {
                    Debug.LogWarning($"[VoiceToWorldLabsPluginCoordinator] Re-fetch failed ({refetchTask.Exception?.GetBaseException().Message}); will attempt panorama download with original world.", this);
                }
            }

            // Download panorama and show as skybox preview while the splat loads.
            Debug.Log($"[VoiceToWorldLabsPluginCoordinator] Starting panorama download. pano_url='{world?.assets?.imagery?.pano_url}'", this);
            Task<Texture2D> thumbnailTask = assetClient.DownloadPanoramaAsync(world);
            while (!thumbnailTask.IsCompleted) yield return null;

            if (_generationCts == null || _generationCts.IsCancellationRequested)
            {
                if (!thumbnailTask.IsFaulted && !thumbnailTask.IsCanceled && thumbnailTask.Result != null)
                    Destroy(thumbnailTask.Result);
                isBusy = false;
                yield break;
            }

            if (!thumbnailTask.IsFaulted && !thumbnailTask.IsCanceled && thumbnailTask.Result != null)
            {
                Debug.Log($"[VoiceToWorldLabsPluginCoordinator] Panorama downloaded ({thumbnailTask.Result.width}x{thumbnailTask.Result.height}). thumbnailSkybox={(thumbnailSkybox != null ? "assigned" : "NULL — not wired in Inspector")}", this);
                thumbnailSkybox?.Show(thumbnailTask.Result);
            }
            else
            {
                string reason = thumbnailTask.IsFaulted
                    ? thumbnailTask.Exception?.GetBaseException().Message
                    : thumbnailTask.IsCanceled ? "cancelled" : "null texture";
                Debug.LogWarning($"[VoiceToWorldLabsPluginCoordinator] Panorama download failed ({reason}); skipping skybox preview.", this);
            }

            if (floorLoader != null)
            {
                // ── Floor-loader path ─────────────────────────────────────────────
                Debug.Log($"[VoiceToWorldLabsPluginCoordinator] Floor-loader path: worldId={lastWorldId}", this);
                string spzUrl = ResolveSpzUrl(world);
                if (string.IsNullOrEmpty(spzUrl))
                {
                    isBusy = false;
                    stateMachine.SetError("No SPZ URL found in world assets.");
                    yield break;
                }

                Debug.Log($"[VoiceToWorldLabsPluginCoordinator] SPZ URL resolved: {spzUrl}", this);

                worldManager.RestoreDefaultWorld();
                worldManager.NotifyWorldLoadStarted(lastWorldId);

                Debug.Log("[VoiceToWorldLabsPluginCoordinator] SPZ download starting…", this);
                Task<byte[]> spzTask = WorldLabsClientExtensions.DownloadBinaryAsync(spzUrl);
                while (!spzTask.IsCompleted) yield return null;

                if (spzTask.IsFaulted)
                {
                    isBusy = false;
                    Debug.LogError($"[VoiceToWorldLabsPluginCoordinator] SPZ download failed: {spzTask.Exception?.GetBaseException().Message}", this);
                    worldManager.NotifyWorldLoadFailed(lastWorldId, spzTask.Exception?.GetBaseException().Message ?? "SPZ download failed.");
                    stateMachine.SetError(spzTask.Exception?.GetBaseException().Message ?? "SPZ download failed.");
                    yield break;
                }

                byte[] spzBytes = spzTask.Result;
                Debug.Log($"[VoiceToWorldLabsPluginCoordinator] SPZ download complete: {spzBytes.Length} bytes", this);

                if (_generationCts == null || _generationCts.IsCancellationRequested)
                {
                    Debug.Log("[VoiceToWorldLabsPluginCoordinator] Cancelled after SPZ download — aborting floor load.", this);
                    isBusy = false;
                    yield break;
                }

                Task<RuntimeSplatFloorLoader.LoadResult> placeTask = floorLoader.LoadPlacedRuntimeWorldAsync(
                    spzBytes,
                    worldId:        lastWorldId,
                    worldName:      world.display_name,
                    thumbnailUrl:   world.assets?.thumbnail_url,
                    gameObjectName: $"World_{world.display_name ?? lastWorldId}");

                while (!placeTask.IsCompleted) yield return null;

                if (placeTask.IsFaulted)
                {
                    isBusy = false;
                    Debug.LogError($"[VoiceToWorldLabsPluginCoordinator] Floor load failed: {placeTask.Exception?.GetBaseException().Message}", this);
                    worldManager.NotifyWorldLoadFailed(lastWorldId, placeTask.Exception?.GetBaseException().Message ?? "Floor load failed.");
                    stateMachine.SetError(placeTask.Exception?.GetBaseException().Message ?? "Floor load failed.");
                    yield break;
                }

                RuntimeSplatFloorLoader.LoadResult placed = placeTask.Result;
                Debug.Log($"[VoiceToWorldLabsPluginCoordinator] Floor load complete: renderer={placed.renderer != null}", this);

                if (placed.floorEstimate != null)
                {
                    SplatFloorEstimate est = placed.floorEstimate;
                    Debug.Log($"[VoiceToWorldLabsPluginCoordinator] Floor analysis: success={est.success} floorY={est.estimatedFloorY:F3} message='{est.message}'", this);
                }

                worldManager.RegisterExternalWorld(lastWorldId, placed.renderer);
                Debug.Log("[VoiceToWorldLabsPluginCoordinator] RegisterExternalWorld called — world live.", this);
            }
            else
            {
                // ── Standard WorldLabsWorldManager load ───────────────────────────
                worldManager.RestoreDefaultWorld();
                Task loadTask = worldManager.LoadWorldAsync(world);
                while (!loadTask.IsCompleted) yield return null;

                if (loadTask.IsFaulted)
                {
                    isBusy = false;
                    Exception baseException = loadTask.Exception?.GetBaseException();
                    stateMachine.SetError(baseException != null ? baseException.Message : "World load failed.");
                    yield break;
                }
            }

            if (!stateMachine.TryTransitionTo(HolodeckState.Ready))
            {
                isBusy = false;
                stateMachine.SetError("Could not transition to Ready.");
                yield break;
            }

            if (logDebugMessages)
                Debug.Log($"World loaded successfully. WorldId={lastWorldId}", this);

            thumbnailSkybox?.StartFadeOut();
            isBusy = false;
            _activeFlow = null;
        }
```

- [ ] **Step 9: Verify compilation**

Switch to Unity Editor. Check the Console for errors.

Expected: no errors. Common issues:
- `IWakeTrigger` not found → confirm `using Holodeck.Input;` was removed AND all `_wakeTrigger` references were deleted
- `VoiceCaptureManager` not found → confirm `using Holodeck.Voice;` removed AND all voice capture references deleted
- `lastTranscript` not found → confirm the field and property were both removed
- `SplatFloorEstimate` not found → this type is in `WorldLabs.Runtime.Tools`, which is already in the using block

- [ ] **Step 10: Verify the coordinator no longer has Inspector slots for wake/capture/transcription**

In the Unity Inspector, select the `VoiceToWorldLabsPluginCoordinator` component. Confirm:
- `Wake Trigger Behaviour` slot is **gone**
- `Voice Capture Manager` slot is **gone**
- `Transcription Client` slot is **gone**
- `Last Transcript` debug field is **gone**

If those slots are still visible, Unity may be showing stale serialized data. Use **Reset** in the component context menu (right-click the component header → Reset), then re-assign the remaining required fields (`Stat Machine`, `World Manager`, `Thumbnail Skybox`).

- [ ] **Step 11: Commit**

```bash
git add Assets/App/Scripts/Direct/VoiceToWorldLabsPluginCoordinator.cs
git commit -m "refactor: remove wake/capture/transcription from coordinator, add TriggerWorldGeneration"
```

---

## Task 5: Wire Inspector events and verify end-to-end

**Files:**
- No code changes — Inspector wiring and Play mode verification only

### Context

`WorldActionDispatcher.onGenerateWorldPrompt` is a `StringEvent` (which is `UnityEvent<string>`). When the intent pipeline interprets a `GenerateWorld` command, it calls `dispatcher.Execute(command, spatial)`, which calls `HandleGenerateWorld(command)`, which fires `onGenerateWorldPrompt` with `command.world_prompt`. That string must reach `VoiceToWorldLabsPluginCoordinator.TriggerWorldGeneration`.

`onSwitchToStaticWorld` is a `UnityEvent` (no parameter). Wiring it to `WorldLabsWorldManager.RestoreDefaultWorld` adds an additional listener alongside the existing `HandleSwitchToStaticWorld` code — both execute. This is intentional.

- [ ] **Step 1: Wire `onGenerateWorldPrompt` → `TriggerWorldGeneration` (Dynamic String)**

In the Inspector, select the `WorldActionDispatcher` component.

Under `Inspector Hooks → On Generate World Prompt`:
- Click `+` to add a listener
- Drag the `VoiceToWorldLabsPluginCoordinator` component into the object slot
- In the function dropdown, select `VoiceToWorldLabsPluginCoordinator → TriggerWorldGeneration` under the **Dynamic String** section (not Static — you need the dynamic section so the prompt string is passed through at runtime)

- [ ] **Step 2: Wire `onSwitchToStaticWorld` → `RestoreDefaultWorld` (Static)**

Under `Inspector Hooks → On Switch To Static World`:
- Click `+` to add a listener
- Drag the `WorldLabsWorldManager` component into the object slot
- In the function dropdown, select `WorldLabsWorldManager → RestoreDefaultWorld` under Static

- [ ] **Step 3: Verify keyboard debug path (push-to-talk) still works**

Press Play. Hold **V** (push-to-talk). Speak a world description (e.g. "a snowy mountain village"). Release V.

Expected Console sequence:
1. Transcript logged by `VoiceCommandRouter`
2. `GenerateWorld` intent dispatched (`WorldActionDispatcher` logs `Generate world: <prompt>`)
3. `[VoiceToWorldLabsPluginCoordinator]` logs appear: `Starting panorama download`, `SPZ download starting`, etc.
4. World loads and panorama fades out

- [ ] **Step 4: Verify `InteractionMemory.currentWorldRoot` is set automatically**

After the world loads in Step 3, check the `InteractionMemory` component in the Inspector (while still in Play mode).

Expected: `Current World Root` field shows the loaded world's `GameObject`.

- [ ] **Step 5: Verify `SwitchToStaticWorld` command**

Press V. Say "end program". Release V.

Expected: `RestoreDefaultWorld` is called (world unloads) and `InteractionMemory.currentWorldRoot` updates to the default world's root (or null if no default).

- [ ] **Step 6: Commit**

```bash
git commit --allow-empty -m "feat: wire WorldActionDispatcher events to coordinator and world manager in Inspector"
```

(Use `--allow-empty` since Inspector changes are in `.unity` scene files. If the scene file was modified, add it instead: `git add <your-scene-file>.unity && git commit -m ...`)
