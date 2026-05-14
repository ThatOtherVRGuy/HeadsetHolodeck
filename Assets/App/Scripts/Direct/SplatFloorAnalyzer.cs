// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using GaussianSplatting.Runtime;
using Unity.Collections;
using UnityEngine;

namespace WorldLabs.Runtime.Tools
{
    [Serializable]
    public class SplatFloorAnalysisOptions
    {
        [Tooltip("Ignore the lowest outliers below this percentile.")]
        [Range(0f, 0.2f)]
        public float lowerPercentile = 0.01f;

        [Tooltip("Only search for the floor up to this percentile of heights.")]
        [Range(0.05f, 1f)]
        public float upperPercentile = 0.40f;

        [Tooltip("Number of slices in the coarse vertical pass.")]
        [Min(4)]
        public int coarseSliceCount = 64;

        [Tooltip("How many refinement passes to run around the winning band.")]
        [Range(0, 4)]
        public int refinePasses = 2;

        [Tooltip("Number of slices in each refinement pass.")]
        [Min(4)]
        public int refineSliceCount = 24;

        [Tooltip("XZ occupancy cell size. Larger = smoother, smaller = more detailed.")]
        [Min(0.001f)]
        public float occupancyCellSize = 0.25f;

        [Tooltip("Weight applied to XZ footprint occupancy.")]
        [Range(0f, 4f)]
        public float occupancyWeight = 1.0f;

        [Tooltip("Weight applied to per-slice density.")]
        [Range(0f, 4f)]
        public float densityWeight = 0.5f;

        [Tooltip("Use splat opacity as part of the density term.")]
        public bool useOpacityWeightedDensity = true;

        [Tooltip("Prefer the first strong low-elevation peak over the absolute highest one.")]
        [Range(0.1f, 1f)]
        public float firstPeakRelativeThreshold = 0.72f;

        [Tooltip("Simple moving-average smoothing radius over slice scores.")]
        [Range(0, 4)]
        public int smoothingRadius = 1;

        [Tooltip("Transform applied to source splat positions before analysis. " +
                 "To match WorldLabsWorldManager, use Matrix4x4.Rotate(Quaternion.Euler(-180f, 0f, 0f)).")]
        public Matrix4x4 positionTransform = default;

        [Tooltip("If true and positionTransform is left at default(Matrix4x4), identity is used.")]
        public bool treatDefaultTransformAsIdentity = true;
    }

    [Serializable]
    public struct SplatSliceScore
    {
        public float yMin;
        public float yMax;

        public int count;
        public float densitySum;

        public int occupiedCellCount;
        public float occupiedArea;

        public float rawScore;
        public float smoothedScore;

        public float CenterY => 0.5f * (yMin + yMax);
        public float Height => yMax - yMin;
    }

    [Serializable]
    public class SplatFloorEstimate
    {
        public bool success;
        public string message;

        public Bounds analyzedBounds;
        public Bounds supportBounds;

        public float estimatedFloorY;
        public Vector2 winningBandY;

        public int supportCount;
        public float supportAreaXZ;

        /// <summary>
        /// XZ centroid of occupied floor cells, in the same analyzed coordinate space.
        /// </summary>
        public Vector2 floorCenterXZ;

        /// <summary>
        /// Recommended local position for the splat GameObject, assuming the object's
        /// local rotation matches the transform used during analysis.
        /// This places floor Y at 0 and floor center XZ at (0,0).
        /// </summary>
        public Vector3 recommendedLocalPosition;

        public SplatSliceScore[] coarseSlices;
        public SplatSliceScore[] refinedSlices;
    }

    public static class SplatFloorAnalyzer
    {
        public static SplatFloorEstimate AnalyzeSpzFile(string spzFilePath, SplatFloorAnalysisOptions options = null)
        {
            if (string.IsNullOrWhiteSpace(spzFilePath))
                throw new ArgumentException("SPZ file path is null or empty.", nameof(spzFilePath));

            NativeArray<InputSplatData> splats = default;
            try
            {
                SPZFileReader.ReadFile(spzFilePath, out splats);
                return AnalyzeSplats(splats, options);
            }
            finally
            {
                if (splats.IsCreated)
                    splats.Dispose();
            }
        }

        public static SplatFloorEstimate AnalyzeSpzBytes(byte[] compressedSpzBytes, SplatFloorAnalysisOptions options = null)
        {
            if (compressedSpzBytes == null || compressedSpzBytes.Length == 0)
                throw new ArgumentException("SPZ bytes are null or empty.", nameof(compressedSpzBytes));

            NativeArray<InputSplatData> splats = default;
            try
            {
                SPZFileReader.ReadFile(compressedSpzBytes, out splats);
                return AnalyzeSplats(splats, options);
            }
            finally
            {
                if (splats.IsCreated)
                    splats.Dispose();
            }
        }

        public static SplatFloorEstimate AnalyzeSplats(NativeArray<InputSplatData> splats, SplatFloorAnalysisOptions options = null)
        {
            options ??= new SplatFloorAnalysisOptions();

            if (!splats.IsCreated || splats.Length == 0)
            {
                return new SplatFloorEstimate
                {
                    success = false,
                    message = "No splats provided."
                };
            }

            Matrix4x4 transform = ResolveTransform(options);

            int n = splats.Length;
            var positions = new Vector3[n];
            var ySorted = new float[n];
            var densityWeights = new float[n];

            Vector3 min = new(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            Vector3 max = new(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);

            for (int i = 0; i < n; i++)
            {
                Vector3 p = transform.MultiplyPoint3x4(splats[i].pos);
                positions[i] = p;
                ySorted[i] = p.y;

                densityWeights[i] = options.useOpacityWeightedDensity
                    ? Mathf.Max(0.0001f, splats[i].opacity)
                    : 1f;

                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }

            Array.Sort(ySorted);

            float yLow = PercentileFromSorted(ySorted, options.lowerPercentile);
            float yHigh = PercentileFromSorted(ySorted, options.upperPercentile);

            if (!(yHigh > yLow))
            {
                yLow = min.y;
                yHigh = max.y;
            }

            if (!(yHigh > yLow))
            {
                return new SplatFloorEstimate
                {
                    success = false,
                    message = "Degenerate height range.",
                    analyzedBounds = CreateBounds(min, max)
                };
            }

            Bounds analyzedBounds = CreateBounds(min, max);

            SplatSliceScore[] coarseSlices = ScoreBand(
                positions,
                densityWeights,
                yLow,
                yHigh,
                Mathf.Max(4, options.coarseSliceCount),
                Mathf.Max(0.001f, options.occupancyCellSize),
                Mathf.Clamp(options.occupancyWeight, 0f, 4f),
                Mathf.Clamp(options.densityWeight, 0f, 4f),
                Mathf.Max(0, options.smoothingRadius),
                analyzedBounds.min.x,
                analyzedBounds.min.z
            );

            int bestIndex = PickFirstStrongPeak(coarseSlices, options.firstPeakRelativeThreshold);
            if (bestIndex < 0)
                bestIndex = ArgMax(coarseSlices);

            float bandMin = coarseSlices[bestIndex].yMin;
            float bandMax = coarseSlices[bestIndex].yMax;

            SplatSliceScore[] latestSlices = coarseSlices;

            for (int pass = 0; pass < Mathf.Max(0, options.refinePasses); pass++)
            {
                latestSlices = ScoreBand(
                    positions,
                    densityWeights,
                    bandMin,
                    bandMax,
                    Mathf.Max(4, options.refineSliceCount),
                    Mathf.Max(0.001f, options.occupancyCellSize),
                    Mathf.Clamp(options.occupancyWeight, 0f, 4f),
                    Mathf.Clamp(options.densityWeight, 0f, 4f),
                    Mathf.Max(0, options.smoothingRadius),
                    analyzedBounds.min.x,
                    analyzedBounds.min.z
                );

                int refinedBest = PickFirstStrongPeak(latestSlices, options.firstPeakRelativeThreshold);
                if (refinedBest < 0)
                    refinedBest = ArgMax(latestSlices);

                bandMin = latestSlices[refinedBest].yMin;
                bandMax = latestSlices[refinedBest].yMax;
            }

            float estimatedFloorY = MedianYInBand(positions, bandMin, bandMax);
            if (float.IsNaN(estimatedFloorY))
                estimatedFloorY = 0.5f * (bandMin + bandMax);

            Vector2 floorCenterXZ = ComputeOccupiedCellCentroidXZ(
                positions,
                bandMin,
                bandMax,
                Mathf.Max(0.001f, options.occupancyCellSize),
                analyzedBounds.min.x,
                analyzedBounds.min.z
            );

            Bounds supportBounds = ComputeSupportBounds(positions, bandMin, bandMax);
            int supportCount = CountInBand(positions, bandMin, bandMax);

            int finalBestIndex = ArgMax(latestSlices);
            float supportAreaXZ = finalBestIndex >= 0 ? latestSlices[finalBestIndex].occupiedArea : 0f;

            Vector3 recommendedLocalPosition = new(
                -floorCenterXZ.x,
                -estimatedFloorY,
                -floorCenterXZ.y
            );

            return new SplatFloorEstimate
            {
                success = true,
                message = "OK",
                analyzedBounds = analyzedBounds,
                supportBounds = supportBounds,
                estimatedFloorY = estimatedFloorY,
                winningBandY = new Vector2(bandMin, bandMax),
                supportCount = supportCount,
                supportAreaXZ = supportAreaXZ,
                floorCenterXZ = floorCenterXZ,
                recommendedLocalPosition = recommendedLocalPosition,
                coarseSlices = coarseSlices,
                refinedSlices = latestSlices
            };
        }

        public static void ApplyRecommendedPlacement(Transform target, SplatFloorEstimate estimate)
        {
            if (target == null)
                throw new ArgumentNullException(nameof(target));
            if (estimate == null)
                throw new ArgumentNullException(nameof(estimate));

            target.localPosition = estimate.recommendedLocalPosition;
        }

        static Matrix4x4 ResolveTransform(SplatFloorAnalysisOptions options)
        {
            if (options.treatDefaultTransformAsIdentity && options.positionTransform == default)
                return Matrix4x4.identity;

            return options.positionTransform;
        }

        static SplatSliceScore[] ScoreBand(
            Vector3[] positions,
            float[] densityWeights,
            float yMin,
            float yMax,
            int sliceCount,
            float cellSize,
            float occupancyWeight,
            float densityWeight,
            int smoothingRadius,
            float xOrigin,
            float zOrigin)
        {
            var slices = new SplatSliceScore[sliceCount];
            var occupied = new HashSet<long>[sliceCount];

            float bandHeight = yMax - yMin;
            float sliceHeight = bandHeight / sliceCount;

            for (int i = 0; i < sliceCount; i++)
            {
                float s0 = yMin + sliceHeight * i;
                float s1 = (i == sliceCount - 1) ? yMax : (s0 + sliceHeight);

                slices[i] = new SplatSliceScore
                {
                    yMin = s0,
                    yMax = s1
                };

                occupied[i] = new HashSet<long>();
            }

            for (int i = 0; i < positions.Length; i++)
            {
                Vector3 p = positions[i];
                if (p.y < yMin || p.y > yMax)
                    continue;

                int sliceIndex = Mathf.FloorToInt((p.y - yMin) / Mathf.Max(sliceHeight, 1e-6f));
                if (sliceIndex < 0)
                    continue;
                if (sliceIndex >= sliceCount)
                    sliceIndex = sliceCount - 1;

                slices[sliceIndex].count++;
                slices[sliceIndex].densitySum += densityWeights[i];

                int ix = Mathf.FloorToInt((p.x - xOrigin) / cellSize);
                int iz = Mathf.FloorToInt((p.z - zOrigin) / cellSize);
                long key = PackInt2(ix, iz);

                occupied[sliceIndex].Add(key);
            }

            float maxArea = 0f;
            float maxDensity = 0f;

            for (int i = 0; i < sliceCount; i++)
            {
                slices[i].occupiedCellCount = occupied[i].Count;
                slices[i].occupiedArea = slices[i].occupiedCellCount * cellSize * cellSize;

                if (slices[i].occupiedArea > maxArea)
                    maxArea = slices[i].occupiedArea;
                if (slices[i].densitySum > maxDensity)
                    maxDensity = slices[i].densitySum;
            }

            if (maxArea <= 0f) maxArea = 1f;
            if (maxDensity <= 0f) maxDensity = 1f;

            for (int i = 0; i < sliceCount; i++)
            {
                float areaNorm = slices[i].occupiedArea / maxArea;
                float densityNorm = slices[i].densitySum / maxDensity;

                // Area dominates. Density boosts broad, well-supported slices.
                slices[i].rawScore =
                    occupancyWeight * areaNorm *
                    (1f + densityWeight * densityNorm);

                slices[i].smoothedScore = slices[i].rawScore;
            }

            ApplySmoothing(slices, smoothingRadius);
            return slices;
        }

        static void ApplySmoothing(SplatSliceScore[] slices, int radius)
        {
            if (slices == null || slices.Length == 0 || radius <= 0)
                return;

            float[] tmp = new float[slices.Length];

            for (int i = 0; i < slices.Length; i++)
            {
                int from = Mathf.Max(0, i - radius);
                int to = Mathf.Min(slices.Length - 1, i + radius);

                float sum = 0f;
                int count = 0;

                for (int j = from; j <= to; j++)
                {
                    sum += slices[j].rawScore;
                    count++;
                }

                tmp[i] = count > 0 ? sum / count : slices[i].rawScore;
            }

            for (int i = 0; i < slices.Length; i++)
                slices[i].smoothedScore = tmp[i];
        }

        static int PickFirstStrongPeak(SplatSliceScore[] slices, float relativeThreshold)
        {
            if (slices == null || slices.Length == 0)
                return -1;

            float best = float.NegativeInfinity;
            for (int i = 0; i < slices.Length; i++)
            {
                if (slices[i].smoothedScore > best)
                    best = slices[i].smoothedScore;
            }

            if (!(best > 0f))
                return -1;

            float threshold = best * Mathf.Clamp01(relativeThreshold);

            for (int i = 0; i < slices.Length; i++)
            {
                float s = slices[i].smoothedScore;
                if (s < threshold)
                    continue;

                float prev = i > 0 ? slices[i - 1].smoothedScore : float.NegativeInfinity;
                float next = i < slices.Length - 1 ? slices[i + 1].smoothedScore : float.NegativeInfinity;

                bool isLocalPeak = s >= prev && s >= next;
                if (isLocalPeak)
                    return i;
            }

            return -1;
        }

        static int ArgMax(SplatSliceScore[] slices)
        {
            if (slices == null || slices.Length == 0)
                return -1;

            int bestIndex = 0;
            float bestValue = slices[0].smoothedScore;

            for (int i = 1; i < slices.Length; i++)
            {
                if (slices[i].smoothedScore > bestValue)
                {
                    bestValue = slices[i].smoothedScore;
                    bestIndex = i;
                }
            }

            return bestIndex;
        }

        static float MedianYInBand(Vector3[] positions, float yMin, float yMax)
        {
            List<float> ys = new();

            for (int i = 0; i < positions.Length; i++)
            {
                float y = positions[i].y;
                if (y >= yMin && y <= yMax)
                    ys.Add(y);
            }

            if (ys.Count == 0)
                return float.NaN;

            ys.Sort();

            int mid = ys.Count / 2;
            if ((ys.Count & 1) == 1)
                return ys[mid];

            return 0.5f * (ys[mid - 1] + ys[mid]);
        }

        static int CountInBand(Vector3[] positions, float yMin, float yMax)
        {
            int count = 0;

            for (int i = 0; i < positions.Length; i++)
            {
                float y = positions[i].y;
                if (y >= yMin && y <= yMax)
                    count++;
            }

            return count;
        }

        static Bounds ComputeSupportBounds(Vector3[] positions, float yMin, float yMax)
        {
            Vector3 min = new(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            Vector3 max = new(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);

            bool any = false;

            for (int i = 0; i < positions.Length; i++)
            {
                Vector3 p = positions[i];
                if (p.y < yMin || p.y > yMax)
                    continue;

                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
                any = true;
            }

            if (!any)
                return new Bounds(Vector3.zero, Vector3.zero);

            return CreateBounds(min, max);
        }

        static Vector2 ComputeOccupiedCellCentroidXZ(
            Vector3[] positions,
            float yMin,
            float yMax,
            float cellSize,
            float xOrigin,
            float zOrigin)
        {
            var occupied = new HashSet<long>();

            double sumX = 0.0;
            double sumZ = 0.0;
            int count = 0;

            for (int i = 0; i < positions.Length; i++)
            {
                Vector3 p = positions[i];
                if (p.y < yMin || p.y > yMax)
                    continue;

                int ix = Mathf.FloorToInt((p.x - xOrigin) / cellSize);
                int iz = Mathf.FloorToInt((p.z - zOrigin) / cellSize);
                long key = PackInt2(ix, iz);

                if (!occupied.Add(key))
                    continue;

                float cx = xOrigin + (ix + 0.5f) * cellSize;
                float cz = zOrigin + (iz + 0.5f) * cellSize;

                sumX += cx;
                sumZ += cz;
                count++;
            }

            if (count == 0)
                return Vector2.zero;

            return new Vector2(
                (float)(sumX / count),
                (float)(sumZ / count)
            );
        }

        static float PercentileFromSorted(float[] sorted, float t)
        {
            if (sorted == null || sorted.Length == 0)
                return float.NaN;

            if (sorted.Length == 1)
                return sorted[0];

            t = Mathf.Clamp01(t);

            float f = t * (sorted.Length - 1);
            int i0 = Mathf.FloorToInt(f);
            int i1 = Mathf.Min(sorted.Length - 1, i0 + 1);
            float frac = f - i0;

            return Mathf.Lerp(sorted[i0], sorted[i1], frac);
        }

        static Bounds CreateBounds(Vector3 min, Vector3 max)
        {
            Bounds b = new();
            b.SetMinMax(min, max);
            return b;
        }

        static long PackInt2(int a, int b)
        {
            return ((long)a << 32) ^ (uint)b;
        }
    }
}