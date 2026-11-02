using System;
using System.Reflection;
using SpeechIntent;
using UnityEditor;
using UnityEngine;

namespace HeadsetHolodeck.EditorTests
{
    public static class LocalRemoteSplatLoaderBatchTests
    {
        public static void RunFailureReportingTests()
        {
            try
            {
                TestLoadFailureReportsStatusEventAndSpeech();
                TestRendererDiagnosticsDescribeRuntimeState();
                Debug.Log("[LocalRemoteSplatLoaderBatchTests] Failure reporting tests passed.");
            }
            catch (Exception ex)
            {
                Debug.LogError("[LocalRemoteSplatLoaderBatchTests] Failure reporting tests failed: " + ex);
                EditorApplication.Exit(1);
                throw;
            }
        }

        static void TestRendererDiagnosticsDescribeRuntimeState()
        {
            MethodInfo diagnostics = typeof(LocalRemoteSplatLoader).GetMethod(
                "BuildRendererDiagnostics",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            AssertTrue(diagnostics != null,
                "LocalRemoteSplatLoader should provide a renderer diagnostic summary for device load tracing.");

            GameObject loaderObject = new GameObject("LoaderDiagnostics_Test");
            GameObject rendererObject = new GameObject("RendererDiagnostics_Test");
            GameObject cameraObject = new GameObject("CameraDiagnostics_Test");
            try
            {
                LocalRemoteSplatLoader loader = loaderObject.AddComponent<LocalRemoteSplatLoader>();
                GaussianSplatting.Runtime.GaussianSplatRenderer renderer =
                    rendererObject.AddComponent<GaussianSplatting.Runtime.GaussianSplatRenderer>();
                Camera camera = cameraObject.AddComponent<Camera>();

                string summary = (string)diagnostics.Invoke(null, new object[] { "test", renderer, camera });
                AssertTrue(summary.Contains("phase=test"), "Diagnostic summary should include its phase.");
                AssertTrue(summary.Contains("renderer=RendererDiagnostics_Test"), "Diagnostic summary should identify the renderer.");
                AssertTrue(summary.Contains("camera=CameraDiagnostics_Test"), "Diagnostic summary should identify the camera.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(loaderObject);
                UnityEngine.Object.DestroyImmediate(rendererObject);
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }

        static void TestLoadFailureReportsStatusEventAndSpeech()
        {
            GameObject go = new GameObject("LocalRemoteSplatLoader_FailureReporting_Test");
            try
            {
                LocalRemoteSplatLoader loader = go.AddComponent<LocalRemoteSplatLoader>();
                TtsPlayer tts = go.AddComponent<TtsPlayer>();
                tts.delayBeforeSpeaking = 0f;

                string eventMessage = null;
                loader.onLoadFailed = new StringEvent();
                loader.onLoadFailed.AddListener(message => eventMessage = message);

                FieldInfo ttsField = typeof(LocalRemoteSplatLoader).GetField(
                    "voiceFeedback",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                AssertTrue(ttsField != null, "LocalRemoteSplatLoader should expose a voiceFeedback field.");
                ttsField.SetValue(loader, tts);

                MethodInfo reporter = typeof(LocalRemoteSplatLoader).GetMethod(
                    "ReportLoadFailure",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                AssertTrue(reporter != null, "LocalRemoteSplatLoader should have a shared ReportLoadFailure method.");

                string detailed = "PLY parse failed: allocator explosion.";
                string spoken = "Could not load that PLY file.";
                reporter.Invoke(loader, new object[] { "local_test", detailed, spoken });

                AssertEqual(detailed, ArchStatusBus.LastMessage.message, "Detailed failure should be posted to status UI.");
                AssertEqual(ArchStatusLevel.Error, ArchStatusBus.LastMessage.level, "Failure should be posted as an error.");
                AssertEqual("LOAD", ArchStatusBus.LastMessage.mode, "Failure should use LOAD status mode.");
                AssertEqual(detailed, eventMessage, "Failure event should receive the detailed message.");
                AssertEqual(spoken, tts.textToSpeak, "TTS should receive the short spoken failure message.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        static void AssertTrue(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }

        static void AssertEqual<T>(T expected, T actual, string message)
        {
            if (!Equals(expected, actual))
                throw new InvalidOperationException($"{message} Expected {expected}, got {actual}.");
        }
    }
}
