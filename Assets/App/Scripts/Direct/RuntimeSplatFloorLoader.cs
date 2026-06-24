// SPDX-License-Identifier: MIT

using System;
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
            SplatSpawnMetadata savedSpawnMetadata = null)
        {
            if (spzBytes == null || spzBytes.Length == 0)
                throw new ArgumentException("SPZ bytes are null or empty.", nameof(spzBytes));

            EnsureShaders();

            SourceOrientation orientation = ResolveSourceOrientation(ResolveSourceKind(sourceKind), looseSplatMirrorAxis);

            // Analyze in the same local coordinate system the spawned object will use.
            floorAnalysis ??= new SplatFloorAnalysisOptions();
            floorAnalysis.positionTransform = orientation.Matrix;

            var (posFormat, scaleFormat, colorFormat, shFormat) = GetFormats(quality);
            SplatFloorEstimate floorEstimate;
            SplatSpawnMetadata spawnMetadata;
            RuntimeSplatData data;
            NativeArray<InputSplatData> inputSplats = default;
            try
            {
                SPZFileReader.ReadFile(spzBytes, out inputSplats);
                floorEstimate = SplatFloorAnalyzer.AnalyzeSplats(inputSplats, floorAnalysis);
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

            string goName = !string.IsNullOrWhiteSpace(gameObjectName)
                ? gameObjectName
                : (!string.IsNullOrWhiteSpace(worldName) ? $"World_{worldName}" : "World");

            var go = new GameObject(goName);
            go.transform.SetParent(worldParent, false);
            go.transform.localRotation = orientation.rotation;
            go.transform.localScale = orientation.scale;
            go.transform.localPosition = autoPlaceAtOrigin && floorEstimate != null && floorEstimate.success
                ? floorEstimate.recommendedLocalPosition
                : Vector3.zero;
            AttachSpawnPose(go, floorEstimate, spawnMetadata);

            var renderer = go.AddComponent<GaussianSplatRenderer>();
            AssignShaders(renderer);
            renderer.LoadFromRuntimeData(data);

            return new LoadResult
            {
                gameObject = go,
                renderer = renderer,
                runtimeData = data,
                floorEstimate = floorEstimate,
                spawnMetadata = spawnMetadata
            };
        }

        /// <summary>
        /// Async variant of <see cref="LoadPlacedRuntimeWorld"/>. Offloads floor analysis and SPZ
        /// processing to a background thread, then creates the renderer on the main thread.
        /// Use this from coroutines to avoid blocking the main thread on large splats.
        /// </summary>
        public async Task<LoadResult> LoadPlacedRuntimeWorldAsync(
            byte[] spzBytes,
            string worldId = null,
            string worldName = null,
            string thumbnailUrl = null,
            string gameObjectName = null,
            SplatSourceKind? sourceKind = null,
            SplatSpawnMetadata savedSpawnMetadata = null)
        {
            if (spzBytes == null || spzBytes.Length == 0)
                throw new ArgumentException("SPZ bytes are null or empty.", nameof(spzBytes));

            EnsureShaders();

            SourceOrientation orientation = ResolveSourceOrientation(ResolveSourceKind(sourceKind), looseSplatMirrorAxis);

            SplatFloorAnalysisOptions opts = floorAnalysis ?? new SplatFloorAnalysisOptions();
            opts.positionTransform = orientation.Matrix;

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
                    SPZFileReader.ReadFile(spzBytes, out inputSplats);
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

            string goName = !string.IsNullOrWhiteSpace(gameObjectName)
                ? gameObjectName
                : (!string.IsNullOrWhiteSpace(worldName) ? $"World_{worldName}" : "World");

            var go = new GameObject(goName);
            go.transform.SetParent(worldParent, false);
            go.transform.localRotation = orientation.rotation;
            go.transform.localScale = orientation.scale;
            go.transform.localPosition = autoPlaceAtOrigin && floorEstimate != null && floorEstimate.success
                ? floorEstimate.recommendedLocalPosition
                : Vector3.zero;
            AttachSpawnPose(go, floorEstimate, spawnMetadata);

            var renderer = go.AddComponent<GaussianSplatRenderer>();
            AssignShaders(renderer);
            renderer.LoadFromRuntimeData(data);
            await Task.Yield();

            return new LoadResult
            {
                gameObject    = go,
                renderer      = renderer,
                runtimeData   = data,
                floorEstimate = floorEstimate,
                spawnMetadata = spawnMetadata,
            };
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
            SplatSpawnMetadata savedSpawnMetadata = null)
        {
            if (!inputSplats.IsCreated || inputSplats.Length == 0)
                throw new ArgumentException("inputSplats is empty or not created.", nameof(inputSplats));

            EnsureShaders();

            SourceOrientation orientation = ResolveSourceOrientation(ResolveSourceKind(sourceKind), looseSplatMirrorAxis);

            SplatFloorAnalysisOptions opts = floorAnalysis ?? new SplatFloorAnalysisOptions();
            opts.positionTransform = orientation.Matrix;

            var (posFormat, scaleFormat, colorFormat, shFormat) = GetFormats(quality);

            var (floorEstimate, spawnMetadata, data) = await Task.Run(() =>
            {
                try
                {
                    SplatFloorEstimate floorEstimate = SplatFloorAnalyzer.AnalyzeSplats(inputSplats, opts);
                    SplatSpawnMetadata spawnMetadata = ResolveSpawnMetadata(
                        savedSpawnMetadata,
                        inputSplats,
                        orientation.Matrix,
                        worldName,
                        floorEstimate);

                    RuntimeSplatData data = RuntimeSplatProcessing.Process(
                        inputSplats, posFormat, scaleFormat, colorFormat, shFormat);

                    data.worldId   = worldId;
                    data.worldName = worldName;

                    return (floorEstimate, spawnMetadata, data);
                }
                finally
                {
                    inputSplats.Dispose();
                }
            });

            await Task.Yield();

            string goName = !string.IsNullOrWhiteSpace(gameObjectName)
                ? gameObjectName
                : (!string.IsNullOrWhiteSpace(worldName) ? $"World_{worldName}" : "World");

            var go = new GameObject(goName);
            go.transform.SetParent(worldParent, false);
            go.transform.localRotation = orientation.rotation;
            go.transform.localScale = orientation.scale;
            go.transform.localPosition = autoPlaceAtOrigin && floorEstimate != null && floorEstimate.success
                ? floorEstimate.recommendedLocalPosition
                : Vector3.zero;
            AttachSpawnPose(go, floorEstimate, spawnMetadata);

            var renderer = go.AddComponent<GaussianSplatRenderer>();
            AssignShaders(renderer);
            renderer.LoadFromRuntimeData(data);
            await Task.Yield();

            return new LoadResult
            {
                gameObject    = go,
                renderer      = renderer,
                runtimeData   = data,
                floorEstimate = floorEstimate,
                spawnMetadata = spawnMetadata,
            };
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
            SplatFloorEstimate floorEstimate)
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
                spawnEstimation,
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
