# Cached Object Catalog Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Cache generated/imported 3D objects as reusable assets, restore them from world JSON, and ask the user whether to use a saved object or create a new one when a matching cached object exists.

**Architecture:** Add a `CachedObjects` store beside `CachedWorlds`, with one folder per reusable object containing `object.json`, `model.glb`, and optional `thumbnail.png`. Per-world `world.json` stores scene instances that reference cached object ids plus transform/material/component state. Object creation routes through a cache lookup before API generation.

**Tech Stack:** Unity 6.2 C#, glTFast, Newtonsoft JSON/JObject, existing `WorldConfigStore`, `WorldConfigComponentRegistry`, `ObjectGenerationService`, `WorldActionDispatcher`, and SpeechIntent routing.

---

## File Map

- Create `Assets/App/Scripts/Direct/ObjectGeneration/CachedObjectModels.cs`: serializable metadata for cached reusable objects.
- Create `Assets/App/Scripts/Direct/ObjectGeneration/CachedObjectStore.cs`: path management, metadata read/write, name/alias lookup, GLB writes.
- Create `Assets/App/Save/Runtime/CachedObjectReference.cs`: component attached to generated/imported objects to remember the cached object id.
- Create `Assets/App/Save/Runtime/CachedObjectReferenceSerializer.cs`: saves/restores cached object id in world JSON components.
- Modify `Assets/App/Save/Runtime/WorldConfigStore.cs`: expose `CachedObjectsPath`.
- Modify `Assets/App/Scripts/Direct/ObjectGeneration/ObjectGenerationService.cs`: write provider GLB bytes to `CachedObjects`, import cached GLBs, and attach `CachedObjectReference`.
- Modify `Assets/App/Save/WorldConfigRestorer.cs`: import cached GLB before applying saved components.
- Modify `Assets/App/Command/SpeechIntent/Runtime/WorldActionDispatcher.cs`: check the object cache before starting API generation.
- Create `Assets/App/Command/SpeechIntent/Runtime/CachedObjectChoiceController.cs`: pending saved/new decision state for voice/UI.
- Create `Assets/App/Command/SpeechIntent/Runtime/UI/CachedObjectChoicePanel.cs`: simple UI hook for saved/new choice.
- Add tests in `Assets/App/Editor/CachedObjectBatchTests.cs`: cache write/read/search and restore-path behavior.

---

### Task 1: Cached Object Store Foundation

**Files:**
- Create: `Assets/App/Scripts/Direct/ObjectGeneration/CachedObjectModels.cs`
- Create: `Assets/App/Scripts/Direct/ObjectGeneration/CachedObjectStore.cs`
- Modify: `Assets/App/Save/Runtime/WorldConfigStore.cs`
- Test: `Assets/App/Editor/CachedObjectBatchTests.cs`

- [ ] **Step 1: Write the failing store test**

Create `Assets/App/Editor/CachedObjectBatchTests.cs`:

```csharp
using System;
using System.IO;
using Holodeck.Direct;
using UnityEditor;
using UnityEngine;

namespace HeadsetHolodeck.EditorTests
{
    public static class CachedObjectBatchTests
    {
        public static void RunCachedObjectStoreTests()
        {
            string root = Path.Combine(Path.GetTempPath(), "HeadsetHolodeck_CachedObjectTests_" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(root);
                CachedObjectStore store = CachedObjectStore.CreateForTesting(root);
                byte[] glbBytes = { 1, 2, 3, 4 };

                CachedObjectRecord record = store.SaveGeneratedObject(
                    canonicalName: "teddy bear",
                    prompt: "a friendly teddy bear",
                    providerName: "TestProvider",
                    taskId: "task_123",
                    modelUrl: "https://example.test/model.glb",
                    modelBytes: glbBytes);

                AssertTrue(!string.IsNullOrWhiteSpace(record.object_id), "Expected object id.");
                AssertTrue(File.Exists(Path.Combine(root, "CachedObjects", record.object_id, "model.glb")), "Expected model.glb.");
                AssertTrue(File.Exists(Path.Combine(root, "CachedObjects", record.object_id, "object.json")), "Expected object.json.");

                CachedObjectRecord loaded = store.Load(record.object_id);
                AssertEqual("teddy bear", loaded.canonical_name, "Expected canonical name round-trip.");
                AssertEqual("TestProvider", loaded.provider, "Expected provider round-trip.");
                AssertEqual("task_123", loaded.task_id, "Expected task id round-trip.");

                var matches = store.FindByName("Teddy Bear");
                AssertEqual(1, matches.Count, "Expected case-insensitive name lookup.");
                AssertEqual(record.object_id, matches[0].object_id, "Expected matching object id.");

                Debug.Log("[CachedObjectBatchTests] Cached object store tests passed.");
            }
            catch (Exception ex)
            {
                Debug.LogError("[CachedObjectBatchTests] Failed: " + ex);
                EditorApplication.Exit(1);
                throw;
            }
            finally
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, true);
            }
        }

        static void AssertTrue(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
        }

        static void AssertEqual<T>(T expected, T actual, string message)
        {
            if (!Equals(expected, actual))
                throw new InvalidOperationException($"{message} Expected={expected}, Actual={actual}");
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```bash
/Applications/Unity/Hub/Editor/6000.2.10f1/Unity.app/Contents/MacOS/Unity -batchmode -quit -projectPath /Users/davidarendash/Documents/Projects/Unity/HeadsetHolodeckDev -executeMethod HeadsetHolodeck.EditorTests.CachedObjectBatchTests.RunCachedObjectStoreTests -logFile /Users/davidarendash/Documents/Projects/Unity/HeadsetHolodeckDev/unity-cached-object-store-red.log
```

Expected: compile failure for missing `CachedObjectStore` / `CachedObjectRecord`.

- [ ] **Step 3: Implement models and store**

Create `CachedObjectModels.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace Holodeck.Direct
{
    [Serializable]
    public sealed class CachedObjectRecord
    {
        public int schema_version = 1;
        public string object_id = "";
        public string canonical_name = "";
        public List<string> aliases = new List<string>();
        public List<string> tags = new List<string>();
        public string provider = "";
        public string source_prompt = "";
        public string task_id = "";
        public string model_url = "";
        public string created_at = "";
        public string modified_at = "";
        public long file_size_bytes;
        public string model_path = "model.glb";
        public string thumbnail_path = "";
    }
}
```

Create `CachedObjectStore.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace Holodeck.Direct
{
    public sealed class CachedObjectStore : MonoBehaviour
    {
        string _testRootOverride;

        public string RootPath => _testRootOverride ?? Path.Combine(Application.persistentDataPath, "Worlds");
        public string CachedObjectsPath => Path.Combine(RootPath, "CachedObjects");

        public static CachedObjectStore GetOrCreate()
        {
            CachedObjectStore existing = FindFirstObjectByType<CachedObjectStore>(FindObjectsInactive.Include);
            if (existing != null) return existing;
            GameObject go = new GameObject("CachedObjectStore");
            return go.AddComponent<CachedObjectStore>();
        }

        public static CachedObjectStore CreateForTesting(string rootPath)
        {
            GameObject go = new GameObject("CachedObjectStore_Test");
            CachedObjectStore store = go.AddComponent<CachedObjectStore>();
            store._testRootOverride = rootPath;
            return store;
        }

        public CachedObjectRecord SaveGeneratedObject(
            string canonicalName,
            string prompt,
            string providerName,
            string taskId,
            string modelUrl,
            byte[] modelBytes)
        {
            if (modelBytes == null || modelBytes.Length == 0)
                throw new ArgumentException("Generated model bytes are empty.", nameof(modelBytes));

            string now = DateTime.UtcNow.ToString("yyyy-MM-ddTHHmmssZ");
            string id = BuildObjectId(canonicalName, taskId);
            string folder = Path.Combine(CachedObjectsPath, id);
            Directory.CreateDirectory(folder);

            string modelPath = Path.Combine(folder, "model.glb");
            File.WriteAllBytes(modelPath, modelBytes);

            CachedObjectRecord record = new CachedObjectRecord
            {
                object_id = id,
                canonical_name = NormalizeDisplayName(canonicalName),
                provider = providerName ?? "",
                source_prompt = prompt ?? "",
                task_id = taskId ?? "",
                model_url = modelUrl ?? "",
                created_at = now,
                modified_at = now,
                file_size_bytes = modelBytes.LongLength,
                model_path = "model.glb"
            };
            Save(record);
            return record;
        }

        public void Save(CachedObjectRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            string folder = Path.Combine(CachedObjectsPath, record.object_id);
            Directory.CreateDirectory(folder);
            record.modified_at = DateTime.UtcNow.ToString("yyyy-MM-ddTHHmmssZ");
            string json = JsonConvert.SerializeObject(record, Formatting.Indented);
            File.WriteAllText(Path.Combine(folder, "object.json"), json);
        }

        public CachedObjectRecord Load(string objectId)
        {
            string path = Path.Combine(CachedObjectsPath, objectId, "object.json");
            if (!File.Exists(path)) return null;
            return JsonConvert.DeserializeObject<CachedObjectRecord>(File.ReadAllText(path));
        }

        public List<CachedObjectRecord> FindByName(string requestedName)
        {
            string normalized = NormalizeKey(requestedName);
            var results = new List<CachedObjectRecord>();
            if (!Directory.Exists(CachedObjectsPath)) return results;

            foreach (string jsonPath in Directory.GetFiles(CachedObjectsPath, "object.json", SearchOption.AllDirectories))
            {
                CachedObjectRecord record = JsonConvert.DeserializeObject<CachedObjectRecord>(File.ReadAllText(jsonPath));
                if (record == null) continue;
                if (NormalizeKey(record.canonical_name) == normalized || ContainsAlias(record, normalized))
                    results.Add(record);
            }
            return results;
        }

        public string GetModelAbsolutePath(CachedObjectRecord record)
        {
            if (record == null || string.IsNullOrWhiteSpace(record.object_id)) return null;
            string relative = string.IsNullOrWhiteSpace(record.model_path) ? "model.glb" : record.model_path;
            return Path.Combine(CachedObjectsPath, record.object_id, relative);
        }

        static bool ContainsAlias(CachedObjectRecord record, string normalized)
        {
            if (record.aliases == null) return false;
            foreach (string alias in record.aliases)
            {
                if (NormalizeKey(alias) == normalized) return true;
            }
            return false;
        }

        static string BuildObjectId(string name, string taskId)
        {
            string baseName = NormalizeKey(name).Replace(' ', '_');
            if (string.IsNullOrWhiteSpace(baseName)) baseName = "object";
            string suffix = !string.IsNullOrWhiteSpace(taskId)
                ? taskId.GetHashCode().ToString("x")
                : Guid.NewGuid().ToString("N").Substring(0, 8);
            return $"{baseName}_{suffix}";
        }

        static string NormalizeDisplayName(string value) => string.IsNullOrWhiteSpace(value) ? "Generated Object" : value.Trim();

        static string NormalizeKey(string value)
        {
            value = (value ?? "").Trim().ToLowerInvariant();
            if (value.StartsWith("a ")) value = value.Substring(2).Trim();
            if (value.StartsWith("an ")) value = value.Substring(3).Trim();
            if (value.StartsWith("the ")) value = value.Substring(4).Trim();
            return value;
        }
    }
}
```

Modify `WorldConfigStore.cs`:

```csharp
public string CachedObjectsPath => Path.Combine(RootPath, "CachedObjects");
```

- [ ] **Step 4: Run store test to verify it passes**

Run the same Unity command. Expected: `[CachedObjectBatchTests] Cached object store tests passed.`

- [ ] **Step 5: Commit**

```bash
git add Assets/App/Scripts/Direct/ObjectGeneration/CachedObjectModels.cs Assets/App/Scripts/Direct/ObjectGeneration/CachedObjectStore.cs Assets/App/Save/Runtime/WorldConfigStore.cs Assets/App/Editor/CachedObjectBatchTests.cs docs/superpowers/specs/2026-05-20-cached-object-catalog-design.md docs/superpowers/plans/2026-05-20-cached-object-catalog.md
git commit -m "Add cached object store foundation"
```

---

### Task 2: Cache Provider Results And Import Cached GLBs

**Files:**
- Modify: `Assets/App/Scripts/Direct/ObjectGeneration/ObjectGenerationService.cs`
- Create: `Assets/App/Save/Runtime/CachedObjectReference.cs`
- Create: `Assets/App/Save/Runtime/CachedObjectReferenceSerializer.cs`
- Test: extend `Assets/App/Editor/CachedObjectBatchTests.cs`

- [ ] **Step 1: Write failing service/cache test**

Add a test method that creates a `CachedObjectStore`, saves a generated object, attaches `CachedObjectReference` to a GameObject, and verifies the serializer writes the cached id.

- [ ] **Step 2: Run test to verify it fails**

Expected: missing `CachedObjectReference`.

- [ ] **Step 3: Implement cached object reference**

Create `CachedObjectReference.cs`:

```csharp
using UnityEngine;

namespace Holodeck.Save
{
    public sealed class CachedObjectReference : MonoBehaviour
    {
        public string cachedObjectId = "";
        public string cachedModelPath = "";
    }
}
```

Create `CachedObjectReferenceSerializer.cs`:

```csharp
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Scripting;

namespace Holodeck.Save
{
    [Preserve]
    public sealed class CachedObjectReferenceSerializer : IComponentSerializer
    {
        public string TypeName => "CachedObjectReference";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void AutoRegister() => WorldConfigComponentRegistry.Register(new CachedObjectReferenceSerializer());

        public JObject Save(GameObject go)
        {
            CachedObjectReference reference = go.GetComponent<CachedObjectReference>();
            if (reference == null || string.IsNullOrWhiteSpace(reference.cachedObjectId)) return null;
            return new JObject
            {
                ["cached_object_id"] = reference.cachedObjectId,
                ["cached_model_path"] = reference.cachedModelPath ?? ""
            };
        }

        public void Restore(GameObject go, JObject data, RestorationContext ctx)
        {
            CachedObjectReference reference = go.GetComponent<CachedObjectReference>() ?? go.AddComponent<CachedObjectReference>();
            reference.cachedObjectId = data?["cached_object_id"]?.Value<string>() ?? "";
            reference.cachedModelPath = data?["cached_model_path"]?.Value<string>() ?? "";
        }
    }
}
```

- [ ] **Step 4: Cache successful provider results**

In `ObjectGenerationService`, add `public CachedObjectStore cachedObjectStore;`, resolve it in `ResolveDependencies`, and after provider success call:

```csharp
CachedObjectRecord cached = cachedObjectStore.SaveGeneratedObject(
    request.objectName,
    request.prompt,
    result.providerName,
    result.taskId,
    result.modelUrl,
    result.modelBytes);
```

Pass the cached record into import, then attach `CachedObjectReference` after instantiation:

```csharp
CachedObjectReference reference = root.GetComponent<CachedObjectReference>() ?? root.AddComponent<CachedObjectReference>();
reference.cachedObjectId = cached.object_id;
reference.cachedModelPath = cached.model_path;
```

- [ ] **Step 5: Run tests**

Run cached object tests and speech intent tests. Expected both pass.

- [ ] **Step 6: Commit**

```bash
git add Assets/App/Scripts/Direct/ObjectGeneration/ObjectGenerationService.cs Assets/App/Save/Runtime/CachedObjectReference.cs Assets/App/Save/Runtime/CachedObjectReferenceSerializer.cs Assets/App/Editor/CachedObjectBatchTests.cs
git commit -m "Cache generated object models"
```

---

### Task 3: Restore Cached Objects From World JSON

**Files:**
- Modify: `Assets/App/Save/WorldConfigRestorer.cs`
- Modify: `Assets/App/Scripts/Direct/ObjectGeneration/ObjectGenerationService.cs`
- Test: extend `Assets/App/Editor/CachedObjectBatchTests.cs`

- [ ] **Step 1: Write failing restore test**

Add a test for resolving a saved `CachedObjectReference` component into an absolute GLB path. Use a tiny fake file for path existence; do not require glTFast import in the unit test.

- [ ] **Step 2: Run test to verify it fails**

Expected: missing restore helper.

- [ ] **Step 3: Add cached import helper**

Add a public coroutine to `ObjectGenerationService`:

```csharp
public IEnumerator ImportCachedObject(CachedObjectRecord record, VoiceIntentCommand placementCommand, SpatialSnapshot spatial, Action<GameObject, string> onComplete)
{
    string path = cachedObjectStore.GetModelAbsolutePath(record);
    if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
    {
        onComplete?.Invoke(null, "Cached object model is missing.");
        yield break;
    }
    byte[] bytes = File.ReadAllBytes(path);
    var request = new ObjectGenerationRequest { objectName = record.canonical_name, prompt = record.source_prompt };
    var result = new ObjectGenerationResult { success = true, providerName = record.provider, taskId = record.task_id, modelUrl = record.model_url, modelBytes = bytes };
    yield return ImportGlbCoroutine(result, request, record, placementCommand, spatial, onComplete);
}
```

- [ ] **Step 4: Restore cached GLB before components**

In `WorldConfigRestorer.RestoreObject`, detect saved component type `CachedObjectReference`, load the record, import the GLB, then apply all components to the imported root. If the file is missing, log warning and restore a placeholder GameObject.

- [ ] **Step 5: Run tests**

Expected cached object tests pass; a manual world restore with an existing cached object should recreate the object.

- [ ] **Step 6: Commit**

```bash
git add Assets/App/Save/WorldConfigRestorer.cs Assets/App/Scripts/Direct/ObjectGeneration/ObjectGenerationService.cs Assets/App/Editor/CachedObjectBatchTests.cs
git commit -m "Restore world objects from cached models"
```

---

### Task 4: Cache Lookup Before API Generation

**Files:**
- Modify: `Assets/App/Command/SpeechIntent/Runtime/WorldActionDispatcher.cs`
- Create: `Assets/App/Command/SpeechIntent/Runtime/CachedObjectChoiceController.cs`
- Test: extend `Assets/App/Editor/CachedObjectBatchTests.cs`

- [ ] **Step 1: Write failing lookup/decision test**

Test that a cached `teddy bear` match creates a pending object choice instead of starting generation.

- [ ] **Step 2: Run test to verify it fails**

Expected: missing `CachedObjectChoiceController`.

- [ ] **Step 3: Implement pending choice controller**

Create `CachedObjectChoiceController` with:

```csharp
public sealed class CachedObjectChoiceController : MonoBehaviour
{
    public bool HasPendingChoice { get; private set; }
    public VoiceIntentCommand PendingCommand { get; private set; }
    public SpatialSnapshot PendingSpatial { get; private set; }
    public List<CachedObjectRecord> PendingMatches { get; private set; } = new List<CachedObjectRecord>();

    public void BeginChoice(VoiceIntentCommand command, SpatialSnapshot spatial, List<CachedObjectRecord> matches) { ... }
    public bool TryConsumeUseSaved(out VoiceIntentCommand command, out SpatialSnapshot spatial, out CachedObjectRecord record) { ... }
    public bool TryConsumeCreateNew(out VoiceIntentCommand command, out SpatialSnapshot spatial) { ... }
    public void Cancel() { ... }
}
```

- [ ] **Step 4: Integrate dispatcher lookup**

In `HandlePlaceObject`, before API generation:

```csharp
List<CachedObjectRecord> matches = cachedObjectStore.FindByName(objectName);
if (matches.Count > 0)
{
    cachedObjectChoiceController.BeginChoice(command, spatial, matches);
    command.spoken_response = matches.Count == 1
        ? $"I found a saved {objectName}. Use it, or create a new one?"
        : $"I found {matches.Count} saved {objectName} objects. Which one should I use, or should I create a new one?";
    return;
}
```

- [ ] **Step 5: Run tests**

Expected: cached match creates pending choice; no match starts provider generation.

- [ ] **Step 6: Commit**

```bash
git add Assets/App/Command/SpeechIntent/Runtime/WorldActionDispatcher.cs Assets/App/Command/SpeechIntent/Runtime/CachedObjectChoiceController.cs Assets/App/Editor/CachedObjectBatchTests.cs
git commit -m "Ask before regenerating cached objects"
```

---

### Task 5: Voice And UI Decision Handling

**Files:**
- Modify: `Assets/App/Command/SpeechIntent/Runtime/VoiceCommandRouter.cs`
- Modify: `Assets/App/Command/SpeechIntent/Runtime/LocalTypedIntentParser.cs`
- Create: `Assets/App/Command/SpeechIntent/Runtime/UI/CachedObjectChoicePanel.cs`
- Modify: `Assets/App/Command/SpeechIntent/Runtime/WorldActionDispatcher.cs`

- [ ] **Step 1: Add local parser support for choice replies**

Add intent handling for short replies:

```csharp
if (ContainsAny(lower, "use saved", "use the saved one", "use that one", "use it"))
{
    command.intent = VoiceIntentType.SelectCachedObject;
    command.object_choice_action = "use_saved";
}
if (ContainsAny(lower, "create new", "make a new one", "generate a new one"))
{
    command.intent = VoiceIntentType.SelectCachedObject;
    command.object_choice_action = "create_new";
}
```

If schema changes are needed, add `SelectCachedObject` and `object_choice_action` to `VoiceIntentSchemas`.

- [ ] **Step 2: Dispatch choice replies**

In `WorldActionDispatcher.Execute`, add a `SelectCachedObject` case:

- `use_saved`: import the selected cached object and place it using pending placement.
- `create_new`: start provider generation using pending placement.
- `cancel`: clear pending choice.

- [ ] **Step 3: Add simple choice panel**

Create `CachedObjectChoicePanel` with buttons wired to public methods:

```csharp
public void Show(IReadOnlyList<CachedObjectRecord> matches, Action<CachedObjectRecord> onUse, Action onCreateNew, Action onCancel)
public void Hide()
```

Use text labels and optional thumbnail later; first pass can show name/provider/date.

- [ ] **Step 4: Run tests and manual check**

Manual check:

1. Generate a teddy bear.
2. Say `create a teddy bear in front of me`.
3. Confirm system asks whether to use saved or create new.
4. Say `use saved`.
5. Confirm cached GLB is imported and placed in front of the headset.

- [ ] **Step 5: Commit**

```bash
git add Assets/App/Command/SpeechIntent/Runtime/VoiceIntentSchemas.cs Assets/App/Command/SpeechIntent/Runtime/LocalTypedIntentParser.cs Assets/App/Command/SpeechIntent/Runtime/VoiceCommandRouter.cs Assets/App/Command/SpeechIntent/Runtime/WorldActionDispatcher.cs Assets/App/Command/SpeechIntent/Runtime/UI/CachedObjectChoicePanel.cs
git commit -m "Add cached object saved-or-new choice flow"
```

---

### Task 6: Object Catalog UI

**Files:**
- Create: `Assets/App/Command/SpeechIntent/Runtime/UI/CachedObjectCatalogPanel.cs`
- Create: `Assets/App/Command/SpeechIntent/Runtime/UI/CachedObjectCardUI.cs`
- Optional editor setup later: `Assets/App/Editor/CachedObjectCatalogUiSetup.cs`

- [ ] **Step 1: Create card UI script**

Expose labels, thumbnail, and buttons:

```csharp
public void SetData(CachedObjectRecord record, Texture thumbnail, UnityAction onUse, UnityAction onDelete, UnityAction onRename)
```

- [ ] **Step 2: Create catalog panel script**

Load `CachedObjectStore.ListAll()` and instantiate card prefabs under a content root. Add `Refresh()`, `Use(record)`, and `Delete(record)`.

- [ ] **Step 3: Manual scene wiring**

Add panel to LCARS UI by hand first. Assign card prefab/content root/buttons in Inspector.

- [ ] **Step 4: Commit**

```bash
git add Assets/App/Command/SpeechIntent/Runtime/UI/CachedObjectCatalogPanel.cs Assets/App/Command/SpeechIntent/Runtime/UI/CachedObjectCardUI.cs
git commit -m "Add cached object catalog UI scripts"
```

---

## Self-Review

- Spec coverage: storage, world references, creation flow, saved/new decision, restore, UI, and tests are each represented by at least one task.
- Scope: object thumbnails are supported by metadata and UI fields, but automatic thumbnail rendering is intentionally deferred until the cache/restore loop works.
- Type consistency: `CachedObjectRecord`, `CachedObjectStore`, `CachedObjectReference`, and `CachedObjectChoiceController` are named consistently across tasks.
- Secrets: `.env` is never staged; metadata stores provider/task info only.
