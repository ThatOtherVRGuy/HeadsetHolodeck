// SPDX-License-Identifier: MIT

using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace WorldLabs.Editor
{
    /// <summary>
    /// Before each build, decides what the project-root .env may contribute to the artifact and
    /// writes (or removes) Assets/Resources/WorldLabs/worldlabs_env.txt so it can be read on every
    /// platform through Resources.Load.
    ///
    /// What is embedded depends on the declared build credential intent — see
    /// <see cref="BuildEnvCredentials"/>. A private build carries every entry; a distribution build
    /// carries non-secret configuration only; an undeclared build that would leak credentials is
    /// stopped instead of guessing.
    ///
    /// The copy is deleted immediately after the build, at editor shutdown, and at editor startup
    /// (for a build that crashed before its cleanup ran) so it never ends up in source control or in
    /// a later build.
    /// </summary>
    class BuildEnvCopier : IPreprocessBuildWithReport, IPostprocessBuild
    {
        const string ResourcesDir  = "Assets/Resources/WorldLabs";
        // Unity TextAsset requires a .txt extension; EnvLoader references "WorldLabs/worldlabs_env"
        const string ResourcesFile = ResourcesDir + "/worldlabs_env.txt";

        public int callbackOrder => 0;

        // ── Before build ──────────────────────────────────────────────────────

        public void OnPreprocessBuild(BuildReport report)
        {
            string sourcePath = BuildCredentialSettings.ProjectEnvPath;
            BuildCredentialMode mode = BuildCredentialSettings.Mode;
            Debug.Log($"[BuildEnvCopier] Build credential intent: {BuildCredentialSettings.Describe()}");

            BuildEnvEmbedResult result;
            try
            {
                result = BuildEnvCredentials.Embed(sourcePath, ResourcesFile, mode);
            }
            catch (BuildCredentialModeException error)
            {
                Debug.LogError("[BuildEnvCopier] " + error.Message);
                throw;
            }

            if (result.RemovedStaleEmbeddedFile)
                Debug.Log("[BuildEnvCopier] Removed an embedded .env copy left behind by an earlier build.");

            if (result.WroteEmbeddedFile)
            {
                AssetDatabase.ImportAsset(ResourcesFile, ImportAssetOptions.ForceSynchronousImport);
                Debug.Log($"[BuildEnvCopier] Embedded {result.IncludedKeys.Count} .env " +
                          $"entr{(result.IncludedKeys.Count == 1 ? "y" : "ies")} → {ResourcesFile}. {result.Reason}.");
            }
            else
            {
                Debug.Log($"[BuildEnvCopier] No .env embedded in this build. {result.Reason}.");
            }

            if (result.ExcludedKeys.Count > 0)
            {
                // Key names only: values are never logged and never written into the build.
                Debug.Log($"[BuildEnvCopier] Excluded {result.ExcludedKeys.Count} .env " +
                          $"entr{(result.ExcludedKeys.Count == 1 ? "y" : "ies")} from this build: " +
                          string.Join(", ", result.ExcludedKeys) + ".");
            }

            if (mode == BuildCredentialMode.Private && result.WroteEmbeddedFile)
            {
                Debug.LogWarning("[BuildEnvCopier] This is a PRIVATE build: it contains your live API keys. " +
                                 "Do not share this artifact. Switch to 'Headset Holodeck > Build Credentials > Distribution' " +
                                 "before building anything for someone else.");
            }
            else if (mode == BuildCredentialMode.Private)
            {
                Debug.LogWarning($"[BuildEnvCopier] No .env found at '{sourcePath}'. " +
                                 "The build will not contain an API key.");
            }
            else if (mode == BuildCredentialMode.Distribution && !result.WroteEmbeddedFile && File.Exists(sourcePath))
            {
                Debug.LogWarning("[BuildEnvCopier] Distribution build: no API keys are embedded. " +
                                 "The app will need provider keys from its environment or its own .env on the target device.");
            }
            else if (mode == BuildCredentialMode.Device)
            {
                Debug.Log("[BuildEnvCopier] Device build: the artifact contains no .env at all. " +
                          "The app reads keys from 'Application.persistentDataPath/.env' on the device " +
                          "(for this project's headset builds: /sdcard/Android/data/com.QLC.HeadsetHolodeck/files/.env).");
            }
        }

        // ── After build ───────────────────────────────────────────────────────

        public void OnPostprocessBuild(BuildTarget target, string path)
        {
            Cleanup();
        }

        // Guard against cancelled builds or editor restarts leaving the file behind.
        [InitializeOnLoadMethod]
        static void RegisterEditorQuitCleanup()
        {
            EditorApplication.quitting += Cleanup;

            // A build that crashed before its postprocess step left the embedded copy on disk; that
            // copy must never be committed and must never reach a later distribution build.
            EditorApplication.delayCall += Cleanup;
        }

        static void Cleanup()
        {
            bool changed = BuildEnvCredentials.RemoveEmbedded(ResourcesFile);

            // Remove empty WorldLabs Resources dir so it doesn't pollute the project
            if (Directory.Exists(ResourcesDir) &&
                Directory.GetFileSystemEntries(ResourcesDir).Length == 0)
            {
                Directory.Delete(ResourcesDir);
                string dirMeta = ResourcesDir + ".meta";
                if (File.Exists(dirMeta)) File.Delete(dirMeta);
                changed = true;
            }

            if (changed)
            {
                AssetDatabase.Refresh();
                Debug.Log("[BuildEnvCopier] Removed embedded .env after build.");
            }
        }
    }
}
