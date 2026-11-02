using Holodeck.Diagnostics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using WorldLabs.Runtime;

namespace Holodeck.Editor
{
    public static class WorldLabsNamedWorldDiagnosticSetup
    {
        private const string ScenePath = "Assets/Scenes/Holodeck.unity";
        private const string LoaderName = "WorldLabsNamedWorldDiagnosticLoader";
        private const string DefaultWorldName = "Christmas at the North Pole";

        [MenuItem("Headset Holodeck/Diagnostics/Install Named World Loader")]
        public static void InstallNamedWorldLoader()
        {
            Configure(DefaultWorldName, loadOnStart: false);
        }

        [MenuItem("Headset Holodeck/Diagnostics/Enable Christmas World Loader")]
        public static void EnableChristmasWorldLoader()
        {
            Configure(DefaultWorldName, loadOnStart: true);
        }

        [MenuItem("Headset Holodeck/Diagnostics/Disable Named World Loader")]
        public static void DisableNamedWorldLoader()
        {
            Configure(DefaultWorldName, loadOnStart: false);
        }

        public static void ConfigureChristmasAtNorthPoleLoader()
        {
            Configure(GetArgument("-worldName", DefaultWorldName), loadOnStart: true);
        }

        private static void Configure(string worldName, bool loadOnStart)
        {
            var scene = EditorSceneManager.OpenScene(ScenePath);

            GameObject systems = GameObject.Find("Systems");
            if (systems == null)
            {
                GameObject holodeck = GameObject.Find("Holodeck");
                systems = new GameObject("Systems");
                if (holodeck != null)
                    systems.transform.SetParent(holodeck.transform, false);
            }

            GameObject loaderGo = GameObject.Find(LoaderName);
            if (loaderGo == null)
            {
                loaderGo = new GameObject(LoaderName);
                loaderGo.transform.SetParent(systems.transform, false);
            }

            var loader = loaderGo.GetComponent<WorldLabsNamedWorldDiagnosticLoader>()
                         ?? loaderGo.AddComponent<WorldLabsNamedWorldDiagnosticLoader>();
            loader.WorldDisplayName = string.IsNullOrWhiteSpace(worldName) ? DefaultWorldName : worldName;
            loader.LoadOnStart = loadOnStart;

            SerializedObject serialized = new SerializedObject(loader);
            SerializedProperty managerProperty = serialized.FindProperty("worldManager");
            if (managerProperty != null && managerProperty.objectReferenceValue == null)
                managerProperty.objectReferenceValue = Object.FindFirstObjectByType<WorldLabsWorldManager>();
            serialized.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log($"[WorldLabsNamedWorldDiagnosticSetup] {(loadOnStart ? "Enabled" : "Installed/disabled")} '{LoaderName}' for '{loader.WorldDisplayName}'.");
        }

        private static string GetArgument(string name, string fallback)
        {
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == name)
                    return args[i + 1];
            }

            return fallback;
        }
    }
}
