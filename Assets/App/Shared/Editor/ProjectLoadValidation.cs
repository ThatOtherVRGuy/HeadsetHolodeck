using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Holodeck.Editor
{
    /// <summary>Batch-mode validation of the scenes used by the existing Android build.</summary>
    public static class ProjectLoadValidation
    {
        public static void ValidateAndBuildApk()
        {
            ValidateBuildScenes();
            CommandLineAndroidBuild.BuildApk();
        }

        public static void ValidateBuildScenes()
        {
            if (!Application.isBatchMode)
                throw new InvalidOperationException("Run scene validation in batch mode to preserve open editor scenes.");

            var scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).ToArray();
            if (scenes.Length == 0)
                throw new InvalidOperationException("No enabled build scenes to validate.");

            int objectCount = 0;
            int missingCount = 0;
            foreach (var entry in scenes)
            {
                var scene = EditorSceneManager.OpenScene(entry.path, OpenSceneMode.Single);
                if (!scene.IsValid() || !scene.isLoaded)
                    throw new InvalidOperationException($"Could not load build scene: {entry.path}");

                foreach (var root in scene.GetRootGameObjects())
                foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                {
                    objectCount++;
                    int count = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject);
                    if (count == 0)
                        continue;

                    missingCount += count;
                    Debug.LogError($"[ProjectLoadValidation] {entry.path}: {GetHierarchyPath(transform)} has {count} missing script(s).");
                }
            }

            if (missingCount != 0)
                throw new InvalidOperationException($"Build scenes contain {missingCount} missing script component(s).");

            Debug.Log($"[ProjectLoadValidation] PASS: loaded {scenes.Length} build scene(s), checked {objectCount} objects, 0 missing scripts.");
        }

        static string GetHierarchyPath(Transform transform)
        {
            string path = transform.name;
            while (transform.parent != null)
            {
                transform = transform.parent;
                path = transform.name + "/" + path;
            }
            return path;
        }
    }
}
