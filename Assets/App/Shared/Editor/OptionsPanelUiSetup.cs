using SpeechIntent;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Holodeck.Editor
{
    /// <summary>
    /// Creates the persistent Operations/Options panel and adds its menu item.
    /// Runtime only displays this authored hierarchy; it never creates it.
    /// </summary>
    public static class OptionsPanelUiSetup
    {
        [MenuItem("Holodeck/Setup Options Panel")]
        public static void Setup()
        {
            ArchOperationsPanel ops = Object.FindFirstObjectByType<ArchOperationsPanel>(FindObjectsInactive.Include);
            if (ops == null)
            {
                const string scenePath = "Assets/Scenes/Holodeck.unity";
                if (!System.IO.File.Exists(scenePath))
                {
                    Debug.LogWarning("[OptionsPanelUiSetup] Holodeck scene was not found.");
                    return;
                }

                EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                ops = Object.FindFirstObjectByType<ArchOperationsPanel>(FindObjectsInactive.Include);
            }

            if (ops == null)
            {
                Debug.LogWarning("[OptionsPanelUiSetup] ArchOperationsPanel not found. Run Holodeck/Setup LCARS Arch UI first.");
                return;
            }

            Undo.RecordObject(ops, "Create Options panel");
            ops.BuildOptionsPanelLayout();
            EditorUtility.SetDirty(ops);

            // Adds OPTIONS below THUMBNAIL and PANORAMA in WorldViewCaptureControls,
            // with the persistent ShowOptions listener.
            WorldViewCaptureUiSetup.Setup();

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            Debug.Log("[OptionsPanelUiSetup] Persistent Options panel and menu item are ready.");
        }
    }
}
