using System;
using System.IO;
using System.Security.Cryptography;
using GaussianSplatting.Runtime;
using Unity.Collections;
using UnityEditor;
using UnityEngine;

namespace HeadsetHolodeck.EditorTests
{
    public static class RuntimeSplatProcessingBatchTests
    {
        public static void BackgroundProcessingDoesNotUseFrameLimitedAllocators()
        {
            try
            {
                string sourcePath = Path.Combine(
                    Application.dataPath,
                    "../Packages/com.worldlabs.gaussian-splatting/Runtime/GaussianSplatting/RuntimeSplatProcessing.cs");
                string source = File.ReadAllText(sourcePath);

                if (source.Contains("Allocator.TempJob"))
                    throw new InvalidOperationException(
                        "RuntimeSplatProcessing runs inside Task.Run for runtime splats and must not use " +
                        "Allocator.TempJob, whose four-frame lifetime can expire before processing finishes.");

                Debug.Log("[RuntimeSplatProcessingBatchTests] Background processing allocator test passed.");
            }
            catch (Exception ex)
            {
                Debug.LogError("[RuntimeSplatProcessingBatchTests] Background processing allocator test failed: " + ex);
                EditorApplication.Exit(1);
                throw;
            }
        }

        public static void BoundsJobDoesNotWriteThroughStackPointers()
        {
            try
            {
                string sourcePath = Path.Combine(
                    Application.dataPath,
                    "../Packages/com.worldlabs.gaussian-splatting/Runtime/GaussianSplatting/RuntimeSplatProcessing.cs");
                string source = File.ReadAllText(sourcePath);

                if (source.Contains("float3* m_BoundsMin") ||
                    source.Contains("float3* m_BoundsMax") ||
                    !source.Contains("NativeArray<float3> m_Bounds"))
                    throw new InvalidOperationException(
                        "CalcBoundsJob must return bounds through NativeArray, not stack pointers passed to a worker thread.");

                Debug.Log("[RuntimeSplatProcessingBatchTests] Bounds-job native-result contract passed.");
            }
            catch (Exception ex)
            {
                Debug.LogError("[RuntimeSplatProcessingBatchTests] Bounds-job contract failed: " + ex);
                EditorApplication.Exit(1);
                throw;
            }
        }

        public static void DiagnoseQuestCozyCabinSpz()
        {
            const string spzPath = "/tmp/headsetholodeck-quest-cache/CozyCabin.spz";
            try
            {
                if (!File.Exists(spzPath))
                    throw new FileNotFoundException("Pull the Quest Cozy Cabin cache before running this diagnostic.", spzPath);

                byte[] bytes = File.ReadAllBytes(spzPath);
                string hash;
                using (SHA256 sha256 = SHA256.Create())
                    hash = BitConverter.ToString(sha256.ComputeHash(bytes)).Replace("-", string.Empty);
                RuntimeSplatData first = RuntimeSplatProcessing.ProcessSPZBytes(bytes);
                RuntimeSplatData second = RuntimeSplatProcessing.ProcessSPZBytes(bytes);

                if (first.boundsMin.x != second.boundsMin.x || first.boundsMin.y != second.boundsMin.y || first.boundsMin.z != second.boundsMin.z ||
                    first.boundsMax.x != second.boundsMax.x || first.boundsMax.y != second.boundsMax.y || first.boundsMax.z != second.boundsMax.z)
                    throw new InvalidOperationException("SPZ processing produced non-deterministic bounds for identical bytes.");

                Debug.Log($"[RuntimeSplatProcessingBatchTests] Cozy Cabin: bytes={bytes.Length}; sha256={hash}; " +
                          $"boundsMin={first.boundsMin}; boundsMax={first.boundsMax}; splats={first.splatCount}.");
            }
            catch (Exception ex)
            {
                Debug.LogError("[RuntimeSplatProcessingBatchTests] Cozy Cabin diagnostic failed: " + ex);
                EditorApplication.Exit(1);
                throw;
            }
        }

    }
}
