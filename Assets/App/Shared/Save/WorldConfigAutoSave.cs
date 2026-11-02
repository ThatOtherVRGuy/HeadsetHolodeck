// Assets/App/Shared/Save/Runtime/WorldConfigAutoSave.cs
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
        WorldConfig _activeConfig;
        long _configVersion;
        string _activeWorldId;
        WorldTransitionOperation _restoreOperation;
        WorldConfig _restoreConfig;
        bool _restoring;
        public WorldConfig ActiveConfig
        {
            get => _activeConfig;
            set { _activeConfig = value; ++_configVersion; }
        }

        /// <summary>True when the active config is an in-memory draft that has never been written.</summary>
        public bool HasDraftConfig =>
            ActiveConfig != null &&
            string.IsNullOrEmpty(ActiveConfig.config_id) &&
            string.Equals(ActiveConfig.world_source?.type, "static", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Default name for a world saved without one. Timestamped so two saves never collide; the
        /// owner wants this user-selectable later, which is why it lives in one place.
        /// </summary>
        public static string DefaultDraftName(DateTime? nowLocal = null)
        {
            DateTime when = nowLocal ?? DateTime.Now;
            return $"Holodeck {when:yyyy-MM-dd HH:mm}";
        }

        /// <summary>
        /// Establishes an in-memory draft config for the built-in room, so edits made before any world
        /// is loaded (a sound, a prop, a lighting change) are recorded against something and can be
        /// saved. A draft has no <c>config_id</c> and is not in the store until it is materialised on
        /// save, so it never appears in My Worlds by itself.
        /// </summary>
        public WorldConfig EnsureStaticDraft(string displayName = null)
        {
            if (HasDraftConfig) return ActiveConfig;

            string now = DateTime.UtcNow.ToString("yyyy-MM-ddTHHmmssZ");
            var draft = new WorldConfig
            {
                config_id = null,
                display_name = string.IsNullOrWhiteSpace(displayName) ? DefaultDraftName() : displayName.Trim(),
                created_at = now,
                modified_at = now,
                world_source = new WorldSourceData { type = "static" },
            };

            ActiveConfig = draft;
            _activeWorldId = null;
            Debug.Log($"[WorldConfigAutoSave] Draft config '{draft.display_name}' established for the static room.");
            return draft;
        }

        public void BeginRestore(WorldTransitionOperation operation, WorldConfig config)
        {
            operation.ThrowIfStale();
            _restoreOperation = operation;
            _restoreConfig = config;
            _restoring = true;
        }

        public void CompleteRestore(WorldTransitionOperation operation)
        {
            operation.ThrowIfStale();
            if (ReferenceEquals(operation, _restoreOperation)) _restoring = false;
        }

        public void CommitRestore(WorldTransitionOperation operation, string worldId = null)
        {
            operation.ThrowIfStale();
            if (!ReferenceEquals(operation, _restoreOperation)) return;
            if (!ReferenceEquals(ActiveConfig, _restoreConfig)) ActiveConfig = _restoreConfig;
            if (worldId != null) _activeWorldId = worldId;
        }

        public void CancelRestore(WorldTransitionOperation operation)
        {
            if (!ReferenceEquals(operation, _restoreOperation)) return;
            _restoreOperation = null;
            _restoreConfig = null;
            _restoring = false;
            ActiveConfig = null;
            _activeWorldId = null;
        }

        void OnTransitionStarted(WorldTransitionOperation operation)
        {
            _restoreOperation = null;
            _restoreConfig = null;
            _restoring = false;
            ActiveConfig = null;
            _activeWorldId = null;
            _pendingSplatBytes.Clear();
            _pendingPanoBytes.Clear();
        }

        void OnTransitionCancelled(WorldTransitionOperation operation)
        {
            CancelRestore(operation);
            // Cancellation also invalidates async caching for ordinary (non-restore) loads.
            if (worldManager == null || worldManager.CurrentTransition == null ||
                ReferenceEquals(worldManager.CurrentTransition, operation))
            {
                ActiveConfig = null;
                _activeWorldId = null;
                _pendingSplatBytes.Clear();
                _pendingPanoBytes.Clear();
            }
        }

        // Bytes buffered between download events and OnWorldLoaded (which creates the config folder)
        readonly Dictionary<string, byte[]> _pendingSplatBytes = new();
        readonly Dictionary<string, byte[]> _pendingPanoBytes  = new();

        void OnEnable()
        {
            ResolveOptionalDependencies();

            if (worldManager != null)
            {
                worldManager.OnTransitionStarted    += OnTransitionStarted;
                worldManager.OnTransitionCancelled  += OnTransitionCancelled;
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
            // Events while disabled are intentionally missed. Drop the previous binding
            // so re-enabling cannot save mutations into a world that has since unloaded.
            ActiveConfig = null;
            _activeWorldId = null;
            _restoreOperation = null;
            _restoreConfig = null;
            _restoring = false;
            _pendingSplatBytes.Clear();
            _pendingPanoBytes.Clear();
            if (worldManager != null)
            {
                worldManager.OnTransitionStarted    -= OnTransitionStarted;
                worldManager.OnTransitionCancelled  -= OnTransitionCancelled;
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
            if (this == null || !isActiveAndEnabled) return;
            await worldConfigStore.ScanAndMigrateAsync();
            if (this == null || !isActiveAndEnabled) return;

            // The app opens in the built-in room with no world loaded. Give that room a draft straight
            // away so objects, audio and lighting added before any world is loaded are recorded against
            // something and can be saved later.
            if (ActiveConfig == null)
                EnsureStaticDraft();

            if (this == null || !isActiveAndEnabled || worldManager == null) return;
            try
            {
                List<World> worlds = await worldManager.ListWorldsAsync(pageSize: 100);
                if (this != null && isActiveAndEnabled && worldConfigStore != null)
                    worldConfigStore.ReconcileWithWorlds(worlds);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[WorldConfigAutoSave] WorldLabs reconcile failed: {ex.Message}");
            }
        }

        void OnWorldLoaded(string worldId, GaussianSplatRenderer renderer)
        {
            WorldTransitionOperation operation = worldManager != null ? worldManager.CurrentTransition : null;
            if (!IsCurrentOperation(operation)) return;
            if (IsDefaultWorld(worldId))
            {
                // The built-in room is now a saveable place rather than an untracked backdrop: keep a
                // draft so edits to it are recorded and "save this world" has something to write.
                EnsureStaticDraft();
                _pendingSplatBytes.Remove(worldId);
                _pendingPanoBytes.Remove(worldId);
                return;
            }

            if (worldConfigStore == null) return;

            _activeWorldId = worldId;
            if (_restoreOperation != null && _restoreOperation.IsCurrent)
            {
                CommitRestore(_restoreOperation, worldId);
                EnsureEstimatedSpawnPoint(renderer);

                // A restore that had to download the world runs the manager's fresh-load path, which is
                // where Marble's metric scale and ground offset are applied. Record that once, on first
                // capture only: a later restore from the cached splat has no World object to read the
                // metadata from, so recapturing would overwrite the good scale with an unscaled root.
                if (ActiveConfig != null && ActiveConfig.world_transform == null)
                    CaptureLoadedWorldTransform(renderer);

                if (IsCurrentOperation(operation)) DrainPendingBytes(worldId);
                return;
            }

            // Keep existing active config if it's already for this world, but still try to cache
            if (ActiveConfig != null &&
                ActiveConfig.world_source?.world_id == worldId)
            {
                EnsureEstimatedSpawnPoint(renderer);
                CaptureLoadedWorldTransform(renderer);
                if (IsCurrentOperation(operation)) DrainPendingBytes(worldId);
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
                CaptureLoadedWorldTransform(renderer);
                if (IsCurrentOperation(operation)) DrainPendingBytes(worldId);
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

            WorldConfig created = worldConfigStore.CreateConfig(source, world?.display_name ?? "World", prompt);
            if (!IsCurrentOperation(operation)) return;
            ActiveConfig = created;
            ApplyGenerationModel(created);
            if (!IsCurrentOperation(operation)) return;
            EnsureEstimatedSpawnPoint(renderer);
            CaptureLoadedWorldTransform(renderer);
            if (IsCurrentOperation(operation)) DrainPendingBytes(worldId);
        }

        /// <summary>
        /// Records the root transform a fresh load produced. The world manager applies Marble's metric scale
        /// and ground offset to that root, and a restore from a cached splat has no World object to read
        /// them from — so without persisting the transform here, the second visit would revert to unscaled.
        /// </summary>
        void CaptureLoadedWorldTransform(GaussianSplatRenderer renderer)
        {
            if (renderer == null || ActiveConfig == null) return;

            ActiveConfig.world_transform = WorldTransformData.FromTransform(renderer.transform);
            WorldConfig saved = SaveActiveConfigInternal();
            if (saved?.world_transform != null)
            {
                Debug.Log($"[WorldConfigAutoSave] Recorded loaded world transform for '{saved.display_name}': " +
                          $"scale={saved.world_transform.scale.x:0.####}.");
            }
        }

        bool IsCurrentOperation(WorldTransitionOperation operation) => operation == null
            ? worldManager == null || worldManager.CurrentTransition == null
            : operation.IsCurrent && (worldManager == null || ReferenceEquals(worldManager.CurrentTransition, operation));

        void OnWorldUnloaded(string worldId)
        {
            if (ActiveConfig != null && (_activeWorldId == worldId || ActiveConfig.world_source?.world_id == worldId))
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

            _ = EnsurePanoramaAvailableAsync(worldId);
        }

        readonly HashSet<string> _panoPreloadAttempted = new();

        /// <summary>
        /// Downloads a world's panorama in the background when it is not already cached, so switching a
        /// splat world to panorama works the first time it is asked for. Generation and gallery loads take
        /// this path; only the restore path used to preload a panorama. The pano loader's own callback
        /// caches the bytes, so the next load is instant.
        /// </summary>
        async Task EnsurePanoramaAvailableAsync(string worldId)
        {
            try
            {
                WorldConfig config = ActiveConfig;
                if (panoLoader == null || worldManager == null || config?.world_source == null) return;

                // A restore preloads its own panorama in WorldConfigRestorer.PreloadPanoAsync, so doing it
                // here as well would download the same image twice.
                if (_restoreOperation != null && _restoreOperation.IsCurrent) return;

                if (!string.IsNullOrEmpty(config.world_source.cached_pano)) return;
                if (!string.Equals(config.world_source.type, "worldlabs", StringComparison.OrdinalIgnoreCase)) return;

                // A world restored from a cached splat registers a synthetic renderer id
                // ("local_<name>_<worldId>"), which is not a WorldLabs world id. The config keeps the real
                // one, so prefer it and only fall back to the id the load reported.
                string lookupId = !string.IsNullOrEmpty(config.world_source.world_id)
                    ? config.world_source.world_id
                    : worldId;

                if (string.IsNullOrEmpty(lookupId) || !_panoPreloadAttempted.Add(lookupId)) return;

                World world = await worldManager.GetWorldAsync(lookupId);
                if (this == null || !isActiveAndEnabled) return;

                string url = world?.assets?.imagery?.pano_url;
                if (string.IsNullOrEmpty(url))
                {
                    Debug.Log($"[WorldConfigAutoSave] World '{config.display_name}' has no panorama to preload.");
                    return;
                }

                Debug.Log($"[WorldConfigAutoSave] Preloading panorama for '{config.display_name}' so the view switch is available.");
                WorldTransitionOperation operation = WorldTransitionOperation.CreateStandalone();
                await panoLoader.PreloadAndWaitAsync(url, operation);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[WorldConfigAutoSave] Panorama preload failed: {ex.Message}");
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
            WorldTransitionOperation operation = worldManager != null ? worldManager.CurrentTransition : null;
            if (!IsCurrentOperation(operation)) return;
            if (IsDefaultWorld(worldId))
            {
                EnsureStaticDraft();
                _pendingPanoBytes.Remove(worldId);
                return;
            }

            if (worldConfigStore == null) return;

            _activeWorldId = worldId;
            if (_restoreOperation != null && _restoreOperation.IsCurrent)
            {
                CommitRestore(_restoreOperation, worldId);
                if (IsCurrentOperation(operation)) DrainPendingBytes(worldId);
                return;
            }

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
                    WorldConfig created = worldConfigStore.CreateConfig(source, world?.display_name ?? "World", prompt);
                    if (!IsCurrentOperation(operation)) return;
                    ActiveConfig = created;
                    ApplyGenerationModel(created);
                }
            }

            if (IsCurrentOperation(operation)) DrainPendingBytes(worldId);
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
            WorldConfig target = ActiveConfig;
            WorldConfigStore store = worldConfigStore;
            long version = _configVersion;
            if (target?.world_source == null || store == null || bytes == null) return;

            // Skip if this config already has a local splat
            if (!string.IsNullOrEmpty(target.world_source?.cached_splat)) return;

            string fileName  = BuildCachedFileName(target, "spz");
            string cachedDir = store.CachedWorldsPath;
            string absPath   = Path.Combine(cachedDir, fileName);

            try
            {
                await WriteCacheBytesAsync(absPath, bytes);
                if (this == null || version != _configVersion || !ReferenceEquals(ActiveConfig, target)) return;
                target.world_source.cached_splat = $"../CachedWorlds/{fileName}";
                store.SaveConfig(target);
                Debug.Log($"[WorldConfigAutoSave] Cached SPZ for '{target.display_name}' → {fileName} ({bytes.Length:N0} bytes)");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[WorldConfigAutoSave] Failed to cache SPZ for '{target.display_name}': {ex.Message}");
            }
        }

        async Task CachePanoBytesAsync(byte[] bytes)
        {
            WorldConfig target = ActiveConfig;
            WorldConfigStore store = worldConfigStore;
            long version = _configVersion;
            if (target?.world_source == null || store == null || bytes == null) return;

            // Skip if this config already has a local pano
            if (!string.IsNullOrEmpty(target.world_source?.cached_pano)) return;

            string fileName  = BuildCachedFileName(target, "jpg");
            string cachedDir = store.CachedWorldsPath;
            string absPath   = Path.Combine(cachedDir, fileName);

            try
            {
                await WriteCacheBytesAsync(absPath, bytes);
                if (this == null || version != _configVersion || !ReferenceEquals(ActiveConfig, target)) return;
                target.world_source.cached_pano = $"../CachedWorlds/{fileName}";
                store.SaveConfig(target);
                Debug.Log($"[WorldConfigAutoSave] Cached pano for '{target.display_name}' → {fileName} ({bytes.Length:N0} bytes)");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[WorldConfigAutoSave] Failed to cache pano for '{target.display_name}': {ex.Message}");
            }
        }

        protected virtual Task WriteCacheBytesAsync(string path, byte[] bytes) => Task.Run(() =>
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, bytes);
        });

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

            SaveActiveConfigInternal();
            Debug.Log($"[WorldConfigAutoSave] Saved manual spawn point for '{ActiveConfig.display_name}'.");
            return true;
        }

        public bool SaveActiveConfig()
        {
            if (ActiveConfig == null || worldConfigStore == null)
                return false;

            SaveActiveConfigInternal();
            return true;
        }

        void EnsureEstimatedSpawnPoint(GaussianSplatRenderer renderer)
        {
            WorldConfig target = ActiveConfig;
            if (target == null || worldConfigStore == null || renderer == null)
                return;

            target.spawn_points ??= new List<SpawnPointData>();
            if (target.spawn_points.Count > 0)
                return;

            SplatSpawnPose pose = renderer.GetComponent<SplatSpawnPose>();
            if (pose == null || !pose.hasPose)
                return;

            target.spawn_points.Add(new SpawnPointData
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
            worldConfigStore.SaveConfig(target);
            Debug.Log($"[WorldConfigAutoSave] Saved estimated spawn point for '{target.display_name}' using {pose.method}.");
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

        /// <summary>Removes a runtime object's persisted record before the object is destroyed.</summary>
        public bool RemoveSavedObject(GameObject go)
        {
            if (go == null || ActiveConfig?.objects == null)
                return false;

            SpeechIntentTrackable trackable = go.GetComponent<SpeechIntentTrackable>();
            if (trackable == null || string.IsNullOrWhiteSpace(trackable.configInstanceId))
                return false;

            int removed = ActiveConfig.objects.RemoveAll(item => item != null && item.instance_id == trackable.configInstanceId);
            if (removed <= 0)
                return false;

            SaveActiveConfigInternal();
            Debug.Log($"[WorldConfigAutoSave] Removed saved object '{trackable.configInstanceId}'.");
            return true;
        }

        /// <summary>
        /// Returns the active config with a real folder, materialising the in-memory draft first if that
        /// is all there is. A draft has no config_id, so writing it used to throw
        /// "Config '' is not tracked" whenever a mutation — placing a generated object, for example —
        /// happened before the world had ever been saved.
        /// </summary>
        public WorldConfig EnsureMaterialisedActiveConfig(string requestedName = null)
        {
            if (worldConfigStore == null) return null;
            if (ActiveConfig == null) EnsureStaticDraft();
            if (ActiveConfig == null) return null;
            if (!string.IsNullOrEmpty(ActiveConfig.config_id)) return ActiveConfig;

            WorldConfig draft = ActiveConfig;
            string name = string.IsNullOrWhiteSpace(requestedName) ? DefaultDraftName() : requestedName.Trim();

            var source = draft.world_source ?? new WorldSourceData { type = "static" };
            source.display_name = name;

            WorldConfig created = worldConfigStore.CreateConfig(source, name, null);
            created.world_transform = draft.world_transform;
            created.lighting = draft.lighting;
            created.objects = draft.objects ?? new System.Collections.Generic.List<SavedObject>();
            created.spawn_points = draft.spawn_points ?? new System.Collections.Generic.List<SpawnPointData>();
            created.prompts = draft.prompts ?? new System.Collections.Generic.List<PromptEntry>();
            created.generation_model = draft.generation_model;
            if (!string.IsNullOrEmpty(draft.created_at)) created.created_at = draft.created_at;

            ActiveConfig = created;
            Debug.Log($"[WorldConfigAutoSave] Materialised draft into world '{name}' ({created.config_id}).");
            return created;
        }

        /// <summary>Saves the active config, materialising a draft first.</summary>
        WorldConfig SaveActiveConfigInternal()
        {
            WorldConfig config = EnsureMaterialisedActiveConfig();
            if (config == null) return null;

            worldConfigStore.SaveConfig(config);
            return config;
        }

        void OnObjectMutated(VoiceIntentCommand command, GameObject go)
        {
            if (_restoring) return;
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
                SaveActiveConfigInternal();
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

            SaveActiveConfigInternal();
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
