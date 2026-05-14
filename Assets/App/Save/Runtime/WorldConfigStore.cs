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

        public string WorldsRootPath   => RootPath;
        public string CachedWorldsPath => CachedPath;

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
            if (!_configs.Exists(c => c.config_id == config.config_id))
                throw new InvalidOperationException($"[WorldConfigStore] Config '{config.config_id}' is not tracked. Use CreateConfig first.");
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

        public string GetConfigFolderPath(WorldConfig config) =>
            config == null || string.IsNullOrWhiteSpace(config.config_id)
                ? null
                : Path.Combine(RootPath, config.config_id);

        public string GetConfigFolderPath(string configId) =>
            string.IsNullOrWhiteSpace(configId)
                ? null
                : Path.Combine(RootPath, configId);

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
