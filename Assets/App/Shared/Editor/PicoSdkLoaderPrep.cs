using System.IO;
using UnityEditor;
using UnityEngine;

namespace Holodeck.Editor
{
    /// <summary>
    /// Works around a PICO SDK packaging defect that blocks every Pico build.
    ///
    /// With OpenXR >= 1.16 the SDK compiles
    /// <c>CustomRuntimeLoaderName = "LoaderForUnitySDK_1_1_0"</c> into <c>PICOFeature</c>, and Unity's
    /// OpenXR build step then requires a plugin importer whose asset path contains that string
    /// (<c>OpenXRChooseRuntimeLibraries.FindImportersForLoader</c>). The package ships only
    /// <c>LoaderForUnitySDK.aar</c> — on every branch, with no tags — so the build fails with
    /// "Build process couldn't find the Plugin Importer for the OpenXR Loader library".
    ///
    /// Renaming the asset inside the resolved package satisfies the lookup. The package cache is
    /// regenerated on the next resolve, so this runs before each Pico build instead of being a
    /// one-off manual fix. Replace with a pinned SDK that ships the versioned name when one exists.
    /// </summary>
    public static class PicoSdkLoaderPrep
    {
        const string PackageName = "com.bytedance.pico.xr";
        const string VersionedName = "LoaderForUnitySDK_1_1_0";
        const string ShippedName = "LoaderForUnitySDK";

        /// <summary>
        /// Ensures the runtime loader asset carries the versioned name the SDK's feature declares.
        /// Returns true when the package is in the expected state (whether or not a rename was needed).
        /// </summary>
        public static bool EnsureVersionedLoader()
        {
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath($"Packages/{PackageName}/package.json");
            if (package == null)
            {
                Debug.LogError($"[PicoSdkLoaderPrep] {PackageName} is not resolved; cannot prepare its loader.");
                return false;
            }

            var androidDir = Path.Combine(package.resolvedPath, "Runtime", "Android");
            var shipped = Path.Combine(androidDir, ShippedName + ".aar");
            var versioned = Path.Combine(androidDir, VersionedName + ".aar");

            if (File.Exists(versioned))
            {
                return true;
            }

            if (!File.Exists(shipped))
            {
                Debug.LogError($"[PicoSdkLoaderPrep] Neither '{versioned}' nor '{shipped}' exists. " +
                               "The SDK layout changed; update this workaround.");
                return false;
            }

            try
            {
                File.Move(shipped, versioned);
                var shippedMeta = shipped + ".meta";
                if (File.Exists(shippedMeta))
                {
                    File.Move(shippedMeta, versioned + ".meta");
                }

                AssetDatabase.Refresh();
                Debug.Log($"[PicoSdkLoaderPrep] Renamed {ShippedName}.aar -> {VersionedName}.aar to satisfy " +
                          "the OpenXR custom-loader lookup (PICO SDK packaging defect).");
                return true;
            }
            catch (IOException e)
            {
                Debug.LogError($"[PicoSdkLoaderPrep] Could not rename the loader asset: {e.Message}");
                return false;
            }
        }
    }
}
