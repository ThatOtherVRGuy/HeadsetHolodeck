# Browser-Path Panorama Sphere — Design Spec

**Date:** 2026-03-29
**Status:** Approved

## Overview

When a user taps a world card in the `WorldBrowserController` UI, the app currently calls `LoadWorldAsync` and renders the splat with no panorama sphere. This spec adds panorama-sphere support to the browser path, a panorama-only mode (no splat), and error event hooks — matching the experience already available in the voice path.

## Scope

| Action | File | Purpose |
|--------|------|---------|
| **Modify** | `Packages/com.worldlabs.gaussian-splatting/Runtime/WorldLabs/WorldBrowserController.cs` | Add panorama fields to `WorldBrowserController`; extend `WorldCardUI.Bind()` and `HandleClick()` |

No changes to `ThumbnailSkyboxController`, `WorldLabsWorldManager`, or `VoiceToWorldLabsPluginCoordinator`.

---

## Part 1: New Inspector fields on `WorldBrowserController`

Add under a `[Header("Panorama Sphere")]` group:

```csharp
[Header("Panorama Sphere")]
[Tooltip("Fired with the downloaded Texture2D when panorama is ready. Wire to ThumbnailSkyboxController.Show.")]
[SerializeField] private UnityEvent<Texture2D> onPanoramaTextureReady;

[Tooltip("Fired when the splat renderer is ready. Wire to ThumbnailSkyboxController.StartFadeOut.")]
[SerializeField] private UnityEvent onSplatReady;

[Tooltip("Fired when the panorama texture download fails (network error, missing URL, etc).")]
[SerializeField] private UnityEvent onPanoramaDownloadFailed;

[Tooltip("When enabled, the SPZ is never downloaded or rendered. Only the panorama sphere is shown.")]
[SerializeField] private bool panoramaOnly = false;
```

`UnityEvent<Texture2D>` requires `using UnityEngine.Events;` — add it if not present.

**Assembly constraint:** `WorldBrowserController` is in the `WorldLabs` assembly (has its own `.asmdef`) which does not reference `Assembly-CSharp`. Using `UnityEvent` for inspector wiring avoids a circular assembly dependency while still allowing `ThumbnailSkyboxController` (which lives in `Assembly-CSharp`) to be connected at edit time.

---

## Part 2: Pass panorama callbacks into `WorldCardUI`

`WorldCardUI.Bind()` currently takes `(World world, WorldLabsWorldManager manager)`. Extend the signature:

```csharp
public void Bind(
    World world,
    WorldLabsWorldManager manager,
    Action<Texture2D> onPanoramaReady,
    Action onSplatReady,
    Action onPanoramaFailed,
    bool panoramaOnly)
```

Store the new parameters as private fields on `WorldCardUI`:

```csharp
Action<Texture2D> _onPanoramaReady;
Action            _onSplatReady;
Action            _onPanoramaFailed;
bool              _panoramaOnly;
```

Update the call site at `WorldBrowserController` line 325:

```csharp
card.Bind(
    worlds[i],
    worldManager,
    tex => onPanoramaTextureReady?.Invoke(tex),
    ()  => onSplatReady?.Invoke(),
    ()  => onPanoramaDownloadFailed?.Invoke(),
    panoramaOnly);
```

---

## Part 3: Rewrite `WorldCardUI.HandleClick()`

Replace the current `HandleClick()` body with:

```csharp
async void HandleClick()
{
    Debug.Log($"[WorldCardUI] Click: '{_world?.display_name}'");

    if (_world == null || _manager == null) return;
    if (_manager.IsWorldLoading(_world.world_id)) return;

    // ── Unload path (unchanged) ───────────────────────────────────────────
    if (_manager.IsWorldLoaded(_world.world_id))
    {
        _manager.UnloadWorld(_world.world_id);
        RefreshState();
        return;
    }

    RefreshState();

    // Capture world reference before any await — card may be rebound during downloads.
    World worldAtClick = _world;

    // ── 1. Download panorama texture ──────────────────────────────────────
    string panoUrl  = _world?.assets?.imagery?.pano_url;
    string thumbUrl = _world?.assets?.thumbnail_url;

    if (!string.IsNullOrEmpty(panoUrl) || !string.IsNullOrEmpty(thumbUrl))
    {
        try
        {
            Texture2D tex = await WorldLabsClientExtensions
                .DownloadTextureWithFallbackAsync(panoUrl, thumbUrl);

            // Guard: if card was rebound to a different world while awaiting, discard.
            if (_world != worldAtClick) return;

            if (tex != null)
                _onPanoramaReady?.Invoke(tex);
            else
                _onPanoramaFailed?.Invoke();
        }
        catch (Exception ex)
        {
            if (_world != worldAtClick) return;
            Debug.LogWarning($"[WorldCardUI] Panorama sphere download failed for " +
                             $"'{_world?.display_name}': {ex.Message}");
            _onPanoramaFailed?.Invoke();
        }
    }

    // ── 2. Panorama-only mode — stop here ─────────────────────────────────
    if (_panoramaOnly)
    {
        RefreshState();
        return;
    }

    // Guard: check again before starting the SPZ load (covers the case where no
    // pano URL existed so the block above was skipped entirely).
    if (_world != worldAtClick) return;

    // ── 3. Load splat ─────────────────────────────────────────────────────
    try
    {
        await _manager.LoadWorldAsync(_world);
        _onSplatReady?.Invoke();
    }
    catch (Exception ex)
    {
        Debug.LogError($"[WorldCardUI] Load failed '{_world?.display_name}': {ex.Message}");
    }

    RefreshState();
}
```

**Key behaviours:**
- If no pano URL is available (both `pano_url` and `thumbnail_url` are null/empty), the pano download block is skipped entirely and `onPanoramaDownloadFailed` is **not** fired. In normal mode, load proceeds. In `panoramaOnly` mode, the method still returns without loading the splat (no panorama, no splat).
- If `pano_url` is a WebP URL and `thumbnail_url` is null/empty, `DownloadTextureWithFallbackAsync` will throw (no valid fallback URL) and `onPanoramaDownloadFailed` will fire — even though a `pano_url` was present.
- In normal mode (non-`panoramaOnly`), if the pano download fails, `onPanoramaDownloadFailed` fires but the splat load **continues** regardless.
- If `panoramaOnly` is true and the pano download fails, `onPanoramaDownloadFailed` fires and the method returns — no splat fallback.
- `onSplatReady` fires only on successful `LoadWorldAsync` completion. If the load throws, the exception is logged and `onSplatReady` is not invoked (panorama sphere stays up).
- If the card is rebound to a different world while the pano download is in flight, the download result is discarded (no event fired) and the method returns immediately.
- In `panoramaOnly` mode, `LoadWorldAsync` is never called so the world is never registered in `_loadedWorlds`. The unload branch (`IsWorldLoaded == true`) will never fire for that card. The panorama sphere persists indefinitely — the caller is responsible for hiding it if needed (e.g. via `onPanoramaDownloadFailed` wiring or external teardown).

---

## Part 4: Inspector wiring (Editor setup)

After implementation, wire the events in the Unity Inspector:

| Event | Target | Method |
|-------|--------|--------|
| `onPanoramaTextureReady` | `ThumbnailSkyboxController` | `Show` (dynamic — passes `Texture2D`) |
| `onSplatReady` | `ThumbnailSkyboxController` | `StartFadeOut` |
| `onPanoramaDownloadFailed` | *(optional)* | any fallback handler |

**Timing controls** are already exposed on `ThumbnailSkyboxController` as serialized fields: `expandDuration` and `fadeOutDuration` (seconds) control animation speed; `expandStartScale` and `expandTargetScale` control sphere size. No new fields needed.

---

## Known Limitations

- Panorama download is **sequential** before the SPZ load (not parallel). This is intentional: the panorama image is small and appears quickly, giving the user something to look at before the splat arrives.
- In `panoramaOnly` mode the sphere stays up indefinitely. There is no auto-fade or timeout.
- `ThumbnailSkyboxController.Show()` takes ownership of its texture and destroys it on the next `Show()` call, after `StartFadeOut()` completes, or when the component's `OnDestroy` fires. `HandleClick()` downloads the panorama independently from `DownloadPanorama()` (which populates the card thumbnail), so the two textures are separate objects even though they come from the same URL — `ThumbnailSkyboxController` destroying its copy does not affect the card's `panoramaImage.texture`. A future optimisation could share the download, but sharing the object would introduce a destroy-corruption risk and is out of scope here.
