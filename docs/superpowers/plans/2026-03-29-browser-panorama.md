# Browser-Path Panorama Sphere Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add panorama-sphere support to the browser world-loading path in `WorldBrowserController`, including a panorama-only mode (no splat) and Inspector-wired events for panorama ready/failed/splat-ready.

**Architecture:** All changes are in `WorldBrowserController.cs`. New `UnityEvent` fields on `WorldBrowserController` are wired in the Inspector to `ThumbnailSkyboxController.Show` and `StartFadeOut` — avoiding a circular assembly dependency (the WorldLabs assembly cannot reference Assembly-CSharp directly). `WorldCardUI.Bind()` receives the events as `Action` delegates; `HandleClick()` is rewritten to download the panorama, optionally stop there (panorama-only mode), or continue to load the splat.

**Tech Stack:** Unity C#, `UnityEngine.Events`, `System` (Action delegates), existing `WorldLabsClientExtensions.DownloadTextureWithFallbackAsync`

**Spec:** `docs/superpowers/specs/2026-03-29-browser-panorama-design.md`

---

## File Map

| Action | File | Purpose |
|--------|------|---------|
| **Modify** | `Packages/com.worldlabs.gaussian-splatting/Runtime/WorldLabs/WorldBrowserController.cs` | Add `using UnityEngine.Events;`, new panorama Inspector fields on `WorldBrowserController`, new private fields + extended `Bind()` on `WorldCardUI`, rewritten `HandleClick()` |

---

## Task 1: Add `using` and Inspector fields to `WorldBrowserController`

**Files:**
- Modify: `Packages/com.worldlabs.gaussian-splatting/Runtime/WorldLabs/WorldBrowserController.cs:1–63`

### Context

The file currently has these usings (lines 3–9):
```csharp
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using WorldLabs.API;
```

`UnityEvent<Texture2D>` lives in `UnityEngine.Events` — not currently imported. Add it after `using UnityEngine.UI;`.

The last Inspector header block ends at line 62 (`cardImageHeight`), immediately before the `// ── State ──` comment at line 64. Add the new panorama header group between them.

### Steps

- [ ] **Step 1: Add `using UnityEngine.Events;`**

Insert after `using UnityEngine.UI;` (line 8):

```csharp
using UnityEngine.Events;
```

The using block should then read:
```csharp
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.Events;
using WorldLabs.API;
```

- [ ] **Step 2: Add the Panorama Sphere Inspector header**

Insert after the closing `cardImageHeight` field (line 62) and before the `// ── State ──` comment (line 64):

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

- [ ] **Step 3: Verify compilation**

Switch to Unity Editor and check the Console for compile errors.

Expected: no errors. If `UnityEvent<Texture2D>` is not found, confirm `using UnityEngine.Events;` was added.

- [ ] **Step 4: Commit**

```bash
git add Packages/com.worldlabs.gaussian-splatting/Runtime/WorldLabs/WorldBrowserController.cs
git commit -m "feat: add panorama sphere Inspector fields to WorldBrowserController"
```

---

## Task 2: Extend `WorldCardUI` — new fields, extended `Bind()`, updated call site

**Files:**
- Modify: `Packages/com.worldlabs.gaussian-splatting/Runtime/WorldLabs/WorldBrowserController.cs:848–908`

### Context

`WorldCardUI` is an inner class starting at line 832. Its existing private fields (lines 848–849):

```csharp
World _world;
WorldLabsWorldManager _manager;
```

`Bind()` currently at line 883:

```csharp
public void Bind(World world, WorldLabsWorldManager manager)
{
    _world   = world;
    _manager = manager;
    ...
```

The call site is at line 325 of `WorldBrowserController`:

```csharp
card.Bind(worlds[i], worldManager);
```

### Steps

- [ ] **Step 1: Add three new private fields to `WorldCardUI`**

Insert immediately after the existing `WorldLabsWorldManager _manager;` line (line 849):

```csharp
        Action<Texture2D> _onPanoramaReady;
        Action            _onSplatReady;
        Action            _onPanoramaFailed;
        bool              _panoramaOnly;
```

- [ ] **Step 2: Extend `Bind()` signature and store new params**

Replace:

```csharp
        public void Bind(World world, WorldLabsWorldManager manager)
        {
            _world   = world;
            _manager = manager;
```

With:

```csharp
        public void Bind(
            World world,
            WorldLabsWorldManager manager,
            Action<Texture2D> onPanoramaReady,
            Action onSplatReady,
            Action onPanoramaFailed,
            bool panoramaOnly)
        {
            _world            = world;
            _manager          = manager;
            _onPanoramaReady  = onPanoramaReady;
            _onSplatReady     = onSplatReady;
            _onPanoramaFailed = onPanoramaFailed;
            _panoramaOnly     = panoramaOnly;
```

- [ ] **Step 3: Update the call site in `WorldBrowserController`**

Replace (line 325):

```csharp
                card.Bind(worlds[i], worldManager);
```

With:

```csharp
                card.Bind(
                    worlds[i],
                    worldManager,
                    tex => onPanoramaTextureReady?.Invoke(tex),
                    ()  => onSplatReady?.Invoke(),
                    ()  => onPanoramaDownloadFailed?.Invoke(),
                    panoramaOnly);
```

- [ ] **Step 4: Verify compilation**

Switch to Unity Editor and check the Console for compile errors.

Expected: no errors. If `Action<Texture2D>` is not found, confirm `using System;` is present (it is, at line 3).

- [ ] **Step 5: Commit**

```bash
git add Packages/com.worldlabs.gaussian-splatting/Runtime/WorldLabs/WorldBrowserController.cs
git commit -m "feat: extend WorldCardUI.Bind with panorama callbacks and panoramaOnly flag"
```

---

## Task 3: Rewrite `WorldCardUI.HandleClick()`

**Files:**
- Modify: `Packages/com.worldlabs.gaussian-splatting/Runtime/WorldLabs/WorldBrowserController.cs:956–981`

### Context

The current `HandleClick()` body (lines 956–981):

```csharp
        async void HandleClick()
        {
            Debug.Log($"[WorldCardUI] Click: '{_world?.display_name}'");

            if (_world == null || _manager == null) return;
            if (_manager.IsWorldLoading(_world.world_id)) return;

            if (_manager.IsWorldLoaded(_world.world_id))
            {
                _manager.UnloadWorld(_world.world_id);
                RefreshState();
            }
            else
            {
                RefreshState();
                try
                {
                    await _manager.LoadWorldAsync(_world);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[WorldCardUI] Load failed '{_world.display_name}': {ex.Message}");
                }
                RefreshState();
            }
        }
```

### Steps

- [ ] **Step 1: Replace `HandleClick()` body**

Replace the entire method above with:

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

- [ ] **Step 2: Verify compilation**

Switch to Unity Editor and check the Console for compile errors.

Expected: no errors. Common issues:
- `WorldLabsClientExtensions` not found → confirm `using WorldLabs.API;` is at the top of the file (it is, at line 9)
- `DownloadTextureWithFallbackAsync` not found → confirm it exists in `WorldLabsClientExtensions.cs` in the package

- [ ] **Step 3: Commit**

```bash
git add Packages/com.worldlabs.gaussian-splatting/Runtime/WorldLabs/WorldBrowserController.cs
git commit -m "feat: rewrite WorldCardUI.HandleClick to add panorama sphere and panorama-only mode"
```

---

## Task 4: Wire events in the Unity Editor and verify

**Files:**
- No code changes — Inspector wiring only

### Steps

- [ ] **Step 1: Wire `onPanoramaTextureReady`**

In the Unity Inspector, select the GameObject that has `WorldBrowserController`.

Under `Panorama Sphere → On Panorama Texture Ready`:
- Click `+` to add a listener
- Drag the `ThumbnailSkyboxController` component into the object slot
- In the function dropdown, select `ThumbnailSkyboxController → Show` under **Dynamic Texture2D** (not Static — you need the dynamic section so the downloaded texture is passed through)

- [ ] **Step 2: Wire `onSplatReady`**

Under `Panorama Sphere → On Splat Ready`:
- Click `+`
- Drag `ThumbnailSkyboxController` into the object slot
- Select `ThumbnailSkyboxController → StartFadeOut`

- [ ] **Step 3: Verify panorama + splat mode in Play mode**

Press Play. Click a world card in the browser.

Expected sequence:
1. Panorama sphere expands (controlled by `ThumbnailSkyboxController.expandDuration`)
2. Splat loads in the background while you view the panorama
3. Once splat is ready, `onSplatReady` fires → `StartFadeOut()` → panorama fades out (controlled by `fadeOutDuration`)
4. Splat renderer is visible

- [ ] **Step 4: Verify panorama-only mode**

Tick `Panorama Only` on `WorldBrowserController` in the Inspector. Press Play. Click a world card.

Expected:
- Panorama sphere expands
- No splat is downloaded or rendered
- Sphere stays up indefinitely

Untick `Panorama Only` after verifying.

- [ ] **Step 5: Verify timing controls**

With panorama + splat mode active, adjust `expandDuration` and `fadeOutDuration` on `ThumbnailSkyboxController` and re-run to confirm the timings are respected.
