// SPDX-License-Identifier: MIT

using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using GaussianSplatting.Runtime;
using UnityEngine;
using UnityEngine.Events;
using WorldLabs.API;

namespace WorldLabs.Runtime
{
    /// <summary>
    /// Runtime orchestrator for browsing and loading WorldLabs Gaussian Splat worlds.
    /// Handles API queries, SPZ download, GPU processing, and GaussianSplatRenderer spawning.
    /// Safe to use in builds — no Editor/AssetDatabase dependencies.
    /// </summary>
    public class WorldLabsWorldManager : MonoBehaviour
    {
        // ── Quality preset ────────────────────────────────────────────────────

        public enum SplatQuality
        {
            VeryHigh,   // Float32 pos/scale/color/SH   — maximum fidelity, highest VRAM
            High,       // Norm11 pos/scale, Float16x4 color, Float16 SH
            Medium,     // Norm11 pos/scale, Norm8x4 color, Norm6 SH   (default)
            Low,        // Norm6  pos/scale, Norm8x4 color, Cluster64k SH
            VeryLow,    // Norm6  pos/scale, BC7 color, Cluster4k SH   — lowest VRAM
        }

        public enum SplatResolution
        {
            FullRes,   // "full_res" — maximum detail
            _500k,     // "500k"    — balanced (default)
            _100k,     // "100k"    — lightest, fastest download
        }

        // ── Inspector ─────────────────────────────────────────────────────────

        // Shaders are auto-assigned at reset/awake — no manual drag-drop needed.
        [HideInInspector] public Shader splatShader;
        [HideInInspector] public Shader compositeShader;
        [HideInInspector] public Shader debugPointsShader;
        [HideInInspector] public Shader debugBoxesShader;
        [HideInInspector] public ComputeShader splatUtilitiesDeviceRadix;
        [HideInInspector] public ComputeShader splatUtilitiesFidelityFX;

        [Header("Loading")]
        public SplatQuality quality = SplatQuality.Medium;
        [Tooltip("SPZ resolution to request. Falls back to best available if the chosen resolution is absent.")]
        public SplatResolution preferredResolution = SplatResolution._500k;
        [Tooltip("Parent transform for spawned world GameObjects. Uses this transform if null.")]
        public Transform worldParent;
        [Tooltip("Destroy any loaded splat worlds before starting a new load.")]
        public bool clearLoadedWorldsBeforeLoad = true;
        [Tooltip("Run Resources.UnloadUnusedAssets after clearing the previous world and before loading the next one.")]
        public bool unloadUnusedAssetsBeforeLoad = true;
        [Tooltip("Reset GaussianSplatRenderSystem after unloading worlds so stale command buffers, active splat lists, and render targets do not survive into the next load.")]
        public bool resetSplatRenderSystemBeforeLoad = true;
        [Min(0), Tooltip("Frames to yield after destroying previous world objects before continuing a new load.")]
        public int loadCleanupFrameDelay = 2;

        [Header("Default Asset")]
        [Tooltip("Optional GaussianSplatAsset to display immediately on Start, without an API call.")]
        public GaussianSplatAsset defaultAsset;
        [Tooltip("Apply a -180° X rotation to the default asset. WorldLabs worlds typically require this.")]
        public bool defaultAssetInverted = true;

        // API key is always read from the .env file (via EnvLoader / StreamingAssets in builds).

        // ── UnityEvents (Inspector-wirable) ───────────────────────────────────

        [Serializable] public class WorldsListedEvent : UnityEvent<List<World>> { }
        [Serializable] public class WorldLoadedEvent : UnityEvent<string, GaussianSplatRenderer> { }
        [Serializable] public class WorldLoadFailedEvent : UnityEvent<string, string> { }
        [Serializable] public class WorldProgressEvent : UnityEvent<string, float> { }

        public WorldsListedEvent onWorldsListed;
        public WorldLoadedEvent  onWorldLoaded;
        public WorldLoadFailedEvent onWorldLoadFailed;
        public WorldProgressEvent onWorldLoadProgress;

        // ── C# events (code-only subscribers) ────────────────────────────────

        public event Action<List<World>>                 OnWorldsListed;
        public event Action<string>                      OnWorldLoadStarted;
        public event Action<string, float>               OnWorldLoadProgress;
        public event Action<string, GaussianSplatRenderer> OnWorldLoaded;
        public event Action<string, string>              OnWorldLoadFailed;
        public event Action<string>                      OnWorldUnloaded;
        /// <summary>
        /// Fired with the raw compressed SPZ bytes immediately after download, before
        /// decompression/processing. Subscribe to cache the file to disk.
        /// </summary>
        public event Action<string, byte[]>              OnSplatBytesDownloaded;

        // ── Internal state ────────────────────────────────────────────────────

        WorldLabsClient _client;
        WorldLabsClient Client => _client ??= new WorldLabsClient();

        readonly Dictionary<string, GaussianSplatRenderer> _loadedWorlds = new();
        readonly HashSet<string> _loadingWorlds = new();
        List<World> _cachedWorlds = new();
        int _cleanupVersion;

        // ── Properties ────────────────────────────────────────────────────────

        public IReadOnlyList<World> CachedWorlds      => _cachedWorlds;
        public bool IsApiConfigured                   => Client.IsConfigured;
        public bool IsWorldLoaded(string worldId)     => _loadedWorlds.ContainsKey(worldId);
        public bool IsWorldLoading(string worldId)    => _loadingWorlds.Contains(worldId);
        public IReadOnlyCollection<string> LoadedWorldIds => _loadedWorlds.Keys;
        /// <summary>The next-page token from the most recent <see cref="ListWorldsAsync"/> call. Null when no more pages.</summary>
        public string LastNextPageToken { get; private set; }
        /// <summary>The most recently started world load. Set at the start of LoadWorldAsync and RegisterExternalWorld.</summary>
        public World LastLoadedWorld { get; private set; }

        // ── Lifecycle ─────────────────────────────────────────────────────────

        void Awake()
        {
            if (worldParent == null)
                worldParent = transform;
            EnsureShaders();
        }

        void Start()
        {
            if (defaultAsset != null)
                LoadDefaultAsset();
        }

        void OnDestroy()
        {
            UnloadAllWorlds();
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Fetch a page of worlds from the API.
        /// Fires <see cref="OnWorldsListed"/> / <see cref="onWorldsListed"/> on success.
        /// </summary>
        public async Task<List<World>> ListWorldsAsync(
            string pageToken = null,
            int pageSize = 20,
            WorldStatus? status = WorldStatus.SUCCEEDED,
            bool? isPublic = null)
        {
            if (!CanCallApi(nameof(ListWorldsAsync)))
                return _cachedWorlds;

            try
            {
                var response = await Client.ListWorldsAsync(
                    pageSize:   pageSize,
                    pageToken:  pageToken,
                    status:     status,
                    isPublic:   isPublic);

                _cachedWorlds = response.worlds ?? new List<World>();
                LastNextPageToken = response.next_page_token;
                OnWorldsListed?.Invoke(_cachedWorlds);
                onWorldsListed?.Invoke(_cachedWorlds);
                return _cachedWorlds;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[WorldLabsWorldManager] ListWorldsAsync failed: {ex.Message}");
                return _cachedWorlds;
            }
        }

        /// <summary>
        /// Fetch a single world by ID from the API.
        /// Returns null on failure.
        /// </summary>
        public async Task<World> GetWorldAsync(string worldId)
        {
            if (!CanCallApi(nameof(GetWorldAsync)))
                return null;

            try
            {
                return await Client.GetWorldAsync(worldId);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[WorldLabsWorldManager] GetWorldAsync failed for '{worldId}': {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Download, process, and render a world at runtime.
        /// Returns the spawned <see cref="GaussianSplatRenderer"/>, or null on failure.
        /// Already-loaded worlds are returned immediately without re-downloading.
        /// </summary>
        public async Task<GaussianSplatRenderer> LoadWorldAsync(World world)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            LastLoadedWorld = world;
            string worldId = world.world_id;

            // Return existing renderer immediately when it is the only live world.
            if (_loadedWorlds.TryGetValue(worldId, out var existing) && _loadedWorlds.Count == 1)
                return existing;

            if (_loadingWorlds.Contains(worldId))
            {
                Debug.LogWarning($"[WorldLabsWorldManager] World {worldId} is already loading.");
                return null;
            }

            _loadingWorlds.Add(worldId);
            OnWorldLoadStarted?.Invoke(worldId);

            try
            {
                await PrepareForWorldLoadAsync();

                // ── 1. Resolve SPZ URL ────────────────────────────────────────
                string resKey = preferredResolution switch
                {
                    SplatResolution.FullRes => "full_res",
                    SplatResolution._100k   => "100k",
                    _                       => "500k",
                };
                string spzUrl = world.assets?.splats?.GetUrl(resKey)
                             ?? world.assets?.splats?.GetBestResolutionUrl();
                if (string.IsNullOrEmpty(spzUrl))
                    throw new Exception("No SPZ URL found in world assets.");
                Debug.Log($"[WorldLabsWorldManager] LoadWorldAsync: SPZ URL resolved for '{worldId}': {spzUrl}");

                // ── 2. Download ───────────────────────────────────────────────
                ReportProgress(worldId, 0.05f);
                Debug.Log($"[WorldLabsWorldManager] LoadWorldAsync: download starting for '{worldId}'…");
                byte[] spzBytes = await WorldLabsClientExtensions.DownloadBinaryAsync(spzUrl);
                Debug.Log($"[WorldLabsWorldManager] LoadWorldAsync: download complete for '{worldId}', {spzBytes?.Length ?? 0} bytes");
                OnSplatBytesDownloaded?.Invoke(worldId, spzBytes);
                ReportProgress(worldId, 0.35f);

                // ── 3. Process on a background thread ─────────────────────────
                var (posFormat, scaleFormat, colorFormat, shFormat) = GetFormats(quality);
                RuntimeSplatData data = null;

                Debug.Log($"[WorldLabsWorldManager] LoadWorldAsync: background processing starting for '{worldId}'…");
                await Task.Run(() =>
                {
                    data = RuntimeSplatProcessing.ProcessSPZBytes(
                        spzBytes,
                        posFormat, scaleFormat, colorFormat, shFormat);
                });
                Debug.Log($"[WorldLabsWorldManager] LoadWorldAsync: background processing complete for '{worldId}'");

                data.worldId    = worldId;
                data.worldName  = world.display_name;
                data.thumbnailUrl = world.assets?.thumbnail_url;
                ReportProgress(worldId, 0.90f);

                // ── 4. Spawn renderer on the main thread ──────────────────────
                await Task.Yield();
                Debug.Log($"[WorldLabsWorldManager] LoadWorldAsync: creating renderer GameObject for '{worldId}'");
                var go = new GameObject($"World_{world.display_name ?? worldId}");
                go.transform.SetParent(worldParent, false);
                go.transform.localRotation = Quaternion.Euler(-180f, 0f, 0f);

                var renderer = go.AddComponent<GaussianSplatRenderer>();
                AssignShaders(renderer);
                renderer.LoadFromRuntimeData(data);
                await Task.Yield();

                _loadedWorlds[worldId] = renderer;

                ReportProgress(worldId, 1.0f);

                Debug.Log($"[WorldLabsWorldManager] LoadWorldAsync: firing OnWorldLoaded for '{worldId}'");
                OnWorldLoaded?.Invoke(worldId, renderer);
                onWorldLoaded?.Invoke(worldId, renderer);
                return renderer;
            }
            catch (Exception ex)
            {
                string msg = ex.Message;
                Debug.LogError($"[WorldLabsWorldManager] LoadWorldAsync failed for '{worldId}': {msg}");
                OnWorldLoadFailed?.Invoke(worldId, msg);
                onWorldLoadFailed?.Invoke(worldId, msg);
                return null;
            }
            finally
            {
                _loadingWorlds.Remove(worldId);
            }
        }

        /// <summary>Destroy the renderer and free the slot for a world.</summary>
        public void UnloadWorld(string worldId)
        {
            if (!_loadedWorlds.TryGetValue(worldId, out var renderer)) return;
            _loadedWorlds.Remove(worldId);
            if (renderer != null)
            {
                renderer.gameObject.SetActive(false);
                Destroy(renderer.gameObject);
            }
            OnWorldUnloaded?.Invoke(worldId);
        }

        /// <summary>Show or hide all loaded world renderer GameObjects without unloading them.</summary>
        public void SetAllWorldsActive(bool active)
        {
            foreach (var renderer in _loadedWorlds.Values)
                if (renderer != null)
                    renderer.gameObject.SetActive(active);
        }

        /// <summary>Destroy all loaded worlds.</summary>
        public void UnloadAllWorlds()
        {
            foreach (var id in new List<string>(_loadedWorlds.Keys))
                UnloadWorld(id);
        }

        /// <summary>
        /// Clears all loaded splat renderers and gives Unity a chance to destroy objects and release unused assets.
        /// Use this before starting a new load so old splats cannot linger alongside the incoming world.
        /// </summary>
        public async Task PrepareForWorldLoadAsync()
        {
            if (!clearLoadedWorldsBeforeLoad)
                return;

            _cleanupVersion++;
            UnloadAllWorlds();
            ResetSplatRenderSystemForWorldSwitch();
            for (int i = 0; i < loadCleanupFrameDelay; i++)
                await Task.Yield();
            ResetSplatRenderSystemForWorldSwitch();

            if (unloadUnusedAssetsBeforeLoad)
                await UnloadUnusedAssetsAsync();
        }

        public IEnumerator PrepareForWorldLoadCoroutine()
        {
            if (!clearLoadedWorldsBeforeLoad)
                yield break;

            _cleanupVersion++;
            UnloadAllWorlds();
            ResetSplatRenderSystemForWorldSwitch();
            for (int i = 0; i < loadCleanupFrameDelay; i++)
                yield return null;
            ResetSplatRenderSystemForWorldSwitch();

            if (unloadUnusedAssetsBeforeLoad)
                yield return Resources.UnloadUnusedAssets();
        }

        /// <summary>
        /// Unloads all real worlds and brings back the default asset placeholder.
        /// Call this from an "Unload" button so the user returns to the default view.
        /// </summary>
        public void RestoreDefaultWorld()
        {
            int version = ++_cleanupVersion;
            foreach (var id in new List<string>(_loadedWorlds.Keys))
                if (id != "__default__")
                    UnloadWorld(id);

            StartCoroutine(RestoreDefaultWorldAfterCleanup(version));
        }

        IEnumerator RestoreDefaultWorldAfterCleanup(int version)
        {
            for (int i = 0; i < loadCleanupFrameDelay; i++)
                yield return null;

            ResetSplatRenderSystemForWorldSwitch();

            if (unloadUnusedAssetsBeforeLoad)
                yield return Resources.UnloadUnusedAssets();

            if (version == _cleanupVersion &&
                _loadingWorlds.Count == 0 &&
                defaultAsset != null &&
                !_loadedWorlds.ContainsKey("__default__"))
            {
                LoadDefaultAsset();
            }
        }

        /// <summary>
        /// Adds <paramref name="worldId"/> to the loading set and fires <see cref="OnWorldLoadStarted"/>.
        /// Call this before downloading SPZ bytes when bypassing <see cref="LoadWorldAsync"/>.
        /// </summary>
        public void NotifyWorldLoadStarted(string worldId)
        {
            if (string.IsNullOrEmpty(worldId))
                throw new ArgumentException("worldId must not be null or empty.", nameof(worldId));

            _loadingWorlds.Add(worldId);
            OnWorldLoadStarted?.Invoke(worldId);
        }

        /// <summary>
        /// Registers an externally-created renderer, dismisses the default placeholder,
        /// and fires <see cref="OnWorldLoaded"/> / <see cref="onWorldLoaded"/>.
        /// Call this after <see cref="RuntimeSplatFloorLoader.LoadPlacedRuntimeWorld"/> succeeds.
        /// </summary>
        public void RegisterExternalWorld(string worldId, GaussianSplatRenderer renderer, World world = null)
        {
            if (string.IsNullOrEmpty(worldId))
                throw new ArgumentException("worldId must not be null or empty.", nameof(worldId));
            if (renderer == null)
                throw new ArgumentNullException(nameof(renderer));
            if (world != null) LastLoadedWorld = world;

            UnloadAllWorlds();
            _loadedWorlds[worldId] = renderer;
            _loadingWorlds.Remove(worldId);

            OnWorldLoaded?.Invoke(worldId, renderer);
            onWorldLoaded?.Invoke(worldId, renderer);
        }

        /// <summary>
        /// Removes <paramref name="worldId"/> from the loading set and fires
        /// <see cref="OnWorldLoadFailed"/> / <see cref="onWorldLoadFailed"/>.
        /// Call this if SPZ download, floor analysis, or renderer creation fails.
        /// </summary>
        public void NotifyWorldLoadFailed(string worldId, string error)
        {
            if (string.IsNullOrEmpty(worldId))
                throw new ArgumentException("worldId must not be null or empty.", nameof(worldId));

            _loadingWorlds.Remove(worldId);

            OnWorldLoadFailed?.Invoke(worldId, error ?? string.Empty);
            onWorldLoadFailed?.Invoke(worldId, error ?? string.Empty);
        }

        // ── Private helpers ───────────────────────────────────────────────────

        void LoadDefaultAsset()
        {
            const string id = "__default__";

            var go = new GameObject($"World_{defaultAsset.name}");
            go.transform.SetParent(worldParent, false);
            go.transform.localRotation = defaultAssetInverted ? Quaternion.Euler(-180f, 0f, 0f) : Quaternion.identity;

            var renderer = go.AddComponent<GaussianSplatRenderer>();
            AssignShaders(renderer);
            renderer.m_Asset = defaultAsset;

            _loadedWorlds[id] = renderer;

            OnWorldLoaded?.Invoke(id, renderer);
            onWorldLoaded?.Invoke(id, renderer);
        }

        void ReportProgress(string worldId, float progress)
        {
            OnWorldLoadProgress?.Invoke(worldId, progress);
            onWorldLoadProgress?.Invoke(worldId, progress);
        }

        bool CanCallApi(string operation)
        {
            if (Client.IsConfigured)
                return true;

            Debug.LogWarning($"[WorldLabsWorldManager] {operation} skipped: WorldLabs API key missing. Set WORLDLABS_API_KEY in the project-root .env file before using WorldLabs API features.", this);
            return false;
        }

        static async Task UnloadUnusedAssetsAsync()
        {
            AsyncOperation op = Resources.UnloadUnusedAssets();
            while (op != null && !op.isDone)
                await Task.Yield();
            GC.Collect();
        }

        void ResetSplatRenderSystemForWorldSwitch()
        {
            if (!resetSplatRenderSystemBeforeLoad)
                return;

            GaussianSplatRenderer.ResetRenderSystemForWorldSwitch();
        }

        void AssignShaders(GaussianSplatRenderer r)
        {
            r.m_ShaderSplats                     = splatShader;
            r.m_ShaderComposite                  = compositeShader;
            r.m_ShaderDebugPoints                = debugPointsShader;
            r.m_ShaderDebugBoxes                 = debugBoxesShader;
            r.m_CSSplatUtilities_deviceRadixSort = splatUtilitiesDeviceRadix;
            r.m_CSSplatUtilities_fidelityFX      = splatUtilitiesFidelityFX;
        }

        /// <summary>
        /// Fills any missing shader/compute references using Shader.Find() (runtime-safe).
        /// Compute shaders cannot be found at runtime — they must be serialized (set via
        /// Reset() in the Editor or manually assigned in the Inspector).
        /// </summary>
        void EnsureShaders()
        {
            if (splatShader == null)
                splatShader = Shader.Find("Gaussian Splatting/Render Splats");
            if (compositeShader == null)
                compositeShader = Shader.Find("Hidden/Gaussian Splatting/Composite");
            if (debugPointsShader == null)
                debugPointsShader = Shader.Find("Gaussian Splatting/Debug/Render Points");
            if (debugBoxesShader == null)
                debugBoxesShader = Shader.Find("Gaussian Splatting/Debug/Render Boxes");
        }

#if UNITY_EDITOR
        /// <summary>
        /// Auto-assigns all shader and compute shader fields from the package path.
        /// Called automatically when the component is first added or Reset in the Editor.
        /// </summary>
        void Reset()
        {
            const string root = "Packages/com.worldlabs.gaussian-splatting/Shaders/";
            splatShader               = UnityEditor.AssetDatabase.LoadAssetAtPath<Shader>(root + "RenderGaussianSplats.shader");
            compositeShader           = UnityEditor.AssetDatabase.LoadAssetAtPath<Shader>(root + "GaussianComposite.shader");
            debugPointsShader         = UnityEditor.AssetDatabase.LoadAssetAtPath<Shader>(root + "GaussianDebugRenderPoints.shader");
            debugBoxesShader          = UnityEditor.AssetDatabase.LoadAssetAtPath<Shader>(root + "GaussianDebugRenderBoxes.shader");
            splatUtilitiesDeviceRadix = UnityEditor.AssetDatabase.LoadAssetAtPath<ComputeShader>(root + "SplatUtilities_DeviceRadixSort.compute");
            splatUtilitiesFidelityFX  = UnityEditor.AssetDatabase.LoadAssetAtPath<ComputeShader>(root + "SplatUtilities_FidelityFX.compute");
        }
#endif

        static (GaussianSplatAsset.VectorFormat pos,
                GaussianSplatAsset.VectorFormat scale,
                GaussianSplatAsset.ColorFormat  color,
                GaussianSplatAsset.SHFormat     sh)
            GetFormats(SplatQuality q) => q switch
        {
            SplatQuality.VeryHigh => (GaussianSplatAsset.VectorFormat.Float32,
                                      GaussianSplatAsset.VectorFormat.Float32,
                                      GaussianSplatAsset.ColorFormat.Float32x4,
                                      GaussianSplatAsset.SHFormat.Float32),

            SplatQuality.High     => (GaussianSplatAsset.VectorFormat.Norm11,
                                      GaussianSplatAsset.VectorFormat.Norm11,
                                      GaussianSplatAsset.ColorFormat.Float16x4,
                                      GaussianSplatAsset.SHFormat.Float16),

            SplatQuality.Low      => (GaussianSplatAsset.VectorFormat.Norm6,
                                      GaussianSplatAsset.VectorFormat.Norm6,
                                      GaussianSplatAsset.ColorFormat.Norm8x4,
                                      GaussianSplatAsset.SHFormat.Cluster64k),

            SplatQuality.VeryLow  => (GaussianSplatAsset.VectorFormat.Norm6,
                                      GaussianSplatAsset.VectorFormat.Norm6,
                                      GaussianSplatAsset.ColorFormat.BC7,
                                      GaussianSplatAsset.SHFormat.Cluster4k),

            _ /* Medium */        => (GaussianSplatAsset.VectorFormat.Norm11,
                                      GaussianSplatAsset.VectorFormat.Norm11,
                                      GaussianSplatAsset.ColorFormat.Norm8x4,
                                      GaussianSplatAsset.SHFormat.Norm6),
        };
    }
}
