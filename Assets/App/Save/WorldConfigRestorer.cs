// Assets/App/Save/Runtime/WorldConfigRestorer.cs
using System;
using System.Collections;
using System.IO;
using System.Threading.Tasks;
using GaussianSplatting.Runtime;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Networking;
using WorldLabs.API;
using WorldLabs.Runtime;
using SpeechIntent;

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

            // 1b. Preload cached pano into skybox (without displaying it) so "pano" voice command works.
            _ = PreloadPanoAsync(config);

            // 2. Restore objects
            string configFolderPath = Path.Combine(worldConfigStore.WorldsRootPath, config.config_id);
            var ctx = new RestorationContext { ConfigFolderPath = configFolderPath, Config = config };

            foreach (SavedObject savedObj in config.objects)
            {
                try   { RestoreObject(savedObj, ctx); }
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
                if (File.Exists(absPath))
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
                if (File.Exists(absPath))
                {
                    bool loaded = await WaitForWorldLoadedAsync(() => splatLoader?.LoadAsync(absPath));
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
                if (File.Exists(absPath))
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

            SpeechIntentTrackable trackable = go.GetComponent<SpeechIntentTrackable>()
                                           ?? go.AddComponent<SpeechIntentTrackable>();
            trackable.canonicalName    = savedObj.display_name ?? savedObj.prefab_name ?? go.name;
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
                    AudioPlaybackController playback = holder.GetComponent<AudioPlaybackController>();
                    if (playback != null)
                        playback.Restart(src.loop);
                    else if (src.loop)
                        src.Play();
                }
            }
        }

        string ResolvePath(string configId, string relativePath) =>
            Path.GetFullPath(Path.Combine(worldConfigStore.WorldsRootPath, configId, relativePath));
    }
}
