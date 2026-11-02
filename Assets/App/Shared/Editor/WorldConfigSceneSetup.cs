using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using WorldLabs.Runtime;
using Holodeck.Save;
using SpeechIntent;
using SpeechIntent.Audio;

namespace Holodeck.Editor
{
    /// <summary>
    /// Editor utility that adds and wires the World Config save system into the current scene.
    ///
    /// Adds WorldConfigStore, WorldConfigRestorer, and WorldConfigAutoSave to the
    /// "Systems/SpeechIntent" GameObject (created if missing). Wires all cross-system
    /// references including WorldActionDispatcher and WorldBrowserController.
    ///
    /// Re-running is safe — existing components are reused and references are re-wired.
    ///
    /// After running, complete these manual steps in the Editor:
    ///   1. Create a WorldConfigCardUI prefab and assign it to MyWorldsPanel.cardPrefab.
    ///   2. Add a ScrollRect panel with key "my worlds" wired to MyWorldsPanel in UiPanelController.
    ///   3. Add a bookmark Image to the WorldCardUI prefab and assign it to WorldCardUI.bookmarkIndicator.
    ///
    /// Menu: Holodeck > Setup World Config
    /// </summary>
    public static class WorldConfigSceneSetup
    {
        [MenuItem("Holodeck/Setup World Config")]
        public static void SetupWorldConfig()
        {
            // ── 1. Find or create scene hierarchy ─────────────────────────────
            GameObject systems    = EnsureRootObject("Systems");
            GameObject speechRoot = EnsureChildObject(systems, "SpeechIntent");

            // ── 2. Add save system components ─────────────────────────────────
            WorldConfigStore    store    = GetOrAdd<WorldConfigStore>(speechRoot);
            WorldConfigRestorer restorer = GetOrAdd<WorldConfigRestorer>(speechRoot);
            WorldConfigAutoSave autoSave = GetOrAdd<WorldConfigAutoSave>(speechRoot);

            // ── 3. Locate cross-system dependencies ───────────────────────────
            WorldLabsWorldManager worldManager =
                systems.GetComponentInChildren<WorldLabsWorldManager>(true);

            WorldActionDispatcher dispatcher =
                speechRoot.GetComponent<WorldActionDispatcher>();

            WorldBrowserController worldBrowser = FindInteractiveWorldBrowser();

            ObjectPlacementController objectPlacement =
                speechRoot.GetComponent<ObjectPlacementController>();

            InteractionMemory interactionMemory =
                speechRoot.GetComponent<InteractionMemory>();

            LightRigController lightRig =
                speechRoot.GetComponent<LightRigController>();

            LocalRemoteSplatLoader splatLoader =
                speechRoot.GetComponent<LocalRemoteSplatLoader>();

            LocalRemotePanoLoader panoLoader =
                speechRoot.GetComponent<LocalRemotePanoLoader>();
            AudioWorldActionController audioController =
                speechRoot.GetComponent<AudioWorldActionController>();

            if (worldManager == null)
                Debug.LogWarning("[WorldConfigSceneSetup] WorldLabsWorldManager not found under Systems. " +
                                 "Run Holodeck > Setup Holodeck Scene first, then re-run this.");

            if (dispatcher == null)
                Debug.LogWarning("[WorldConfigSceneSetup] WorldActionDispatcher not found on SpeechIntent. " +
                                 "Run Holodeck > Setup SpeechIntent first, then re-run this.");

            // ── 4. Wire WorldConfigStore (private [SerializeField] worldManager) ──
            {
                SerializedObject storeSo = new SerializedObject(store);
                SerializedProperty wmProp = storeSo.FindProperty("worldManager");
                if (wmProp != null && worldManager != null)
                {
                    wmProp.objectReferenceValue = worldManager;
                    storeSo.ApplyModifiedProperties();
                }
                else if (wmProp == null)
                    Debug.LogWarning("[WorldConfigSceneSetup] WorldConfigStore.worldManager serialized property not found.");
            }

            // ── 5. Wire WorldConfigRestorer ────────────────────────────────────
            Undo.RecordObject(restorer, "Wire WorldConfigRestorer");
            restorer.worldConfigStore    = store;
            restorer.worldManager        = worldManager;
            restorer.worldConfigAutoSave = autoSave;
            restorer.objectPlacement     = objectPlacement;
            restorer.interactionMemory = interactionMemory;
            restorer.lightRig         = lightRig;
            restorer.splatLoader      = splatLoader;
            restorer.panoLoader       = panoLoader;

            // placedObjectsParent — look for a dedicated container or fall back to speechRoot
            GameObject placedRoot = GameObject.Find("Environment/PlacedObjects")
                                 ?? GameObject.Find("Environment/GeneratedWorldRoot");
            if (placedRoot != null)
                restorer.placedObjectsParent = placedRoot.transform;
            else
                Debug.LogWarning("[WorldConfigSceneSetup] Could not find a PlacedObjects root under Environment. " +
                                 "Assign WorldConfigRestorer.placedObjectsParent manually.");

            if (objectPlacement == null)
                Debug.LogWarning("[WorldConfigSceneSetup] ObjectPlacementController not found on SpeechIntent. " +
                                 "Assign WorldConfigRestorer.objectPlacement manually.");
            if (interactionMemory == null)
                Debug.LogWarning("[WorldConfigSceneSetup] InteractionMemory not found on SpeechIntent. " +
                                 "Assign WorldConfigRestorer.interactionMemory manually.");
            if (lightRig == null)
                Debug.LogWarning("[WorldConfigSceneSetup] LightRigController not found on SpeechIntent. " +
                                 "Assign WorldConfigRestorer.lightRig manually.");
            if (splatLoader == null)
                Debug.LogWarning("[WorldConfigSceneSetup] LocalRemoteSplatLoader not found on SpeechIntent. " +
                                 "Assign WorldConfigRestorer.splatLoader manually.");
            if (panoLoader == null)
                Debug.LogWarning("[WorldConfigSceneSetup] LocalRemotePanoLoader not found on SpeechIntent. " +
                                 "Assign WorldConfigRestorer.panoLoader manually.");

            // ── 6. Wire WorldConfigAutoSave ────────────────────────────────────
            Undo.RecordObject(autoSave, "Wire WorldConfigAutoSave");
            autoSave.worldConfigStore = store;
            autoSave.worldManager     = worldManager;
            autoSave.worldBrowser     = worldBrowser;
            autoSave.panoLoader       = panoLoader;
            autoSave.dispatcher       = dispatcher;

            // ── 7. Wire WorldActionDispatcher save fields ─────────────────────
            if (dispatcher != null)
            {
                Undo.RecordObject(dispatcher, "Wire WorldActionDispatcher Save System");
                dispatcher.worldConfigStore    = store;
                dispatcher.worldConfigRestorer = restorer;
                dispatcher.worldConfigAutoSave = autoSave;
                dispatcher.audioWorldActionController = audioController;
            }

            if (audioController != null)
            {
                Undo.RecordObject(audioController, "Wire Audio World Action Controller");
                audioController.worldConfigStore = store;
                audioController.worldConfigAutoSave = autoSave;
                audioController.worldManager = worldManager;
                audioController.interactionMemory = interactionMemory;
            }

            // ── 8. Wire WorldBrowserController.worldConfigStore ───────────────
            if (worldBrowser != null)
            {
                Undo.RecordObject(worldBrowser, "Wire WorldBrowserController Save System");
                worldBrowser.worldBookmarkProvider = store;
            }
            else
                Debug.LogWarning("[WorldConfigSceneSetup] WorldBrowserController not found in scene. " +
                                 "Assign WorldBrowserController.worldConfigStore manually.");

            // ── 9. Wire MyWorldsPanel if present ──────────────────────────────
            MyWorldsPanel myWorldsPanel = Object.FindFirstObjectByType<MyWorldsPanel>();
            if (myWorldsPanel != null)
            {
                Undo.RecordObject(myWorldsPanel, "Wire MyWorldsPanel");
                myWorldsPanel.worldConfigStore    = store;
                myWorldsPanel.worldConfigRestorer = restorer;
            }
            else
                Debug.Log("[WorldConfigSceneSetup] MyWorldsPanel not found in scene. " +
                          "Create the panel, then re-run this setup or assign its fields manually.");

            // ── 10. Register "my worlds" panel with UiPanelController ─────────
            UiPanelController uiPanels = speechRoot.GetComponent<UiPanelController>();
            if (uiPanels != null && myWorldsPanel != null)
                WireUiPanel(uiPanels, "my worlds", myWorldsPanel.gameObject);
            else if (uiPanels == null)
                Debug.LogWarning("[WorldConfigSceneSetup] UiPanelController not found on SpeechIntent. " +
                                 "Register the 'my worlds' panel manually.");

            // ── 11. Mark dirty ─────────────────────────────────────────────────
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

            Debug.Log("[WorldConfigSceneSetup] Done.\n" +
                      "Manual steps remaining:\n" +
                      "  1. Create a WorldConfigCardUI prefab and assign it to MyWorldsPanel.cardPrefab.\n" +
                      "  2. Create a ScrollRect panel for 'My Worlds', add MyWorldsPanel, and re-run " +
                      "     this setup so UiPanelController picks it up under key 'my worlds'.\n" +
                      "  3. Add a bookmark Image child to the WorldCardUI prefab and assign it to " +
                      "     WorldCardUI.bookmarkIndicator.");
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static void WireUiPanel(UiPanelController controller, string key, GameObject panelRoot)
        {
            foreach (UiPanelController.PanelEntry e in controller.panels)
                if (string.Equals(e?.key, key, System.StringComparison.OrdinalIgnoreCase)) return;

            Undo.RecordObject(controller, $"Register UiPanel '{key}'");
            controller.panels.Add(new UiPanelController.PanelEntry { key = key, root = panelRoot });
        }

        private static GameObject EnsureRootObject(string objName)
        {
            Scene active = SceneManager.GetActiveScene();
            foreach (GameObject root in active.GetRootGameObjects())
                if (root.name == objName) return root;

            GameObject created = new GameObject(objName);
            Undo.RegisterCreatedObjectUndo(created, $"Create {objName}");
            return created;
        }

        private static GameObject EnsureChildObject(GameObject parent, string childName)
        {
            Transform existing = parent.transform.Find(childName);
            if (existing != null) return existing.gameObject;

            GameObject child = new GameObject(childName);
            Undo.RegisterCreatedObjectUndo(child, $"Create {childName}");
            child.transform.SetParent(parent.transform, false);
            return child;
        }

        private static T GetOrAdd<T>(GameObject go) where T : Component
        {
            T existing = go.GetComponent<T>();
            return existing != null ? existing : Undo.AddComponent<T>(go);
        }

        /// <summary>
        /// Finds the interactive WorldBrowserController (the card grid, not a status display).
        /// When multiple exist, prefers one whose GameObject name contains "GUI" (case-insensitive)
        /// and whose panoramaOnly field is false. Falls back to the first active instance found.
        /// Logs all candidates so the developer can verify the right one was chosen.
        /// </summary>
        private static WorldBrowserController FindInteractiveWorldBrowser()
        {
            WorldBrowserController[] all = Object.FindObjectsByType<WorldBrowserController>(FindObjectsSortMode.None);

            if (all.Length == 0) return null;

            if (all.Length == 1) return all[0];

            // Log all candidates to help diagnose mis-wiring
            foreach (WorldBrowserController c in all)
                Debug.Log($"[WorldConfigSceneSetup] Found WorldBrowserController on '{c.gameObject.name}' (active={c.gameObject.activeInHierarchy})");

            // Prefer one whose name contains "GUI" and is not a status/HUD display
            foreach (WorldBrowserController c in all)
            {
                string n = c.gameObject.name;
                if (n.IndexOf("GUI", System.StringComparison.OrdinalIgnoreCase) >= 0 &&
                    n.IndexOf("Status", System.StringComparison.OrdinalIgnoreCase) < 0)
                    return c;
            }

            // Fall back: first active one that isn't named "Status"
            foreach (WorldBrowserController c in all)
                if (c.gameObject.activeInHierarchy &&
                    c.gameObject.name.IndexOf("Status", System.StringComparison.OrdinalIgnoreCase) < 0)
                    return c;

            // Last resort
            Debug.LogWarning("[WorldConfigSceneSetup] Could not determine which WorldBrowserController is the interactive browser. " +
                             "Assign WorldConfigAutoSave.worldBrowser manually.");
            return all[0];
        }
    }
}
