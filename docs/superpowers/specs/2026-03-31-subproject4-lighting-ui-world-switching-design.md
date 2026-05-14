# Sub-project 4: Lighting, UI, and World Switching Commands — Design

## Goal

Wire `LightRigController`, `UiPanelController`, and `StaticWorldController` into `SpeechIntentSceneSetup`, auto-assign their scene references, and add a `status` panel instruction to the base developer prompt so GPT-4o knows both UI panel keys.

## Background

All runtime logic is already implemented:
- `LightRigController` — `ApplyPreset(string)` and `TryAlignSun(VoiceIntentCommand, SpatialSnapshot)`
- `UiPanelController` — `Show(string key)` with case-insensitive panel lookup
- `StaticWorldController` — `SwitchToStaticWorld()` / `SwitchToDynamicWorld()`
- `WorldActionDispatcher` — already routes `SetLightingPreset`, `SetSunDirection`, `ShowUi`, `SwitchToStaticWorld` to these controllers (logs warning if null)

The gaps:
1. `SpeechIntentSceneSetup` does not create or wire any of the three controllers
2. `BuildDeveloperInstructions()` in `OpenAiSpeechIntentService` mentions `arch_menu` but not the `status` panel key

## Scene Object Paths

| Reference | Path | Note |
|---|---|---|
| `lightRig.sunLight` | `Lighting/DirectionalLight` | `GetComponent<Light>()` |
| `lightRig.skyReferenceOrigin` | — | Left null (optional) |
| `staticWorldController.staticWorldRoot` | `Environment/TNGHolodeck` | |
| `staticWorldController.dynamicWorldRoot` | `Environment/GeneratedWorldRoot` | |
| `uiPanels.panels` entry 0 | key=`arch_menu`, go=`UI/WorldLabs_GUI` | |
| `uiPanels.panels` entry 1 | key=`status`, go=`UI/WorldLabs_Status` | |

## Changes

### File 1: `Assets/App/Editor/SpeechIntentSceneSetup.cs`

**Components added via `GetOrAdd<T>` (section 2):**
- `LightRigController lightRig`
- `UiPanelController uiPanels`
- `StaticWorldController staticWorldController`

**Dispatcher wires added (section 5):**
- `dispatcher.lightRig = lightRig`
- `dispatcher.uiPanels = uiPanels`
- `dispatcher.staticWorldController = staticWorldController`

**All three added to the existing `Undo.RecordObjects` batch call** (for the dispatcher wire assignments). The expanded array becomes:
`{ service, dispatcher, memory, router, trigger, semantic, entityResolver, targetTransform, lightRig, uiPanels, staticWorldController }`

**Scene reference auto-wiring (new section 6, after section 5):**

Scene-specific field mutations on the new controllers use individual `Undo.RecordObject` calls (separate from the batch above, since they happen after `GameObject.Find` lookups):

Light rig:
```csharp
GameObject dirLightGo = GameObject.Find("Lighting/DirectionalLight");
if (dirLightGo != null)
{
    Undo.RecordObject(lightRig, "Wire LightRigController");
    lightRig.sunLight = dirLightGo.GetComponent<Light>();
}
else
    Debug.LogWarning("[SpeechIntentSceneSetup] 'Lighting/DirectionalLight' not found. Assign lightRig.sunLight manually.");
```

Static world controller:
```csharp
GameObject staticRoot = GameObject.Find("Environment/TNGHolodeck");
GameObject dynamicRoot = GameObject.Find("Environment/GeneratedWorldRoot");
Undo.RecordObject(staticWorldController, "Wire StaticWorldController");
if (staticRoot != null)
    staticWorldController.staticWorldRoot = staticRoot;
else
    Debug.LogWarning("[SpeechIntentSceneSetup] 'Environment/TNGHolodeck' not found. Assign staticWorldController.staticWorldRoot manually.");
if (dynamicRoot != null)
    staticWorldController.dynamicWorldRoot = dynamicRoot;
else
    Debug.LogWarning("[SpeechIntentSceneSetup] 'Environment/GeneratedWorldRoot' not found. Assign staticWorldController.dynamicWorldRoot manually.");
```

UI panel controller (idempotent — only adds entries whose key is not already in the list):
```csharp
Undo.RecordObject(uiPanels, "Wire UiPanelController panels");
WireUiPanel(uiPanels, "arch_menu", "UI/WorldLabs_GUI");
WireUiPanel(uiPanels, "status",    "UI/WorldLabs_Status");
```

Where `WireUiPanel` is a new private helper:
```csharp
private static void WireUiPanel(UiPanelController controller, string key, string goPath)
{
    // Skip if already registered
    foreach (UiPanelController.PanelEntry e in controller.panels)
        if (string.Equals(e?.key, key, System.StringComparison.OrdinalIgnoreCase)) return;

    GameObject go = GameObject.Find(goPath);
    if (go == null)
    {
        Debug.LogWarning($"[SpeechIntentSceneSetup] UI panel '{goPath}' not found. Add '{key}' to uiPanels.panels manually.");
        return;
    }

    controller.panels.Add(new UiPanelController.PanelEntry { key = key, root = go });
}
```

---

### File 2: `Assets/App/Command/SpeechIntent/Runtime/OpenAiSpeechIntentService.cs`

One line added in `BuildDeveloperInstructions()`, immediately after the existing `arch_menu` line:

**Existing line:**
```csharp
sb.AppendLine("For 'arch', 'exit', or 'menu', use intent=ShowUi and ui_panel='arch_menu' unless context suggests another visible panel.");
```

**New line after it:**
```csharp
sb.AppendLine("For 'show status', 'show loading', or 'show progress', use intent=ShowUi and ui_panel='status'.");
```

## Out of Scope

- `lightRig.skyReferenceOrigin`: left null (optional Transform used for sun alignment; works without it)
- `ObjectPlacementController`: already a field on `WorldActionDispatcher` but belongs to a separate sub-project
- Changes to any runtime scripts other than `OpenAiSpeechIntentService.BuildDeveloperInstructions()`

## Testing

After running `Holodeck > Setup SpeechIntent`:
1. `LightRigController`, `UiPanelController`, `StaticWorldController` present on `SpeechIntent` GameObject
2. `WorldActionDispatcher` fields `lightRig`, `uiPanels`, `staticWorldController` all assigned
3. `lightRig.sunLight` → `DirectionalLight` component
4. `staticWorldController.staticWorldRoot` → `TNGHolodeck`, `.dynamicWorldRoot` → `GeneratedWorldRoot`
5. `uiPanels.panels` contains two entries: `arch_menu` and `status`
6. No warnings in Console about missing GameObjects

In Play mode:
- "make it night time" → world lighting changes to night preset
- "arch" / "menu" → `WorldLabs_GUI` panel activates
- "show status" → `WorldLabs_Status` panel activates
- "end program" → switches to static world (TNGHolodeck)
