using UnityEngine;

namespace SpeechIntent
{
    public class RuntimeProxyVisibilityController : MonoBehaviour
    {
        public string LastFailureMessage { get; private set; } = "";

        public int SetVisibility(string categoryText, bool visible)
        {
            LastFailureMessage = "";
            RuntimeProxyCategory? category = ParseCategory(categoryText);
            RuntimeProxyVisual[] proxies = FindObjectsByType<RuntimeProxyVisual>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            int changed = 0;

            foreach (RuntimeProxyVisual proxy in proxies)
            {
                if (proxy == null)
                    continue;

                if (category.HasValue && proxy.category != category.Value)
                    continue;

                proxy.SetVisible(visible);
                changed++;
            }

            if (changed == 0)
                LastFailureMessage = category.HasValue ? $"No {category.Value.ToString().ToLowerInvariant()} proxies found." : "No proxies found.";

            return changed;
        }

        static RuntimeProxyCategory? ParseCategory(string value)
        {
            string normalized = (value ?? "").Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(normalized) || normalized == "all" || normalized == "proxy" || normalized == "proxies")
                return null;

            if (normalized.Contains("audio") || normalized.Contains("sound"))
                return RuntimeProxyCategory.Audio;

            if (normalized.Contains("light") || normalized.Contains("sun") || normalized.Contains("flashlight"))
                return RuntimeProxyCategory.Light;

            return null;
        }
    }
}
