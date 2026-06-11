using System;
using System.Collections.Generic;
using System.Reflection;
using GaussianSplatting.Runtime;
using Holodeck.Save;
using SpeechIntent;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using WorldLabs.Runtime.Tools;

namespace HeadsetHolodeck.EditorTests
{
    public static class SplatSpawnAndOrientationBatchTests
    {
        public static void RunAll()
        {
            try
            {
                TestLooseSplatOrientationUsesPositiveNinetyXAndSelectedMirror();
                TestWorldLabsOrientationUsesNegativeOneEightyXWithoutMirror();
                TestWorldConfigRestorerResolvesCachedSplatSourceKind();
                TestSplatSpawnEstimatorPlacesPlayerAtFloorEyeHeight();
                TestSplatSpawnEstimatorChoosesCaptureCenterForCrescentScene();
                TestSplatSpawnEstimatorCapsRadialOpenSideBias();
                TestSplatSpawnEstimatorFallbackPrefersDenseCoreOverSparseTail();
                TestSplatSpawnEstimatorSamplesAcrossFileWithPermutation();
                TestSplatSpawnEstimatorDebugLinesIncludeSparseSpatialRegions();
                TestSplatSpawnEstimatorLongAxisConsensusFindsCenter();
                TestSplatSpawnEstimatorRefinesSpawnYFromLocalFloor();
                TestSplatSpawnEstimatorHonorsMaxSamples();
                TestSplatSpawnEstimatorCollectsNormalLineDebugData();
                TestRuntimeSplatFloorLoaderAttachesEstimatorSpawnPose();
                TestDebugSceneControllerAppliesCameraSpawnPose();
                TestDebugSceneControllerUpdatesMarkersAndMovesCamera();
                TestDebugSceneControllerCyclesCameraThroughMarkers();
                TestPlayerOriginUsesRendererSpawnPoseWhenAvailable();
                TestSplatSpawnPoseStoresLocalPoseForTransformedWorld();
                Debug.Log("[SplatSpawnAndOrientationBatchTests] All tests passed.");
            }
            catch (Exception ex)
            {
                Debug.LogError("[SplatSpawnAndOrientationBatchTests] Tests failed: " + ex);
                EditorApplication.Exit(1);
                throw;
            }
        }

        static void TestLooseSplatOrientationUsesPositiveNinetyXAndSelectedMirror()
        {
            Type sourceType = typeof(RuntimeSplatFloorLoader).GetNestedType("SplatSourceKind");
            Type mirrorType = typeof(RuntimeSplatFloorLoader).GetNestedType("MirrorAxis");
            AssertTrue(sourceType != null, "RuntimeSplatFloorLoader should define SplatSourceKind.");
            AssertTrue(mirrorType != null, "RuntimeSplatFloorLoader should define MirrorAxis.");

            object loose = Enum.Parse(sourceType, "LooseSplat");
            MethodInfo resolve = ResolveOrientationMethod();
            object result = resolve.Invoke(null, new[] { loose, Enum.Parse(mirrorType, "X") });

            Quaternion rotation = ReadField<Quaternion>(result, "rotation");
            Vector3 scale = ReadField<Vector3>(result, "scale");

            AssertApproximately(0f, Quaternion.Angle(Quaternion.Euler(90f, 0f, 0f), rotation), 0.001f, "Loose splat rotation should be +90 X.");
            AssertVectorApproximately(new Vector3(-1f, 1f, 1f), scale, 0.0001f, "Loose splat should apply the selected mirror axis.");
        }

        static void TestWorldLabsOrientationUsesNegativeOneEightyXWithoutMirror()
        {
            Type sourceType = typeof(RuntimeSplatFloorLoader).GetNestedType("SplatSourceKind");
            Type mirrorType = typeof(RuntimeSplatFloorLoader).GetNestedType("MirrorAxis");
            object worldLabs = Enum.Parse(sourceType, "WorldLabs");
            MethodInfo resolve = ResolveOrientationMethod();
            object result = resolve.Invoke(null, new[] { worldLabs, Enum.Parse(mirrorType, "Z") });

            Quaternion rotation = ReadField<Quaternion>(result, "rotation");
            Vector3 scale = ReadField<Vector3>(result, "scale");

            AssertApproximately(0f, Quaternion.Angle(Quaternion.Euler(-180f, 0f, 0f), rotation), 0.001f, "WorldLabs splat rotation should be -180 X.");
            AssertVectorApproximately(Vector3.one, scale, 0.0001f, "WorldLabs splat should not be mirrored.");
        }

        static void TestWorldConfigRestorerResolvesCachedSplatSourceKind()
        {
            MethodInfo method = typeof(WorldConfigRestorer).GetMethod(
                "ResolveSplatSourceKindForWorldSource",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            AssertTrue(method != null, "WorldConfigRestorer should resolve source kind before loading cached splats.");

            object worldLabsKind = method.Invoke(null, new object[]
            {
                new WorldSourceData { type = "worldlabs", cached_splat = "../CachedWorlds/world.spz" }
            });
            object localKind = method.Invoke(null, new object[]
            {
                new WorldSourceData { type = "local_splat", cached_splat = "../CachedWorlds/local.spz" }
            });

            AssertTrue(worldLabsKind.Equals(RuntimeSplatFloorLoader.SplatSourceKind.WorldLabs),
                "Cached WorldLabs splats should keep WorldLabs orientation.");
            AssertTrue(localKind.Equals(RuntimeSplatFloorLoader.SplatSourceKind.LooseSplat),
                "Cached local_splat files should use loose splat orientation.");
        }

        static void TestPlayerOriginUsesRendererSpawnPoseWhenAvailable()
        {
            GameObject playerGo = new GameObject("Player_Test");
            GameObject anchorGo = new GameObject("Teleport Anchor");
            GameObject rendererGo = new GameObject("Renderer_Test");
            GameObject controllerGo = new GameObject("PlayerOriginController_Test");

            try
            {
                playerGo.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                anchorGo.transform.SetPositionAndRotation(new Vector3(10f, 0f, 0f), Quaternion.Euler(0f, 30f, 0f));

                GaussianSplatRenderer renderer = rendererGo.AddComponent<GaussianSplatRenderer>();
                Type poseType = Type.GetType("WorldLabs.Runtime.Tools.SplatSpawnPose, Assembly-CSharp");
                AssertTrue(poseType != null, "SplatSpawnPose type should exist.");
                Component pose = rendererGo.AddComponent(poseType);
                poseType.GetField("hasPose").SetValue(pose, true);
                poseType.GetField("playerPosition").SetValue(pose, new Vector3(1f, 2f, 3f));
                poseType.GetField("playerRotation").SetValue(pose, Quaternion.Euler(0f, 90f, 0f));

                PlayerOriginController controller = controllerGo.AddComponent<PlayerOriginController>();
                controller.playerRoot = playerGo.transform;
                controller.resetAnchor = anchorGo.transform;
                controller.matchAnchorRotation = true;

                MethodInfo onWorldLoaded = typeof(PlayerOriginController).GetMethod("OnWorldLoaded", BindingFlags.Instance | BindingFlags.NonPublic);
                AssertTrue(onWorldLoaded != null, "PlayerOriginController should have OnWorldLoaded method.");
                onWorldLoaded.Invoke(controller, new object[] { "test_world", renderer });

                AssertVectorApproximately(new Vector3(1f, 2f, 3f), playerGo.transform.position, 0.0001f, "Player should move to splat spawn pose.");
                AssertApproximately(0f, Quaternion.Angle(Quaternion.Euler(0f, 90f, 0f), playerGo.transform.rotation), 0.001f, "Player should rotate to splat spawn pose.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(controllerGo);
                UnityEngine.Object.DestroyImmediate(rendererGo);
                UnityEngine.Object.DestroyImmediate(anchorGo);
                UnityEngine.Object.DestroyImmediate(playerGo);
            }
        }

        static void TestSplatSpawnPoseStoresLocalPoseForTransformedWorld()
        {
            GameObject worldObject = new GameObject("World_LocalSpawnPose_Test");
            try
            {
                worldObject.transform.SetPositionAndRotation(new Vector3(10f, 0f, 0f), Quaternion.Euler(0f, 90f, 0f));
                worldObject.transform.localScale = new Vector3(2f, 2f, 2f);

                var pose = worldObject.AddComponent<SplatSpawnPose>();
                Vector3 originalWorldPosition = new Vector3(12f, 2f, 0f);
                Quaternion originalWorldRotation = Quaternion.Euler(0f, 120f, 0f);
                pose.SetWorldPose(worldObject.transform, originalWorldPosition, originalWorldRotation, new Vector3(12f, 1.4f, 2f));

                worldObject.transform.SetPositionAndRotation(new Vector3(0f, 0f, 10f), Quaternion.Euler(0f, 180f, 0f));
                worldObject.transform.localScale = Vector3.one;

                AssertTrue(pose.TryGetWorldPose(worldObject.transform, out Vector3 transformedPosition, out Quaternion transformedRotation, out _),
                    "Expected local spawn pose to project back into world space.");
                AssertVectorApproximately(worldObject.transform.TransformPoint(pose.localPlayerPosition), transformedPosition, 0.0001f,
                    "Expected transformed position to come from stored local position.");
                AssertApproximately(0f, Quaternion.Angle(worldObject.transform.rotation * pose.localPlayerRotation, transformedRotation), 0.001f,
                    "Expected transformed rotation to come from stored local rotation.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(worldObject);
            }
        }

        static void TestSplatSpawnEstimatorPlacesPlayerAtFloorEyeHeight()
        {
            NativeArray<InputSplatData> splats = CreateSyntheticRoomSplats(20, Allocator.Persistent);
            try
            {
                var settings = new SplatSpawnEstimatorSettings
                {
                    maxSamples = 1000,
                    defaultEyeHeightMeters = 1.6f,
                    candidateRingCount = 0
                };

                SplatSpawnMetadata metadata = SplatSpawnEstimator.EstimateFromSplats(
                    splats,
                    Matrix4x4.identity,
                    settings,
                    "synthetic_room.spz",
                    null);

                AssertTrue(metadata != null, "Expected spawn metadata.");
                AssertTrue(metadata.hasPose, "Expected estimator to produce a usable pose.");
                AssertApproximately(1.6f, metadata.spawn.y, 0.05f, "Expected spawn height to be floor plus eye height.");
                AssertVectorApproximately(new Vector3(0f, 1.4f, 0f), metadata.lookAt, 0.5f, "Expected look target near room center.");
                AssertTrue(metadata.confidence > 0.25f, "Expected useful confidence for synthetic room.");
            }
            finally
            {
                splats.Dispose();
            }
        }

        static void TestSplatSpawnEstimatorChoosesCaptureCenterForCrescentScene()
        {
            NativeArray<InputSplatData> splats = CreateSyntheticCrescentSplats(Allocator.Persistent);
            try
            {
                var settings = new SplatSpawnEstimatorSettings
                {
                    maxSamples = 2000,
                    defaultEyeHeightMeters = 1.6f,
                    candidateRingCount = 0
                };

                SplatSpawnMetadata metadata = SplatSpawnEstimator.EstimateFromSplats(
                    splats,
                    Matrix4x4.identity,
                    settings);

                AssertTrue(metadata != null && metadata.hasPose, "Expected crescent scene to produce a pose.");
                AssertTrue(new Vector2(metadata.spawn.x, metadata.spawn.z).magnitude < 0.5f, $"Expected crescent scene spawn near capture center. Actual spawn={metadata.spawn}.");
                AssertTrue(Vector3.Dot(metadata.rotation * Vector3.forward, Vector3.forward) > 0.65f, "Expected crescent scene spawn to look outward toward the arc.");
            }
            finally
            {
                splats.Dispose();
            }
        }

        static void TestSplatSpawnEstimatorCapsRadialOpenSideBias()
        {
            MethodInfo method = typeof(SplatSpawnEstimator).GetMethod(
                "BiasCenterTowardOpenSide",
                BindingFlags.Static | BindingFlags.NonPublic);
            AssertTrue(method != null, "Expected BiasCenterTowardOpenSide helper.");

            var positions = new List<Vector3>
            {
                new Vector3(100f, 0f, 0f),
                new Vector3(101f, 0f, 1f),
                new Vector3(99f, 0f, -1f),
            };
            var settings = new SplatSpawnEstimatorSettings
            {
                radialCaptureOpenSideOffsetFraction = 0.35f,
                radialCaptureMaxOpenSideOffsetMeters = 2f
            };

            object result = method.Invoke(null, new object[]
            {
                positions,
                Vector2.zero,
                1000f,
                settings
            });

            Vector2 biased = (Vector2)result;
            AssertApproximately(-2f, biased.x, 0.001f, "Expected radial open-side bias to cap by meters.");
            AssertApproximately(0f, biased.y, 0.001f, "Expected radial open-side bias to preserve direction.");
        }

        static void TestSplatSpawnEstimatorFallbackPrefersDenseCoreOverSparseTail()
        {
            NativeArray<InputSplatData> splats = CreateDenseCoreWithSparseTailSplats(Allocator.Persistent);
            try
            {
                var settings = new SplatSpawnEstimatorSettings
                {
                    maxSamples = 2000,
                    defaultEyeHeightMeters = 1.6f,
                    useRadialCaptureCenter = false,
                    candidateRingCount = 2,
                    candidateRingRadiusFractionOfDiagonal = 0.08f
                };

                SplatSpawnMetadata metadata = SplatSpawnEstimator.EstimateFromSplats(
                    splats,
                    Matrix4x4.identity,
                    settings);

                AssertTrue(metadata != null && metadata.hasPose, "Expected dense-core sparse-tail scene to produce a pose.");
                Vector2 horizontal = new Vector2(metadata.spawn.x, metadata.spawn.z);
                AssertTrue(horizontal.magnitude < 1.25f, $"Expected fallback spawn near dense core, not sparse tail. Actual spawn={metadata.spawn}.");
            }
            finally
            {
                splats.Dispose();
            }
        }


        static void TestSplatSpawnEstimatorSamplesAcrossFileWithPermutation()
        {
            MethodInfo method = typeof(SplatSpawnEstimator).GetMethod(
                "DeterministicIndex",
                BindingFlags.Static | BindingFlags.NonPublic);
            AssertTrue(method != null, "Expected DeterministicIndex helper.");

            const int totalCount = 10000;
            const int sampleCount = 128;
            int previous = -1;
            int directionChanges = 0;
            int previousDirection = 0;
            bool[] visitedQuartiles = new bool[4];

            for (int i = 0; i < sampleCount; i++)
            {
                int index = (int)method.Invoke(null, new object[] { i, totalCount, sampleCount, 12345 });
                AssertTrue(index >= 0 && index < totalCount, "Expected sample index within source range.");
                visitedQuartiles[Mathf.Min(3, index * 4 / totalCount)] = true;

                if (previous >= 0)
                {
                    int direction = Math.Sign(index - previous);
                    if (previousDirection != 0 && direction != 0 && direction != previousDirection)
                        directionChanges++;
                    if (direction != 0)
                        previousDirection = direction;
                }

                previous = index;
            }

            for (int i = 0; i < visitedQuartiles.Length; i++)
                AssertTrue(visitedQuartiles[i], "Expected sampler to visit every quartile of the source file.");
            AssertTrue(directionChanges > 8, "Expected sampler to use a permutation-like order, not a mostly monotonic stride.");
        }

        static void TestSplatSpawnEstimatorDebugLinesIncludeSparseSpatialRegions()
        {
            NativeArray<InputSplatData> splats = CreateClusteredEligibleNormalSplats(Allocator.Persistent);
            try
            {
                var settings = new SplatSpawnEstimatorSettings
                {
                    maxSamples = splats.Length,
                    maxDebugNormalLines = 20,
                    minFlatnessRatio = 1.1f
                };
                var debug = new SplatSpawnDebugData();

                SplatSpawnEstimator.EstimateFromSplats(
                    splats,
                    Matrix4x4.identity,
                    settings,
                    debugData: debug);

                bool includesSparseRegion = false;
                for (int i = 0; i < debug.normalLines.Count; i++)
                {
                    if (debug.normalLines[i].point.x > 5f)
                    {
                        includesSparseRegion = true;
                        break;
                    }
                }

                AssertTrue(includesSparseRegion, "Expected debug normal lines to include sparse spatial regions, not only the dense cluster.");
            }
            finally
            {
                splats.Dispose();
            }
        }

        static void TestSplatSpawnEstimatorLongAxisConsensusFindsCenter()
        {
            NativeArray<InputSplatData> splats = CreateTangentLongAxisRingSplats(Allocator.Persistent);
            try
            {
                var settings = new SplatSpawnEstimatorSettings
                {
                    maxSamples = splats.Length,
                    maxDebugNormalLines = 64,
                    minLongAxisRatio = 1.5f
                };
                var debug = new SplatSpawnDebugData();

                SplatSpawnEstimator.EstimateFromSplats(
                    splats,
                    Matrix4x4.identity,
                    settings,
                    debugData: debug);

                AssertTrue(debug.longAxisNormalLines.Count > 0, "Expected long-axis debug lines.");
                AssertTrue(debug.hasLongAxisConsensusPoint, "Expected long-axis consensus point.");
                AssertVectorApproximately(new Vector3(0f, settings.defaultEyeHeightMeters, 0f), debug.longAxisConsensusPoint, 0.2f, "Expected tangent long-axis normals to converge near ring center at eye height.");
            }
            finally
            {
                splats.Dispose();
            }
        }

        static void TestSplatSpawnEstimatorRefinesSpawnYFromLocalFloor()
        {
            NativeArray<InputSplatData> splats = CreateLocalFloorWithUpperGeometrySplats(Allocator.Persistent);
            try
            {
                var settings = new SplatSpawnEstimatorSettings
                {
                    maxSamples = splats.Length,
                    defaultEyeHeightMeters = 1.6f,
                    candidateRingCount = 0,
                    useRadialCaptureCenter = false,
                    minLocalFloorSamples = 16
                };
                var misleadingFloor = new SplatFloorEstimate
                {
                    success = true,
                    estimatedFloorY = 4f,
                    recommendedLocalPosition = Vector3.zero
                };

                SplatSpawnMetadata metadata = SplatSpawnEstimator.EstimateFromSplats(
                    splats,
                    Matrix4x4.identity,
                    settings,
                    floorEstimate: misleadingFloor);

                AssertTrue(metadata != null && metadata.hasPose, "Expected local-floor scene to produce a pose.");
                AssertApproximately(1.6f, metadata.spawn.y, 0.08f, "Expected local dense floor under spawn to override a misleading global floor estimate.");
                AssertTrue(metadata.method.Contains("local_floor_y_v1"), "Expected spawn method to report local floor refinement.");
            }
            finally
            {
                splats.Dispose();
            }
        }

        static void TestSplatSpawnEstimatorHonorsMaxSamples()
        {
            NativeArray<InputSplatData> splats = CreateSyntheticRoomSplats(30, Allocator.Persistent);
            try
            {
                var settings = new SplatSpawnEstimatorSettings
                {
                    maxSamples = 37,
                    defaultEyeHeightMeters = 1.6f
                };

                SplatSpawnMetadata metadata = SplatSpawnEstimator.EstimateFromSplats(
                    splats,
                    Matrix4x4.identity,
                    settings);

                AssertTrue(metadata != null, "Expected spawn metadata.");
                AssertEqual(37, metadata.sampledSplats, "Expected estimator to honor maxSamples.");
                AssertEqual(splats.Length, metadata.totalSplats, "Expected metadata to report total splat count.");
            }
            finally
            {
                splats.Dispose();
            }
        }

        static void TestSplatSpawnEstimatorCollectsNormalLineDebugData()
        {
            NativeArray<InputSplatData> splats = CreateSyntheticRoomSplats(20, Allocator.Persistent);
            try
            {
                var settings = new SplatSpawnEstimatorSettings
                {
                    maxSamples = 1000,
                    maxDebugNormalLines = 24,
                    minFlatnessRatio = 1.25f
                };

                var debug = new SplatSpawnDebugData();
                SplatSpawnMetadata metadata = SplatSpawnEstimator.EstimateFromSplats(
                    splats,
                    Matrix4x4.identity,
                    settings,
                    debugData: debug);

                AssertTrue(metadata != null && metadata.hasPose, "Expected metadata pose from debug estimation.");
                AssertTrue(debug.normalLines.Count > 0, "Expected debug normal lines.");
                AssertTrue(debug.normalLines.Count <= 24, "Expected debug lines to honor maxDebugNormalLines.");
                AssertTrue(debug.hasConsensusPoint, "Expected normal-line consensus point.");
                AssertTrue(debug.consensusConfidence > 0f, "Expected positive consensus confidence.");
            }
            finally
            {
                splats.Dispose();
            }
        }

        static NativeArray<InputSplatData> CreateSyntheticRoomSplats(int cellsPerAxis, Allocator allocator)
        {
            int cellCount = cellsPerAxis * cellsPerAxis;
            int count = cellCount + cellCount / 2;
            var splats = new NativeArray<InputSplatData>(count, allocator);

            int index = 0;
            for (int x = 0; x < cellsPerAxis; x++)
            {
                for (int z = 0; z < cellsPerAxis; z++)
                {
                    float px = Mathf.Lerp(-2f, 2f, x / (float)(cellsPerAxis - 1));
                    float pz = Mathf.Lerp(-2f, 2f, z / (float)(cellsPerAxis - 1));
                    splats[index++] = new InputSplatData
                    {
                        pos = new Vector3(px, 0f, pz),
                        opacity = 1f,
                        scale = new Vector3(0.08f, 0.01f, 0.08f),
                        rot = Quaternion.identity
                    };
                }
            }

            for (; index < count; index++)
            {
                float t = (index - cellCount) / (float)Mathf.Max(1, count - cellCount - 1);
                splats[index] = new InputSplatData
                {
                    pos = new Vector3(-2f, Mathf.Lerp(0f, 2.2f, t), Mathf.Lerp(-2f, 2f, t)),
                    opacity = 0.8f,
                    scale = new Vector3(0.01f, 0.08f, 0.08f),
                    rot = Quaternion.identity
                };
            }

            return splats;
        }

        static NativeArray<InputSplatData> CreateClusteredEligibleNormalSplats(Allocator allocator)
        {
            const int denseCount = 1000;
            const int sparseCount = 10;
            var splats = new NativeArray<InputSplatData>(denseCount + sparseCount, allocator);

            for (int i = 0; i < denseCount; i++)
            {
                int x = i % 50;
                int z = i / 50;
                splats[i] = new InputSplatData
                {
                    pos = new Vector3(x * 0.01f, 0f, z * 0.01f),
                    opacity = 1f,
                    scale = new Vector3(0.01f, 0.08f, 0.08f),
                    rot = Quaternion.identity
                };
            }

            for (int i = 0; i < sparseCount; i++)
            {
                splats[denseCount + i] = new InputSplatData
                {
                    pos = new Vector3(10f + i * 0.05f, 0f, 10f),
                    opacity = 1f,
                    scale = new Vector3(0.01f, 0.08f, 0.08f),
                    rot = Quaternion.identity
                };
            }

            return splats;
        }

        static NativeArray<InputSplatData> CreateTangentLongAxisRingSplats(Allocator allocator)
        {
            const int count = 72;
            var splats = new NativeArray<InputSplatData>(count, allocator);

            for (int i = 0; i < count; i++)
            {
                float angle = Mathf.PI * 2f * i / count;
                Vector3 radial = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                Vector3 tangent = new Vector3(-radial.z, 0f, radial.x);
                splats[i] = new InputSplatData
                {
                    pos = radial * 3f,
                    opacity = 1f,
                    scale = new Vector3(0.24f, 0.04f, 0.04f),
                    rot = Quaternion.FromToRotation(Vector3.right, tangent)
                };
            }

            return splats;
        }

        static NativeArray<InputSplatData> CreateDenseCoreWithSparseTailSplats(Allocator allocator)
        {
            const int coreSide = 32;
            const int tailCount = 120;
            var splats = new NativeArray<InputSplatData>(coreSide * coreSide + tailCount, allocator);

            int index = 0;
            for (int x = 0; x < coreSide; x++)
            {
                for (int z = 0; z < coreSide; z++)
                {
                    float fx = (x / (float)(coreSide - 1) - 0.5f) * 1.2f;
                    float fz = (z / (float)(coreSide - 1) - 0.5f) * 1.2f;
                    splats[index++] = new InputSplatData
                    {
                        pos = new Vector3(fx, 0f, fz),
                        opacity = 1f,
                        scale = new Vector3(0.03f, 0.08f, 0.03f),
                        rot = Quaternion.identity
                    };
                }
            }

            for (int i = 0; i < tailCount; i++)
            {
                float t = i / (float)(tailCount - 1);
                splats[index++] = new InputSplatData
                {
                    pos = new Vector3(Mathf.Sin(t * Mathf.PI * 4f) * 0.25f, 0f, Mathf.Lerp(-1.5f, -9f, t)),
                    opacity = 1f,
                    scale = new Vector3(0.03f, 0.08f, 0.03f),
                    rot = Quaternion.identity
                };
            }

            return splats;
        }

        static NativeArray<InputSplatData> CreateLocalFloorWithUpperGeometrySplats(Allocator allocator)
        {
            const int floorSide = 8;
            const int upperSide = 8;
            int count = floorSide * floorSide + upperSide * upperSide;
            var splats = new NativeArray<InputSplatData>(count, allocator);

            int index = 0;
            for (int x = 0; x < floorSide; x++)
            {
                for (int z = 0; z < floorSide; z++)
                {
                    float px = Mathf.Lerp(-0.45f, 0.45f, x / (float)(floorSide - 1));
                    float pz = Mathf.Lerp(-0.45f, 0.45f, z / (float)(floorSide - 1));
                    splats[index++] = new InputSplatData
                    {
                        pos = new Vector3(px, 0f, pz),
                        opacity = 1f,
                        scale = new Vector3(0.08f, 0.01f, 0.08f),
                        rot = Quaternion.identity
                    };
                }
            }

            for (int x = 0; x < upperSide; x++)
            {
                for (int z = 0; z < upperSide; z++)
                {
                    float px = Mathf.Lerp(-0.5f, 0.5f, x / (float)(upperSide - 1));
                    float pz = Mathf.Lerp(-0.5f, 0.5f, z / (float)(upperSide - 1));
                    splats[index++] = new InputSplatData
                    {
                        pos = new Vector3(px, 5.5f, pz),
                        opacity = 1f,
                        scale = new Vector3(0.08f, 0.01f, 0.08f),
                        rot = Quaternion.identity
                    };
                }
            }

            return splats;
        }

        static NativeArray<InputSplatData> CreateSyntheticCrescentSplats(Allocator allocator)
        {
            const int arcSteps = 64;
            const int heightSteps = 8;
            var splats = new NativeArray<InputSplatData>(arcSteps * heightSteps, allocator);

            int index = 0;
            for (int a = 0; a < arcSteps; a++)
            {
                float t = a / (float)(arcSteps - 1);
                float angle = Mathf.Lerp(35f, 145f, t) * Mathf.Deg2Rad;
                float radius = Mathf.Lerp(2.2f, 2.8f, Mathf.Sin(t * Mathf.PI));
                float x = Mathf.Cos(angle) * radius;
                float z = Mathf.Sin(angle) * radius;

                for (int y = 0; y < heightSteps; y++)
                {
                    float py = y / (float)(heightSteps - 1) * 2.2f;
                    splats[index++] = new InputSplatData
                    {
                        pos = new Vector3(x, py, z),
                        opacity = 1f,
                        scale = new Vector3(0.03f, 0.08f, 0.03f),
                        rot = Quaternion.identity
                    };
                }
            }

            return splats;
        }

        static void TestRuntimeSplatFloorLoaderAttachesEstimatorSpawnPose()
        {
            GameObject loaderObject = new GameObject("RuntimeSplatFloorLoader_Test");
            GameObject worldObject = new GameObject("World_Test");

            try
            {
                RuntimeSplatFloorLoader loader = loaderObject.AddComponent<RuntimeSplatFloorLoader>();
                var metadata = new SplatSpawnMetadata
                {
                    hasPose = true,
                    method = "floor_percentile_candidate_v1",
                    spawn = new Vector3(1f, 1.6f, 2f),
                    rotation = Quaternion.Euler(0f, 45f, 0f),
                    lookAt = new Vector3(1f, 1.4f, 3f),
                    confidence = 0.72f
                };

                MethodInfo attach = typeof(RuntimeSplatFloorLoader).GetMethod(
                    "AttachSpawnPose",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                AssertTrue(attach != null, "Expected RuntimeSplatFloorLoader to expose private AttachSpawnPose.");

                attach.Invoke(loader, new object[] { worldObject, null, metadata });

                SplatSpawnPose pose = worldObject.GetComponent<SplatSpawnPose>();
                AssertTrue(pose != null, "Expected loader to attach SplatSpawnPose.");
                AssertTrue(pose.hasPose, "Expected attached pose to be active.");
                AssertEqual("floor_percentile_candidate_v1", pose.method, "Expected attached pose to use estimator method.");
                AssertVectorApproximately(metadata.spawn, pose.playerPosition, 0.0001f, "Expected attached spawn to match metadata.");
                AssertApproximately(0f, Quaternion.Angle(metadata.rotation, pose.playerRotation), 0.001f, "Expected attached rotation to match metadata.");
                AssertApproximately(0.72f, pose.confidence, 0.0001f, "Expected attached confidence to match metadata.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(worldObject);
                UnityEngine.Object.DestroyImmediate(loaderObject);
            }
        }

        static void TestDebugSceneControllerAppliesCameraSpawnPose()
        {
            GameObject controllerObject = new GameObject("SplatSpawnDebugSceneController_Test");
            GameObject cameraObject = new GameObject("Camera_Test");

            try
            {
                var controller = controllerObject.AddComponent<SplatSpawnDebugSceneController>();
                controller.cameraToPlace = cameraObject.transform;

                var metadata = new SplatSpawnMetadata
                {
                    hasPose = true,
                    spawn = new Vector3(2f, 1.6f, 3f),
                    rotation = Quaternion.Euler(0f, 35f, 0f)
                };

                bool applied = controller.ApplyCameraSpawnPoseForTests(metadata);

                AssertTrue(applied, "Expected debug controller to apply camera spawn pose.");
                AssertVectorApproximately(metadata.spawn, cameraObject.transform.position, 0.0001f, "Expected camera position to match spawn.");
                AssertApproximately(0f, Quaternion.Angle(metadata.rotation, cameraObject.transform.rotation), 0.001f, "Expected camera rotation to match spawn.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(cameraObject);
                UnityEngine.Object.DestroyImmediate(controllerObject);
            }
        }

        static void TestDebugSceneControllerUpdatesMarkersAndMovesCamera()
        {
            GameObject controllerObject = new GameObject("SplatSpawnDebugSceneController_Markers_Test");
            GameObject cameraObject = new GameObject("Camera_Markers_Test");
            GameObject originObject = new GameObject("Origin Marker_Test");
            GameObject consensusObject = new GameObject("Consensus Marker_Test");
            GameObject spawnObject = new GameObject("Spawn Marker_Test");
            GameObject lookAtObject = new GameObject("LookAt Marker_Test");

            try
            {
                var controller = controllerObject.AddComponent<SplatSpawnDebugSceneController>();
                controller.cameraToPlace = cameraObject.transform;
                controller.originMarker = originObject.transform;
                controller.consensusMarker = consensusObject.transform;
                controller.spawnMarker = spawnObject.transform;
                controller.lookAtMarker = lookAtObject.transform;

                var metadata = new SplatSpawnMetadata
                {
                    hasPose = true,
                    spawn = new Vector3(2f, 1.6f, 3f),
                    rotation = Quaternion.Euler(0f, 55f, 0f),
                    lookAt = new Vector3(1f, 1.3f, 4f)
                };
                var debugData = new SplatSpawnDebugData
                {
                    hasConsensusPoint = true,
                    consensusPoint = new Vector3(0.5f, 1.2f, 2.5f)
                };

                controller.UpdateMarkersForTests(metadata, debugData, Vector3.zero);

                AssertVectorApproximately(Vector3.zero, originObject.transform.position, 0.0001f, "Expected origin marker at world origin.");
                AssertVectorApproximately(debugData.consensusPoint, consensusObject.transform.position, 0.0001f, "Expected consensus marker at consensus point.");
                AssertVectorApproximately(metadata.spawn, spawnObject.transform.position, 0.0001f, "Expected spawn marker at estimated spawn.");
                AssertVectorApproximately(metadata.lookAt, lookAtObject.transform.position, 0.0001f, "Expected look-at marker at estimated look target.");

                controller.MoveCameraToSpawnMarker();

                AssertVectorApproximately(metadata.spawn, cameraObject.transform.position, 0.0001f, "Expected camera to move to spawn marker.");
                AssertApproximately(0f, Quaternion.Angle(metadata.rotation, cameraObject.transform.rotation), 0.001f, "Expected camera rotation to match spawn marker.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(lookAtObject);
                UnityEngine.Object.DestroyImmediate(spawnObject);
                UnityEngine.Object.DestroyImmediate(consensusObject);
                UnityEngine.Object.DestroyImmediate(originObject);
                UnityEngine.Object.DestroyImmediate(cameraObject);
                UnityEngine.Object.DestroyImmediate(controllerObject);
            }
        }

        static void TestDebugSceneControllerCyclesCameraThroughMarkers()
        {
            GameObject controllerObject = new GameObject("SplatSpawnDebugSceneController_Cycle_Test");
            GameObject cameraObject = new GameObject("Camera_Cycle_Test");
            GameObject originObject = new GameObject("Origin Marker_Cycle_Test");
            GameObject consensusObject = new GameObject("Consensus Marker_Cycle_Test");
            GameObject longAxisObject = new GameObject("LongAxis Consensus Marker_Cycle_Test");
            GameObject spawnObject = new GameObject("Spawn Marker_Cycle_Test");
            GameObject lookAtObject = new GameObject("LookAt Marker_Cycle_Test");

            try
            {
                var controller = controllerObject.AddComponent<SplatSpawnDebugSceneController>();
                controller.cameraToPlace = cameraObject.transform;
                controller.originMarker = originObject.transform;
                controller.consensusMarker = consensusObject.transform;
                controller.longAxisConsensusMarker = longAxisObject.transform;
                controller.spawnMarker = spawnObject.transform;
                controller.lookAtMarker = lookAtObject.transform;

                originObject.transform.position = new Vector3(1f, 0f, 0f);
                consensusObject.transform.position = new Vector3(2f, 0f, 0f);
                longAxisObject.transform.position = new Vector3(3f, 0f, 0f);
                spawnObject.transform.SetPositionAndRotation(new Vector3(4f, 0f, 0f), Quaternion.Euler(0f, 90f, 0f));
                lookAtObject.transform.position = new Vector3(5f, 0f, 0f);

                AssertTrue(controller.MoveCameraToNextMarkerForTests(), "Expected first marker cycle to move camera.");
                AssertVectorApproximately(originObject.transform.position, cameraObject.transform.position, 0.0001f, "Expected first cycle target to be origin marker.");
                AssertTrue(controller.MoveCameraToNextMarkerForTests(), "Expected second marker cycle to move camera.");
                AssertVectorApproximately(consensusObject.transform.position, cameraObject.transform.position, 0.0001f, "Expected second cycle target to be consensus marker.");
                AssertTrue(controller.MoveCameraToNextMarkerForTests(), "Expected third marker cycle to move camera.");
                AssertVectorApproximately(longAxisObject.transform.position, cameraObject.transform.position, 0.0001f, "Expected third cycle target to be long-axis marker.");
                AssertTrue(controller.MoveCameraToNextMarkerForTests(), "Expected fourth marker cycle to move camera.");
                AssertVectorApproximately(spawnObject.transform.position, cameraObject.transform.position, 0.0001f, "Expected fourth cycle target to be spawn marker.");
                AssertApproximately(0f, Quaternion.Angle(spawnObject.transform.rotation, cameraObject.transform.rotation), 0.001f, "Expected spawn marker cycle to apply spawn rotation.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(lookAtObject);
                UnityEngine.Object.DestroyImmediate(spawnObject);
                UnityEngine.Object.DestroyImmediate(longAxisObject);
                UnityEngine.Object.DestroyImmediate(consensusObject);
                UnityEngine.Object.DestroyImmediate(originObject);
                UnityEngine.Object.DestroyImmediate(cameraObject);
                UnityEngine.Object.DestroyImmediate(controllerObject);
            }
        }

        static MethodInfo ResolveOrientationMethod()
        {
            MethodInfo method = typeof(RuntimeSplatFloorLoader).GetMethod(
                "ResolveSourceOrientation",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            AssertTrue(method != null, "RuntimeSplatFloorLoader should expose ResolveSourceOrientation.");
            return method;
        }

        static T ReadField<T>(object target, string fieldName)
        {
            FieldInfo field = target.GetType().GetField(fieldName);
            AssertTrue(field != null, $"Expected field '{fieldName}'.");
            return (T)field.GetValue(target);
        }

        static void AssertTrue(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }

        static void AssertApproximately(float expected, float actual, float tolerance, string message)
        {
            if (Mathf.Abs(expected - actual) > tolerance)
                throw new InvalidOperationException($"{message} Expected {expected}, got {actual}.");
        }

        static void AssertEqual<T>(T expected, T actual, string message)
        {
            if (!Equals(expected, actual))
                throw new InvalidOperationException($"{message} Expected {expected}, got {actual}.");
        }

        static void AssertVectorApproximately(Vector3 expected, Vector3 actual, float tolerance, string message)
        {
            if (Vector3.Distance(expected, actual) > tolerance)
                throw new InvalidOperationException($"{message} Expected {expected}, got {actual}.");
        }
    }
}
