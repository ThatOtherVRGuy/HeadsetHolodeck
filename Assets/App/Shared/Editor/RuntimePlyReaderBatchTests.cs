using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using GaussianSplatting.Runtime;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using WorldLabs.Runtime.Tools;

namespace HeadsetHolodeck.EditorTests
{
    public static class RuntimePlyReaderBatchTests
    {
        public static void RunBackgroundParseTest()
        {
            NativeArray<InputSplatData> splats = default;
            try
            {
                byte[] ply = CreateMinimalBinaryGaussianPly();
                Task.Run(() => RuntimePlyReader.ReadFromBytes(ply, out splats))
                    .GetAwaiter()
                    .GetResult();

                if (!splats.IsCreated || splats.Length != 1)
                    throw new InvalidOperationException("Expected one parsed splat.");

                Debug.Log("[RuntimePlyReaderBatchTests] Background PLY parse passed.");
            }
            catch (Exception ex)
            {
                Debug.LogError("[RuntimePlyReaderBatchTests] Background PLY parse failed: " + ex);
                EditorApplication.Exit(1);
                throw;
            }
            finally
            {
                if (splats.IsCreated)
                    splats.Dispose();
            }
        }

        static byte[] CreateMinimalBinaryGaussianPly()
        {
            string[] properties =
            {
                "x", "y", "z",
                "f_dc_0", "f_dc_1", "f_dc_2",
                "opacity",
                "scale_0", "scale_1", "scale_2",
                "rot_0", "rot_1", "rot_2", "rot_3"
            };

            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);

            writer.Write(System.Text.Encoding.ASCII.GetBytes("ply\n"));
            writer.Write(System.Text.Encoding.ASCII.GetBytes("format binary_little_endian 1.0\n"));
            writer.Write(System.Text.Encoding.ASCII.GetBytes("element vertex 1\n"));
            foreach (string property in properties)
                writer.Write(System.Text.Encoding.ASCII.GetBytes("property float " + property + "\n"));
            writer.Write(System.Text.Encoding.ASCII.GetBytes("end_header\n"));

            var values = new Dictionary<string, float>
            {
                ["x"] = 0f,
                ["y"] = 0f,
                ["z"] = 0f,
                ["f_dc_0"] = 0.5f,
                ["f_dc_1"] = 0.5f,
                ["f_dc_2"] = 0.5f,
                ["opacity"] = 1f,
                ["scale_0"] = -2f,
                ["scale_1"] = -2f,
                ["scale_2"] = -2f,
                ["rot_0"] = 1f,
                ["rot_1"] = 0f,
                ["rot_2"] = 0f,
                ["rot_3"] = 0f
            };

            foreach (string property in properties)
                writer.Write(values[property]);

            return stream.ToArray();
        }
    }
}
