using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using WorldLabs.Runtime.Tools;

namespace HeadsetHolodeck.EditorTools
{
    public static class SplatSpawnDebugSceneSetup
    {
        public const string ScenePath = "Assets/Scenes/SplatSpawnEstimatorDebug.unity";

        [MenuItem("Holodeck/Debug/Create Splat Spawn Estimator Scene")]
        public static void CreateSceneFromMenu()
        {
            CreateScene();
        }

        public static void CreateScene()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            GameObject root = new GameObject("SplatSpawnEstimatorDebug");
            GameObject worldParent = new GameObject("WorldParent");
            worldParent.transform.SetParent(root.transform, false);

            GameObject systems = new GameObject("Systems");
            systems.transform.SetParent(root.transform, false);

            RuntimeSplatFloorLoader floorLoader = systems.AddComponent<RuntimeSplatFloorLoader>();
            floorLoader.worldParent = worldParent.transform;
            floorLoader.defaultSourceKind = RuntimeSplatFloorLoader.SplatSourceKind.LooseSplat;
            floorLoader.spawnEstimation.maxSamples = 20000;
            floorLoader.spawnEstimation.maxDebugNormalLines = 500;

            SplatSpawnDebugVisualizer visualizer = systems.AddComponent<SplatSpawnDebugVisualizer>();
            visualizer.localSpace = worldParent.transform;
            visualizer.buildRuntimeLineRenderers = true;
            visualizer.normalLineLength = 1.5f;
            visualizer.lineWidth = 0.01f;

            GameObject cameraObject = new GameObject("Main Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetPositionAndRotation(new Vector3(0f, 1.8f, -6f), Quaternion.Euler(12f, 0f, 0f));
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 1000f;

            SplatSpawnDebugSceneController controller = systems.AddComponent<SplatSpawnDebugSceneController>();
            controller.floorLoader = floorLoader;
            controller.visualizer = visualizer;
            controller.worldParent = worldParent.transform;
            controller.cameraToPlace = cameraObject.transform;
            controller.placeCameraAtEstimatedSpawn = true;
            controller.sourceKind = RuntimeSplatFloorLoader.SplatSourceKind.LooseSplat;
            controller.loadOnStart = false;
            controller.status = "Set filePath, choose sourceKind, then use Load Configured File.";

            WireMarkers(root.transform, controller);

            GameObject lightObject = new GameObject("Directional Light");
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1f;
            lightObject.transform.rotation = Quaternion.Euler(45f, -30f, 0f);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
            Debug.Log("[SplatSpawnDebugSceneSetup] Created " + ScenePath);
        }

        [MenuItem("Holodeck/Debug/Update Splat Spawn Estimator Scene Markers")]
        public static void UpdateExistingSceneMarkers()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            SplatSpawnDebugSceneController controller = Object.FindFirstObjectByType<SplatSpawnDebugSceneController>();
            if (controller == null)
            {
                Debug.LogError("[SplatSpawnDebugSceneSetup] No SplatSpawnDebugSceneController found in " + ScenePath);
                return;
            }

            GameObject root = GameObject.Find("SplatSpawnEstimatorDebug");
            if (root == null)
                root = controller.gameObject.scene.GetRootGameObjects().Length > 0
                    ? controller.gameObject.scene.GetRootGameObjects()[0]
                    : controller.gameObject;

            WireMarkers(root.transform, controller);

            if (controller.cameraToPlace == null)
            {
                Camera camera = Object.FindFirstObjectByType<Camera>();
                if (camera != null)
                    controller.cameraToPlace = camera.transform;
            }

            EditorUtility.SetDirty(controller);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.Refresh();
            Debug.Log("[SplatSpawnDebugSceneSetup] Updated marker objects in " + ScenePath);
        }

        static void WireMarkers(Transform root, SplatSpawnDebugSceneController controller)
        {
            Transform markerRoot = FindDirectChild(root, "Markers");
            if (markerRoot == null)
            {
                markerRoot = new GameObject("Markers").transform;
                markerRoot.SetParent(root, false);
            }

            controller.originMarker = FindOrCreateMarker(markerRoot, "Origin Marker");
            controller.consensusMarker = FindOrCreateMarker(markerRoot, "Consensus Marker");
            controller.longAxisConsensusMarker = FindOrCreateMarker(markerRoot, "LongAxis Consensus Marker");
            controller.spawnMarker = FindOrCreateMarker(markerRoot, "Spawn Marker");
            controller.lookAtMarker = FindOrCreateMarker(markerRoot, "LookAt Marker");
        }

        static Transform FindOrCreateMarker(Transform parent, string markerName)
        {
            GameObject existing = GameObject.Find(markerName);
            Transform marker = existing != null ? existing.transform : null;
            if (marker == null)
                marker = new GameObject(markerName).transform;

            marker.name = markerName;
            marker.SetParent(parent, true);
            StripVisualComponents(marker.gameObject);
            return marker;
        }

        static Transform FindDirectChild(Transform parent, string childName)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                if (child.name == childName)
                    return child;
            }

            return null;
        }

        static void StripVisualComponents(GameObject marker)
        {
            foreach (Renderer renderer in marker.GetComponents<Renderer>())
                Object.DestroyImmediate(renderer);
            foreach (Collider collider in marker.GetComponents<Collider>())
                Object.DestroyImmediate(collider);
            foreach (MeshFilter meshFilter in marker.GetComponents<MeshFilter>())
                Object.DestroyImmediate(meshFilter);
        }
    }
}
