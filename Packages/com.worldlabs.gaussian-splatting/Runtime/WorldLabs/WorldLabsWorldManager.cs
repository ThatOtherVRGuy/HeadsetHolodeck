// SPDX-License-Identifier: MIT

using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
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
        /// <summary>
        /// Fired after the incoming renderer GameObject is created but before its runtime
        /// data is loaded or the public world-loaded events are dispatched. Subscribers can
        /// apply saved transforms and other activation-critical state without a raw frame.
        /// </summary>
        public event Action<string, GaussianSplatRenderer> OnWorldBeforeActivated;
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
        readonly Dictionary<string, WorldTransitionOperation> _loadingWorlds = new();
        public WorldTransitionOperation CurrentTransition { get; private set; }
        public event Action<WorldTransitionOperation> OnTransitionStarted;
        public event Action<WorldTransitionOperation> OnTransitionCancelled;
        List<World> _cachedWorlds = new();
        int _cleanupVersion;

        // ── Properties ────────────────────────────────────────────────────────

        public IReadOnlyList<World> CachedWorlds      => _cachedWorlds;
        public bool IsApiConfigured                   => Client.IsConfigured;
        public bool IsWorldLoaded(string worldId)     => _loadedWorlds.ContainsKey(worldId);
        public bool IsWorldLoading(string worldId)    => _loadingWorlds.TryGetValue(worldId, out var owner) && owner.IsCurrent;
        public IReadOnlyCollection<string> LoadedWorldIds => _loadedWorlds.Keys;
        /// <summary>The next-page token from the most recent <see cref="ListWorldsAsync"/> call. Null when no more pages.</summary>
        public string LastNextPageToken { get; private set; }
        /// <summary>The world belonging to the current successfully registered renderer.</summary>
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

        void OnDisable()
        {
            UnloadAllWorlds();
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
        public async Task<World> GetWorldAsync(string worldId, CancellationToken cancellationToken = default)
        {
            if (!CanCallApi(nameof(GetWorldAsync)))
                return null;

            try
            {
                return await Client.GetWorldAsync(worldId, cancellationToken);
            }
            catch (OperationCanceledException) { throw; }
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
        public async Task<GaussianSplatRenderer> LoadWorldAsync(
            World world, WorldTransitionOperation operation = null,
            Action<GameObject> beforeRendererCreated = null)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            bool ownsOperation = operation == null;
            operation ??= BeginTransition();
            string worldId = world.world_id;
            GameObject root = null;
            try
            {
                operation.ThrowIfStale();
                NotifyWorldLoadStarted(worldId, operation);
                await PrepareForWorldLoadAsync(operation);
                operation.ThrowIfStale();
                string resKey = preferredResolution switch
                {
                    SplatResolution.FullRes => "full_res",
                    SplatResolution._100k => "100k",
                    _ => "500k",
                };
                string spzUrl = world.assets?.splats?.GetUrl(resKey)
                             ?? world.assets?.splats?.GetBestResolutionUrl();
                if (string.IsNullOrEmpty(spzUrl))
                    throw new Exception("No SPZ URL found in world assets.");

                ReportProgress(worldId, 0.05f);
                operation.ThrowIfStale();
                byte[] bytes = await operation.AwaitAsync(DownloadWorldBytesAsync(spzUrl, operation.Token));
                InvokeCurrent(OnSplatBytesDownloaded, worldId, bytes, operation);
                operation.ThrowIfStale();
                ReportProgress(worldId, 0.35f);
                operation.ThrowIfStale();
                RuntimeSplatData data = await operation.AwaitAsync(ProcessWorldDataAsync(bytes));
                data.worldId = worldId;
                data.worldName = world.display_name;
                data.thumbnailUrl = world.assets?.thumbnail_url;
                ReportProgress(worldId, 0.90f);
                await Task.Yield();
                operation.ThrowIfStale();

                root = new GameObject($"World_{world.display_name ?? worldId}");
                root.SetActive(false);
                operation.Track(root);
                root.transform.SetParent(worldParent, false);
                root.transform.localRotation = Quaternion.Euler(-180f, 0f, 0f);
                ApplySplatSemantics(root.transform, world);
                beforeRendererCreated?.Invoke(root);
                operation.ThrowIfStale();
                var renderer = root.AddComponent<GaussianSplatRenderer>();
                AssignShaders(renderer);
                InvokeCurrent(OnWorldBeforeActivated, worldId, renderer, operation);
                operation.ThrowIfStale();
                LoadRendererData(renderer, data);
                operation.ThrowIfStale();
                root.SetActive(true);
                await Task.Yield();
                operation.ThrowIfStale();
                if (!RegisterExternalWorld(worldId, renderer, world, operation, beforeActivationAlreadyApplied: true))
                    return null;
                if (operation.IsCurrent) ReportProgress(worldId, 1f);
                if (ownsOperation && operation.IsCurrent) CompleteTransition(operation);
                return operation.IsCurrent ? renderer : null;
            }
            catch (OperationCanceledException)
            {
                WorldTransitionOperation.DestroyOwned(root);
                return null;
            }
            catch (Exception ex)
            {
                WorldTransitionOperation.DestroyOwned(root);
                if (operation.IsCurrent)
                {
                    Debug.LogError($"[WorldLabsWorldManager] LoadWorldAsync failed for '{worldId}': {ex.Message}");
                    NotifyWorldLoadFailed(worldId, ex.Message, operation);
                }
                return null;
            }
            finally
            {
                if (_loadingWorlds.TryGetValue(worldId, out var owner) && owner == operation)
                    _loadingWorlds.Remove(worldId);
            }
        }

        // Narrow overridable boundaries let isolated fixtures delay I/O and processing without
        // contacting a provider or initializing GPU resources.
        protected virtual Task<byte[]> DownloadWorldBytesAsync(string url, CancellationToken token) =>
            WorldLabsClientExtensions.DownloadBinaryAsync(url, token);

        protected virtual Task<RuntimeSplatData> ProcessWorldDataAsync(byte[] bytes)
        {
            var (pos, scale, color, sh) = GetFormats(quality);
            return Task.Run(() => RuntimeSplatProcessing.ProcessSPZBytes(bytes, pos, scale, color, sh));
        }

        protected virtual void LoadRendererData(GaussianSplatRenderer renderer, RuntimeSplatData data) =>
            renderer.LoadFromRuntimeData(data);

        /// <summary>
        /// Applies the metric scale and ground offset Marble reports for a splat. Ignoring them renders the
        /// world at an arbitrary size and floating above or sunk into the floor — obvious in a headset and
        /// the difference between "looks like a place" and "looks like a bug".
        /// </summary>
        static void ApplySplatSemantics(Transform root, World world)
        {
            SplatSemanticsMetadata semantics = world?.assets?.splats?.semantics_metadata;
            if (root == null || semantics == null) return;

            // These are plain floats, not nullable: responses are parsed with JsonUtility, which cannot
            // deserialize Nullable<T>, so a nullable field would silently stay null for every world.
            float scale = semantics.metric_scale_factor;
            if (!float.IsFinite(scale) || scale <= 0f) scale = 1f;
            float groundOffset = semantics.ground_plane_offset;
            if (!float.IsFinite(groundOffset)) groundOffset = 0f;

            if (!Mathf.Approximately(scale, 1f))
                root.localScale = Vector3.one * scale;

            if (!Mathf.Approximately(groundOffset, 0f))
                root.localPosition += Vector3.up * (groundOffset * scale);

            Debug.Log($"[WorldLabsWorldManager] Applied splat semantics for '{world.display_name}': " +
                      $"metric_scale_factor={scale:0.####}, ground_plane_offset={groundOffset:0.####}.");
        }

        /// <summary>Newest request wins. Previous content is removed; failure leaves the static scene.</summary>
        public WorldTransitionOperation BeginTransition()
        {
            if (!isActiveAndEnabled) throw new InvalidOperationException("World manager is disabled.");
            var previous = CurrentTransition;
            bool notifyPrevious = previous != null && !previous.Token.IsCancellationRequested;
            WorldTransitionOperation next = null;
            next = new WorldTransitionOperation(() => this != null && isActiveAndEnabled && CurrentTransition == next);
            CurrentTransition = next;
            next.Token.Register(() => OnOperationCancelled(next));
            previous?.Cancel();
            // Cancellation callbacks may themselves start a newer request.
            if (CurrentTransition != next) return next;
            ClearLoadedWorlds();
            if (CurrentTransition != next) return next;
            _loadingWorlds.Clear();
            LastLoadedWorld = null;
            _cleanupVersion++;
            if (notifyPrevious) NotifyTransition(OnTransitionCancelled, previous);
            if (CurrentTransition == next) NotifyTransition(OnTransitionStarted, next, requireCurrent: true);
            return next;
        }

        public void CancelTransition(WorldTransitionOperation operation) => operation?.Cancel();

        void OnOperationCancelled(WorldTransitionOperation operation)
        {
            // Operation.Cancel is a Unity-thread API, including calls from placement owners.
            if (CurrentTransition != operation) return;
            _cleanupVersion++;
            _loadingWorlds.Clear();
            ClearLoadedWorlds();
            if (CurrentTransition != operation) return;
            LastLoadedWorld = null;
            NotifyTransition(OnTransitionCancelled, operation);
        }

        public void CompleteTransition(WorldTransitionOperation operation)
        {
            operation?.ThrowIfStale();
            if (operation != null)
            {
                operation.IsCompleted = true;
                foreach (var id in new List<string>(_loadingWorlds.Keys))
                    if (_loadingWorlds[id] == operation) _loadingWorlds.Remove(id);
            }
        }

        /// <summary>Explicit unload also invalidates pending downloads and placement.</summary>
        public void UnloadWorld(string worldId)
        {
            var operation = CurrentTransition;
            if (_loadingWorlds.ContainsKey(worldId) || _loadedWorlds.ContainsKey(worldId))
                CancelTransition(operation);
            if (CurrentTransition == operation) UnloadWorldContent(worldId);
        }

        void UnloadWorldContent(string worldId)
        {
            if (!_loadedWorlds.TryGetValue(worldId, out var renderer)) return;
            _loadedWorlds.Remove(worldId);
            if (renderer != null) WorldTransitionOperation.DestroyOwned(renderer.gameObject);
            if (OnWorldUnloaded != null)
                foreach (Action<string> listener in OnWorldUnloaded.GetInvocationList())
                {
                    try { listener(worldId); }
                    catch (Exception ex) { Debug.LogException(ex); }
                }
        }

        public void SetAllWorldsActive(bool active)
        {
            foreach (var renderer in _loadedWorlds.Values)
                if (renderer != null) renderer.gameObject.SetActive(active);
        }

        void ClearLoadedWorlds()
        {
            foreach (var pair in new List<KeyValuePair<string, GaussianSplatRenderer>>(_loadedWorlds))
                if (_loadedWorlds.TryGetValue(pair.Key, out var current) && ReferenceEquals(current, pair.Value))
                    UnloadWorldContent(pair.Key);
        }

        public void UnloadAllWorlds()
        {
            _cleanupVersion++;
            var operation = CurrentTransition;
            CancelTransition(operation);
            if (CurrentTransition != operation) return;
            ClearLoadedWorlds();
            if (CurrentTransition == operation) LastLoadedWorld = null;
        }

        public async Task PrepareForWorldLoadAsync(WorldTransitionOperation operation = null)
        {
            operation ??= CurrentTransition ?? BeginTransition();
            operation.ThrowIfStale();
            if (!clearLoadedWorldsBeforeLoad) return;
            ClearLoadedWorlds();
            operation.ThrowIfStale();
            ResetSplatRenderSystemForWorldSwitch();
            for (int i = 0; i < loadCleanupFrameDelay; i++)
            {
                await Task.Yield();
                operation.ThrowIfStale();
            }
            ResetSplatRenderSystemForWorldSwitch();
            if (unloadUnusedAssetsBeforeLoad) await UnloadUnusedAssetsAsync();
            operation.ThrowIfStale();
        }

        public IEnumerator PrepareForWorldLoadCoroutine(WorldTransitionOperation operation = null)
        {
            operation ??= CurrentTransition ?? BeginTransition();
            if (!operation.IsCurrent || !clearLoadedWorldsBeforeLoad) yield break;
            ClearLoadedWorlds();
            if (!operation.IsCurrent) yield break;
            ResetSplatRenderSystemForWorldSwitch();
            for (int i = 0; i < loadCleanupFrameDelay; i++)
            {
                yield return null;
                if (!operation.IsCurrent) yield break;
            }
            ResetSplatRenderSystemForWorldSwitch();
            if (unloadUnusedAssetsBeforeLoad) yield return Resources.UnloadUnusedAssets();
        }

        public void RestoreDefaultWorld()
        {
            var operation = CurrentTransition;
            UnloadAllWorlds();
            if (CurrentTransition != operation) return;
            int version = ++_cleanupVersion;
            if (isActiveAndEnabled) StartCoroutine(RestoreDefaultWorldAfterCleanup(version));
        }

        IEnumerator RestoreDefaultWorldAfterCleanup(int version)
        {
            for (int i = 0; i < loadCleanupFrameDelay; i++)
            {
                yield return null;
                if (version != _cleanupVersion) yield break;
            }
            if (version != _cleanupVersion) yield break;
            ResetSplatRenderSystemForWorldSwitch();
            if (unloadUnusedAssetsBeforeLoad) yield return Resources.UnloadUnusedAssets();
            if (version == _cleanupVersion && isActiveAndEnabled && defaultAsset != null && !_loadedWorlds.ContainsKey("__default__"))
                LoadDefaultAsset();
        }

        public void NotifyWorldLoadStarted(string worldId, WorldTransitionOperation operation = null)
        {
            if (string.IsNullOrEmpty(worldId)) throw new ArgumentException("worldId is required.", nameof(worldId));
            operation ??= BeginTransition();
            operation.ThrowIfStale();
            _loadingWorlds[worldId] = operation;
            if (OnWorldLoadStarted != null)
                foreach (Action<string> listener in OnWorldLoadStarted.GetInvocationList())
                {
                    operation.ThrowIfStale();
                    listener(worldId);
                }
        }

        public bool RegisterExternalWorld(string worldId, GaussianSplatRenderer renderer, World world = null,
            WorldTransitionOperation operation = null, bool beforeActivationAlreadyApplied = false)
        {
            if (string.IsNullOrEmpty(worldId)) throw new ArgumentException("worldId is required.", nameof(worldId));
            if (renderer == null) throw new ArgumentNullException(nameof(renderer));
            if (operation == null && !_loadingWorlds.TryGetValue(worldId, out operation))
            {
                // Preserve initial external registration for legacy integrations. Once a request
                // exists, an unowned late registration must never claim the current world.
                if (CurrentTransition == null) operation = BeginTransition();
            }
            if (operation == null || !operation.IsCurrent || CurrentTransition != operation)
            {
                WorldTransitionOperation.DestroyOwned(renderer.gameObject);
                return false;
            }
            try
            {
                operation.Track(renderer.gameObject);
                if (!beforeActivationAlreadyApplied) InvokeCurrent(OnWorldBeforeActivated, worldId, renderer, operation);
                operation.ThrowIfStale();
                _loadedWorlds[worldId] = renderer;
                _loadingWorlds.Remove(worldId);
                LastLoadedWorld = world;
                InvokeCurrent(OnWorldLoaded, worldId, renderer, operation);
                operation.ThrowIfStale();
                onWorldLoaded?.Invoke(worldId, renderer);
                return operation.IsCurrent;
            }
            catch (OperationCanceledException) { return false; }
            catch
            {
                if (_loadedWorlds.TryGetValue(worldId, out var current) && current == renderer)
                    UnloadWorldContent(worldId);
                else WorldTransitionOperation.DestroyOwned(renderer.gameObject);
                throw;
            }
        }

        public void NotifyWorldLoadFailed(string worldId, string error, WorldTransitionOperation operation = null)
        {
            if (string.IsNullOrEmpty(worldId)) throw new ArgumentException("worldId is required.", nameof(worldId));
            if (operation == null) _loadingWorlds.TryGetValue(worldId, out operation);
            if (operation != null && !operation.IsCurrent) return;
            _loadingWorlds.Remove(worldId);
            if (operation != null) InvokeCurrent(OnWorldLoadFailed, worldId, error ?? string.Empty, operation);
            else OnWorldLoadFailed?.Invoke(worldId, error ?? string.Empty);
            if (operation == null || operation.IsCurrent) onWorldLoadFailed?.Invoke(worldId, error ?? string.Empty);
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

        static void NotifyTransition(Action<WorldTransitionOperation> handlers, WorldTransitionOperation operation,
            bool requireCurrent = false)
        {
            if (handlers == null) return;
            foreach (Action<WorldTransitionOperation> listener in handlers.GetInvocationList())
            {
                if (requireCurrent && !operation.IsCurrent) return;
                try { listener(operation); }
                catch (Exception ex) { Debug.LogException(ex); }
            }
        }

        static void InvokeCurrent<T>(Action<string, T> handlers, string worldId, T value, WorldTransitionOperation operation)
        {
            if (handlers == null) return;
            foreach (Action<string, T> listener in handlers.GetInvocationList())
            {
                if (!operation.IsCurrent) return;
                listener(worldId, value);
            }
        }

        void ReportProgress(string worldId, float progress)
        {
            var operation = CurrentTransition;
            operation.ThrowIfStale();
            InvokeCurrent(OnWorldLoadProgress, worldId, progress, operation);
            operation.ThrowIfStale();
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
