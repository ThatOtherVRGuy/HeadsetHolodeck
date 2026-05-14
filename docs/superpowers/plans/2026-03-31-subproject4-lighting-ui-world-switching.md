# Sub-project 4: Lighting, UI, and World Switching Commands — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Wire `LightRigController`, `UiPanelController`, and `StaticWorldController` into the scene setup script and teach GPT-4o the `status` UI panel key, enabling lighting, UI, and world-switching voice commands end-to-end.

**Architecture:** Two files changed. `SpeechIntentSceneSetup` (editor script) adds three controllers via `GetOrAdd<T>`, wires them to the dispatcher, and auto-finds scene GameObjects by path. `OpenAiSpeechIntentService` gets one new line in its hardcoded developer prompt. No runtime logic changes.

**Tech Stack:** Unity 2022+, C# editor scripting, `Undo` API, `GameObject.Find()`.

---

## Files

| File | Change |
|---|---|
| `Assets/App/Editor/SpeechIntentSceneSetup.cs` | Add 3 components, expand Undo array, add 3 dispatcher wires, add scene-ref wiring block, add `WireUiPanel` helper |
| `Assets/App/Command/SpeechIntent/Runtime/OpenAiSpeechIntentService.cs` | Add 1 line to `BuildDeveloperInstructions()` |

---

### Task 1: Update SpeechIntentSceneSetup

**Spec:** `docs/superpowers/specs/2026-03-31-subproject4-lighting-ui-world-switching-design.md`

**File:** `Assets/App/Editor/SpeechIntentSceneSetup.cs`

**Context:** This is an editor `[MenuItem]` script. Re-running is idempotent. The file currently has these clearly-labelled sections in `SetupSpeechIntent()`:
- Section 2 (line 42): `GetOrAdd<T>` calls — ends at line 52 with `targetTransform`
- Section 5 (line 70): `Undo.RecordObjects` + wire assignments
- After line 128: SerializedObject block for `InteractionMemory.worldManager`
- Section 6 (line 130): Cross-system UnityEvents
- Section 7 (line 137): Mark dirty + log

The method ends at line 143. Private helpers follow below.

---

- [ ] **Step 1: Read the file**

Open and read `Assets/App/Editor/SpeechIntentSceneSetup.cs` in full. Confirm line 52 ends with `targetTransform`, line 72 contains the `Undo.RecordObjects` array, and line 83 contains `dispatcher.targetTransformController`.

- [ ] **Step 2: Add three GetOrAdd calls (section 2)**

After line 52 (`TargetTransformController targetTransform = GetOrAdd<TargetTransformController>(speechRoot);`), add:

```csharp
            LightRigController             lightRig           = GetOrAdd<LightRigController>(speechRoot);
            UiPanelController              uiPanels           = GetOrAdd<UiPanelController>(speechRoot);
            StaticWorldController          staticWorld        = GetOrAdd<StaticWorldController>(speechRoot);
```

- [ ] **Step 3: Expand the Undo.RecordObjects array (section 5)**

Find the current array (lines 71–73):
```csharp
            Undo.RecordObjects(
                new Object[] { service, dispatcher, memory, router, trigger, semantic, entityResolver, targetTransform },
                "Wire SpeechIntent Components");
```

Replace with:
```csharp
            Undo.RecordObjects(
                new Object[] { service, dispatcher, memory, router, trigger, semantic, entityResolver, targetTransform, lightRig, uiPanels, staticWorld },
                "Wire SpeechIntent Components");
```

- [ ] **Step 4: Add three dispatcher wire assignments (section 5)**

After line 83 (`dispatcher.targetTransformController = targetTransform;`), add:

```csharp
            dispatcher.lightRig              = lightRig;
            dispatcher.uiPanels              = uiPanels;
            dispatcher.staticWorldController = staticWorld;
```

- [ ] **Step 5: Add scene reference wiring block**

After line 128 (the closing `}` of the `if (worldManager != null)` block for `InteractionMemory.worldManager`) and before the `// ── 6. Wire cross-system UnityEvents` comment, insert:

```csharp
            // ── 6. Wire scene references ──────────────────────────────────────
            GameObject dirLightGo = GameObject.Find("Lighting/DirectionalLight");
            if (dirLightGo != null)
            {
                Undo.RecordObject(lightRig, "Wire LightRigController");
                lightRig.sunLight = dirLightGo.GetComponent<Light>();
            }
            else
                Debug.LogWarning("[SpeechIntentSceneSetup] 'Lighting/DirectionalLight' not found. Assign lightRig.sunLight manually.");

            GameObject staticRoot  = GameObject.Find("Environment/TNGHolodeck");
            GameObject dynamicRoot = GameObject.Find("Environment/GeneratedWorldRoot");
            Undo.RecordObject(staticWorld, "Wire StaticWorldController");
            if (staticRoot != null)
                staticWorld.staticWorldRoot = staticRoot;
            else
                Debug.LogWarning("[SpeechIntentSceneSetup] 'Environment/TNGHolodeck' not found. Assign staticWorldController.staticWorldRoot manually.");
            if (dynamicRoot != null)
                staticWorld.dynamicWorldRoot = dynamicRoot;
            else
                Debug.LogWarning("[SpeechIntentSceneSetup] 'Environment/GeneratedWorldRoot' not found. Assign staticWorldController.dynamicWorldRoot manually.");

            Undo.RecordObject(uiPanels, "Wire UiPanelController panels");
            WireUiPanel(uiPanels, "arch_menu", "UI/WorldLabs_GUI");
            WireUiPanel(uiPanels, "status",    "UI/WorldLabs_Status");

```

- [ ] **Step 6: Add the WireUiPanel private helper method**

Add this method to the class, after the existing `GetOrAdd<T>` helper (after line 253, before the closing `}` of the class):

```csharp
        private static void WireUiPanel(UiPanelController controller, string key, string goPath)
        {
            // Idempotent: skip if a panel with this key is already registered.
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

- [ ] **Step 7: Verify the file compiles**

Save the file. Confirm no compiler errors in the Unity Console.

Common issues:
- `LightRigController`, `UiPanelController`, `StaticWorldController` are in the `SpeechIntent` namespace — already imported via `using SpeechIntent;`
- `Light` (the component type) is in `UnityEngine` — already imported

- [ ] **Step 8: Run Setup SpeechIntent and verify**

Menu: `Holodeck > Setup SpeechIntent`

Expected Console: `[SpeechIntentSceneSetup] Done. Set your OpenAI API key...`

No warnings about missing GameObjects (assuming the scene has the expected hierarchy).

Select the `SpeechIntent` GameObject and confirm in the Inspector:
1. `LightRigController` present — `sunLight` field → `DirectionalLight` component
2. `UiPanelController` present — `panels` list has 2 entries: `arch_menu` and `status`
3. `StaticWorldController` present — `staticWorldRoot` → `TNGHolodeck`, `dynamicWorldRoot` → `GeneratedWorldRoot`
4. `WorldActionDispatcher` — `lightRig`, `uiPanels`, `staticWorldController` all assigned

- [ ] **Step 9: Commit**

```bash
cd /Users/davidarendash/Documents/Projects/Unity/HeadsetHolodeck
git add Assets/App/Editor/SpeechIntentSceneSetup.cs
git commit -m "feat: wire LightRigController, UiPanelController, StaticWorldController in SpeechIntentSceneSetup

Enables SetLightingPreset, SetSunDirection, ShowUi, and SwitchToStaticWorld
voice commands. Auto-wires scene references for sun light, world roots,
and UI panels.

Co-Authored-By: Claude Sonnet 4.6 <noreply@anthropic.com>"
```

---

### Task 2: Add status panel instruction to developer prompt

**Spec:** `docs/superpowers/specs/2026-03-31-subproject4-lighting-ui-world-switching-design.md`

**File:** `Assets/App/Command/SpeechIntent/Runtime/OpenAiSpeechIntentService.cs`

**Context:** `BuildDeveloperInstructions()` builds the hardcoded system prompt sent to GPT-4o. It already contains a line for `arch_menu` (line 345). The new line goes immediately after it so all UI panel instructions are grouped together.

---

- [ ] **Step 1: Read the file and locate the insertion point**

Open `Assets/App/Command/SpeechIntent/Runtime/OpenAiSpeechIntentService.cs`.

Find this line (around line 345):
```csharp
            sb.AppendLine("For 'arch', 'exit', or 'menu', use intent=ShowUi and ui_panel='arch_menu' unless context suggests another visible panel.");
```

- [ ] **Step 2: Add the status panel instruction**

Add one line immediately after the `arch_menu` line:

```csharp
            sb.AppendLine("For 'show status', 'show loading', or 'show progress', use intent=ShowUi and ui_panel='status'.");
```

- [ ] **Step 3: Verify**

Save. Confirm no compiler errors. The change is one line — no logic to verify beyond compilation.

- [ ] **Step 4: Commit**

```bash
git add Assets/App/Command/SpeechIntent/Runtime/OpenAiSpeechIntentService.cs
git commit -m "feat: add status panel key to GPT-4o developer instructions

Teaches the model to use ui_panel='status' for show-status/loading utterances.

Co-Authored-By: Claude Sonnet 4.6 <noreply@anthropic.com>"
```

- [ ] **Step 5: Save the scene in Unity and commit it**

After running Setup SpeechIntent in Task 1, Unity marks the scene dirty. Save it (`File > Save` or Cmd+S), then commit:

```bash
git add Assets/Scenes/   # adjust to your actual .unity file path
git commit -m "scene: wire lighting, UI, and world-switching controllers via SpeechIntentSceneSetup"
```
