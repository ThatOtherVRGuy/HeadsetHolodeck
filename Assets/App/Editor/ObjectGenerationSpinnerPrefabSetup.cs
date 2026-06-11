using System.IO;
using Holodeck.Direct;
using UnityEditor;
using UnityEngine;

namespace HeadsetHolodeck.EditorTools
{
    public static class ObjectGenerationSpinnerPrefabSetup
    {
        const string PrefabPath = "Assets/App/Prefabs/ObjectGenerationSpinner.prefab";
        const string MaterialFolder = "Assets/App/Materials/ObjectGeneration";
        const string BlueMaterialPath = MaterialFolder + "/ObjectGenerationSpinner_Blue.mat";
        const string AmberMaterialPath = MaterialFolder + "/ObjectGenerationSpinner_Amber.mat";

        [MenuItem("Headset Holodeck/Object Generation/Create Spinner Prefab")]
        public static void CreateOrUpdateSpinnerPrefab()
        {
            string folder = Path.GetDirectoryName(PrefabPath);
            if (!string.IsNullOrWhiteSpace(folder) && !AssetDatabase.IsValidFolder(folder))
                Directory.CreateDirectory(folder);

            GameObject root = new GameObject("ObjectGenerationSpinner");
            try
            {
                ObjectGenerationSpinnerController spinner = root.AddComponent<ObjectGenerationSpinnerController>();
                spinner.rotationDegreesPerSecond = new Vector3(0f, 42f, 0f);
                spinner.sparkleColor = new Color(0.35f, 0.85f, 1f, 1f);
                spinner.accentColor = new Color(1f, 0.62f, 0.08f, 1f);
                spinner.BuildDefaultVisuals();
                AssignPersistentMaterials(root);

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log($"[ObjectGenerationSpinnerPrefabSetup] Spinner prefab saved to {PrefabPath}");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        static void AssignPersistentMaterials(GameObject root)
        {
            EnsureFolder(MaterialFolder);
            Material blue = CreateOrUpdateMaterial(BlueMaterialPath, new Color(0.35f, 0.85f, 1f, 1f));
            Material amber = CreateOrUpdateMaterial(AmberMaterialPath, new Color(1f, 0.62f, 0.08f, 1f));

            foreach (ParticleSystemRenderer renderer in root.GetComponentsInChildren<ParticleSystemRenderer>(true))
            {
                bool amberRenderer = renderer.gameObject.name.IndexOf("Amber", System.StringComparison.OrdinalIgnoreCase) >= 0;
                renderer.sharedMaterial = amberRenderer ? amber : blue;
            }
        }

        static Material CreateOrUpdateMaterial(string path, Color color)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
                if (shader == null)
                    shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null)
                    shader = Shader.Find("Sprites/Default");

                material = new Material(shader)
                {
                    name = Path.GetFileNameWithoutExtension(path)
                };
                AssetDatabase.CreateAsset(material, path);
            }

            material.color = color;
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", color);
            if (material.HasProperty("_Surface"))
                material.SetFloat("_Surface", 1f);
            if (material.HasProperty("_Blend"))
                material.SetFloat("_Blend", 0f);
            if (material.HasProperty("_ZWrite"))
                material.SetFloat("_ZWrite", 0f);

            material.renderQueue = 3000;
            EditorUtility.SetDirty(material);
            return material;
        }

        static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
                return;

            string[] parts = folder.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
