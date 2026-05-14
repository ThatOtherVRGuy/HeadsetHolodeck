using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using WorldLabs.Runtime;
using Holodeck.Direct;
using SpeechIntent;

namespace Holodeck.Editor
{
    /// <summary>
    /// Editor utility that adds and wires the WorldMeshController into the current scene.
    ///
    /// What it does:
    ///   1. Adds WorldMeshController to the "SpeechIntent" child of "Systems" (same GameObject
    ///      as ViewModeController and WorldActionDispatcher).
    ///   2. Wires WorldMeshController.worldManager → WorldLabsWorldManager (on Systems).
    ///   3. Wires ViewModeController.worldMeshController → the new component.
    ///   4. Wires WorldActionDispatcher.worldMeshController → the new component.
    ///   5. Wires WorldActionDispatcher.coordinator → VoiceToWorldLabsPluginCoordinator (on Systems).
    ///   6. Wires WorldActionDispatcher.worldBrowser → WorldBrowserController (on UI/WorldLabs_GUI).
    ///
    /// Re-running is safe — existing components are reused and references are re-wired.
    ///
    /// Menu: Holodeck > Setup World Mesh
    /// </summary>
    public static class WorldMeshSceneSetup
    {
        [MenuItem("Holodeck/Setup World Mesh")]
        public static void SetupWorldMesh()
        {
            // ── 1. Find scene hierarchy ───────────────────────────────────────
            GameObject systems = EnsureRootObject("Systems");
            GameObject speechRoot = EnsureChildObject(systems, "SpeechIntent");

            // ── 2. Add WorldMeshController (idempotent) ───────────────────────
            WorldMeshController meshController = GetOrAdd<WorldMeshController>(speechRoot);

            // ── 3. Locate cross-system dependencies ───────────────────────────
            WorldLabsWorldManager worldManager =
                systems.GetComponentInChildren<WorldLabsWorldManager>(true);
            VoiceToWorldLabsPluginCoordinator coordinator =
                systems.GetComponentInChildren<VoiceToWorldLabsPluginCoordinator>(true);
            ViewModeController viewMode =
                speechRoot.GetComponent<ViewModeController>();
            WorldActionDispatcher dispatcher =
                speechRoot.GetComponent<WorldActionDispatcher>();

            GameObject worldLabsGui = GameObject.Find("UI/WorldLabs_GUI");
            WorldBrowserController worldBrowser =
                worldLabsGui != null ? worldLabsGui.GetComponent<WorldBrowserController>() : null;

            // ── 4. Warn about missing dependencies ────────────────────────────
            if (worldManager == null)
                Debug.LogWarning("[WorldMeshSceneSetup] WorldLabsWorldManager not found under Systems. " +
                                 "Run Holodeck > Setup Holodeck Scene first, then re-run this.");

            if (coordinator == null)
                Debug.LogWarning("[WorldMeshSceneSetup] VoiceToWorldLabsPluginCoordinator not found under Systems. " +
                                 "Run Holodeck > Setup Holodeck Scene first, then re-run this.");

            if (viewMode == null)
                Debug.LogWarning("[WorldMeshSceneSetup] ViewModeController not found on SpeechIntent. " +
                                 "Run Holodeck > Setup SpeechIntent first, then re-run this.");

            if (dispatcher == null)
                Debug.LogWarning("[WorldMeshSceneSetup] WorldActionDispatcher not found on SpeechIntent. " +
                                 "Run Holodeck > Setup SpeechIntent first, then re-run this.");

            if (worldBrowser == null)
                Debug.LogWarning("[WorldMeshSceneSetup] WorldBrowserController not found at 'UI/WorldLabs_GUI'. " +
                                 "Assign WorldActionDispatcher.worldBrowser manually.");

            // ── 5. Wire WorldMeshController ───────────────────────────────────
            Undo.RecordObject(meshController, "Wire WorldMeshController");
            meshController.worldManager = worldManager;
            EditorUtility.SetDirty(meshController);

            // ── 6. Wire ViewModeController.worldMeshController ────────────────
            if (viewMode != null)
            {
                Undo.RecordObject(viewMode, "Wire ViewModeController.worldMeshController");
                viewMode.worldMeshController = meshController;
                EditorUtility.SetDirty(viewMode);
            }

            // ── 7. Wire WorldActionDispatcher new fields ───────────────────────
            if (dispatcher != null)
            {
                Undo.RecordObject(dispatcher, "Wire WorldActionDispatcher mesh fields");
                dispatcher.worldMeshController = meshController;
                dispatcher.coordinator         = coordinator;
                dispatcher.worldBrowser        = worldBrowser;
                EditorUtility.SetDirty(dispatcher);
            }

            // ── 8. Mark scene dirty ───────────────────────────────────────────
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

            Debug.Log("[WorldMeshSceneSetup] Done. " +
                      "WorldMeshController added and wired. Save the scene to persist changes.");
        }

        // ── Scene helpers (mirrors SpeechIntentSceneSetup patterns) ──────────

        private static GameObject EnsureRootObject(string objName)
        {
            Scene active = SceneManager.GetActiveScene();
            foreach (GameObject root in active.GetRootGameObjects())
            {
                if (root.name == objName)
                    return root;
            }

            GameObject created = new GameObject(objName);
            Undo.RegisterCreatedObjectUndo(created, $"Create {objName}");
            return created;
        }

        private static GameObject EnsureChildObject(GameObject parent, string childName)
        {
            Transform existing = parent.transform.Find(childName);
            if (existing != null)
                return existing.gameObject;

            GameObject child = new GameObject(childName);
            Undo.RegisterCreatedObjectUndo(child, $"Create {childName}");
            child.transform.SetParent(parent.transform, false);
            return child;
        }

        private static T GetOrAdd<T>(GameObject go) where T : Component
        {
            T existing = go.GetComponent<T>();
            if (existing != null)
                return existing;

            return Undo.AddComponent<T>(go);
        }
    }
}
