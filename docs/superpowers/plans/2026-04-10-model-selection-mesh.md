# Multi-Model Selection & GLB Mesh Support Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add four-tier model selection (Draft/Fast/Standard/High) for world generation, automatic GLB collision-mesh download via GLTFast, a mesh view mode, and voice + UI controls for both.

**Architecture:** `MarbleModel` enum expands to four values; a new `WorldMeshController` MonoBehaviour auto-downloads the collider GLB when any world loads, builds `MeshCollider` components, and exposes renderer toggling; `ViewModeController` gains a `Mesh` view mode; voice intents and a button-row UI drive model selection.

**Tech Stack:** Unity 2022+, C#, GLTFast (com.unity.cloud.gltfast or com.atteneder.gltfast), WorldLabs API, OpenAI GPT-4o for speech intent.

---

## File Map

| File | Action |
|---|---|
| `Packages/com.worldlabs.gaussian-splatting/Runtime/WorldLabs/WorldLabsModels.cs` | Expand `MarbleModel` enum |
| `Packages/com.worldlabs.gaussian-splatting/Runtime/WorldLabs/WorldLabsClient.cs` | Update `GetModelString()` |
| `Packages/com.worldlabs.gaussian-splatting/Runtime/WorldLabs/WorldLabsWorldManager.cs` | Add `LastLoadedWorld`; update `RegisterExternalWorld` |
| `Packages/com.worldlabs.gaussian-splatting/Runtime/WorldLabs/WorldBrowserController.cs` | Remove `GenerationModel` enum; add `MarbleModel selectedModel`; add model selector UI; add `SetGenerationModel()` |
| `Assets/App/Scripts/Direct/VoiceToWorldLabsPluginCoordinator.cs` | Replace `useMiniModel` bool with `MarbleModel generationModel`; add `SetGenerationModel()` |
| `Assets/App/Command/SpeechIntent/Runtime/VoiceIntentSchemas.cs` | Add two new intent types; add `generation_model` field |
| `Assets/App/Command/SpeechIntent/Runtime/WorldActionDispatcher.cs` | Add new handlers and inspector fields |
| `Assets/App/Command/SpeechIntent/Runtime/ViewModeController.cs` | Add `ViewMode.Mesh`; add `RequestMeshView()` |
| `Assets/App/Command/SpeechIntent/Runtime/WorldMeshController.cs` | **New file** — GLTFast download, collider build, visual toggle |
| `Assets/App/Command/SpeechIntent/Runtime/OpenAiSpeechIntentConfig.cs` | Append new voice routing hints |

---

## Task 1: Expand MarbleModel enum and update model string mapping

**Files:**
- Modify: `Packages/com.worldlabs.gaussian-splatting/Runtime/WorldLabs/WorldLabsModels.cs`
- Modify: `Packages/com.worldlabs.gaussian-splatting/Runtime/WorldLabs/WorldLabsClient.cs`

> ⚠️ After this task the project will not compile until Task 2 and 3 fix the usages. Treat Tasks 1–3 as one compilation unit.

- [ ] **Step 1: Replace `MarbleModel` enum in `WorldLabsModels.cs`**

Find the existing enum (lines ~23-29):
```csharp
[Serializable]
public enum MarbleModel
{
    [InspectorName("Marble 0.1-mini")]
    Mini,
    [InspectorName("Marble 0.1-plus")]
    Plus
}
```
Replace with:
```csharp
[Serializable]
public enum MarbleModel
{
    [InspectorName("Draft  (marble-1.0-draft)")]  Draft,
    [InspectorName("Fast   (marble-1.0)")]         Fast,
    [InspectorName("Standard (marble-1.1)")]       Standard,
    [InspectorName("High   (marble-1.1-plus)")]    High
}
```

- [ ] **Step 2: Update `WorldsGenerateRequest` default model string in `WorldLabsModels.cs`**

Find in `WorldsGenerateRequest` (~line 578):
```csharp
public string model = "Marble 0.1-plus";
```
Replace with:
```csharp
public string model = "marble-1.1";
```

- [ ] **Step 3: Update `GetModelString()` in `WorldLabsClient.cs`**

Find `GetModelString` (~line 580):
```csharp
private string GetModelString(MarbleModel model)
{
    return model == MarbleModel.Mini ? "Marble 0.1-mini" : "Marble 0.1-plus";
}
```
Replace with:
```csharp
private string GetModelString(MarbleModel model) => model switch
{
    MarbleModel.Draft    => "marble-1.0-draft",
    MarbleModel.Fast     => "marble-1.0",
    MarbleModel.High     => "marble-1.1-plus",
    _                    => "marble-1.1",   // Standard + default
};
```

---

## Task 2: Update WorldBrowserController — replace GenerationModel with MarbleModel

**Files:**
- Modify: `Packages/com.worldlabs.gaussian-splatting/Runtime/WorldLabs/WorldBrowserController.cs`

- [ ] **Step 1: Remove the `GenerationModel` enum**

Find and delete these lines near the top of the file (~line 15):
```csharp
/// <summary>WorldLabs model variant to use for runtime world generation.</summary>
public enum GenerationModel { Plus, Mini }
```

- [ ] **Step 2: Replace the `creationModel` inspector field**

Find (~line 43):
```csharp
[Header("World Creation")]
[Tooltip("Model to use when generating new worlds from a text prompt.\nPlus = higher quality / slower.  Mini = faster generation.")]
public GenerationModel creationModel = GenerationModel.Plus;
```
Replace with:
```csharp
[Header("World Creation")]
[Tooltip("Model to use when generating new worlds from a text prompt.")]
public MarbleModel selectedModel = MarbleModel.Standard;
```

- [ ] **Step 3: Add model button storage field**

After the `_pool` and `_history` fields (~line 90), add:
```csharp
readonly Button[] _modelButtons = new Button[4];
```

- [ ] **Step 4: Add `SetGenerationModel()` method**

Add this method in the `// ── World creation ──` section, before `ToggleCreateMode()`:
```csharp
/// <summary>
/// Sets the active generation model, refreshes model button highlights,
/// and updates the header label. Called by UI taps and voice commands.
/// </summary>
public void SetGenerationModel(MarbleModel m)
{
    selectedModel = m;
    var activeColor   = new Color(0.25f, 0.55f, 1.00f, 1f);
    var inactiveColor = new Color(0.18f, 0.20f, 0.28f, 1f);
    for (int i = 0; i < _modelButtons.Length; i++)
    {
        if (_modelButtons[i] == null) continue;
        var img = _modelButtons[i].GetComponent<Image>();
        if (img != null) img.color = i == (int)m ? activeColor : inactiveColor;
    }
    if (currentModelLabel != null)
    {
        string label = m switch
        {
            MarbleModel.Draft    => "Draft",
            MarbleModel.Fast     => "Fast",
            MarbleModel.Standard => "Standard",
            MarbleModel.High     => "High",
            _                    => m.ToString()
        };
        currentModelLabel.text = $"Model: {label}";
    }
}
```

- [ ] **Step 5: Fix `StartWorldCreation()` — remove old enum usage**

Find in `StartWorldCreation()` (~line 500):
```csharp
string modelStr = creationModel == GenerationModel.Plus
    ? "Marble 0.1-plus"
    : "Marble 0.1-mini";

var request = new WorldsGenerateRequest
{
    world_prompt = TextPrompt.Create(prompt),
    model        = modelStr,
    permission   = Permission.Private
};
```
Replace with:
```csharp
_wlClient ??= new WorldLabsClient();
string modelStr = selectedModel switch
{
    MarbleModel.Draft    => "marble-1.0-draft",
    MarbleModel.Fast     => "marble-1.0",
    MarbleModel.High     => "marble-1.1-plus",
    _                    => "marble-1.1",
};

var request = new WorldsGenerateRequest
{
    world_prompt = TextPrompt.Create(prompt),
    model        = modelStr,
    permission   = Permission.Private
};
```

> Note: The original `StartWorldCreation()` had `_wlClient ??= new WorldLabsClient();` appearing twice — once before and once inside the try block. After this edit there is one at the top of the method. Remove any remaining second occurrence inside the try block so `_wlClient` is only initialised once.

- [ ] **Step 6: Add new inspector fields for model selector**

In the `[Header("Delete UI — leave null to auto-create")]` block, add a new header block below it:
```csharp
[Header("Model Selector — leave null to auto-create")]
[Tooltip("Row of four model-selection buttons (auto-created if null).")]
public GameObject modelSelectorRow;
[Tooltip("Label showing active model in the header (auto-created if null).")]
public Text       currentModelLabel;
```

---

## Task 3: Update VoiceToWorldLabsPluginCoordinator

**Files:**
- Modify: `Assets/App/Scripts/Direct/VoiceToWorldLabsPluginCoordinator.cs`

- [ ] **Step 1: Replace `useMiniModel` bool with `generationModel` field**

Find (~line 23):
```csharp
[SerializeField] private bool useMiniModel = true;
```
Replace with:
```csharp
[SerializeField] private MarbleModel generationModel = MarbleModel.Standard;
```

- [ ] **Step 2: Add `using WorldLabs.API;` if not present**

Check the top of the file — `WorldLabs.API` is already imported (needed for `World`, `MarbleModel`). If missing, add:
```csharp
using WorldLabs.API;
```

- [ ] **Step 3: Add public `SetGenerationModel()` method**

Add after the `IsBusy` property:
```csharp
public void SetGenerationModel(MarbleModel model)
{
    generationModel = model;
}
```

- [ ] **Step 4: Fix `GenerateWorldAsync` — replace old model logic**

Find in `GenerateWorldAsync` (~line 295):
```csharp
MarbleModel model = useMiniModel ? MarbleModel.Mini : MarbleModel.Plus;
```
Replace with:
```csharp
MarbleModel model = generationModel;
```

- [ ] **Step 5: Commit Tasks 1-3 (project should now compile)**

```bash
git add \
  Packages/com.worldlabs.gaussian-splatting/Runtime/WorldLabs/WorldLabsModels.cs \
  Packages/com.worldlabs.gaussian-splatting/Runtime/WorldLabs/WorldLabsClient.cs \
  Packages/com.worldlabs.gaussian-splatting/Runtime/WorldLabs/WorldBrowserController.cs \
  Assets/App/Scripts/Direct/VoiceToWorldLabsPluginCoordinator.cs
git commit -m "feat: expand MarbleModel to four tiers (Draft/Fast/Standard/High)"
```

---

## Task 4: Add `LastLoadedWorld` to `WorldLabsWorldManager`

**Files:**
- Modify: `Packages/com.worldlabs.gaussian-splatting/Runtime/WorldLabs/WorldLabsWorldManager.cs`
- Modify: `Assets/App/Scripts/Direct/VoiceToWorldLabsPluginCoordinator.cs`

- [ ] **Step 1: Add `LastLoadedWorld` property**

In the `// ── Properties ──` section (~line 94), add after `LastNextPageToken`:
```csharp
/// <summary>The most recently started world load. Set at the start of LoadWorldAsync and RegisterExternalWorld.</summary>
public World LastLoadedWorld { get; private set; }
```

- [ ] **Step 2: Set `LastLoadedWorld` in `LoadWorldAsync`**

Find the start of `LoadWorldAsync` (~line 160):
```csharp
public async Task<GaussianSplatRenderer> LoadWorldAsync(World world)
{
    if (world == null) throw new ArgumentNullException(nameof(world));
    string worldId = world.world_id;
```
Add `LastLoadedWorld = world;` after the null check:
```csharp
public async Task<GaussianSplatRenderer> LoadWorldAsync(World world)
{
    if (world == null) throw new ArgumentNullException(nameof(world));
    LastLoadedWorld = world;
    string worldId = world.world_id;
```

- [ ] **Step 3: Add optional `World` param to `RegisterExternalWorld`**

Find the signature (~line 310):
```csharp
public void RegisterExternalWorld(string worldId, GaussianSplatRenderer renderer)
```
Replace with:
```csharp
public void RegisterExternalWorld(string worldId, GaussianSplatRenderer renderer, World world = null)
```
And at the top of the method body, add:
```csharp
if (world != null) LastLoadedWorld = world;
```

- [ ] **Step 4: Pass `world` in coordinator's `RegisterExternalWorld` call**

In `VoiceToWorldLabsPluginCoordinator.cs`, find (~line 250):
```csharp
worldManager.RegisterExternalWorld(lastWorldId, placed.renderer);
```
Replace with:
```csharp
worldManager.RegisterExternalWorld(lastWorldId, placed.renderer, world);
```

- [ ] **Step 5: Commit**

```bash
git add \
  Packages/com.worldlabs.gaussian-splatting/Runtime/WorldLabs/WorldLabsWorldManager.cs \
  Assets/App/Scripts/Direct/VoiceToWorldLabsPluginCoordinator.cs
git commit -m "feat: add LastLoadedWorld to WorldLabsWorldManager"
```

---

## Task 5: Add model selector UI to WorldBrowserController

**Files:**
- Modify: `Packages/com.worldlabs.gaussian-splatting/Runtime/WorldLabs/WorldBrowserController.cs`

- [ ] **Step 1: Add `currentModelLabel` to the header in `BuildUI()`**

Find the header section in `BuildUI()` where `pageLabel` is created:
```csharp
pageLabel = MakeLabel(headerGo.transform, "PageLabel", "Page 1", 12,
    TextAnchor.MiddleRight,
    new Vector2(0.78f, 0), new Vector2(1, 1),
    new Vector2(0, 0), new Vector2(-8, 0));
pageLabel.color = new Color(0.65f, 0.65f, 0.65f, 1f);
```
Replace with (tighter page label + new model label):
```csharp
pageLabel = MakeLabel(headerGo.transform, "PageLabel", "Page 1", 11,
    TextAnchor.MiddleRight,
    new Vector2(0.72f, 0), new Vector2(0.87f, 1),
    new Vector2(0, 0), new Vector2(0, 0));
pageLabel.color = new Color(0.65f, 0.65f, 0.65f, 1f);

currentModelLabel = MakeLabel(headerGo.transform, "ModelLabel", "Model: Standard", 11,
    TextAnchor.MiddleRight,
    new Vector2(0.87f, 0), new Vector2(1f, 1),
    new Vector2(0, 0), new Vector2(-6, 0));
currentModelLabel.color = new Color(0.45f, 0.70f, 1.00f, 1f);
currentModelLabel.raycastTarget = false;
```

- [ ] **Step 2: Add model selector row to the create panel in `BuildUI()`**

Find in `BuildUI()` where the create panel labels are built:
```csharp
MakeLabel(createGo.transform, "PromptLabel", "Describe your world:", 13,
    TextAnchor.MiddleLeft,
    new Vector2(0, 0.70f), new Vector2(1, 0.83f),
    new Vector2(16, 0), new Vector2(-16, 0));
```
Insert the model selector row **before** this `MakeLabel` call:
```csharp
// ── Model selector row ────────────────────────────────────────────────
var selectorGo = Div("ModelSelectorRow", createGo.transform);
var selectorRt = selectorGo.GetComponent<RectTransform>();
selectorRt.anchorMin = new Vector2(0f, 0.84f);
selectorRt.anchorMax = new Vector2(1f, 0.96f);
selectorRt.offsetMin = new Vector2(14f, 0f);
selectorRt.offsetMax = new Vector2(-14f, 0f);
var hlg = selectorGo.AddComponent<HorizontalLayoutGroup>();
hlg.spacing            = 4f;
hlg.childForceExpandWidth  = true;
hlg.childForceExpandHeight = true;
hlg.padding = new RectOffset(0, 0, 0, 0);
modelSelectorRow = selectorGo;

string[] modelLabels = { "Draft", "Fast", "Standard", "High" };
var activeColor   = new Color(0.25f, 0.55f, 1.00f, 1f);
var inactiveColor = new Color(0.18f, 0.20f, 0.28f, 1f);
for (int i = 0; i < 4; i++)
{
    int captured = i;
    var btnGo = new GameObject(modelLabels[i], typeof(RectTransform));
    btnGo.transform.SetParent(selectorGo.transform, false);
    btnGo.AddComponent<LayoutElement>();

    var img = btnGo.AddComponent<Image>();
    img.color = i == (int)selectedModel ? activeColor : inactiveColor;

    var btn = btnGo.AddComponent<Button>();
    btn.targetGraphic = img;
    btn.transition    = Selectable.Transition.ColorTint;
    btn.navigation    = new Navigation { mode = Navigation.Mode.None };
    btn.onClick.AddListener(() => SetGenerationModel((MarbleModel)captured));
    _modelButtons[i] = btn;

    var textGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
    textGo.transform.SetParent(btnGo.transform, false);
    var trt = textGo.GetComponent<RectTransform>();
    trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
    trt.offsetMin = trt.offsetMax = Vector2.zero;
    var txt = textGo.GetComponent<Text>();
    txt.font        = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    txt.fontSize    = 11;
    txt.color       = Color.white;
    txt.alignment   = TextAnchor.MiddleCenter;
    txt.text        = modelLabels[i];
    txt.raycastTarget = false;
}
```

- [ ] **Step 3: Shift create panel prompt label + input field to make room**

The create panel layout now needs adjustment since we added the model selector row at 84-96%. Update the existing label and input anchors:

Find:
```csharp
MakeLabel(createGo.transform, "PromptLabel", "Describe your world:", 13,
    TextAnchor.MiddleLeft,
    new Vector2(0, 0.70f), new Vector2(1, 0.83f),
    new Vector2(16, 0), new Vector2(-16, 0));

promptInputField = MakeInputField(createGo.transform, "PromptInput",
    new Vector2(0, 0.36f), new Vector2(1, 0.70f),
    new Vector2(14, 0), new Vector2(-14, 0));

createWorldButton = MakeButton(createGo.transform, "Generate World",
    new Vector2(0.08f, 0.20f), new Vector2(0.92f, 0.34f),
    new Vector2(0, 0), new Vector2(0, 0));
```
Replace with (all items shifted down to leave room for the model selector at top):
```csharp
MakeLabel(createGo.transform, "PromptLabel", "Describe your world:", 13,
    TextAnchor.MiddleLeft,
    new Vector2(0, 0.58f), new Vector2(1, 0.70f),
    new Vector2(16, 0), new Vector2(-16, 0));

promptInputField = MakeInputField(createGo.transform, "PromptInput",
    new Vector2(0, 0.26f), new Vector2(1, 0.58f),
    new Vector2(14, 0), new Vector2(-14, 0));

createWorldButton = MakeButton(createGo.transform, "Generate World",
    new Vector2(0.08f, 0.10f), new Vector2(0.92f, 0.24f),
    new Vector2(0, 0), new Vector2(0, 0));
```

- [ ] **Step 4: Commit**

```bash
git add Packages/com.worldlabs.gaussian-splatting/Runtime/WorldLabs/WorldBrowserController.cs
git commit -m "feat: add model selector row and header label to WorldBrowserController"
```

---

## Task 6: Add voice intents for model selection and mesh view

**Files:**
- Modify: `Assets/App/Command/SpeechIntent/Runtime/VoiceIntentSchemas.cs`
- Modify: `Assets/App/Command/SpeechIntent/Runtime/WorldActionDispatcher.cs`

- [ ] **Step 1: Add new intent types to `VoiceIntentType` enum**

Find in `VoiceIntentSchemas.cs`:
```csharp
LoadPanorama    = 15,  // load a local/remote panoramic image
```
Add after it:
```csharp
SetGenerationModel = 16,  // change the active generation model tier
ShowMeshWorld      = 17,  // switch view to the collision mesh
```

- [ ] **Step 2: Add `generation_model` field to `VoiceIntentCommand`**

Find in `VoiceIntentCommand`:
```csharp
[Header("Local/Remote Content")]
[Tooltip("File name, relative path, or full URL for LoadSplat and LoadPanorama intents.")]
public string content_path = "";
```
Add a new header block **before** this one:
```csharp
[Header("World Generation Model")]
[Tooltip("Target model tier for SetGenerationModel intent. Values: draft, fast, standard, high.")]
public string generation_model = "";
```

- [ ] **Step 3: Add new inspector fields to `WorldActionDispatcher`**

In `WorldActionDispatcher.cs`, find the existing scene controller fields:
```csharp
[Header("Scene Controllers")]
public LightRigController lightRig;
public UiPanelController uiPanels;
public ObjectPlacementController objectPlacement;
public StaticWorldController staticWorldController;
public TargetTransformController targetTransformController;
public InteractionMemory interactionMemory;
public ViewModeController viewModeController;
public PlayerOriginController playerOriginController;
public LocalRemoteSplatLoader splatLoader;
public LocalRemotePanoLoader  panoLoader;
```
Add three new fields at the end of that block:
```csharp
public WorldMeshController worldMeshController;
public VoiceToWorldLabsPluginCoordinator coordinator;
public WorldBrowserController worldBrowser;
```
Add the required using directives at the top of the file (after the existing `using UnityEngine;` lines):
```csharp
using WorldLabs.API;      // MarbleModel
using WorldLabs.Runtime;  // WorldBrowserController
using Holodeck.Direct;    // VoiceToWorldLabsPluginCoordinator
```

- [ ] **Step 4: Add two new switch cases to `WorldActionDispatcher.Execute()`**

Find the switch statement and add after the `LoadPanorama` case:
```csharp
case VoiceIntentType.SetGenerationModel:
    HandleSetGenerationModel(command);
    break;

case VoiceIntentType.ShowMeshWorld:
    if (viewModeController != null)
        viewModeController.RequestMeshView();
    else
        Debug.LogWarning("[WorldActionDispatcher] viewModeController is null.");
    break;
```

- [ ] **Step 5: Add handler methods**

Add these two private methods in `WorldActionDispatcher.cs`, next to the other private handlers:
```csharp
private void HandleSetGenerationModel(VoiceIntentCommand command)
{
    if (string.IsNullOrWhiteSpace(command.generation_model))
    {
        Debug.LogWarning("[WorldActionDispatcher] SetGenerationModel: generation_model is empty.");
        return;
    }

    MarbleModel model = command.generation_model.ToLower() switch
    {
        "draft"                  => MarbleModel.Draft,
        "fast" or "low"          => MarbleModel.Fast,
        "standard" or "normal"   => MarbleModel.Standard,
        "high" or "best" or "premium" => MarbleModel.High,
        _                        => MarbleModel.Standard
    };

    coordinator?.SetGenerationModel(model);
    worldBrowser?.SetGenerationModel(model);
    Debug.Log($"[WorldActionDispatcher] SetGenerationModel → {model}");
}
```

- [ ] **Step 6: Commit**

```bash
git add \
  Assets/App/Command/SpeechIntent/Runtime/VoiceIntentSchemas.cs \
  Assets/App/Command/SpeechIntent/Runtime/WorldActionDispatcher.cs
git commit -m "feat: add SetGenerationModel and ShowMeshWorld voice intents"
```

---

## Task 7: Create `WorldMeshController`

**Files:**
- Create: `Assets/App/Command/SpeechIntent/Runtime/WorldMeshController.cs`

- [ ] **Step 1: Create the file**

Create `Assets/App/Command/SpeechIntent/Runtime/WorldMeshController.cs` with full content:

```csharp
// Assets/App/Command/SpeechIntent/Runtime/WorldMeshController.cs

using System;
using System.Threading.Tasks;
using GLTFast;
using GaussianSplatting.Runtime;
using UnityEngine;
using WorldLabs.API;
using WorldLabs.Runtime;

namespace SpeechIntent
{
    /// <summary>
    /// Automatically downloads the GLB collision mesh when a WorldLabs world loads.
    /// Builds MeshCollider components (always active) and toggleable MeshRenderers
    /// for mesh-only view mode. Aligns to the same transform as the splat renderer.
    /// </summary>
    public class WorldMeshController : MonoBehaviour
    {
        [Header("Dependencies")]
        public WorldLabsWorldManager worldManager;

        // ── State ─────────────────────────────────────────────────────────────
        GltfImport  _gltf;
        GameObject  _meshRoot;
        bool        _isLoading;

        public bool HasMesh => _meshRoot != null && !_isLoading;

        // ── Lifecycle ─────────────────────────────────────────────────────────

        void OnEnable()
        {
            if (worldManager != null)
            {
                worldManager.OnWorldLoaded   += OnWorldLoaded;
                worldManager.OnWorldUnloaded += OnWorldUnloaded;
            }
        }

        void OnDisable()
        {
            if (worldManager != null)
            {
                worldManager.OnWorldLoaded   -= OnWorldLoaded;
                worldManager.OnWorldUnloaded -= OnWorldUnloaded;
            }
        }

        // ── Event handlers ────────────────────────────────────────────────────

        private async void OnWorldLoaded(string worldId, GaussianSplatRenderer renderer)
        {
            // Skip the default placeholder world
            if (worldId == "__default__") return;

            World world = worldManager.LastLoadedWorld;
            string meshUrl = world?.assets?.mesh?.collider_mesh_url;

            if (string.IsNullOrEmpty(meshUrl))
            {
                Debug.Log($"[WorldMeshController] No collider_mesh_url for '{worldId}' — skipping mesh download.");
                return;
            }

            await LoadMeshAsync(worldId, meshUrl, renderer);
        }

        private void OnWorldUnloaded(string worldId)
        {
            DestroyMesh();
        }

        // ── Mesh loading ──────────────────────────────────────────────────────

        private async Task LoadMeshAsync(string worldId, string url, GaussianSplatRenderer renderer)
        {
            if (_isLoading)
            {
                Debug.LogWarning($"[WorldMeshController] Already loading a mesh, skipping '{worldId}'.");
                return;
            }

            DestroyMesh();
            _isLoading = true;

            Debug.Log($"[WorldMeshController] Downloading collider mesh for '{worldId}'…");

            try
            {
                byte[] bytes = await WorldLabsClientExtensions.DownloadBinaryAsync(url);
                Debug.Log($"[WorldMeshController] GLB download complete: {bytes.Length} bytes.");

                // Create root aligned to the splat renderer's parent transform
                _meshRoot = new GameObject("MeshRoot");
                _meshRoot.transform.SetParent(transform, false);

                // Copy splat root transform so mesh and splat align
                if (renderer != null)
                {
                    Transform splatParent = renderer.transform.parent != null
                        ? renderer.transform.parent
                        : renderer.transform;
                    _meshRoot.transform.SetPositionAndRotation(
                        splatParent.position, splatParent.rotation);
                    _meshRoot.transform.localScale = splatParent.lossyScale;
                }

                // Import GLB
                _gltf = new GltfImport();
                bool ok = await _gltf.LoadGltfBinary(bytes);
                if (!ok)
                {
                    Debug.LogError($"[WorldMeshController] GLTFast failed to parse GLB for '{worldId}'.");
                    DestroyMesh();
                    return;
                }

                await _gltf.InstantiateSceneAsync(_meshRoot.transform);
                Debug.Log($"[WorldMeshController] GLB instantiated under MeshRoot.");

                // Add MeshColliders; hide renderers (visual is off by default)
                foreach (var mf in _meshRoot.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.sharedMesh == null) continue;
                    var mc = mf.gameObject.AddComponent<MeshCollider>();
                    mc.sharedMesh = mf.sharedMesh;
                    mc.convex     = false;
                }

                foreach (var mr in _meshRoot.GetComponentsInChildren<MeshRenderer>(true))
                    mr.enabled = false;

                Debug.Log($"[WorldMeshController] Mesh ready for '{worldId}' — colliders active, visual hidden.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[WorldMeshController] Mesh load failed for '{worldId}': {ex.Message}");
                DestroyMesh();
            }
            finally
            {
                _isLoading = false;
            }
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Enables all MeshRenderers in the mesh root so the geometry is visible.
        /// Colliders remain active regardless of this call.
        /// </summary>
        public void ShowVisual()
        {
            if (_meshRoot == null) return;
            foreach (var mr in _meshRoot.GetComponentsInChildren<MeshRenderer>(true))
                mr.enabled = true;
        }

        /// <summary>
        /// Disables all MeshRenderers in the mesh root. Colliders remain active.
        /// </summary>
        public void HideVisual()
        {
            if (_meshRoot == null) return;
            foreach (var mr in _meshRoot.GetComponentsInChildren<MeshRenderer>(true))
                mr.enabled = false;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        void DestroyMesh()
        {
            if (_meshRoot != null)
            {
                Destroy(_meshRoot);
                _meshRoot = null;
            }
            _gltf?.Dispose();
            _gltf = null;
        }
    }
}
```

- [ ] **Step 2: Verify GLTFast namespace**

Confirm the GLTFast package assembly name. Open Unity's Package Manager and check whether it's `com.unity.cloud.gltfast` or `com.atteneder.gltfast`. The namespace `GLTFast` is correct for both, but the assembly reference in the `.asmdef` for the SpeechIntent assembly may need updating.

Check `Assets/App/Command/SpeechIntent/Runtime/*.asmdef` — if one exists, add `"GLTFast"` (or the correct assembly name, e.g. `"glTFast"`) to its `references` array. If there is no `.asmdef`, no change is needed.

- [ ] **Step 3: Commit**

```bash
git add Assets/App/Command/SpeechIntent/Runtime/WorldMeshController.cs
git commit -m "feat: add WorldMeshController — auto GLB download, colliders, visual toggle"
```

---

## Task 8: Extend `ViewModeController` with Mesh mode

**Files:**
- Modify: `Assets/App/Command/SpeechIntent/Runtime/ViewModeController.cs`

- [ ] **Step 1: Add `Mesh` to `ViewMode` enum**

Find:
```csharp
public enum ViewMode { None, Pano, Splat3D }
```
Replace with:
```csharp
public enum ViewMode { None, Pano, Splat3D, Mesh }
```

- [ ] **Step 2: Add `worldMeshController` inspector field**

In the `[Header("Scene References")]` block, add after `worldBrowser`:
```csharp
public WorldMeshController worldMeshController;
```

- [ ] **Step 3: Add `RequestMeshView()` method**

Add after `RequestSplatView()`:
```csharp
public void RequestMeshView()
{
    Debug.Log($"[ViewModeController] RequestMeshView — worldMeshController={(worldMeshController != null ? "set" : "NULL")}");

    if (worldMeshController == null || !worldMeshController.HasMesh)
    {
        onViewModeError?.Invoke("Mesh not available for this world.");
        Debug.LogWarning("[ViewModeController] RequestMeshView — no mesh loaded.");
        return;
    }

    DesiredMode = ViewMode.Mesh;
    HideCurrentWorldRoot();          // hide splat
    worldMeshController.ShowVisual();
    // Leave skybox running — pano remains visible behind the mesh
    if (thumbnailSkybox != null) thumbnailSkybox.SuppressNextFadeOut = true;
    TryApply();
}
```

- [ ] **Step 4: Call `HideVisual()` in `RequestSplatView()` and `RequestPanoView()`**

In `RequestSplatView()`, add at the very beginning of the method body:
```csharp
worldMeshController?.HideVisual();
```

In `RequestPanoView()`, add at the very beginning of the method body:
```csharp
worldMeshController?.HideVisual();
```

- [ ] **Step 5: Add `ViewMode.Mesh` branch to `TryApply()`**

Find the `else if (DesiredMode == ViewMode.Splat3D)` block in `TryApply()`. Add a new branch after it:
```csharp
else if (DesiredMode == ViewMode.Mesh)
{
    if (worldMeshController != null && worldMeshController.HasMesh)
    {
        Debug.Log("[ViewModeController] TryApply: Mesh path — showing mesh visual.");
        HideCurrentWorldRoot();
        worldMeshController.ShowVisual();
    }
    else
    {
        Debug.Log("[ViewModeController] TryApply: Mesh path — mesh not ready, falling back to pano.");
        if (_isPanoReady)
        {
            DesiredMode = ViewMode.Pano;
            HideCurrentWorldRoot();
            thumbnailSkybox.ShowStored();
            onViewModeError?.Invoke("Mesh not available, showing panorama");
        }
    }
}
```

- [ ] **Step 6: Commit**

```bash
git add Assets/App/Command/SpeechIntent/Runtime/ViewModeController.cs
git commit -m "feat: add ViewMode.Mesh and RequestMeshView to ViewModeController"
```

---

## Task 9: Update voice routing hints in `OpenAiSpeechIntentConfig`

**Files:**
- Modify: `Assets/App/Command/SpeechIntent/Runtime/OpenAiSpeechIntentConfig.cs`

- [ ] **Step 1: Append new hint lines to `additionalDeveloperInstructions`**

Find the end of the `additionalDeveloperInstructions` string. The last line currently ends with:
```
If the user says only 'rotate it' with no useful default amount, ask a clarification question like 'By how many degrees would you like to rotate it?'.";
```
Change to:
```
If the user says only 'rotate it' with no useful default amount, ask a clarification question like 'By how many degrees would you like to rotate it?'.
For 'use high quality', 'best model', 'use draft', 'switch to fast model', 'use standard', etc., use intent=SetGenerationModel. Set generation_model to exactly one of: draft, fast, standard, high. Accept 'low' as an alias for 'fast' and 'best' or 'premium' as aliases for 'high'. Accept 'normal' as an alias for 'standard'.
For 'show mesh', 'mesh view', 'show collision mesh', 'show 3d mesh', or 'show the mesh', use intent=ShowMeshWorld.";
```

- [ ] **Step 2: Commit**

```bash
git add Assets/App/Command/SpeechIntent/Runtime/OpenAiSpeechIntentConfig.cs
git commit -m "feat: add model selection and mesh view routing hints to speech intent config"
```

---

## Task 10: Wire `WorldMeshController` in the scene and verify

**Files:**
- Scene: `Assets/Scenes/Holodeck.unity` (manual Inspector wiring)

- [ ] **Step 1: Add `WorldMeshController` component to the scene**

In the Unity Editor, find the GameObject that holds `WorldActionDispatcher` and `ViewModeController` (likely under the `SpeechIntent` or `Holodeck` root). Add the `WorldMeshController` component to a suitable parent (e.g. the same `Holodeck` root or a dedicated child).

- [ ] **Step 2: Wire `WorldMeshController` inspector fields**

In the `WorldMeshController` Inspector:
- `worldManager` → drag in `WorldLabsWorldManager` from the scene

- [ ] **Step 3: Wire `ViewModeController.worldMeshController`**

On the `ViewModeController` component, set `worldMeshController` to the new component.

- [ ] **Step 4: Wire `WorldActionDispatcher` new fields**

On the `WorldActionDispatcher` component:
- `worldMeshController` → the new `WorldMeshController`
- `coordinator` → `VoiceToWorldLabsPluginCoordinator`
- `worldBrowser` → `WorldBrowserController` (on `WorldLabs_GUI` prefab)

- [ ] **Step 5: Verify compilation in Unity**

Open Unity. Confirm the Console shows zero compilation errors. If GLTFast assembly reference is missing, follow Task 7 Step 2 to add it to the `.asmdef`.

- [ ] **Step 6: Play-mode smoke test — model selection UI**

1. Enter Play Mode.
2. Open the WorldBrowser create panel (tap ➕ Create).
3. Confirm four buttons appear: Draft / Fast / Standard / High.
4. Tap each — confirm the active one turns blue and `currentModelLabel` in the header updates.
5. Confirm the selected model is used when generating (check the Debug log for "marble-1.0-draft" etc.).

- [ ] **Step 7: Play-mode smoke test — voice model selection**

1. Say "use high quality" — confirm `generation_model` = "high" in the Debug log.
2. Say "use draft" — confirm `generation_model` = "draft".
3. Confirm `WorldBrowserController` model buttons update in sync.

- [ ] **Step 8: Play-mode smoke test — mesh download**

1. Load a world that has `collider_mesh_url` populated (check with a Debug breakpoint or log in `OnWorldLoaded`).
2. Confirm the Debug log shows "GLB download complete" and "Mesh ready".
3. Say "show mesh" or call `viewModeController.RequestMeshView()` from code.
4. Confirm the mesh appears solid opaque with the skybox visible behind it.
5. Say "show 3d" — confirm the mesh visual hides and the splat reappears.
6. Confirm colliders remain active even when visual is hidden (add a primitive and check physics interaction).

- [ ] **Step 9: Final commit**

```bash
git add Assets/Scenes/Holodeck.unity
git commit -m "feat: wire WorldMeshController in scene"
git push
```

---

## Notes for the Implementer

**GLTFast assembly reference:** If the project uses assembly definition files (`.asmdef`), the `SpeechIntent` or host assembly must reference GLTFast. The package assembly name varies:
- `com.unity.cloud.gltfast` → assembly name: `GLTFast`
- `com.atteneder.gltfast` → assembly name: `glTFast`

Check Package Manager to determine which variant is installed, then add the correct name to the relevant `.asmdef`'s `references` array.

**`collider_mesh_url` availability:** Not all worlds return a mesh URL. The controller skips silently when the URL is absent — this is expected for Draft and Fast tier worlds.

**`SuppressNextFadeOut` in `RequestMeshView`:** This property exists on `ThumbnailSkyboxController`. If the skybox is not currently showing (e.g. the user just loaded a splat and the pano faded out), the mesh view will show only the mesh against the default skybox — that is intentional per the design.
