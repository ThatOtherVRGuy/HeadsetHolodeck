# Thumbnail Skybox Preview

**Date:** 2026-03-26
**Status:** Approved

## Overview

When a World Labs world finishes generating, display its equirectangular thumbnail as a skybox while the gaussian splat downloads and processes. Once the splat is ready, fade the skybox out and restore the previous skybox.

## Goals

- Give the user immediate visual feedback that generation succeeded
- Bridge the gap between generation complete and splat visible
- Non-fatal: if thumbnail download fails, silently skip and proceed

## New Component: `ThumbnailSkyboxController`

**File:** `Assets/App/Scripts/Direct/ThumbnailSkyboxController.cs`
**Namespace:** `Holodeck.Direct`

### Serialized fields
- `[Header("Thumbnail Skybox")] float fadeOutDuration = 1.5f`

### Public API

**`void Show(Texture2D tex)`**
- If a fade coroutine is currently running (tracked internally), stop it via `StopCoroutine(_fadeCoroutine)` — safe because the coroutine is always started on this MonoBehaviour
- If `_skyboxMaterial` exists, destroy it; if `_thumbnailTexture` exists, destroy it
- **Takes ownership of `tex`** — the caller must not destroy it; `ThumbnailSkyboxController` is responsible for destroying it in `FadeOutAndRestore()` and `OnDestroy()`
- Save `RenderSettings.skybox` to `_previousSkybox` (may be null — that is valid; assigning null back to `RenderSettings.skybox` correctly disables the skybox). `RestoreDefaultWorld()` is confirmed to not touch `RenderSettings.skybox`, so `_previousSkybox` will not become stale between `Show()` and `FadeOutAndRestore()`
- Call `Shader.Find("Skybox/Panoramic")`; if null, log an error and return without modifying `RenderSettings.skybox`
- Create a `new Material(shader)`
- Set texture via `material.SetTexture("_Tex", tex)` (the `Skybox/Panoramic` shader uses `_Tex`, not `_MainTex`)
- Explicitly set `material.SetFloat("_Exposure", 1.0f)`
- Assign to `RenderSettings.skybox`
- Call `DynamicGI.UpdateEnvironment()`
- Store references in `_skyboxMaterial`, `_thumbnailTexture`

**`void StartFadeOut()`**
- Stops any in-progress fade coroutine
- Calls `_fadeCoroutine = StartCoroutine(FadeOutCoroutine())`
- The coroutine is owned by this MonoBehaviour so `StopCoroutine` in `Show()` and `OnDestroy()` works reliably

**`private IEnumerator FadeOutCoroutine()`**
- Lerp `_skyboxMaterial.SetFloat("_Exposure", ...)` from 1→0 over `fadeOutDuration` seconds
- Set `RenderSettings.skybox = _previousSkybox`
- Call `DynamicGI.UpdateEnvironment()`
- `Destroy(_skyboxMaterial)` and `Destroy(_thumbnailTexture)`
- Clear `_skyboxMaterial`, `_thumbnailTexture`, `_fadeCoroutine` to null

### Internal state
- `Material _skyboxMaterial`
- `Material _previousSkybox`
- `Texture2D _thumbnailTexture`
- `Coroutine _fadeCoroutine`

### Cleanup (`OnDestroy`)
- `StopCoroutine(_fadeCoroutine)` if non-null
- `Destroy(_skyboxMaterial)` and `Destroy(_thumbnailTexture)` if non-null
- `RenderSettings.skybox = _previousSkybox`
- `DynamicGI.UpdateEnvironment()`

## Changes to `VoiceToWorldLabsPluginCoordinator`

### New serialized field
Under the existing `[Header("Dependencies")]` group:
```csharp
[SerializeField] private ThumbnailSkyboxController thumbnailSkybox;
```
Optional — leaving it unassigned disables all skybox behaviour with no errors.

### Flow change in `RunVoiceToWorldFlow`

After the generation task completes and `World world` is obtained:

1. Create a `new WorldLabsClient()` and start `client.DownloadThumbnailAsync(world)` as a `Task<Texture2D>`. A new instance is used rather than reusing the generation client since `GenerateWorldAsync` is an async method that does not return the client. Creating a second `WorldLabsClient()` is the same pattern already used elsewhere in the codebase and carries no auth implications.
2. Yield until the task completes: `while (!thumbnailTask.IsCompleted) yield return null`
3. Check cancellation: if `_generationCts` is null or cancelled, and the thumbnail task completed successfully, call `Destroy(thumbnailTask.Result)` to prevent a texture memory leak, then `yield break`
4. If the task succeeded and was not cancelled: call `thumbnailSkybox?.Show(thumbnailTask.Result)` — ownership of the `Texture2D` transfers to `ThumbnailSkyboxController`
5. If the task faulted or `thumbnail_url` was null: `Debug.LogWarning(...)`, continue without skybox (non-fatal)
6. Call `worldManager.RestoreDefaultWorld()` — confirmed safe: does not touch `RenderSettings.skybox`
7. Start `worldManager.LoadWorldAsync(world)` and yield until complete as before
8. After the load task completes successfully:
   ```csharp
   if (thumbnailSkybox != null)
       thumbnailSkybox.StartFadeOut();
   ```

### Cancellation

`DownloadThumbnailAsync` does not accept a `CancellationToken`. Cancellation is checked after the download task completes (step 3). If cancelled after a successful download, the texture is explicitly destroyed to prevent a leak before yielding break. If the component is disabled mid-download, `OnDisable` stops `_activeFlow` via the existing coroutine handle; `ThumbnailSkyboxController.OnDestroy` handles skybox and texture cleanup.

## Wiring

Add `ThumbnailSkyboxController` to the Systems GameObject. Wire it into `VoiceToWorldLabsPluginCoordinator → Thumbnail Skybox` in the Inspector.

## Out of Scope

- Panorama URL (`pano_url`) — higher resolution equirectangular; same approach, swap in later if needed
- Skybox fade-in transition
- UI preview of thumbnail
