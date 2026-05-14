# Speech Intent Core Integration — Design Spec

**Date:** 2026-03-30
**Status:** Approved
**Sub-project:** 1 of 4 (Core Integration)

## Overview

Connect the `SpeechIntent` system to the existing project so that speech-interpreted world-generation commands flow through `WorldActionDispatcher` → `VoiceToWorldLabsPluginCoordinator` and `InteractionMemory` stays in sync with the world lifecycle. This sub-project wires the two systems together without touching the voice capture or wake-word layers (those are later sub-projects).

## Scope

| Action | File | Purpose |
|--------|------|---------|
| **Modify** | `Assets/App/Scripts/Direct/VoiceToWorldLabsPluginCoordinator.cs` | Remove wake/capture/transcription dependencies; add `TriggerWorldGeneration(string)` entry point |
| **Modify** | `Assets/App/Command/SpeechIntent/Runtime/InteractionMemory.cs` | Subscribe to `WorldLabsWorldManager.OnWorldLoaded` / `OnWorldUnloaded` to keep `currentWorldRoot` in sync |
| **Modify** | `Assets/App/Command/SpeechIntent/Runtime/VoiceCommandRouter.cs` | Add `_isRecording` guard to prevent double-trigger |
| **Modify** | `Assets/App/Command/SpeechIntent/Runtime/OpenAiSpeechIntentConfig.cs` | Fix wrong model names |
| **No code changes** | `Assets/App/Command/SpeechIntent/Runtime/WorldActionDispatcher.cs` | Inspector wiring only |

No changes to `ThumbnailSkyboxController`, `WorldLabsWorldManager`, `RuntimeSplatFloorLoader`, or `HolodeckStateMachine`.

---

## Part 1: Refactor `VoiceToWorldLabsPluginCoordinator`

### What to remove

Remove the three serialized dependency fields and all code that uses them:

```csharp
// Remove these fields:
[SerializeField] private MonoBehaviour wakeTriggerBehaviour;
[SerializeField] private VoiceCaptureManager voiceCaptureManager;
[SerializeField] private OpenAITranscriptionClient transcriptionClient;

// Remove this private field:
private IWakeTrigger _wakeTrigger;

// Remove this debug field (transcript no longer lives here):
[SerializeField, TextArea] private string lastTranscript = string.Empty;
```

Also remove the public property that backs `lastTranscript`:
```csharp
public string LastTranscript => lastTranscript;
```

Remove the following methods in their entirety:
- `HandleWakeTriggered()`
- `BeginListening()`
- `EndListeningAndGenerate()`
- `RunVoiceToWorldFlow(VoiceCaptureResult capture)` — replaced by `RunGenerationFlow`
- `HandleCaptureFailed(string message)`

Update `Awake()` — remove the three null checks for `_wakeTrigger`, `voiceCaptureManager`, and `transcriptionClient`.

Update `OnEnable()` / `OnDisable()` — remove the `WakeTriggered` and `CaptureFailed` subscription/unsubscription blocks.

Update `ResetDebugFields()` — remove the `lastTranscript = string.Empty;` line.

### What to add

**New public entry point:**

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

**New private coroutine** — `RunVoiceToWorldFlow` without transcription, without capture cleanup, going straight to Generating:

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
        Debug.Log($"[VoiceToWorldLabsPluginCoordinator] pano_url missing — re-fetching world '{lastWorldId}'.", this);
        Task<World> refetchTask = assetClient.GetWorldAsync(lastWorldId);
        while (!refetchTask.IsCompleted) yield return null;
        if (!refetchTask.IsFaulted && !refetchTask.IsCanceled && refetchTask.Result != null)
        {
            world = refetchTask.Result;
            Debug.Log($"[VoiceToWorldLabsPluginCoordinator] Re-fetched. pano_url='{world?.assets?.imagery?.pano_url}'", this);
        }
        else
        {
            Debug.LogWarning($"[VoiceToWorldLabsPluginCoordinator] Re-fetch failed; continuing with original world.", this);
        }
    }

    // Download panorama and show as skybox preview.
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
        Debug.Log($"[VoiceToWorldLabsPluginCoordinator] Panorama downloaded ({thumbnailTask.Result.width}x{thumbnailTask.Result.height}).", this);
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
        // Floor-loader path.
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
        // Standard WorldLabsWorldManager load.
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

**Key differences from `RunVoiceToWorldFlow`:**
- No `capture` parameter.
- No transcription coroutine (`transcriptionClient.TranscribeWav`).
- No `HolodeckState.Interpreting` transition.
- No `lastTranscript` assignment.
- No `capture?.Clip` destroy at end.

---

## Part 2: WorldActionDispatcher Inspector Wiring (Editor — no code changes)

After implementation, wire two events in the Unity Inspector on the `WorldActionDispatcher` component:

| Event | Target Component | Method | Mode |
|-------|-----------------|--------|------|
| `onGenerateWorldPrompt` | `VoiceToWorldLabsPluginCoordinator` | `TriggerWorldGeneration` | Dynamic String |
| `onSwitchToStaticWorld` | `WorldLabsWorldManager` | `RestoreDefaultWorld` | Static |

**Dynamic String** means the string value passed by the event at runtime is forwarded to the method parameter — do not use the Static section for `onGenerateWorldPrompt`.

`RestoreDefaultWorld` takes no parameters, so it appears under Static.

**Note on `onSwitchToStaticWorld` side effects:** `WorldActionDispatcher.HandleSwitchToStaticWorld()` already calls `staticWorldController.SwitchToStaticWorld()` (when that field is assigned) and then fires `onSwitchToStaticWorld`. The Inspector wiring above adds `RestoreDefaultWorld` as an additional listener on that event — both calls will execute. This is additive and intentional. See Known Constraints for details.

---

## Part 3: `InteractionMemory` subscribes to world lifecycle

`InteractionMemory` currently has no automatic update mechanism — `currentWorldRoot` must be set manually. Add subscription to `WorldLabsWorldManager`'s C# events so it stays in sync automatically.

`OnWorldLoaded` signature: `event Action<string, GaussianSplatRenderer> OnWorldLoaded`
`OnWorldUnloaded` signature: `event Action<string> OnWorldUnloaded`

Add to `InteractionMemory.cs`:

```csharp
// New serialized field:
[Header("World Manager")]
[SerializeField] private WorldLabsWorldManager worldManager;

// New lifecycle:
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

`GaussianSplatRenderer` lives in the `GaussianSplatting.Runtime` namespace. `WorldLabsWorldManager` lives in `WorldLabs.Runtime`. Add **both** using directives at the top of `InteractionMemory.cs`:

```csharp
using GaussianSplatting.Runtime;
using WorldLabs.Runtime;
```

**Required Inspector wiring:** drag the `WorldLabsWorldManager` component into the `worldManager` slot on `InteractionMemory`.

---

## Part 4: VoiceCommandRouter recording guard

`BeginRecording()` and `EndRecordingAndProcess()` are public and can be called from external code (and will be, once the wake-word system is added). Without a guard, a duplicate `BeginRecording()` call starts a second microphone session on top of the first.

Add a private flag and early-return guards:

```csharp
private bool _isRecording;

public void BeginRecording()
{
    if (_isRecording) return;          // guard added
    _isRecording = true;
    if (recorder == null)
    {
        _isRecording = false;
        EmitError("Recorder reference is missing.");
        return;
    }
    recorder.BeginRecording();
}

public void EndRecordingAndProcess()
{
    if (!_isRecording) return;         // guard added
    _isRecording = false;
    if (recorder == null)
    {
        EmitError("Recorder reference is missing.");
        return;
    }
    // ... rest of method unchanged
}
```

`_wasRecordingFromKeyboard` (existing field) is still needed to pair keyboard key-down/key-up — do not remove it.

---

## Part 5: Model name corrections in `OpenAiSpeechIntentConfig`

Two model names in the default ScriptableObject values are wrong:

| Field | Current value | Correct value |
|-------|--------------|---------------|
| `transcriptionModel` | `"gpt-4o-mini-transcribe"` | `"whisper-1"` |
| `intentModel` | `"gpt-5.4"` | **Verify on OpenAI dashboard** — `gpt-5.4` was unverifiable at spec time (knowledge cutoff Aug 2025). Replace with the best available structured-output model (e.g. `"gpt-4o"`) and update once confirmed. |

Change the default field values in the ScriptableObject class:

```csharp
public string transcriptionModel = "whisper-1";
public string intentModel = "gpt-4o";  // Update if a newer model is preferred
```

These are default values on the ScriptableObject — existing project assets that have been saved with the old names will retain the old string and must be updated manually in the Inspector or via the asset file.

---

## Known Constraints

- **`isBusy` guards only within a single session.** If the editor is stopped mid-generation, `isBusy` resets on the next Play. This is acceptable for now.
- **`InteractionMemory.currentWorldRoot` set from `OnWorldLoaded`.** This fires for both the voice path and the browser path, so after this change, both paths will automatically populate `InteractionMemory`. Note that `OnWorldLoaded` also fires when `WorldLabsWorldManager` loads the default asset (`RestoreDefaultWorld` → `LoadDefaultAsset`), so `currentWorldRoot` will be set to the default splat's `GameObject` after any world reset. This is acceptable — `GetLastCreatedOrInteracted()` falling back to the world root is correct behaviour.
- **No cancel/restart on double TriggerWorldGeneration.** If `isBusy` is true, the new prompt is silently dropped. This matches the existing `HandleWakeTriggered` behaviour. A queue or cancel-and-restart strategy is a future enhancement.
- **`onSwitchToStaticWorld` wiring calls `worldManager.RestoreDefaultWorld()`.** The existing `HandleSwitchToStaticWorld` in `WorldActionDispatcher` also calls `staticWorldController.SwitchToStaticWorld()` if that field is assigned. Both can coexist — the Inspector wiring is additive.
