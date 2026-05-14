// Assets/App/GaussianSplatting/Runtime/RuntimePlyReader.cs
// Runtime-safe PLY reader: port of GaussianSplatting.Editor.Utils.GaussianFileReader
// + PLYFileReader. No UnityEditor.* dependencies.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using GaussianSplatting.Runtime;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Assertions;

namespace WorldLabs.Runtime.Tools
{
    /// <summary>
    /// Runtime-safe parser for Gaussian Splat PLY files.
    /// Produces <see cref="InputSplatData"/> arrays compatible with
    /// <see cref="GaussianSplatting.Runtime.RuntimeSplatProcessing.Process"/>.
    /// Caller is responsible for disposing the output NativeArray.
    /// </summary>
    public static class RuntimePlyReader
    {
        public enum ElementType { None, Float, Double, UChar, Int }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>Read PLY from a file path. Caller must dispose <paramref name="splats"/>.</summary>
        public static void ReadFromFile(string filePath, out NativeArray<InputSplatData> splats)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"PLY file not found: {filePath}");
            byte[] bytes = File.ReadAllBytes(filePath);
            ReadFromBytes(bytes, filePath, out splats);
        }

        /// <summary>Read PLY from raw bytes. Caller must dispose <paramref name="splats"/>.</summary>
        public static void ReadFromBytes(byte[] plyBytes, out NativeArray<InputSplatData> splats)
            => ReadFromBytes(plyBytes, "<bytes>", out splats);

        // ── Internal ──────────────────────────────────────────────────────────

        static void ReadFromBytes(byte[] plyBytes, string label, out NativeArray<InputSplatData> splats)
        {
            using var ms = new MemoryStream(plyBytes);
            ReadHeader(ms, label, out int vertexCount, out int vertexStride,
                out List<(string name, ElementType type)> attributes);

            string attrError = CheckRequiredAttributes(attributes);
            if (!string.IsNullOrEmpty(attrError))
                throw new IOException($"PLY '{label}' is not a Gaussian Splat file. Missing: {attrError}");

            // Read raw vertex bytes from current stream position
            NativeArray<byte> rawData = new NativeArray<byte>(vertexCount * vertexStride, Allocator.Persistent);
            unsafe
            {
                void* ptr = rawData.GetUnsafePtr();
                byte[] buf = new byte[vertexCount * vertexStride];
                int readBytes = ms.Read(buf, 0, buf.Length);
                if (readBytes != buf.Length)
                    throw new IOException($"PLY '{label}': expected {buf.Length} data bytes, got {readBytes}");
                fixed (byte* src = buf)
                    Buffer.MemoryCopy(src, ptr, buf.Length, buf.Length);
            }

            NativeArray<InputSplatData> tmp = default;
            try
            {
                tmp = PLYDataToSplats(rawData, vertexCount, vertexStride, attributes);
                ReorderSHs(vertexCount, tmp);
                LinearizeData(tmp);
                splats = tmp;
            }
            catch
            {
                if (tmp.IsCreated) tmp.Dispose();
                throw;
            }
            finally
            {
                rawData.Dispose();
            }
        }

        static void ReadHeader(Stream stream, string label,
            out int vertexCount, out int vertexStride,
            out List<(string, ElementType)> attributes)
        {
            vertexCount = 0;
            vertexStride = 0;
            attributes = new List<(string, ElementType)>();
            const int kMaxLines = 9000;
            for (int i = 0; i < kMaxLines; i++)
            {
                string line = ReadLine(stream);
                if (line == "end_header") break;
                if (line.Length == 0) continue;
                var tokens = line.Split(' ');
                if (tokens.Length == 3 && tokens[0] == "element" && tokens[1] == "vertex")
                    vertexCount = int.Parse(tokens[2]);
                if (tokens.Length == 3 && tokens[0] == "property")
                {
                    ElementType type = tokens[1] switch
                    {
                        "float"  => ElementType.Float,
                        "double" => ElementType.Double,
                        "uchar"  => ElementType.UChar,
                        "int"    => ElementType.Int,
                        _        => ElementType.None
                    };
                    vertexStride += TypeToSize(type);
                    attributes.Add((tokens[2], type));
                }
            }
            Debug.Log($"[RuntimePlyReader] {label}: {vertexCount} vertices, stride={vertexStride}, attrs={attributes.Count}");
        }

        static string CheckRequiredAttributes(List<(string, ElementType)> attributes)
        {
            string[] required = { "x", "y", "z", "f_dc_0", "f_dc_1", "f_dc_2",
                                   "opacity", "scale_0", "scale_1", "scale_2",
                                   "rot_0", "rot_1", "rot_2", "rot_3" };
            var missing = required
                .Where(r => !attributes.Contains((r, ElementType.Float)))
                .ToList();
            return missing.Count == 0 ? null : string.Join(", ", missing);
        }

        static unsafe NativeArray<InputSplatData> PLYDataToSplats(
            NativeArray<byte> input, int count, int stride,
            List<(string name, ElementType type)> attributes)
        {
            // Build per-attribute byte offsets
            NativeArray<int> fileAttrOffsets = new NativeArray<int>(attributes.Count, Allocator.Temp);
            int offset = 0;
            for (int ai = 0; ai < attributes.Count; ai++)
            {
                fileAttrOffsets[ai] = offset;
                offset += TypeToSize(attributes[ai].type);
            }

            // Canonical attribute order matches InputSplatData field layout (all float32)
            string[] splatAttrs =
            {
                "x","y","z","nx","ny","nz",
                "f_dc_0","f_dc_1","f_dc_2",
                "f_rest_0","f_rest_1","f_rest_2","f_rest_3","f_rest_4",
                "f_rest_5","f_rest_6","f_rest_7","f_rest_8","f_rest_9",
                "f_rest_10","f_rest_11","f_rest_12","f_rest_13","f_rest_14",
                "f_rest_15","f_rest_16","f_rest_17","f_rest_18","f_rest_19",
                "f_rest_20","f_rest_21","f_rest_22","f_rest_23","f_rest_24",
                "f_rest_25","f_rest_26","f_rest_27","f_rest_28","f_rest_29",
                "f_rest_30","f_rest_31","f_rest_32","f_rest_33","f_rest_34",
                "f_rest_35","f_rest_36","f_rest_37","f_rest_38","f_rest_39",
                "f_rest_40","f_rest_41","f_rest_42","f_rest_43","f_rest_44",
                "opacity","scale_0","scale_1","scale_2","rot_0","rot_1","rot_2","rot_3",
            };
            Assert.AreEqual(UnsafeUtility.SizeOf<InputSplatData>() / 4, splatAttrs.Length);

            NativeArray<int> srcOffsets = new NativeArray<int>(splatAttrs.Length, Allocator.Temp);
            for (int ai = 0; ai < splatAttrs.Length; ai++)
            {
                int attrIndex = attributes.IndexOf((splatAttrs[ai], ElementType.Float));
                srcOffsets[ai] = attrIndex >= 0 ? fileAttrOffsets[attrIndex] : -1;
            }

            NativeArray<InputSplatData> dst = new NativeArray<InputSplatData>(count, Allocator.Persistent);
            ReorderPLYData(count, (byte*)input.GetUnsafeReadOnlyPtr(), stride,
                           (byte*)dst.GetUnsafePtr(), UnsafeUtility.SizeOf<InputSplatData>(),
                           (int*)srcOffsets.GetUnsafeReadOnlyPtr());

            srcOffsets.Dispose();
            fileAttrOffsets.Dispose();
            return dst;
        }

        [BurstCompile]
        static unsafe void ReorderPLYData(
            int splatCount, byte* src, int srcStride,
            byte* dst, int dstStride, int* srcOffsets)
        {
            for (int i = 0; i < splatCount; i++)
            {
                for (int attr = 0; attr < dstStride / 4; attr++)
                {
                    if (srcOffsets[attr] >= 0)
                        *(int*)(dst + attr * 4) = *(int*)(src + srcOffsets[attr]);
                }
                src += srcStride;
                dst += dstStride;
            }
        }

        static unsafe void ReorderSHs(int splatCount, NativeArray<InputSplatData> splats)
        {
            float* data = (float*)splats.GetUnsafePtr();
            int splatStride = UnsafeUtility.SizeOf<InputSplatData>() / 4;
            int shStartOffset = 9, shCount = 15;
            float* tmp = stackalloc float[shCount * 3];
            int idx = shStartOffset;
            for (int i = 0; i < splatCount; i++)
            {
                for (int j = 0; j < shCount; j++)
                {
                    tmp[j * 3 + 0] = data[idx + j];
                    tmp[j * 3 + 1] = data[idx + j + shCount];
                    tmp[j * 3 + 2] = data[idx + j + shCount * 2];
                }
                for (int j = 0; j < shCount * 3; j++)
                    data[idx + j] = tmp[j];
                idx += splatStride;
            }
        }

        [BurstCompile]
        struct LinearizeDataJob : IJobParallelFor
        {
            public NativeArray<InputSplatData> splatData;
            public void Execute(int index)
            {
                var s = splatData[index];
                var q  = s.rot;
                var qq = GaussianUtils.NormalizeSwizzleRotation(new float4(q.x, q.y, q.z, q.w));
                qq = GaussianUtils.PackSmallest3Rotation(qq);
                s.rot     = new Quaternion(qq.x, qq.y, qq.z, qq.w);
                s.scale   = GaussianUtils.LinearScale(s.scale);
                s.dc0     = GaussianUtils.SH0ToColor(s.dc0);
                s.opacity = GaussianUtils.Sigmoid(s.opacity);
                splatData[index] = s;
            }
        }

        static void LinearizeData(NativeArray<InputSplatData> splats)
        {
            var job = new LinearizeDataJob { splatData = splats };
            job.Schedule(splats.Length, 4096).Complete();
        }

        static int TypeToSize(ElementType t) => t switch
        {
            ElementType.Float  => 4,
            ElementType.Double => 8,
            ElementType.UChar  => 1,
            ElementType.Int    => 4,
            _                  => 0
        };

        static string ReadLine(Stream stream)
        {
            var bytes = new List<byte>();
            while (true)
            {
                int b = stream.ReadByte();
                if (b == -1 || b == '\n') break;
                bytes.Add((byte)b);
            }
            if (bytes.Count > 0 && bytes[^1] == '\r') bytes.RemoveAt(bytes.Count - 1);
            return Encoding.UTF8.GetString(bytes.ToArray());
        }
    }
}
