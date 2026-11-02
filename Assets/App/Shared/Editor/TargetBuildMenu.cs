using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEngine;
using WorldLabs.Editor;

namespace Holodeck.Editor
{
    /// <summary>
    /// Target-specific build entry points.
    ///
    /// A build cannot be started from <c>script.eval</c>: the eval itself leaves the script compiler busy,
    /// and Unity aborts with "Error building Player because scripts are compiling". These menu items do the
    /// same work without triggering a Roslyn compilation, and they are what an automated driver invokes.
    /// </summary>
    public static class TargetBuildMenu
    {
        const string PicoProfilePath = "Assets/App/Platforms/Pico/Settings/Pico.asset";
        const string PicoOutput = "Builds/Pico-Swan-parity.apk";
        const string MetaOutput = "Builds/Meta-current.apk";

        [MenuItem("Headset Holodeck/Build/Build Pico APK (device)", priority = 100)]
        public static void BuildPico()
        {
            Build(TargetConfiguration.Target.Pico, PicoOutput, PicoProfilePath);
        }

        [MenuItem("Headset Holodeck/Build/Build Meta APK (device)", priority = 101)]
        public static void BuildMeta()
        {
            Build(TargetConfiguration.Target.Meta, MetaOutput, null);
        }

        static void Build(TargetConfiguration.Target target, string outputPath, string profilePath)
        {
            BuildProfile profile = null;
            if (!string.IsNullOrEmpty(profilePath))
            {
                profile = AssetDatabase.LoadAssetAtPath<BuildProfile>(profilePath);
                if (profile == null)
                {
                    Debug.LogError($"[TargetBuildMenu] Build profile not found at '{profilePath}'; aborting {target} build.");
                    return;
                }
            }

            try
            {
                BuildProfile.SetActiveBuildProfile(profile);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[TargetBuildMenu] Could not change the active build profile to " +
                                 $"{profile?.name ?? "(none)"}: {e.Message}");
            }

            // Apply before the build so the XR feature set matches the profile even if the menu switch was skipped.
            if (!TargetConfiguration.Apply(target, true))
            {
                Debug.LogError("[TargetBuildMenu] Target configuration could not be applied; aborting the build " +
                               "rather than producing a half-configured APK. See the error above.");
                return;
            }

            // The PICO SDK ships an unversioned OpenXR loader while its feature declares a versioned name;
            // without this the build stops in Unity's OpenXR loader step. See PicoSdkLoaderPrep.
            if (target == TargetConfiguration.Target.Pico && !PicoSdkLoaderPrep.EnsureVersionedLoader())
            {
                Debug.LogError("[TargetBuildMenu] Pico SDK loader is not in the expected state; aborting the build.");
                return;
            }

            // A device build embeds no keys; the app reads its own .env on the headset.
            Environment.SetEnvironmentVariable(BuildEnvCredentials.ModeEnvironmentVariable, "device");

            // Burst's native compiler runs as a child process and writes a response file to TMP. In this
            // environment that write is denied (System.UnauthorizedAccessException inside
            // Path.InternalGetTempFileName) even though the same write succeeds outside the editor, so
            // point its temp inside the project, which the editor's process context can always write.
            var burstTemp = Path.GetFullPath(Path.Combine("Temp", "burst-tmp"));
            Directory.CreateDirectory(burstTemp);
            Environment.SetEnvironmentVariable("TMP", burstTemp);
            Environment.SetEnvironmentVariable("TEMP", burstTemp);
            Debug.Log($"[TargetBuildMenu] Burst temp redirected to {burstTemp}");

            // Development builds are slower under the emulator's ARM translation and add nothing for a
            // parity artifact, so parity builds are release builds unless someone opts back in.
            EditorUserBuildSettings.development = false;
            EditorUserBuildSettings.allowDebugging = false;

            var activeProfile = TargetConfiguration.ActiveProfileName() ?? "(none)";
            Debug.Log($"[TargetBuildMenu] Building {target} via profile '{activeProfile}' -> {outputPath}");
            CommandLineAndroidBuild.BuildApk(outputPath);
        }
    }
}
