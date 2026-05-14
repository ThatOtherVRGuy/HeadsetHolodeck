# Floor Loader Integration — Single Download via Embedded Package

**Date:** 2026-03-28
**Status:** Approved

## Overview

Integrate `RuntimeSplatFloorLoader` into the voice-driven world-loading flow so the loaded splat is floor-detected and automatically placed with its floor at Y=0. SPZ bytes are downloaded exactly once and reused for both floor analysis and renderer creation. The WorldLabs package is embedded as a local package so `WorldLabsWorldManager` can be extended with three new public methods that expose its internal event infrastructure to external callers.

## Goals

- Single SPZ download per world load in the voice flow
- Floor detection and placement applied to all voice-generated worlds
- Full event lifecycle preserved (`OnWorldLoadStarted`, `OnWorldLoaded`, `OnWorldLoadFailed`) for `HolodeckModelController` and `HolodeckAudioFeedback`
- `floorLoader` is optional — if unassigned the coordinator falls back to `worldManager.LoadWorldAsync` (existing behavior, unchanged)

## Scope

| Action | File | Purpose |
|--------|------|---------|
| **Copy + embed** | `Packages/com.worldlabs.gaussian-splatting/` | Local editable copy of the WorldLabs package |
| **Modify** | `Packages/manifest.json` | Point package reference at local path |
| **Modify** | `Packages/com.worldlabs.gaussian-splatting/Runtime/WorldLabs/WorldLabsWorldManager.cs` | Add three event-infrastructure methods |
| **Modify** | `Assets/App/Scripts/Direct/VoiceToWorldLabsPluginCoordinator.cs` | Add floor loader field, replace `LoadWorldAsync` with single-download flow |

---

## Part 1: Embed the Package

Copy `Library/PackageCache/com.worldlabs.gaussian-splatting@d573119ed976/` to `Packages/com.worldlabs.gaussian-splatting/`.

In `Packages/manifest.json`, change:
```json
"com.worldlabs.gaussian-splatting": "https://github.com/nigelhartman/worldlabs_unity.git"
```
to:
```json
"com.worldlabs.gaussian-splatting": "file:com.worldlabs.gaussian-splatting"
```

Unity resolves `file:` paths relative to the `Packages/` directory. No other manifest changes are needed.

---

## Part 2: Add Three Methods to `WorldLabsWorldManager`

### `NotifyWorldLoadStarted(string worldId)`

Adds `worldId` to `_loadingWorlds` and fires `OnWorldLoadStarted`. Called by the coordinator before downloading SPZ bytes, so `HolodeckModelController` shows the holodeck model during loading.

```csharp
public void NotifyWorldLoadStarted(string worldId)
{
    if (string.IsNullOrEmpty(worldId))
        throw new ArgumentException("worldId must not be null or empty.", nameof(worldId));

    _loadingWorlds.Add(worldId);
    OnWorldLoadStarted?.Invoke(worldId);
}
```

### `RegisterExternalWorld(string worldId, GaussianSplatRenderer renderer)`

Registers an externally-created renderer, dismisses the default placeholder, and fires `OnWorldLoaded` (both C# event and UnityEvent). Called by the coordinator after `RuntimeSplatFloorLoader.LoadPlacedRuntimeWorld` succeeds.

```csharp
public void RegisterExternalWorld(string worldId, GaussianSplatRenderer renderer)
{
    if (string.IsNullOrEmpty(worldId))
        throw new ArgumentException("worldId must not be null or empty.", nameof(worldId));
    if (renderer == null)
        throw new ArgumentNullException(nameof(renderer));

    _loadedWorlds[worldId] = renderer;
    _loadingWorlds.Remove(worldId);

    if (_loadedWorlds.ContainsKey("__default__"))
        UnloadWorld("__default__");

    OnWorldLoaded?.Invoke(worldId, renderer);
    onWorldLoaded?.Invoke(worldId, renderer);
}
```

### `NotifyWorldLoadFailed(string worldId, string error)`

Removes `worldId` from `_loadingWorlds` and fires `OnWorldLoadFailed` (both C# event and UnityEvent). Called by the coordinator if SPZ download, floor analysis, or renderer creation fails.

```csharp
public void NotifyWorldLoadFailed(string worldId, string error)
{
    if (string.IsNullOrEmpty(worldId))
        throw new ArgumentException("worldId must not be null or empty.", nameof(worldId));

    _loadingWorlds.Remove(worldId);

    OnWorldLoadFailed?.Invoke(worldId, error ?? string.Empty);
    onWorldLoadFailed?.Invoke(worldId, error ?? string.Empty);
}
```

All three methods follow the existing patterns in `WorldLabsWorldManager` (null guards, same event invocation style).

---

## Part 3: Update `VoiceToWorldLabsPluginCoordinator`

### New using statement

```csharp
using WorldLabs.Runtime.Tools;
```

### New serialized field

```csharp
[Header("Floor Detection")]
[SerializeField] private RuntimeSplatFloorLoader floorLoader;
```

Optional — no `Awake` null check. If null, coordinator falls back to `worldManager.LoadWorldAsync`.

### New helper method: `ResolveSpzUrl`

Mirrors `WorldLabsWorldManager.LoadWorldAsync`'s URL resolution logic so the coordinator downloads the same resolution:

```csharp
private string ResolveSpzUrl(World world)
{
    string resKey = worldManager.preferredResolution switch
    {
        WorldLabsWorldManager.SplatResolution.FullRes => "full_res",
        WorldLabsWorldManager.SplatResolution._100k   => "100k",
        _                                              => "500k",
    };
    return world.assets?.splats?.GetUrl(resKey)
        ?? world.assets?.splats?.GetBestResolutionUrl();
}
```

### Replacement load block in `RunVoiceToWorldFlow`

Replace the existing `worldManager.RestoreDefaultWorld()` + `worldManager.LoadWorldAsync(world)` block with:

```csharp
if (floorLoader != null)
{
    // ── Floor-loader path (single download) ──────────────────────────
    string spzUrl = ResolveSpzUrl(world);
    if (string.IsNullOrEmpty(spzUrl))
    {
        isBusy = false;
        stateMachine.SetError("No SPZ URL found in world assets.");
        yield break;
    }

    worldManager.RestoreDefaultWorld();
    worldManager.NotifyWorldLoadStarted(lastWorldId);

    // Download SPZ bytes (single download for both analysis and rendering)
    Task<byte[]> spzTask = WorldLabsClientExtensions.DownloadBinaryAsync(spzUrl);
    while (!spzTask.IsCompleted)
        yield return null;

    if (spzTask.IsFaulted)
    {
        isBusy = false;
        worldManager.NotifyWorldLoadFailed(lastWorldId, spzTask.Exception?.GetBaseException().Message ?? "SPZ download failed.");
        stateMachine.SetError(spzTask.Exception?.GetBaseException().Message ?? "SPZ download failed.");
        yield break;
    }

    byte[] spzBytes = spzTask.Result;

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

    if (logDebugMessages && placed.floorEstimate != null)
    {
        SplatFloorEstimate est = placed.floorEstimate;
        Debug.Log($"[VoiceToWorldLabsPluginCoordinator] Floor analysis: success={est.success} floorY={est.estimatedFloorY:F3} message='{est.message}'", this);
    }

    worldManager.RegisterExternalWorld(lastWorldId, placed.renderer);
}
else
{
    // ── Fallback: standard WorldLabsWorldManager load ─────────────────
    worldManager.RestoreDefaultWorld();

    Task loadTask = worldManager.LoadWorldAsync(world);
    while (!loadTask.IsCompleted)
        yield return null;

    if (loadTask.IsFaulted)
    {
        isBusy = false;
        Exception baseException = loadTask.Exception?.GetBaseException();
        stateMachine.SetError(baseException != null ? baseException.Message : "World load failed.");
        yield break;
    }
}
```

The state-machine transition to `Ready`, skybox fade-out, and `isBusy = false` that follow are unchanged.

---

## Known Limitations

- `floorLoader.LoadPlacedRuntimeWorld` runs `RuntimeSplatProcessing.ProcessSPZBytes` synchronously on the main thread. This may cause a brief frame hitch for large splats. The existing `WorldLabsWorldManager.LoadWorldAsync` offloads this to a background thread. This limitation is pre-existing in `RuntimeSplatFloorLoader` itself — out of scope here.
- Floor placement is applied to the voice flow only. Worlds loaded via `WorldBrowserController` go through `WorldLabsWorldManager.LoadWorldAsync` and receive no floor correction (out of scope).

---

## Out of Scope

- Floor correction for browser-loaded worlds
- Moving `RuntimeSplatProcessing.ProcessSPZBytes` off the main thread inside `RuntimeSplatFloorLoader`
- Exposing `LoadResult.floorEstimate` to the rest of the app
