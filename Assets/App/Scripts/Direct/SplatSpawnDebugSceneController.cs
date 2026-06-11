// SPDX-License-Identifier: MIT

using System;
using System.IO;
using System.Threading.Tasks;
using GaussianSplatting.Runtime;
using Unity.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace WorldLabs.Runtime.Tools
{
    public sealed class SplatSpawnDebugSceneController : MonoBehaviour
    {
        [Header("Input")]
        [Tooltip("Absolute path to an .spz or .ply file.")]
        public string filePath;
        public RuntimeSplatFloorLoader.SplatSourceKind sourceKind = RuntimeSplatFloorLoader.SplatSourceKind.LooseSplat;

        [Header("Scene")]
        public RuntimeSplatFloorLoader floorLoader;
        public SplatSpawnDebugVisualizer visualizer;
        public Transform worldParent;
        [Tooltip("Camera to move to the estimated spawn pose after loading. Defaults to Camera.main.")]
        public Transform cameraToPlace;
        public bool placeCameraAtEstimatedSpawn = true;
        public bool loadOnStart;

        [Header("Markers")]
        public bool updateMarkersAfterLoad = true;
        public Transform originMarker;
        public Transform consensusMarker;
        public Transform longAxisConsensusMarker;
        public Transform spawnMarker;
        public Transform lookAtMarker;
        public bool cycleCameraWithSpace = true;
        int _nextMarkerIndex;

        [Header("Debug")]
        public SplatSpawnEstimatorSettings estimatorSettings = new()
        {
            maxSamples = 20000,
            maxDebugNormalLines = 500
        };

        [TextArea(2, 4)] public string status = "Idle.";

        void Start()
        {
            if (loadOnStart)
                LoadConfiguredFile();
        }

        void Update()
        {
            if (cycleCameraWithSpace && Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
                MoveCameraToNextMarker();
        }

        [ContextMenu("Load Configured File")]
        public void LoadConfiguredFile()
        {
            _ = LoadConfiguredFileAsync();
        }

        public async Task LoadConfiguredFileAsync()
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                status = "No file path configured.";
                Debug.LogWarning("[SplatSpawnDebugSceneController] No file path configured.", this);
                return;
            }

            if (!File.Exists(filePath))
            {
                status = "File not found: " + filePath;
                Debug.LogWarning("[SplatSpawnDebugSceneController] " + status, this);
                return;
            }

            if (floorLoader == null)
                floorLoader = FindFirstObjectByType<RuntimeSplatFloorLoader>();
            if (visualizer == null)
                visualizer = FindFirstObjectByType<SplatSpawnDebugVisualizer>();
            if (floorLoader == null)
            {
                status = "RuntimeSplatFloorLoader not found.";
                Debug.LogWarning("[SplatSpawnDebugSceneController] " + status, this);
                return;
            }

            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            if (ext != ".spz" && ext != ".ply")
            {
                status = "Unsupported format: " + ext;
                Debug.LogWarning("[SplatSpawnDebugSceneController] " + status, this);
                return;
            }

            status = "Loading " + Path.GetFileName(filePath) + ".";
            Debug.Log("[SplatSpawnDebugSceneController] " + status, this);

            try
            {
                RuntimeSplatFloorLoader.SourceOrientation orientation =
                    RuntimeSplatFloorLoader.ResolveSourceOrientation(sourceKind, floorLoader.looseSplatMirrorAxis);

                RuntimeSplatFloorLoader.LoadResult loadResult;
                SplatSpawnDebugData debugData = new();
                SplatSpawnMetadata metadata;
                byte[] bytes = await Task.Run(() => File.ReadAllBytes(filePath));

                if (ext == ".spz")
                {
                    SplatFloorAnalysisOptions opts = floorLoader.floorAnalysis ?? new SplatFloorAnalysisOptions();
                    opts.positionTransform = orientation.Matrix;
                    var debugResult = await AnalyzeSpzForDebugAsync(bytes, orientation, opts);
                    metadata = debugResult.metadata;
                    debugData = debugResult.debugData;
                    loadResult = await floorLoader.LoadPlacedRuntimeWorldAsync(
                        bytes,
                        Path.GetFileNameWithoutExtension(filePath),
                        Path.GetFileNameWithoutExtension(filePath),
                        gameObjectName: "DebugSplat_" + Path.GetFileNameWithoutExtension(filePath),
                        sourceKind: sourceKind);
                }
                else
                {
                    NativeArray<InputSplatData> splats = await Task.Run(() =>
                    {
                        RuntimePlyReader.ReadFromBytes(bytes, out NativeArray<InputSplatData> parsed);
                        return parsed;
                    });

                    try
                    {
                        SplatFloorAnalysisOptions opts = floorLoader.floorAnalysis ?? new SplatFloorAnalysisOptions();
                        opts.positionTransform = orientation.Matrix;
                        SplatFloorEstimate floorEstimate = SplatFloorAnalyzer.AnalyzeSplats(splats, opts);
                        metadata = SplatSpawnEstimator.EstimateFromSplats(
                            splats,
                            orientation.Matrix,
                            estimatorSettings,
                            filePath,
                            null,
                            floorEstimate,
                            debugData);
                    }
                    finally
                    {
                        // The loader takes ownership, so pass a fresh parsed copy below.
                        if (splats.IsCreated)
                            splats.Dispose();
                    }

                    NativeArray<InputSplatData> loaderSplats = await Task.Run(() =>
                    {
                        RuntimePlyReader.ReadFromBytes(bytes, out NativeArray<InputSplatData> parsed);
                        return parsed;
                    });
                    loadResult = await floorLoader.LoadPlacedRuntimeWorldFromSplatsAsync(
                        loaderSplats,
                        Path.GetFileNameWithoutExtension(filePath),
                        Path.GetFileNameWithoutExtension(filePath),
                        gameObjectName: "DebugSplat_" + Path.GetFileNameWithoutExtension(filePath),
                        sourceKind: sourceKind);
                }

                Vector3 placementOffset = loadResult.floorEstimate != null && loadResult.floorEstimate.success
                    ? loadResult.floorEstimate.recommendedLocalPosition
                    : Vector3.zero;

                if (visualizer != null)
                {
                    visualizer.localSpace = worldParent != null ? worldParent : floorLoader.worldParent;
                    visualizer.SetDebugData(metadata, debugData, placementOffset);
                }

                ApplyCameraSpawnPose(metadata);
                UpdateMarkers(metadata, debugData, placementOffset);

                status = $"Loaded. Lines={debugData.normalLines.Count}, consensus={debugData.hasConsensusPoint}, confidence={metadata.confidence:0.00}.";
                Debug.Log("[SplatSpawnDebugSceneController] " + status, this);
            }
            catch (Exception ex)
            {
                status = "Load failed: " + ex.Message;
                Debug.LogError("[SplatSpawnDebugSceneController] " + ex, this);
            }
        }

        async Task<(SplatSpawnMetadata metadata, SplatSpawnDebugData debugData)> AnalyzeSpzForDebugAsync(
            byte[] bytes,
            RuntimeSplatFloorLoader.SourceOrientation orientation,
            SplatFloorAnalysisOptions opts)
        {
            return await Task.Run(() =>
            {
                NativeArray<InputSplatData> splats = default;
                try
                {
                    SPZFileReader.ReadFile(bytes, out splats);
                    SplatFloorEstimate floorEstimate = SplatFloorAnalyzer.AnalyzeSplats(splats, opts);
                    SplatSpawnDebugData debugData = new();
                    SplatSpawnMetadata metadata = SplatSpawnEstimator.EstimateFromSplats(
                        splats,
                        orientation.Matrix,
                        estimatorSettings,
                        filePath,
                        null,
                        floorEstimate,
                        debugData);
                    return (metadata, debugData);
                }
                finally
                {
                    if (splats.IsCreated)
                        splats.Dispose();
                }
            });
        }

        bool ApplyCameraSpawnPose(SplatSpawnMetadata metadata)
        {
            if (!placeCameraAtEstimatedSpawn || metadata == null || !metadata.hasPose)
                return false;

            Transform target = cameraToPlace;
            if (target == null && Camera.main != null)
                target = Camera.main.transform;
            if (target == null)
                return false;

            target.SetPositionAndRotation(metadata.spawn, metadata.rotation);
            return true;
        }

        public bool ApplyCameraSpawnPoseForTests(SplatSpawnMetadata metadata)
        {
            return ApplyCameraSpawnPose(metadata);
        }

        void UpdateMarkers(SplatSpawnMetadata metadata, SplatSpawnDebugData debugData, Vector3 placementOffset)
        {
            if (!updateMarkersAfterLoad)
                return;

            Matrix4x4 markerSpace = ResolveMarkerSpace();
            SetMarker(originMarker, markerSpace.MultiplyPoint3x4(Vector3.zero), Quaternion.identity, true);

            bool hasConsensus = debugData != null && debugData.hasConsensusPoint;
            SetMarker(
                consensusMarker,
                hasConsensus ? markerSpace.MultiplyPoint3x4(debugData.consensusPoint + placementOffset) : Vector3.zero,
                Quaternion.identity,
                hasConsensus);

            bool hasLongAxisConsensus = debugData != null && debugData.hasLongAxisConsensusPoint;
            SetMarker(
                longAxisConsensusMarker,
                hasLongAxisConsensus ? markerSpace.MultiplyPoint3x4(debugData.longAxisConsensusPoint + placementOffset) : Vector3.zero,
                Quaternion.identity,
                hasLongAxisConsensus);

            bool hasSpawn = metadata != null && metadata.hasPose;
            SetMarker(
                spawnMarker,
                hasSpawn ? markerSpace.MultiplyPoint3x4(metadata.spawn) : Vector3.zero,
                hasSpawn ? ResolveMarkerRotation(metadata.rotation) : Quaternion.identity,
                hasSpawn);

            SetMarker(
                lookAtMarker,
                hasSpawn ? markerSpace.MultiplyPoint3x4(metadata.lookAt) : Vector3.zero,
                Quaternion.identity,
                hasSpawn);
        }

        Matrix4x4 ResolveMarkerSpace()
        {
            Transform space = worldParent;
            if (space == null && floorLoader != null)
                space = floorLoader.worldParent;
            return space != null ? space.localToWorldMatrix : Matrix4x4.identity;
        }

        Quaternion ResolveMarkerRotation(Quaternion localRotation)
        {
            Transform space = worldParent;
            if (space == null && floorLoader != null)
                space = floorLoader.worldParent;
            return space != null ? space.rotation * localRotation : localRotation;
        }

        static void SetMarker(Transform marker, Vector3 position, Quaternion rotation, bool active)
        {
            if (marker == null)
                return;

            if (marker.gameObject.activeSelf != active)
                marker.gameObject.SetActive(active);
            if (!active)
                return;

            marker.SetPositionAndRotation(position, rotation);
        }

        [ContextMenu("Move Camera To Origin Marker")]
        public void MoveCameraToOriginMarker()
        {
            MoveCameraToMarker(originMarker, useMarkerRotation: false);
        }

        [ContextMenu("Move Camera To Consensus Marker")]
        public void MoveCameraToConsensusMarker()
        {
            MoveCameraToMarker(consensusMarker, useMarkerRotation: false);
        }

        [ContextMenu("Move Camera To Long-Axis Consensus Marker")]
        public void MoveCameraToLongAxisConsensusMarker()
        {
            MoveCameraToMarker(longAxisConsensusMarker, useMarkerRotation: false);
        }

        [ContextMenu("Move Camera To Spawn Marker")]
        public void MoveCameraToSpawnMarker()
        {
            MoveCameraToMarker(spawnMarker, useMarkerRotation: true);
        }

        [ContextMenu("Move Camera To Look-At Marker")]
        public void MoveCameraToLookAtMarker()
        {
            MoveCameraToMarker(lookAtMarker, useMarkerRotation: false);
        }

        bool MoveCameraToMarker(Transform marker, bool useMarkerRotation)
        {
            Transform target = cameraToPlace;
            if (target == null && Camera.main != null)
                target = Camera.main.transform;
            if (target == null || marker == null || !marker.gameObject.activeInHierarchy)
                return false;

            if (useMarkerRotation)
                target.SetPositionAndRotation(marker.position, marker.rotation);
            else
                target.position = marker.position;

            return true;
        }

        [ContextMenu("Move Camera To Next Marker")]
        public void MoveCameraToNextMarker()
        {
            TryMoveCameraToNextMarker();
        }

        bool TryMoveCameraToNextMarker()
        {
            var markers = new (Transform marker, bool useRotation)[]
            {
                (originMarker, false),
                (consensusMarker, false),
                (longAxisConsensusMarker, false),
                (spawnMarker, true),
                (lookAtMarker, false)
            };

            for (int i = 0; i < markers.Length; i++)
            {
                int index = PositiveMod(_nextMarkerIndex + i, markers.Length);
                _nextMarkerIndex = PositiveMod(index + 1, markers.Length);
                Transform marker = markers[index].marker;
                if (marker == null || !marker.gameObject.activeInHierarchy)
                    continue;

                return MoveCameraToMarker(marker, markers[index].useRotation);
            }

            return false;
        }

        static int PositiveMod(int value, int modulus)
        {
            if (modulus <= 0)
                return 0;
            int result = value % modulus;
            return result < 0 ? result + modulus : result;
        }

        public void UpdateMarkersForTests(SplatSpawnMetadata metadata, SplatSpawnDebugData debugData, Vector3 placementOffset)
        {
            UpdateMarkers(metadata, debugData, placementOffset);
        }

        public bool MoveCameraToNextMarkerForTests()
        {
            return TryMoveCameraToNextMarker();
        }
    }
}
