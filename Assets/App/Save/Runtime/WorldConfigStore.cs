// Assets/App/Save/Runtime/WorldConfigStore.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using WorldLabs.API;      // World
using WorldLabs.Runtime;  // IWorldBookmarkProvider

namespace Holodeck.Save
{
    public class WorldConfigStore : MonoBehaviour, IWorldBookmarkProvider
    {
        public event Action OnConfigsChanged;

        [Header("Dependencies")]
        [SerializeField] WorldLabs.Runtime.WorldLabsWorldManager worldManager;

        readonly List<WorldConfig> _configs = new List<WorldConfig>();
        Task _scanTask;
        static readonly object s_migrationLock = new object();
        static readonly Dictionary<string, Task> s_migrationTasksByRoot = new Dictionary<string, Task>();

        public string WorldsRootPath   => RootPath;
        public string CachedWorldsPath => CachedPath;
        public string CachedObjectsPath => Path.Combine(RootPath, "CachedObjects");

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
        string RootPath => _testRootOverride ?? Path.Combine(Application.persistentDataPath, "Worlds");
        string CachedPath => Path.Combine(RootPath, "CachedWorlds");

        // ── Public CRUD ────────────────────────────────────────────────────────

        /// <summary>Creates a new config folder and world.json. Returns the new WorldConfig.</summary>
        public WorldConfig CreateConfig(WorldSourceData source, string displayName, PromptEntry creationPrompt)
        {
            string now = DateTime.UtcNow.ToString("yyyy-MM-ddTHHmmssZ");
            string id  = CreateUniqueFolderId(RootPath, displayName ?? source?.display_name ?? "World");
            string dir = GetSafeConfigDirectory(id);
            if (dir == null) throw new InvalidOperationException("[WorldConfigStore] Could not create a safe config path.");
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
            if (!_configs.Exists(c => c.config_id == config.config_id))
                throw new InvalidOperationException($"[WorldConfigStore] Config '{config.config_id}' is not tracked. Use CreateConfig first.");
            config.modified_at = DateTime.UtcNow.ToString("yyyy-MM-ddTHHmmssZ");
            WriteJson(config);
            OnConfigsChanged?.Invoke();
        }

        /// <summary>Reads world.json for the given config_id from disk.</summary>
        public WorldConfig LoadConfig(string configId)
        {
            string dir = GetSafeConfigDirectory(configId);
            if (dir == null) return null;

            string path = Path.Combine(dir, "world.json");
            if (!File.Exists(path)) return null;
            return JsonConvert.DeserializeObject<WorldConfig>(File.ReadAllText(path));
        }

        /// <summary>Deletes the config folder from disk and removes it from the in-memory list. Does NOT touch CachedWorlds.</summary>
        public void DeleteConfig(string configId)
        {
            string dir = GetSafeConfigDirectory(configId);
            if (dir == null) return;

            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);

            _configs.RemoveAll(c => c.config_id == configId);
            OnConfigsChanged?.Invoke();
        }

        public IReadOnlyList<WorldConfig> ListConfigs() => _configs.AsReadOnly();

        public string GetConfigFolderPath(WorldConfig config) =>
            config == null || string.IsNullOrWhiteSpace(config.config_id)
                ? null
                : GetSafeConfigDirectory(config.config_id);

        public string GetConfigFolderPath(string configId) =>
            string.IsNullOrWhiteSpace(configId)
                ? null
                : GetSafeConfigDirectory(configId);

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
            string id  = CreateUniqueFolderId(RootPath, newDisplayName);
            string dir = GetSafeConfigDirectory(id);
            if (dir == null) throw new InvalidOperationException("[WorldConfigStore] Could not create a safe fork path.");
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

        /// <summary>Must be called from the Unity main thread — continuation resumes on main thread via Unity's SynchronizationContext.</summary>
        public async Task ScanAndMigrateAsync()
        {
            if (_scanTask != null)
            {
                await _scanTask;
                return;
            }

            _scanTask = ScanAndMigrateInternalAsync();
            try
            {
                await _scanTask;
            }
            finally
            {
                _scanTask = null;
            }
        }

        async Task ScanAndMigrateInternalAsync()
        {
            string root   = RootPath;
            string cached = CachedPath;

            // Phase 1-2: ensure directories and migrate loose files.
            // This is serialized globally per root because a scene can contain more than one
            // WorldConfigStore component. Without this, each instance can migrate the same
            // startup files and create duplicate world.json folders.
            await GetOrCreateMigrationTask(root, cached);

            // Phase 3: load existing config folders
            List<WorldConfig> loaded = await Task.Run(() => LoadExistingConfigs(root));
            _configs.Clear();
            _configs.AddRange(loaded);

            OnConfigsChanged?.Invoke();
            // Phase 4 (WorldLabs reconcile) is handled by WorldConfigAutoSave.
        }

        static Task GetOrCreateMigrationTask(string root, string cached)
        {
            string key = Path.GetFullPath(root);

            lock (s_migrationLock)
            {
                if (s_migrationTasksByRoot.TryGetValue(key, out Task existing))
                    return existing;

                Task created = Task.Run(() =>
                {
                    Directory.CreateDirectory(root);
                    Directory.CreateDirectory(cached);
                    MigrateLooseFiles(root, cached);
                });

                s_migrationTasksByRoot[key] = created;
                _ = created.ContinueWith(_ =>
                {
                    lock (s_migrationLock)
                    {
                        if (s_migrationTasksByRoot.TryGetValue(key, out Task current) && ReferenceEquals(current, created))
                            s_migrationTasksByRoot.Remove(key);
                    }
                });

                return created;
            }
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
                string id  = CreateUniqueFolderId(RootPath, w.display_name ?? "World");
                string dir = GetSafeConfigDirectory(id);
                if (dir == null) continue;
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
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (!IsSafeConfigId(config.config_id))
                config.config_id = CreateUniqueFolderId(RootPath, config.display_name ?? "World");

            string dir = GetSafeConfigDirectory(config.config_id);
            if (dir == null)
            {
                config.config_id = CreateUniqueFolderId(RootPath, config.display_name ?? "World");
                dir = GetSafeConfigDirectory(config.config_id);
            }
            if (dir == null) throw new InvalidOperationException("[WorldConfigStore] Could not create a safe config path.");

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

        static string CreateUniqueFolderId(string root, string displayName)
        {
            string baseName = MakeFolderName(displayName);
            for (int attempt = 0; attempt < 20; attempt++)
            {
                string suffix = Guid.NewGuid().ToString("N").Substring(0, 6);
                string id = $"{baseName}_{suffix}";
                string dir = GetSafeConfigDirectory(root, id);
                if (dir != null && !Directory.Exists(dir)) return id;
            }

            return $"{baseName}_{Guid.NewGuid():N}";
        }

        string GetSafeConfigDirectory(string configId) => GetSafeConfigDirectory(RootPath, configId);

        static string GetSafeConfigDirectory(string root, string configId)
        {
            if (!IsSafeConfigId(configId)) return null;

            string candidate = Path.GetFullPath(Path.Combine(root, configId));
            return IsPathUnder(root, candidate) ? candidate : null;
        }

        static bool IsSafeConfigId(string configId)
        {
            return !string.IsNullOrWhiteSpace(configId)
                && Regex.IsMatch(configId, @"^[A-Za-z0-9_.-]+$");
        }

        static bool IsPathUnder(string rootPath, string candidatePath)
        {
            string root = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string candidate = Path.GetFullPath(candidatePath);
            return candidate.StartsWith(root, StringComparison.Ordinal);
        }

        static readonly string[] SplatExtensions = { ".spz", ".ply" };
        static readonly string[] PanoExtensions  = { ".jpg", ".jpeg", ".png", ".webp" };

        /// <summary>
        /// Moves loose files to CachedWorlds/ and writes world.json entries to disk.
        /// Does NOT update _configs — callers must run LoadExistingConfigs (Phase 3) afterward to pick up the new entries.
        /// </summary>
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
                string cachedFileName = Path.GetFileName(destination);
                if (HasExistingConfigForCachedAsset(root, cachedFileName))
                {
                    Debug.LogWarning($"[WorldConfigStore] Migrated {fileName} to CachedWorlds, but a config already references {cachedFileName}; skipping duplicate config.");
                    continue;
                }

                string id   = CreateUniqueFolderId(root, name);
                string dir  = GetSafeConfigDirectory(root, id);
                if (dir == null)
                {
                    Debug.LogWarning($"[WorldConfigStore] Could not create safe config folder for {fileName}.");
                    continue;
                }
                Directory.CreateDirectory(dir);

                // Relative path from config folder to CachedWorlds
                string relativePath = $"../CachedWorlds/{cachedFileName}";
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

        static bool HasExistingConfigForCachedAsset(string root, string cachedFileName)
        {
            if (string.IsNullOrWhiteSpace(cachedFileName) || !Directory.Exists(root))
                return false;

            foreach (string dir in Directory.GetDirectories(root))
            {
                if (Path.GetFileName(dir) == "CachedWorlds") continue;

                string jsonPath = Path.Combine(dir, "world.json");
                if (!File.Exists(jsonPath)) continue;

                try
                {
                    WorldConfig config = JsonConvert.DeserializeObject<WorldConfig>(File.ReadAllText(jsonPath));
                    string splatName = Path.GetFileName(config?.world_source?.cached_splat ?? string.Empty);
                    string panoName = Path.GetFileName(config?.world_source?.cached_pano ?? string.Empty);

                    if (string.Equals(splatName, cachedFileName, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(panoName, cachedFileName, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[WorldConfigStore] Could not inspect {jsonPath} for duplicate cached asset references: {ex.Message}");
                }
            }

            return false;
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
                    if (c != null)
                    {
                        string folderId = Path.GetFileName(dir);
                        if (!IsSafeConfigId(c.config_id) || GetSafeConfigDirectory(root, c.config_id) != Path.GetFullPath(dir))
                            c.config_id = folderId;

                        result.Add(c);
                    }
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
