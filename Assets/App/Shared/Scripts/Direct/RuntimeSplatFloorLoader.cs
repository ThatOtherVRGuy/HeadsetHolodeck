// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GaussianSplatting.Runtime;
using Unity.Collections;
using UnityEngine;
using WorldLabs.Runtime.Tools;

namespace WorldLabs.Runtime.Tools
{
    /// <summary>
    /// Build a GaussianSplatRenderer from SPZ bytes, estimate the floor, and place the
    /// spawned object so the floor lands at Y=0 and the floor center lands at X=Z=0.
    /// </summary>
    public class RuntimeSplatFloorLoader : MonoBehaviour
    {
        public enum SplatQualityPreset
        {
            VeryHigh,
            High,
            Medium,
            Low,
            VeryLow
        }

        public enum SplatSourceKind
        {
            WorldLabs,
            LooseSplat
        }

        public enum MirrorAxis
        {
            None,
            X,
            Y,
            Z
        }

        const float MetaHyperscapeMaxAxisRatio = 64f;

        public struct SourceOrientation
        {
            public Quaternion rotation;
            public Vector3 scale;

            public Matrix4x4 Matrix => Matrix4x4.TRS(Vector3.zero, rotation, scale);
        }

        [Header("Parent")]
        [Tooltip("Parent transform for spawned worlds. Uses this transform if null.")]
        public Transform worldParent;

        [Header("Placement")]
        [Tooltip("Legacy toggle. When true, default sourceKind is WorldLabs; when false, identity is used unless sourceKind is overridden by the caller.")]
        public bool applyWorldLabsDefaultRotation = true;

        [Tooltip("Default source orientation for loader calls that do not pass an explicit source kind.")]
        public SplatSourceKind defaultSourceKind = SplatSourceKind.WorldLabs;

        [Tooltip("Loose .ply/.spz files need +90 X plus a mirror. Z matches the current local import convention.")]
        public MirrorAxis looseSplatMirrorAxis = MirrorAxis.Z;

        [Tooltip("If true, move the loaded splat so the detected floor is at Y=0 and floor center is at X=Z=0.")]
        public bool autoPlaceAtOrigin = true;

        [Tooltip("Attach an estimated player spawn pose to loaded splats so PlayerOriginController can place Me inside the world.")]
        public bool attachEstimatedSpawnPose = true;

        [Min(0.1f)]
        public float spawnEyeHeightMeters = 1.6f;

        [Min(0.25f)]
        public float spawnLookDistanceMeters = 2f;

        [Header("Processing")]
        public SplatQualityPreset quality = SplatQualityPreset.Medium;

        [Header("Floor Detection")]
        public SplatFloorAnalysisOptions floorAnalysis = new();

        [Header("Spawn Estimation")]
        public SplatSpawnEstimatorSettings spawnEstimation = new();

        [Header("Shaders")]
        [HideInInspector] public Shader splatShader;
        [HideInInspector] public Shader compositeShader;
        [HideInInspector] public Shader debugPointsShader;
        [HideInInspector] public Shader debugBoxesShader;
        [HideInInspector] public ComputeShader splatUtilitiesDeviceRadix;
        [HideInInspector] public ComputeShader splatUtilitiesFidelityFX;

        [Serializable]
        public class LoadResult
        {
            public GameObject gameObject;
            public GaussianSplatRenderer renderer;
            public RuntimeSplatData runtimeData;
            public SplatFloorEstimate floorEstimate;
            public SplatSpawnMetadata spawnMetadata;
        }

        void Awake()
        {
            if (worldParent == null)
                worldParent = transform;

            EnsureShaders();
        }

        readonly HashSet<PlacementScope> activePlacements = new();
        WorldTransitionOperation standaloneOperation;

        void OnDisable()
        {
            // Cancellation callbacks only wake managed awaiters. Unity cleanup occurs here
            // and when those awaiters resume on Unity's context.
            foreach (PlacementScope placement in new List<PlacementScope>(activePlacements))
                placement.operation.Cancel();
            standaloneOperation?.Cancel();
            standaloneOperation = null;
        }

        void OnDestroy() => OnDisable();

        sealed class PlacementScope : IDisposable
        {
            readonly RuntimeSplatFloorLoader owner;
            readonly CancellationTokenSource cancellation;
            public readonly WorldTransitionOperation operation;
            GameObject root;
            bool succeeded;
            public CancellationToken Token => cancellation.Token;

            public PlacementScope(RuntimeSplatFloorLoader owner, WorldTransitionOperation operation, CancellationToken token)
            {
                this.owner = owner;
                this.operation = operation;
                cancellation = CancellationTokenSource.CreateLinkedTokenSource(token, operation.Token);
                owner.activePlacements.Add(this);
            }

            public void ThrowIfStale()
            {
                Token.ThrowIfCancellationRequested();
                operation.ThrowIfStale();
                if (owner == null || !owner.isActiveAndEnabled)
                    throw new OperationCanceledException();
            }

            public void Track(GameObject gameObject)
            {
                root = gameObject;
                operation.Track(gameObject);
                ThrowIfStale();
            }

            public void Succeed() { ThrowIfStale(); succeeded = true; }

            public void Dispose()
            {
                owner.activePlacements.Remove(this);
                cancellation.Dispose();
                if (!succeeded && root != null)
                    DestroyPlacementRoot(root);
            }
        }

        PlacementScope BeginPlacement(WorldTransitionOperation operation, CancellationToken token)
        {
            if (operation == null)
            {
                standaloneOperation?.Cancel();
                operation = standaloneOperation = WorldTransitionOperation.CreateStandalone();
            }
            return new PlacementScope(this, operation, token);
        }

        static void DestroyPlacementRoot(GameObject root)
        {
            if (root == null) return;
            root.SetActive(false);
            if (Application.isPlaying) Destroy(root);
            else DestroyImmediate(root);
        }

        static async Task<T> AwaitPlacementAsync<T>(Task<T> processing, CancellationToken token)
        {
            var canceled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (token.Register(() => canceled.TrySetResult(true)))
            {
                if (await Task.WhenAny(processing, canceled.Task) != processing)
                {
                    _ = ObserveProcessingAsync(processing);
                    throw new OperationCanceledException(token);
                }
            }
            return await processing;
        }

        static async Task ObserveProcessingAsync(Task processing)
        {
            try { await processing; }
            catch (Exception) { /* Native allocations are disposed by the worker's finally. */ }
        }

        SplatFloorAnalysisOptions SnapshotFloorOptions(Matrix4x4 orientation)
        {
            // Options belong to one request; another load must not change a worker's orientation.
            var options = floorAnalysis == null ? new SplatFloorAnalysisOptions()
                : JsonUtility.FromJson<SplatFloorAnalysisOptions>(JsonUtility.ToJson(floorAnalysis));
            options.positionTransform = orientation;
            return options;
        }

        SplatSpawnEstimatorSettings SnapshotSpawnSettings()
        {
            return spawnEstimation == null ? new SplatSpawnEstimatorSettings()
                : JsonUtility.FromJson<SplatSpawnEstimatorSettings>(JsonUtility.ToJson(spawnEstimation));
        }

        LoadResult CreatePlacedRenderer(PlacementScope placement, RuntimeSplatData data,
            SplatFloorEstimate floorEstimate, SplatSpawnMetadata spawnMetadata,
            SourceOrientation orientation, SplatSourceKind sourceKind, string worldName,
            string gameObjectName, Action<GameObject> beforeRendererCreated)
        {
            placement.ThrowIfStale();
            string goName = !string.IsNullOrWhiteSpace(gameObjectName)
                ? gameObjectName : (!string.IsNullOrWhiteSpace(worldName) ? $"World_{worldName}" : "World");
            var go = new GameObject(goName);
            go.SetActive(false);
            // Track immediately: callback failures, disable, and supersession must not orphan a root.
            placement.Track(go);
            go.transform.SetParent(worldParent, false);
            go.transform.localRotation = orientation.rotation;
            go.transform.localScale = orientation.scale;
            go.transform.localPosition = autoPlaceAtOrigin && floorEstimate != null && floorEstimate.success
                ? floorEstimate.recommendedLocalPosition : Vector3.zero;
            AttachSpawnPose(go, floorEstimate, spawnMetadata);
            // Persisted transform/spawn overrides run before the renderer exists or becomes visible.
            beforeRendererCreated?.Invoke(go);
            placement.ThrowIfStale();
            var renderer = go.AddComponent<GaussianSplatRenderer>();
            ConfigureRuntimeRenderer(renderer, sourceKind);
            renderer.LoadFromRuntimeData(data);
            placement.ThrowIfStale();
            go.SetActive(true);
            placement.ThrowIfStale();
            return new LoadResult
            {
                gameObject = go, renderer = renderer, runtimeData = data,
                floorEstimate = floorEstimate, spawnMetadata = spawnMetadata
            };
        }

        /// <summary>
        /// Load a world from compressed SPZ bytes, estimate floor placement, and spawn the renderer.
        /// </summary>
        public LoadResult LoadPlacedRuntimeWorld(
            byte[] spzBytes,
            string worldId = null,
            string worldName = null,
            string thumbnailUrl = null,
            string gameObjectName = null,
            SplatSourceKind? sourceKind = null,
            SplatSpawnMetadata savedSpawnMetadata = null,
            WorldTransitionOperation operation = null)
        {
            using var placement = BeginPlacement(operation, default);
            placement.ThrowIfStale();
            if (spzBytes == null || spzBytes.Length == 0)
                throw new ArgumentException("SPZ bytes are null or empty.", nameof(spzBytes));

            EnsureShaders();

            SplatSourceKind resolvedSourceKind = ResolveSourceKind(sourceKind);
            SPZFileReader.HeaderInfo spzHeader = SPZFileReader.ReadHeaderInfo(spzBytes);
            SourceOrientation orientation = ResolveSourceOrientation(resolvedSourceKind, looseSplatMirrorAxis);
            float compatibilityScale = GetCompatibilityScaleForSource(resolvedSourceKind, spzHeader.version, spzHeader.shLevel, spzHeader.flags);
            orientation.scale *= compatibilityScale;
            Debug.Log($"[RuntimeSplatFloorLoader] SPZ header world='{worldName}'; version={spzHeader.version}; count={spzHeader.vertexCount}; sh={spzHeader.shLevel}; fractBits={spzHeader.fractBits}; flags=0x{spzHeader.flags:X2}; reserved=0x{spzHeader.reserved:X2}; compatibilityScale={compatibilityScale}.");

            // Analyze in the same local coordinate system the spawned object will use.
            SplatFloorAnalysisOptions options = SnapshotFloorOptions(orientation.Matrix);

            var (posFormat, scaleFormat, colorFormat, shFormat) = GetFormats(quality);
            SplatFloorEstimate floorEstimate;
            SplatSpawnMetadata spawnMetadata;
            RuntimeSplatData data;
            NativeArray<InputSplatData> inputSplats = default;
            try
            {
                SPZFileReader.ReadFile(spzBytes, out inputSplats);
                if (ShouldClampAnisotropyForSource(resolvedSourceKind, spzHeader.version, spzHeader.shLevel, spzHeader.flags))
                    ClampSplatScaleAnisotropy(inputSplats, MetaHyperscapeMaxAxisRatio, worldName);

                floorEstimate = SplatFloorAnalyzer.AnalyzeSplats(inputSplats, options);
                spawnMetadata = ResolveSpawnMetadata(
                    savedSpawnMetadata,
                    inputSplats,
                    orientation.Matrix,
                    worldName,
                    floorEstimate);
                data = RuntimeSplatProcessing.Process(
                    inputSplats,
                    posFormat,
                    scaleFormat,
                    colorFormat,
                    shFormat);
            }
            finally
            {
                if (inputSplats.IsCreated)
                    inputSplats.Dispose();
            }

            data.worldId = worldId;
            data.worldName = worldName;
            data.thumbnailUrl = thumbnailUrl;

            LoadResult result = CreatePlacedRenderer(placement, data, floorEstimate, spawnMetadata,
                orientation, resolvedSourceKind, worldName, gameObjectName, null);
            placement.Succeed();
            return result;
        }

        /// <summary>
        /// Async placement variant of <see cref="LoadPlacedRuntimeWorld"/>. SPZ processing stays
        /// on Unity's thread for Quest compatibility; operation checks guard every async boundary.
        /// </summary>
        public async Task<LoadResult> LoadPlacedRuntimeWorldAsync(
            byte[] spzBytes,
            string worldId = null,
            string worldName = null,
            string thumbnailUrl = null,
            string gameObjectName = null,
            SplatSourceKind? sourceKind = null,
            SplatSpawnMetadata savedSpawnMetadata = null,
            Action<GameObject> beforeRendererCreated = null,
            CancellationToken cancellationToken = default,
            WorldTransitionOperation operation = null)
        {
            using var placement = BeginPlacement(operation, cancellationToken);
            placement.ThrowIfStale();
            if (spzBytes == null || spzBytes.Length == 0)
                throw new ArgumentException("SPZ bytes are null or empty.", nameof(spzBytes));

            EnsureShaders();

            SplatSourceKind resolvedSourceKind = ResolveSourceKind(sourceKind);
            SPZFileReader.HeaderInfo spzHeader = SPZFileReader.ReadHeaderInfo(spzBytes);
            SourceOrientation orientation = ResolveSourceOrientation(resolvedSourceKind, looseSplatMirrorAxis);
            float compatibilityScale = GetCompatibilityScaleForSource(resolvedSourceKind, spzHeader.version, spzHeader.shLevel, spzHeader.flags);
            orientation.scale *= compatibilityScale;
            Debug.Log($"[RuntimeSplatFloorLoader] SPZ header world='{worldName}'; version={spzHeader.version}; count={spzHeader.vertexCount}; sh={spzHeader.shLevel}; fractBits={spzHeader.fractBits}; flags=0x{spzHeader.flags:X2}; reserved=0x{spzHeader.reserved:X2}; compatibilityScale={compatibilityScale}.");

            SplatFloorAnalysisOptions opts = SnapshotFloorOptions(orientation.Matrix);

            var (posFormat, scaleFormat, colorFormat, shFormat) = GetFormats(quality);

            // Unity.Collections used by the SPZ decoder and processor are not safe on
            // Quest when allocated and accessed from Task.Run. Keep this work on the
            // Unity thread; correctness is more important than a short load hitch.
            var (floorEstimate, spawnMetadata, data) = ProcessOnUnityThread();

            (SplatFloorEstimate floorEstimate, SplatSpawnMetadata spawnMetadata, RuntimeSplatData data) ProcessOnUnityThread()
            {
                NativeArray<InputSplatData> inputSplats = default;
                try
                {
                    Debug.Log($"[RuntimeSplatFloorLoader] SPZ source world='{worldName}'; {DescribeSourceBytes(spzBytes)}.");
                    placement.ThrowIfStale();
                    SPZFileReader.ReadFile(spzBytes, out inputSplats);
                    placement.ThrowIfStale();
                    if (ShouldClampAnisotropyForSource(resolvedSourceKind, spzHeader.version, spzHeader.shLevel, spzHeader.flags))
                        ClampSplatScaleAnisotropy(inputSplats, MetaHyperscapeMaxAxisRatio, worldName);

                    SplatFloorEstimate floorEstimate = SplatFloorAnalyzer.AnalyzeSplats(inputSplats, opts);
                    SplatSpawnMetadata spawnMetadata = ResolveSpawnMetadata(
                        savedSpawnMetadata,
                        inputSplats,
                        orientation.Matrix,
                        worldName,
                        floorEstimate);

                    RuntimeSplatData data = RuntimeSplatProcessing.Process(
                        inputSplats, posFormat, scaleFormat, colorFormat, shFormat);

                    Debug.Log("[RuntimeSplatFloorLoader] " + BuildRuntimeDataSignature(worldName, data));

                    data.worldId      = worldId;
                    data.worldName    = worldName;
                    data.thumbnailUrl = thumbnailUrl;

                    return (floorEstimate, spawnMetadata, data);
                }
                finally
                {
                    if (inputSplats.IsCreated)
                        inputSplats.Dispose();
                }
            }

            // Back on the main thread; all Unity API calls go here.
            await Task.Yield();

            placement.ThrowIfStale();
            LoadResult result = CreatePlacedRenderer(placement, data, floorEstimate, spawnMetadata,
                orientation, resolvedSourceKind, worldName, gameObjectName, beforeRendererCreated);
            await Task.Yield();
            placement.Succeed();
            return result;
        }

        static string BuildRuntimeDataSignature(string worldName, RuntimeSplatData data)
        {
            if (data == null)
                return "Runtime data signature: <null>.";

            return $"Runtime data signature world='{worldName}'; count={data.splatCount}; " +
                   $"boundsMin={data.boundsMin}; boundsMax={data.boundsMax}; " +
                   $"formats=pos:{data.posFormat},scale:{data.scaleFormat},color:{data.colorFormat},sh:{data.shFormat}; " +
                   $"bytes=pos:{data.posData?.Length ?? 0},other:{data.othData?.Length ?? 0},color:{data.colData?.Length ?? 0},sh:{data.shData?.Length ?? 0},chunks:{data.chkData?.Length ?? 0}.";
        }

        static string DescribeSourceBytes(byte[] bytes)
        {
            const ulong offsetBasis = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;
            ulong hash = offsetBasis;
            for (int i = 0; i < bytes.Length; ++i)
            {
                hash ^= bytes[i];
                hash *= prime;
            }
            return $"bytes={bytes.Length}; fnv1a64={hash:X16}";
        }

        /// <summary>
        /// Async variant that accepts already-parsed <see cref="InputSplatData"/> instead of raw SPZ bytes.
        /// Use this for PLY files: parse with <see cref="RuntimePlyReader"/>, then call this method.
        /// <para><paramref name="inputSplats"/> is disposed by this method after processing.</para>
        /// </summary>
        public async Task<LoadResult> LoadPlacedRuntimeWorldFromSplatsAsync(
            NativeArray<InputSplatData> inputSplats,
            string worldId = null,
            string worldName = null,
            string gameObjectName = null,
            SplatSourceKind? sourceKind = null,
            SplatSpawnMetadata savedSpawnMetadata = null,
            Action<GameObject> beforeRendererCreated = null,
            CancellationToken cancellationToken = default,
            WorldTransitionOperation operation = null)
        {
            PlacementScope placement = null;
            bool workerOwnsSplats = false;
            try
            {
                placement = BeginPlacement(operation, cancellationToken);
                placement.ThrowIfStale();
                if (!inputSplats.IsCreated || inputSplats.Length == 0)
                    throw new ArgumentException("inputSplats is empty or not created.", nameof(inputSplats));
                EnsureShaders();
                SplatSourceKind resolvedSourceKind = ResolveSourceKind(sourceKind);
                SourceOrientation orientation = ResolveSourceOrientation(resolvedSourceKind, looseSplatMirrorAxis);
                SplatFloorAnalysisOptions opts = SnapshotFloorOptions(orientation.Matrix);
                SplatSpawnEstimatorSettings spawnSettings = SnapshotSpawnSettings();
                var (posFormat, scaleFormat, colorFormat, shFormat) = GetFormats(quality);
                CancellationToken token = placement.Token;

                Task<(SplatFloorEstimate floorEstimate, SplatSpawnMetadata spawnMetadata, RuntimeSplatData data)> processing = Task.Run(() =>
                {
                    try
                    {
                        token.ThrowIfCancellationRequested();
                        SplatFloorEstimate floorEstimate = SplatFloorAnalyzer.AnalyzeSplats(inputSplats, opts);
                        token.ThrowIfCancellationRequested();
                        SplatSpawnMetadata spawnMetadata = ResolveSpawnMetadata(savedSpawnMetadata, inputSplats,
                            orientation.Matrix, worldName, floorEstimate, spawnSettings);
                        token.ThrowIfCancellationRequested();
                        RuntimeSplatData data = RuntimeSplatProcessing.Process(
                            inputSplats, posFormat, scaleFormat, colorFormat, shFormat);
                        data.worldId = worldId;
                        data.worldName = worldName;
                        return (floorEstimate, spawnMetadata, data);
                    }
                    finally
                    {
                        inputSplats.Dispose();
                    }
                });
                workerOwnsSplats = true;
                var (floorEstimate, spawnMetadata, data) = await AwaitPlacementAsync(processing, token);
                placement.ThrowIfStale();
                await Task.Yield();
                placement.ThrowIfStale();
                LoadResult result = CreatePlacedRenderer(placement, data, floorEstimate, spawnMetadata,
                    orientation, resolvedSourceKind, worldName, gameObjectName, beforeRendererCreated);
                await Task.Yield();
                placement.Succeed();
                return result;
            }
            finally
            {
                // Once started, the worker keeps exclusive ownership until its finally completes,
                // including when local waiting was canceled and the caller has already returned.
                if (!workerOwnsSplats && inputSplats.IsCreated)
                    inputSplats.Dispose();
                placement?.Dispose();
            }
        }

        /// <summary>
        /// Apply a previously computed floor placement to an existing transform.
        /// Use this only if the estimate was computed with the same local rotation.
        /// </summary>
        public void ApplyFloorPlacement(Transform target, SplatFloorEstimate estimate)
        {
            if (target == null)
                throw new ArgumentNullException(nameof(target));
            if (estimate == null)
                throw new ArgumentNullException(nameof(estimate));

            target.localPosition = estimate.recommendedLocalPosition;
        }

        public static SourceOrientation ResolveSourceOrientation(SplatSourceKind sourceKind, MirrorAxis looseMirrorAxis)
        {
            if (sourceKind == SplatSourceKind.WorldLabs)
            {
                return new SourceOrientation
                {
                    rotation = Quaternion.Euler(-180f, 0f, 0f),
                    scale = Vector3.one
                };
            }

            return new SourceOrientation
            {
                rotation = Quaternion.Euler(90f, 0f, 0f),
                scale = MirrorScale(looseMirrorAxis)
            };
        }

        SplatSpawnMetadata ResolveSpawnMetadata(
            SplatSpawnMetadata savedSpawnMetadata,
            NativeArray<InputSplatData> inputSplats,
            Matrix4x4 orientationMatrix,
            string worldName,
            SplatFloorEstimate floorEstimate,
            SplatSpawnEstimatorSettings settings = null)
        {
            if (savedSpawnMetadata != null && savedSpawnMetadata.hasPose)
            {
                if (IsSavedSpawnWithinPlacedBounds(savedSpawnMetadata, floorEstimate))
                    return savedSpawnMetadata;

                Debug.LogWarning($"[RuntimeSplatFloorLoader] Ignoring saved spawn for '{worldName}' because it is outside the newly decoded splat bounds.");
            }

            return SplatSpawnEstimator.EstimateFromSplats(
                inputSplats,
                orientationMatrix,
                settings ?? spawnEstimation,
                worldName,
                null,
                floorEstimate);
        }

        /// <summary>
        /// Rejects stale saved poses made against an incorrectly decoded or differently transformed splat.
        /// The saved pose is in the placed-world coordinate space, as is this expanded bounds check.
        /// </summary>
        public static bool IsSavedSpawnWithinPlacedBounds(SplatSpawnMetadata savedSpawn, SplatFloorEstimate floorEstimate)
        {
            if (savedSpawn == null || !savedSpawn.hasPose || !IsFinite(savedSpawn.spawn))
                return false;

            // If a floor could not be estimated, retain the saved pose rather than replacing it
            // with an unrelated fallback solely because we have no reliable new bounds.
            if (floorEstimate == null || !floorEstimate.success)
                return true;

            Bounds placedBounds = floorEstimate.analyzedBounds;
            placedBounds.center += floorEstimate.recommendedLocalPosition;

            // Allow a normal entry point just outside a small capture while still rejecting
            // grossly stale positions such as the former SPZ int24 sentinel-derived spawn.
            float margin = Mathf.Max(2f, placedBounds.size.magnitude * 0.05f);
            placedBounds.Expand(margin * 2f);
            return placedBounds.Contains(savedSpawn.spawn);
        }

        static bool IsFinite(Vector3 value)
        {
            return float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
        }

        SplatSourceKind ResolveSourceKind(SplatSourceKind? sourceKind)
        {
            if (sourceKind.HasValue)
                return sourceKind.Value;

            if (!applyWorldLabsDefaultRotation)
                return SplatSourceKind.LooseSplat;

            return defaultSourceKind;
        }

        static Vector3 MirrorScale(MirrorAxis axis)
        {
            return axis switch
            {
                MirrorAxis.X => new Vector3(-1f, 1f, 1f),
                MirrorAxis.Y => new Vector3(1f, -1f, 1f),
                MirrorAxis.Z => new Vector3(1f, 1f, -1f),
                _ => Vector3.one
            };
        }

        void AttachSpawnPose(GameObject worldObject, SplatFloorEstimate estimate, SplatSpawnMetadata spawnMetadata)
        {
            if (!attachEstimatedSpawnPose || worldObject == null)
                return;

            SplatSpawnPose pose = worldObject.GetComponent<SplatSpawnPose>() ?? worldObject.AddComponent<SplatSpawnPose>();
            if (spawnMetadata != null && spawnMetadata.hasPose)
            {
                pose.SetWorldPose(worldObject.transform, spawnMetadata.spawn, spawnMetadata.rotation, spawnMetadata.lookAt);
                pose.confidence = spawnMetadata.confidence;
                pose.method = spawnMetadata.method;
                return;
            }

            if (estimate == null || !estimate.success)
                return;

            Vector3 playerPosition = new Vector3(0f, Mathf.Max(0.1f, spawnEyeHeightMeters), 0f);

            Vector3 placedBoundsCenter = estimate.analyzedBounds.center + estimate.recommendedLocalPosition;
            Vector3 lookTarget = new Vector3(placedBoundsCenter.x, Mathf.Max(0.1f, spawnEyeHeightMeters * 0.9f), placedBoundsCenter.z);
            Vector3 forward = Vector3.ProjectOnPlane(lookTarget - playerPosition, Vector3.up);
            if (forward.sqrMagnitude < 0.0001f)
                forward = Vector3.forward;

            Vector3 lookAt = playerPosition + forward.normalized * Mathf.Max(0.25f, spawnLookDistanceMeters);
            pose.SetWorldPose(worldObject.transform, playerPosition, Quaternion.LookRotation(forward.normalized, Vector3.up), lookAt);
            pose.confidence = Mathf.Clamp01(estimate.supportCount > 0 ? 0.65f : 0.25f);
            pose.method = "floor_bounds_center_v1";
        }

        internal static bool ShouldOptimizeForQuest(RuntimePlatform platform, SplatSourceKind sourceKind)
        {
            return platform == RuntimePlatform.Android && sourceKind == SplatSourceKind.WorldLabs;
        }

        internal static int GetSHOrderForSource(SplatSourceKind sourceKind)
        {
            return sourceKind == SplatSourceKind.LooseSplat ? 0 : 3;
        }

        internal static float GetCompatibilityScaleForSource(SplatSourceKind sourceKind, int spzVersion, int shLevel, int flags)
        {
            return sourceKind == SplatSourceKind.LooseSplat &&
                   spzVersion == 2 &&
                   shLevel == 3 &&
                   flags == 0x0c
                ? 0.001f
                : 1f;
        }

        internal static bool ShouldClampAnisotropyForSource(SplatSourceKind sourceKind, int spzVersion, int shLevel, int flags)
        {
            return sourceKind == SplatSourceKind.LooseSplat &&
                   spzVersion == 2 &&
                   shLevel == 3 &&
                   flags == 0x0c;
        }

        internal static int ClampSplatScaleAnisotropy(NativeArray<InputSplatData> splats, float maxAxisRatio, string worldName = null)
        {
            if (!splats.IsCreated || splats.Length == 0 || maxAxisRatio <= 1f)
                return 0;

            int changed = 0;
            for (int i = 0; i < splats.Length; ++i)
            {
                InputSplatData splat = splats[i];
                Vector3 scale = splat.scale;
                float max = Mathf.Max(scale.x, Mathf.Max(scale.y, scale.z));
                if (max <= 0f || !float.IsFinite(max))
                    continue;

                float minAllowed = max / maxAxisRatio;
                Vector3 clamped = new Vector3(
                    Mathf.Max(scale.x, minAllowed),
                    Mathf.Max(scale.y, minAllowed),
                    Mathf.Max(scale.z, minAllowed));

                if (clamped != scale)
                {
                    splat.scale = clamped;
                    splats[i] = splat;
                    ++changed;
                }
            }

            Debug.Log($"[RuntimeSplatFloorLoader] Meta/Hyperscape anisotropy clamp world='{worldName}'; maxAxisRatio={maxAxisRatio}; changed={changed}/{splats.Length}.");
            return changed;
        }

        void ConfigureRuntimeRenderer(GaussianSplatRenderer r, SplatSourceKind sourceKind)
        {
            AssignShaders(r);
            r.m_OptimizeForQuest = ShouldOptimizeForQuest(Application.platform, sourceKind);
            r.m_SHOrder = GetSHOrderForSource(sourceKind);
            Debug.Log($"[RuntimeSplatFloorLoader] Renderer configured renderer='{r.name}' sourceKind={sourceKind} shOrder={r.m_SHOrder} optimizeForQuest={r.m_OptimizeForQuest} platform={Application.platform}.");
        }

        void AssignShaders(GaussianSplatRenderer r)
        {
            r.m_ShaderSplats = splatShader;
            r.m_ShaderComposite = compositeShader;
            r.m_ShaderDebugPoints = debugPointsShader;
            r.m_ShaderDebugBoxes = debugBoxesShader;
            r.m_CSSplatUtilities_deviceRadixSort = splatUtilitiesDeviceRadix;
            r.m_CSSplatUtilities_fidelityFX = splatUtilitiesFidelityFX;
        }

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
        void Reset()
        {
            const string root = "Packages/com.worldlabs.gaussian-splatting/Shaders/";
            splatShader = UnityEditor.AssetDatabase.LoadAssetAtPath<Shader>(root + "RenderGaussianSplats.shader");
            compositeShader = UnityEditor.AssetDatabase.LoadAssetAtPath<Shader>(root + "GaussianComposite.shader");
            debugPointsShader = UnityEditor.AssetDatabase.LoadAssetAtPath<Shader>(root + "GaussianDebugRenderPoints.shader");
            debugBoxesShader = UnityEditor.AssetDatabase.LoadAssetAtPath<Shader>(root + "GaussianDebugRenderBoxes.shader");
            splatUtilitiesDeviceRadix = UnityEditor.AssetDatabase.LoadAssetAtPath<ComputeShader>(root + "SplatUtilities_DeviceRadixSort.compute");
            splatUtilitiesFidelityFX = UnityEditor.AssetDatabase.LoadAssetAtPath<ComputeShader>(root + "SplatUtilities_FidelityFX.compute");
        }
#endif

        static (
            GaussianSplatAsset.VectorFormat pos,
            GaussianSplatAsset.VectorFormat scale,
            GaussianSplatAsset.ColorFormat color,
            GaussianSplatAsset.SHFormat sh) GetFormats(SplatQualityPreset q)
        {
            return q switch
            {
                SplatQualityPreset.VeryHigh => (
                    GaussianSplatAsset.VectorFormat.Float32,
                    GaussianSplatAsset.VectorFormat.Float32,
                    GaussianSplatAsset.ColorFormat.Float32x4,
                    GaussianSplatAsset.SHFormat.Float32
                ),

                SplatQualityPreset.High => (
                    GaussianSplatAsset.VectorFormat.Norm11,
                    GaussianSplatAsset.VectorFormat.Norm11,
                    GaussianSplatAsset.ColorFormat.Float16x4,
                    GaussianSplatAsset.SHFormat.Float16
                ),

                SplatQualityPreset.Low => (
                    GaussianSplatAsset.VectorFormat.Norm6,
                    GaussianSplatAsset.VectorFormat.Norm6,
                    GaussianSplatAsset.ColorFormat.Norm8x4,
                    GaussianSplatAsset.SHFormat.Cluster64k
                ),

                SplatQualityPreset.VeryLow => (
                    GaussianSplatAsset.VectorFormat.Norm6,
                    GaussianSplatAsset.VectorFormat.Norm6,
                    GaussianSplatAsset.ColorFormat.BC7,
                    GaussianSplatAsset.SHFormat.Cluster4k
                ),

                _ => (
                    GaussianSplatAsset.VectorFormat.Norm11,
                    GaussianSplatAsset.VectorFormat.Norm11,
                    GaussianSplatAsset.ColorFormat.Norm8x4,
                    GaussianSplatAsset.SHFormat.Norm6
                ),
            };
        }
    }
}
