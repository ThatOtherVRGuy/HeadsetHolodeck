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

        [Header("Parent")]
        [Tooltip("Parent transform for spawned worlds. Uses this transform if null.")]
        public Transform worldParent;

        [Header("Placement")]
        [Tooltip("Apply the same -180 X rotation used by WorldLabsWorldManager.")]
        public bool applyWorldLabsDefaultRotation = true;

        [Tooltip("If true, move the loaded splat so the detected floor is at Y=0 and floor center is at X=Z=0.")]
        public bool autoPlaceAtOrigin = true;

        [Header("Processing")]
        public SplatQualityPreset quality = SplatQualityPreset.Medium;

        [Header("Floor Detection")]
        public SplatFloorAnalysisOptions floorAnalysis = new();

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
            string gameObjectName = null)
        {
            if (spzBytes == null || spzBytes.Length == 0)
                throw new ArgumentException("SPZ bytes are null or empty.", nameof(spzBytes));

            EnsureShaders();

            Quaternion localRotation = applyWorldLabsDefaultRotation
                ? Quaternion.Euler(-180f, 0f, 0f)
                : Quaternion.identity;

            // Analyze in the same local coordinate system the spawned object will use.
            floorAnalysis ??= new SplatFloorAnalysisOptions();
            floorAnalysis.positionTransform = Matrix4x4.Rotate(localRotation);

            SplatFloorEstimate floorEstimate = SplatFloorAnalyzer.AnalyzeSpzBytes(spzBytes, floorAnalysis);

            var (posFormat, scaleFormat, colorFormat, shFormat) = GetFormats(quality);
            RuntimeSplatData data = RuntimeSplatProcessing.ProcessSPZBytes(
                spzBytes,
                posFormat,
                scaleFormat,
                colorFormat,
                shFormat);

            data.worldId = worldId;
            data.worldName = worldName;
            data.thumbnailUrl = thumbnailUrl;

            string goName = !string.IsNullOrWhiteSpace(gameObjectName)
                ? gameObjectName
                : (!string.IsNullOrWhiteSpace(worldName) ? $"World_{worldName}" : "World");

            var go = new GameObject(goName);
            go.transform.SetParent(worldParent, false);
            go.transform.localRotation = localRotation;
            go.transform.localPosition = autoPlaceAtOrigin && floorEstimate != null && floorEstimate.success
                ? floorEstimate.recommendedLocalPosition
                : Vector3.zero;

            var renderer = go.AddComponent<GaussianSplatRenderer>();
            AssignShaders(renderer);
            renderer.LoadFromRuntimeData(data);

            return new LoadResult
            {
                gameObject = go,
                renderer = renderer,
                runtimeData = data,
                floorEstimate = floorEstimate
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
            string gameObjectName = null)
        {
            if (spzBytes == null || spzBytes.Length == 0)
                throw new ArgumentException("SPZ bytes are null or empty.", nameof(spzBytes));

            EnsureShaders();

            Quaternion localRotation = applyWorldLabsDefaultRotation
                ? Quaternion.Euler(-180f, 0f, 0f)
                : Quaternion.identity;

            SplatFloorAnalysisOptions opts = floorAnalysis ?? new SplatFloorAnalysisOptions();
            opts.positionTransform = Matrix4x4.Rotate(localRotation);

            var (posFormat, scaleFormat, colorFormat, shFormat) = GetFormats(quality);

            var (floorEstimate, data) = await Task.Run(() =>
            {
                SplatFloorEstimate floorEstimate = SplatFloorAnalyzer.AnalyzeSpzBytes(spzBytes, opts);

                RuntimeSplatData data = RuntimeSplatProcessing.ProcessSPZBytes(
                    spzBytes, posFormat, scaleFormat, colorFormat, shFormat);

                data.worldId      = worldId;
                data.worldName    = worldName;
                data.thumbnailUrl = thumbnailUrl;

                return (floorEstimate, data);
            });

            // Back on the main thread; all Unity API calls go here.
            await Task.Yield();

            string goName = !string.IsNullOrWhiteSpace(gameObjectName)
                ? gameObjectName
                : (!string.IsNullOrWhiteSpace(worldName) ? $"World_{worldName}" : "World");

            var go = new GameObject(goName);
            go.transform.SetParent(worldParent, false);
            go.transform.localRotation = localRotation;
            go.transform.localPosition = autoPlaceAtOrigin && floorEstimate != null && floorEstimate.success
                ? floorEstimate.recommendedLocalPosition
                : Vector3.zero;

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
            };
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
            string gameObjectName = null)
        {
            if (!inputSplats.IsCreated || inputSplats.Length == 0)
                throw new ArgumentException("inputSplats is empty or not created.", nameof(inputSplats));

            EnsureShaders();

            Quaternion localRotation = applyWorldLabsDefaultRotation
                ? Quaternion.Euler(-180f, 0f, 0f)
                : Quaternion.identity;

            SplatFloorAnalysisOptions opts = floorAnalysis ?? new SplatFloorAnalysisOptions();
            opts.positionTransform = Matrix4x4.Rotate(localRotation);

            var (posFormat, scaleFormat, colorFormat, shFormat) = GetFormats(quality);

            var (floorEstimate, data) = await Task.Run(() =>
            {
                try
                {
                    SplatFloorEstimate floorEstimate = SplatFloorAnalyzer.AnalyzeSplats(inputSplats, opts);

                    RuntimeSplatData data = RuntimeSplatProcessing.Process(
                        inputSplats, posFormat, scaleFormat, colorFormat, shFormat);

                    data.worldId   = worldId;
                    data.worldName = worldName;

                    return (floorEstimate, data);
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
            go.transform.localRotation = localRotation;
            go.transform.localPosition = autoPlaceAtOrigin && floorEstimate != null && floorEstimate.success
                ? floorEstimate.recommendedLocalPosition
                : Vector3.zero;

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
