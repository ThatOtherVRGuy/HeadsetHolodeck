using System;
using System.Collections.Generic;
using System.IO;
using Holodeck.Direct;
using Holodeck.Save;
using SpeechIntent;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class InteractableObjectSetup
{
    const string InteractablePrefabFolder = "Assets/VRTemplateAssets/Prefabs/Interactables";
    const string SetupScenePath = "Assets/Scenes/Holodeck.unity";
    const string ObjectGenerationSpinnerPrefabPath = "Assets/App/Prefabs/ObjectGenerationSpinner.prefab";

    [MenuItem("Headset Holodeck/Objects/Wire Interactable Prefab Catalog")]
    public static void WireInteractablePrefabCatalog()
    {
        WireInteractablePrefabCatalog(saveScene: false);
    }

    public static void WireInteractablePrefabCatalogBatch()
    {
        EnsureSetupSceneLoaded();
        WireInteractablePrefabCatalog(saveScene: true);
    }

    static void WireInteractablePrefabCatalog(bool saveScene)
    {
        ObjectPlacementController controller = UnityEngine.Object.FindFirstObjectByType<ObjectPlacementController>(FindObjectsInactive.Include);
        if (controller == null)
        {
            Debug.LogWarning("[InteractableObjectSetup] No ObjectPlacementController found in the open scene.");
            return;
        }

        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { InteractablePrefabFolder });
        Array.Sort(guids, StringComparer.OrdinalIgnoreCase);

        int added = 0;
        foreach (string guid in guids)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (prefab == null)
                continue;

            foreach (string alias in BuildAliases(assetPath))
            {
                if (HasEntry(controller, alias))
                    continue;

                controller.namedPrefabs.Add(new NamedPrefabEntry
                {
                    name = alias,
                    prefab = prefab
                });
                added++;
            }
        }

        RuntimeMaterialCatalog materialCatalog = EnsureMaterialCatalog();
        ThreeDAIStudioCreditService creditService = EnsureThreeDAIStudioCreditService();
        EnsureObjectGenerationService(controller, creditService);
        EnsureMaterialController(materialCatalog);
        controller.materialCatalog = materialCatalog;
        EnsurePrimitiveHints(controller);

        EditorUtility.SetDirty(controller);
        Debug.Log($"[InteractableObjectSetup] Wired interactable prefab catalog. Added {added} prefab aliases to {controller.name}.");

        if (saveScene)
        {
            Scene activeScene = SceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(activeScene);
            EditorSceneManager.SaveScene(activeScene);
            Debug.Log($"[InteractableObjectSetup] Saved scene '{activeScene.path}'.");
        }
    }

    static void EnsureSetupSceneLoaded()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        if (string.Equals(activeScene.path, SetupScenePath, StringComparison.OrdinalIgnoreCase))
            return;

        if (!File.Exists(SetupScenePath))
        {
            Debug.LogWarning($"[InteractableObjectSetup] Setup scene not found at {SetupScenePath}.");
            return;
        }

        EditorSceneManager.OpenScene(SetupScenePath, OpenSceneMode.Single);
    }

    static RuntimeMaterialCatalog EnsureMaterialCatalog()
    {
        RuntimeMaterialCatalog existing = UnityEngine.Object.FindFirstObjectByType<RuntimeMaterialCatalog>(FindObjectsInactive.Include);
        if (existing != null)
            return existing;

        Transform systems =
            GameObject.Find("Holodeck/Systems")?.transform
            ?? GameObject.Find("Systems")?.transform
            ?? new GameObject("Systems").transform;

        GameObject go = new GameObject("RuntimeMaterialCatalog");
        Undo.RegisterCreatedObjectUndo(go, "Create runtime material catalog");
        go.transform.SetParent(systems, false);

        RuntimeMaterialCatalog catalog = Undo.AddComponent<RuntimeMaterialCatalog>(go);
        EditorUtility.SetDirty(catalog);
        return catalog;
    }

    static ThreeDAIStudioCreditService EnsureThreeDAIStudioCreditService()
    {
        ThreeDAIStudioCreditService existing = UnityEngine.Object.FindFirstObjectByType<ThreeDAIStudioCreditService>(FindObjectsInactive.Include);
        if (existing != null)
            return existing;

        Transform systems =
            GameObject.Find("Holodeck/Systems")?.transform
            ?? GameObject.Find("Systems")?.transform
            ?? new GameObject("Systems").transform;

        GameObject go = new GameObject("ThreeDAIStudioCreditService");
        Undo.RegisterCreatedObjectUndo(go, "Create 3dAIStudio credit service");
        go.transform.SetParent(systems, false);

        ThreeDAIStudioCreditService service = Undo.AddComponent<ThreeDAIStudioCreditService>(go);
        EditorUtility.SetDirty(service);
        return service;
    }

    static ObjectGenerationService EnsureObjectGenerationService(
        ObjectPlacementController objectPlacement,
        ThreeDAIStudioCreditService creditService)
    {
        ObjectGenerationService existing = UnityEngine.Object.FindFirstObjectByType<ObjectGenerationService>(FindObjectsInactive.Include);
        Transform systems = EnsureSystemsTransform();

        GameObject go;
        if (existing != null)
        {
            go = existing.gameObject;
            if (go.transform.parent == null || go.transform.parent != systems)
                go.transform.SetParent(systems, false);
        }
        else
        {
            go = new GameObject("ObjectGenerationService");
            Undo.RegisterCreatedObjectUndo(go, "Create object generation service");
            go.transform.SetParent(systems, false);
            existing = Undo.AddComponent<ObjectGenerationService>(go);
        }

        if (go.name != "ObjectGenerationService")
            go.name = "ObjectGenerationService";

        existing.objectPlacement = objectPlacement;
        existing.captureService = UnityEngine.Object.FindFirstObjectByType<HeadsetCameraCaptureService>(FindObjectsInactive.Include);
        existing.hitemProvider = EnsureProvider<HitemObjectGenerationProvider>(go);
        existing.threeDAIStudioProvider = EnsureProvider<ThreeDAIStudioObjectGenerationProvider>(go);
        existing.threeDAIStudioCreditService = creditService;
        existing.cachedObjectStore = EnsureCachedObjectStore(systems);
        existing.thumbnailCaptureService = EnsureProvider<ObjectThumbnailCaptureService>(go);
        existing.worldConfigAutoSave = UnityEngine.Object.FindFirstObjectByType<WorldConfigAutoSave>(FindObjectsInactive.Include);
        existing.interactionMemory = UnityEngine.Object.FindFirstObjectByType<InteractionMemory>(FindObjectsInactive.Include);

        GameObject spinnerPrefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ObjectGenerationSpinnerPrefabPath);
        ObjectGenerationSpinnerController spinnerPrefab = spinnerPrefabAsset != null
            ? spinnerPrefabAsset.GetComponent<ObjectGenerationSpinnerController>()
            : null;
        if (spinnerPrefab != null)
            existing.objectGenerationSpinnerPrefab = spinnerPrefab;
        else
            Debug.LogWarning($"[InteractableObjectSetup] Object generation spinner prefab was not found at {ObjectGenerationSpinnerPrefabPath}.");

        Transform generatedWorldRoot = GameObject.Find("Holodeck/Environment/GeneratedWorldRoot")?.transform
            ?? GameObject.Find("GeneratedWorldRoot")?.transform;
        if (generatedWorldRoot != null)
            existing.defaultParent = generatedWorldRoot;

        EditorUtility.SetDirty(go);
        EditorUtility.SetDirty(existing);
        Debug.Log("[InteractableObjectSetup] Ensured ObjectGenerationService under Holodeck/Systems and wired spinner prefab.");
        return existing;
    }

    static T EnsureProvider<T>(GameObject host) where T : Component
    {
        T existing = UnityEngine.Object.FindFirstObjectByType<T>(FindObjectsInactive.Include);
        if (existing != null)
            return existing;

        T component = host.GetComponent<T>();
        if (component != null)
            return component;

        component = Undo.AddComponent<T>(host);
        EditorUtility.SetDirty(component);
        return component;
    }

    static CachedObjectStore EnsureCachedObjectStore(Transform systems)
    {
        CachedObjectStore existing = UnityEngine.Object.FindFirstObjectByType<CachedObjectStore>(FindObjectsInactive.Include);
        if (existing != null)
        {
            if (existing.transform.parent == null || existing.transform.parent != systems)
                existing.transform.SetParent(systems, false);
            return existing;
        }

        GameObject go = new GameObject("CachedObjectStore");
        Undo.RegisterCreatedObjectUndo(go, "Create cached object store");
        go.transform.SetParent(systems, false);
        CachedObjectStore store = Undo.AddComponent<CachedObjectStore>(go);
        EditorUtility.SetDirty(store);
        return store;
    }

    static Transform EnsureSystemsTransform()
    {
        Transform systems =
            GameObject.Find("Holodeck/Systems")?.transform
            ?? GameObject.Find("Systems")?.transform;
        if (systems != null)
            return systems;

        GameObject holodeck = GameObject.Find("Holodeck");
        GameObject go = new GameObject("Systems");
        Undo.RegisterCreatedObjectUndo(go, "Create systems container");
        if (holodeck != null)
            go.transform.SetParent(holodeck.transform, false);
        return go.transform;
    }

    static MaterialTargetController EnsureMaterialController(RuntimeMaterialCatalog materialCatalog)
    {
        MaterialTargetController existing = UnityEngine.Object.FindFirstObjectByType<MaterialTargetController>(FindObjectsInactive.Include);
        WorldActionDispatcher dispatcher = UnityEngine.Object.FindFirstObjectByType<WorldActionDispatcher>(FindObjectsInactive.Include);

        GameObject host = existing != null
            ? existing.gameObject
            : (dispatcher != null ? dispatcher.gameObject : new GameObject("MaterialTargetController"));

        if (existing == null)
            existing = Undo.AddComponent<MaterialTargetController>(host);

        existing.materialCatalog = materialCatalog;
        existing.entityResolver = UnityEngine.Object.FindFirstObjectByType<SceneEntityResolver>(FindObjectsInactive.Include);
        existing.interactionMemory = UnityEngine.Object.FindFirstObjectByType<InteractionMemory>(FindObjectsInactive.Include);
        EditorUtility.SetDirty(existing);

        if (dispatcher != null)
        {
            dispatcher.materialTargetController = existing;
            EditorUtility.SetDirty(dispatcher);
        }

        return existing;
    }

    static void EnsurePrimitiveHints(ObjectPlacementController controller)
    {
        string[] primitives = { "cube", "box", "sphere", "ball", "plane", "platform", "cylinder", "capsule" };
        SceneSemanticContextProvider context = UnityEngine.Object.FindFirstObjectByType<SceneSemanticContextProvider>(FindObjectsInactive.Include);
        if (context == null)
            return;

        foreach (string primitive in primitives)
        {
            if (!context.availablePlaceableObjects.Contains(primitive))
                context.availablePlaceableObjects.Add(primitive);
        }

        foreach (NamedPrefabEntry entry in controller.namedPrefabs)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.name))
                continue;

            if (!context.availablePlaceableObjects.Contains(entry.name))
                context.availablePlaceableObjects.Add(entry.name);
        }

        EditorUtility.SetDirty(context);
    }

    static bool HasEntry(ObjectPlacementController controller, string alias)
    {
        foreach (NamedPrefabEntry entry in controller.namedPrefabs)
        {
            if (entry != null &&
                !string.IsNullOrWhiteSpace(entry.name) &&
                string.Equals(entry.name, alias, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    static IEnumerable<string> BuildAliases(string assetPath)
    {
        string fileName = Path.GetFileNameWithoutExtension(assetPath);
        string cleaned = RemoveToken(fileName, " Interactable");
        cleaned = RemoveToken(cleaned, " Variant").Trim();

        yield return cleaned;
        yield return cleaned.ToLowerInvariant();

        string compact = cleaned.Replace(" ", "");
        if (!string.Equals(compact, cleaned, StringComparison.OrdinalIgnoreCase))
            yield return compact.ToLowerInvariant();

        if (cleaned.EndsWith(" 1", StringComparison.Ordinal))
            yield return cleaned.Substring(0, cleaned.Length - 2).Trim().ToLowerInvariant();
    }

    static string RemoveToken(string value, string token)
    {
        int index = value.IndexOf(token, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
            return value;

        return value.Remove(index, token.Length);
    }
}
