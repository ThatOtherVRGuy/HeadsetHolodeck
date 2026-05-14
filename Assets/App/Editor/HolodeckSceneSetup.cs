using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using WorldLabs.Runtime;
using Holodeck.Direct;
using Holodeck.State;

namespace Holodeck.Editor
{
    /// <summary>
    /// Editor utility that creates and wires up the minimal Holodeck scene hierarchy
    /// described in README_Holodeck_DirectPlugin.md.
    ///
    /// Re-running the menu item is safe: existing objects and components are reused,
    /// only missing pieces are added, and all Inspector fields are (re-)wired.
    ///
    /// Menu: Holodeck > Setup Holodeck Scene
    /// </summary>
    public static class HolodeckSceneSetup
    {
        private const string XrOriginPrefabPath =
            "Assets/Samples/XR Interaction Toolkit/3.3.0/Starter Assets/Prefabs/XR Origin (XR Rig).prefab";

        private const string ShaderRoot =
            "Packages/com.worldlabs.gaussian-splatting/Shaders/";

        // ── Entry point ───────────────────────────────────────────────────────

        [MenuItem("Holodeck/Setup Holodeck Scene")]
        public static void SetupScene()
        {
            // 1. Root scene objects
            EnsureXrOrigin();
            GameObject systems            = EnsureRootObject("Systems");
            GameObject generatedWorldRoot = EnsureRootObject("GeneratedWorldRoot");

            // 2. Components on Systems
            HolodeckStateMachine              stateMachine = GetOrAdd<HolodeckStateMachine>(systems);
            WorldLabsWorldManager             worldManager = GetOrAdd<WorldLabsWorldManager>(systems);
            VoiceToWorldLabsPluginCoordinator coordinator  = GetOrAdd<VoiceToWorldLabsPluginCoordinator>(systems);

            // 3. Inspector wiring
            WireWorldManager(worldManager, generatedWorldRoot.transform);
            WireCoordinator(coordinator, stateMachine, worldManager);

            // 4. Mark scene dirty so the user is prompted to save
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

            Debug.Log("[HolodeckSceneSetup] Scene setup complete. Remember to set OPENAI_API_KEY and WORLDLABS_API_KEY in the project-root .env file.");
        }

        // ── XR Origin ─────────────────────────────────────────────────────────

        private static void EnsureXrOrigin()
        {
            // Treat any root object named "XR Origin", or any object that already has
            // an XROrigin component, as "already present".
            Scene active = SceneManager.GetActiveScene();
            foreach (GameObject root in active.GetRootGameObjects())
            {
                if (root.name == "XR Origin")
                    return;

                // Guard: also accept if the XROrigin component is present under any name.
                if (root.GetComponent("XROrigin") != null)
                    return;
            }

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(XrOriginPrefabPath);
            if (prefab == null)
            {
                Debug.LogWarning(
                    $"[HolodeckSceneSetup] XR Origin prefab not found at:\n  {XrOriginPrefabPath}\n" +
                    "Please add it manually from the XR Interaction Toolkit Starter Assets.");
                return;
            }

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            Undo.RegisterCreatedObjectUndo(instance, "Create XR Origin");
        }

        // ── Root-object helpers ───────────────────────────────────────────────

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

        private static T GetOrAdd<T>(GameObject go) where T : Component
        {
            T existing = go.GetComponent<T>();
            if (existing != null)
                return existing;

            return Undo.AddComponent<T>(go);
        }

        // ── WorldLabsWorldManager wiring ──────────────────────────────────────

        private static void WireWorldManager(WorldLabsWorldManager wm, Transform worldParent)
        {
            Undo.RecordObject(wm, "Configure WorldLabsWorldManager");

            // Core references
            wm.worldParent = worldParent;

            // Quality settings from the README
            wm.preferredResolution = WorldLabsWorldManager.SplatResolution._500k;
            wm.quality             = WorldLabsWorldManager.SplatQuality.Medium;

            // Mirror the logic in WorldLabsWorldManager.Reset() so the shaders are
            // populated without requiring a manual Inspector reset.
            Shader splatShader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderRoot + "RenderGaussianSplats.shader");
            Shader compositeShader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderRoot + "GaussianComposite.shader");
            Shader debugPoints = AssetDatabase.LoadAssetAtPath<Shader>(ShaderRoot + "GaussianDebugRenderPoints.shader");
            Shader debugBoxes = AssetDatabase.LoadAssetAtPath<Shader>(ShaderRoot + "GaussianDebugRenderBoxes.shader");
            ComputeShader deviceRadix = AssetDatabase.LoadAssetAtPath<ComputeShader>(ShaderRoot + "SplatUtilities_DeviceRadixSort.compute");
            ComputeShader fidelityFX  = AssetDatabase.LoadAssetAtPath<ComputeShader>(ShaderRoot + "SplatUtilities_FidelityFX.compute");

            if (splatShader     == null) Debug.LogWarning("[HolodeckSceneSetup] Could not find RenderGaussianSplats.shader.");
            if (compositeShader == null) Debug.LogWarning("[HolodeckSceneSetup] Could not find GaussianComposite.shader.");
            if (deviceRadix     == null) Debug.LogWarning("[HolodeckSceneSetup] Could not find SplatUtilities_DeviceRadixSort.compute.");
            if (fidelityFX      == null) Debug.LogWarning("[HolodeckSceneSetup] Could not find SplatUtilities_FidelityFX.compute.");

            wm.splatShader               = splatShader;
            wm.compositeShader           = compositeShader;
            wm.debugPointsShader         = debugPoints;
            wm.debugBoxesShader          = debugBoxes;
            wm.splatUtilitiesDeviceRadix = deviceRadix;
            wm.splatUtilitiesFidelityFX  = fidelityFX;

            EditorUtility.SetDirty(wm);
        }

        // ── VoiceToWorldLabsPluginCoordinator wiring ──────────────────────────

        private static void WireCoordinator(
            VoiceToWorldLabsPluginCoordinator coord,
            HolodeckStateMachine              stateMachine,
            WorldLabsWorldManager             worldManager)
        {
            SerializedObject so = new SerializedObject(coord);
            so.FindProperty("stateMachine").objectReferenceValue = stateMachine;
            so.FindProperty("worldManager").objectReferenceValue = worldManager;
            so.ApplyModifiedProperties();
        }
    }
}
