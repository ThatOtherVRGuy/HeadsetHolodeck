using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace HeadsetHolodeck.EditorTests
{
    public static class AndroidAudioRecordBridgeBatchTests
    {
        public static void NativeAudioRecorderImplementsFallbackSourceContract()
        {
            try
            {
                string sourcePath = Path.Combine(
                    Application.dataPath,
                    "Plugins/SherpaOnnx/Android/AndroidAudioRecorder.java");
                string source = File.ReadAllText(sourcePath);

                RequireMethod(source, "hasNextSource");
                RequireMethod(source, "getCurrentSourceName");
                RequireMethod(source, "restartWithNextSource");

                Debug.Log("[AndroidAudioRecordBridgeBatchTests] Native fallback source contract test passed.");
            }
            catch (Exception ex)
            {
                Debug.LogError("[AndroidAudioRecordBridgeBatchTests] Native fallback source contract test failed: " + ex);
                EditorApplication.Exit(1);
                throw;
            }
        }

        static void RequireMethod(string source, string methodName)
        {
            if (!source.Contains(methodName + "("))
                throw new InvalidOperationException(
                    "AndroidAudioRecorder must implement " + methodName +
                    " because AndroidAudioRecordBridge calls it after native fallback silence.");
        }
    }
}
