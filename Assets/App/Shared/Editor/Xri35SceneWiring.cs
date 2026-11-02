using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Inputs;

namespace Holodeck.Editor
{
    public static class Xri35SceneWiring
    {
        const string HolodeckScenePath = "Assets/Scenes/Holodeck.unity";
        const string HandTeleportPrefabPath =
            "Assets/Samples/XR Interaction Toolkit/3.5.0/Hands Interaction Demo/Prefabs/HandTeleportInteractor.prefab";
        const string XriDefaultActionsPath =
            "Assets/Samples/XR Interaction Toolkit/3.5.0/Starter Assets/XRI Default Input Actions.inputactions";
        const string HolodeckActionsPath = "Assets/App/Input/HolodeckInputActions.inputactions";

        [MenuItem("Holodeck/XRI 3.5/Wire Hand Teleportation")]
        public static void WireActiveScene()
        {
            WireScene(saveScene: false);
        }

        public static void WireHolodeckSceneBatch()
        {
            EditorSceneManager.OpenScene(HolodeckScenePath, OpenSceneMode.Single);
            WireScene(saveScene: true);
        }

        public static void DumpHolodeckRigBatch()
        {
            EditorSceneManager.OpenScene(HolodeckScenePath, OpenSceneMode.Single);
            GameObject me = GameObject.Find("Me");
            if (me == null)
            {
                Debug.LogError("[Xri35SceneWiring] Could not find GameObject named 'Me'.");
                return;
            }

            Debug.Log("[Xri35SceneWiring] Me hierarchy:\n" + BuildHierarchyDump(me.transform, 0, 5));
            DumpPath("Main Camera");
            DumpPath("Left Hand");
            DumpPath("Right Hand");
            DumpPath("Left Hand Teleport Interactor");
            DumpPath("Right Hand Teleport Interactor");
            DumpPath("HandTeleportation");
        }

        static void WireScene(bool saveScene)
        {
            GameObject me = GameObject.Find("Me");
            if (me == null)
            {
                Debug.LogError("[Xri35SceneWiring] Could not find GameObject named 'Me'.");
                return;
            }

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(HandTeleportPrefabPath);
            if (prefab == null)
            {
                Debug.LogError("[Xri35SceneWiring] Could not find XRI 3.5 hand teleport prefab at " + HandTeleportPrefabPath);
                return;
            }

            Transform hostParent = FindHandTeleportHostParent(me.transform);
            Transform host = EnsureUniqueChild(hostParent, "HandTeleportation");
            GameObject left = EnsureHandTeleportInstance(host, prefab, "Left Hand Teleport Interactor", handedness: 1);
            GameObject right = EnsureHandTeleportInstance(host, prefab, "Right Hand Teleport Interactor", handedness: 2);
            EnsureInputActionManager(me);
            ValidateTeleportArea();

            EditorUtility.SetDirty(left);
            EditorUtility.SetDirty(right);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

            if (saveScene)
                EditorSceneManager.SaveScene(SceneManager.GetActiveScene());

            Debug.Log("[Xri35SceneWiring] Wired XRI 3.5 hand teleportation under " + GetPath(host) + ".");
        }

        static Transform FindHandTeleportHostParent(Transform me)
        {
            Transform cameraOffset = FindChildRecursive(me, "Camera Offset");
            if (cameraOffset != null)
                return cameraOffset;

            Transform mainCamera = FindChildRecursive(me, "Main Camera");
            if (mainCamera != null && mainCamera.parent != null)
                return mainCamera.parent;

            Debug.LogWarning("[Xri35SceneWiring] Could not find 'Camera Offset'. Falling back to Me as hand teleport parent.");
            return me;
        }

        static Transform EnsureUniqueChild(Transform parent, string childName)
        {
            Transform existing = FindChildRecursive(parent.root, childName);
            if (existing != null)
            {
                if (existing.parent != parent)
                {
                    Undo.SetTransformParent(existing, parent, "Move " + childName);
                    existing.localPosition = Vector3.zero;
                    existing.localRotation = Quaternion.identity;
                    existing.localScale = Vector3.one;
                    EditorUtility.SetDirty(existing);
                }

                return existing;
            }

            GameObject created = new GameObject(childName);
            Undo.RegisterCreatedObjectUndo(created, "Create " + childName);
            created.transform.SetParent(parent, false);
            return created.transform;
        }

        static Transform FindChildRecursive(Transform root, string childName)
        {
            if (root.name == childName)
                return root;

            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindChildRecursive(root.GetChild(i), childName);
                if (found != null)
                    return found;
            }

            return null;
        }

        static GameObject EnsureHandTeleportInstance(Transform host, GameObject prefab, string objectName, int handedness)
        {
            Transform existing = host.Find(objectName);
            GameObject instance;

            if (existing == null)
            {
                instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, host);
                Undo.RegisterCreatedObjectUndo(instance, "Create " + objectName);
                instance.name = objectName;
            }
            else
            {
                instance = existing.gameObject;
                string sourcePath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(instance);
                if (!string.IsNullOrEmpty(sourcePath) && sourcePath != HandTeleportPrefabPath)
                {
                    Undo.DestroyObjectImmediate(instance);
                    instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, host);
                    Undo.RegisterCreatedObjectUndo(instance, "Replace " + objectName);
                    instance.name = objectName;
                }
            }

            instance.transform.SetParent(host, false);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;
            SetSerializedHandedness(instance, handedness);
            SetTeleportPoseActions(instance, handedness);
            return instance;
        }

        static void SetSerializedHandedness(GameObject root, int handedness)
        {
            foreach (MonoBehaviour behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null)
                    continue;

                SerializedObject so = new SerializedObject(behaviour);
                SerializedProperty handednessProperty = so.FindProperty("m_Handedness");
                if (handednessProperty == null || handednessProperty.propertyType != SerializedPropertyType.Enum)
                    continue;

                handednessProperty.enumValueIndex = handedness;
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(behaviour);
            }
        }

        static void SetTeleportPoseActions(GameObject root, int handedness)
        {
            string mapName = handedness == 1 ? "XRI Left" : "XRI Right";
            InputActionReference position = FindActionReference(mapName, "Position");
            InputActionReference rotation = FindActionReference(mapName, "Rotation");
            InputActionReference trackingState = FindActionReference(mapName, "Tracking State");

            if (position == null || rotation == null || trackingState == null)
            {
                Debug.LogWarning("[Xri35SceneWiring] Could not find all " + mapName + " aim action references for hand teleportation.");
                return;
            }

            foreach (MonoBehaviour behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null)
                    continue;

                SerializedObject so = new SerializedObject(behaviour);
                SerializedProperty positionReference = so.FindProperty("m_PositionInput.m_Reference");
                SerializedProperty rotationReference = so.FindProperty("m_RotationInput.m_Reference");
                SerializedProperty trackingStateReference = so.FindProperty("m_TrackingStateInput.m_Reference");
                if (positionReference == null || rotationReference == null || trackingStateReference == null)
                    continue;

                positionReference.objectReferenceValue = position;
                rotationReference.objectReferenceValue = rotation;
                trackingStateReference.objectReferenceValue = trackingState;
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(behaviour);
            }
        }

        static InputActionReference FindActionReference(string mapName, string actionName)
        {
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(XriDefaultActionsPath))
            {
                if (asset is not InputActionReference actionReference || actionReference.action == null)
                    continue;

                if (actionReference.action.actionMap?.name == mapName && actionReference.action.name == actionName)
                    return actionReference;
            }

            return null;
        }

        static void EnsureInputActionManager(GameObject me)
        {
            InputActionManager manager = me.GetComponent<InputActionManager>();
            if (manager == null)
                manager = Object.FindFirstObjectByType<InputActionManager>(FindObjectsInactive.Include);

            if (manager == null)
                manager = Undo.AddComponent<InputActionManager>(me);

            InputActionAsset xriActions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(XriDefaultActionsPath);
            InputActionAsset holodeckActions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(HolodeckActionsPath);

            SerializedObject so = new SerializedObject(manager);
            SerializedProperty actionAssets = so.FindProperty("m_ActionAssets");
            if (actionAssets == null)
            {
                Debug.LogWarning("[Xri35SceneWiring] InputActionManager.m_ActionAssets was not found.");
                return;
            }

            RemoveNullActionAssets(actionAssets);
            if (xriActions != null)
                EnsureActionAsset(actionAssets, xriActions);
            else
                Debug.LogWarning("[Xri35SceneWiring] XRI 3.5 default input actions not found at " + XriDefaultActionsPath);

            if (holodeckActions != null)
                EnsureActionAsset(actionAssets, holodeckActions);

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(manager);
        }

        static void RemoveNullActionAssets(SerializedProperty actionAssets)
        {
            for (int i = actionAssets.arraySize - 1; i >= 0; i--)
            {
                SerializedProperty element = actionAssets.GetArrayElementAtIndex(i);
                if (element.objectReferenceValue == null)
                    actionAssets.DeleteArrayElementAtIndex(i);
            }
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

        static void ValidateTeleportArea()
        {
            GameObject teleportArea = GameObject.Find("Teleport Area");
            if (teleportArea == null)
            {
                Debug.LogWarning("[Xri35SceneWiring] No GameObject named 'Teleport Area' found. Hand teleport rays need a TeleportationArea or TeleportationAnchor target.");
                return;
            }

            Component teleportComponent = teleportArea.GetComponent("TeleportationArea");
            if (teleportComponent == null)
                Debug.LogWarning("[Xri35SceneWiring] 'Teleport Area' exists, but it does not have a TeleportationArea component.");
        }

        static string BuildHierarchyDump(Transform root, int depth, int maxDepth)
        {
            string indent = new string(' ', depth * 2);
            string text = indent + root.name + "  local=" + root.localPosition + " world=" + root.position + "\n";
            if (depth >= maxDepth)
                return text;

            for (int i = 0; i < root.childCount; i++)
                text += BuildHierarchyDump(root.GetChild(i), depth + 1, maxDepth);

            return text;
        }

        static void DumpPath(string objectName)
        {
            GameObject found = GameObject.Find(objectName);
            if (found == null)
            {
                Debug.Log("[Xri35SceneWiring] " + objectName + ": not found");
                return;
            }

            Debug.Log("[Xri35SceneWiring] " + objectName + ": " + GetPath(found.transform) +
                " local=" + found.transform.localPosition + " world=" + found.transform.position);
        }

        static string GetPath(Transform transform)
        {
            string path = transform.name;
            Transform current = transform.parent;
            while (current != null)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }

            return path;
        }
    }
}
