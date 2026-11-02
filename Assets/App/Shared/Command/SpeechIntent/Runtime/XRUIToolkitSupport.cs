using UnityEngine;

namespace SpeechIntent
{
    /// <summary>
    /// Ensures XRI's <c>XRUIToolkitManager</c> exists, which is what enables UI interaction for every XRI
    /// interactor in the scene.
    ///
    /// Without it, <c>XRUIToolkitHandler.uiToolkitSupportEnabled</c> stays false, and an interactor's own
    /// <c>enableUIInteraction</c> is ANDed with it:
    ///     enableUIInteraction => m_EnableUIInteraction &amp;&amp; XRUIToolkitHandler.uiToolkitSupportEnabled
    /// The visible effect is exactly what we saw on hardware: rays track and render, hover never happens and
    /// nothing can be clicked. Nothing logs it, because every component looks correctly configured.
    ///
    /// Holodeck.unity has no XRUIToolkitManager (only the XRI sample scene does), so this installs one at
    /// runtime rather than relying on someone remembering to add a component. The type is resolved by
    /// reflection so this file does not take a hard dependency on the XRI UI assembly.
    /// </summary>
    public static class XRUIToolkitSupport
    {
        const string TypeName = "UnityEngine.XR.Interaction.Toolkit.UI.XRUIToolkitManager";

        static bool _installed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            EnsureInstalled();
        }

        public static bool EnsureInstalled()
        {
            if (_installed) return true;

            System.Type managerType = FindType(TypeName);
            if (managerType == null)
            {
                Debug.LogWarning($"[XRUIToolkitSupport] {TypeName} not found. UI interaction will stay " +
                                 "disabled for every interactor; use ray hover only as a fallback.");
                return false;
            }

            var existing = Object.FindFirstObjectByType(managerType, FindObjectsInactive.Include) as Component;
            if (existing != null)
            {
                _installed = true;
                Debug.Log($"[XRUIToolkitSupport] XRUIToolkitManager already present on '{existing.gameObject.name}'.");
                return true;
            }

            var host = new GameObject("XR UI Toolkit Manager");
            Object.DontDestroyOnLoad(host);
            host.AddComponent(managerType);
            _installed = true;

            Debug.Log("[XRUIToolkitSupport] Added XRUIToolkitManager: XRI UI interaction enabled " +
                      "(rays can now hover and select UI).");
            return true;
        }

        static System.Type FindType(string fullName)
        {
            System.Type direct = System.Type.GetType(fullName);
            if (direct != null) return direct;

            foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                System.Type type = assembly.GetType(fullName);
                if (type != null) return type;
            }

            return null;
        }
    }
}
