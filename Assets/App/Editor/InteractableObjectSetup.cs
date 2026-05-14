using System;
using System.Collections.Generic;
using System.IO;
using SpeechIntent;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class InteractableObjectSetup
{
    const string InteractablePrefabFolder = "Assets/VRTemplateAssets/Prefabs/Interactables";
    const string SetupScenePath = "Assets/Scenes/Holodeck.unity";

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
