# Panorama Sphere Preview Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the skybox-only panorama preview in `ThumbnailSkyboxController` with a sphere that expands from scale 0.5 → 500, with the skybox path kept as an automatic fallback.

**Architecture:** All changes are in a single file — `ThumbnailSkyboxController.cs`. `Show()` now tries `Sprites/Default` first (sphere path); if that shader is missing it falls back to the existing `Skybox/Panoramic` path unchanged. A new `ExpandCoroutine` animates scale; `FadeOutCoroutine` and `StartFadeOut` branch on `_sphereMode` to handle the two paths. The sphere `GameObject` is created once and reused across generations via `SetActive`.

**Tech Stack:** Unity C#, `PrimitiveType.Sphere`, `Sprites/Default` shader, `MeshFilter.mesh` (instance copy for normal flip), `RenderSettings.skybox` (fallback only)

**Spec:** `docs/superpowers/specs/2026-03-26-panorama-sphere-preview-design.md`

---

## File Map

| Action | File | Purpose |
|--------|------|---------|
| **Modify** | `Assets/App/Scripts/Direct/ThumbnailSkyboxController.cs` | Full rewrite to add sphere path |

---

## Task 1: Rewrite `ThumbnailSkyboxController`

**Files:**
- Modify: `Assets/App/Scripts/Direct/ThumbnailSkyboxController.cs`

This is a single-file refactor where all changes are tightly coupled (Show calls ExpandCoroutine, StartFadeOut calls FadeOutCoroutine which branches on _sphereMode, OnDestroy touches both paths). The safest approach is a clean full-file rewrite verified in one step.

- [ ] **Step 1: Replace the file contents**

Replace `Assets/App/Scripts/Direct/ThumbnailSkyboxController.cs` with:

```csharp
using System.Collections;
using UnityEngine;
using UnityEngine.Events;

namespace Holodeck.Direct
{
    public sealed class ThumbnailSkyboxController : MonoBehaviour
    {
        [Header("Thumbnail Skybox")]
        [SerializeField] private float fadeOutDuration = 1.5f;
        [SerializeField] private UnityEvent onThumbnailShown;

        [Header("Panorama Sphere")]
        [SerializeField] private Transform sphereOrigin;
        [SerializeField] private float expandDuration = 1.5f;
        [SerializeField] private float expandStartScale = 0.5f;
        [SerializeField] private float expandTargetScale = 500f;

        // Skybox fallback state
        private Material _skyboxMaterial;
        private Material _previousSkybox;
        private Coroutine _fadeCoroutine;
        private bool _isShowing;

        // Sphere state
        private GameObject _sphereGO;
        private Material _sphereMaterial;
        private Coroutine _expandCoroutine;
        private bool _sphereMode;

        // Shared state (used by both sphere and skybox paths)
        private Texture2D _thumbnailTexture;

        private static readonly int ExposureId = Shader.PropertyToID("_Exposure");
        private static readonly int TexId      = Shader.PropertyToID("_Tex");
        private static readonly int ColorId    = Shader.PropertyToID("_Color");
        private static readonly int MainTexId  = Shader.PropertyToID("_MainTex");

        /// <summary>
        /// Displays the panorama on the inside of a sphere that expands from expandStartScale to
        /// expandTargetScale. Falls back to RenderSettings.skybox if Sprites/Default is missing.
        /// Takes ownership of tex — do not destroy it after calling this.
        /// </summary>
        public void Show(Texture2D tex)
        {
            // Stop any in-progress coroutines.
            if (_fadeCoroutine != null)
            {
                StopCoroutine(_fadeCoroutine);
                _fadeCoroutine = null;
            }
            if (_expandCoroutine != null)
            {
                StopCoroutine(_expandCoroutine);
                _expandCoroutine = null;
            }

            // Hide sphere before tearing down its material to avoid one-frame destroyed-texture display.
            if (_sphereGO != null)
                _sphereGO.SetActive(false);

            // Tear down previous materials and texture.
            if (_sphereMaterial != null)
            {
                Destroy(_sphereMaterial);
                _sphereMaterial = null;
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

            Shader spritesShader = Shader.Find("Sprites/Default");
            if (spritesShader != null)
            {
                // ── Sphere path ──────────────────────────────────────────────
                if (_sphereGO == null)
                {
                    _sphereGO = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    _sphereGO.name = "PanoramaSphere";

                    // Flip normals + reverse winding so inside faces are visible.
                    Mesh mesh = _sphereGO.GetComponent<MeshFilter>().mesh; // instance copy
                    Vector3[] normals = mesh.normals;
                    for (int i = 0; i < normals.Length; i++)
                        normals[i] = -normals[i];
                    mesh.normals = normals;

                    int[] tris = mesh.triangles;
                    for (int i = 0; i < tris.Length; i += 3)
                    {
                        int tmp = tris[i];
                        tris[i] = tris[i + 2];
                        tris[i + 2] = tmp;
                    }
                    mesh.triangles = tris;

                    Destroy(_sphereGO.GetComponent<SphereCollider>());
                }

                _sphereMaterial = new Material(spritesShader);
                _sphereMaterial.SetTexture(MainTexId, tex);
                _sphereMaterial.SetColor(ColorId, Color.white);
                _sphereGO.GetComponent<MeshRenderer>().material = _sphereMaterial;
                _thumbnailTexture = tex;

                _sphereGO.transform.position = sphereOrigin != null ? sphereOrigin.position : Vector3.zero;
                _sphereGO.transform.localScale = Vector3.one * expandStartScale;
                _sphereGO.SetActive(true);

                _expandCoroutine = StartCoroutine(ExpandCoroutine());
                _sphereMode = true;
                _isShowing = true;

                Debug.Log($"[ThumbnailSkyboxController] Panorama sphere shown. Texture={tex.width}x{tex.height}", this);
                onThumbnailShown?.Invoke();
            }
            else
            {
                // ── Skybox fallback ──────────────────────────────────────────
                Debug.LogError("[ThumbnailSkyboxController] Sprites/Default shader not found — falling back to skybox.", this);
                _sphereMode = false;

                Shader panoramicShader = Shader.Find("Skybox/Panoramic");
                if (panoramicShader == null)
                {
                    Debug.LogError("[ThumbnailSkyboxController] Skybox/Panoramic shader not found. Add it to Graphics Settings > Always Included Shaders.", this);
                    return;
                }

                if (!_isShowing)
                    _previousSkybox = RenderSettings.skybox;
                _thumbnailTexture = tex;

                _skyboxMaterial = new Material(panoramicShader);
                _skyboxMaterial.SetTexture(TexId, tex);
                _skyboxMaterial.SetFloat(ExposureId, 1.0f);

                RenderSettings.skybox = _skyboxMaterial;
                DynamicGI.UpdateEnvironment();

                _isShowing = true;
                Debug.Log($"[ThumbnailSkyboxController] Panorama skybox shown (fallback). Texture={tex.width}x{tex.height}", this);
                onThumbnailShown?.Invoke();
            }
        }

        /// <summary>
        /// Fades the panorama out (sphere alpha or skybox exposure) and restores state.
        /// No-op if Show() was never successfully called.
        /// </summary>
        public void StartFadeOut()
        {
            if (!_isShowing)
                return;

            // Stop expand before fade so they don't fight over localScale.
            if (_expandCoroutine != null)
            {
                StopCoroutine(_expandCoroutine);
                _expandCoroutine = null;
            }

            if (_fadeCoroutine != null)
                StopCoroutine(_fadeCoroutine);

            _fadeCoroutine = StartCoroutine(FadeOutCoroutine());
        }

        private IEnumerator ExpandCoroutine()
        {
            float elapsed = 0f;

            while (elapsed < expandDuration && _sphereGO != null)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / expandDuration);
                _sphereGO.transform.localScale = Vector3.one * Mathf.Lerp(expandStartScale, expandTargetScale, t);
                yield return null;
            }

            if (_sphereGO != null)
                _sphereGO.transform.localScale = Vector3.one * expandTargetScale;

            _expandCoroutine = null;
        }

        private IEnumerator FadeOutCoroutine()
        {
            float elapsed = 0f;

            if (_sphereMode)
            {
                while (elapsed < fadeOutDuration && _sphereMaterial != null)
                {
                    elapsed += Time.deltaTime;
                    float t = Mathf.Clamp01(elapsed / fadeOutDuration);
                    Color c = _sphereMaterial.GetColor(ColorId);
                    c.a = Mathf.Lerp(1f, 0f, t);
                    _sphereMaterial.SetColor(ColorId, c);
                    yield return null;
                }

                if (_sphereGO != null)
                    _sphereGO.SetActive(false);

                if (_sphereMaterial != null)
                {
                    Destroy(_sphereMaterial);
                    _sphereMaterial = null;
                }
            }
            else
            {
                while (elapsed < fadeOutDuration && _skyboxMaterial != null)
                {
                    elapsed += Time.deltaTime;
                    float t = Mathf.Clamp01(elapsed / fadeOutDuration);
                    _skyboxMaterial.SetFloat(ExposureId, Mathf.Lerp(1f, 0f, t));
                    yield return null;
                }

                RenderSettings.skybox = _previousSkybox;
                DynamicGI.UpdateEnvironment();

                if (_skyboxMaterial != null)
                {
                    Destroy(_skyboxMaterial);
                    _skyboxMaterial = null;
                }
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
            if (_expandCoroutine != null)
            {
                StopCoroutine(_expandCoroutine);
                _expandCoroutine = null;
            }

            if (_fadeCoroutine != null)
            {
                StopCoroutine(_fadeCoroutine);
                _fadeCoroutine = null;
            }

            if (_sphereMaterial != null)
            {
                Destroy(_sphereMaterial);
                _sphereMaterial = null;
            }

            if (_thumbnailTexture != null)
            {
                Destroy(_thumbnailTexture);
                _thumbnailTexture = null;
            }

            if (_sphereGO != null)
            {
                Destroy(_sphereGO.GetComponent<MeshFilter>().mesh);
                Destroy(_sphereGO);
                _sphereGO = null;
            }

            // Only restore skybox if the skybox fallback path was active.
            if (!_sphereMode && _isShowing)
            {
                RenderSettings.skybox = _previousSkybox;
                DynamicGI.UpdateEnvironment();
            }

            if (_skyboxMaterial != null)
            {
                Destroy(_skyboxMaterial);
                _skyboxMaterial = null;
            }
        }
    }
}
```

- [ ] **Step 2: Verify compilation**

Switch to the Unity Editor. Check the Console for compile errors.

Expected: no errors. The existing `VoiceToWorldLabsPluginCoordinator` calls `Show()` and `StartFadeOut()` — both signatures are unchanged, so no other file needs updating.

- [ ] **Step 3: Commit**

```bash
git add Assets/App/Scripts/Direct/ThumbnailSkyboxController.cs
git commit -m "feat: show panorama on inside of expanding sphere with skybox fallback"
```

---

## Task 2: Wire Sphere Origin and verify end-to-end

- [ ] **Step 1: Optionally set Sphere Origin in the Inspector**

Select the GameObject that has `ThumbnailSkyboxController`. In the Inspector, the new **Panorama Sphere** section now appears with fields:
- `Sphere Origin` — drag any Transform here to control where the sphere appears; leave empty to use world origin (0, 0, 0)
- `Expand Duration` — default 1.5s
- `Expand Start Scale` — default 0.5
- `Expand Target Scale` — default 500

- [ ] **Step 2: Verify Sprites/Default is not stripped (Meta Quest only)**

If targeting Meta Quest: go to **Edit → Project Settings → Graphics → Always Included Shaders**. Check if `Sprites/Default` is in the list. If not, add it to prevent shader stripping in the Android build.

For Editor/PC testing this step can be skipped — the shader is always available in the Editor.

- [ ] **Step 3: Run an end-to-end test in Play mode**

Press Play. Press Space (or whatever wakes the coordinator), say a world prompt, press Space again to trigger generation.

Expected sequence in the Console and scene:
1. `[VoiceToWorldLabsPluginCoordinator] pano_url missing from generation response — re-fetching world '...'` (if pano_url was empty)
2. `[VoiceToWorldLabsPluginCoordinator] Panorama downloaded (WxH). thumbnailSkybox=assigned`
3. `[ThumbnailSkyboxController] Panorama sphere shown. Texture=WxH` — sphere appears at scale 0.5 and expands outward
4. Gaussian splat loads in the scene
5. `[ThumbnailSkyboxController]` sphere fades out (alpha 1→0) over ~1.5 seconds

**If sphere never appears:**
- Check Console for `Sprites/Default shader not found` → follow Step 2
- Check `thumbnailSkybox=NULL` in the coordinator log → `ThumbnailSkyboxController` is not wired in the coordinator's Inspector

**If panorama is black or wrong:**
- Check that `pano_url` in the re-fetch log is non-empty and is a JPEG/PNG (not WebP)
- If still WebP, the download will fault and the warning `Panorama download failed` appears — the skybox preview is skipped non-fatally

**If the sphere is visible but inside-out (solid sphere surface, not panorama inside):**
- The normal flip did not take effect — verify the mesh instance was obtained via `meshFilter.mesh` (not `sharedMesh`)
