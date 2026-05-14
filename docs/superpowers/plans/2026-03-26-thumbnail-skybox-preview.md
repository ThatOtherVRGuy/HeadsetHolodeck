# Thumbnail Skybox Preview Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Display the World Labs thumbnail as a skybox while the gaussian splat loads, then fade it out when the splat is ready.

**Architecture:** A new `ThumbnailSkyboxController` MonoBehaviour owns the full skybox lifecycle (show, fade, cleanup). The existing `VoiceToWorldLabsPluginCoordinator` downloads the thumbnail after world generation and delegates all skybox work to the controller. An `_isShowing` flag gates all restore operations so `OnDestroy` and `FadeOutCoroutine` never overwrite `RenderSettings.skybox` unless `Show()` actually succeeded.

**Tech Stack:** Unity C#, `RenderSettings.skybox`, `Skybox/Panoramic` shader, `WorldLabsClientExtensions.DownloadThumbnailAsync`

**Spec:** `docs/superpowers/specs/2026-03-26-thumbnail-skybox-preview-design.md`

---

## File Map

| Action | File | Purpose |
|--------|------|---------|
| **Create** | `Assets/App/Scripts/Direct/ThumbnailSkyboxController.cs` | Owns skybox material lifecycle, Show/fade/restore |
| **Modify** | `Assets/App/Scripts/Direct/VoiceToWorldLabsPluginCoordinator.cs` | Add thumbnail download + delegate to controller |

---

## Task 1: Create `ThumbnailSkyboxController`

**Files:**
- Create: `Assets/App/Scripts/Direct/ThumbnailSkyboxController.cs`

- [ ] **Step 1: Create the script**

Create `Assets/App/Scripts/Direct/ThumbnailSkyboxController.cs`:

```csharp
using System.Collections;
using UnityEngine;

namespace Holodeck.Direct
{
    public sealed class ThumbnailSkyboxController : MonoBehaviour
    {
        [Header("Thumbnail Skybox")]
        [SerializeField] private float fadeOutDuration = 1.5f;

        private Material _skyboxMaterial;
        private Material _previousSkybox;
        private Texture2D _thumbnailTexture;
        private Coroutine _fadeCoroutine;
        private bool _isShowing;

        /// <summary>
        /// Displays the texture as the active skybox.
        /// Takes ownership of tex — do not destroy it after calling this.
        /// </summary>
        public void Show(Texture2D tex)
        {
            if (_fadeCoroutine != null)
            {
                StopCoroutine(_fadeCoroutine);
                _fadeCoroutine = null;
            }

            if (_skyboxMaterial != null)
            {
                Destroy(_skyboxMaterial);
                _skyboxMaterial = null;
            }

            if (_thumbnailTexture != null)
            {
                Destroy(_thumbnailTexture);
                _thumbnailTexture = null;
            }

            Shader panoramicShader = Shader.Find("Skybox/Panoramic");
            if (panoramicShader == null)
            {
                Debug.LogError("[ThumbnailSkyboxController] Skybox/Panoramic shader not found. Add it to Graphics Settings > Always Included Shaders.", this);
                return;
            }

            _previousSkybox = RenderSettings.skybox;
            _thumbnailTexture = tex;

            _skyboxMaterial = new Material(panoramicShader);
            _skyboxMaterial.SetTexture("_Tex", tex);
            _skyboxMaterial.SetFloat("_Exposure", 1.0f);

            RenderSettings.skybox = _skyboxMaterial;
            DynamicGI.UpdateEnvironment();

            _isShowing = true;
        }

        /// <summary>
        /// Fades the thumbnail skybox out and restores the previous skybox.
        /// No-op if Show() was never successfully called.
        /// </summary>
        public void StartFadeOut()
        {
            if (!_isShowing)
                return;

            if (_fadeCoroutine != null)
                StopCoroutine(_fadeCoroutine);

            _fadeCoroutine = StartCoroutine(FadeOutCoroutine());
        }

        private IEnumerator FadeOutCoroutine()
        {
            float elapsed = 0f;

            while (elapsed < fadeOutDuration && _skyboxMaterial != null)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / fadeOutDuration);
                _skyboxMaterial.SetFloat("_Exposure", Mathf.Lerp(1f, 0f, t));
                yield return null;
            }

            RenderSettings.skybox = _previousSkybox;
            DynamicGI.UpdateEnvironment();

            if (_skyboxMaterial != null)
            {
                Destroy(_skyboxMaterial);
                _skyboxMaterial = null;
            }

            if (_thumbnailTexture != null)
            {
                Destroy(_thumbnailTexture);
                _thumbnailTexture = null;
            }

            _isShowing = false;
            _fadeCoroutine = null;
        }

        private void OnDestroy()
        {
            if (_fadeCoroutine != null)
            {
                StopCoroutine(_fadeCoroutine);
                _fadeCoroutine = null;
            }

            if (_skyboxMaterial != null)
            {
                Destroy(_skyboxMaterial);
                _skyboxMaterial = null;
            }

            if (_thumbnailTexture != null)
            {
                Destroy(_thumbnailTexture);
                _thumbnailTexture = null;
            }

            if (_isShowing)
            {
                RenderSettings.skybox = _previousSkybox;
                DynamicGI.UpdateEnvironment();
                _isShowing = false;
            }
        }
    }
}
```

- [ ] **Step 2: Verify it compiles**

Switch to Unity Editor. Check the Console for compile errors. Expected: no errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/App/Scripts/Direct/ThumbnailSkyboxController.cs
git commit -m "feat: add ThumbnailSkyboxController for thumbnail skybox preview"
```

---

## Task 2: Add thumbnail download and skybox show to coordinator

**Files:**
- Modify: `Assets/App/Scripts/Direct/VoiceToWorldLabsPluginCoordinator.cs`

- [ ] **Step 1: Add the `thumbnailSkybox` field under `[Header("Dependencies")]`**

In the `[Header("Dependencies")]` block, add the new field after `worldManager`:

```csharp
        [SerializeField] private WorldLabsWorldManager worldManager;
        [SerializeField] private ThumbnailSkyboxController thumbnailSkybox;
```

- [ ] **Step 2: Add thumbnail download block in `RunVoiceToWorldFlow`**

Find this section (after `lastWorldId` is set, before `worldManager.RestoreDefaultWorld()`):

```csharp
            World world = generationTask.Result;
            lastWorldId = world != null ? world.world_id : string.Empty;

            // RestoreDefaultWorld and LoadWorldAsync are called here in the coroutine (main thread)
            // rather than inside the async task, ensuring Unity scene operations are thread-safe.
            worldManager.RestoreDefaultWorld();
```

Replace with:

```csharp
            World world = generationTask.Result;
            lastWorldId = world != null ? world.world_id : string.Empty;

            // Download thumbnail and show as skybox preview while the splat loads.
            // WorldLabsClient does not implement IDisposable — no disposal required.
            Task<Texture2D> thumbnailTask = new WorldLabsClient().DownloadThumbnailAsync(world);
            while (!thumbnailTask.IsCompleted)
            {
                yield return null;
            }

            if (_generationCts == null || _generationCts.IsCancellationRequested)
            {
                // Destroy downloaded texture to prevent memory leak before bailing out.
                if (!thumbnailTask.IsFaulted && !thumbnailTask.IsCanceled && thumbnailTask.Result != null)
                    Destroy(thumbnailTask.Result);
                isBusy = false;
                yield break;
            }

            if (!thumbnailTask.IsFaulted && !thumbnailTask.IsCanceled && thumbnailTask.Result != null)
            {
                thumbnailSkybox?.Show(thumbnailTask.Result);
            }
            else
            {
                Debug.LogWarning("[VoiceToWorldLabsPluginCoordinator] Thumbnail download failed or returned null; skipping skybox preview.", this);
            }

            // RestoreDefaultWorld and LoadWorldAsync are called here in the coroutine (main thread)
            // rather than inside the async task, ensuring Unity scene operations are thread-safe.
            worldManager.RestoreDefaultWorld();
```

- [ ] **Step 3: Verify it compiles**

Switch to Unity Editor. Check the Console for compile errors. Expected: no errors.

- [ ] **Step 4: Commit**

```bash
git add Assets/App/Scripts/Direct/VoiceToWorldLabsPluginCoordinator.cs
git commit -m "feat: download thumbnail and show as skybox preview after world generation"
```

---

## Task 3: Add fade-out after splat loads

**Files:**
- Modify: `Assets/App/Scripts/Direct/VoiceToWorldLabsPluginCoordinator.cs`

- [ ] **Step 1: Add `StartFadeOut()` call after successful load**

Find this block in `RunVoiceToWorldFlow`:

```csharp
            if (logDebugMessages)
            {
                Debug.Log($"World loaded successfully. WorldId={lastWorldId}", this);
            }

            if (capture?.Clip != null)
            {
                Destroy(capture.Clip);
            }
```

Replace with:

```csharp
            if (logDebugMessages)
            {
                Debug.Log($"World loaded successfully. WorldId={lastWorldId}", this);
            }

            if (thumbnailSkybox != null)
                thumbnailSkybox.StartFadeOut();

            if (capture?.Clip != null)
            {
                Destroy(capture.Clip);
            }
```

- [ ] **Step 2: Verify it compiles**

Switch to Unity Editor. Check Console for compile errors. Expected: no errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/App/Scripts/Direct/VoiceToWorldLabsPluginCoordinator.cs
git commit -m "feat: fade out thumbnail skybox after gaussian splat finishes loading"
```

---

## Task 4: Wire and verify in the Editor

- [ ] **Step 1: Add `ThumbnailSkyboxController` to Systems GameObject**

In the Unity scene hierarchy, select the `Systems` GameObject. In the Inspector, click **Add Component** and add `ThumbnailSkyboxController`.

- [ ] **Step 2: Wire the reference in the coordinator**

Select the `Systems` GameObject. Find `VoiceToWorldLabsPluginCoordinator` in the Inspector. Drag the `Systems` GameObject into the **Thumbnail Skybox** field.

- [ ] **Step 3: Ensure `Skybox/Panoramic` shader is included**

Go to **Edit → Project Settings → Graphics**. Scroll to **Always Included Shaders**. If `Skybox/Panoramic` is not listed, click **+** and add it. This prevents the shader from being stripped in builds.

- [ ] **Step 4: Run an end-to-end test**

Press Play. Press Space, say a world prompt (e.g. "a tropical island at sunset"), press Space again. Watch the Console and the scene view.

Expected sequence:
1. Transcription log appears
2. Generation polling logs appear (`[WorldLabs] Poll: done=False`)
3. After generation: skybox changes to the thumbnail equirectangular image
4. Gaussian splat downloads and appears in the scene
5. Skybox fades out over ~1.5 seconds, original skybox (or none) restored

**If skybox never appears:**
- Check Console for `[ThumbnailSkyboxController] Skybox/Panoramic shader not found` → fix via Step 3
- Check Console for `Thumbnail download failed` warning → thumbnail URL may not be populated on the world object yet; check the `World` response payload

**If the splat loads but fade doesn't happen:**
- Confirm `ThumbnailSkyboxController` is wired into the coordinator Inspector field
