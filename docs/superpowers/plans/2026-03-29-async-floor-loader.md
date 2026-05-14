# Async Floor Loader Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Offload `SplatFloorAnalyzer.AnalyzeSpzBytes` and `RuntimeSplatProcessing.ProcessSPZBytes` to a background thread so the Unity main thread is no longer blocked during voice-triggered world loads.

**Architecture:** Add `LoadPlacedRuntimeWorldAsync` to `RuntimeSplatFloorLoader` — it runs the two CPU-heavy steps in `Task.Run`, then creates the renderer on the main thread via `ContinueWith(..., TaskScheduler.FromCurrentSynchronizationContext())`. `VoiceToWorldLabsPluginCoordinator` replaces its synchronous `try/catch` call with a Task-polling coroutine pattern (`while (!placeTask.IsCompleted) yield return null`). The existing sync `LoadPlacedRuntimeWorld` is untouched.

**Tech Stack:** Unity C#, `System.Threading.Tasks`, `TaskScheduler.FromCurrentSynchronizationContext()`

**Spec:** `docs/superpowers/specs/2026-03-29-runtime-splat-floor-loader-async-design.md`

---

## File Map

| Action | File | Purpose |
|--------|------|---------|
| **Modify** | `Assets/App/Scripts/Direct/RuntimeSplatFloorLoader.cs` | Add `using System.Threading.Tasks;` and `LoadPlacedRuntimeWorldAsync` method |
| **Modify** | `Assets/App/Scripts/Direct/VoiceToWorldLabsPluginCoordinator.cs:359–376` | Replace sync `LoadPlacedRuntimeWorld` try/catch with async Task polling |

---

## Task 1: Add `LoadPlacedRuntimeWorldAsync` to `RuntimeSplatFloorLoader`

**Files:**
- Modify: `Assets/App/Scripts/Direct/RuntimeSplatFloorLoader.cs`

### Context

The file currently has four using directives (lines 1–6). `LoadPlacedRuntimeWorld` ends around line 126 and is followed by `ApplyFloorPlacement`. The new method goes between them.

The two private helpers `AssignShaders` and `GetFormats` are already accessible from the new method (same class). `EnsureShaders` is also a private instance method.

### Steps

- [ ] **Step 1: Add `using System.Threading.Tasks;`**

Add after `using System;` (line 3):

```csharp
using System.Threading.Tasks;
```

The file's using block should then read:
```csharp
using System;
using System.Threading.Tasks;
using GaussianSplatting.Runtime;
using UnityEngine;
using WorldLabs.Runtime.Tools;
```

- [ ] **Step 2: Add `LoadPlacedRuntimeWorldAsync`**

Insert the following method immediately after the closing brace of `LoadPlacedRuntimeWorld` (currently line 126) and before `ApplyFloorPlacement`:

```csharp
/// <summary>
/// Async variant of <see cref="LoadPlacedRuntimeWorld"/>. Offloads floor analysis and SPZ
/// processing to a background thread, then creates the renderer on the main thread.
/// Use this from coroutines to avoid blocking the main thread on large splats.
/// </summary>
public Task<LoadResult> LoadPlacedRuntimeWorldAsync(
    byte[] spzBytes,
    string worldId = null,
    string worldName = null,
    string thumbnailUrl = null,
    string gameObjectName = null)
{
    if (spzBytes == null || spzBytes.Length == 0)
        throw new ArgumentException("SPZ bytes are null or empty.", nameof(spzBytes));

    EnsureShaders();

    Quaternion localRotation = applyWorldLabsDefaultRotation
        ? Quaternion.Euler(-180f, 0f, 0f)
        : Quaternion.identity;

    SplatFloorAnalysisOptions opts = floorAnalysis ?? new SplatFloorAnalysisOptions();
    opts.positionTransform = Matrix4x4.Rotate(localRotation);

    var (posFormat, scaleFormat, colorFormat, shFormat) = GetFormats(quality);

    // Capture the main-thread scheduler before entering the background task.
    TaskScheduler mainThread = TaskScheduler.FromCurrentSynchronizationContext();

    return Task.Run(() =>
    {
        SplatFloorEstimate floorEstimate = SplatFloorAnalyzer.AnalyzeSpzBytes(spzBytes, opts);

        RuntimeSplatData data = RuntimeSplatProcessing.ProcessSPZBytes(
            spzBytes, posFormat, scaleFormat, colorFormat, shFormat);

        data.worldId      = worldId;
        data.worldName    = worldName;
        data.thumbnailUrl = thumbnailUrl;

        return (floorEstimate, data);
    }).ContinueWith(t =>
    {
        // Back on the main thread — all Unity API calls go here.
        if (t.IsFaulted)
            throw t.Exception!.GetBaseException();

        var (floorEstimate, data) = t.Result;

        string goName = !string.IsNullOrWhiteSpace(gameObjectName)
            ? gameObjectName
            : (!string.IsNullOrWhiteSpace(worldName) ? $"World_{worldName}" : "World");

        var go = new GameObject(goName);
        go.transform.SetParent(worldParent, false);
        go.transform.localRotation = localRotation;
        go.transform.localPosition = autoPlaceAtOrigin && floorEstimate != null && floorEstimate.success
            ? floorEstimate.recommendedLocalPosition
            : Vector3.zero;

        var renderer = go.AddComponent<GaussianSplatRenderer>();
        AssignShaders(renderer);
        renderer.LoadFromRuntimeData(data);

        return new LoadResult
        {
            gameObject    = go,
            renderer      = renderer,
            runtimeData   = data,
            floorEstimate = floorEstimate,
        };
    }, mainThread);
}
```

- [ ] **Step 3: Verify compilation**

Switch to Unity Editor and check the Console for compile errors.

Expected: no errors. Common issues:
- `Task` not found → confirm `using System.Threading.Tasks;` was added
- `TaskScheduler` not found → same fix
- `SplatFloorEstimate` not found → confirm `SplatFloorAnalyzer.cs` is present at `Assets/App/Scripts/Direct/`

- [ ] **Step 4: Commit**

```bash
git add Assets/App/Scripts/Direct/RuntimeSplatFloorLoader.cs
git commit -m "feat: add LoadPlacedRuntimeWorldAsync to offload SPZ processing to background thread"
```

---

## Task 2: Update `VoiceToWorldLabsPluginCoordinator` to use async method

**Files:**
- Modify: `Assets/App/Scripts/Direct/VoiceToWorldLabsPluginCoordinator.cs:359–376`

### Context: block to replace

The current synchronous call (lines 359–376):

```csharp
// Load and place renderer (floor analysis runs inside LoadPlacedRuntimeWorld)
RuntimeSplatFloorLoader.LoadResult placed;
try
{
    placed = floorLoader.LoadPlacedRuntimeWorld(
        spzBytes,
        worldId: lastWorldId,
        worldName: world.display_name,
        thumbnailUrl: world.assets?.thumbnail_url,
        gameObjectName: $"World_{world.display_name ?? lastWorldId}");
}
catch (Exception ex)
{
    isBusy = false;
    worldManager.NotifyWorldLoadFailed(lastWorldId, ex.Message);
    stateMachine.SetError(ex.Message);
    yield break;
}
```

Everything before this block (cancellation guard at lines 353–357) and after it (floor estimate log at lines 378–382, `RegisterExternalWorld` at line 384) is unchanged.

### Steps

- [ ] **Step 1: Replace the block**

Replace the block shown above (lines 359–376) with:

```csharp
// Load and place renderer — heavy processing runs on background thread
Task<RuntimeSplatFloorLoader.LoadResult> placeTask =
    floorLoader.LoadPlacedRuntimeWorldAsync(
        spzBytes,
        worldId:        lastWorldId,
        worldName:      world.display_name,
        thumbnailUrl:   world.assets?.thumbnail_url,
        gameObjectName: $"World_{world.display_name ?? lastWorldId}");

while (!placeTask.IsCompleted)
    yield return null;

if (placeTask.IsFaulted)
{
    isBusy = false;
    worldManager.NotifyWorldLoadFailed(lastWorldId, placeTask.Exception?.GetBaseException().Message ?? "Floor load failed.");
    stateMachine.SetError(placeTask.Exception?.GetBaseException().Message ?? "Floor load failed.");
    yield break;
}

RuntimeSplatFloorLoader.LoadResult placed = placeTask.Result;
```

The `// Load and place renderer` comment and the `placed` variable declaration are now part of the replacement — the old comment and `RuntimeSplatFloorLoader.LoadResult placed;` declaration above the old `try` are removed entirely.

- [ ] **Step 2: Verify compilation**

Switch to Unity Editor and check the Console for compile errors.

Expected: no errors. If `LoadPlacedRuntimeWorldAsync` is not found, confirm Task 1 was completed and the method name is spelled correctly.

- [ ] **Step 3: Verify runtime behaviour in Play mode**

Press Play. Trigger the voice flow.

Expected:
- Editor remains responsive during SPZ download and processing (no freeze)
- Console shows floor analysis log once the world loads
- World appears with floor at Y=0
- `HolodeckModelController` shows/hides `TNGHolodeck` correctly
- Browser world loading also works normally (unaffected — uses `LoadWorldAsync` directly)

- [ ] **Step 4: Commit**

```bash
git add Assets/App/Scripts/Direct/VoiceToWorldLabsPluginCoordinator.cs
git commit -m "feat: use LoadPlacedRuntimeWorldAsync in coordinator to unblock main thread"
```
