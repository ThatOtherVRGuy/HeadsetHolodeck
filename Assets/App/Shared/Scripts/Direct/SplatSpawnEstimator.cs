// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using GaussianSplatting.Runtime;
using Unity.Collections;
using UnityEngine;

namespace WorldLabs.Runtime.Tools
{
    [Serializable]
    public sealed class SplatSpawnMetadata
    {
        public int version = 1;
        public bool hasPose;
        public string method = "floor_percentile_candidate_v1";
        public string sourceAssetPath;
        public string sourceAssetHash;
        public Vector3 spawn;
        public Quaternion rotation = Quaternion.identity;
        public Vector3 lookAt;
        public Vector3 up = Vector3.up;
        public Bounds bounds;
        [Range(0f, 1f)] public float confidence;
        public float sceneDiagonal;
        public int totalSplats;
        public int sampledSplats;
        public int acceptedSplats;
        public string[] warnings = Array.Empty<string>();
    }

    [Serializable]
    public sealed class SplatSpawnEstimatorSettings
    {
        public int randomSeed = 12345;
        [Min(1)] public int maxSamples = 30000;
        [Min(1)] public int minAcceptedSamples = 500;
        [Min(0)] public int maxDebugNormalLines = 300;
        [Min(1f)] public float minFlatnessRatio = 1.25f;
        [Min(1f)] public float maxFlatnessRatioForWeight = 8f;
        [Min(1f)] public float minLongAxisRatio = 1.35f;
        [Min(0.1f)] public float defaultEyeHeightMeters = 1.6f;
        [Range(0f, 0.25f)] public float floorPercentile = 0.03f;
        [Range(0f, 1f)] public float lookAtEyeHeightFraction = 0.875f;
        [Min(0f)] public float candidateRingRadiusFractionOfDiagonal = 0.08f;
        [Min(0f)] public float minCandidateClearanceFractionOfDiagonal = 0.015f;
        [Range(0, 4)] public int candidateRingCount = 2;
        [Min(4)] public int candidateDirectionsPerRing = 12;
        public bool useRadialCaptureCenter = true;
        [Range(0.01f, 1f)] public float radialCaptureMaxRadiusCoefficientOfVariation = 0.28f;
        [Min(0.1f)] public float radialCaptureMinRadiusMeters = 0.75f;
        [Range(0f, 1f)] public float radialCaptureOpenSideOffsetFraction = 0.35f;
        [Min(0f)] public float radialCaptureMaxOpenSideOffsetMeters = 2f;
        public bool preferNormalConsensusAsSpawn = true;
        public bool preferLongAxisConsensusAsSpawn = true;
        public bool preferOriginFallbackOverCandidateSearch = true;
        public bool refineSpawnYFromLocalFloor = true;
        [Min(0.01f)] public float localFloorSearchRadiusFractionOfDiagonal = 0.06f;
        [Min(0.05f)] public float localFloorMinRadiusMeters = 0.5f;
        [Range(0.05f, 0.95f)] public float localFloorMaxHeightFraction = 0.5f;
        [Range(0f, 0.25f)] public float localFloorPercentile = 0.08f;
        [Min(1)] public int minLocalFloorSamples = 24;
    }

    [Serializable]
    public readonly struct SplatSpawnNormalLine
    {
        public SplatSpawnNormalLine(Vector3 point, Vector3 direction, float weight)
        {
            this.point = point;
            this.direction = direction;
            this.weight = weight;
        }

        public readonly Vector3 point;
        public readonly Vector3 direction;
        public readonly float weight;
    }

    [Serializable]
    public sealed class SplatSpawnDebugData
    {
        public readonly List<SplatSpawnNormalLine> normalLines = new();
        public readonly List<SplatSpawnNormalLine> longAxisNormalLines = new();
        public bool hasConsensusPoint;
        public Vector3 consensusPoint;
        public float consensusConfidence;
        public bool hasLongAxisConsensusPoint;
        public Vector3 longAxisConsensusPoint;
        public float longAxisConsensusConfidence;
        public bool hasLocalFloorEstimate;
        public float localFloorY;
        public int localFloorSampleCount;
        public Vector3 spawnCandidate;
        public Vector3 lookAt;

        public void Clear()
        {
            normalLines.Clear();
            longAxisNormalLines.Clear();
            hasConsensusPoint = false;
            consensusPoint = Vector3.zero;
            consensusConfidence = 0f;
            hasLongAxisConsensusPoint = false;
            longAxisConsensusPoint = Vector3.zero;
            longAxisConsensusConfidence = 0f;
            hasLocalFloorEstimate = false;
            localFloorY = 0f;
            localFloorSampleCount = 0;
            spawnCandidate = Vector3.zero;
            lookAt = Vector3.zero;
        }
    }

    public static class SplatSpawnEstimator
    {
        public static SplatSpawnMetadata EstimateFromSplats(
            NativeArray<InputSplatData> splats,
            Matrix4x4 positionTransform,
            SplatSpawnEstimatorSettings settings,
            string sourceAssetPath = null,
            string sourceAssetHash = null,
            SplatFloorEstimate floorEstimate = null,
            SplatSpawnDebugData debugData = null)
        {
            settings ??= new SplatSpawnEstimatorSettings();
            debugData?.Clear();

            var metadata = new SplatSpawnMetadata
            {
                sourceAssetPath = sourceAssetPath,
                sourceAssetHash = sourceAssetHash,
                totalSplats = splats.IsCreated ? splats.Length : 0,
                method = "floor_percentile_candidate_v1"
            };

            if (!splats.IsCreated || splats.Length == 0)
            {
                metadata.warnings = new[] { "No splats provided." };
                return metadata;
            }

            int sampleCount = Mathf.Min(splats.Length, Mathf.Max(1, settings.maxSamples));
            metadata.sampledSplats = sampleCount;

            var positions = new List<Vector3>(sampleCount);
            var yValues = new List<float>(sampleCount);
            bool collectConsensusLines = debugData != null || settings.preferNormalConsensusAsSpawn || settings.preferLongAxisConsensusAsSpawn;
            var consensusLines = collectConsensusLines ? new List<SplatSpawnNormalLine>(sampleCount) : null;
            var longAxisLines = collectConsensusLines ? new List<SplatSpawnNormalLine>(sampleCount) : null;

            Vector3 min = new(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            Vector3 max = new(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);

            for (int i = 0; i < sampleCount; i++)
            {
                int index = DeterministicIndex(i, splats.Length, sampleCount, settings.randomSeed);
                InputSplatData splat = splats[index];
                Vector3 p = positionTransform.MultiplyPoint3x4(splat.pos);

                if (!IsFinite(p))
                    continue;

                positions.Add(p);
                yValues.Add(p.y);
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);

                if (collectConsensusLines && TryBuildNormalLine(splat, positionTransform, settings, out SplatSpawnNormalLine line))
                    consensusLines.Add(line);
                if (collectConsensusLines && TryBuildLongAxisHorizontalNormalLine(splat, positionTransform, settings, out SplatSpawnNormalLine longAxisLine))
                    longAxisLines.Add(longAxisLine);
            }

            metadata.acceptedSplats = positions.Count;
            if (positions.Count == 0)
            {
                metadata.warnings = new[] { "No finite splat positions accepted." };
                return metadata;
            }

            Bounds bounds = CreateBounds(min, max);
            metadata.bounds = bounds;
            metadata.sceneDiagonal = Mathf.Max(bounds.size.magnitude, 0.0001f);

            float floorY = floorEstimate != null && floorEstimate.success
                ? floorEstimate.estimatedFloorY
                : Percentile(yValues, settings.floorPercentile);

            Vector3 placementOffset = floorEstimate != null && floorEstimate.success
                ? floorEstimate.recommendedLocalPosition
                : Vector3.zero;

            Vector3 lookAtAnalyzed = bounds.center;
            lookAtAnalyzed.y = floorY + Mathf.Max(0.1f, settings.defaultEyeHeightMeters * settings.lookAtEyeHeightFraction);
            Vector3 boundsLookAtAnalyzed = lookAtAnalyzed;
            bool hasNormalConsensusSpawn = false;
            Vector3 normalConsensusSpawn = Vector3.zero;
            bool hasLongAxisConsensusSpawn = false;
            Vector3 longAxisConsensusSpawn = Vector3.zero;

            if (collectConsensusLines && SolveLineConsensus(consensusLines, out Vector3 consensusPoint))
            {
                Bounds expanded = bounds;
                expanded.Expand(metadata.sceneDiagonal * 0.3f);
                if (expanded.Contains(consensusPoint))
                {
                    lookAtAnalyzed = consensusPoint;
                    normalConsensusSpawn = new Vector3(
                        consensusPoint.x,
                        floorY + Mathf.Max(0.1f, settings.defaultEyeHeightMeters),
                        consensusPoint.z);
                    hasNormalConsensusSpawn = true;
                    if (debugData != null)
                    {
                        debugData.hasConsensusPoint = true;
                        debugData.consensusPoint = consensusPoint;
                        debugData.consensusConfidence = Mathf.Clamp01(consensusLines.Count / (float)Mathf.Max(1, settings.minAcceptedSamples));
                    }
                    metadata.method = "normal_line_consensus_candidate_v1";
                }
            }

            if (collectConsensusLines && SolveHorizontalLineConsensus(longAxisLines, out Vector3 longAxisConsensusPoint))
            {
                Bounds expanded = bounds;
                expanded.Expand(metadata.sceneDiagonal * 0.3f);
                Vector3 placedLongAxisConsensus = new(
                    longAxisConsensusPoint.x,
                    floorY + Mathf.Max(0.1f, settings.defaultEyeHeightMeters),
                    longAxisConsensusPoint.z);
                Vector3 boundsTestPoint = new(placedLongAxisConsensus.x, bounds.center.y, placedLongAxisConsensus.z);
                if (expanded.Contains(boundsTestPoint))
                {
                    longAxisConsensusSpawn = placedLongAxisConsensus;
                    hasLongAxisConsensusSpawn = true;
                    if (debugData != null)
                    {
                        debugData.hasLongAxisConsensusPoint = true;
                        debugData.longAxisConsensusPoint = placedLongAxisConsensus;
                        debugData.longAxisConsensusConfidence = Mathf.Clamp01(longAxisLines.Count / (float)Mathf.Max(1, settings.minAcceptedSamples));
                    }
                }
            }

            Vector3 spawnAnalyzed;
            if (settings.preferNormalConsensusAsSpawn && hasNormalConsensusSpawn)
            {
                spawnAnalyzed = normalConsensusSpawn;
                lookAtAnalyzed = boundsLookAtAnalyzed;
                metadata.method = metadata.method + "+normal_consensus_spawn_v1";
            }
            else if (settings.preferLongAxisConsensusAsSpawn && hasLongAxisConsensusSpawn)
            {
                spawnAnalyzed = longAxisConsensusSpawn;
                lookAtAnalyzed = boundsLookAtAnalyzed;
                metadata.method = metadata.method + "+long_axis_consensus_spawn_v1";
            }
            else if (settings.useRadialCaptureCenter &&
                TryEstimateRadialCaptureCenter(positions, settings, floorY, out Vector3 radialCaptureCenter))
            {
                spawnAnalyzed = radialCaptureCenter;
                metadata.method = metadata.method + "+radial_capture_center_v1";
            }
            else if (settings.preferOriginFallbackOverCandidateSearch)
            {
                spawnAnalyzed = new Vector3(0f, floorY + Mathf.Max(0.1f, settings.defaultEyeHeightMeters), 0f);
                metadata.method = metadata.method + "+origin_spawn_fallback_v1";
            }
            else
            {
                spawnAnalyzed = PickCandidate(positions, bounds, floorY, lookAtAnalyzed, metadata.sceneDiagonal, settings);
            }

            if (TryEstimateLocalFloorY(
                    positions,
                    spawnAnalyzed,
                    bounds,
                    metadata.sceneDiagonal,
                    settings,
                    out float localFloorY,
                    out int localFloorSampleCount))
            {
                floorY = localFloorY;
                float eyeHeight = Mathf.Max(0.1f, settings.defaultEyeHeightMeters);
                spawnAnalyzed.y = floorY + eyeHeight;
                lookAtAnalyzed.y = floorY + Mathf.Max(0.1f, eyeHeight * settings.lookAtEyeHeightFraction);
                metadata.method = metadata.method + "+local_floor_y_v1";

                if (debugData != null)
                {
                    debugData.hasLocalFloorEstimate = true;
                    debugData.localFloorY = floorY;
                    debugData.localFloorSampleCount = localFloorSampleCount;
                    if (debugData.hasLongAxisConsensusPoint)
                    {
                        Vector3 p = debugData.longAxisConsensusPoint;
                        p.y = floorY + eyeHeight;
                        debugData.longAxisConsensusPoint = p;
                    }
                }
            }

            Vector3 spawn = spawnAnalyzed + placementOffset;
            Vector3 lookAt = lookAtAnalyzed + placementOffset;
            Vector3 forward = Vector3.ProjectOnPlane(lookAt - spawn, Vector3.up);
            if (forward.sqrMagnitude < 0.0001f)
                forward = Vector3.forward;

            metadata.hasPose = true;
            metadata.spawn = spawn;
            metadata.lookAt = lookAt;
            metadata.rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
            metadata.confidence = ComputeConfidence(metadata.acceptedSplats, settings.minAcceptedSamples);
            if (metadata.acceptedSplats < settings.minAcceptedSamples)
                metadata.warnings = new[] { "Accepted sample count is below preferred threshold." };

            if (debugData != null)
            {
                CopyDebugLines(consensusLines, debugData.normalLines, Mathf.Max(0, settings.maxDebugNormalLines), settings.randomSeed);
                CopyDebugLines(longAxisLines, debugData.longAxisNormalLines, Mathf.Max(0, settings.maxDebugNormalLines), settings.randomSeed ^ unchecked((int)0x4C6F6E67));
                debugData.spawnCandidate = spawn;
                debugData.lookAt = lookAt;
            }

            return metadata;
        }

        static bool TryEstimateLocalFloorY(
            List<Vector3> positions,
            Vector3 spawnAnalyzed,
            Bounds bounds,
            float sceneDiagonal,
            SplatSpawnEstimatorSettings settings,
            out float floorY,
            out int sampleCount)
        {
            floorY = 0f;
            sampleCount = 0;

            if (!settings.refineSpawnYFromLocalFloor || positions == null || positions.Count == 0)
                return false;

            float baseRadius = Mathf.Max(
                Mathf.Max(0.05f, settings.localFloorMinRadiusMeters),
                Mathf.Max(0.0001f, sceneDiagonal) * Mathf.Max(0.01f, settings.localFloorSearchRadiusFractionOfDiagonal));
            float heightFraction = Mathf.Clamp(settings.localFloorMaxHeightFraction, 0.05f, 0.95f);
            float maxY = bounds.size.y > 0.001f
                ? bounds.min.y + bounds.size.y * heightFraction
                : bounds.max.y;
            int minimumSamples = Mathf.Max(1, settings.minLocalFloorSamples);
            var localYValues = new List<float>(minimumSamples * 2);

            for (int pass = 1; pass <= 3; pass++)
            {
                localYValues.Clear();
                float radius = baseRadius * pass;
                float radiusSq = radius * radius;

                for (int i = 0; i < positions.Count; i++)
                {
                    Vector3 p = positions[i];
                    if (p.y > maxY)
                        continue;

                    float dx = p.x - spawnAnalyzed.x;
                    float dz = p.z - spawnAnalyzed.z;
                    if (dx * dx + dz * dz <= radiusSq)
                        localYValues.Add(p.y);
                }

                if (localYValues.Count >= minimumSamples)
                {
                    float y = Percentile(localYValues, settings.localFloorPercentile);
                    if (float.IsFinite(y))
                    {
                        floorY = y;
                        sampleCount = localYValues.Count;
                        return true;
                    }
                }
            }

            return false;
        }

        static bool TryEstimateRadialCaptureCenter(
            List<Vector3> positions,
            SplatSpawnEstimatorSettings settings,
            float floorY,
            out Vector3 captureCenter)
        {
            captureCenter = Vector3.zero;
            if (positions == null || positions.Count < 12)
                return false;

            if (TryEstimateArcCircleCenter(positions, out Vector2 arcCenter) &&
                RadialDistributionPasses(positions, arcCenter, settings, out float arcMeanRadius))
            {
                arcCenter = BiasCenterTowardOpenSide(positions, arcCenter, arcMeanRadius, settings);
                captureCenter = new Vector3(arcCenter.x, floorY + Mathf.Max(0.1f, settings.defaultEyeHeightMeters), arcCenter.y);
                return IsFinite(captureCenter);
            }

            double a00 = 0, a01 = 0, a02 = 0, a11 = 0, a12 = 0, a22 = 0;
            double b0 = 0, b1 = 0, b2 = 0;

            for (int i = 0; i < positions.Count; i++)
            {
                double x = positions[i].x;
                double z = positions[i].z;
                double r2 = x * x + z * z;

                a00 += x * x;
                a01 += x * z;
                a02 += x;
                a11 += z * z;
                a12 += z;
                a22 += 1.0;

                b0 -= x * r2;
                b1 -= z * r2;
                b2 -= r2;
            }

            if (!SolveSymmetric3x3(a00, a01, a02, a11, a12, a22, b0, b1, b2, out Vector3 solved))
                return false;

            Vector2 center = new(-solved.x * 0.5f, -solved.y * 0.5f);
            if (!float.IsFinite(center.x) || !float.IsFinite(center.y))
                return false;

            if (!RadialDistributionPasses(positions, center, settings, out float meanRadius))
                return false;

            center = BiasCenterTowardOpenSide(positions, center, meanRadius, settings);
            captureCenter = new Vector3(center.x, floorY + Mathf.Max(0.1f, settings.defaultEyeHeightMeters), center.y);
            return IsFinite(captureCenter);
        }

        static bool TryEstimateArcCircleCenter(List<Vector3> positions, out Vector2 center)
        {
            center = Vector2.zero;
            Vector2 seed = ToHorizontal(positions[0]);
            Vector2 a = FarthestPoint(positions, seed);
            Vector2 b = FarthestPoint(positions, a);

            Vector2 chord = b - a;
            float chordLength = chord.magnitude;
            if (chordLength < 0.001f)
                return false;

            Vector2 c = a;
            float maxDistanceFromChord = 0f;
            for (int i = 0; i < positions.Count; i++)
            {
                Vector2 p = ToHorizontal(positions[i]);
                float distance = Mathf.Abs(Cross(chord, p - a)) / chordLength;
                if (distance > maxDistanceFromChord)
                {
                    maxDistanceFromChord = distance;
                    c = p;
                }
            }

            if (maxDistanceFromChord < chordLength * 0.08f)
                return false;

            return TryCircumcenter(a, b, c, out center);
        }

        static bool RadialDistributionPasses(
            List<Vector3> positions,
            Vector2 center,
            SplatSpawnEstimatorSettings settings,
            out float meanRadiusResult)
        {
            meanRadiusResult = 0f;
            double radiusSum = 0;
            for (int i = 0; i < positions.Count; i++)
                radiusSum += Vector2.Distance(new Vector2(positions[i].x, positions[i].z), center);

            double meanRadius = radiusSum / positions.Count;
            if (meanRadius < Mathf.Max(0.01f, settings.radialCaptureMinRadiusMeters))
                return false;

            double varianceSum = 0;
            for (int i = 0; i < positions.Count; i++)
            {
                double radius = Vector2.Distance(new Vector2(positions[i].x, positions[i].z), center);
                double d = radius - meanRadius;
                varianceSum += d * d;
            }

            double coefficientOfVariation = Math.Sqrt(varianceSum / positions.Count) / meanRadius;
            meanRadiusResult = (float)meanRadius;
            return coefficientOfVariation <= settings.radialCaptureMaxRadiusCoefficientOfVariation;
        }

        static Vector2 BiasCenterTowardOpenSide(
            List<Vector3> positions,
            Vector2 center,
            float meanRadius,
            SplatSpawnEstimatorSettings settings)
        {
            float offsetFraction = Mathf.Clamp01(settings.radialCaptureOpenSideOffsetFraction);
            if (offsetFraction <= 0f)
                return center;

            Vector2 centroid = Vector2.zero;
            for (int i = 0; i < positions.Count; i++)
                centroid += ToHorizontal(positions[i]);
            centroid /= Mathf.Max(1, positions.Count);

            Vector2 awayFromMass = center - centroid;
            if (awayFromMass.sqrMagnitude < 0.0001f)
                return center;

            float maxOffset = Mathf.Max(0f, settings.radialCaptureMaxOpenSideOffsetMeters);
            float offset = Mathf.Min(meanRadius * offsetFraction, maxOffset);
            if (offset <= 0f)
                return center;

            return center + awayFromMass.normalized * offset;
        }

        static Vector2 FarthestPoint(List<Vector3> positions, Vector2 from)
        {
            Vector2 best = from;
            float bestDistance = -1f;
            for (int i = 0; i < positions.Count; i++)
            {
                Vector2 p = ToHorizontal(positions[i]);
                float distance = (p - from).sqrMagnitude;
                if (distance > bestDistance)
                {
                    bestDistance = distance;
                    best = p;
                }
            }

            return best;
        }

        static bool TryCircumcenter(Vector2 a, Vector2 b, Vector2 c, out Vector2 center)
        {
            center = Vector2.zero;
            double ax = a.x, ay = a.y;
            double bx = b.x, by = b.y;
            double cx = c.x, cy = c.y;
            double d = 2.0 * (ax * (by - cy) + bx * (cy - ay) + cx * (ay - by));
            if (Math.Abs(d) < 1e-8)
                return false;

            double a2 = ax * ax + ay * ay;
            double b2 = bx * bx + by * by;
            double c2 = cx * cx + cy * cy;
            center = new Vector2(
                (float)((a2 * (by - cy) + b2 * (cy - ay) + c2 * (ay - by)) / d),
                (float)((a2 * (cx - bx) + b2 * (ax - cx) + c2 * (bx - ax)) / d));
            return float.IsFinite(center.x) && float.IsFinite(center.y);
        }

        static Vector2 ToHorizontal(Vector3 p)
        {
            return new Vector2(p.x, p.z);
        }

        static float Cross(Vector2 a, Vector2 b)
        {
            return a.x * b.y - a.y * b.x;
        }

        static bool TryBuildNormalLine(
            InputSplatData splat,
            Matrix4x4 positionTransform,
            SplatSpawnEstimatorSettings settings,
            out SplatSpawnNormalLine line)
        {
            line = default;

            Vector3 scale = Abs(splat.scale);
            float minScale = Mathf.Min(scale.x, Mathf.Min(scale.y, scale.z));
            float maxScale = Mathf.Max(scale.x, Mathf.Max(scale.y, scale.z));
            if (!(minScale > 0f) || !(maxScale > 0f))
                return false;

            float flatness = maxScale / Mathf.Max(minScale, 1e-6f);
            if (flatness < Mathf.Max(1f, settings.minFlatnessRatio))
                return false;

            Quaternion rotation = splat.rot;
            if (!IsFinite(rotation))
                rotation = Quaternion.identity;
            rotation = Normalize(rotation);

            Vector3 localNormal = SmallestAxis(scale);
            Vector3 sourceDirection = rotation * localNormal;
            Vector3 direction = positionTransform.MultiplyVector(sourceDirection);
            if (direction.sqrMagnitude < 1e-8f || !IsFinite(direction))
                return false;

            Vector3 point = positionTransform.MultiplyPoint3x4(splat.pos);
            if (!IsFinite(point))
                return false;

            float flatnessWeight = Mathf.InverseLerp(
                Mathf.Max(1f, settings.minFlatnessRatio),
                Mathf.Max(settings.minFlatnessRatio + 0.01f, settings.maxFlatnessRatioForWeight),
                flatness);
            float opacityWeight = Mathf.Clamp01(splat.opacity <= 0f ? 1f : splat.opacity);
            float weight = Mathf.Max(0.0001f, flatnessWeight * opacityWeight);

            line = new SplatSpawnNormalLine(point, direction.normalized, weight);
            return true;
        }

        static bool TryBuildLongAxisHorizontalNormalLine(
            InputSplatData splat,
            Matrix4x4 positionTransform,
            SplatSpawnEstimatorSettings settings,
            out SplatSpawnNormalLine line)
        {
            line = default;

            Vector3 scale = Abs(splat.scale);
            float minScale = Mathf.Min(scale.x, Mathf.Min(scale.y, scale.z));
            float maxScale = Mathf.Max(scale.x, Mathf.Max(scale.y, scale.z));
            if (!(minScale > 0f) || !(maxScale > 0f))
                return false;

            float elongation = maxScale / Mathf.Max(minScale, 1e-6f);
            if (elongation < Mathf.Max(1f, settings.minLongAxisRatio))
                return false;

            Quaternion rotation = splat.rot;
            if (!IsFinite(rotation))
                rotation = Quaternion.identity;
            rotation = Normalize(rotation);

            Vector3 sourceLongAxis = rotation * LargestAxis(scale);
            Vector3 worldLongAxis = positionTransform.MultiplyVector(sourceLongAxis);
            Vector3 horizontalTangent = Vector3.ProjectOnPlane(worldLongAxis, Vector3.up);
            if (horizontalTangent.sqrMagnitude < 1e-8f || !IsFinite(horizontalTangent))
                return false;

            horizontalTangent.Normalize();
            Vector3 horizontalNormal = new Vector3(-horizontalTangent.z, 0f, horizontalTangent.x);
            if (horizontalNormal.sqrMagnitude < 1e-8f || !IsFinite(horizontalNormal))
                return false;

            Vector3 point = positionTransform.MultiplyPoint3x4(splat.pos);
            if (!IsFinite(point))
                return false;

            point.y = 0f;
            float opacityWeight = Mathf.Clamp01(splat.opacity <= 0f ? 1f : splat.opacity);
            float elongationWeight = Mathf.InverseLerp(
                Mathf.Max(1f, settings.minLongAxisRatio),
                Mathf.Max(settings.minLongAxisRatio + 0.01f, settings.maxFlatnessRatioForWeight),
                elongation);
            float weight = Mathf.Max(0.0001f, elongationWeight * opacityWeight);

            line = new SplatSpawnNormalLine(point, horizontalNormal.normalized, weight);
            return true;
        }

        static void CopyDebugLines(List<SplatSpawnNormalLine> source, List<SplatSpawnNormalLine> destination, int maxLines, int seed)
        {
            if (source == null || destination == null || maxLines <= 0)
                return;

            int count = Mathf.Min(source.Count, maxLines);
            if (count == source.Count)
            {
                destination.AddRange(source);
                return;
            }

            float minX = float.PositiveInfinity;
            float maxX = float.NegativeInfinity;
            float minZ = float.PositiveInfinity;
            float maxZ = float.NegativeInfinity;
            for (int i = 0; i < source.Count; i++)
            {
                Vector3 p = source[i].point;
                minX = Mathf.Min(minX, p.x);
                maxX = Mathf.Max(maxX, p.x);
                minZ = Mathf.Min(minZ, p.z);
                maxZ = Mathf.Max(maxZ, p.z);
            }

            if (!float.IsFinite(minX) || Mathf.Abs(maxX - minX) < 1e-6f || Mathf.Abs(maxZ - minZ) < 1e-6f)
            {
                CopyDebugLinesByPermutation(source, destination, count, seed);
                return;
            }

            int grid = Mathf.Clamp(Mathf.CeilToInt(Mathf.Sqrt(count)), 2, 16);
            var buckets = new Dictionary<int, List<int>>();
            for (int i = 0; i < source.Count; i++)
            {
                Vector3 p = source[i].point;
                int cellX = Mathf.Clamp(Mathf.FloorToInt((p.x - minX) / (maxX - minX) * grid), 0, grid - 1);
                int cellZ = Mathf.Clamp(Mathf.FloorToInt((p.z - minZ) / (maxZ - minZ) * grid), 0, grid - 1);
                int key = cellX + cellZ * grid;
                if (!buckets.TryGetValue(key, out List<int> indices))
                {
                    indices = new List<int>();
                    buckets.Add(key, indices);
                }

                indices.Add(i);
            }

            var keys = new List<int>(buckets.Keys);
            keys.Sort((a, b) => PositiveMod(Mix32(a ^ seed), int.MaxValue).CompareTo(PositiveMod(Mix32(b ^ seed), int.MaxValue)));

            int round = 0;
            while (destination.Count < count)
            {
                bool addedInRound = false;
                for (int i = 0; i < keys.Count && destination.Count < count; i++)
                {
                    List<int> indices = buckets[keys[i]];
                    if (round >= indices.Count)
                        continue;

                    int indexInBucket = DeterministicIndex(round, indices.Count, indices.Count, seed ^ keys[i]);
                    destination.Add(source[indices[indexInBucket]]);
                    addedInRound = true;
                }

                if (!addedInRound)
                    break;
                round++;
            }
        }

        static void CopyDebugLinesByPermutation(List<SplatSpawnNormalLine> source, List<SplatSpawnNormalLine> destination, int count, int seed)
        {
            for (int i = 0; i < count; i++)
            {
                int index = DeterministicIndex(i, source.Count, count, seed ^ unchecked((int)0xA53A9D13));
                destination.Add(source[index]);
            }
        }

        static bool SolveLineConsensus(List<SplatSpawnNormalLine> lines, out Vector3 c)
        {
            c = Vector3.zero;
            if (lines == null || lines.Count < 3)
                return false;

            double a00 = 0, a01 = 0, a02 = 0, a11 = 0, a12 = 0, a22 = 0;
            double b0 = 0, b1 = 0, b2 = 0;

            for (int i = 0; i < lines.Count; i++)
            {
                Vector3 n = lines[i].direction.normalized;
                Vector3 p = lines[i].point;
                double w = Math.Max(0.0, lines[i].weight);

                double p00 = 1.0 - n.x * n.x;
                double p01 = -n.x * n.y;
                double p02 = -n.x * n.z;
                double p11 = 1.0 - n.y * n.y;
                double p12 = -n.y * n.z;
                double p22 = 1.0 - n.z * n.z;

                a00 += w * p00;
                a01 += w * p01;
                a02 += w * p02;
                a11 += w * p11;
                a12 += w * p12;
                a22 += w * p22;

                b0 += w * (p00 * p.x + p01 * p.y + p02 * p.z);
                b1 += w * (p01 * p.x + p11 * p.y + p12 * p.z);
                b2 += w * (p02 * p.x + p12 * p.y + p22 * p.z);
            }

            return SolveSymmetric3x3(a00, a01, a02, a11, a12, a22, b0, b1, b2, out c);
        }

        static bool SolveHorizontalLineConsensus(List<SplatSpawnNormalLine> lines, out Vector3 c)
        {
            c = Vector3.zero;
            if (lines == null || lines.Count < 2)
                return false;

            double a00 = 0, a01 = 0, a11 = 0;
            double b0 = 0, b1 = 0;

            for (int i = 0; i < lines.Count; i++)
            {
                Vector3 d3 = Vector3.ProjectOnPlane(lines[i].direction, Vector3.up);
                if (d3.sqrMagnitude < 1e-8f)
                    continue;

                d3.Normalize();
                double nx = -d3.z;
                double nz = d3.x;
                double w = Math.Max(0.0, lines[i].weight);
                Vector3 p = lines[i].point;

                a00 += w * nx * nx;
                a01 += w * nx * nz;
                a11 += w * nz * nz;
                double rhs = nx * p.x + nz * p.z;
                b0 += w * nx * rhs;
                b1 += w * nz * rhs;
            }

            double det = a00 * a11 - a01 * a01;
            if (Math.Abs(det) < 1e-9)
                return false;

            c = new Vector3(
                (float)((a11 * b0 - a01 * b1) / det),
                0f,
                (float)((-a01 * b0 + a00 * b1) / det));
            return IsFinite(c);
        }

        static bool SolveSymmetric3x3(
            double a00,
            double a01,
            double a02,
            double a11,
            double a12,
            double a22,
            double b0,
            double b1,
            double b2,
            out Vector3 x)
        {
            x = Vector3.zero;

            double det =
                a00 * (a11 * a22 - a12 * a12) -
                a01 * (a01 * a22 - a12 * a02) +
                a02 * (a01 * a12 - a11 * a02);

            if (Math.Abs(det) < 1e-9)
                return false;

            double inv00 = (a11 * a22 - a12 * a12) / det;
            double inv01 = (a02 * a12 - a01 * a22) / det;
            double inv02 = (a01 * a12 - a02 * a11) / det;
            double inv11 = (a00 * a22 - a02 * a02) / det;
            double inv12 = (a01 * a02 - a00 * a12) / det;
            double inv22 = (a00 * a11 - a01 * a01) / det;

            x = new Vector3(
                (float)(inv00 * b0 + inv01 * b1 + inv02 * b2),
                (float)(inv01 * b0 + inv11 * b1 + inv12 * b2),
                (float)(inv02 * b0 + inv12 * b1 + inv22 * b2));

            return IsFinite(x);
        }

        static Vector3 PickCandidate(
            List<Vector3> positions,
            Bounds bounds,
            float floorY,
            Vector3 lookAt,
            float sceneDiagonal,
            SplatSpawnEstimatorSettings settings)
        {
            float eyeY = floorY + Mathf.Max(0.1f, settings.defaultEyeHeightMeters);
            Vector3 center = new(bounds.center.x, eyeY, bounds.center.z);

            Vector3 best = center;
            float bestScore = ScoreCandidate(center, positions, bounds, lookAt, sceneDiagonal, settings);

            int ringCount = Mathf.Max(0, settings.candidateRingCount);
            int directions = Mathf.Max(4, settings.candidateDirectionsPerRing);
            float baseRadius = Mathf.Max(0f, sceneDiagonal * settings.candidateRingRadiusFractionOfDiagonal);

            for (int ring = 1; ring <= ringCount; ring++)
            {
                float radius = baseRadius * ring;
                for (int i = 0; i < directions; i++)
                {
                    float angle = Mathf.PI * 2f * i / directions;
                    Vector3 candidate = center + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                    float score = ScoreCandidate(candidate, positions, bounds, lookAt, sceneDiagonal, settings);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = candidate;
                    }
                }
            }

            return best;
        }

        static float ScoreCandidate(
            Vector3 candidate,
            List<Vector3> positions,
            Bounds bounds,
            Vector3 lookAt,
            float sceneDiagonal,
            SplatSpawnEstimatorSettings settings)
        {
            Bounds expanded = bounds;
            expanded.Expand(sceneDiagonal * 0.2f);
            if (!expanded.Contains(candidate))
                return -1f;

            float clearanceRadius = Mathf.Max(0.05f, sceneDiagonal * settings.minCandidateClearanceFractionOfDiagonal);
            int nearby = 0;
            float r2 = clearanceRadius * clearanceRadius;
            for (int i = 0; i < positions.Count; i++)
            {
                if ((positions[i] - candidate).sqrMagnitude <= r2)
                    nearby++;
            }

            float clearanceScore = 1f / (1f + nearby);
            float idealDistance = Mathf.Max(0.5f, sceneDiagonal * 0.1f);
            float distance = Vector3.Distance(candidate, lookAt);
            float distanceScore = Mathf.Exp(-Mathf.Abs(distance - idealDistance) / idealDistance);
            float centerScore = 1f - Mathf.Clamp01(Vector3.Distance(candidate, bounds.center) / sceneDiagonal);

            return 0.45f * clearanceScore + 0.35f * distanceScore + 0.20f * centerScore;
        }

        static int DeterministicIndex(int sampleNumber, int totalCount, int sampleCount, int seed)
        {
            if (sampleCount >= totalCount)
                return sampleNumber;

            int offset = PositiveMod(Mix32(seed), totalCount);
            int stride = CoprimeStride(totalCount, seed);
            long index = offset + (long)sampleNumber * stride;
            return (int)(index % totalCount);
        }

        static int CoprimeStride(int totalCount, int seed)
        {
            if (totalCount <= 2)
                return 1;

            int stride = 1 + PositiveMod(Mix32(seed ^ unchecked((int)0x9E3779B9)), totalCount - 1);
            while (GreatestCommonDivisor(stride, totalCount) != 1)
            {
                stride++;
                if (stride >= totalCount)
                    stride = 1;
            }

            return stride;
        }

        static int GreatestCommonDivisor(int a, int b)
        {
            a = Mathf.Abs(a);
            b = Mathf.Abs(b);
            while (b != 0)
            {
                int t = a % b;
                a = b;
                b = t;
            }

            return Mathf.Max(1, a);
        }

        static int Mix32(int value)
        {
            unchecked
            {
                uint x = (uint)value;
                x ^= x >> 16;
                x *= 0x7feb352dU;
                x ^= x >> 15;
                x *= 0x846ca68bU;
                x ^= x >> 16;
                return (int)x;
            }
        }

        static int PositiveMod(int value, int modulus)
        {
            if (modulus <= 1)
                return 0;
            long v = value;
            long m = modulus;
            long r = v % m;
            if (r < 0)
                r += m;
            return (int)r;
        }

        static float Percentile(List<float> values, float percentile)
        {
            if (values == null || values.Count == 0)
                return 0f;

            values.Sort();
            if (values.Count == 1)
                return values[0];

            float t = Mathf.Clamp01(percentile) * (values.Count - 1);
            int i0 = Mathf.FloorToInt(t);
            int i1 = Mathf.Min(values.Count - 1, i0 + 1);
            return Mathf.Lerp(values[i0], values[i1], t - i0);
        }

        static float ComputeConfidence(int acceptedSamples, int preferredSamples)
        {
            if (acceptedSamples <= 0)
                return 0f;

            float sampleScore = Mathf.Clamp01(acceptedSamples / (float)Mathf.Max(1, preferredSamples));
            return Mathf.Lerp(0.35f, 0.82f, sampleScore);
        }

        static bool IsFinite(Vector3 v)
        {
            return float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);
        }

        static bool IsFinite(Quaternion q)
        {
            return float.IsFinite(q.x) && float.IsFinite(q.y) && float.IsFinite(q.z) && float.IsFinite(q.w);
        }

        static Quaternion Normalize(Quaternion q)
        {
            float mag = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            if (mag < 1e-8f)
                return Quaternion.identity;

            float inv = 1f / mag;
            return new Quaternion(q.x * inv, q.y * inv, q.z * inv, q.w * inv);
        }

        static Vector3 Abs(Vector3 v)
        {
            return new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
        }

        static Vector3 SmallestAxis(Vector3 scale)
        {
            if (scale.x <= scale.y && scale.x <= scale.z)
                return Vector3.right;
            if (scale.y <= scale.x && scale.y <= scale.z)
                return Vector3.up;
            return Vector3.forward;
        }

        static Vector3 LargestAxis(Vector3 scale)
        {
            if (scale.x >= scale.y && scale.x >= scale.z)
                return Vector3.right;
            if (scale.y >= scale.x && scale.y >= scale.z)
                return Vector3.up;
            return Vector3.forward;
        }

        static Bounds CreateBounds(Vector3 min, Vector3 max)
        {
            Bounds b = new();
            b.SetMinMax(min, max);
            return b;
        }
    }
}
