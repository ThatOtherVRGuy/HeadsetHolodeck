// Assets/App/Shared/Save/Runtime/WorldConfigRestorer.cs
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
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

        WorldTransitionOperation _operation;
        long _requestVersion;

        void OnDisable() => CancelRestore();
        void OnDestroy() => CancelRestore();

        public void CancelRestore()
        {
            ++_requestVersion;
            CancelRestoreOperation();
        }

        void CancelRestoreOperation()
        {
            WorldTransitionOperation operation = _operation;
            _operation = null;
            if (operation == null) return;
            if (worldManager != null) worldManager.CancelTransition(operation);
            else operation.Cancel();
            worldConfigAutoSave?.CancelRestore(operation);
        }

        public async Task RestoreAsync(WorldConfig config)
        {
            if (config == null)
            {
                onRestoreError?.Invoke("No config provided.");
                return;
            }
            if (!isActiveAndEnabled) return;

            long requestVersion = ++_requestVersion;
            WorldTransitionOperation operation = null;
            var restoredObjects = new List<GameObject>();
            try
            {
                CancelRestoreOperation();
                // A cancellation listener may have started a newer restore synchronously.
                if (requestVersion != _requestVersion) return;
                operation = worldManager != null
                    ? worldManager.BeginTransition()
                    : WorldTransitionOperation.CreateStandalone();
                _operation = operation;
                worldConfigAutoSave?.BeginRestore(operation, config);
                onRestoreStarted?.Invoke();
                operation.ThrowIfStale();
                Debug.Log($"[WorldConfigRestorer] Restoring '{config.display_name}'");

                bool loaded = await AwaitCurrentAsync(LoadWorldForRestoreAsync(config, operation), operation,
                    RestoreTimeoutSeconds);
                operation.ThrowIfStale();
                if (!loaded) throw new InvalidOperationException($"Could not load world for '{config.display_name}'.");
                worldConfigAutoSave?.CommitRestore(operation);

                var ctx = new RestorationContext
                {
                    ConfigFolderPath = Path.Combine(worldConfigStore != null ? worldConfigStore.WorldsRootPath : "", config.config_id),
                    Config = config
                };
                if (config.objects != null)
                {
                    foreach (SavedObject savedObj in config.objects)
                    {
                        operation.ThrowIfStale();
                        if (savedObj == null) continue;
                        try
                        {
                            GameObject restored = await RestoreObjectAsync(savedObj, ctx, operation);
                            if (restored != null) restoredObjects.Add(restored);
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception ex)
                        {
                            Debug.LogWarning($"[WorldConfigRestorer] Object '{savedObj.instance_id}' restore failed: {ex.Message}");
                        }
                        await Task.Yield();
                    }
                }

                operation.ThrowIfStale();
                if (restoredObjects.Count > 0)
                    StartCoroutine(LoadAudioClipsCoroutine(restoredObjects, operation));
                if (!string.IsNullOrEmpty(config.lighting?.preset) && lightRig != null)
                    lightRig.ApplyPreset(config.lighting.preset);
                operation.ThrowIfStale();
                worldManager?.CompleteTransition(operation);
                worldConfigAutoSave?.CompleteRestore(operation);
                // Ancillary work shares this world's lifetime; a new load cancels it as well.
                _ = PreloadPanoAsync(config, operation);
                onRestoreComplete?.Invoke();
                if (operation.IsCurrent) Debug.Log($"[WorldConfigRestorer] Restore complete for '{config.display_name}'");
            }
            catch (OperationCanceledException)
            {
                // A cancelled restore used to vanish without a trace, which made a stale operation look
                // like a hang. Say so, at warning level, only while this request is still the live one.
                if (requestVersion == _requestVersion)
                    Debug.LogWarning($"[WorldConfigRestorer] Restore of '{config.display_name}' was cancelled.");
                if (operation != null && operation.IsCurrent)
                {
                    if (worldManager != null) worldManager.CancelTransition(operation);
                    else operation.Cancel();
                }
                if (operation != null) worldConfigAutoSave?.CancelRestore(operation);
            }
            catch (Exception ex)
            {
                bool report = operation == null || operation.IsCurrent;
                if (worldManager != null) worldManager.CancelTransition(operation);
                else operation?.Cancel();
                if (operation != null) worldConfigAutoSave?.CancelRestore(operation);
                if (report && requestVersion == _requestVersion && this != null && isActiveAndEnabled &&
                    (operation == null || worldManager == null || ReferenceEquals(worldManager.CurrentTransition, operation)))
                {
                    Debug.LogWarning($"[WorldConfigRestorer] Restore failed: {ex.Message}");
                    onRestoreError?.Invoke(ex.Message);
                }
            }
        }

        async Task PreloadPanoAsync(WorldConfig config, WorldTransitionOperation operation)
        {
            try
            {
                if (panoLoader == null || config.world_source == null) return;
                operation.ThrowIfStale();
                WorldSourceData src = config.world_source;
                if (!string.IsNullOrEmpty(src.cached_pano))
                {
                    string path = ResolvePath(config.config_id, src.cached_pano);
                    if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    {
                        await panoLoader.PreloadAndWaitAsync(path, operation);
                        return;
                    }
                }
                if (src.type == "worldlabs" && !string.IsNullOrEmpty(src.world_id) && worldManager != null)
                {
                    World world = await AwaitCurrentAsync(worldManager.GetWorldAsync(src.world_id, operation.Token), operation, RestoreTimeoutSeconds);
                    operation.ThrowIfStale();
                    string url = world?.assets?.imagery?.pano_url;
                    if (!string.IsNullOrEmpty(url)) await panoLoader.PreloadAndWaitAsync(url, operation);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (operation.IsCurrent) Debug.LogWarning($"[WorldConfigRestorer] Panorama preload failed: {ex.Message}");
            }
        }

        // Virtual acquisition seam lets isolated transition fixtures hold a request at download
        // or placement without invoking a provider or touching a user's saved worlds.
        protected virtual async Task<bool> LoadWorldForRestoreAsync(WorldConfig config, WorldTransitionOperation operation)
        {
            WorldSourceData src = config.world_source;
            if (src == null) return false;
            operation.ThrowIfStale();
            Action<GameObject> prepare = root =>
            {
                operation.ThrowIfStale();
                ApplyRestoredWorldState(config, root);
            };

            // A "static" config describes the built-in holodeck room plus whatever the user added to
            // it (objects, audio, lighting). There is no splat to load, so there is nothing to acquire
            // here: the room is already present and the object/lighting restore below rebuilds the
            // contents. Unloading here would be wrong — it invalidates this restore's own transition.
            if (string.Equals(src.type, "static", StringComparison.OrdinalIgnoreCase))
                return true;

            if (!string.IsNullOrEmpty(src.cached_splat))
            {
                string path = ResolvePath(config.config_id, src.cached_splat);
                if (!string.IsNullOrEmpty(path) && File.Exists(path) && splatLoader != null)
                {
                    bool loaded = await splatLoader.LoadAndWaitAsync(path, src.display_name ?? config.display_name,
                        ResolveSplatSourceKindForWorldSource(src), FirstSavedSpawnMetadata(config), prepare, operation);
                    operation.ThrowIfStale();
                    if (loaded) return true;
                }
            }
            if (src.type == "worldlabs" && !string.IsNullOrEmpty(src.world_id) && worldManager != null)
            {
                World world = await AwaitCurrentAsync(worldManager.GetWorldAsync(src.world_id, operation.Token), operation, RestoreTimeoutSeconds);
                operation.ThrowIfStale();
                if (world != null)
                {
                    GaussianSplatRenderer renderer = await worldManager.LoadWorldAsync(world, operation, prepare);
                    operation.ThrowIfStale();
                    if (renderer != null) return true;
                }
            }
            if (!string.IsNullOrEmpty(src.cached_pano))
            {
                string path = ResolvePath(config.config_id, src.cached_pano);
                if (!string.IsNullOrEmpty(path) && File.Exists(path) && panoLoader != null)
                {
                    bool loaded = await panoLoader.LoadAndWaitAsync(path, operation);
                    operation.ThrowIfStale();
                    if (loaded) return true;
                }
            }
            if (!string.IsNullOrEmpty(src.url) && splatLoader != null)
                return await splatLoader.LoadAndWaitAsync(src.url, src.display_name ?? config.display_name,
                    ResolveSplatSourceKindForWorldSource(src), FirstSavedSpawnMetadata(config), prepare, operation);
            return false;
        }

        static async Task<T> AwaitCurrentAsync<T>(Task<T> task, WorldTransitionOperation operation, float timeoutSeconds)
        {
            using var timeout = new CancellationTokenSource();
            Task delay = Task.Delay(TimeSpan.FromSeconds(timeoutSeconds), timeout.Token);
            Task canceled = Task.Delay(Timeout.Infinite, operation.Token);
            Task winner = await Task.WhenAny(task, delay, canceled);
            timeout.Cancel();
            if (winner != task)
            {
                _ = ObserveAbandonedTaskAsync(task);
                operation.ThrowIfStale();
                throw new TimeoutException($"World restore timed out after {timeoutSeconds:0.#} seconds.");
            }
            operation.ThrowIfStale();
            return await task;
        }

        static async Task ObserveAbandonedTaskAsync(Task task)
        {
            try { await task; }
            catch (Exception) { /* The operation owner already reported timeout/cancellation. */ }
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

        void ApplyRestoredWorldState(WorldConfig config, GameObject worldRoot)
        {
            if (worldRoot == null)
            {
                return;
            }

            if (config?.world_transform != null)
            {
                config.world_transform.ApplyTo(worldRoot.transform);
                Debug.Log($"[WorldConfigRestorer] Applied saved world transform for '{config.display_name}' before activation.");
            }

            // AttachSpawnPose may have captured the loader's provisional root transform.
            // Reproject saved spawn 1 only after the persisted world transform is in place.
            SpawnPointData firstSpawn = config?.spawn_points != null && config.spawn_points.Count > 0
                ? config.spawn_points[0]
                : null;
            if (firstSpawn == null)
                return;

            SplatSpawnPose pose = worldRoot.GetComponent<SplatSpawnPose>();
            if (pose == null)
                pose = worldRoot.AddComponent<SplatSpawnPose>();

            pose.SetWorldPose(worldRoot.transform, firstSpawn.position, firstSpawn.rotation, firstSpawn.look_at);
            pose.confidence = firstSpawn.confidence;
            pose.method = string.IsNullOrWhiteSpace(firstSpawn.method) ? "saved_spawn_point" : firstSpawn.method;
            Debug.Log($"[WorldConfigRestorer] Prepared saved spawn point 1 for '{config.display_name}' after world transform.");
        }

        async Task<GameObject> RestoreObjectAsync(SavedObject savedObj, RestorationContext ctx, WorldTransitionOperation operation)
        {
            GameObject go = null;
            try
            {
                go = await TryRestoreCachedObjectAsync(savedObj, operation);
                operation.ThrowIfStale();
                if (go == null) go = CreatePlaceholderObject(savedObj);
                operation.Track(go);
                operation.ThrowIfStale();
                if (placedObjectsParent != null && go.transform.parent != placedObjectsParent)
                    go.transform.SetParent(placedObjectsParent, true);
                SpeechIntentTrackable trackable = go.GetComponent<SpeechIntentTrackable>() ?? go.AddComponent<SpeechIntentTrackable>();
                trackable.canonicalName = savedObj.display_name ?? savedObj.prefab_name ?? go.name;
                trackable.configInstanceId = savedObj.instance_id;
                WorldConfigComponentRegistry.RestoreAll(go, savedObj.components, ctx);
                operation.ThrowIfStale();
                interactionMemory?.RegisterCreatedObject(go);
                return go;
            }
            catch
            {
                if (go != null) Destroy(go);
                throw;
            }
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

        async Task<GameObject> TryRestoreCachedObjectAsync(SavedObject savedObj, WorldTransitionOperation operation)
        {
            operation.ThrowIfStale();
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
                    if (cancelled || !operation.IsCurrent)
                    {
                        if (go != null)
                            Destroy(go);
                        return;
                    }

                    operation.Track(go);
                    imported = go;
                    importError = error;
                    done = true;
                }, root =>
                {
                    importRoot = root;
                    if ((cancelled || !operation.IsCurrent) && importRoot != null)
                    {
                        Destroy(importRoot);
                        importRoot = null;
                    }
                    else operation.Track(root);
                }));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[WorldConfigRestorer] Cached object '{cachedObjectId}' import could not start: {ex.Message}. Restoring placeholder.");
                return null;
            }

            float startTime = Time.realtimeSinceStartup;
            while (!done && operation.IsCurrent && (Time.realtimeSinceStartup - startTime) < CachedObjectImportTimeoutSeconds)
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

                operation.ThrowIfStale();
                Debug.LogWarning($"[WorldConfigRestorer] Cached object '{cachedObjectId}' import timed out after {CachedObjectImportTimeoutSeconds:0.#} seconds. Restoring placeholder.");
                return null;
            }

            if (imported == null)
            {
                Debug.LogWarning($"[WorldConfigRestorer] Cached object '{cachedObjectId}' import failed: {importError ?? "Unknown error"}. Restoring placeholder.");
                return null;
            }

            operation.ThrowIfStale();
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

        IEnumerator LoadAudioClipsCoroutine(List<GameObject> restoredObjects, WorldTransitionOperation operation)
        {
            foreach (GameObject restored in restoredObjects)
            {
                if (!operation.IsCurrent) yield break;
                if (restored == null) continue;
                foreach (AudioClipPathHolder holder in restored.GetComponentsInChildren<AudioClipPathHolder>(true))
                {
                    if (!operation.IsCurrent) yield break;
                    if (holder == null || string.IsNullOrEmpty(holder.absolutePath) || !File.Exists(holder.absolutePath)) continue;
                    using UnityWebRequest req = UnityWebRequestMultimedia.GetAudioClip("file://" + holder.absolutePath, AudioType.UNKNOWN);
                    UnityWebRequestAsyncOperation request = req.SendWebRequest();
                    while (!request.isDone && operation.IsCurrent) yield return null;
                    if (!operation.IsCurrent) { req.Abort(); yield break; }
                    if (req.result != UnityWebRequest.Result.Success) continue;
                    AudioClip clip = DownloadHandlerAudioClip.GetContent(req);
                    operation.Track(clip);
                    if (!operation.IsCurrent || holder == null) yield break;
                    AudioSource source = holder.GetComponent<AudioSource>();
                    if (source == null) continue;
                    source.clip = clip;
                    AudioPlaybackController playback = holder.GetComponent<AudioPlaybackController>();
                    if (playback != null) playback.Restart(source.loop);
                    else if (source.loop) source.Play();
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
