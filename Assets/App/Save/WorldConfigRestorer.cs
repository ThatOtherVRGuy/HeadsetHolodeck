// Assets/App/Save/Runtime/WorldConfigRestorer.cs
using System;
using System.Collections;
using System.IO;
using System.Threading.Tasks;
using GaussianSplatting.Runtime;
using Holodeck.Direct;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Networking;
using WorldLabs.API;
using WorldLabs.Runtime;
using SpeechIntent;
using WorldLabs.Runtime.Tools;

namespace Holodeck.Save
{
    public class WorldConfigRestorer : MonoBehaviour
    {
        [Header("Dependencies")]
        public WorldConfigStore          worldConfigStore;
        public WorldLabsWorldManager     worldManager;
        public WorldConfigAutoSave       worldConfigAutoSave;
        public ObjectPlacementController objectPlacement;
        public InteractionMemory         interactionMemory;
        public LightRigController        lightRig;
        public LocalRemoteSplatLoader    splatLoader;
        public LocalRemotePanoLoader     panoLoader;
        public Transform                 placedObjectsParent;
        public CachedObjectStore         cachedObjectStore;
        public ObjectGenerationService   objectGenerationService;

        [Header("Events")]
        public UnityEvent onRestoreStarted;
        public StringEvent onRestoreError;
        public UnityEvent onRestoreComplete;

        const float RestoreTimeoutSeconds = 30f;
        const float CachedObjectImportTimeoutSeconds = 15f;

        public async Task RestoreAsync(WorldConfig config)
        {
            if (config == null)
            {
                onRestoreError?.Invoke("No config provided.");
                return;
            }

            onRestoreStarted?.Invoke();
            Debug.Log($"[WorldConfigRestorer] Restoring '{config.display_name}'");

            // Tell AutoSave which config is active BEFORE loading, so OnWorldLoaded doesn't
            // create a duplicate when LocalRemoteSplatLoader registers a "local_..." worldId.
            if (worldConfigAutoSave != null)
                worldConfigAutoSave.ActiveConfig = config;

            // 1. Load world
            bool worldLoaded = await LoadWorldAsync(config);
            if (!worldLoaded)
            {
                onRestoreError?.Invoke($"Could not load world for '{config.display_name}'.");
                return;
            }

            ApplyWorldTransform(config);

            // 1b. Preload cached pano into skybox (without displaying it) so "pano" voice command works.
            _ = PreloadPanoAsync(config);

            // 2. Restore objects
            string configFolderPath = Path.Combine(worldConfigStore.WorldsRootPath, config.config_id);
            var ctx = new RestorationContext { ConfigFolderPath = configFolderPath, Config = config };

            foreach (SavedObject savedObj in config.objects)
            {
                try   { await RestoreObjectAsync(savedObj, ctx); }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[WorldConfigRestorer] Object '{savedObj.instance_id}' restore failed: {ex.Message}");
                }

                await Task.Yield();
            }

            // 3. Load deferred audio clips
            StartCoroutine(LoadAudioClipsCoroutine());

            // 4. Apply lighting
            if (config.lighting != null && lightRig != null)
            {
                if (!string.IsNullOrEmpty(config.lighting.preset))
                    lightRig.ApplyPreset(config.lighting.preset);
            }

            // Note: onRestoreComplete fires before audio clips finish loading (LoadAudioClipsCoroutine runs async).
            // Callers should not assume audio is ready when this event fires.
            onRestoreComplete?.Invoke();
            Debug.Log($"[WorldConfigRestorer] Restore complete for '{config.display_name}'");
        }

        /// <summary>
        /// Preloads the panorama texture into the skybox without displaying it.
        /// Prefers the cached local file; falls back to fetching the pano URL from the WorldLabs API.
        /// Fire-and-forget — called with discard (_=).
        /// </summary>
        async Task PreloadPanoAsync(WorldConfig config)
        {
            if (panoLoader == null) return;

            WorldSourceData src = config.world_source;
            if (src == null) return;

            // Prefer cached local pano
            if (!string.IsNullOrEmpty(src.cached_pano))
            {
                string absPath = ResolvePath(config.config_id, src.cached_pano);
                if (!string.IsNullOrEmpty(absPath) && File.Exists(absPath))
                {
                    panoLoader.PreloadAsync(absPath);
                    return;
                }
            }

            // Fall back to WorldLabs API pano_url
            if (src.type == "worldlabs" && !string.IsNullOrEmpty(src.world_id) && worldManager != null)
            {
                World world = await worldManager.GetWorldAsync(src.world_id);
                string panoUrl = world?.assets?.imagery?.pano_url;
                if (!string.IsNullOrEmpty(panoUrl))
                {
                    Debug.Log($"[WorldConfigRestorer] Preloading pano from API URL for '{config.display_name}'");
                    panoLoader.PreloadAsync(panoUrl);
                }
                else
                {
                    Debug.LogWarning($"[WorldConfigRestorer] No pano_url found in WorldLabs assets for '{config.display_name}'");
                }
            }
        }

        async Task<bool> LoadWorldAsync(WorldConfig config)
        {
            WorldSourceData src = config.world_source;
            if (src == null) return false;

            // Prefer cached splat if it exists
            if (!string.IsNullOrEmpty(src.cached_splat))
            {
                string absPath = ResolvePath(config.config_id, src.cached_splat);
                if (!string.IsNullOrEmpty(absPath) && File.Exists(absPath))
                {
                    RuntimeSplatFloorLoader.SplatSourceKind sourceKind = ResolveSplatSourceKindForWorldSource(src);
                    SplatSpawnMetadata savedSpawn = FirstSavedSpawnMetadata(config);
                    bool loaded = await WaitForWorldLoadedAsync(() => splatLoader?.LoadAsync(absPath, src.display_name ?? config.display_name, sourceKind, savedSpawn));
                    if (loaded) return true;
                }
            }

            // Fall back to WorldLabs stream
            if (src.type == "worldlabs" && !string.IsNullOrEmpty(src.world_id) && worldManager != null)
            {
                // Fetch full world data so LoadWorldAsync has the assets (splat URLs) it needs.
                World world = await worldManager.GetWorldAsync(src.world_id);
                if (world == null)
                {
                    // GetWorldAsync already logged the error; fall through to remaining fallbacks.
                    Debug.LogWarning($"[WorldConfigRestorer] Could not fetch world data for '{src.world_id}' — cannot stream from WorldLabs.");
                }
                else
                {
                    return await WaitForWorldLoadedAsync(() => _ = worldManager.LoadWorldAsync(world));
                }
            }

            // Local pano
            if (!string.IsNullOrEmpty(src.cached_pano))
            {
                string absPath = ResolvePath(config.config_id, src.cached_pano);
                if (!string.IsNullOrEmpty(absPath) && File.Exists(absPath))
                {
                    bool loaded = await WaitForWorldLoadedAsync(() => panoLoader?.LoadAsync(absPath));
                    if (loaded) return true;
                }
            }

            // URL fallback
            if (!string.IsNullOrEmpty(src.url))
            {
                return await WaitForWorldLoadedAsync(() => splatLoader?.LoadAsync(src.url));
            }

            return false;
        }

        static RuntimeSplatFloorLoader.SplatSourceKind ResolveSplatSourceKindForWorldSource(WorldSourceData source)
        {
            return string.Equals(source?.type, "worldlabs", StringComparison.OrdinalIgnoreCase)
                ? RuntimeSplatFloorLoader.SplatSourceKind.WorldLabs
                : RuntimeSplatFloorLoader.SplatSourceKind.LooseSplat;
        }

        static SplatSpawnMetadata FirstSavedSpawnMetadata(WorldConfig config)
        {
            SpawnPointData spawn = config?.spawn_points != null && config.spawn_points.Count > 0
                ? config.spawn_points[0]
                : null;
            if (spawn == null)
                return null;

            return new SplatSpawnMetadata
            {
                hasPose = true,
                method = string.IsNullOrWhiteSpace(spawn.method) ? "saved_spawn_point" : spawn.method,
                spawn = spawn.position,
                rotation = spawn.rotation,
                lookAt = spawn.look_at,
                confidence = spawn.confidence,
                warnings = Array.Empty<string>()
            };
        }

        void ApplyWorldTransform(WorldConfig config)
        {
            if (config?.world_transform == null)
                return;

            GameObject worldRoot = interactionMemory != null ? interactionMemory.currentWorldRoot : null;
            if (worldRoot == null)
            {
                Debug.LogWarning($"[WorldConfigRestorer] Cannot apply saved world transform for '{config.display_name}': no current world root.");
                return;
            }

            config.world_transform.ApplyTo(worldRoot.transform);
            Debug.Log($"[WorldConfigRestorer] Applied saved world transform for '{config.display_name}'.");
        }

        // OnWorldLoaded is Action<string, GaussianSplatRenderer> (worldId, renderer)
        async Task<bool> WaitForWorldLoadedAsync(Action triggerLoad)
        {
            if (worldManager == null)
            {
                Debug.LogError("[WorldConfigRestorer] worldManager is null — cannot subscribe to OnWorldLoaded. Assign it in the Inspector.");
                return false;
            }

            bool received = false;
            void OnLoaded(string worldId, GaussianSplatRenderer renderer) => received = true;
            worldManager.OnWorldLoaded += OnLoaded;

            triggerLoad?.Invoke();

            float startTime = Time.realtimeSinceStartup;
            while (!received && (Time.realtimeSinceStartup - startTime) < RestoreTimeoutSeconds)
            {
                await Task.Delay(100);
            }

            worldManager.OnWorldLoaded -= OnLoaded;
            return received;
        }

        async Task RestoreObjectAsync(SavedObject savedObj, RestorationContext ctx)
        {
            GameObject go = await TryRestoreCachedObjectAsync(savedObj);
            if (go == null)
                go = CreatePlaceholderObject(savedObj);

            if (placedObjectsParent != null && go.transform.parent != placedObjectsParent)
                go.transform.SetParent(placedObjectsParent, true);

            SpeechIntentTrackable trackable = go.GetComponent<SpeechIntentTrackable>()
                                           ?? go.AddComponent<SpeechIntentTrackable>();
            trackable.canonicalName    = savedObj.display_name ?? savedObj.prefab_name ?? go.name;
            trackable.configInstanceId = savedObj.instance_id;

            WorldConfigComponentRegistry.RestoreAll(go, savedObj.components, ctx);

            interactionMemory?.RegisterCreatedObject(go);
        }

        GameObject CreatePlaceholderObject(SavedObject savedObj)
        {
            if (!string.IsNullOrEmpty(savedObj.prefab_name) && objectPlacement != null)
            {
                GameObject prefab = objectPlacement.FindPrefab(savedObj.prefab_name);
                return prefab != null
                    ? Instantiate(prefab, placedObjectsParent)
                    : new GameObject($"Restored_{savedObj.prefab_name}");
            }

            return new GameObject(savedObj.display_name ?? "RestoredObject");
        }

        async Task<GameObject> TryRestoreCachedObjectAsync(SavedObject savedObj)
        {
            string cachedObjectId = TryGetCachedObjectId(savedObj);
            if (string.IsNullOrWhiteSpace(cachedObjectId))
                return null;

            ResolveCachedObjectDependencies();

            if (!TryLoadCachedObjectRecord(cachedObjectStore, cachedObjectId, out CachedObjectRecord record, out string loadError))
            {
                string suffix = string.IsNullOrWhiteSpace(loadError) ? "" : $" {loadError}";
                Debug.LogWarning($"[WorldConfigRestorer] Cached object '{cachedObjectId}' could not be loaded. Restoring placeholder for '{savedObj.instance_id}'.{suffix}");
                return null;
            }

            if (objectGenerationService == null)
            {
                Debug.LogWarning($"[WorldConfigRestorer] ObjectGenerationService was not found. Restoring placeholder for cached object '{cachedObjectId}'.");
                return null;
            }

            GameObject imported = null;
            GameObject importRoot = null;
            string importError = null;
            bool done = false;
            bool cancelled = false;
            Coroutine importCoroutine = null;

            try
            {
                importCoroutine = StartCoroutine(objectGenerationService.ImportCachedObject(record, null, null, (go, error) =>
                {
                    if (cancelled)
                    {
                        if (go != null)
                            Destroy(go);
                        return;
                    }

                    imported = go;
                    importError = error;
                    done = true;
                }, root =>
                {
                    importRoot = root;
                    if (cancelled && importRoot != null)
                    {
                        Destroy(importRoot);
                        importRoot = null;
                    }
                }));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[WorldConfigRestorer] Cached object '{cachedObjectId}' import could not start: {ex.Message}. Restoring placeholder.");
                return null;
            }

            float startTime = Time.realtimeSinceStartup;
            while (!done && (Time.realtimeSinceStartup - startTime) < CachedObjectImportTimeoutSeconds)
                await Task.Yield();

            if (!done)
            {
                cancelled = true;
                if (importCoroutine != null)
                    StopCoroutine(importCoroutine);
                if (importRoot != null)
                {
                    Destroy(importRoot);
                    importRoot = null;
                }

                Debug.LogWarning($"[WorldConfigRestorer] Cached object '{cachedObjectId}' import timed out after {CachedObjectImportTimeoutSeconds:0.#} seconds. Restoring placeholder.");
                return null;
            }

            if (imported == null)
            {
                Debug.LogWarning($"[WorldConfigRestorer] Cached object '{cachedObjectId}' import failed: {importError ?? "Unknown error"}. Restoring placeholder.");
                return null;
            }

            WrapRestoredCachedObject(imported, savedObj.display_name ?? record.canonical_name);
            return imported;
        }

        void WrapRestoredCachedObject(GameObject imported, string canonicalName)
        {
            if (imported == null)
                return;

            objectPlacement ??= FindFirstObjectByType<ObjectPlacementController>(FindObjectsInactive.Include);
            if (objectPlacement == null)
                return;

            Vector3 position = imported.transform.position;
            Quaternion rotation = imported.transform.rotation;
            Vector3 scale = imported.transform.localScale;

            objectPlacement.WrapExistingGeometry(imported, canonicalName);
            imported.transform.SetPositionAndRotation(position, rotation);
            imported.transform.localScale = scale;
        }

        void ResolveCachedObjectDependencies()
        {
            if (cachedObjectStore == null)
                cachedObjectStore = FindFirstObjectByType<CachedObjectStore>(FindObjectsInactive.Include);
            if (cachedObjectStore == null)
                cachedObjectStore = CachedObjectStore.GetOrCreate();

            if (objectGenerationService == null)
                objectGenerationService = FindFirstObjectByType<ObjectGenerationService>(FindObjectsInactive.Include);
            if (objectGenerationService == null)
                objectGenerationService = ObjectGenerationService.GetOrCreate();

            if (objectGenerationService != null)
            {
                if (objectGenerationService.cachedObjectStore == null)
                    objectGenerationService.cachedObjectStore = cachedObjectStore;
                if (objectGenerationService.defaultParent == null)
                    objectGenerationService.defaultParent = placedObjectsParent;
            }
        }

        public static string TryGetCachedObjectId(SavedObject savedObj)
        {
            if (savedObj?.components == null)
                return null;

            foreach (SavedComponent component in savedObj.components)
            {
                if (!string.Equals(component?.type, "CachedObjectReference", StringComparison.Ordinal))
                    continue;

                JObject data = component.data;
                string cachedObjectId = data?["cached_object_id"]?.Value<string>();
                return string.IsNullOrWhiteSpace(cachedObjectId) ? null : cachedObjectId;
            }

            return null;
        }

        public static bool TryLoadCachedObjectRecord(
            CachedObjectStore store,
            string cachedObjectId,
            out CachedObjectRecord record,
            out string error)
        {
            record = null;
            error = null;

            if (store == null)
            {
                error = "Cached object store is missing.";
                return false;
            }

            try
            {
                record = store.Load(cachedObjectId);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }

            if (record != null)
                return true;

            error = "Cached object metadata was not found.";
            return false;
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
                    AudioPlaybackController playback = holder.GetComponent<AudioPlaybackController>();
                    if (playback != null)
                        playback.Restart(src.loop);
                    else if (src.loop)
                        src.Play();
                }
            }
        }

        string ResolvePath(string configId, string relativePath) =>
            ResolveCachedWorldAssetPath(worldConfigStore?.WorldsRootPath, configId, relativePath);

        public static string ResolveCachedWorldAssetPath(string worldsRootPath, string configId, string relativePath)
        {
            if (string.IsNullOrWhiteSpace(worldsRootPath) ||
                string.IsNullOrWhiteSpace(configId) ||
                string.IsNullOrWhiteSpace(relativePath) ||
                Path.IsPathRooted(relativePath))
            {
                return null;
            }

            string worldsRoot = Path.GetFullPath(worldsRootPath);
            string configFolder = Path.GetFullPath(Path.Combine(worldsRoot, configId));
            string candidate = Path.GetFullPath(Path.Combine(configFolder, relativePath));
            return IsPathUnder(worldsRoot, candidate) ? candidate : null;
        }

        static bool IsPathUnder(string rootPath, string candidatePath)
        {
            string root = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string candidate = Path.GetFullPath(candidatePath);
            return candidate.StartsWith(root, StringComparison.Ordinal);
        }
    }
}
