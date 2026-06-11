using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Inputs;
using UnityEngine.SceneManagement;

namespace Holodeck.Editor
{
    public static class XRHeadTrackingSetup
    {
        const string XriDefaultActionsPath =
            "Assets/Samples/XR Interaction Toolkit/3.5.0/Starter Assets/XRI Default Input Actions.inputactions";
        const string HolodeckActionsPath =
            "Assets/App/Input/HolodeckInputActions.inputactions";

        [MenuItem("Holodeck/Fix XR Head Tracking Actions")]
        public static void Fix()
        {
            InputActionAsset xriActions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(XriDefaultActionsPath);
            InputActionAsset holodeckActions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(HolodeckActionsPath);

            if (xriActions == null)
            {
                Debug.LogError("[XRHeadTrackingSetup] XRI Default Input Actions asset not found at " + XriDefaultActionsPath);
                return;
            }

            InputActionManager manager = Object.FindFirstObjectByType<InputActionManager>(FindObjectsInactive.Include);
            if (manager == null)
            {
                Debug.LogError("[XRHeadTrackingSetup] No InputActionManager found in the scene. Add one to the XR Origin root.");
                return;
            }

            Undo.RecordObject(manager, "Fix XR Head Tracking Actions");

            SerializedObject so = new SerializedObject(manager);
            SerializedProperty actionAssets = so.FindProperty("m_ActionAssets");
            if (actionAssets == null)
            {
                Debug.LogError("[XRHeadTrackingSetup] InputActionManager.m_ActionAssets was not found.");
                return;
            }

            EnsureActionAsset(actionAssets, xriActions);
            if (holodeckActions != null)
                EnsureActionAsset(actionAssets, holodeckActions);

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(manager);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

            Debug.Log("[XRHeadTrackingSetup] InputActionManager now includes XRI Default Input Actions. Rebuild and test headset camera tracking.");
        }

        static void EnsureActionAsset(SerializedProperty actionAssets, InputActionAsset asset)
        {
            for (int i = 0; i < actionAssets.arraySize; i++)
            {
                SerializedProperty element = actionAssets.GetArrayElementAtIndex(i);
                if (element.objectReferenceValue == asset)
                    return;
            }

            int index = actionAssets.arraySize;
            actionAssets.InsertArrayElementAtIndex(index);
            actionAssets.GetArrayElementAtIndex(index).objectReferenceValue = asset;
        }
    }
}
