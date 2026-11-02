using SpeechIntent;
using System.IO;
using UnityEngine;

namespace Holodeck.Direct
{
    public sealed class WorldLabsApiConfig : MonoBehaviour
    {
        [Header("Optional API Key")]
        [SerializeField] string worldLabsApiKey = "";
        static bool _reportedMissingConfiguration;

        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(worldLabsApiKey) ||
            HasRuntimeKey();

        public static bool IsWorldLabsConfigured()
        {
            WorldLabsApiConfig sceneConfig = Object.FindFirstObjectByType<WorldLabsApiConfig>(FindObjectsInactive.Include);
            return sceneConfig != null
                ? !string.IsNullOrWhiteSpace(sceneConfig.worldLabsApiKey) || HasRuntimeKey(logMissing: true)
                : HasRuntimeKey(logMissing: true);
        }

        static bool HasRuntimeKey(bool logMissing = false)
        {
            bool hasKey = !string.IsNullOrWhiteSpace(RuntimeDotEnv.GetEnvironmentOrDotEnv("WORLDLABS_API_KEY"));
            if (!hasKey && logMissing)
                ReportMissingConfigurationOnce();

            return hasKey;
        }

        static void ReportMissingConfigurationOnce()
        {
            if (_reportedMissingConfiguration)
                return;

            _reportedMissingConfiguration = true;

            string envPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".env"));
            string message = File.Exists(envPath)
                ? $"[WorldLabsApiConfig] WORLDLABS_API_KEY is missing or empty in '{envPath}'. WorldLabs API calls are disabled."
                : $"[WorldLabsApiConfig] No .env file found at '{envPath}'. Add WORLDLABS_API_KEY before using WorldLabs API features.";

            Debug.LogWarning(message);
        }
    }
}
