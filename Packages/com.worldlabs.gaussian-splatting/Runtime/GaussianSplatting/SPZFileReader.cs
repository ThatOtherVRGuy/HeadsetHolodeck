// SPDX-License-Identifier: MIT

using System;
using System.IO;
using System.IO.Compression;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace GaussianSplatting.Runtime
{
    // Reads Niantic/Scaniverse .SPZ files:
    // https://github.com/nianticlabs/spz
    // https://scaniverse.com/spz
    [BurstCompile]
    public static class SPZFileReader
    {
        public static void ReadFileHeader(string filePath, out int vertexCount)
        {
            vertexCount = 0;
            if (!File.Exists(filePath))
                return;
            using var fs = File.OpenRead(filePath);
            using var gz = new GZipStream(fs, CompressionMode.Decompress);
            ReadHeaderImpl(filePath, gz, out vertexCount, out _, out _, out _);
        }

        static void ReadHeaderImpl(string label, Stream stream, out int vertexCount, out int shLevel, out int fractBits, out int flags)
        {
            // Use a plain managed byte[] — Allocator.Temp is forbidden on threadpool threads
            // (e.g. when called from Task.Run), so we avoid NativeArray for this tiny read.
            var buf = new byte[16];
            int readBytes = stream.Read(buf, 0, 16);
            if (readBytes != 16)
                throw new IOException($"SPZ {label} read error, failed to read header");

            uint magic     = BitConverter.ToUInt32(buf, 0);
            uint version   = BitConverter.ToUInt32(buf, 4);
            uint numPoints = BitConverter.ToUInt32(buf, 8);
            uint shFlags   = BitConverter.ToUInt32(buf, 12);

            if (magic != 0x5053474e)
                throw new IOException($"SPZ {label} read error, header magic unexpected {magic}");
            if (version != 2)
                throw new IOException($"SPZ {label} read error, header version unexpected {version}");

            vertexCount = (int)numPoints;
            shLevel     = (int)(shFlags & 0xFF);
            fractBits   = (int)((shFlags >> 8) & 0xFF);
            flags       = (int)((shFlags >> 16) & 0xFF);
        }

        static int SHCoeffsForLevel(int level)
        {
            return level switch
            {
                0 => 0,
                1 => 3,
                2 => 8,
                3 => 15,
                _ => 0
            };
        }

        /// <summary>Read SPZ from a file on disk.</summary>
        public static void ReadFile(string filePath, out NativeArray<InputSplatData> splats)
        {
            using var fs = File.OpenRead(filePath);
            using var gz = new GZipStream(fs, CompressionMode.Decompress);
            ReadImpl(filePath, gz, out splats);
        }

        /// <summary>Read SPZ from raw (compressed) bytes already in memory.</summary>
        public static void ReadFile(byte[] compressedBytes, out NativeArray<InputSplatData> splats)
        {
            using var ms = new MemoryStream(compressedBytes);
            using var gz = new GZipStream(ms, CompressionMode.Decompress);
            ReadImpl("<in-memory>", gz, out splats);
        }

        static void ReadImpl(string label, Stream decompressedStream, out NativeArray<InputSplatData> splats)
        {
            ReadHeaderImpl(label, decompressedStream, out var splatCount, out var shLevel, out var fractBits, out _);

            if (splatCount < 1 || splatCount > 10_000_000)
                throw new IOException($"SPZ {label} read error, out of range splat count {splatCount}");
            if (shLevel < 0 || shLevel > 3)
                throw new IOException($"SPZ {label} read error, out of range SH level {shLevel}");
            if (fractBits < 0 || fractBits > 24)
                throw new IOException($"SPZ {label} read error, out of range fractional bits {fractBits}");

            int shCoeffs = SHCoeffsForLevel(shLevel);
            byte[] packedPos   = ReadExactly(decompressedStream, splatCount * 3 * 3);
            byte[] packedAlpha = ReadExactly(decompressedStream, splatCount);
            byte[] packedCol   = ReadExactly(decompressedStream, splatCount * 3);
            byte[] packedScale = ReadExactly(decompressedStream, splatCount * 3);
            byte[] packedRot   = ReadExactly(decompressedStream, splatCount * 3);
            byte[] packedSh    = ReadExactly(decompressedStream, splatCount * 3 * shCoeffs);

            splats = new NativeArray<InputSplatData>(splatCount, Allocator.Persistent);
            var job = new UnpackDataJob
            {
                packedPos   = packedPos,
                packedScale = packedScale,
                packedRot   = packedRot,
                packedAlpha = packedAlpha,
                packedCol   = packedCol,
                packedSh    = packedSh,
                shCoeffs    = shCoeffs,
                fractScale  = 1.0f / (1 << fractBits),
                splats      = splats
            };
            // Runtime SPZ decoding already runs on a background Task. On Quest,
            // scheduling this unpack job through Burst corrupts some int24 positions
            // (observed as -4096 coordinate sentinels), so execute the same decoder
            // deterministically on this worker thread instead.
            for (int i = 0; i < splatCount; ++i)
                job.Execute(i);

        }

        static byte[] ReadExactly(Stream stream, int count)
        {
            byte[] buffer = new byte[count];
            int offset = 0;
            while (offset < count)
            {
                int read = stream.Read(buffer, offset, count - offset);
                if (read <= 0)
                    throw new IOException($"SPZ stream ended at {offset} bytes; expected {count}.");
                offset += read;
            }
            return buffer;
        }

        struct UnpackDataJob
        {
            public byte[] packedPos;
            public byte[] packedScale;
            public byte[] packedRot;
            public byte[] packedAlpha;
            public byte[] packedCol;
            public byte[] packedSh;
            public float fractScale;
            public int shCoeffs;
            public NativeArray<InputSplatData> splats;

            public void Execute(int index)
            {
                var splat = splats[index];

                splat.pos = new Vector3(
                    UnpackFloat(index * 3 + 0) * fractScale,
                    UnpackFloat(index * 3 + 1) * fractScale,
                    UnpackFloat(index * 3 + 2) * fractScale);

                splat.scale = new Vector3(
                    packedScale[index * 3 + 0],
                    packedScale[index * 3 + 1],
                    packedScale[index * 3 + 2]) / 16.0f - new Vector3(10.0f, 10.0f, 10.0f);
                splat.scale = GaussianUtils.LinearScale(splat.scale);

                Vector3 xyz = new Vector3(
                    packedRot[index * 3 + 0],
                    packedRot[index * 3 + 1],
                    packedRot[index * 3 + 2]) * (1.0f / 127.5f) - new Vector3(1, 1, 1);
                float w = math.sqrt(math.max(0.0f, 1.0f - xyz.sqrMagnitude));
                var q = new float4(xyz.x, xyz.y, xyz.z, w);
                var qq = math.normalize(q);
                qq = GaussianUtils.PackSmallest3Rotation(qq);
                splat.rot = new Quaternion(qq.x, qq.y, qq.z, qq.w);

                splat.opacity = packedAlpha[index] / 255.0f;

                Vector3 col = new Vector3(
                    packedCol[index * 3 + 0],
                    packedCol[index * 3 + 1],
                    packedCol[index * 3 + 2]);
                col = col / 255.0f - new Vector3(0.5f, 0.5f, 0.5f);
                col /= 0.15f;
                splat.dc0 = GaussianUtils.SH0ToColor(col);

                if (shCoeffs > 0)
                {
                    int shIdx = index * shCoeffs * 3;
                    splat.sh1 = UnpackSH(shIdx); shIdx += 3;
                    splat.sh2 = UnpackSH(shIdx); shIdx += 3;
                    splat.sh3 = UnpackSH(shIdx); shIdx += 3;
                    if (shCoeffs > 3)
                    {
                        splat.sh4 = UnpackSH(shIdx); shIdx += 3;
                        splat.sh5 = UnpackSH(shIdx); shIdx += 3;
                        splat.sh6 = UnpackSH(shIdx); shIdx += 3;
                        splat.sh7 = UnpackSH(shIdx); shIdx += 3;
                        splat.sh8 = UnpackSH(shIdx); shIdx += 3;
                        if (shCoeffs > 8)
                        {
                            splat.sh9 = UnpackSH(shIdx); shIdx += 3;
                            splat.shA = UnpackSH(shIdx); shIdx += 3;
                            splat.shB = UnpackSH(shIdx); shIdx += 3;
                            splat.shC = UnpackSH(shIdx); shIdx += 3;
                            splat.shD = UnpackSH(shIdx); shIdx += 3;
                            splat.shE = UnpackSH(shIdx); shIdx += 3;
                            splat.shF = UnpackSH(shIdx); shIdx += 3;
                        }
                    }
                }

                splats[index] = splat;
            }

            float UnpackFloat(int idx)
            {
                int fx = packedPos[idx * 3 + 0] | (packedPos[idx * 3 + 1] << 8) | (packedPos[idx * 3 + 2] << 16);
                fx |= (fx & 0x800000) != 0 ? -16777216 : 0;
                return fx;
            }

            Vector3 UnpackSH(int idx)
            {
                Vector3 sh = new Vector3(packedSh[idx], packedSh[idx + 1], packedSh[idx + 2]) - new Vector3(128.0f, 128.0f, 128.0f);
                sh /= 128.0f;
                return sh;
            }
        }
    }
}
