# Design: Multi-Model Selection & GLB Mesh Support

**Date:** 2026-04-10  
**Status:** Approved

---

## Overview

Adds four-tier model selection for world generation, automatic GLB collision-mesh download, a new mesh view mode, and voice + UI controls for both features.

---

## 1. Model Enum Expansion

### `WorldLabsModels.cs` — `MarbleModel` enum

Replace the existing two-value enum (`Mini`, `Plus`) with four values:

```csharp
public enum MarbleModel
{
    [InspectorName("Draft  (marble-1.0-draft)")]  Draft,
    [InspectorName("Fast   (marble-1.0)")]         Fast,
    [InspectorName("Standard (marble-1.1)")]       Standard,
    [InspectorName("High   (marble-1.1-plus)")]    High
}
```

### `WorldLabsClient.cs` — `GetModelString()`

```
Draft    → "marble-1.0-draft"
Fast     → "marble-1.0"
Standard → "marble-1.1"
High     → "marble-1.1-plus"
```

### `WorldBrowserController.cs`

- Remove `GenerationModel` enum; replace all usages with `MarbleModel`.
- Rename `creationModel` field to `MarbleModel selectedModel = MarbleModel.Standard`.

### `VoiceToWorldLabsPluginCoordinator.cs`

- Remove `bool useMiniModel`.
- Add `[SerializeField] MarbleModel generationModel = MarbleModel.Standard` (Inspector-visible).
- Pass `generationModel` to `GenerateWorldFromTextAsync`.

---

## 2. `WorldMeshController` (new file)

**Path:** `Assets/App/Command/SpeechIntent/Runtime/WorldMeshController.cs`  
**Namespace:** `SpeechIntent`

### Inspector fields

```csharp
public WorldLabsWorldManager worldManager;
public Transform             splatParent;   // parent transform to copy position/rotation/scale from
```

### `WorldLabsWorldManager` — new property

Add `public World LastLoadedWorld { get; private set; }` and set it inside both `LoadWorldAsync(World world)` and `RegisterExternalWorld(string worldId, GaussianSplatRenderer renderer)`. This ensures voice-triggered and browser-triggered loads both populate the property before `OnWorldLoaded` fires.

### Lifecycle

- `OnEnable` / `OnDisable`: subscribe/unsubscribe `worldManager.OnWorldLoaded` and `worldManager.OnWorldUnloaded`.

### On `OnWorldLoaded(string worldId, GaussianSplatRenderer renderer)`

1. Read `worldManager.LastLoadedWorld?.assets?.mesh?.collider_mesh_url`.
2. If URL is null or empty: log `[WorldMeshController] No collider mesh URL for '{worldId}' — skipping.` and return.
3. Download GLB bytes: `await WorldLabsClientExtensions.DownloadBinaryAsync(url)`.
4. Import with GLTFast into a new child `GameObject("MeshRoot")`:
   ```csharp
   var gltf = new GltfImport();
   await gltf.LoadGltfBinary(bytes);
   await gltf.InstantiateSceneAsync(meshRoot.transform);
   ```
5. Copy `splatParent` transform (position, rotation, localScale) onto `meshRoot`.
6. Walk `meshRoot` hierarchy: for every `MeshFilter` found, add a `MeshCollider(sharedMesh)` to the same GameObject. All `MeshRenderer` components start disabled (visual hidden by default, colliders always active).
7. Store `_gltf` reference for cleanup.

### On `OnWorldUnloaded(string worldId)`

- Destroy `MeshRoot` GameObject.
- Call `_gltf?.Dispose()`.
- Clear `_gltf`, `_meshRoot`.

### Public API

```csharp
bool HasMesh { get; }   // true after successful import
void ShowVisual();      // enable all MeshRenderers in meshRoot; caller hides splat root separately
void HideVisual();      // disable all MeshRenderers in meshRoot; colliders remain active
```

---

## 3. View Mode Extension

### `ViewModeController.cs`

- Add `ViewMode.Mesh` to the existing enum.
- Add Inspector field: `public WorldMeshController worldMeshController`.
- Add `RequestMeshView()`:
  ```
  - Call worldMeshController.ShowVisual()
  - Hide splat world root (HideCurrentWorldRoot())
  - Leave thumbnailSkybox running (no StartFadeOut)
  - Set DesiredMode = ViewMode.Mesh
  ```
- Update `RequestSplatView()` and `RequestPanoView()`: each calls `worldMeshController?.HideVisual()` before their existing logic.
- `TryApply()`: add a `ViewMode.Mesh` branch that re-applies `ShowVisual()` if `HasMesh` is true, or falls back to `ViewMode.Pano` with an error event if mesh is not yet loaded.

---

## 4. Voice Intents

### `VoiceIntentSchemas.cs` — `VoiceIntentType`

```csharp
SetGenerationModel = 16,
ShowMeshWorld      = 17,
```

### `VoiceIntentSchemas.cs` — `VoiceIntentCommand`

New field:
```csharp
[Header("World Generation Model")]
public string generation_model = "";
// Valid values: "draft", "fast", "standard", "high"
```

### `WorldActionDispatcher.cs`

- Add Inspector field: `public WorldMeshController worldMeshController`.
- Add Inspector field: `public VoiceToWorldLabsPluginCoordinator coordinator`.
- Add Inspector field: `public WorldBrowserController worldBrowser` (for UI sync).
- Add two new switch cases:
  - `SetGenerationModel` → `HandleSetGenerationModel(command)`
  - `ShowMeshWorld` → `viewModeController?.RequestMeshView()`

```csharp
void HandleSetGenerationModel(VoiceIntentCommand cmd)
{
    MarbleModel model = cmd.generation_model.ToLower() switch {
        "draft"                => MarbleModel.Draft,
        "fast" or "low"        => MarbleModel.Fast,
        "standard" or "normal" => MarbleModel.Standard,
        "high" or "best"       => MarbleModel.High,
        _                      => MarbleModel.Standard
    };
    coordinator?.SetGenerationModel(model);   // syncs coordinator
    worldBrowser?.SetGenerationModel(model);  // syncs UI label + button highlight
}
```

`VoiceToWorldLabsPluginCoordinator` gets a new public method:
```csharp
public void SetGenerationModel(MarbleModel model)
{
    generationModel = model;
}
```

`WorldBrowserController` also exposes `SetGenerationModel(MarbleModel m)` (already planned for UI) — the dispatcher calls **both** so voice and UI stay in sync.

### `OpenAiSpeechIntentConfig.additionalDeveloperInstructions` additions

```
For 'use high quality', 'best model', 'use draft', 'switch to fast', etc., use intent=SetGenerationModel.
  Set generation_model to one of: draft, fast, standard, high.
  Accept 'low' as alias for 'fast' and 'best'/'premium' as alias for 'high'.
For 'show mesh', 'mesh view', 'show collision mesh', or 'show 3d mesh', use intent=ShowMeshWorld.
```

---

## 5. Model Selector UI

### `WorldBrowserController.cs`

**New Inspector fields:**
```csharp
[Header("Model Selector — leave null to auto-create")]
public GameObject modelSelectorRow;
public Text       currentModelLabel;   // "Model: Standard" shown in the header
```

**Behavior:**
- `SetGenerationModel(MarbleModel m)`: sets `selectedModel`, refreshes button highlight colors on `modelSelectorRow`, updates `currentModelLabel.text`.
- Called by both UI button taps and voice handler.

**Auto-build (in `BuildUI()`):**
- `currentModelLabel` placed in the header bar, right of the page label (small, ~11pt, gray, raycastTarget=false).
- `modelSelectorRow`: a horizontal strip of four `MakeButton`-style buttons (Draft / Fast / Standard / High) auto-built at the top of the create panel, above `PromptLabel`. Active model button uses `highlightedColor`; others use the standard dark color.

**No Dropdown component** — avoids Quest virtual keyboard focus conflicts.

---

## 6. File Change Summary

| File | Change |
|---|---|
| `WorldLabsModels.cs` | Expand `MarbleModel` enum (4 values) |
| `WorldLabsWorldManager.cs` | Add `World LastLoadedWorld` property; set in `LoadWorldAsync` + `RegisterExternalWorld` |
| `WorldLabsClient.cs` | Update `GetModelString()` for new IDs |
| `WorldBrowserController.cs` | Replace `GenerationModel` with `MarbleModel`; add model selector UI; add `SetGenerationModel()` |
| `VoiceToWorldLabsPluginCoordinator.cs` | Replace `useMiniModel` bool with `MarbleModel generationModel`; add `SetGenerationModel()` public method |
| `VoiceIntentSchemas.cs` | Add `SetGenerationModel`, `ShowMeshWorld` intents; add `generation_model` field |
| `WorldActionDispatcher.cs` | Add handlers + new Inspector fields |
| `ViewModeController.cs` | Add `ViewMode.Mesh`, `RequestMeshView()`, wire `WorldMeshController` |
| `OpenAiSpeechIntentConfig.cs` | Append new hint lines to `additionalDeveloperInstructions` |
| `WorldMeshController.cs` | **New file** — GLTFast download, collider, visual toggle |

---

## 7. Dependencies

- **GLTFast** package installed via Unity Package Manager (confirmed by user).
- `collider_mesh_url` present in `MeshAssets` struct (already in codebase).
- No new third-party dependencies beyond GLTFast.

---

## 8. Out of Scope

- High-quality mesh (600k triangle textured variant) — requires a separate async API trigger and up to 1 hour wait; deferred.
- Mesh download progress UI — the status bar already shows text status; a dedicated progress bar is deferred.
- Per-world mesh caching to disk — deferred.
