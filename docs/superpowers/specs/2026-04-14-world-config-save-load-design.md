# World Configuration Save/Load Design

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Persist world configurations (world source, placed objects, lighting, prompts) as self-contained folders on the device so they can be saved, restored, and imported/exported.

**Architecture:** A `WorldConfigStore` manages a `Worlds/` directory containing one folder per configuration plus a shared `CachedWorlds/` subfolder for raw splat/pano/audio files. An extensible component registry handles per-object property serialization. A `WorldConfigRestorer` orchestrates full scene restoration.

**Tech Stack:** C# / Unity, Newtonsoft JSON (already in project via `com.unity.nuget.newtonsoft-json`), `Application.persistentDataPath`, existing `WorldLabsWorldManager`, `LightRigController`, `ObjectPlacementController`, `SpeechIntentTrackable`, `UiPanelController`.

---

## 1. Folder Structure

```
Application.persistentDataPath/Worlds/
  CachedWorlds/                              ← all raw splat/pano/audio files
    abc123.spz
    abc123_pano.jpg
    old_local_file.spz                       ← migrated from Worlds/ root on first startup
  BeachEmpty_2026-04-14T103000Z/             ← one folder per configuration
    world.json
  BeachWithChairs_2026-04-14T114500Z/
    world.json
```

- Config folder names: `{SanitizedDisplayName}_{UtcTimestamp}Z/` — sanitized means alphanumeric + underscores only, spaces replaced by underscores.
- Multiple configs may reference the same files in `CachedWorlds/` — no duplication.
- Deleting a config folder never touches `CachedWorlds/`.

---

## 2. JSON Schema (`world.json`)

```json
{
  "schema_version": 1,
  "config_id": "BeachEmpty_2026-04-14T103000Z",
  "display_name": "Beach",
  "created_at": "2026-04-14T10:30:00Z",
  "modified_at": "2026-04-14T11:45:00Z",

  "world_source": {
    "type": "worldlabs",
    "world_id": "abc123",
    "display_name": "Sunny Beach",
    "cached_splat": "../CachedWorlds/abc123.spz",
    "cached_pano":  "../CachedWorlds/abc123_pano.jpg"
  },

  "generation_model": "Standard",

  "prompts": [
    {
      "timestamp": "2026-04-14T10:30:00Z",
      "type": "world_creation",
      "intent": "GenerateWorld",
      "transcript": "create a sunny beach with palm trees"
    },
    {
      "timestamp": "2026-04-14T11:00:00Z",
      "type": "voice_command",
      "intent": "PlaceObject",
      "transcript": "place a beach chair to my left"
    }
  ],

  "objects": [
    {
      "instance_id": "beach_chair_001",
      "prefab_name": "beach chair",
      "display_name": "Beach Chair",
      "components": [
        {
          "type": "Transform",
          "data": {
            "position": { "x": 1.2, "y": 0.0, "z": 2.5 },
            "rotation": { "x": 0.0, "y": 0.383, "z": 0.0, "w": 0.924 },
            "scale":    { "x": 1.0, "y": 1.0,   "z": 1.0 }
          }
        }
      ]
    },
    {
      "instance_id": "audio_001",
      "prefab_name": null,
      "display_name": "Ocean Waves",
      "components": [
        {
          "type": "Transform",
          "data": {
            "position": { "x": 0.0, "y": 1.0, "z": 0.0 },
            "rotation": { "x": 0.0, "y": 0.0,   "z": 0.0, "w": 1.0 },
            "scale":    { "x": 1.0, "y": 1.0,   "z": 1.0 }
          }
        },
        {
          "type": "AudioSource",
          "data": {
            "clip_path": "../CachedWorlds/ocean_waves.mp3",
            "volume": 0.7,
            "loop": true,
            "spatial_blend": 1.0
          }
        }
      ]
    }
  ],

  "lighting": {
    "preset": "Golden Hour",
    "sun_azimuth": 220.0,
    "sun_elevation": 35.0
  }
}
```

**Field notes:**
- `world_source.type`: `"worldlabs"` | `"local_splat"` | `"local_pano"` | `"url"`
- `world_source.cached_splat` / `cached_pano`: relative paths from the config folder; `null` if not cached
- `objects[].instance_id`: stable identifier stored on `SpeechIntentTrackable.configInstanceId` at placement time
- `objects[].prefab_name`: matches `NamedPrefabEntry.name` in `ObjectPlacementController`; `null` for programmatically created objects (e.g. audio sources)
- `objects[].components`: open-ended array — new component types are added without changing the schema
- `lighting`: nullable — absent for configs where lighting was not set

---

## 3. C# Data Classes

**File:** `Assets/App/Save/Runtime/WorldConfig.cs`

Plain C# data classes (no Unity dependencies), serialized with `Newtonsoft.Json`.

```csharp
// Top-level config
public class WorldConfig
{
    public int schema_version = 1;
    public string config_id;
    public string display_name;
    public string created_at;   // ISO 8601 UTC
    public string modified_at;
    public WorldSourceData world_source;
    public string generation_model;
    public List<PromptEntry> prompts = new();
    public List<SavedObject> objects = new();
    public LightingData lighting;
}

public class WorldSourceData
{
    public string type;         // "worldlabs" | "local_splat" | "local_pano" | "url"
    public string world_id;     // WorldLabs world_id; null for local/url
    public string display_name; // human-readable world name
    public string url;          // for type="url"
    public string cached_splat; // relative path to CachedWorlds/; null if not cached
    public string cached_pano;
}

public class PromptEntry
{
    public string timestamp;
    public string type;         // "world_creation" | "voice_command"
    public string intent;       // VoiceIntentType name
    public string transcript;
}

public class SavedObject
{
    public string instance_id;
    public string prefab_name;
    public string display_name;
    public List<SavedComponent> components = new();
}

public class SavedComponent
{
    public string type;
    public JObject data;        // Newtonsoft JObject — free-form per component type
}

public class LightingData
{
    public string preset;
    public float sun_azimuth;
    public float sun_elevation;
}
```

---

## 4. Component Registry

**Files:** `Assets/App/Save/Runtime/IComponentSerializer.cs`, `WorldConfigComponentRegistry.cs`

```csharp
public class RestorationContext
{
    public string ConfigFolderPath;   // absolute path — used to resolve relative asset paths
    public WorldConfig Config;
}

public interface IComponentSerializer
{
    string TypeName { get; }
    JObject Save(GameObject go);
    void Restore(GameObject go, JObject data, RestorationContext ctx);
}
```

`RestorationContext.ConfigFolderPath` lets serializers like `AudioSourceSerializer` resolve `../CachedWorlds/...` relative paths to absolute device paths without baking absolute paths into the JSON (which would break import/export).

`WorldConfigComponentRegistry` is a static class. Built-in serializers self-register via `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]` — no central list to maintain.

**Built-in serializers (initial):**

| TypeName | Saves | Restores |
|---|---|---|
| `Transform` | `position`, `rotation` (quaternion), `scale` from `transform` | Sets `transform.localPosition`, `localRotation`, `localScale` |
| `AudioSource` | `clip_path`, `volume`, `loop`, `spatial_blend` from `AudioSource` component | Creates/configures `AudioSource`, loads clip from path |

Adding a new type: write a class implementing `IComponentSerializer`, call `WorldConfigComponentRegistry.Register(new MySerializer())` in `[RuntimeInitializeOnLoadMethod]`.

---

## 5. Core Services

### 5.1 `WorldConfigStore`

**File:** `Assets/App/Save/Runtime/WorldConfigStore.cs`  
**MonoBehaviour**, lives on the `Systems` GameObject.

**Public API:**
```csharp
public event Action OnConfigsChanged;

public async Task ScanAndMigrateAsync();
public WorldConfig CreateConfig(WorldSourceData source, string displayName, PromptEntry creationPrompt);
public void SaveConfig(WorldConfig config);
public WorldConfig LoadConfig(string configId);
public void DeleteConfig(string configId);
public IReadOnlyList<WorldConfig> ListConfigs();
public bool HasConfigForWorldId(string worldId);
public string WorldsRootPath { get; }
public string CachedWorldsPath { get; }
```

**Folder naming:**
```csharp
private string MakeFolderName(string displayName)
{
    string sanitized = Regex.Replace(displayName, @"[^a-zA-Z0-9]", "_");
    string timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHHmmss");
    return $"{sanitized}_{timestamp}Z";
}
```

### 5.2 `WorldConfigRestorer`

**File:** `Assets/App/Save/Runtime/WorldConfigRestorer.cs`  
**MonoBehaviour**, lives on the `Systems` GameObject.

```csharp
public async Task RestoreAsync(WorldConfig config);
```

Restoration sequence:
1. Resolve world source — prefer `cached_splat` if file exists, else fall back to WorldLabs stream by `world_id`, else `url`
2. Trigger load via `WorldLabsWorldManager` or `LocalRemoteSplatLoader` / `LocalRemotePanoLoader`
3. Await `WorldLabsWorldManager.OnWorldLoaded` (timeout after 30 s, show error via `onRestoreError` event)
4. For each entry in `config.objects`: instantiate prefab via `ObjectPlacementController.FindPrefab` (or create empty GameObject if `prefab_name` is null), call `WorldConfigComponentRegistry.Restore(go, components)`, call `interactionMemory.RegisterCreatedObject(go)`
5. Apply `config.lighting` via `LightRigController` if non-null
6. Set `WorldConfigAutoSave.ActiveConfig = config`

### 5.3 `WorldConfigAutoSave`

**File:** `Assets/App/Save/Runtime/WorldConfigAutoSave.cs`  
**MonoBehaviour**, lives on the `Systems` GameObject.

```csharp
public WorldConfig ActiveConfig { get; set; }
```

**Subscriptions:**
- `WorldLabsWorldManager.OnWorldLoaded` → if no config exists for this world_id, create one via `WorldConfigStore.CreateConfig`; set as `ActiveConfig`
- `WorldActionDispatcher.OnObjectMutated` → re-snapshot affected GameObject, update `ActiveConfig.objects`, append to `ActiveConfig.prompts`, call `WorldConfigStore.SaveConfig`

`WorldActionDispatcher` gains a new event (added to modified files list):
```csharp
public event Action<VoiceIntentCommand, GameObject> OnObjectMutated;
```
Fired at the end of `HandlePlaceObject`, `HandleMoveTarget`, `HandleScaleTarget`, `HandleRotateTarget`, and `HandleResetTransform`, passing the command and the affected GameObject.

**Instance ID assignment:**  
When a new object is placed, `WorldConfigAutoSave` assigns `SpeechIntentTrackable.configInstanceId = $"{prefabName}_{Guid.NewGuid():N}[..8]}"`. This field is added to `SpeechIntentTrackable`.

---

## 6. Startup Scan — `ScanAndMigrateAsync` Detail

Four sequential phases, all errors logged as warnings (scan continues):

**Phase 1 — Ensure directories**  
`Directory.CreateDirectory` for `Worlds/` and `Worlds/CachedWorlds/` if absent.

**Phase 2 — Migrate loose files**  
Scan `Worlds/` root for direct file children. For each:
- Move to `CachedWorlds/`
- Determine type by extension: `.spz`/`.ply` → `local_splat`; `.jpg`/`.jpeg`/`.png`/`.webp` → `local_pano`; other → log warning, skip
- Create config folder + minimal `world.json` (no objects, no prompts, `cached_splat` or `cached_pano` set)
- Unrecognised extensions left in place

**Phase 3 — Load existing config folders**  
Scan `Worlds/` for subdirectories that are not `CachedWorlds/`. Read each `world.json` into a `WorldConfig`. Malformed JSON → log warning, skip. Populate in-memory list.

**Phase 4 — Reconcile with WorldLabs**  
Call `worldManager.ListWorldsAsync()`. Build set of `world_id`s already in in-memory list. For each WorldLabs world not in set: create config folder + minimal `world.json` (`type = "worldlabs"`, both cache paths null).

Fire `OnConfigsChanged` on completion.

---

## 7. Voice Commands

Two new `VoiceIntentType` values added to `VoiceIntentSchemas.cs`:

| Value | Intent name | Trigger phrase examples |
|---|---|---|
| 18 | `SaveWorldConfig` | "save", "save as beach with chairs" |
| 19 | `LoadWorldConfig` | "load my worlds", "load beach with chairs", "show my worlds" |

New field on `VoiceIntentCommand`:
```csharp
[Header("World Config")]
public string config_name = "";  // populated for "save as [name]" and "load [name]"
```

**Dispatch logic in `WorldActionDispatcher`:**
- `SaveWorldConfig` with empty `config_name` → `WorldConfigStore.SaveConfig(autoSave.ActiveConfig)`
- `SaveWorldConfig` with `config_name` → fork: create new config copying active state, set as active
- `LoadWorldConfig` with empty `config_name` → `UiPanelController.Show("my worlds")`
- `LoadWorldConfig` with `config_name` → fuzzy match against `ListConfigs()` display names, call `WorldConfigRestorer.RestoreAsync`

---

## 8. My Worlds UI Panel

**File:** `Assets/App/Command/SpeechIntent/Runtime/UI/MyWorldsPanel.cs`  
Follows existing `LocalFileBrowserPanel` pattern. Registered in `UiPanelController` as `"my worlds"`.

**Card fields:** display name, world source type badge, thumbnail (cached pano if available, else default icon), `modified_at` date.

**Card actions:**
- **Load** → `WorldConfigRestorer.RestoreAsync(config)`
- **Save As** → inline text input → fork config with new display name
- **Delete** → confirmation dialog → `WorldConfigStore.DeleteConfig(configId)`

**WorldLabs browser integration:**  
`WorldBrowserController` calls `WorldConfigStore.HasConfigForWorldId(worldId)` when populating cards. Cards with a matching config show a small bookmark indicator. Tapping the indicator opens `"my worlds"` panel. No structural changes to `WorldBrowserController`.

**Panel opens via:**
- Voice: `"show my worlds"` / `"load [name]"`
- Existing UI button flow via `UiPanelController`

---

## 9. New Files Summary

| Path | Type | Description |
|---|---|---|
| `Assets/App/Save/Runtime/WorldConfig.cs` | Data classes | JSON-serializable config model |
| `Assets/App/Save/Runtime/IComponentSerializer.cs` | Interface | Component save/restore contract |
| `Assets/App/Save/Runtime/WorldConfigComponentRegistry.cs` | Static class | Registry + dispatch for component serializers |
| `Assets/App/Save/Runtime/TransformSerializer.cs` | Serializer | Saves/restores `Transform` |
| `Assets/App/Save/Runtime/AudioSourceSerializer.cs` | Serializer | Saves/restores `AudioSource` |
| `Assets/App/Save/Runtime/WorldConfigStore.cs` | MonoBehaviour | Filesystem CRUD + startup scan |
| `Assets/App/Save/Runtime/WorldConfigRestorer.cs` | MonoBehaviour | Full scene restoration |
| `Assets/App/Save/Runtime/WorldConfigAutoSave.cs` | MonoBehaviour | Event-driven auto-save |
| `Assets/App/Command/SpeechIntent/Runtime/UI/MyWorldsPanel.cs` | MonoBehaviour | My Worlds UI panel |
| `Assets/App/Editor/WorldConfigSceneSetup.cs` | Editor script | Wires Save/Restore/AutoSave into Systems |

**Modified files:**
| Path | Change |
|---|---|
| `VoiceIntentSchemas.cs` | Add `SaveWorldConfig = 18`, `LoadWorldConfig = 19`; add `config_name` field |
| `WorldActionDispatcher.cs` | Add handlers for two new intents; add `worldConfigStore`, `worldConfigRestorer`, `worldConfigAutoSave` fields; add `OnObjectMutated` event |
| `OpenAiSpeechIntentService.cs` | Add new intents + `config_name` to JSON schema |
| `OpenAiSpeechIntentConfig.cs` | Add routing hints for save/load intents |
| `SpeechIntentTrackable.cs` | Add `configInstanceId` field |
| `WorldBrowserController.cs` | Add bookmark indicator + `HasConfigForWorldId` query |
| `ObjectPlacementController.cs` | Make `FindPrefab` `internal` (needed by `WorldConfigRestorer` for prefab lookup during restore) |
