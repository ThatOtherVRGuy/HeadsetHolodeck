// Assets/App/Save/Runtime/WorldConfigAutoSave.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using GaussianSplatting.Runtime;
using UnityEngine;
using WorldLabs.Runtime;
using WorldLabs.API;
using SpeechIntent;
using Holodeck.Direct;
using WorldLabs.Runtime.Tools;

namespace Holodeck.Save
{
    public class WorldConfigAutoSave : MonoBehaviour
    {
        const string DefaultWorldId = "__default__";

        [Header("Dependencies")]
        public WorldConfigStore      worldConfigStore;
        public WorldLabsWorldManager worldManager;
        public WorldBrowserController worldBrowser;
        public LocalRemotePanoLoader  panoLoader;
        public WorldActionDispatcher dispatcher;
        public VoiceToWorldLabsPluginCoordinator coordinator;
        public InteractionMemory interactionMemory;

        /// <summary>The config that corresponds to the currently loaded world.</summary>
        public WorldConfig ActiveConfig { get; set; }

        // Bytes buffered between download events and OnWorldLoaded (which creates the config folder)
        readonly Dictionary<string, byte[]> _pendingSplatBytes = new();
        readonly Dictionary<string, byte[]> _pendingPanoBytes  = new();

        void OnEnable()
        {
            ResolveOptionalDependencies();

            if (worldManager != null)
            {
                worldManager.OnWorldLoaded          += OnWorldLoaded;
                worldManager.OnWorldUnloaded        += OnWorldUnloaded;
                worldManager.OnSplatBytesDownloaded += OnSplatBytesDownloaded;
                worldManager.OnWorldLoadFailed      += OnWorldLoadFailed;
            }
            if (worldBrowser != null)
            {
                worldBrowser.OnPanoBytesDownloaded += OnPanoBytesDownloaded;
                worldBrowser.OnPanoWorldShown      += OnPanoWorldShown;
            }
            else
                Debug.LogWarning("[WorldConfigAutoSave] worldBrowser is not assigned — panoramic images will not be cached. " +
                                 "Re-run Holodeck > Setup World Config.", this);
            if (panoLoader != null)
                panoLoader.OnPanoBytesLoaded += OnPanoLoaderBytesLoaded;
            if (dispatcher != null)
                dispatcher.OnObjectMutated += OnObjectMutated;
        }

        void OnDisable()
        {
            if (worldManager != null)
            {
                worldManager.OnWorldLoaded          -= OnWorldLoaded;
                worldManager.OnWorldUnloaded        -= OnWorldUnloaded;
                worldManager.OnSplatBytesDownloaded -= OnSplatBytesDownloaded;
                worldManager.OnWorldLoadFailed      -= OnWorldLoadFailed;
            }
            if (worldBrowser != null)
            {
                worldBrowser.OnPanoBytesDownloaded -= OnPanoBytesDownloaded;
                worldBrowser.OnPanoWorldShown      -= OnPanoWorldShown;
            }
            if (panoLoader != null)
                panoLoader.OnPanoBytesLoaded -= OnPanoLoaderBytesLoaded;
            if (dispatcher != null)
                dispatcher.OnObjectMutated -= OnObjectMutated;
        }

        async void Start()
        {
            if (worldConfigStore == null) return;

            await Task.Yield();
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

        void OnWorldLoaded(string worldId, GaussianSplatRenderer renderer)
        {
            if (IsDefaultWorld(worldId))
            {
                ActiveConfig = null;
                _pendingSplatBytes.Remove(worldId);
                _pendingPanoBytes.Remove(worldId);
                return;
            }

            if (worldConfigStore == null) return;

            // Keep existing active config if it's already for this world, but still try to cache
            if (ActiveConfig != null &&
                ActiveConfig.world_source?.world_id == worldId)
            {
                EnsureEstimatedSpawnPoint(renderer);
                DrainPendingBytes(worldId);
                return;
            }

            // Find existing config
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
                EnsureEstimatedSpawnPoint(renderer);
                DrainPendingBytes(worldId);
                return;
            }

            // If ActiveConfig is already set and the incoming worldId is a local fake (generated by
            // LocalRemoteSplatLoader when loading from a cached file), this is a restore — don't create
            // a duplicate config.
            if (ActiveConfig != null && worldId.StartsWith("local_", StringComparison.Ordinal))
            {
                Debug.Log($"[WorldConfigAutoSave] OnWorldLoaded: local worldId '{worldId}' fired during restore of '{ActiveConfig.display_name}' — skipping new config creation.");
                EnsureEstimatedSpawnPoint(renderer);
                return;
            }

            // Create new config (synchronous small file write — acceptable for single JSON file)
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
            ApplyGenerationModel(ActiveConfig);
            EnsureEstimatedSpawnPoint(renderer);
            DrainPendingBytes(worldId);
        }

        void OnWorldUnloaded(string worldId)
        {
            if (ActiveConfig != null && ActiveConfig.world_source?.world_id == worldId)
                ActiveConfig = null;
        }

        void DrainPendingBytes(string worldId)
        {
            if (_pendingSplatBytes.TryGetValue(worldId, out byte[] splatBytes))
            {
                _pendingSplatBytes.Remove(worldId);
                _ = CacheSplatBytesAsync(splatBytes);
            }
            if (_pendingPanoBytes.TryGetValue(worldId, out byte[] panoBytes))
            {
                _pendingPanoBytes.Remove(worldId);
                _ = CachePanoBytesAsync(panoBytes);
            }
        }

        void OnSplatBytesDownloaded(string worldId, byte[] bytes)
        {
            if (bytes != null)
                _pendingSplatBytes[worldId] = bytes;
        }

        /// <summary>
        /// Fired by WorldBrowserController when panorama-only mode finishes displaying a world.
        /// OnWorldLoaded never fires in this case, so this is our cue to drain pending pano bytes.
        /// </summary>
        void OnPanoWorldShown(string worldId)
        {
            if (IsDefaultWorld(worldId))
            {
                ActiveConfig = null;
                _pendingPanoBytes.Remove(worldId);
                return;
            }

            if (worldConfigStore == null) return;

            // Set ActiveConfig if not already pointing at this world
            if (ActiveConfig == null || ActiveConfig.world_source?.world_id != worldId)
            {
                WorldConfig existing = null;
                foreach (WorldConfig c in worldConfigStore.ListConfigs())
                {
                    if (c.world_source?.world_id == worldId) { existing = c; break; }
                }

                if (existing != null)
                {
                    ActiveConfig = existing;
                }
                else
                {
                    // First time this pano-only world has been seen — create a config for it
                    World world = worldBrowser.LastClickedWorld;
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
                    ApplyGenerationModel(ActiveConfig);
                }
            }

            DrainPendingBytes(worldId);
        }

        /// <summary>
        /// Fired by LocalRemotePanoLoader when a pano is downloaded from a URL (load or preload path).
        /// ActiveConfig is already set at this point, so cache directly without buffering.
        /// </summary>
        void OnPanoLoaderBytesLoaded(byte[] bytes)
        {
            if (bytes != null)
                _ = CachePanoBytesAsync(bytes);
        }

        void OnPanoBytesDownloaded(string worldId, byte[] bytes)
        {
            if (bytes != null)
                _pendingPanoBytes[worldId] = bytes;
        }

        void OnWorldLoadFailed(string worldId, string _)
        {
            _pendingSplatBytes.Remove(worldId);
            _pendingPanoBytes.Remove(worldId);
        }

        async Task CacheSplatBytesAsync(byte[] bytes)
        {
            if (ActiveConfig == null || worldConfigStore == null || bytes == null) return;

            // Skip if this config already has a local splat
            if (!string.IsNullOrEmpty(ActiveConfig.world_source?.cached_splat)) return;

            string fileName  = BuildCachedFileName(ActiveConfig, "spz");
            string cachedDir = worldConfigStore.CachedWorldsPath;
            string absPath   = Path.Combine(cachedDir, fileName);

            try
            {
                await Task.Run(() =>
                {
                    Directory.CreateDirectory(cachedDir);
                    File.WriteAllBytes(absPath, bytes);
                });
                ActiveConfig.world_source.cached_splat = $"../CachedWorlds/{fileName}";
                worldConfigStore.SaveConfig(ActiveConfig);
                Debug.Log($"[WorldConfigAutoSave] Cached SPZ for '{ActiveConfig.display_name}' → {fileName} ({bytes.Length:N0} bytes)");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[WorldConfigAutoSave] Failed to cache SPZ for '{ActiveConfig.display_name}': {ex.Message}");
            }
        }

        async Task CachePanoBytesAsync(byte[] bytes)
        {
            if (ActiveConfig == null || worldConfigStore == null || bytes == null) return;

            // Skip if this config already has a local pano
            if (!string.IsNullOrEmpty(ActiveConfig.world_source?.cached_pano)) return;

            string fileName  = BuildCachedFileName(ActiveConfig, "jpg");
            string cachedDir = worldConfigStore.CachedWorldsPath;
            string absPath   = Path.Combine(cachedDir, fileName);

            try
            {
                await Task.Run(() =>
                {
                    Directory.CreateDirectory(cachedDir);
                    File.WriteAllBytes(absPath, bytes);
                });
                ActiveConfig.world_source.cached_pano = $"../CachedWorlds/{fileName}";
                worldConfigStore.SaveConfig(ActiveConfig);
                Debug.Log($"[WorldConfigAutoSave] Cached pano for '{ActiveConfig.display_name}' → {fileName} ({bytes.Length:N0} bytes)");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[WorldConfigAutoSave] Failed to cache pano for '{ActiveConfig.display_name}': {ex.Message}");
            }
        }

        /// <summary>
        /// Builds a filename like "MyBeachScene_abc123def456.spz".
        /// Uses up to 20 alphanumeric characters of the display name,
        /// followed by _ and the WorldLabs world ID (or a UTC timestamp if unavailable).
        /// </summary>
        static string BuildCachedFileName(WorldConfig config, string ext)
        {
            string raw = config.display_name ?? "World";
            var sb = new StringBuilder();
            foreach (char c in raw)
                if (char.IsLetterOrDigit(c)) sb.Append(c);
            string safeName = sb.ToString();
            if (safeName.Length > 20) safeName = safeName.Substring(0, 20);
            if (safeName.Length == 0) safeName = "World";

            string worldId = config.world_source?.world_id;
            string suffix  = !string.IsNullOrEmpty(worldId) ? worldId : DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ");

            return $"{safeName}_{suffix}.{ext}";
        }

        static bool IsDefaultWorld(string worldId) =>
            string.Equals(worldId, DefaultWorldId, StringComparison.Ordinal);

        void ResolveOptionalDependencies()
        {
            if (interactionMemory == null)
                interactionMemory = FindFirstObjectByType<InteractionMemory>(FindObjectsInactive.Include);

            if (coordinator == null && dispatcher != null && dispatcher.coordinator != null)
            {
                coordinator = dispatcher.coordinator;
            }

            if (coordinator != null)
                return;

#if UNITY_2023_1_OR_NEWER
            coordinator = FindFirstObjectByType<VoiceToWorldLabsPluginCoordinator>();
#else
            coordinator = FindObjectOfType<VoiceToWorldLabsPluginCoordinator>();
#endif
        }

        void ApplyGenerationModel(WorldConfig config)
        {
            if (config == null || worldConfigStore == null)
                return;

            string model = CurrentGenerationModelLabel();
            if (string.IsNullOrWhiteSpace(model))
                return;

            config.generation_model = model;
            worldConfigStore.SaveConfig(config);
        }

        public bool SaveManualSpawnPoint(Transform playerRoot, string displayName = null)
        {
            if (playerRoot == null || ActiveConfig == null || worldConfigStore == null)
                return false;

            ActiveConfig.spawn_points ??= new List<SpawnPointData>();
            ActiveConfig.spawn_points.Add(new SpawnPointData
            {
                id = Guid.NewGuid().ToString("N"),
                name = string.IsNullOrWhiteSpace(displayName)
                    ? $"Spawn {ActiveConfig.spawn_points.Count + 1}"
                    : displayName,
                source = "manual",
                method = "manual",
                created_at = DateTime.UtcNow.ToString("yyyy-MM-ddTHHmmssZ"),
                position = playerRoot.position,
                rotation = playerRoot.rotation,
                look_at = playerRoot.position + playerRoot.forward,
                confidence = 1f
            });

            worldConfigStore.SaveConfig(ActiveConfig);
            Debug.Log($"[WorldConfigAutoSave] Saved manual spawn point for '{ActiveConfig.display_name}'.");
            return true;
        }

        public bool SaveActiveConfig()
        {
            if (ActiveConfig == null || worldConfigStore == null)
                return false;

            worldConfigStore.SaveConfig(ActiveConfig);
            return true;
        }

        void EnsureEstimatedSpawnPoint(GaussianSplatRenderer renderer)
        {
            if (ActiveConfig == null || worldConfigStore == null || renderer == null)
                return;

            ActiveConfig.spawn_points ??= new List<SpawnPointData>();
            if (ActiveConfig.spawn_points.Count > 0)
                return;

            SplatSpawnPose pose = renderer.GetComponent<SplatSpawnPose>();
            if (pose == null || !pose.hasPose)
                return;

            ActiveConfig.spawn_points.Add(new SpawnPointData
            {
                id = Guid.NewGuid().ToString("N"),
                name = "Estimated Spawn 1",
                source = "estimated",
                method = pose.method,
                created_at = DateTime.UtcNow.ToString("yyyy-MM-ddTHHmmssZ"),
                position = pose.playerPosition,
                rotation = pose.playerRotation,
                look_at = pose.lookAt,
                confidence = pose.confidence
            });
            worldConfigStore.SaveConfig(ActiveConfig);
            Debug.Log($"[WorldConfigAutoSave] Saved estimated spawn point for '{ActiveConfig.display_name}' using {pose.method}.");
        }

        string CurrentGenerationModelLabel()
        {
            if (coordinator != null)
                return coordinator.CurrentGenerationModelName;

            if (worldBrowser != null)
                return WorldBrowserController.GetGenerationModelLabel(worldBrowser.SelectedGenerationModel);

            return null;
        }

        public void RegisterObjectMutation(VoiceIntentCommand command, GameObject go)
        {
            OnObjectMutated(command, go);
        }

        void OnObjectMutated(VoiceIntentCommand command, GameObject go)
        {
            if (worldConfigStore == null || ActiveConfig == null) return;
            if (go == null) return;

            if (IsCurrentWorldRoot(go))
            {
                ActiveConfig.world_transform = WorldTransformData.FromTransform(go.transform);
                ActiveConfig.prompts.Add(new PromptEntry
                {
                    timestamp  = DateTime.UtcNow.ToString("yyyy-MM-ddTHHmmssZ"),
                    type       = "voice_command",
                    intent     = command.intent.ToString(),
                    transcript = command.transcript
                });
                worldConfigStore.SaveConfig(ActiveConfig);
                Debug.Log($"[WorldConfigAutoSave] Saved world transform for '{ActiveConfig.display_name}'.");
                return;
            }

            // Ensure stable instance ID
            SpeechIntentTrackable trackable = go.GetComponent<SpeechIntentTrackable>();
            if (trackable == null) trackable = go.AddComponent<SpeechIntentTrackable>();
            if (string.IsNullOrEmpty(trackable.configInstanceId))
            {
                string raw = $"{go.name}_{Guid.NewGuid():N}";
                trackable.configInstanceId = raw.Length > 24 ? raw.Substring(0, 24) : raw;
            }

            // Snapshot all registered components
            List<SavedComponent> components = WorldConfigComponentRegistry.SaveAll(go);

            // Update or insert SavedObject
            string id  = trackable.configInstanceId;
            int    idx = ActiveConfig.objects.FindIndex(o => o.instance_id == id);
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

        bool IsCurrentWorldRoot(GameObject go)
        {
            if (go == null)
                return false;

            if (interactionMemory == null)
                interactionMemory = FindFirstObjectByType<InteractionMemory>(FindObjectsInactive.Include);

            return interactionMemory != null && interactionMemory.currentWorldRoot == go;
        }
    }
}
