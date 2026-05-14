using System;
using System.IO;
using PonyuDev.SherpaOnnx.Vad.Config;
using PonyuDev.SherpaOnnx.Vad.Data;
using SpeechIntent;
using SpeechIntent.VoiceActivation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Holodeck.Editor
{
    public static class VoiceActivationSceneSetup
    {
        const string ConfigAssetPath =
            "Assets/App/Command/SpeechIntent/VoiceActivationConfig.asset";

        [MenuItem("Holodeck/Setup Voice Activation")]
        public static void SetupVoiceActivation()
        {
            GameObject systems = EnsureRootObject("Systems");
            GameObject speechRoot = EnsureChildObject(systems, "SpeechIntent");

            VoiceActivationConfig config = EnsureConfigAsset();

            VadAsrWakeTrigger vadAsrWakeTrigger = GetOrAdd<VadAsrWakeTrigger>(speechRoot);
            KwsWakeTrigger kwsWakeTrigger = GetOrAdd<KwsWakeTrigger>(speechRoot);
            HeadsetHolodeckCommandRouter commandRouter = GetOrAdd<HeadsetHolodeckCommandRouter>(speechRoot);
            HeadsetHolodeckVoiceController voiceController = GetOrAdd<HeadsetHolodeckVoiceController>(speechRoot);

            VoiceCommandRouter existingRouter =
                speechRoot.GetComponent<VoiceCommandRouter>() ??
                UnityEngine.Object.FindFirstObjectByType<VoiceCommandRouter>(FindObjectsInactive.Include);

            Undo.RecordObjects(
                new UnityEngine.Object[]
                {
                    vadAsrWakeTrigger,
                    kwsWakeTrigger,
                    commandRouter,
                    voiceController
                },
                "Wire Voice Activation");

            vadAsrWakeTrigger.config = config;
            kwsWakeTrigger.config = config;

            commandRouter.voiceCommandRouter = existingRouter;

            voiceController.config = config;
            voiceController.commandRouter = commandRouter;

            // Leave wakeTriggerBehaviour empty so VoiceActivationConfig.activationMode
            // selects VadAsrWakeTrigger today or KwsWakeTrigger later.
            voiceController.wakeTriggerBehaviour = null;
            voiceController.commandRecognizerBehaviour = vadAsrWakeTrigger;

            TextMeshProUGUIReference.TryAutoWireStatusText(voiceController);

            EditorUtility.SetDirty(vadAsrWakeTrigger);
            EditorUtility.SetDirty(kwsWakeTrigger);
            EditorUtility.SetDirty(commandRouter);
            EditorUtility.SetDirty(voiceController);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

            if (existingRouter == null)
            {
                Debug.LogWarning(
                    "[VoiceActivationSceneSetup] VoiceCommandRouter was not found. " +
                    "Run Holodeck > Setup SpeechIntent first, then re-run this setup.");
            }

            WarnIfVadSetupLooksInvalid();

            Debug.Log(
                "[VoiceActivationSceneSetup] Done. VoiceActivationConfig is at:\n" +
                ConfigAssetPath +
                "\nSet Activation Mode to VadAsr for today's VAD/ASR wake-word flow, or Kws when a real KWS bridge is ready.");
        }

        static VoiceActivationConfig EnsureConfigAsset()
        {
            VoiceActivationConfig existing =
                AssetDatabase.LoadAssetAtPath<VoiceActivationConfig>(ConfigAssetPath);
            if (existing != null)
                return existing;

            VoiceActivationConfig asset = ScriptableObject.CreateInstance<VoiceActivationConfig>();
            asset.wakeWords.Clear();
            asset.wakeWords.Add("computer");
            asset.activationMode = VoiceActivationMode.VadAsr;
            asset.wakeWordMatchMode = WakeWordMatchMode.StartsWith;
            asset.allowInlineCommands = true;
            asset.commandListenTimeoutSeconds = 7f;
            asset.cooldownSeconds = 0.75f;
            asset.debugLogging = true;

            AssetDatabase.CreateAsset(asset, ConfigAssetPath);
            AssetDatabase.SaveAssets();
            return asset;
        }

        static GameObject EnsureRootObject(string name)
        {
            GameObject existing = GameObject.Find(name);
            if (existing != null)
                return existing;

            GameObject go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "Create " + name);
            return go;
        }

        static GameObject EnsureChildObject(GameObject parent, string name)
        {
            Transform existing = parent.transform.Find(name);
            if (existing != null)
                return existing.gameObject;

            GameObject child = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(child, "Create " + name);
            child.transform.SetParent(parent.transform, false);
            return child;
        }

        static T GetOrAdd<T>(GameObject go) where T : Component
        {
            T existing = go.GetComponent<T>();
            return existing != null ? existing : Undo.AddComponent<T>(go);
        }

        static void WarnIfVadSetupLooksInvalid()
        {
            string settingsPath = Path.Combine(
                Application.streamingAssetsPath,
                "SherpaOnnx",
                "vad-settings.json");

            if (!File.Exists(settingsPath))
            {
                Debug.LogWarning(
                    "[VoiceActivationSceneSetup] Sherpa VAD settings were not found at:\n" +
                    settingsPath +
                    "\nOpen Project Settings > Sherpa-ONNX > VAD and install/select an ONNX Silero VAD profile.");
                return;
            }

            VadSettingsData settings = VadSettingsLoader.Load();
            VadProfile activeProfile = VadSettingsLoader.GetActiveProfile(settings);
            if (activeProfile == null)
            {
                Debug.LogWarning(
                    "[VoiceActivationSceneSetup] Sherpa VAD has no active profile. " +
                    "Open Project Settings > Sherpa-ONNX > VAD and install/select an ONNX Silero VAD profile.");
                return;
            }

            string modelName = activeProfile.model ?? string.Empty;
            string extension = Path.GetExtension(modelName);
            string modelPath = Path.Combine(
                Application.streamingAssetsPath,
                "SherpaOnnx",
                "vad-models",
                activeProfile.profileName ?? string.Empty,
                modelName);

            if (!string.Equals(extension, ".onnx", StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogWarning(
                    $"[VoiceActivationSceneSetup] Active Sherpa VAD profile '{activeProfile.profileName}' uses '{modelName}'. " +
                    "That is not an ONNX VAD model, so wake-word activation will not start in Mac Editor or Quest builds. " +
                    "Install/select silero_vad.onnx in Project Settings > Sherpa-ONNX > VAD.");
                return;
            }

            if (!File.Exists(modelPath))
            {
                Debug.LogWarning(
                    "[VoiceActivationSceneSetup] Active Sherpa VAD model was not found at:\n" +
                    modelPath +
                    "\nMake sure the ONNX VAD model is included under Assets/StreamingAssets/SherpaOnnx/vad-models.");
            }
        }

        static class TextMeshProUGUIReference
        {
            public static void TryAutoWireStatusText(HeadsetHolodeckVoiceController voiceController)
            {
                if (voiceController.statusText != null)
                    return;

                TMPro.TextMeshProUGUI[] texts =
                    UnityEngine.Object.FindObjectsByType<TMPro.TextMeshProUGUI>(
                        FindObjectsInactive.Include,
                        FindObjectsSortMode.None);

                foreach (TMPro.TextMeshProUGUI text in texts)
                {
                    string lowerName = text.gameObject.name.ToLowerInvariant();
                    if (!lowerName.Contains("status"))
                        continue;

                    voiceController.statusText = text;
                    return;
                }
            }
        }
    }
}
