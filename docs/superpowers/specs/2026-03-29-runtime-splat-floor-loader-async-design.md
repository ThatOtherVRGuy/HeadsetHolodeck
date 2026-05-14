# RuntimeSplatFloorLoader Async — Design Spec

**Date:** 2026-03-29
**Status:** Approved

## Overview

`RuntimeSplatFloorLoader.LoadPlacedRuntimeWorld` currently runs `SplatFloorAnalyzer.AnalyzeSpzBytes` and `RuntimeSplatProcessing.ProcessSPZBytes` synchronously on the main thread. For large splats this blocks the Unity Editor for several seconds, freezing the UI and causing async Task continuations to execute in Edit mode when the user stops Play mode mid-load.

Add `LoadPlacedRuntimeWorldAsync` — an async variant that offloads the two heavy CPU steps to a background thread, then returns to the main thread for renderer creation. The existing sync method is unchanged.

## Scope

| Action | File | Purpose |
|--------|------|---------|
| **Modify** | `Assets/App/Scripts/Direct/RuntimeSplatFloorLoader.cs` | Add `using System.Threading.Tasks;` and `LoadPlacedRuntimeWorldAsync` |
| **Modify** | `Assets/App/Scripts/Direct/VoiceToWorldLabsPluginCoordinator.cs` | Replace sync call with async Task polling |

---

## Part 1: `LoadPlacedRuntimeWorldAsync`

Add this method to `RuntimeSplatFloorLoader`, immediately after `LoadPlacedRuntimeWorld`:

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

    return Task.Run(() =>
    {
        SplatFloorEstimate floorEstimate = SplatFloorAnalyzer.AnalyzeSpzBytes(spzBytes, opts);

        RuntimeSplatData data = RuntimeSplatProcessing.ProcessSPZBytes(
            spzBytes, posFormat, scaleFormat, colorFormat, shFormat);

        data.worldId     = worldId;
        data.worldName   = worldName;
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
    }, TaskScheduler.FromCurrentSynchronizationContext());
}
```

**Key points:**
- `Task.Run` executes `AnalyzeSpzBytes` + `ProcessSPZBytes` on a thread pool thread.
- `ContinueWith(..., TaskScheduler.FromCurrentSynchronizationContext())` ensures the renderer creation block runs on Unity's main thread.
- `TaskScheduler.FromCurrentSynchronizationContext()` must be called from the main thread (i.e. when `LoadPlacedRuntimeWorldAsync` is first called), not from inside `Task.Run`. The method does this correctly since it's a regular (non-async) method that captures the scheduler before launching the background work.
- If `Task.Run` faults, the `ContinueWith` block re-throws, propagating the exception through the returned Task so the caller can observe it via `IsFaulted`.
- `using System.Threading.Tasks;` must be added to `RuntimeSplatFloorLoader.cs` — it is not currently present. Add it after `using System;`.
- `SplatFloorAnalysisOptions` is a class (reference type). `opts` shares the same reference as `floorAnalysis` when non-null, matching the behaviour of the existing sync method. The mutation of `opts.positionTransform` happens on the main thread before `Task.Run` is called, so the background thread only reads it — no data race. Concurrent calls are prevented in practice by the coordinator's `isBusy` guard.

---

## Part 2: Update `VoiceToWorldLabsPluginCoordinator`

Replace the `try/catch` block that calls `floorLoader.LoadPlacedRuntimeWorld(...)` (currently around lines 360–378) with:

```csharp
// Load and place renderer — heavy processing runs on background thread
Task<RuntimeSplatFloorLoader.LoadResult> placeTask =
    floorLoader.LoadPlacedRuntimeWorldAsync(
        spzBytes,
        worldId:       lastWorldId,
        worldName:     world.display_name,
        thumbnailUrl:  world.assets?.thumbnail_url,
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

Everything after this block (cancellation guard check no longer needed here — it already runs before this block, floor estimate log, `RegisterExternalWorld`) is unchanged.

---

## Known Limitations

- `renderer.LoadFromRuntimeData(data)` runs on the main thread inside the `ContinueWith` block. This call uploads vertex data to the GPU and may still cause a brief 1–3 frame hitch for very large splats, but is significantly shorter than the full synchronous path.
- `TaskScheduler.FromCurrentSynchronizationContext()` requires that `LoadPlacedRuntimeWorldAsync` is called from a thread that has a synchronization context (i.e. Unity's main thread). Calling it from a background thread will throw. This matches the existing contract of `LoadPlacedRuntimeWorld`.
