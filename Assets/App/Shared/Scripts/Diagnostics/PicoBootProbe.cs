using UnityEngine;

namespace Holodeck.Diagnostics
{
    /// <summary>
    /// Temporary boot instrumentation for Pico emulator bring-up.
    ///
    /// The player stalls before any scene code runs (no XR session is created and the last engine log is
    /// "applyWindowUIChanges"), so we need markers between engine init and first scene to tell how far the
    /// runtime actually gets. Remove once Pico startup is understood.
    /// </summary>
    public static class PicoBootProbe
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void SubsystemRegistration()
        {
            Debug.Log("[BootProbe] SubsystemRegistration");
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        static void AfterAssembliesLoaded()
        {
            Debug.Log("[BootProbe] AfterAssembliesLoaded");
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
        static void BeforeSplashScreen()
        {
            Debug.Log("[BootProbe] BeforeSplashScreen");
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void BeforeSceneLoad()
        {
            Debug.Log($"[BootProbe] BeforeSceneLoad graphics={SystemInfo.graphicsDeviceType} " +
                      $"xrActive={UnityEngine.XR.XRSettings.isDeviceActive} " +
                      $"xrLoaded={UnityEngine.XR.XRSettings.isDeviceActive || UnityEngine.XR.XRSettings.enabled}");
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AfterSceneLoad()
        {
            Debug.Log($"[BootProbe] AfterSceneLoad scene={UnityEngine.SceneManagement.SceneManager.GetActiveScene().name} " +
                      $"roots={UnityEngine.SceneManagement.SceneManager.GetActiveScene().rootCount}");
        }
    }
}
