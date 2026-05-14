using SpeechIntent;
using UnityEngine;

namespace Holodeck.Direct
{
    public sealed class WorldLabsApiConfig : MonoBehaviour
    {
        [Header("Optional API Key")]
        [SerializeField] string worldLabsApiKey = "";

        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(worldLabsApiKey) ||
            HasRuntimeKey();

        public static bool IsWorldLabsConfigured()
        {
            WorldLabsApiConfig sceneConfig = Object.FindFirstObjectByType<WorldLabsApiConfig>(FindObjectsInactive.Include);
            return sceneConfig != null ? sceneConfig.IsConfigured : HasRuntimeKey();
        }

        static bool HasRuntimeKey()
        {
            return !string.IsNullOrWhiteSpace(RuntimeDotEnv.GetEnvironmentOrDotEnv("WORLDLABS_API_KEY"));
        }
    }
}
