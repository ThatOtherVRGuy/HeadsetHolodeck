// SPDX-License-Identifier: MIT

using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace GaussianSplatting.Runtime
{
    /// <summary>
    /// Converts raw <see cref="InputSplatData"/> arrays (from SPZ/PLY files) into
    /// the compressed GPU-ready byte buffers stored in <see cref="RuntimeSplatData"/>.
    ///
    /// All jobs are Burst-compiled and have zero Editor dependencies — safe to call at runtime in builds.
    /// </summary>
    [BurstCompile]
    public static class RuntimeSplatProcessing
    {
        // ─────────────────────────────────────────────────────────────────────
        //  Public entry points
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>Read an SPZ file from disk and process it into a <see cref="RuntimeSplatData"/>.</summary>
        public static RuntimeSplatData ProcessSPZFile(
            string filePath,
            GaussianSplatAsset.VectorFormat posFormat   = GaussianSplatAsset.VectorFormat.Norm11,
            GaussianSplatAsset.VectorFormat scaleFormat = GaussianSplatAsset.VectorFormat.Norm11,
            GaussianSplatAsset.ColorFormat  colorFormat = GaussianSplatAsset.ColorFormat.Norm8x4,
            GaussianSplatAsset.SHFormat     shFormat    = GaussianSplatAsset.SHFormat.Norm6,
            Action<string, float> progressCallback = null)
        {
            NativeArray<InputSplatData> splats = default;
            try
            {
                SPZFileReader.ReadFile(filePath, out splats);
                return Process(splats, posFormat, scaleFormat, colorFormat, shFormat, progressCallback);
            }
            finally
            {
                if (splats.IsCreated) splats.Dispose();
            }
        }

        /// <summary>Read an SPZ file from compressed bytes in memory and process it into a <see cref="RuntimeSplatData"/>.</summary>
        public static RuntimeSplatData ProcessSPZBytes(
            byte[] compressedSPZBytes,
            GaussianSplatAsset.VectorFormat posFormat   = GaussianSplatAsset.VectorFormat.Norm11,
            GaussianSplatAsset.VectorFormat scaleFormat = GaussianSplatAsset.VectorFormat.Norm11,
            GaussianSplatAsset.ColorFormat  colorFormat = GaussianSplatAsset.ColorFormat.Norm8x4,
            GaussianSplatAsset.SHFormat     shFormat    = GaussianSplatAsset.SHFormat.Norm6,
            Action<string, float> progressCallback = null)
        {
            NativeArray<InputSplatData> splats = default;
            try
            {
                SPZFileReader.ReadFile(compressedSPZBytes, out splats);
                return Process(splats, posFormat, scaleFormat, colorFormat, shFormat, progressCallback);
            }
            finally
            {
                if (splats.IsCreated) splats.Dispose();
            }
        }

        /// <summary>Process an already-parsed <see cref="InputSplatData"/> array into <see cref="RuntimeSplatData"/>.</summary>
        public static unsafe RuntimeSplatData Process(
            NativeArray<InputSplatData> inputSplats,
            GaussianSplatAsset.VectorFormat posFormat,
            GaussianSplatAsset.VectorFormat scaleFormat,
            GaussianSplatAsset.ColorFormat  colorFormat,
            GaussianSplatAsset.SHFormat     shFormat,
            Action<string, float> progressCallback = null)
        {
            if (inputSplats.Length == 0)
                throw new ArgumentException("inputSplats is empty");

            progressCallback?.Invoke("Calculating bounds", 0.0f);

            float3 boundsMin, boundsMax;
            var boundsJob = new CalcBoundsJob
            {
                m_BoundsMin  = &boundsMin,
                m_BoundsMax  = &boundsMax,
                m_SplatData  = inputSplats
            };
            boundsJob.Schedule().Complete();

            progressCallback?.Invoke("Morton reordering", 0.05f);
            ReorderMorton(inputSplats, boundsMin, boundsMax);

            // Cluster SHs if format requires it
            NativeArray<int>                              splatSHIndices = default;
            NativeArray<GaussianSplatAsset.SHTableItemFloat16> clusteredSHs  = default;
            if (shFormat >= GaussianSplatAsset.SHFormat.Cluster64k)
            {
                progressCallback?.Invoke("Clustering SHs", 0.2f);
                ClusterSHs(inputSplats, shFormat, out clusteredSHs, out splatSHIndices,
                    p => { progressCallback?.Invoke($"Clustering SHs ({p:P0})", 0.2f + p * 0.5f); return true; });
            }

            progressCallback?.Invoke("Encoding data", 0.7f);

            bool useChunks = posFormat   != GaussianSplatAsset.VectorFormat.Float32 ||
                             scaleFormat != GaussianSplatAsset.VectorFormat.Float32 ||
                             colorFormat != GaussianSplatAsset.ColorFormat.Float32x4 ||
                             shFormat    != GaussianSplatAsset.SHFormat.Float32;

            var result = new RuntimeSplatData
            {
                splatCount  = inputSplats.Length,
                boundsMin   = boundsMin,
                boundsMax   = boundsMax,
                posFormat   = posFormat,
                scaleFormat = scaleFormat,
                colorFormat = colorFormat,
                shFormat    = shFormat,
            };

            if (useChunks)
                result.chkData = CreateChunkData(inputSplats);

            result.posData = CreatePositionsData(inputSplats, posFormat);
            progressCallback?.Invoke("Encoding positions", 0.75f);

            result.othData = CreateOtherData(inputSplats, scaleFormat, splatSHIndices);
            progressCallback?.Invoke("Encoding other", 0.80f);

            result.colData = CreateColorData(inputSplats);
            progressCallback?.Invoke("Encoding color", 0.85f);

            result.shData = CreateSHData(inputSplats, shFormat, clusteredSHs);
            progressCallback?.Invoke("Encoding SH", 0.90f);

            if (splatSHIndices.IsCreated) splatSHIndices.Dispose();
            if (clusteredSHs.IsCreated)   clusteredSHs.Dispose();

            progressCallback?.Invoke("Done", 1.0f);
            return result;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Encoding helpers for shared use by Editor pipeline
        // ─────────────────────────────────────────────────────────────────────

        public static ulong EncodeFloat3ToNorm16(float3 v)
            => (ulong)(v.x * 65535.5f) | ((ulong)(v.y * 65535.5f) << 16) | ((ulong)(v.z * 65535.5f) << 32);

        public static uint EncodeFloat3ToNorm11(float3 v)
            => (uint)(v.x * 2047.5f) | ((uint)(v.y * 1023.5f) << 11) | ((uint)(v.z * 2047.5f) << 21);

        public static ushort EncodeFloat3ToNorm655(float3 v)
            => (ushort)((uint)(v.x * 63.5f) | ((uint)(v.y * 31.5f) << 6) | ((uint)(v.z * 31.5f) << 11));

        public static ushort EncodeFloat3ToNorm565(float3 v)
            => (ushort)((uint)(v.x * 31.5f) | ((uint)(v.y * 63.5f) << 5) | ((uint)(v.z * 31.5f) << 11));

        public static uint EncodeQuatToNorm10(float4 v)
            => (uint)(v.x * 1023.5f) | ((uint)(v.y * 1023.5f) << 10) | ((uint)(v.z * 1023.5f) << 20) | ((uint)(v.w * 3.5f) << 30);

        public static unsafe void EmitEncodedVector(float3 v, byte* outputPtr, GaussianSplatAsset.VectorFormat format)
        {
            switch (format)
            {
                case GaussianSplatAsset.VectorFormat.Float32:
                    *(float*)outputPtr       = v.x;
                    *(float*)(outputPtr + 4) = v.y;
                    *(float*)(outputPtr + 8) = v.z;
                    break;
                case GaussianSplatAsset.VectorFormat.Norm16:
                {
                    ulong enc = EncodeFloat3ToNorm16(math.saturate(v));
                    *(uint*)outputPtr           = (uint)enc;
                    *(ushort*)(outputPtr + 4)   = (ushort)(enc >> 32);
                }
                    break;
                case GaussianSplatAsset.VectorFormat.Norm11:
                    *(uint*)outputPtr = EncodeFloat3ToNorm11(math.saturate(v));
                    break;
                case GaussianSplatAsset.VectorFormat.Norm6:
                    *(ushort*)outputPtr = EncodeFloat3ToNorm655(math.saturate(v));
                    break;
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Bounds
        // ─────────────────────────────────────────────────────────────────────

        [BurstCompile]
        public struct CalcBoundsJob : IJob
        {
            [NativeDisableUnsafePtrRestriction] public unsafe float3* m_BoundsMin;
            [NativeDisableUnsafePtrRestriction] public unsafe float3* m_BoundsMax;
            [ReadOnly] public NativeArray<InputSplatData> m_SplatData;

            public unsafe void Execute()
            {
                float3 bMin = float.PositiveInfinity;
                float3 bMax = float.NegativeInfinity;
                for (int i = 0; i < m_SplatData.Length; ++i)
                {
                    float3 pos = m_SplatData[i].pos;
                    bMin = math.min(bMin, pos);
                    bMax = math.max(bMax, pos);
                }
                *m_BoundsMin = bMin;
                *m_BoundsMax = bMax;
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Morton reordering
        // ─────────────────────────────────────────────────────────────────────

        [BurstCompile]
        struct ReorderMortonJob : IJobParallelFor
        {
            const float kScaler = (float)((1 << 21) - 1);
            public float3 m_BoundsMin;
            public float3 m_InvBoundsSize;
            [ReadOnly] public NativeArray<InputSplatData> m_SplatData;
            public NativeArray<(ulong, int)> m_Order;

            public void Execute(int index)
            {
                float3 pos  = ((float3)m_SplatData[index].pos - m_BoundsMin) * m_InvBoundsSize * kScaler;
                uint3  ipos = (uint3)pos;
                ulong  code = GaussianUtils.MortonEncode3(ipos);
                m_Order[index] = (code, index);
            }
        }

        struct OrderComparer : System.Collections.Generic.IComparer<(ulong, int)>
        {
            public int Compare((ulong, int) a, (ulong, int) b)
            {
                if (a.Item1 < b.Item1) return -1;
                if (a.Item1 > b.Item1) return +1;
                return a.Item2 - b.Item2;
            }
        }

        public static void ReorderMorton(NativeArray<InputSplatData> splatData, float3 boundsMin, float3 boundsMax)
        {
            var order = new ReorderMortonJob
            {
                m_SplatData     = splatData,
                m_BoundsMin     = boundsMin,
                m_InvBoundsSize = 1.0f / (boundsMax - boundsMin),
                m_Order         = new NativeArray<(ulong, int)>(splatData.Length, Allocator.TempJob)
            };
            order.Schedule(splatData.Length, 4096).Complete();
            order.m_Order.Sort(new OrderComparer());

            NativeArray<InputSplatData> copy = new(order.m_SplatData, Allocator.TempJob);
            for (int i = 0; i < copy.Length; ++i)
                order.m_SplatData[i] = copy[order.m_Order[i].Item2];
            copy.Dispose();
            order.m_Order.Dispose();
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Chunk data
        // ─────────────────────────────────────────────────────────────────────

        [BurstCompile]
        public struct CalcChunkDataJob : IJobParallelFor
        {
            [NativeDisableParallelForRestriction] public NativeArray<InputSplatData> splatData;
            public NativeArray<GaussianSplatAsset.ChunkInfo> chunks;

            public void Execute(int chunkIdx)
            {
                float3 chunkMinpos = float.PositiveInfinity;
                float3 chunkMinscl = float.PositiveInfinity;
                float4 chunkMincol = float.PositiveInfinity;
                float3 chunkMinshs = float.PositiveInfinity;
                float3 chunkMaxpos = float.NegativeInfinity;
                float3 chunkMaxscl = float.NegativeInfinity;
                float4 chunkMaxcol = float.NegativeInfinity;
                float3 chunkMaxshs = float.NegativeInfinity;

                int splatBegin = math.min(chunkIdx * GaussianSplatAsset.kChunkSize, splatData.Length);
                int splatEnd   = math.min((chunkIdx + 1) * GaussianSplatAsset.kChunkSize, splatData.Length);

                for (int i = splatBegin; i < splatEnd; ++i)
                {
                    InputSplatData s = splatData[i];
                    s.scale   = math.pow(s.scale, 1.0f / 8.0f);
                    s.opacity = GaussianUtils.SquareCentered01(s.opacity);
                    splatData[i] = s;

                    chunkMinpos = math.min(chunkMinpos, s.pos);
                    chunkMinscl = math.min(chunkMinscl, s.scale);
                    chunkMincol = math.min(chunkMincol, new float4(s.dc0, s.opacity));
                    chunkMinshs = math.min(chunkMinshs, s.sh1);
                    chunkMinshs = math.min(chunkMinshs, s.sh2);
                    chunkMinshs = math.min(chunkMinshs, s.sh3);
                    chunkMinshs = math.min(chunkMinshs, s.sh4);
                    chunkMinshs = math.min(chunkMinshs, s.sh5);
                    chunkMinshs = math.min(chunkMinshs, s.sh6);
                    chunkMinshs = math.min(chunkMinshs, s.sh7);
                    chunkMinshs = math.min(chunkMinshs, s.sh8);
                    chunkMinshs = math.min(chunkMinshs, s.sh9);
                    chunkMinshs = math.min(chunkMinshs, s.shA);
                    chunkMinshs = math.min(chunkMinshs, s.shB);
                    chunkMinshs = math.min(chunkMinshs, s.shC);
                    chunkMinshs = math.min(chunkMinshs, s.shD);
                    chunkMinshs = math.min(chunkMinshs, s.shE);
                    chunkMinshs = math.min(chunkMinshs, s.shF);
                    chunkMaxpos = math.max(chunkMaxpos, s.pos);
                    chunkMaxscl = math.max(chunkMaxscl, s.scale);
                    chunkMaxcol = math.max(chunkMaxcol, new float4(s.dc0, s.opacity));
                    chunkMaxshs = math.max(chunkMaxshs, s.sh1);
                    chunkMaxshs = math.max(chunkMaxshs, s.sh2);
                    chunkMaxshs = math.max(chunkMaxshs, s.sh3);
                    chunkMaxshs = math.max(chunkMaxshs, s.sh4);
                    chunkMaxshs = math.max(chunkMaxshs, s.sh5);
                    chunkMaxshs = math.max(chunkMaxshs, s.sh6);
                    chunkMaxshs = math.max(chunkMaxshs, s.sh7);
                    chunkMaxshs = math.max(chunkMaxshs, s.sh8);
                    chunkMaxshs = math.max(chunkMaxshs, s.sh9);
                    chunkMaxshs = math.max(chunkMaxshs, s.shA);
                    chunkMaxshs = math.max(chunkMaxshs, s.shB);
                    chunkMaxshs = math.max(chunkMaxshs, s.shC);
                    chunkMaxshs = math.max(chunkMaxshs, s.shD);
                    chunkMaxshs = math.max(chunkMaxshs, s.shE);
                    chunkMaxshs = math.max(chunkMaxshs, s.shF);
                }

                chunkMaxpos = math.max(chunkMaxpos, chunkMinpos + 1.0e-5f);
                chunkMaxscl = math.max(chunkMaxscl, chunkMinscl + 1.0e-5f);
                chunkMaxcol = math.max(chunkMaxcol, chunkMincol + 1.0e-5f);
                chunkMaxshs = math.max(chunkMaxshs, chunkMinshs + 1.0e-5f);

                GaussianSplatAsset.ChunkInfo info = default;
                info.posX = new float2(chunkMinpos.x, chunkMaxpos.x);
                info.posY = new float2(chunkMinpos.y, chunkMaxpos.y);
                info.posZ = new float2(chunkMinpos.z, chunkMaxpos.z);
                info.sclX = math.f32tof16(chunkMinscl.x) | (math.f32tof16(chunkMaxscl.x) << 16);
                info.sclY = math.f32tof16(chunkMinscl.y) | (math.f32tof16(chunkMaxscl.y) << 16);
                info.sclZ = math.f32tof16(chunkMinscl.z) | (math.f32tof16(chunkMaxscl.z) << 16);
                info.colR = math.f32tof16(chunkMincol.x) | (math.f32tof16(chunkMaxcol.x) << 16);
                info.colG = math.f32tof16(chunkMincol.y) | (math.f32tof16(chunkMaxcol.y) << 16);
                info.colB = math.f32tof16(chunkMincol.z) | (math.f32tof16(chunkMaxcol.z) << 16);
                info.colA = math.f32tof16(chunkMincol.w) | (math.f32tof16(chunkMaxcol.w) << 16);
                info.shR  = math.f32tof16(chunkMinshs.x) | (math.f32tof16(chunkMaxshs.x) << 16);
                info.shG  = math.f32tof16(chunkMinshs.y) | (math.f32tof16(chunkMaxshs.y) << 16);
                info.shB  = math.f32tof16(chunkMinshs.z) | (math.f32tof16(chunkMaxshs.z) << 16);
                chunks[chunkIdx] = info;

                for (int i = splatBegin; i < splatEnd; ++i)
                {
                    InputSplatData s = splatData[i];
                    s.pos     = ((float3)s.pos   - chunkMinpos) / (chunkMaxpos - chunkMinpos);
                    s.scale   = ((float3)s.scale  - chunkMinscl) / (chunkMaxscl - chunkMinscl);
                    s.dc0     = ((float3)s.dc0   - chunkMincol.xyz) / (chunkMaxcol.xyz - chunkMincol.xyz);
                    s.opacity = (s.opacity - chunkMincol.w) / (chunkMaxcol.w - chunkMincol.w);
                    s.sh1     = ((float3)s.sh1 - chunkMinshs) / (chunkMaxshs - chunkMinshs);
                    s.sh2     = ((float3)s.sh2 - chunkMinshs) / (chunkMaxshs - chunkMinshs);
                    s.sh3     = ((float3)s.sh3 - chunkMinshs) / (chunkMaxshs - chunkMinshs);
                    s.sh4     = ((float3)s.sh4 - chunkMinshs) / (chunkMaxshs - chunkMinshs);
                    s.sh5     = ((float3)s.sh5 - chunkMinshs) / (chunkMaxshs - chunkMinshs);
                    s.sh6     = ((float3)s.sh6 - chunkMinshs) / (chunkMaxshs - chunkMinshs);
                    s.sh7     = ((float3)s.sh7 - chunkMinshs) / (chunkMaxshs - chunkMinshs);
                    s.sh8     = ((float3)s.sh8 - chunkMinshs) / (chunkMaxshs - chunkMinshs);
                    s.sh9     = ((float3)s.sh9 - chunkMinshs) / (chunkMaxshs - chunkMinshs);
                    s.shA     = ((float3)s.shA - chunkMinshs) / (chunkMaxshs - chunkMinshs);
                    s.shB     = ((float3)s.shB - chunkMinshs) / (chunkMaxshs - chunkMinshs);
                    s.shC     = ((float3)s.shC - chunkMinshs) / (chunkMaxshs - chunkMinshs);
                    s.shD     = ((float3)s.shD - chunkMinshs) / (chunkMaxshs - chunkMinshs);
                    s.shE     = ((float3)s.shE - chunkMinshs) / (chunkMaxshs - chunkMinshs);
                    s.shF     = ((float3)s.shF - chunkMinshs) / (chunkMaxshs - chunkMinshs);
                    splatData[i] = s;
                }
            }
        }

        public static byte[] CreateChunkData(NativeArray<InputSplatData> splatData)
        {
            int chunkCount = (splatData.Length + GaussianSplatAsset.kChunkSize - 1) / GaussianSplatAsset.kChunkSize;
            var job = new CalcChunkDataJob
            {
                splatData = splatData,
                chunks    = new NativeArray<GaussianSplatAsset.ChunkInfo>(chunkCount, Allocator.TempJob),
            };
            job.Schedule(chunkCount, 8).Complete();

            var bytes = new byte[chunkCount * UnsafeUtility.SizeOf<GaussianSplatAsset.ChunkInfo>()];
            job.chunks.Reinterpret<byte>(UnsafeUtility.SizeOf<GaussianSplatAsset.ChunkInfo>()).CopyTo(bytes);
            job.chunks.Dispose();
            return bytes;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Position data
        // ─────────────────────────────────────────────────────────────────────

        [BurstCompile]
        public struct CreatePositionsDataJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<InputSplatData> m_Input;
            public GaussianSplatAsset.VectorFormat m_Format;
            public int m_FormatSize;
            [NativeDisableParallelForRestriction] public NativeArray<byte> m_Output;

            public unsafe void Execute(int index)
            {
                byte* outputPtr = (byte*)m_Output.GetUnsafePtr() + index * m_FormatSize;
                EmitEncodedVector(m_Input[index].pos, outputPtr, m_Format);
            }
        }

        public static byte[] CreatePositionsData(NativeArray<InputSplatData> inputSplats, GaussianSplatAsset.VectorFormat formatPos)
        {
            int dataLen = NextMultipleOf(inputSplats.Length * GaussianSplatAsset.GetVectorSize(formatPos), 8);
            var data    = new NativeArray<byte>(dataLen, Allocator.TempJob);
            var job = new CreatePositionsDataJob
            {
                m_Input      = inputSplats,
                m_Format     = formatPos,
                m_FormatSize = GaussianSplatAsset.GetVectorSize(formatPos),
                m_Output     = data
            };
            job.Schedule(inputSplats.Length, 8192).Complete();
            var bytes = new byte[dataLen];
            data.CopyTo(bytes);
            data.Dispose();
            return bytes;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Other data (rotation + scale + optional SH index)
        // ─────────────────────────────────────────────────────────────────────

        [BurstCompile]
        public struct CreateOtherDataJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<InputSplatData> m_Input;
            [NativeDisableContainerSafetyRestriction] [ReadOnly] public NativeArray<int> m_SplatSHIndices;
            public GaussianSplatAsset.VectorFormat m_ScaleFormat;
            public int m_FormatSize;
            [NativeDisableParallelForRestriction] public NativeArray<byte> m_Output;

            public unsafe void Execute(int index)
            {
                byte* outputPtr = (byte*)m_Output.GetUnsafePtr() + index * m_FormatSize;

                Quaternion rotQ = m_Input[index].rot;
                float4 rot = new float4(rotQ.x, rotQ.y, rotQ.z, rotQ.w);
                *(uint*)outputPtr = EncodeQuatToNorm10(rot);
                outputPtr += 4;

                EmitEncodedVector(m_Input[index].scale, outputPtr, m_ScaleFormat);
                outputPtr += GaussianSplatAsset.GetVectorSize(m_ScaleFormat);

                if (m_SplatSHIndices.IsCreated)
                    *(ushort*)outputPtr = (ushort)m_SplatSHIndices[index];
            }
        }

        public static byte[] CreateOtherData(NativeArray<InputSplatData> inputSplats, GaussianSplatAsset.VectorFormat scaleFormat, NativeArray<int> splatSHIndices)
        {
            int formatSize = GaussianSplatAsset.GetOtherSizeNoSHIndex(scaleFormat);
            if (splatSHIndices.IsCreated)
                formatSize += 2;
            int dataLen = NextMultipleOf(inputSplats.Length * formatSize, 8);
            var data    = new NativeArray<byte>(dataLen, Allocator.TempJob);
            var job = new CreateOtherDataJob
            {
                m_Input         = inputSplats,
                m_SplatSHIndices= splatSHIndices,
                m_ScaleFormat   = scaleFormat,
                m_FormatSize    = formatSize,
                m_Output        = data
            };
            job.Schedule(inputSplats.Length, 8192).Complete();
            var bytes = new byte[dataLen];
            data.CopyTo(bytes);
            data.Dispose();
            return bytes;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Color data (always Float32x4 per splat; renderer converts at load time)
        // ─────────────────────────────────────────────────────────────────────

        [BurstCompile]
        public struct CreateSimpleColorDataJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<InputSplatData> m_Input;
            [NativeDisableParallelForRestriction] public NativeArray<float4> m_Output;

            public void Execute(int index)
            {
                var splat       = m_Input[index];
                m_Output[index] = new float4(splat.dc0.x, splat.dc0.y, splat.dc0.z, splat.opacity);
            }
        }

        public static byte[] CreateColorData(NativeArray<InputSplatData> inputSplats)
        {
            var data = new NativeArray<float4>(inputSplats.Length, Allocator.TempJob);
            var job  = new CreateSimpleColorDataJob { m_Input = inputSplats, m_Output = data };
            job.Schedule(inputSplats.Length, 8192).Complete();
            var bytes = new byte[inputSplats.Length * 16];
            data.Reinterpret<byte>(16).CopyTo(bytes);
            data.Dispose();
            return bytes;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  SH data
        // ─────────────────────────────────────────────────────────────────────

        [BurstCompile]
        public struct CreateSHDataJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<InputSplatData> m_Input;
            public GaussianSplatAsset.SHFormat m_Format;
            public NativeArray<byte> m_Output;

            public unsafe void Execute(int index)
            {
                var splat = m_Input[index];
                switch (m_Format)
                {
                    case GaussianSplatAsset.SHFormat.Float32:
                    {
                        GaussianSplatAsset.SHTableItemFloat32 res;
                        res.sh1 = splat.sh1; res.sh2 = splat.sh2; res.sh3 = splat.sh3;
                        res.sh4 = splat.sh4; res.sh5 = splat.sh5; res.sh6 = splat.sh6;
                        res.sh7 = splat.sh7; res.sh8 = splat.sh8; res.sh9 = splat.sh9;
                        res.shA = splat.shA; res.shB = splat.shB; res.shC = splat.shC;
                        res.shD = splat.shD; res.shE = splat.shE; res.shF = splat.shF;
                        res.shPadding = default;
                        ((GaussianSplatAsset.SHTableItemFloat32*)m_Output.GetUnsafePtr())[index] = res;
                        break;
                    }
                    case GaussianSplatAsset.SHFormat.Float16:
                    {
                        GaussianSplatAsset.SHTableItemFloat16 res;
                        res.sh1 = new half3(splat.sh1); res.sh2 = new half3(splat.sh2); res.sh3 = new half3(splat.sh3);
                        res.sh4 = new half3(splat.sh4); res.sh5 = new half3(splat.sh5); res.sh6 = new half3(splat.sh6);
                        res.sh7 = new half3(splat.sh7); res.sh8 = new half3(splat.sh8); res.sh9 = new half3(splat.sh9);
                        res.shA = new half3(splat.shA); res.shB = new half3(splat.shB); res.shC = new half3(splat.shC);
                        res.shD = new half3(splat.shD); res.shE = new half3(splat.shE); res.shF = new half3(splat.shF);
                        res.shPadding = default;
                        ((GaussianSplatAsset.SHTableItemFloat16*)m_Output.GetUnsafePtr())[index] = res;
                        break;
                    }
                    case GaussianSplatAsset.SHFormat.Norm11:
                    {
                        GaussianSplatAsset.SHTableItemNorm11 res;
                        res.sh1 = EncodeFloat3ToNorm11(splat.sh1); res.sh2 = EncodeFloat3ToNorm11(splat.sh2);
                        res.sh3 = EncodeFloat3ToNorm11(splat.sh3); res.sh4 = EncodeFloat3ToNorm11(splat.sh4);
                        res.sh5 = EncodeFloat3ToNorm11(splat.sh5); res.sh6 = EncodeFloat3ToNorm11(splat.sh6);
                        res.sh7 = EncodeFloat3ToNorm11(splat.sh7); res.sh8 = EncodeFloat3ToNorm11(splat.sh8);
                        res.sh9 = EncodeFloat3ToNorm11(splat.sh9); res.shA = EncodeFloat3ToNorm11(splat.shA);
                        res.shB = EncodeFloat3ToNorm11(splat.shB); res.shC = EncodeFloat3ToNorm11(splat.shC);
                        res.shD = EncodeFloat3ToNorm11(splat.shD); res.shE = EncodeFloat3ToNorm11(splat.shE);
                        res.shF = EncodeFloat3ToNorm11(splat.shF);
                        ((GaussianSplatAsset.SHTableItemNorm11*)m_Output.GetUnsafePtr())[index] = res;
                        break;
                    }
                    case GaussianSplatAsset.SHFormat.Norm6:
                    {
                        GaussianSplatAsset.SHTableItemNorm6 res;
                        res.sh1 = EncodeFloat3ToNorm565(splat.sh1); res.sh2 = EncodeFloat3ToNorm565(splat.sh2);
                        res.sh3 = EncodeFloat3ToNorm565(splat.sh3); res.sh4 = EncodeFloat3ToNorm565(splat.sh4);
                        res.sh5 = EncodeFloat3ToNorm565(splat.sh5); res.sh6 = EncodeFloat3ToNorm565(splat.sh6);
                        res.sh7 = EncodeFloat3ToNorm565(splat.sh7); res.sh8 = EncodeFloat3ToNorm565(splat.sh8);
                        res.sh9 = EncodeFloat3ToNorm565(splat.sh9); res.shA = EncodeFloat3ToNorm565(splat.shA);
                        res.shB = EncodeFloat3ToNorm565(splat.shB); res.shC = EncodeFloat3ToNorm565(splat.shC);
                        res.shD = EncodeFloat3ToNorm565(splat.shD); res.shE = EncodeFloat3ToNorm565(splat.shE);
                        res.shF = EncodeFloat3ToNorm565(splat.shF);
                        res.shPadding = default;
                        ((GaussianSplatAsset.SHTableItemNorm6*)m_Output.GetUnsafePtr())[index] = res;
                        break;
                    }
                }
            }
        }

        public static byte[] CreateSHData(NativeArray<InputSplatData> inputSplats, GaussianSplatAsset.SHFormat shFormat, NativeArray<GaussianSplatAsset.SHTableItemFloat16> clusteredSHs)
        {
            if (clusteredSHs.IsCreated)
            {
                int size  = clusteredSHs.Length * UnsafeUtility.SizeOf<GaussianSplatAsset.SHTableItemFloat16>();
                var bytes = new byte[size];
                clusteredSHs.Reinterpret<byte>(UnsafeUtility.SizeOf<GaussianSplatAsset.SHTableItemFloat16>()).CopyTo(bytes);
                return bytes;
            }

            int dataLen = (int)GaussianSplatAsset.CalcSHDataSize(inputSplats.Length, shFormat);
            var data    = new NativeArray<byte>(dataLen, Allocator.TempJob);
            var job     = new CreateSHDataJob { m_Input = inputSplats, m_Format = shFormat, m_Output = data };
            job.Schedule(inputSplats.Length, 8192).Complete();
            var result = new byte[dataLen];
            data.CopyTo(result);
            data.Dispose();
            return result;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  SH Clustering
        // ─────────────────────────────────────────────────────────────────────

        [BurstCompile]
        public struct ConvertSHClustersJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float3> m_Input;
            public NativeArray<GaussianSplatAsset.SHTableItemFloat16> m_Output;

            public void Execute(int index)
            {
                int addr = index * 15;
                GaussianSplatAsset.SHTableItemFloat16 res;
                res.sh1 = new half3(m_Input[addr + 0]);  res.sh2 = new half3(m_Input[addr + 1]);
                res.sh3 = new half3(m_Input[addr + 2]);  res.sh4 = new half3(m_Input[addr + 3]);
                res.sh5 = new half3(m_Input[addr + 4]);  res.sh6 = new half3(m_Input[addr + 5]);
                res.sh7 = new half3(m_Input[addr + 6]);  res.sh8 = new half3(m_Input[addr + 7]);
                res.sh9 = new half3(m_Input[addr + 8]);  res.shA = new half3(m_Input[addr + 9]);
                res.shB = new half3(m_Input[addr + 10]); res.shC = new half3(m_Input[addr + 11]);
                res.shD = new half3(m_Input[addr + 12]); res.shE = new half3(m_Input[addr + 13]);
                res.shF = new half3(m_Input[addr + 14]);
                res.shPadding = default;
                m_Output[index] = res;
            }
        }

        [BurstCompile]
        static unsafe void GatherSHs(int splatCount, InputSplatData* splatData, float* shData)
        {
            for (int i = 0; i < splatCount; ++i)
            {
                UnsafeUtility.MemCpy(shData, ((float*)splatData) + 9, 15 * 3 * sizeof(float));
                splatData++;
                shData += 15 * 3;
            }
        }

        static unsafe void ClusterSHs(
            NativeArray<InputSplatData> splatData,
            GaussianSplatAsset.SHFormat format,
            out NativeArray<GaussianSplatAsset.SHTableItemFloat16> shs,
            out NativeArray<int> shIndices,
            Func<float, bool> progressCallback)
        {
            shs       = default;
            shIndices = default;

            int shCount = GaussianSplatAsset.GetSHCount(format, splatData.Length);
            if (shCount >= splatData.Length) return;

            const int kShDim    = 15 * 3;
            const int kBatchSize = 2048;
            float passesOverData = format switch
            {
                GaussianSplatAsset.SHFormat.Cluster64k => 0.3f,
                GaussianSplatAsset.SHFormat.Cluster32k => 0.4f,
                GaussianSplatAsset.SHFormat.Cluster16k => 0.5f,
                GaussianSplatAsset.SHFormat.Cluster8k  => 0.8f,
                GaussianSplatAsset.SHFormat.Cluster4k  => 1.2f,
                _ => throw new ArgumentOutOfRangeException(nameof(format), format, null)
            };

            NativeArray<float> shData  = new(splatData.Length * kShDim, Allocator.Persistent);
            GatherSHs(splatData.Length, (InputSplatData*)splatData.GetUnsafeReadOnlyPtr(), (float*)shData.GetUnsafePtr());

            NativeArray<float> shMeans = new(shCount * kShDim, Allocator.Persistent);
            shIndices = new(splatData.Length, Allocator.Persistent);

            KMeansClustering.Calculate(kShDim, shData, kBatchSize, passesOverData, progressCallback, shMeans, shIndices);
            shData.Dispose();

            shs = new(shCount, Allocator.Persistent);
            var job = new ConvertSHClustersJob
            {
                m_Input  = shMeans.Reinterpret<float3>(4),
                m_Output = shs
            };
            job.Schedule(shCount, 256).Complete();
            shMeans.Dispose();
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Utility
        // ─────────────────────────────────────────────────────────────────────

        public static int NextMultipleOf(int size, int multipleOf)
            => (size + multipleOf - 1) / multipleOf * multipleOf;
    }
}
