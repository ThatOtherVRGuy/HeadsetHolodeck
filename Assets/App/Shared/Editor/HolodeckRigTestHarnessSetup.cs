using SpeechIntent;
using SpeechIntent.Testing;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Holodeck.Editor
{
    public static class HolodeckRigTestHarnessSetup
    {
        const string PrefabPath = "Assets/App/Prefabs/TestRig_Me.prefab";

        [MenuItem("Holodeck/Testing/Setup Semantic Test Rig")]
        public static void SetupSemanticTestRig()
        {
            GameObject rig = GameObject.Find("TestRig_Me") ?? new GameObject("TestRig_Me");
            Undo.RegisterFullObjectHierarchyUndo(rig, "Setup Semantic Test Rig");

            Transform head = EnsureChild(rig.transform, "TestHead", new Vector3(0f, 1.65f, 0f));
            Transform left = EnsureChild(rig.transform, "TestLeftHand", new Vector3(-0.35f, 1.25f, 0.35f));
            Transform right = EnsureChild(rig.transform, "TestRightHand", new Vector3(0.35f, 1.25f, 0.35f));

            PointingSource leftSource = GetOrAdd<PointingSource>(left.gameObject);
            PointingSource rightSource = GetOrAdd<PointingSource>(right.gameObject);
            leftSource.SetState(false, false);
            rightSource.SetState(false, false);

            HolodeckRigTestHarness harness = GetOrAdd<HolodeckRigTestHarness>(rig);
            harness.testMeRoot = rig;
            harness.testHead = head;
            harness.testLeftHandSource = leftSource;
            harness.testRightHandSource = rightSource;
            harness.realMeRoot = GameObject.Find("Me");
            harness.spatialContextProvider = Object.FindFirstObjectByType<SpatialContextProvider>(FindObjectsInactive.Include);
            harness.focusContext = Object.FindFirstObjectByType<SceneFocusContext>(FindObjectsInactive.Include);

            rig.SetActive(false);

            EditorUtility.SetDirty(rig);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

            EnsurePrefab(rig);
            Debug.Log("[HolodeckRigTestHarnessSetup] Semantic TestRig_Me created/wired. It is disabled until a test activates it.");
        }

        static Transform EnsureChild(Transform parent, string name, Vector3 localPosition)
        {
            Transform child = parent.Find(name);
            if (child == null)
            {
                GameObject go = new GameObject(name);
                Undo.RegisterCreatedObjectUndo(go, "Create Test Rig Child");
                child = go.transform;
                child.SetParent(parent, false);
            }

            child.localPosition = localPosition;
            child.localRotation = Quaternion.identity;
            child.localScale = Vector3.one;
            return child;
        }

        static T GetOrAdd<T>(GameObject go) where T : Component
        {
            T existing = go.GetComponent<T>();
            if (existing != null)
                return existing;

            T added = Undo.AddComponent<T>(go);
            EditorUtility.SetDirty(go);
            return added;
        }

        static void EnsurePrefab(GameObject rig)
        {
            if (rig == null)
                return;

            string folder = System.IO.Path.GetDirectoryName(PrefabPath);
            if (!AssetDatabase.IsValidFolder(folder))
                AssetDatabase.CreateFolder("Assets/App", "Prefabs");

            PrefabUtility.SaveAsPrefabAsset(rig, PrefabPath);
        }
    }
}
