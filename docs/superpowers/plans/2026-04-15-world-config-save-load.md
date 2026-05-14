# World Configuration Save/Load Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Persist world configurations (world source, placed objects, lighting, voice prompt history) as self-contained folders under `Application.persistentDataPath/Worlds/`, with full scene restoration, voice commands, and a My Worlds UI panel.

**Architecture:** A pure-C# `WorldConfig` data model is serialized to `world.json` per config folder via Newtonsoft JSON. A static `WorldConfigComponentRegistry` maps type-string keys to `IComponentSerializer` handlers for extensible per-object property save/restore. Three MonoBehaviours on `Systems` — `WorldConfigStore` (filesystem CRUD + startup migration), `WorldConfigRestorer` (scene restoration), and `WorldConfigAutoSave` (event-driven saves) — orchestrate the system. A `MyWorldsPanel` follows the existing `LocalFileBrowserPanel` pattern.

**Tech Stack:** C# / Unity 6, Newtonsoft JSON (`com.unity.nuget.newtonsoft-json`), TextMeshPro, `System.IO`, `System.Threading.Tasks.Task`, existing `WorldLabsWorldManager`, `LightRigController`, `ObjectPlacementController`, `SpeechIntentTrackable`, `UiPanelController`, `FileEntryItemUI` pattern.

**Spec:** `docs/superpowers/specs/2026-04-14-world-config-save-load-design.md`

---

## File Map

**New files:**
- `Assets/App/Save/Runtime/WorldConfig.cs` — data classes
- `Assets/App/Save/Runtime/RestorationContext.cs` — context passed to serializers
- `Assets/App/Save/Runtime/IComponentSerializer.cs` — interface
- `Assets/App/Save/Runtime/WorldConfigComponentRegistry.cs` — static registry
- `Assets/App/Save/Runtime/TransformSerializer.cs` — saves/restores Transform
- `Assets/App/Save/Runtime/AudioClipPathHolder.cs` — lightweight component tracking clip path
- `Assets/App/Save/Runtime/AudioSourceSerializer.cs` — saves/restores AudioSource
- `Assets/App/Save/Runtime/WorldConfigStore.cs` — filesystem CRUD + startup scan
- `Assets/App/Save/Runtime/WorldConfigRestorer.cs` — full scene restoration
- `Assets/App/Save/Runtime/WorldConfigAutoSave.cs` — event-driven auto-save + WorldLabs reconcile
- `Assets/App/Command/SpeechIntent/Runtime/UI/WorldConfigCardUI.cs` — card component
- `Assets/App/Command/SpeechIntent/Runtime/UI/MyWorldsPanel.cs` — panel controller
- `Assets/App/Editor/WorldConfigSceneSetup.cs` — wires everything into Systems
- `Assets/App/Save/Tests/WorldConfigTests.asmdef` — test assembly definition
- `Assets/App/Save/Tests/WorldConfigSerializationTests.cs` — JSON round-trip tests
- `Assets/App/Save/Tests/WorldConfigRegistryTests.cs` — registry tests
- `Assets/App/Save/Tests/WorldConfigStoreTests.cs` — CRUD + scan tests

**Modified files:**
- `Assets/App/Command/SpeechIntent/Runtime/SpeechIntentTrackable.cs` — add `configInstanceId`
- `Assets/App/Command/SpeechIntent/Runtime/ObjectPlacementController.cs` — make `FindPrefab` public
- `Assets/App/Command/SpeechIntent/Runtime/WorldActionDispatcher.cs` — add `OnObjectMutated` event + new fields + new intent handlers
- `Assets/App/Command/SpeechIntent/Runtime/VoiceIntentSchemas.cs` — add `SaveWorldConfig`/`LoadWorldConfig` + `config_name`
- `Assets/App/Command/SpeechIntent/Runtime/OpenAiSpeechIntentService.cs` — add new intents + `config_name` to JSON schema
- `Assets/App/Command/SpeechIntent/Runtime/OpenAiSpeechIntentConfig.cs` — add routing hints
- `Packages/com.worldlabs.gaussian-splatting/Runtime/WorldLabs/WorldBrowserController.cs` — add bookmark indicator

---

## Task 1: Data Classes + Test Assembly

**Files:**
- Create: `Assets/App/Save/Runtime/WorldConfig.cs`
- Create: `Assets/App/Save/Tests/WorldConfigTests.asmdef`
- Create: `Assets/App/Save/Tests/WorldConfigSerializationTests.cs`

- [ ] **Step 1: Create the `Assets/App/Save/Runtime/` and `Assets/App/Save/Tests/` directories**

In the Unity Editor: right-click `Assets/App/Save` in the Project window → these will be created when you save the files below. Alternatively, create them via Finder/Explorer first.

- [ ] **Step 2: Create `WorldConfig.cs`**

```csharp
// Assets/App/Save/Runtime/WorldConfig.cs
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Holodeck.Save
{
    public class WorldConfig
    {
        public int schema_version = 1;
        public string config_id;
        public string display_name;
        public string created_at;   // ISO 8601 UTC, e.g. "2026-04-15T103000Z"
        public string modified_at;  // ISO 8601 UTC
        public WorldSourceData world_source;
        public string generation_model;
        public List<PromptEntry> prompts = new List<PromptEntry>();
        public List<SavedObject> objects = new List<SavedObject>();
        public LightingData lighting;  // null if lighting not set
    }

    public class WorldSourceData
    {
        public string type;          // "worldlabs" | "local_splat" | "local_pano" | "url"
        public string world_id;      // WorldLabs world_id; null for local/url
        public string display_name;  // human-readable world name
        public string url;           // for type="url"
        public string cached_splat;  // relative path from config folder (e.g. "../CachedWorlds/x.spz"); null if not cached
        public string cached_pano;
    }

    public class PromptEntry
    {
        public string timestamp;    // ISO 8601 UTC
        public string type;         // "world_creation" | "voice_command"
        public string intent;       // VoiceIntentType name string
        public string transcript;
    }

    public class SavedObject
    {
        public string instance_id;
        public string prefab_name;   // null for programmatically created objects (e.g. audio sources)
        public string display_name;
        public List<SavedComponent> components = new List<SavedComponent>();
    }

    public class SavedComponent
    {
        public string type;
        public JObject data;         // free-form per component type — Newtonsoft handles JObject natively
    }

    public class LightingData
    {
        public string preset;
        public float sun_azimuth;
        public float sun_elevation;
    }
}
```

- [ ] **Step 3: Create the test assembly definition**

```json
// Assets/App/Save/Tests/WorldConfigTests.asmdef
{
    "name": "Holodeck.Save.Tests",
    "rootNamespace": "Holodeck.Save.Tests",
    "references": [
        "UnityEngine.TestRunner",
        "UnityEditor.TestRunner"
    ],
    "includePlatforms": [
        "Editor"
    ],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": true,
    "precompiledReferences": [
        "nunit.framework.dll"
    ],
    "autoReferenced": false,
    "defineConstraints": [],
    "versionDefines": [],
    "noEngineReferences": false
}
```

- [ ] **Step 4: Write serialization tests**

```csharp
// Assets/App/Save/Tests/WorldConfigSerializationTests.cs
using NUnit.Framework;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Holodeck.Save;
using System.Collections.Generic;

namespace Holodeck.Save.Tests
{
    public class WorldConfigSerializationTests
    {
        [Test]
        public void WorldConfig_JsonRoundTrip_PreservesAllFields()
        {
            var config = new WorldConfig
            {
                config_id    = "Beach_2026-04-15T103000Z",
                display_name = "Beach",
                created_at   = "2026-04-15T10:30:00Z",
                modified_at  = "2026-04-15T11:00:00Z",
                generation_model = "Standard",
                world_source = new WorldSourceData
                {
                    type         = "worldlabs",
                    world_id     = "abc123",
                    display_name = "Sunny Beach",
                    cached_splat = "../CachedWorlds/abc123.spz",
                    cached_pano  = null
                },
                lighting = new LightingData { preset = "Golden Hour", sun_azimuth = 220f, sun_elevation = 35f }
            };
            config.prompts.Add(new PromptEntry
            {
                timestamp  = "2026-04-15T10:30:00Z",
                type       = "world_creation",
                intent     = "GenerateWorld",
                transcript = "a sunny beach with palm trees"
            });
            config.objects.Add(new SavedObject
            {
                instance_id  = "chair_abc123",
                prefab_name  = "beach chair",
                display_name = "Beach Chair",
                components   = new List<SavedComponent>
                {
                    new SavedComponent
                    {
                        type = "Transform",
                        data = JObject.Parse("{\"position\":{\"x\":1.2,\"y\":0,\"z\":2.5}}")
                    }
                }
            });

            string json = JsonConvert.SerializeObject(config, Formatting.Indented);
            var restored = JsonConvert.DeserializeObject<WorldConfig>(json);

            Assert.AreEqual(config.config_id,                 restored.config_id);
            Assert.AreEqual(config.display_name,              restored.display_name);
            Assert.AreEqual(config.world_source.world_id,     restored.world_source.world_id);
            Assert.AreEqual(config.world_source.cached_splat, restored.world_source.cached_splat);
            Assert.IsNull(restored.world_source.cached_pano);
            Assert.AreEqual(1, restored.prompts.Count);
            Assert.AreEqual("a sunny beach with palm trees", restored.prompts[0].transcript);
            Assert.AreEqual(1, restored.objects.Count);
            Assert.AreEqual("chair_abc123", restored.objects[0].instance_id);
            Assert.AreEqual("Transform", restored.objects[0].components[0].type);
            Assert.IsNotNull(restored.objects[0].components[0].data);
            Assert.AreEqual(220f, restored.lighting.sun_azimuth, 0.001f);
        }

        [Test]
        public void WorldConfig_NullOptionalFields_SerializesCleanly()
        {
            var config = new WorldConfig
            {
                config_id    = "MinimalConfig",
                display_name = "Minimal",
                world_source = new WorldSourceData { type = "local_splat", cached_splat = "../CachedWorlds/file.spz" }
            };

            string json = JsonConvert.SerializeObject(config);
            var restored = JsonConvert.DeserializeObject<WorldConfig>(json);

            Assert.IsNull(restored.lighting);
            Assert.IsNull(restored.world_source.world_id);
            Assert.AreEqual(0, restored.prompts.Count);
            Assert.AreEqual(0, restored.objects.Count);
        }
    }
}
```

- [ ] **Step 5: Run tests to confirm they pass**

In Unity Editor: `Window > General > Test Runner > EditMode > Run All`  
Expected: Both tests pass (green).

- [ ] **Step 6: Commit**

```bash
git add Assets/App/Save/
git commit -m "feat: add WorldConfig data classes and serialization tests"
```

---

## Task 2: Component Registry

**Files:**
- Create: `Assets/App/Save/Runtime/RestorationContext.cs`
- Create: `Assets/App/Save/Runtime/IComponentSerializer.cs`
- Create: `Assets/App/Save/Runtime/WorldConfigComponentRegistry.cs`
- Create: `Assets/App/Save/Tests/WorldConfigRegistryTests.cs`

- [ ] **Step 1: Create `RestorationContext.cs`**

```csharp
// Assets/App/Save/Runtime/RestorationContext.cs
namespace Holodeck.Save
{
    /// <summary>
    /// Passed to IComponentSerializer.Restore so serializers can resolve
    /// relative asset paths (e.g. ../CachedWorlds/sound.mp3) to absolute paths.
    /// </summary>
    public class RestorationContext
    {
        /// <summary>Absolute path to the config folder containing world.json.</summary>
        public string ConfigFolderPath;
        public WorldConfig Config;
    }
}
```

- [ ] **Step 2: Create `IComponentSerializer.cs`**

```csharp
// Assets/App/Save/Runtime/IComponentSerializer.cs
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Holodeck.Save
{
    public interface IComponentSerializer
    {
        /// <summary>Unique string identifying this component type in world.json.</summary>
        string TypeName { get; }

        /// <summary>
        /// Snapshot the relevant component(s) on <paramref name="go"/>.
        /// Returns null if this serializer is not applicable to the GameObject.
        /// </summary>
        JObject Save(GameObject go);

        /// <summary>Apply saved <paramref name="data"/> to <paramref name="go"/>.</summary>
        void Restore(GameObject go, JObject data, RestorationContext ctx);
    }
}
```

- [ ] **Step 3: Create `WorldConfigComponentRegistry.cs`**

```csharp
// Assets/App/Save/Runtime/WorldConfigComponentRegistry.cs
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Holodeck.Save
{
    public static class WorldConfigComponentRegistry
    {
        static readonly Dictionary<string, IComponentSerializer> _serializers
            = new Dictionary<string, IComponentSerializer>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Register a serializer. Called from [RuntimeInitializeOnLoadMethod] in each serializer file.</summary>
        public static void Register(IComponentSerializer serializer)
        {
            if (serializer == null) throw new ArgumentNullException(nameof(serializer));
            _serializers[serializer.TypeName] = serializer;
        }

        /// <summary>
        /// Run all registered serializers against <paramref name="go"/> and return
        /// any non-null results as a SavedComponent list.
        /// </summary>
        public static List<SavedComponent> SaveAll(GameObject go)
        {
            var result = new List<SavedComponent>();
            foreach (IComponentSerializer s in _serializers.Values)
            {
                JObject data = s.Save(go);
                if (data != null)
                    result.Add(new SavedComponent { type = s.TypeName, data = data });
            }
            return result;
        }

        /// <summary>Apply each SavedComponent to <paramref name="go"/> using the registered serializer.</summary>
        public static void RestoreAll(GameObject go, List<SavedComponent> components, RestorationContext ctx)
        {
            if (components == null) return;
            foreach (SavedComponent c in components)
            {
                if (_serializers.TryGetValue(c.type, out IComponentSerializer s))
                    s.Restore(go, c.data, ctx);
                else
                    Debug.LogWarning($"[WorldConfigComponentRegistry] No serializer registered for type '{c.type}'");
            }
        }

        /// <summary>Used only in tests — clears all registered serializers.</summary>
        internal static void ClearForTesting() => _serializers.Clear();

        /// <summary>Used only in tests — returns the count of registered serializers.</summary>
        internal static int CountForTesting() => _serializers.Count;
    }
}
```

- [ ] **Step 4: Write registry tests**

```csharp
// Assets/App/Save/Tests/WorldConfigRegistryTests.cs
using NUnit.Framework;
using Newtonsoft.Json.Linq;
using Holodeck.Save;
using UnityEngine;
using System.Collections.Generic;

namespace Holodeck.Save.Tests
{
    public class WorldConfigRegistryTests
    {
        // Minimal in-test serializer
        class FakeSerializer : IComponentSerializer
        {
            public string TypeName => "Fake";
            public bool SaveCalled;
            public bool RestoreCalled;
            public JObject Save(GameObject go) { SaveCalled = true; return new JObject { ["value"] = 42 }; }
            public void Restore(GameObject go, JObject data, RestorationContext ctx) { RestoreCalled = true; }
        }

        [SetUp]
        public void SetUp() => WorldConfigComponentRegistry.ClearForTesting();

        [Test]
        public void Register_AddsSerializer()
        {
            WorldConfigComponentRegistry.Register(new FakeSerializer());
            Assert.AreEqual(1, WorldConfigComponentRegistry.CountForTesting());
        }

        [Test]
        public void SaveAll_CallsSaveOnRegisteredSerializer()
        {
            var fake = new FakeSerializer();
            WorldConfigComponentRegistry.Register(fake);
            var go = new GameObject("TestGO");
            try
            {
                List<SavedComponent> result = WorldConfigComponentRegistry.SaveAll(go);
                Assert.IsTrue(fake.SaveCalled);
                Assert.AreEqual(1, result.Count);
                Assert.AreEqual("Fake", result[0].type);
                Assert.AreEqual(42, result[0].data["value"].Value<int>());
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void RestoreAll_CallsRestoreOnMatchingSerializer()
        {
            var fake = new FakeSerializer();
            WorldConfigComponentRegistry.Register(fake);
            var go = new GameObject("TestGO");
            var ctx = new RestorationContext { ConfigFolderPath = "/tmp", Config = new WorldConfig() };
            var components = new List<SavedComponent>
            {
                new SavedComponent { type = "Fake", data = new JObject() }
            };
            try
            {
                WorldConfigComponentRegistry.RestoreAll(go, components, ctx);
                Assert.IsTrue(fake.RestoreCalled);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void RestoreAll_UnknownType_LogsWarningAndDoesNotThrow()
        {
            var go = new GameObject("TestGO");
            var ctx = new RestorationContext { ConfigFolderPath = "/tmp", Config = new WorldConfig() };
            var components = new List<SavedComponent>
            {
                new SavedComponent { type = "DoesNotExist", data = new JObject() }
            };
            try
            {
                Assert.DoesNotThrow(() =>
                    WorldConfigComponentRegistry.RestoreAll(go, components, ctx));
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
```

- [ ] **Step 5: Run tests**

`Window > General > Test Runner > EditMode > Run All`  
Expected: All registry tests pass.

- [ ] **Step 6: Commit**

```bash
git add Assets/App/Save/
git commit -m "feat: add component registry with interface and tests"
```

---

## Task 3: Built-in Serializers

**Files:**
- Create: `Assets/App/Save/Runtime/AudioClipPathHolder.cs`
- Create: `Assets/App/Save/Runtime/TransformSerializer.cs`
- Create: `Assets/App/Save/Runtime/AudioSourceSerializer.cs`

> Note: These serializers self-register via `[RuntimeInitializeOnLoadMethod]`. No central list to maintain — adding a new serializer just means implementing `IComponentSerializer` and calling `Register` in that attribute.

- [ ] **Step 1: Create `AudioClipPathHolder.cs`**

```csharp
// Assets/App/Save/Runtime/AudioClipPathHolder.cs
using UnityEngine;

namespace Holodeck.Save
{
    /// <summary>
    /// Lightweight component that stores the file path of a loaded AudioClip.
    /// AudioSourceSerializer reads this path on Save; WorldConfigRestorer uses
    /// absolutePath to reload the clip on Restore.
    /// </summary>
    public class AudioClipPathHolder : MonoBehaviour
    {
        [Tooltip("Relative path from config folder, e.g. ../CachedWorlds/sound.mp3")]
        public string clipPath = "";
        [Tooltip("Resolved absolute device path — populated by AudioSourceSerializer.Restore")]
        public string absolutePath = "";
    }
}
```

- [ ] **Step 2: Create `TransformSerializer.cs`**

```csharp
// Assets/App/Save/Runtime/TransformSerializer.cs
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Scripting;

namespace Holodeck.Save
{
    [Preserve]
    public class TransformSerializer : IComponentSerializer
    {
        public string TypeName => "Transform";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void AutoRegister() => WorldConfigComponentRegistry.Register(new TransformSerializer());

        public JObject Save(GameObject go)
        {
            Transform t = go.transform;
            return new JObject
            {
                ["position"] = Vec3ToJObject(t.localPosition),
                ["rotation"] = QuatToJObject(t.localRotation),
                ["scale"]    = Vec3ToJObject(t.localScale)
            };
        }

        public void Restore(GameObject go, JObject data, RestorationContext ctx)
        {
            Transform t = go.transform;
            if (data["position"] is JObject pos) t.localPosition = JObjectToVec3(pos);
            if (data["rotation"] is JObject rot) t.localRotation = JObjectToQuat(rot);
            if (data["scale"]    is JObject scl) t.localScale    = JObjectToVec3(scl);
        }

        static JObject Vec3ToJObject(Vector3 v) =>
            new JObject { ["x"] = v.x, ["y"] = v.y, ["z"] = v.z };

        static JObject QuatToJObject(Quaternion q) =>
            new JObject { ["x"] = q.x, ["y"] = q.y, ["z"] = q.z, ["w"] = q.w };

        static Vector3 JObjectToVec3(JObject j) =>
            new Vector3(j["x"].Value<float>(), j["y"].Value<float>(), j["z"].Value<float>());

        static Quaternion JObjectToQuat(JObject j) =>
            new Quaternion(j["x"].Value<float>(), j["y"].Value<float>(),
                           j["z"].Value<float>(), j["w"].Value<float>());
    }
}
```

- [ ] **Step 3: Create `AudioSourceSerializer.cs`**

```csharp
// Assets/App/Save/Runtime/AudioSourceSerializer.cs
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Scripting;

namespace Holodeck.Save
{
    /// <summary>
    /// Saves AudioSource settings and clip path. Restore sets all properties
    /// and records the absolute path in AudioClipPathHolder; actual clip loading
    /// is deferred to WorldConfigRestorer.LoadAudioClipsAsync() (needs coroutine).
    /// </summary>
    [Preserve]
    public class AudioSourceSerializer : IComponentSerializer
    {
        public string TypeName => "AudioSource";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void AutoRegister() => WorldConfigComponentRegistry.Register(new AudioSourceSerializer());

        public JObject Save(GameObject go)
        {
            AudioSource src = go.GetComponent<AudioSource>();
            if (src == null) return null;

            string clipPath = "";
            AudioClipPathHolder holder = go.GetComponent<AudioClipPathHolder>();
            if (holder != null) clipPath = holder.clipPath ?? "";

            return new JObject
            {
                ["clip_path"]     = clipPath,
                ["volume"]        = src.volume,
                ["loop"]          = src.loop,
                ["spatial_blend"] = src.spatialBlend
            };
        }

        public void Restore(GameObject go, JObject data, RestorationContext ctx)
        {
            AudioSource src = go.GetComponent<AudioSource>() ?? go.AddComponent<AudioSource>();
            src.volume       = data["volume"]?.Value<float>()        ?? 1f;
            src.loop         = data["loop"]?.Value<bool>()           ?? false;
            src.spatialBlend = data["spatial_blend"]?.Value<float>() ?? 0f;
            src.playOnAwake  = false;  // WorldConfigRestorer will Play() after clip loads

            string relative = data["clip_path"]?.Value<string>();
            if (string.IsNullOrEmpty(relative)) return;

            string absolute = Path.GetFullPath(Path.Combine(ctx.ConfigFolderPath, relative));
            AudioClipPathHolder holder = go.GetComponent<AudioClipPathHolder>()
                                      ?? go.AddComponent<AudioClipPathHolder>();
            holder.clipPath    = relative;
            holder.absolutePath = absolute;
        }
    }
}
```

- [ ] **Step 4: Verify Unity compiles without errors**

Save all files. Switch to Unity Editor and wait for compilation. Check Console for errors.  
Expected: No compile errors.

- [ ] **Step 5: Commit**

```bash
git add Assets/App/Save/Runtime/AudioClipPathHolder.cs \
        Assets/App/Save/Runtime/TransformSerializer.cs \
        Assets/App/Save/Runtime/AudioSourceSerializer.cs
git commit -m "feat: add Transform and AudioSource component serializers with auto-registration"
```

---

## Task 4: WorldConfigStore — CRUD + In-Memory List

**Files:**
- Create: `Assets/App/Save/Runtime/WorldConfigStore.cs`
- Create: `Assets/App/Save/Tests/WorldConfigStoreTests.cs`

- [ ] **Step 1: Write the failing test first**

```csharp
// Assets/App/Save/Tests/WorldConfigStoreTests.cs
using NUnit.Framework;
using System.IO;
using System.Threading.Tasks;
using Holodeck.Save;
using Newtonsoft.Json;

namespace Holodeck.Save.Tests
{
    public class WorldConfigStoreTests
    {
        string _tempRoot;
        WorldConfigStore _store;

        [SetUp]
        public void SetUp()
        {
            _tempRoot = Path.Combine(Path.GetTempPath(), "HolodeckSaveTests_" + System.Guid.NewGuid().ToString("N")[..8]);
            _store = WorldConfigStore.CreateForTesting(_tempRoot);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_tempRoot))
                Directory.Delete(_tempRoot, recursive: true);
        }

        [Test]
        public void CreateConfig_WritesJsonToDisk()
        {
            var source = new WorldSourceData { type = "worldlabs", world_id = "abc123", display_name = "Beach" };
            var prompt = new PromptEntry { timestamp = "2026-04-15T10:00:00Z", type = "world_creation", transcript = "a beach" };

            WorldConfig config = _store.CreateConfig(source, "My Beach", prompt);

            string expectedPath = Path.Combine(_tempRoot, config.config_id, "world.json");
            Assert.IsTrue(File.Exists(expectedPath), $"world.json not found at {expectedPath}");

            var loaded = JsonConvert.DeserializeObject<WorldConfig>(File.ReadAllText(expectedPath));
            Assert.AreEqual("My Beach", loaded.display_name);
            Assert.AreEqual("worldlabs", loaded.world_source.type);
            Assert.AreEqual("abc123", loaded.world_source.world_id);
            Assert.AreEqual(1, loaded.prompts.Count);
        }

        [Test]
        public void SaveConfig_UpdatesModifiedAt()
        {
            var source = new WorldSourceData { type = "local_splat" };
            WorldConfig config = _store.CreateConfig(source, "Test", null);
            string originalModifiedAt = config.modified_at;

            System.Threading.Thread.Sleep(10);
            config.display_name = "Updated";
            _store.SaveConfig(config);

            string json = File.ReadAllText(Path.Combine(_tempRoot, config.config_id, "world.json"));
            var reloaded = JsonConvert.DeserializeObject<WorldConfig>(json);
            Assert.AreEqual("Updated", reloaded.display_name);
            Assert.AreNotEqual(originalModifiedAt, reloaded.modified_at);
        }

        [Test]
        public void DeleteConfig_RemovesFolderAndInMemory()
        {
            var source = new WorldSourceData { type = "local_splat" };
            WorldConfig config = _store.CreateConfig(source, "ToDelete", null);
            string folder = Path.Combine(_tempRoot, config.config_id);
            Assert.IsTrue(Directory.Exists(folder));

            _store.DeleteConfig(config.config_id);

            Assert.IsFalse(Directory.Exists(folder));
            Assert.AreEqual(0, _store.ListConfigs().Count);
        }

        [Test]
        public void ListConfigs_ReturnsAllCreated()
        {
            _store.CreateConfig(new WorldSourceData { type = "local_splat" }, "A", null);
            _store.CreateConfig(new WorldSourceData { type = "worldlabs", world_id = "x" }, "B", null);

            Assert.AreEqual(2, _store.ListConfigs().Count);
        }

        [Test]
        public void HasConfigForWorldId_ReturnsTrueWhenExists()
        {
            _store.CreateConfig(new WorldSourceData { type = "worldlabs", world_id = "wl_abc" }, "Test", null);
            Assert.IsTrue(_store.HasConfigForWorldId("wl_abc"));
            Assert.IsFalse(_store.HasConfigForWorldId("wl_xyz"));
        }

        [Test]
        public void ForkConfig_CreatesSeparateFolderWithSameObjects()
        {
            var source = new WorldSourceData { type = "worldlabs", world_id = "wl1", display_name = "Beach" };
            WorldConfig original = _store.CreateConfig(source, "Beach Empty", null);
            original.objects.Add(new SavedObject { instance_id = "chair_001", prefab_name = "beach chair" });
            _store.SaveConfig(original);

            WorldConfig fork = _store.ForkConfig(original, "Beach With Chairs");

            Assert.AreNotEqual(original.config_id, fork.config_id);
            Assert.AreEqual("Beach With Chairs", fork.display_name);
            Assert.AreEqual(1, fork.objects.Count);
            Assert.AreEqual("chair_001", fork.objects[0].instance_id);
            Assert.IsTrue(Directory.Exists(Path.Combine(_tempRoot, fork.config_id)));
        }
    }
}
```

- [ ] **Step 2: Run failing tests**

`Window > General > Test Runner > EditMode > Run All`  
Expected: All `WorldConfigStoreTests` fail with "type not found" or compile error.

- [ ] **Step 3: Create `WorldConfigStore.cs`**

```csharp
// Assets/App/Save/Runtime/WorldConfigStore.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using WorldLabs.API;  // World

namespace Holodeck.Save
{
    public class WorldConfigStore : MonoBehaviour
    {
        public event Action OnConfigsChanged;

        [Header("Dependencies")]
        [SerializeField] WorldLabs.Runtime.WorldLabsWorldManager worldManager;

        readonly List<WorldConfig> _configs = new List<WorldConfig>();

        public string WorldsRootPath   => Path.Combine(Application.persistentDataPath, "Worlds");
        public string CachedWorldsPath => Path.Combine(WorldsRootPath, "CachedWorlds");

        // ── Lifecycle ──────────────────────────────────────────────────────────

        void Start() => _ = ScanAndMigrateAsync();

        // ── Test factory — bypasses MonoBehaviour lifecycle ───────────────────

        /// <summary>
        /// Creates a store instance with a custom root path. Used by tests only.
        /// Call methods directly — Start() is not invoked.
        /// </summary>
        public static WorldConfigStore CreateForTesting(string rootPath)
        {
            var go = new GameObject("WorldConfigStore_Test");
            var store = go.AddComponent<WorldConfigStore>();
            store._testRootOverride = rootPath;
            return store;
        }

        string _testRootOverride;
        string RootPath => _testRootOverride ?? WorldsRootPath;
        string CachedPath => Path.Combine(RootPath, "CachedWorlds");

        // ── Public CRUD ────────────────────────────────────────────────────────

        /// <summary>Creates a new config folder and world.json. Returns the new WorldConfig.</summary>
        public WorldConfig CreateConfig(WorldSourceData source, string displayName, PromptEntry creationPrompt)
        {
            string now = DateTime.UtcNow.ToString("yyyy-MM-ddTHHmmssZ");
            string id  = MakeFolderName(displayName ?? source?.display_name ?? "World");
            string dir = Path.Combine(RootPath, id);
            Directory.CreateDirectory(dir);

            var config = new WorldConfig
            {
                config_id      = id,
                display_name   = displayName ?? source?.display_name ?? "World",
                created_at     = now,
                modified_at    = now,
                world_source   = source,
                generation_model = null
            };

            if (creationPrompt != null)
                config.prompts.Add(creationPrompt);

            WriteJson(config);
            _configs.Add(config);
            OnConfigsChanged?.Invoke();
            return config;
        }

        /// <summary>Overwrites world.json for an existing config, updating modified_at.</summary>
        public void SaveConfig(WorldConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            config.modified_at = DateTime.UtcNow.ToString("yyyy-MM-ddTHHmmssZ");
            WriteJson(config);
            OnConfigsChanged?.Invoke();
        }

        /// <summary>Reads world.json for the given config_id from disk.</summary>
        public WorldConfig LoadConfig(string configId)
        {
            string path = Path.Combine(RootPath, configId, "world.json");
            if (!File.Exists(path)) return null;
            return JsonConvert.DeserializeObject<WorldConfig>(File.ReadAllText(path));
        }

        /// <summary>Deletes the config folder from disk and removes it from the in-memory list. Does NOT touch CachedWorlds.</summary>
        public void DeleteConfig(string configId)
        {
            string dir = Path.Combine(RootPath, configId);
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);

            _configs.RemoveAll(c => c.config_id == configId);
            OnConfigsChanged?.Invoke();
        }

        public IReadOnlyList<WorldConfig> ListConfigs() => _configs.AsReadOnly();

        public bool HasConfigForWorldId(string worldId)
        {
            if (string.IsNullOrEmpty(worldId)) return false;
            return _configs.Exists(c => c.world_source?.world_id == worldId);
        }

        /// <summary>
        /// Creates a new config folder that is a deep copy of <paramref name="source"/>
        /// with a new config_id and the given display name.
        /// </summary>
        public WorldConfig ForkConfig(WorldConfig source, string newDisplayName)
        {
            string now = DateTime.UtcNow.ToString("yyyy-MM-ddTHHmmssZ");
            string id  = MakeFolderName(newDisplayName);
            string dir = Path.Combine(RootPath, id);
            Directory.CreateDirectory(dir);

            // Deep copy via JSON round-trip
            string json  = JsonConvert.SerializeObject(source);
            WorldConfig fork = JsonConvert.DeserializeObject<WorldConfig>(json);
            fork.config_id    = id;
            fork.display_name = newDisplayName;
            fork.created_at   = now;
            fork.modified_at  = now;

            WriteJson(fork);
            _configs.Add(fork);
            OnConfigsChanged?.Invoke();
            return fork;
        }

        // ── Startup scan ───────────────────────────────────────────────────────

        public async Task ScanAndMigrateAsync()
        {
            string root   = RootPath;
            string cached = CachedPath;

            // Phase 1: ensure directories
            await Task.Run(() =>
            {
                Directory.CreateDirectory(root);
                Directory.CreateDirectory(cached);
            });

            // Phase 2: migrate loose files
            await Task.Run(() => MigrateLooseFiles(root, cached));

            // Phase 3: load existing config folders
            List<WorldConfig> loaded = await Task.Run(() => LoadExistingConfigs(root));
            _configs.Clear();
            _configs.AddRange(loaded);

            OnConfigsChanged?.Invoke();
            // Phase 4 (WorldLabs reconcile) is handled by WorldConfigAutoSave.
        }

        /// <summary>
        /// Called by WorldConfigAutoSave after fetching the WorldLabs world list.
        /// Creates minimal config entries for any world not already tracked.
        /// </summary>
        public void ReconcileWithWorlds(IReadOnlyList<World> worlds)
        {
            if (worlds == null) return;
            bool changed = false;
            foreach (World w in worlds)
            {
                if (string.IsNullOrEmpty(w.world_id)) continue;
                if (HasConfigForWorldId(w.world_id)) continue;

                var source = new WorldSourceData
                {
                    type         = "worldlabs",
                    world_id     = w.world_id,
                    display_name = w.display_name
                };
                string now = DateTime.UtcNow.ToString("yyyy-MM-ddTHHmmssZ");
                string id  = MakeFolderName(w.display_name ?? "World");
                string dir = Path.Combine(RootPath, id);
                Directory.CreateDirectory(dir);

                var config = new WorldConfig
                {
                    config_id    = id,
                    display_name = w.display_name ?? "World",
                    created_at   = now,
                    modified_at  = now,
                    world_source = source
                };
                WriteJson(config);
                _configs.Add(config);
                changed = true;
            }
            if (changed) OnConfigsChanged?.Invoke();
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        void WriteJson(WorldConfig config)
        {
            string dir  = Path.Combine(RootPath, config.config_id);
            string path = Path.Combine(dir, "world.json");
            Directory.CreateDirectory(dir);
            File.WriteAllText(path, JsonConvert.SerializeObject(config, Formatting.Indented));
        }

        static string MakeFolderName(string displayName)
        {
            string sanitized = Regex.Replace(displayName ?? "World", @"[^a-zA-Z0-9]", "_");
            if (sanitized.Length > 40) sanitized = sanitized.Substring(0, 40);
            return $"{sanitized}_{DateTime.UtcNow:yyyy-MM-ddTHHmmss}Z";
        }

        static readonly string[] SplatExtensions = { ".spz", ".ply" };
        static readonly string[] PanoExtensions  = { ".jpg", ".jpeg", ".png", ".webp" };

        static void MigrateLooseFiles(string root, string cachedDir)
        {
            foreach (string file in Directory.GetFiles(root))
            {
                string ext = Path.GetExtension(file).ToLowerInvariant();
                bool isSplat = Array.IndexOf(SplatExtensions, ext) >= 0;
                bool isPano  = Array.IndexOf(PanoExtensions, ext) >= 0;
                if (!isSplat && !isPano)
                {
                    Debug.LogWarning($"[WorldConfigStore] Unrecognised file in Worlds/ root, skipping: {file}");
                    continue;
                }

                string fileName    = Path.GetFileName(file);
                string destination = Path.Combine(cachedDir, fileName);
                if (File.Exists(destination))
                    destination = Path.Combine(cachedDir, Path.GetFileNameWithoutExtension(fileName) + "_" + Guid.NewGuid().ToString("N")[..4] + ext);

                try { File.Move(file, destination); }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[WorldConfigStore] Could not migrate {file}: {ex.Message}");
                    continue;
                }

                string name = Path.GetFileNameWithoutExtension(fileName);
                string now  = DateTime.UtcNow.ToString("yyyy-MM-ddTHHmmssZ");
                string id   = MakeFolderName(name);
                string dir  = Path.Combine(root, id);
                Directory.CreateDirectory(dir);

                // Relative path from config folder to CachedWorlds
                string relativePath = $"../CachedWorlds/{Path.GetFileName(destination)}";
                var config = new WorldConfig
                {
                    config_id    = id,
                    display_name = name,
                    created_at   = now,
                    modified_at  = now,
                    world_source = new WorldSourceData
                    {
                        type         = isSplat ? "local_splat" : "local_pano",
                        cached_splat = isSplat ? relativePath : null,
                        cached_pano  = isPano  ? relativePath : null
                    }
                };

                string configPath = Path.Combine(dir, "world.json");
                File.WriteAllText(configPath, JsonConvert.SerializeObject(config, Formatting.Indented));
                Debug.Log($"[WorldConfigStore] Migrated {fileName} → {destination}, created config {id}");
            }
        }

        static List<WorldConfig> LoadExistingConfigs(string root)
        {
            var result = new List<WorldConfig>();
            foreach (string dir in Directory.GetDirectories(root))
            {
                if (Path.GetFileName(dir) == "CachedWorlds") continue;
                string jsonPath = Path.Combine(dir, "world.json");
                if (!File.Exists(jsonPath)) continue;
                try
                {
                    WorldConfig c = JsonConvert.DeserializeObject<WorldConfig>(File.ReadAllText(jsonPath));
                    if (c != null) result.Add(c);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[WorldConfigStore] Could not parse {jsonPath}: {ex.Message}");
                }
            }
            return result;
        }
    }
}
```

- [ ] **Step 4: Run tests**

`Window > General > Test Runner > EditMode > Run All`  
Expected: All `WorldConfigStoreTests` pass.

- [ ] **Step 5: Commit**

```bash
git add Assets/App/Save/Runtime/WorldConfigStore.cs \
        Assets/App/Save/Tests/WorldConfigStoreTests.cs
git commit -m "feat: add WorldConfigStore with CRUD, startup scan, and WorldLabs reconcile"
```

---

## Task 5: WorldConfigRestorer

**Files:**
- Create: `Assets/App/Save/Runtime/WorldConfigRestorer.cs`
- Modify: `Assets/App/Command/SpeechIntent/Runtime/ObjectPlacementController.cs` (make `FindPrefab` public)

- [ ] **Step 1: Make `FindPrefab` public in `ObjectPlacementController.cs`**

In `Assets/App/Command/SpeechIntent/Runtime/ObjectPlacementController.cs`, change line:

```csharp
private GameObject FindPrefab(string objectName)
```
to:
```csharp
public GameObject FindPrefab(string objectName)
```

- [ ] **Step 2: Verify compile**

Switch to Unity Editor. Wait for compilation. Expected: no errors.

- [ ] **Step 3: Create `WorldConfigRestorer.cs`**

```csharp
// Assets/App/Save/Runtime/WorldConfigRestorer.cs
using System;
using System.Collections;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Networking;
using WorldLabs.Runtime;
using SpeechIntent;

namespace Holodeck.Save
{
    public class WorldConfigRestorer : MonoBehaviour
    {
        [Header("Dependencies")]
        public WorldConfigStore         worldConfigStore;
        public WorldLabsWorldManager    worldManager;
        public ObjectPlacementController objectPlacement;
        public InteractionMemory        interactionMemory;
        public LightRigController       lightRig;
        public LocalRemoteSplatLoader   splatLoader;
        public LocalRemotePanoLoader    panoLoader;
        public Transform                placedObjectsParent;

        [Header("Events")]
        public UnityEvent onRestoreStarted;
        public StringEvent onRestoreError;
        public UnityEvent onRestoreComplete;

        const float RestoreTimeoutSeconds = 30f;

        public async Task RestoreAsync(WorldConfig config)
        {
            if (config == null)
            {
                onRestoreError?.Invoke("No config provided.");
                return;
            }

            onRestoreStarted?.Invoke();
            Debug.Log($"[WorldConfigRestorer] Restoring '{config.display_name}'");

            // ── 1. Load world ─────────────────────────────────────────────────
            bool worldLoaded = await LoadWorldAsync(config);
            if (!worldLoaded)
            {
                onRestoreError?.Invoke($"Could not load world for '{config.display_name}'.");
                return;
            }

            // ── 2. Restore objects ────────────────────────────────────────────
            string configFolderPath = Path.Combine(worldConfigStore.WorldsRootPath, config.config_id);
            var ctx = new RestorationContext { ConfigFolderPath = configFolderPath, Config = config };

            foreach (SavedObject savedObj in config.objects)
            {
                try   { RestoreObject(savedObj, ctx); }
                catch (Exception ex) { Debug.LogWarning($"[WorldConfigRestorer] Object '{savedObj.instance_id}' restore failed: {ex.Message}"); }
            }

            // ── 3. Load deferred audio clips ──────────────────────────────────
            StartCoroutine(LoadAudioClipsCoroutine());

            // ── 4. Apply lighting ─────────────────────────────────────────────
            if (config.lighting != null && lightRig != null)
            {
                if (!string.IsNullOrEmpty(config.lighting.preset))
                    lightRig.ApplyPreset(config.lighting.preset);
            }

            onRestoreComplete?.Invoke();
            Debug.Log($"[WorldConfigRestorer] Restore complete for '{config.display_name}'");
        }

        // ── Private helpers ────────────────────────────────────────────────────

        async Task<bool> LoadWorldAsync(WorldConfig config)
        {
            WorldSourceData src = config.world_source;
            if (src == null) return false;

            // Prefer cached splat if it exists
            if (!string.IsNullOrEmpty(src.cached_splat))
            {
                string absPath = ResolvePath(config.config_id, src.cached_splat);
                if (File.Exists(absPath))
                {
                    bool loaded = await WaitForWorldLoadedAsync(
                        () => splatLoader?.LoadAsync(absPath));
                    if (loaded) return true;
                }
            }

            // Fall back to WorldLabs stream
            if (src.type == "worldlabs" && !string.IsNullOrEmpty(src.world_id) && worldManager != null)
            {
                var world = new WorldLabs.API.World { world_id = src.world_id, display_name = src.display_name };
                return await WaitForWorldLoadedAsync(() => _ = worldManager.LoadWorldAsync(world));
            }

            // Local pano
            if (!string.IsNullOrEmpty(src.cached_pano))
            {
                string absPath = ResolvePath(config.config_id, src.cached_pano);
                if (File.Exists(absPath))
                {
                    bool loaded = await WaitForWorldLoadedAsync(() => panoLoader?.LoadAsync(absPath));
                    if (loaded) return true;
                }
            }

            // URL fallback
            if (!string.IsNullOrEmpty(src.url))
            {
                bool loaded = await WaitForWorldLoadedAsync(() => splatLoader?.LoadAsync(src.url));
                return loaded;
            }

            return false;
        }

        async Task<bool> WaitForWorldLoadedAsync(Action triggerLoad)
        {
            if (worldManager == null) return false;

            bool received = false;
            void OnLoaded(string id, GaussianSplatting.Runtime.GaussianSplatRenderer r) => received = true;
            worldManager.OnWorldLoaded += OnLoaded;

            triggerLoad?.Invoke();

            float elapsed = 0f;
            while (!received && elapsed < RestoreTimeoutSeconds)
            {
                await Task.Delay(100);
                elapsed += 0.1f;
            }

            worldManager.OnWorldLoaded -= OnLoaded;
            return received;
        }

        void RestoreObject(SavedObject savedObj, RestorationContext ctx)
        {
            GameObject go;
            if (!string.IsNullOrEmpty(savedObj.prefab_name) && objectPlacement != null)
            {
                GameObject prefab = objectPlacement.FindPrefab(savedObj.prefab_name);
                go = prefab != null
                    ? Instantiate(prefab, placedObjectsParent)
                    : new GameObject($"Restored_{savedObj.prefab_name}");
            }
            else
            {
                go = new GameObject(savedObj.display_name ?? "RestoredObject");
            }

            if (placedObjectsParent != null && go.transform.parent == null)
                go.transform.SetParent(placedObjectsParent, false);

            // Assign the tracked instance ID
            SpeechIntentTrackable trackable = go.GetComponent<SpeechIntentTrackable>()
                                            ?? go.AddComponent<SpeechIntentTrackable>();
            trackable.canonicalName   = savedObj.display_name ?? savedObj.prefab_name ?? go.name;
            trackable.configInstanceId = savedObj.instance_id;

            WorldConfigComponentRegistry.RestoreAll(go, savedObj.components, ctx);

            interactionMemory?.RegisterCreatedObject(go);
        }

        IEnumerator LoadAudioClipsCoroutine()
        {
            AudioClipPathHolder[] holders = FindObjectsByType<AudioClipPathHolder>(FindObjectsSortMode.None);
            foreach (AudioClipPathHolder holder in holders)
            {
                if (string.IsNullOrEmpty(holder.absolutePath)) continue;
                if (!File.Exists(holder.absolutePath)) continue;

                string url = "file://" + holder.absolutePath;
                using UnityWebRequest req = UnityWebRequestMultimedia.GetAudioClip(url, AudioType.UNKNOWN);
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning($"[WorldConfigRestorer] Failed to load audio {holder.absolutePath}: {req.error}");
                    continue;
                }
                AudioClip clip = DownloadHandlerAudioClip.GetContent(req);
                AudioSource src = holder.GetComponent<AudioSource>();
                if (src != null)
                {
                    src.clip = clip;
                    if (src.loop) src.Play();
                }
            }
        }

        string ResolvePath(string configId, string relativePath) =>
            Path.GetFullPath(Path.Combine(worldConfigStore.WorldsRootPath, configId, relativePath));
    }
}
```

- [ ] **Step 4: Verify compile**

Expected: no errors.

- [ ] **Step 5: Commit**

```bash
git add Assets/App/Save/Runtime/WorldConfigRestorer.cs \
        Assets/App/Command/SpeechIntent/Runtime/ObjectPlacementController.cs
git commit -m "feat: add WorldConfigRestorer with world load, object restore, and audio deferred load"
```

---

## Task 6: Groundwork — SpeechIntentTrackable + WorldActionDispatcher

**Files:**
- Modify: `Assets/App/Command/SpeechIntent/Runtime/SpeechIntentTrackable.cs`
- Modify: `Assets/App/Command/SpeechIntent/Runtime/WorldActionDispatcher.cs`

- [ ] **Step 1: Add `configInstanceId` to `SpeechIntentTrackable`**

In `Assets/App/Command/SpeechIntent/Runtime/SpeechIntentTrackable.cs`, add one field after `aliases`:

```csharp
public string canonicalName = "";
public List<string> aliases = new List<string>();
/// <summary>Stable ID assigned by WorldConfigAutoSave at placement time. Persisted in world.json.</summary>
public string configInstanceId = "";
```

- [ ] **Step 2: Add `OnObjectMutated` event and new fields to `WorldActionDispatcher`**

In `Assets/App/Command/SpeechIntent/Runtime/WorldActionDispatcher.cs`:

Add this after the existing `[Header("Scene Controllers")]` block and before `[Header("Inspector Hooks")]`:

```csharp
[Header("Save System")]
public Holodeck.Save.WorldConfigStore    worldConfigStore;
public Holodeck.Save.WorldConfigRestorer worldConfigRestorer;
public WorldConfigAutoSave               worldConfigAutoSave;
```

Where `WorldConfigAutoSave` is in namespace `Holodeck.Save` — add the full reference. Add this using at the top of the file:

```csharp
using Holodeck.Save;
```

Then add the event after the existing `UnityEvent` fields:

```csharp
/// <summary>Fired after PlaceObject, MoveTarget, ScaleTarget, RotateTarget, ResetTransform.
/// First arg = the command that caused the mutation; second = the affected GameObject.</summary>
public event Action<VoiceIntentCommand, GameObject> OnObjectMutated;
```

- [ ] **Step 3: Fire `OnObjectMutated` in the five mutating handlers**

In `HandlePlaceObject`, after `interactionMemory.RegisterCreatedObject(placed)`:
```csharp
if (placed != null) OnObjectMutated?.Invoke(command, placed);
```

In `HandleMoveTarget`, after `Debug.Log($"Moved target '{target.name}'.")`:
```csharp
OnObjectMutated?.Invoke(command, target);
```

In `HandleScaleTarget`, after `Debug.Log($"Scaled target '{target.name}'.")`:
```csharp
OnObjectMutated?.Invoke(command, target);
```

In `HandleRotateTarget`, after `Debug.Log($"Rotated target '{target.name}'.")`:
```csharp
OnObjectMutated?.Invoke(command, target);
```

In `HandleResetTransform`, after `Debug.Log($"Reset transform of '{target.name}'.")`:
```csharp
OnObjectMutated?.Invoke(command, target);
```

- [ ] **Step 4: Verify compile**

Expected: no errors.

- [ ] **Step 5: Commit**

```bash
git add Assets/App/Command/SpeechIntent/Runtime/SpeechIntentTrackable.cs \
        Assets/App/Command/SpeechIntent/Runtime/WorldActionDispatcher.cs
git commit -m "feat: add configInstanceId to Trackable; add OnObjectMutated event to WorldActionDispatcher"
```

---

## Task 7: WorldConfigAutoSave

**Files:**
- Create: `Assets/App/Save/Runtime/WorldConfigAutoSave.cs`

- [ ] **Step 1: Create `WorldConfigAutoSave.cs`**

```csharp
// Assets/App/Save/Runtime/WorldConfigAutoSave.cs
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GaussianSplatting.Runtime;
using UnityEngine;
using WorldLabs.Runtime;
using WorldLabs.API;
using SpeechIntent;

namespace Holodeck.Save
{
    public class WorldConfigAutoSave : MonoBehaviour
    {
        [Header("Dependencies")]
        public WorldConfigStore    worldConfigStore;
        public WorldLabsWorldManager worldManager;
        public WorldActionDispatcher dispatcher;

        /// <summary>The config that corresponds to the currently loaded world.</summary>
        public WorldConfig ActiveConfig { get; set; }

        void OnEnable()
        {
            if (worldManager != null)
                worldManager.OnWorldLoaded += OnWorldLoaded;
            if (dispatcher != null)
                dispatcher.OnObjectMutated += OnObjectMutated;
        }

        void OnDisable()
        {
            if (worldManager != null)
                worldManager.OnWorldLoaded -= OnWorldLoaded;
            if (dispatcher != null)
                dispatcher.OnObjectMutated -= OnObjectMutated;
        }

        async void Start()
        {
            if (worldConfigStore == null) return;

            await worldConfigStore.ScanAndMigrateAsync();

            if (worldManager == null) return;
            try
            {
                List<World> worlds = await worldManager.ListWorldsAsync(pageSize: 100);
                worldConfigStore.ReconcileWithWorlds(worlds);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[WorldConfigAutoSave] WorldLabs reconcile failed: {ex.Message}");
            }
        }

        // ── Event handlers ────────────────────────────────────────────────────

        void OnWorldLoaded(string worldId, GaussianSplatRenderer renderer)
        {
            if (worldConfigStore == null) return;

            // If we already have an active config for this world, keep it.
            if (ActiveConfig != null &&
                ActiveConfig.world_source?.world_id == worldId) return;

            // Find existing config or create a minimal one
            WorldConfig existing = null;
            foreach (WorldConfig c in worldConfigStore.ListConfigs())
            {
                if (c.world_source?.world_id == worldId)
                {
                    existing = c;
                    break;
                }
            }

            if (existing != null)
            {
                ActiveConfig = existing;
                return;
            }

            // Create new config
            World world = worldManager.LastLoadedWorld;
            var source = new WorldSourceData
            {
                type         = "worldlabs",
                world_id     = worldId,
                display_name = world?.display_name
            };
            var prompt = new PromptEntry
            {
                timestamp  = DateTime.UtcNow.ToString("yyyy-MM-ddTHHmmssZ"),
                type       = "world_creation",
                intent     = "GenerateWorld",
                transcript = world?.display_name ?? worldId
            };

            ActiveConfig = worldConfigStore.CreateConfig(source, world?.display_name ?? "World", prompt);
        }

        void OnObjectMutated(VoiceIntentCommand command, GameObject go)
        {
            if (worldConfigStore == null || ActiveConfig == null) return;

            // Ensure the object has a stable instance ID
            SpeechIntentTrackable trackable = go.GetComponent<SpeechIntentTrackable>();
            if (trackable == null) trackable = go.AddComponent<SpeechIntentTrackable>();
            if (string.IsNullOrEmpty(trackable.configInstanceId))
                trackable.configInstanceId = $"{go.name}_{Guid.NewGuid():N}".Substring(0, Mathf.Min(24, go.name.Length + 9));

            // Snapshot all registered components
            List<SavedComponent> components = WorldConfigComponentRegistry.SaveAll(go);

            // Update or insert SavedObject
            string id = trackable.configInstanceId;
            int idx = ActiveConfig.objects.FindIndex(o => o.instance_id == id);
            var savedObj = new SavedObject
            {
                instance_id  = id,
                prefab_name  = trackable.canonicalName,
                display_name = go.name,
                components   = components
            };

            if (idx >= 0) ActiveConfig.objects[idx] = savedObj;
            else          ActiveConfig.objects.Add(savedObj);

            // Append prompt
            ActiveConfig.prompts.Add(new PromptEntry
            {
                timestamp  = DateTime.UtcNow.ToString("yyyy-MM-ddTHHmmssZ"),
                type       = "voice_command",
                intent     = command.intent.ToString(),
                transcript = command.transcript
            });

            worldConfigStore.SaveConfig(ActiveConfig);
        }
    }
}
```

- [ ] **Step 2: Verify compile**

Expected: no errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/App/Save/Runtime/WorldConfigAutoSave.cs
git commit -m "feat: add WorldConfigAutoSave — auto-creates config on world load, saves on object mutation"
```

---

## Task 8: Voice Command Integration

**Files:**
- Modify: `Assets/App/Command/SpeechIntent/Runtime/VoiceIntentSchemas.cs`
- Modify: `Assets/App/Command/SpeechIntent/Runtime/WorldActionDispatcher.cs`
- Modify: `Assets/App/Command/SpeechIntent/Runtime/OpenAiSpeechIntentService.cs`
- Modify: `Assets/App/Command/SpeechIntent/Runtime/OpenAiSpeechIntentConfig.cs`

- [ ] **Step 1: Add intent types and `config_name` field to `VoiceIntentSchemas.cs`**

In the `VoiceIntentType` enum, after `ShowMeshWorld = 17`:
```csharp
SaveWorldConfig    = 18,  // "save" or "save as [name]"
LoadWorldConfig    = 19,  // "load [name]" or "show my worlds"
```

In `VoiceIntentCommand`, after the `[Header("World Generation Model")]` block:
```csharp
[Header("World Config")]
[Tooltip("Config name for SaveWorldConfig (save-as) and LoadWorldConfig (load by name). Empty = save current / open panel.")]
public string config_name = "";
```

- [ ] **Step 2: Add intent handlers to `WorldActionDispatcher.cs`**

In the `switch (command.intent)` block, add two cases before `default`:

```csharp
case VoiceIntentType.SaveWorldConfig:
    HandleSaveWorldConfig(command);
    break;

case VoiceIntentType.LoadWorldConfig:
    HandleLoadWorldConfig(command);
    break;
```

Add the two handler methods to `WorldActionDispatcher`:

```csharp
private void HandleSaveWorldConfig(VoiceIntentCommand command)
{
    if (worldConfigAutoSave == null || worldConfigAutoSave.ActiveConfig == null)
    {
        Debug.LogWarning("[WorldActionDispatcher] SaveWorldConfig: no active config.");
        return;
    }

    if (!string.IsNullOrWhiteSpace(command.config_name))
    {
        // Save As — fork the active config with the new name
        WorldConfig fork = worldConfigStore?.ForkConfig(worldConfigAutoSave.ActiveConfig, command.config_name);
        if (fork != null)
        {
            worldConfigAutoSave.ActiveConfig = fork;
            Debug.Log($"[WorldActionDispatcher] Saved As '{command.config_name}'.");
        }
    }
    else
    {
        // Save — overwrite current
        worldConfigStore?.SaveConfig(worldConfigAutoSave.ActiveConfig);
        Debug.Log("[WorldActionDispatcher] Saved current config.");
    }
}

private void HandleLoadWorldConfig(VoiceIntentCommand command)
{
    if (!string.IsNullOrWhiteSpace(command.config_name))
    {
        // Load by name — fuzzy match on display_name
        if (worldConfigStore == null || worldConfigRestorer == null) return;
        string lower = command.config_name.ToLowerInvariant();
        WorldConfig match = null;
        foreach (WorldConfig c in worldConfigStore.ListConfigs())
        {
            if (c.display_name != null &&
                c.display_name.ToLowerInvariant().Contains(lower))
            {
                match = c;
                break;
            }
        }
        if (match != null)
            _ = worldConfigRestorer.RestoreAsync(match);
        else
            Debug.LogWarning($"[WorldActionDispatcher] LoadWorldConfig: no config matching '{command.config_name}'.");
    }
    else
    {
        // Open My Worlds panel
        uiPanels?.Show("my worlds");
    }
}
```

- [ ] **Step 3: Update JSON schema in `OpenAiSpeechIntentService.cs`**

Find `BuildCommandJsonSchema()`. In the `intent` enum array, after `"ShowMeshWorld"`:
```json
"SaveWorldConfig",
"LoadWorldConfig"
```

In the `required` array, add `"config_name"`.

In the `properties` object, add after `"generation_model"`:
```json
"config_name": {
    "type": "string",
    "description": "Name for save-as or load-by-name. Empty string for plain save or to open the panel."
}
```

- [ ] **Step 4: Add routing hints to `OpenAiSpeechIntentConfig.cs`**

Append to the existing `systemPromptSuffix` or routing hints string:

```
SaveWorldConfig: Use when user says "save", "save my world", or "save as [name]". Populate config_name with the name after "save as", empty string for plain "save".
LoadWorldConfig: Use when user says "load", "open my worlds", "show my worlds", or "load [name]". Populate config_name with the name after "load", empty string to open the panel.
```

- [ ] **Step 5: Verify compile**

Expected: no errors.

- [ ] **Step 6: Commit**

```bash
git add Assets/App/Command/SpeechIntent/Runtime/VoiceIntentSchemas.cs \
        Assets/App/Command/SpeechIntent/Runtime/WorldActionDispatcher.cs \
        Assets/App/Command/SpeechIntent/Runtime/OpenAiSpeechIntentService.cs \
        Assets/App/Command/SpeechIntent/Runtime/OpenAiSpeechIntentConfig.cs
git commit -m "feat: add SaveWorldConfig and LoadWorldConfig voice intents"
```

---

## Task 9: WorldBrowserController Bookmark Indicator

**Files:**
- Modify: `Packages/com.worldlabs.gaussian-splatting/Runtime/WorldLabs/WorldBrowserController.cs`

The goal: when populating world cards in the `WorldBrowserController`, check if `WorldConfigStore` has a config for each world and show a small "saved" indicator on the card.

- [ ] **Step 1: Add `worldConfigStore` field**

In `WorldBrowserController`, in the `[Header("Scene References")]` block (around line 15), add:

```csharp
[Header("Save System")]
public Holodeck.Save.WorldConfigStore worldConfigStore;
```

- [ ] **Step 2: Find the card population method**

Search for where cards are instantiated — the method that creates `WorldCardUI` entries for each `World` in the list. It will be somewhere in the Refresh/pagination flow. In that loop, after each card is created and configured, add:

```csharp
// Bookmark indicator — show if this world has a saved config
if (worldConfigStore != null && card != null)
{
    bool hasSaved = worldConfigStore.HasConfigForWorldId(w.world_id);
    card.SetBookmarkVisible(hasSaved);
}
```

- [ ] **Step 3: Add `SetBookmarkVisible` to `WorldCardUI`**

Find the `WorldCardUI` class (it will be a component used by `WorldBrowserController` for cards). Add a method and a serialized field for the bookmark indicator:

```csharp
[Header("Save System")]
public UnityEngine.UI.Image bookmarkIndicator;  // assign a small icon Image in the prefab

public void SetBookmarkVisible(bool visible)
{
    if (bookmarkIndicator != null)
        bookmarkIndicator.gameObject.SetActive(visible);
}
```

> Note: The bookmark `Image` GameObject must be added to the WorldCardUI prefab manually in the Unity Editor and assigned to `bookmarkIndicator`. The editor setup script does not create prefab UI elements. After running the setup script, open the WorldCardUI prefab and add a small Image child, then wire it.

- [ ] **Step 4: Verify compile**

Expected: no errors.

- [ ] **Step 5: Commit**

```bash
git add Packages/com.worldlabs.gaussian-splatting/Runtime/WorldLabs/WorldBrowserController.cs
git commit -m "feat: add bookmark indicator to WorldBrowserController cards"
```

---

## Task 10: My Worlds Panel

**Files:**
- Create: `Assets/App/Command/SpeechIntent/Runtime/UI/WorldConfigCardUI.cs`
- Create: `Assets/App/Command/SpeechIntent/Runtime/UI/MyWorldsPanel.cs`

- [ ] **Step 1: Create `WorldConfigCardUI.cs`**

```csharp
// Assets/App/Command/SpeechIntent/Runtime/UI/WorldConfigCardUI.cs
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace SpeechIntent
{
    /// <summary>Card component for a single WorldConfig entry in MyWorldsPanel.</summary>
    public class WorldConfigCardUI : MonoBehaviour
    {
        public TMP_Text nameLabel;
        public TMP_Text sourceLabel;   // e.g. "WorldLabs", "Local Splat"
        public TMP_Text dateLabel;
        public RawImage thumbnail;     // pano preview if available
        public Button   loadButton;
        public Button   saveAsButton;
        public Button   deleteButton;

        public void SetData(
            string displayName,
            string sourceType,
            string modifiedAt,
            Texture2D thumb,
            UnityAction onLoad,
            UnityAction onSaveAs,
            UnityAction onDelete)
        {
            if (nameLabel   != null) nameLabel.text   = displayName ?? "";
            if (sourceLabel != null) sourceLabel.text = FormatSourceType(sourceType);
            if (dateLabel   != null) dateLabel.text   = modifiedAt  ?? "";
            if (thumbnail   != null)
            {
                thumbnail.texture = thumb;
                thumbnail.gameObject.SetActive(thumb != null);
            }

            WireButton(loadButton,   onLoad);
            WireButton(saveAsButton, onSaveAs);
            WireButton(deleteButton, onDelete);
        }

        static void WireButton(Button btn, UnityAction action)
        {
            if (btn == null) return;
            btn.onClick.RemoveAllListeners();
            if (action != null) btn.onClick.AddListener(action);
        }

        static string FormatSourceType(string type) => type switch
        {
            "worldlabs"  => "WorldLabs",
            "local_splat" => "Local Splat",
            "local_pano"  => "Local Pano",
            "url"         => "URL",
            _             => type ?? ""
        };
    }
}
```

- [ ] **Step 2: Create `MyWorldsPanel.cs`**

```csharp
// Assets/App/Command/SpeechIntent/Runtime/UI/MyWorldsPanel.cs
using System.IO;
using TMPro;
using UnityEngine;
using Holodeck.Save;

namespace SpeechIntent
{
    /// <summary>
    /// Scrollable panel showing all WorldConfig folders.
    /// Follows the LocalFileBrowserPanel pattern.
    /// Register with UiPanelController under key "my worlds".
    /// </summary>
    public class MyWorldsPanel : MonoBehaviour
    {
        [Header("Dependencies")]
        public WorldConfigStore    worldConfigStore;
        public WorldConfigRestorer worldConfigRestorer;

        [Header("UI References")]
        public RectTransform        cardListContent;
        public WorldConfigCardUI    cardPrefab;
        public TMP_Text             statusLabel;
        public TMP_InputField       saveAsInputField;
        public UnityEngine.UI.Button saveAsConfirmButton;

        WorldConfig _pendingSaveAs;

        void OnEnable()
        {
            if (worldConfigStore != null)
                worldConfigStore.OnConfigsChanged += Refresh;
            Refresh();
        }

        void OnDisable()
        {
            if (worldConfigStore != null)
                worldConfigStore.OnConfigsChanged -= Refresh;
        }

        public void Refresh()
        {
            if (cardListContent == null) return;

            for (int i = cardListContent.childCount - 1; i >= 0; i--)
                Destroy(cardListContent.GetChild(i).gameObject);

            if (worldConfigStore == null)
            {
                ShowStatus("WorldConfigStore not assigned.");
                return;
            }

            var configs = worldConfigStore.ListConfigs();
            if (configs.Count == 0)
            {
                ShowStatus("No saved worlds found.");
                return;
            }

            ShowStatus(null);
            foreach (WorldConfig config in configs)
            {
                WorldConfig captured = config;
                Texture2D thumb = TryLoadThumbnail(config);

                WorldConfigCardUI card = Instantiate(cardPrefab, cardListContent);
                card.SetData(
                    displayName: config.display_name,
                    sourceType:  config.world_source?.type,
                    modifiedAt:  config.modified_at,
                    thumb:       thumb,
                    onLoad:   () => _ = worldConfigRestorer?.RestoreAsync(captured),
                    onSaveAs: () => BeginSaveAs(captured),
                    onDelete: () => ConfirmDelete(captured)
                );
            }
        }

        void BeginSaveAs(WorldConfig config)
        {
            _pendingSaveAs = config;
            if (saveAsInputField != null)
            {
                saveAsInputField.text = config.display_name ?? "";
                saveAsInputField.gameObject.SetActive(true);
            }
            if (saveAsConfirmButton != null)
            {
                saveAsConfirmButton.gameObject.SetActive(true);
                saveAsConfirmButton.onClick.RemoveAllListeners();
                saveAsConfirmButton.onClick.AddListener(CommitSaveAs);
            }
        }

        void CommitSaveAs()
        {
            if (_pendingSaveAs == null || worldConfigStore == null) return;
            string newName = saveAsInputField != null ? saveAsInputField.text.Trim() : "";
            if (string.IsNullOrEmpty(newName)) return;

            worldConfigStore.ForkConfig(_pendingSaveAs, newName);
            _pendingSaveAs = null;
            if (saveAsInputField    != null) saveAsInputField.gameObject.SetActive(false);
            if (saveAsConfirmButton != null) saveAsConfirmButton.gameObject.SetActive(false);
        }

        void ConfirmDelete(WorldConfig config)
        {
            // Simple immediate delete — add a confirmation dialog prefab if needed later
            worldConfigStore?.DeleteConfig(config.config_id);
        }

        Texture2D TryLoadThumbnail(WorldConfig config)
        {
            string panoPath = config.world_source?.cached_pano;
            if (string.IsNullOrEmpty(panoPath)) return null;
            string abs = Path.GetFullPath(
                Path.Combine(worldConfigStore.WorldsRootPath, config.config_id, panoPath));
            if (!File.Exists(abs)) return null;
            try
            {
                byte[] bytes = File.ReadAllBytes(abs);
                var tex = new Texture2D(2, 2);
                tex.LoadImage(bytes);
                return tex;
            }
            catch { return null; }
        }

        void ShowStatus(string msg)
        {
            if (statusLabel == null) return;
            statusLabel.gameObject.SetActive(msg != null);
            if (msg != null) statusLabel.text = msg;
        }
    }
}
```

- [ ] **Step 3: Verify compile**

Expected: no errors.

- [ ] **Step 4: Commit**

```bash
git add Assets/App/Command/SpeechIntent/Runtime/UI/WorldConfigCardUI.cs \
        Assets/App/Command/SpeechIntent/Runtime/UI/MyWorldsPanel.cs
git commit -m "feat: add WorldConfigCardUI and MyWorldsPanel"
```

---

## Task 11: WorldConfigSceneSetup Editor Script

**Files:**
- Create: `Assets/App/Editor/WorldConfigSceneSetup.cs`

- [ ] **Step 1: Create `WorldConfigSceneSetup.cs`**

```csharp
// Assets/App/Editor/WorldConfigSceneSetup.cs
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using WorldLabs.Runtime;
using SpeechIntent;
using Holodeck.Save;

namespace Holodeck.Editor
{
    /// <summary>
    /// Adds and wires WorldConfigStore, WorldConfigRestorer, and WorldConfigAutoSave
    /// into the current scene's Systems/SpeechIntent hierarchy.
    ///
    /// Also wires WorldConfigStore into WorldBrowserController if present.
    ///
    /// Re-running is safe — existing components are reused.
    ///
    /// Menu: Holodeck > Setup World Config
    /// </summary>
    public static class WorldConfigSceneSetup
    {
        [MenuItem("Holodeck/Setup World Config")]
        public static void SetupWorldConfig()
        {
            // ── Locate scene objects ──────────────────────────────────────────
            GameObject systems    = FindRoot("Systems");
            GameObject speechRoot = systems != null ? FindChild(systems, "SpeechIntent") : null;

            if (systems == null)
            {
                Debug.LogError("[WorldConfigSceneSetup] 'Systems' root not found. Run Holodeck > Setup Holodeck Scene first.");
                return;
            }
            if (speechRoot == null)
            {
                Debug.LogError("[WorldConfigSceneSetup] 'Systems/SpeechIntent' not found. Run Holodeck > Setup SpeechIntent first.");
                return;
            }

            // ── Add components ────────────────────────────────────────────────
            WorldConfigStore    store    = GetOrAdd<WorldConfigStore>(speechRoot);
            WorldConfigRestorer restorer = GetOrAdd<WorldConfigRestorer>(speechRoot);
            WorldConfigAutoSave autoSave = GetOrAdd<WorldConfigAutoSave>(speechRoot);

            // ── Locate cross-system deps ──────────────────────────────────────
            WorldLabsWorldManager    worldManager  = systems.GetComponentInChildren<WorldLabsWorldManager>(true);
            WorldActionDispatcher    dispatcher    = speechRoot.GetComponent<WorldActionDispatcher>();
            ObjectPlacementController placement    = speechRoot.GetComponent<ObjectPlacementController>();
            InteractionMemory        memory        = speechRoot.GetComponent<InteractionMemory>();
            LightRigController       lightRig      = speechRoot.GetComponent<LightRigController>();
            LocalRemoteSplatLoader   splatLoader   = speechRoot.GetComponent<LocalRemoteSplatLoader>();
            LocalRemotePanoLoader    panoLoader    = speechRoot.GetComponent<LocalRemotePanoLoader>();

            GameObject worldLabsGui = GameObject.Find("UI/WorldLabs_GUI");
            WorldBrowserController browser = worldLabsGui != null
                ? worldLabsGui.GetComponent<WorldBrowserController>()
                : null;

            if (worldManager == null)
                Debug.LogWarning("[WorldConfigSceneSetup] WorldLabsWorldManager not found. Assign worldManager fields manually.");

            // ── Wire WorldConfigStore ─────────────────────────────────────────
            Undo.RecordObject(store, "Wire WorldConfigStore");
            // WorldConfigStore has no MonoBehaviour dependencies except worldManager (not needed at runtime).
            EditorUtility.SetDirty(store);

            // ── Wire WorldConfigRestorer ──────────────────────────────────────
            Undo.RecordObject(restorer, "Wire WorldConfigRestorer");
            restorer.worldConfigStore  = store;
            restorer.worldManager      = worldManager;
            restorer.objectPlacement   = placement;
            restorer.interactionMemory = memory;
            restorer.lightRig          = lightRig;
            restorer.splatLoader       = splatLoader;
            restorer.panoLoader        = panoLoader;
            EditorUtility.SetDirty(restorer);

            // ── Wire WorldConfigAutoSave ──────────────────────────────────────
            Undo.RecordObject(autoSave, "Wire WorldConfigAutoSave");
            autoSave.worldConfigStore = store;
            autoSave.worldManager     = worldManager;
            autoSave.dispatcher       = dispatcher;
            EditorUtility.SetDirty(autoSave);

            // ── Wire WorldActionDispatcher ────────────────────────────────────
            if (dispatcher != null)
            {
                Undo.RecordObject(dispatcher, "Wire WorldActionDispatcher save fields");
                dispatcher.worldConfigStore    = store;
                dispatcher.worldConfigRestorer = restorer;
                dispatcher.worldConfigAutoSave = autoSave;
                EditorUtility.SetDirty(dispatcher);
            }

            // ── Wire WorldBrowserController ───────────────────────────────────
            if (browser != null)
            {
                Undo.RecordObject(browser, "Wire WorldBrowserController.worldConfigStore");
                browser.worldConfigStore = store;
                EditorUtility.SetDirty(browser);
            }
            else
            {
                Debug.LogWarning("[WorldConfigSceneSetup] WorldBrowserController not found at 'UI/WorldLabs_GUI'. " +
                                 "Assign worldConfigStore on WorldBrowserController manually.");
            }

            // ── Mark scene dirty ──────────────────────────────────────────────
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log("[WorldConfigSceneSetup] Done. WorldConfigStore, Restorer, and AutoSave wired. " +
                      "Save the scene to persist. Then:\n" +
                      "1. Create a WorldConfigCardUI prefab and assign to MyWorldsPanel.cardPrefab.\n" +
                      "2. Add a 'my worlds' UI panel to the scene and register it with UiPanelController.\n" +
                      "3. Add a bookmark Image to the WorldCardUI prefab and assign to WorldCardUI.bookmarkIndicator.");
        }

        static GameObject FindRoot(string name)
        {
            foreach (GameObject r in SceneManager.GetActiveScene().GetRootGameObjects())
                if (r.name == name) return r;
            return null;
        }

        static GameObject FindChild(GameObject parent, string name)
        {
            Transform t = parent.transform.Find(name);
            return t != null ? t.gameObject : null;
        }

        static T GetOrAdd<T>(GameObject go) where T : Component
        {
            T existing = go.GetComponent<T>();
            return existing != null ? existing : Undo.AddComponent<T>(go);
        }
    }
}
```

- [ ] **Step 2: Run the setup menu**

`Holodeck > Setup World Config`

Expected: Console shows "Done. WorldConfigStore, Restorer, and AutoSave wired."  
Expected: No errors (warnings about missing prefab wiring are OK — those are manual steps).

- [ ] **Step 3: Manually wire remaining UI prefab references**

After running the setup:
1. Create a `WorldConfigCardUI` prefab in `Assets/App/Prefabs/` following the `FileEntryItemUI` prefab as a template. Add `TMP_Text` fields for name/source/date, `Button` components for Load/Save As/Delete. Assign all fields on the `WorldConfigCardUI` component.
2. In the scene, create a UI panel (Canvas child), add a `ScrollRect` → `Viewport` → `Content` structure, add `MyWorldsPanel` component, assign `cardPrefab`, `cardListContent`, `statusLabel`. Register this panel with `UiPanelController` under key `"my worlds"`.
3. Add a small `Image` child to the WorldCardUI prefab and assign it to `WorldCardUI.bookmarkIndicator`.

- [ ] **Step 4: Enter Play Mode and verify startup scan runs**

Console should show: `[WorldConfigStore] ...` messages on startup (migration or loading).

- [ ] **Step 5: Commit**

```bash
git add Assets/App/Editor/WorldConfigSceneSetup.cs
git commit -m "feat: add WorldConfigSceneSetup editor script to wire save system into scene"
```

---

## Self-Review

### Spec Coverage

| Spec section | Covered by |
|---|---|
| Folder structure + CachedWorlds | Task 4 (`WorldConfigStore.WorldsRootPath`, `CachedWorldsPath`) |
| JSON schema (all fields) | Task 1 (`WorldConfig.cs`) + serialization tests |
| C# data classes | Task 1 |
| Component registry + RestorationContext | Task 2 |
| TransformSerializer + AudioSourceSerializer | Task 3 |
| WorldConfigStore CRUD | Task 4 |
| Startup scan phases 1-3 | Task 4 (`MigrateLooseFiles`, `LoadExistingConfigs`) |
| Startup scan phase 4 (WorldLabs reconcile) | Task 7 (`WorldConfigAutoSave.Start`) |
| WorldConfigRestorer full restoration | Task 5 |
| Auto-save on world load | Task 7 (`OnWorldLoaded`) |
| Auto-save on object mutation | Task 7 (`OnObjectMutated`) |
| Instance ID assignment | Task 7 (`OnObjectMutated`) |
| Voice commands (SaveWorldConfig, LoadWorldConfig) | Task 8 |
| config_name field | Task 8 |
| WorldBrowserController bookmark | Task 9 |
| MyWorldsPanel (cards, Load/SaveAs/Delete) | Task 10 |
| WorldConfigSceneSetup editor script | Task 11 |
| SpeechIntentTrackable.configInstanceId | Task 6 |
| ObjectPlacementController.FindPrefab public | Task 5 |
| WorldActionDispatcher.OnObjectMutated | Task 6 |

All spec requirements covered. ✓

### Type Consistency Check

- `WorldConfigStore.ForkConfig` — called in `WorldActionDispatcher.HandleSaveWorldConfig` ✓
- `WorldConfigStore.ReconcileWithWorlds(IReadOnlyList<World>)` — called in `WorldConfigAutoSave.Start` with `List<World>` (which is `IReadOnlyList<World>`) ✓
- `WorldConfigRestorer.RestoreAsync(WorldConfig)` — called from `MyWorldsPanel` and `HandleLoadWorldConfig` ✓
- `WorldConfigComponentRegistry.SaveAll(GameObject)` — called in `WorldConfigAutoSave.OnObjectMutated` ✓
- `WorldConfigComponentRegistry.RestoreAll(GameObject, List<SavedComponent>, RestorationContext)` — called in `WorldConfigRestorer.RestoreObject` ✓
- `AudioClipPathHolder.clipPath` / `.absolutePath` — written by `AudioSourceSerializer.Restore`, read by `WorldConfigRestorer.LoadAudioClipsCoroutine` ✓
