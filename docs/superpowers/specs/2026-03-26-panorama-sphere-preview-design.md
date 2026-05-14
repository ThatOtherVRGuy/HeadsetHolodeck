# Panorama Sphere Preview

**Date:** 2026-03-26
**Status:** Approved

## Overview

Replace the skybox-only preview in `ThumbnailSkyboxController` with a sphere-based approach: the equirectangular panorama is applied to the inside of a sphere that expands from scale 0.5 to 500 over a configurable duration. The existing skybox path remains as an automatic fallback when the required shader is unavailable.

## Goals

- Display the panorama on the inside of a sphere while the gaussian splat loads
- Animate the sphere expanding from a seed point (scale 0.5 → 500) for a reveal effect
- Fade the sphere out when the splat is ready
- Fall back to the existing `RenderSettings.skybox` path if `Sprites/Default` shader is missing
- Reuse the sphere GameObject across multiple generations (disable, not destroy)

## Scope

Single file: `Assets/App/Scripts/Direct/ThumbnailSkyboxController.cs`. No changes to `VoiceToWorldLabsPluginCoordinator` or any other file.

---

## Changes to `ThumbnailSkyboxController`

### Shader

The sphere path uses `Sprites/Default`. This shader is built-in, always available, unlit, and exposes both `_MainTex` (texture) and `_Color` (tint + alpha). The alpha channel of `_Color` controls transparency, making it suitable for the alpha fade-out. `Unlit/Transparent` must not be used — it has no `_Color` property and its alpha fade would silently no-op.

The skybox fallback continues to use `Skybox/Panoramic` unchanged.

### New serialized fields

Under a new `[Header("Panorama Sphere")]` group:

```csharp
[SerializeField] private Transform sphereOrigin;         // world position; defaults to Vector3.zero if null
[SerializeField] private float expandDuration = 1.5f;
[SerializeField] private float expandStartScale = 0.5f;
[SerializeField] private float expandTargetScale = 500f;
```

Existing fields (`fadeOutDuration`, `onThumbnailShown`, and the `[Header("Thumbnail Skybox")]` group) are unchanged.

### New private state

```csharp
private GameObject _sphereGO;       // created once on first Show(), reused thereafter
private Material _sphereMaterial;   // new instance each Show()
private Coroutine _expandCoroutine;
private bool _sphereMode;           // true = sphere active; false = skybox fallback active
```

Existing state (`_skyboxMaterial`, `_previousSkybox`, `_thumbnailTexture`, `_fadeCoroutine`, `_isShowing`, `ExposureId`, `TexId`) is unchanged.

### Cached shader property IDs

Add alongside existing `ExposureId` / `TexId`:

```csharp
private static readonly int ColorId  = Shader.PropertyToID("_Color");
private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
```

---

### `Show(Texture2D tex)` — revised

1. Stop `_fadeCoroutine` (if running) and `_expandCoroutine` (if running); null both.
2. If `_sphereGO != null`, call `_sphereGO.SetActive(false)` — hides any in-progress sphere before tearing down its material, preventing one-frame display of a destroyed texture.
3. Destroy `_sphereMaterial` if non-null; null it. Destroy `_thumbnailTexture` if non-null; null it.
4. Try `Shader.Find("Sprites/Default")`.

**Sphere path** (shader found):

5. On first call only: instantiate `GameObject.CreatePrimitive(PrimitiveType.Sphere)` as `_sphereGO`. Get its `MeshFilter.mesh` (instance copy — not `sharedMesh`). Flip normals (negate each normal vector) and reverse triangle winding (swap `triangles[i]` ↔ `triangles[i+2]` for each triple). Remove the default `SphereCollider` component. This happens exactly once; subsequent calls reuse `_sphereGO`.
6. Create `new Material(spritesDefaultShader)`. Set `_MainTex = tex` via `MainTexId`. Set `_Color = Color.white` via `ColorId`. Assign to `_sphereGO.GetComponent<MeshRenderer>().material`.
7. Store references: `_sphereMaterial = material`, `_thumbnailTexture = tex`.
8. Position `_sphereGO` at `sphereOrigin != null ? sphereOrigin.position : Vector3.zero`.
9. Set `_sphereGO.transform.localScale = Vector3.one * expandStartScale`.
10. `_sphereGO.SetActive(true)`.
11. Start `_expandCoroutine = StartCoroutine(ExpandCoroutine())`.
12. Set `_sphereMode = true`, `_isShowing = true`.
13. Fire `onThumbnailShown`.

**Skybox fallback** (shader not found):

- Log error: `[ThumbnailSkyboxController] Sprites/Default shader not found — falling back to skybox.`
- Set `_sphereMode = false`.
- Execute existing skybox Show logic unchanged (find `Skybox/Panoramic`, capture `_previousSkybox` if `!_isShowing`, create material, set `_Tex`/`_Exposure`, assign `RenderSettings.skybox`, `DynamicGI.UpdateEnvironment()`, set `_isShowing = true`, fire `onThumbnailShown`).

---

### `ExpandCoroutine()` — new

```
float elapsed = 0f
while (elapsed < expandDuration && _sphereGO != null):
    elapsed += Time.deltaTime
    t = Clamp01(elapsed / expandDuration)
    scale = Lerp(expandStartScale, expandTargetScale, t)
    _sphereGO.transform.localScale = Vector3.one * scale
    yield return null
if _sphereGO != null:
    _sphereGO.transform.localScale = Vector3.one * expandTargetScale
_expandCoroutine = null
```

---

### `StartFadeOut()` — revised

Was: no-op if `!_isShowing`, stop `_fadeCoroutine`, start `FadeOutCoroutine`.

Now also: stop `_expandCoroutine` (if running) and null it before starting `FadeOutCoroutine`. This prevents a one-frame scale conflict when fade is triggered while expand is still running.

```
if (!_isShowing) return
if (_expandCoroutine != null):
    StopCoroutine(_expandCoroutine)
    _expandCoroutine = null
if (_fadeCoroutine != null):
    StopCoroutine(_fadeCoroutine)
_fadeCoroutine = StartCoroutine(FadeOutCoroutine())
```

---

### `FadeOutCoroutine()` — revised

Branches on `_sphereMode`:

**Sphere branch:**

1. Lerp `_sphereMaterial` color alpha from 1 → 0 over `fadeOutDuration` seconds. Each frame: read `_sphereMaterial.GetColor(ColorId)`, set `a = Lerp(1, 0, t)`, write back via `_sphereMaterial.SetColor(ColorId, c)`.
2. `_sphereGO.SetActive(false)`.
3. `Destroy(_sphereMaterial)`. `_sphereMaterial = null`.
4. `Destroy(_thumbnailTexture)`. `_thumbnailTexture = null`.
5. `_isShowing = false`. `_fadeCoroutine = null`.

**Skybox branch:**

Existing code unchanged (lerp `_Exposure` 1→0, restore `RenderSettings.skybox`, `DynamicGI.UpdateEnvironment()`, destroy `_skyboxMaterial` and `_thumbnailTexture`, clear `_isShowing`, clear `_fadeCoroutine`).

---

### `OnDestroy()` — revised

1. Stop `_expandCoroutine` if non-null; null it.
2. Stop `_fadeCoroutine` if non-null; null it.
3. Destroy `_sphereMaterial` if non-null; null it.
4. Destroy `_thumbnailTexture` if non-null; null it.
5. If `_sphereGO != null`: destroy the instanced mesh (`Destroy(_sphereGO.GetComponent<MeshFilter>().mesh)`) before destroying the GO — the instanced mesh created by `.mesh` is not auto-destroyed with the GO. Then `Destroy(_sphereGO)`. Null `_sphereGO`.
6. If `!_sphereMode && _isShowing`: restore `RenderSettings.skybox = _previousSkybox`; `DynamicGI.UpdateEnvironment()`.
7. Destroy `_skyboxMaterial` if non-null; null it.

---

## Wiring

No new Inspector wiring required in `VoiceToWorldLabsPluginCoordinator`. Optionally assign `Sphere Origin` in the `ThumbnailSkyboxController` Inspector to control where the sphere appears; leave unassigned for world origin.

`Sprites/Default` is a built-in Unity shader and does not need to be added to Always Included Shaders for PC/Editor builds. On Android (Meta Quest), verify that shader stripping settings do not exclude it — if it is stripped, the controller will silently fall back to the skybox path at runtime. To be safe, add `Sprites/Default` to **Edit → Project Settings → Graphics → Always Included Shaders**.

## Out of Scope

- Easing curves for the expand animation (linear lerp is sufficient)
- Scale-down animation on fade-out (alpha fade is sufficient)
- Multiple simultaneous panorama spheres
