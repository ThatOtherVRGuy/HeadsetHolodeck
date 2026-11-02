using SpeechIntent;
using UnityEngine;

namespace Holodeck.Direct
{
    public sealed class ObjectGenerationApiConfig : MonoBehaviour
    {
        [Header("Optional API Keys")]
        [SerializeField] string meshyApiKey = "";
        [SerializeField] string tripoApiKey = "";
        [SerializeField] string threeDAIStudioApiKey = "";
        [SerializeField] string hitemAccessKey = "";
        [SerializeField] string hitemSecretKey = "";

        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(meshyApiKey) ||
            !string.IsNullOrWhiteSpace(tripoApiKey) ||
            !string.IsNullOrWhiteSpace(threeDAIStudioApiKey) ||
            (!string.IsNullOrWhiteSpace(hitemAccessKey) && !string.IsNullOrWhiteSpace(hitemSecretKey)) ||
            HasRuntimeKey();

        public static bool IsAnyProviderConfigured()
        {
            ObjectGenerationApiConfig sceneConfig = Object.FindFirstObjectByType<ObjectGenerationApiConfig>(FindObjectsInactive.Include);
            return sceneConfig != null ? sceneConfig.IsConfigured : HasRuntimeKey();
        }

        static bool HasRuntimeKey()
        {
            return HasRuntimeKey("MESHY_API_KEY") ||
                   HasRuntimeKey("TRIPO_API_KEY") ||
                   HasRuntimeKey("THREEDAISTUDIO_API_KEY") ||
                   (HasRuntimeKey("HITEM_ACCESS_KEY") && HasRuntimeKey("HITEM_SECRET_KEY"));
        }

        static bool HasRuntimeKey(string key)
        {
            return !string.IsNullOrWhiteSpace(RuntimeDotEnv.GetEnvironmentOrDotEnv(key));
        }
    }
}
