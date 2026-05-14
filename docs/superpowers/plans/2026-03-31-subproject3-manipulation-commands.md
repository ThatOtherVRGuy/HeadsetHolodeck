# Sub-project 3: Object/World Manipulation Commands — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Wire `SceneEntityResolver` and `TargetTransformController` into `SpeechIntentSceneSetup` so ScaleTarget, RotateTarget, and MoveTarget voice commands work end-to-end.

**Architecture:** All runtime logic (target resolution, transform manipulation, interaction memory) is already implemented. The only change is to the editor setup script, which creates and wires components onto the `SpeechIntent` GameObject. Six new wire assignments and two new `GetOrAdd<T>` calls are needed; `semantic` also receives two currently-missing wires.

**Tech Stack:** Unity 2022+, C# editor scripting, `Undo` API for safe Inspector mutations.

---

## Files

| File | Change |
|---|---|
| `Assets/App/Editor/SpeechIntentSceneSetup.cs` | Modify — add two components and six wire assignments |

No new files. No runtime scripts touched.

---

### Task 1: Update SpeechIntentSceneSetup and verify in Editor

**Spec:** `docs/superpowers/specs/2026-03-31-subproject3-manipulation-commands-design.md`

**Files:**
- Modify: `Assets/App/Editor/SpeechIntentSceneSetup.cs`

**Context for the implementer:**

`SpeechIntentSceneSetup` is an editor-only `[MenuItem]` script (`Holodeck > Setup SpeechIntent`) that creates/reuses a `SpeechIntent` child GameObject under `Systems` and wires all SpeechIntent components. Re-running is idempotent — `GetOrAdd<T>` returns existing components.

The current `SetupSpeechIntent()` method has these clearly-labelled sections:
- `// ── 2. Add SpeechIntent components ──` (lines ~43–50) — `GetOrAdd<T>` calls
- `// ── 5. Wire internal SpeechIntent references ──` (lines ~69–83) — direct field assignments preceded by `Undo.RecordObjects`

The change adds to both sections. Do NOT move, reformat, or remove any existing code.

**Exact change — section 2 (add two lines after the `trigger` line):**

Current end of section 2:
```csharp
VoiceCommandRouter             router        = GetOrAdd<VoiceCommandRouter>(speechRoot);
PushToTalkTrigger              trigger       = GetOrAdd<PushToTalkTrigger>(speechRoot);
```

New end of section 2:
```csharp
VoiceCommandRouter             router          = GetOrAdd<VoiceCommandRouter>(speechRoot);
PushToTalkTrigger              trigger         = GetOrAdd<PushToTalkTrigger>(speechRoot);
SceneEntityResolver            entityResolver  = GetOrAdd<SceneEntityResolver>(speechRoot);
TargetTransformController      targetTransform = GetOrAdd<TargetTransformController>(speechRoot);
```

**Exact change — section 5 `Undo.RecordObjects` call (expand array):**

Current:
```csharp
Undo.RecordObjects(
    new Object[] { service, dispatcher, memory, router, trigger },
    "Wire SpeechIntent Components");
```

New:
```csharp
Undo.RecordObjects(
    new Object[] { service, dispatcher, memory, router, trigger, semantic, entityResolver, targetTransform },
    "Wire SpeechIntent Components");
```

**Exact change — section 5 wire assignments (add six lines after existing wires, before the trigger wiring block):**

Add immediately after `dispatcher.interactionMemory = memory;` and before `router.recorder = recorder;`:
```csharp
semantic.interactionMemory    = memory;
semantic.entityResolver       = entityResolver;

entityResolver.interactionMemory = memory;

dispatcher.targetTransformController = targetTransform;

targetTransform.entityResolver    = entityResolver;
targetTransform.interactionMemory = memory;
```

The complete wiring block should read:
```csharp
service.config = config;

semantic.interactionMemory    = memory;
semantic.entityResolver       = entityResolver;

entityResolver.interactionMemory = memory;

dispatcher.interactionMemory         = memory;
dispatcher.targetTransformController = targetTransform;

router.recorder               = recorder;
router.spatialContextProvider = spatial;
router.sceneContextProvider   = semantic;
router.speechIntentService    = service;
router.dispatcher             = dispatcher;

trigger.router = router;

targetTransform.entityResolver    = entityResolver;
targetTransform.interactionMemory = memory;
```

---

- [ ] **Step 1: Open and read the file**

Open `Assets/App/Editor/SpeechIntentSceneSetup.cs` in full. Locate section 2 (GetOrAdd calls) and section 5 (Undo.RecordObjects + wire assignments). Confirm the current code matches the "current" snippets above before making any edits.

- [ ] **Step 2: Add the two new GetOrAdd calls**

After the `trigger` line in section 2, add:
```csharp
SceneEntityResolver            entityResolver  = GetOrAdd<SceneEntityResolver>(speechRoot);
TargetTransformController      targetTransform = GetOrAdd<TargetTransformController>(speechRoot);
```

- [ ] **Step 3: Expand Undo.RecordObjects**

Update the `Undo.RecordObjects` call to include `semantic`, `entityResolver`, and `targetTransform`:
```csharp
Undo.RecordObjects(
    new Object[] { service, dispatcher, memory, router, trigger, semantic, entityResolver, targetTransform },
    "Wire SpeechIntent Components");
```

- [ ] **Step 4: Add the six new wire assignments**

In the wire assignments block, add the six lines shown in the "complete wiring block" above. Follow the grouping shown — semantic first, then entityResolver, then dispatcher additions, then router (unchanged), then trigger (unchanged), then targetTransform at the end.

- [ ] **Step 5: Verify the file compiles — check for errors in Unity Console**

Save the file. Unity will recompile. Confirm there are no compiler errors in the Console. If there are errors:
- Check for typos in field names: `entityResolver`, `interactionMemory`, `targetTransformController`, `targetTransform`
- `SceneEntityResolver` and `TargetTransformController` are in the `SpeechIntent` namespace — already imported via `using SpeechIntent;` at the top of the file

- [ ] **Step 6: Run Setup SpeechIntent**

In the Unity Editor menu: `Holodeck > Setup SpeechIntent`

Expected Console output: `[SpeechIntentSceneSetup] Done. Set your OpenAI API key...`

No warnings should appear about missing components or failed wiring.

- [ ] **Step 7: Verify components in Inspector**

Select the `SpeechIntent` GameObject under `Systems` in the Hierarchy. Confirm in the Inspector:

1. `SceneEntityResolver` component is present
   - `interactionMemory` field → points to `InteractionMemory` on the same GameObject
   - `searchRoot` → None (intentional)

2. `TargetTransformController` component is present
   - `entityResolver` → points to `SceneEntityResolver` on the same GameObject
   - `interactionMemory` → points to `InteractionMemory` on the same GameObject

3. `WorldActionDispatcher` component
   - `targetTransformController` → points to `TargetTransformController` on the same GameObject
   - `interactionMemory` → already wired (should remain pointing to `InteractionMemory`)

4. `SceneSemanticContextProvider` component
   - `interactionMemory` → points to `InteractionMemory`
   - `entityResolver` → points to `SceneEntityResolver`

- [ ] **Step 8: Commit**

```bash
git add Assets/App/Editor/SpeechIntentSceneSetup.cs
git commit -m "feat: wire SceneEntityResolver and TargetTransformController in SpeechIntentSceneSetup

Adds ScaleTarget, RotateTarget, and MoveTarget voice command support.
Also wires SceneSemanticContextProvider.interactionMemory and .entityResolver
which were previously unset."
```

- [ ] **Step 9: Save the scene**

In Unity: `File > Save` (or Cmd+S). The scene was marked dirty by the setup script. The commit above does not include the scene file — save it separately in Unity, then stage and commit the `.unity` scene file:

```bash
git add Assets/Scenes/   # or the exact path to your .unity file
git commit -m "scene: wire manipulation components via SpeechIntentSceneSetup"
```
