// SPDX-License-Identifier: MIT

using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace WorldLabs.Editor
{
    /// <summary>
    /// Before each build, copies the project-root .env file into
    /// Assets/Resources/WorldLabs/worldlabs_env.txt so it is embedded in the build
    /// and readable on all platforms (including Android/Pico) via Resources.Load.
    ///
    /// The copy is deleted immediately after the build so it never ends up in source control.
    /// </summary>
    class BuildEnvCopier : IPreprocessBuildWithReport, IPostprocessBuild
    {
        const string EnvFileName   = ".env";
        const string ResourcesDir  = "Assets/Resources/WorldLabs";
        // Unity TextAsset requires a .txt extension; EnvLoader references "WorldLabs/worldlabs_env"
        const string ResourcesFile = ResourcesDir + "/worldlabs_env.txt";
        const string ResourcesMeta = ResourcesFile + ".meta";

        public int callbackOrder => 0;

        // ── Before build ──────────────────────────────────────────────────────

        public void OnPreprocessBuild(BuildReport report)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string sourcePath  = Path.Combine(projectRoot, EnvFileName);

            if (!File.Exists(sourcePath))
            {
                Debug.LogWarning($"[BuildEnvCopier] No .env found at '{sourcePath}'. " +
                                 "The build will not contain an API key.");
                return;
            }

            if (!Directory.Exists(ResourcesDir))
                Directory.CreateDirectory(ResourcesDir);

            File.Copy(sourcePath, ResourcesFile, overwrite: true);
            AssetDatabase.ImportAsset(ResourcesFile, ImportAssetOptions.ForceSynchronousImport);

            Debug.Log($"[BuildEnvCopier] Embedded .env → {ResourcesFile}");
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
        }

        static void Cleanup()
        {
            bool changed = false;

            if (File.Exists(ResourcesFile))
            {
                File.Delete(ResourcesFile);
                changed = true;
            }
            if (File.Exists(ResourcesMeta))
            {
                File.Delete(ResourcesMeta);
                changed = true;
            }

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
