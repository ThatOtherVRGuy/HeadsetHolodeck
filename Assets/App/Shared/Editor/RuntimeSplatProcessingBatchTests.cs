using System;
using System.IO;
using System.Security.Cryptography;
using System.Reflection;
using GaussianSplatting.Runtime;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using WorldLabs.Runtime.Tools;

namespace HeadsetHolodeck.EditorTests
{
    public static class RuntimeSplatProcessingBatchTests
    {
        public static void RuntimeSplatFloorLoaderUsesQuestRendererPath()
        {
            try
            {
                MethodInfo shouldOptimize = typeof(RuntimeSplatFloorLoader).GetMethod(
                    "ShouldOptimizeForQuest",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

                if (shouldOptimize == null)
                    throw new InvalidOperationException(
                        "RuntimeSplatFloorLoader must centralize its Quest renderer configuration.");

                AssertEqual(true, (bool)shouldOptimize.Invoke(null, new object[] { RuntimePlatform.Android, RuntimeSplatFloorLoader.SplatSourceKind.WorldLabs }),
                    "Runtime-created renderers must enable Optimize for Quest on Android.");
                AssertEqual(false, (bool)shouldOptimize.Invoke(null, new object[] { RuntimePlatform.Android, RuntimeSplatFloorLoader.SplatSourceKind.LooseSplat }),
                    "Loose splats must avoid the Quest vertex-position override while testing Meta/Hyperscape compatibility.");
                AssertEqual(false, (bool)shouldOptimize.Invoke(null, new object[] { RuntimePlatform.OSXEditor, RuntimeSplatFloorLoader.SplatSourceKind.WorldLabs }),
                    "Runtime-created renderers must retain the desktop renderer path in the Editor.");

                MethodInfo shOrder = typeof(RuntimeSplatFloorLoader).GetMethod(
                    "GetSHOrderForSource",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

                if (shOrder == null)
                    throw new InvalidOperationException(
                        "RuntimeSplatFloorLoader must centralize its source-specific SH compatibility policy.");

                AssertEqual(0, (int)shOrder.Invoke(null, new object[] { RuntimeSplatFloorLoader.SplatSourceKind.LooseSplat }),
                    "Loose splats must use base color until their proprietary SH convention is supported.");
                AssertEqual(3, (int)shOrder.Invoke(null, new object[] { RuntimeSplatFloorLoader.SplatSourceKind.WorldLabs }),
                    "WorldLabs splats must preserve the renderer's full SH order.");

                Debug.Log("[RuntimeSplatProcessingBatchTests] Quest renderer configuration test passed.");
            }
            catch (Exception ex)
            {
                Debug.LogError("[RuntimeSplatProcessingBatchTests] Quest renderer configuration test failed: " + ex);
                EditorApplication.Exit(1);
                throw;
            }
        }

        public static void RuntimeSplatFloorLoaderUsesMetaHyperscapeCompatibilityScale()
        {
            try
            {
                MethodInfo compatibilityScale = typeof(RuntimeSplatFloorLoader).GetMethod(
                    "GetCompatibilityScaleForSource",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

                if (compatibilityScale == null)
                    throw new InvalidOperationException(
                        "RuntimeSplatFloorLoader must centralize metadata-specific loose SPZ compatibility scaling.");

                AssertNearlyEqual(0.001f, (float)compatibilityScale.Invoke(null, new object[] { RuntimeSplatFloorLoader.SplatSourceKind.LooseSplat, 2, 3, 0x0c }),
                    "Meta/Hyperscape-style loose SPZ files should be scaled from millimeters to meters.");
                AssertNearlyEqual(1f, (float)compatibilityScale.Invoke(null, new object[] { RuntimeSplatFloorLoader.SplatSourceKind.LooseSplat, 2, 0, 0x00 }),
                    "Ordinary loose SPZ files should not receive Meta/Hyperscape scaling.");
                AssertNearlyEqual(1f, (float)compatibilityScale.Invoke(null, new object[] { RuntimeSplatFloorLoader.SplatSourceKind.WorldLabs, 2, 3, 0x0c }),
                    "WorldLabs splats should not receive loose-file compatibility scaling.");

                Debug.Log("[RuntimeSplatProcessingBatchTests] Meta/Hyperscape compatibility scale test passed.");
            }
            catch (Exception ex)
            {
                Debug.LogError("[RuntimeSplatProcessingBatchTests] Meta/Hyperscape compatibility scale test failed: " + ex);
                EditorApplication.Exit(1);
                throw;
            }
        }

        public static void RuntimeSplatFloorLoaderClampsExtremeMetaHyperscapeAnisotropy()
        {
            NativeArray<InputSplatData> splats = default;
            try
            {
                MethodInfo clamp = typeof(RuntimeSplatFloorLoader).GetMethod(
                    "ClampSplatScaleAnisotropy",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

                if (clamp == null)
                    throw new InvalidOperationException(
                        "RuntimeSplatFloorLoader must expose a bounded Meta/Hyperscape anisotropy compatibility pass.");

                splats = new NativeArray<InputSplatData>(1, Allocator.Persistent);
                splats[0] = new InputSplatData
                {
                    scale = new Vector3(0.0001f, 0.01f, 10f)
                };

                clamp.Invoke(null, new object[] { splats, 64f, "test" });

                Vector3 scale = splats[0].scale;
                float min = Mathf.Min(scale.x, Mathf.Min(scale.y, scale.z));
                float max = Mathf.Max(scale.x, Mathf.Max(scale.y, scale.z));
                AssertNearlyEqual(64f, max / min,
                    "Extreme Meta/Hyperscape anisotropy should be clamped to the configured max axis ratio.");
                AssertNearlyEqual(10f, max,
                    "Clamping should preserve the dominant axis and expand only tiny axes.");

                Debug.Log("[RuntimeSplatProcessingBatchTests] Meta/Hyperscape anisotropy clamp test passed.");
            }
            catch (Exception ex)
            {
                Debug.LogError("[RuntimeSplatProcessingBatchTests] Meta/Hyperscape anisotropy clamp test failed: " + ex);
                EditorApplication.Exit(1);
                throw;
            }
            finally
            {
                if (splats.IsCreated)
                    splats.Dispose();
            }
        }

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

        static void AssertEqual<T>(T expected, T actual, string message)
        {
            if (!Equals(expected, actual))
                throw new InvalidOperationException($"{message} Expected {expected}, got {actual}.");
        }

        static void AssertNearlyEqual(float expected, float actual, string message)
        {
            if (Mathf.Abs(expected - actual) > 0.000001f)
                throw new InvalidOperationException($"{message} Expected {expected}, got {actual}.");
        }

    }
}
