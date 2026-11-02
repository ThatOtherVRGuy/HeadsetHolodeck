using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpeechIntent
{
    [Serializable]
    public struct RuntimeMaterialDescriptor
    {
        public string name;
        public Color color;
        [Range(0f, 1f)] public float metallic;
        [Range(0f, 1f)] public float smoothness;

        public string Key => BuildKey(name, color, metallic, smoothness);

        public static RuntimeMaterialDescriptor Default => new RuntimeMaterialDescriptor
        {
            name = "gray",
            color = new Color(0.5f, 0.5f, 0.5f, 1f),
            metallic = 0f,
            smoothness = 0.45f
        };

        public static string BuildKey(string name, Color color, float metallic, float smoothness)
        {
            string label = string.IsNullOrWhiteSpace(name) ? "material" : name.Trim().ToLowerInvariant();
            return $"{label}|{ColorUtility.ToHtmlStringRGBA(color)}|m{Mathf.RoundToInt(metallic * 100f)}|s{Mathf.RoundToInt(smoothness * 100f)}";
        }
    }

    public class RuntimeMaterialCatalog : MonoBehaviour
    {
        public RuntimeMaterialDescriptor defaultDescriptor = RuntimeMaterialDescriptor.Default;
        public bool debugLogging = false;

        readonly Dictionary<string, Material> _materials = new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<Material, string> _materialKeys = new Dictionary<Material, string>();

        public Material DefaultMaterial => GetOrCreate(defaultDescriptor);

        public Material GetOrCreate(RuntimeMaterialDescriptor descriptor)
        {
            string key = descriptor.Key;
            if (_materials.TryGetValue(key, out Material existing) && existing != null)
                return existing;

            Material material = CreateMaterial(descriptor);
            if (material == null)
                return null;

            _materials[key] = material;
            _materialKeys[material] = key;
            if (debugLogging)
                Debug.Log($"[RuntimeMaterialCatalog] Created material '{material.name}'.");
            return material;
        }

        public Material Fork(Material source, RuntimeMaterialDescriptor descriptor)
        {
            Material fork = CreateMaterial(descriptor);
            if (fork == null)
                return null;

            if (source != null)
            {
                fork.CopyPropertiesFromMaterial(source);
                ApplyDescriptor(fork, descriptor);
            }

            string key = descriptor.Key;
            _materials[key] = fork;
            _materialKeys[fork] = key;
            return fork;
        }

        public void ApplyTo(GameObject target, RuntimeMaterialDescriptor descriptor, bool forkIfShared)
        {
            if (target == null)
                return;

            Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null)
                    continue;

                Material[] materials = renderer.sharedMaterials;
                if (materials == null || materials.Length == 0)
                {
                    renderer.sharedMaterial = GetOrCreate(descriptor);
                    continue;
                }

                bool changed = false;
                for (int i = 0; i < materials.Length; i++)
                {
                    Material current = materials[i];
                    if (current == null)
                    {
                        materials[i] = GetOrCreate(descriptor);
                        changed = true;
                        continue;
                    }

                    if (!forkIfShared || !IsUsedOutsideTarget(current, target))
                    {
                        Material existing = FindExisting(descriptor);
                        if (existing != null && existing != current)
                        {
                            materials[i] = existing;
                            changed = true;
                        }
                        else
                        {
                            MoveCatalogEntry(current, descriptor);
                            ApplyDescriptor(current, descriptor);
                        }
                    }
                    else
                    {
                        materials[i] = GetOrCreate(descriptor);
                        changed = true;
                    }
                }

                if (changed)
                    renderer.sharedMaterials = materials;
            }
        }

        public bool TryParseDescriptor(string prompt, out RuntimeMaterialDescriptor descriptor)
        {
            return TryParseDescriptor(prompt, defaultDescriptor, out descriptor);
        }

        public bool ObjectMatches(GameObject target, string prompt)
        {
            if (!TryParseDescriptor(prompt, out RuntimeMaterialDescriptor descriptor))
                return true;

            return ObjectMatches(target, descriptor);
        }

        public static bool ObjectMatches(GameObject target, RuntimeMaterialDescriptor descriptor, float colorTolerance = 0.22f)
        {
            if (target == null)
                return false;

            Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null)
                    continue;

                Material[] materials = renderer.sharedMaterials;
                if (materials == null)
                    continue;

                foreach (Material material in materials)
                {
                    if (MaterialMatches(material, descriptor, colorTolerance))
                        return true;
                }
            }

            return false;
        }

        public static bool TryParseDescriptor(string prompt, RuntimeMaterialDescriptor fallback, out RuntimeMaterialDescriptor descriptor)
        {
            descriptor = fallback;
            string lower = (prompt ?? "").ToLowerInvariant();
            bool matched = TryParseColor(lower, out Color color, out string colorName);
            bool metallic = ContainsAny(lower, "metallic", "metal", "chrome", "silver", "gold");
            bool matte = ContainsAny(lower, "matte", "flat", "dull");
            bool glossy = ContainsAny(lower, "glossy", "shiny", "polished");

            if (!matched && !metallic && !matte && !glossy)
                return false;

            if (!matched)
            {
                color = fallback.color;
                colorName = fallback.name;
            }

            descriptor = new RuntimeMaterialDescriptor
            {
                name = BuildName(colorName, metallic, matte, glossy),
                color = color,
                metallic = metallic ? 1f : fallback.metallic,
                smoothness = matte ? 0.18f : (metallic || glossy ? 0.82f : fallback.smoothness)
            };
            return true;
        }

        Material FindExisting(RuntimeMaterialDescriptor descriptor)
        {
            return _materials.TryGetValue(descriptor.Key, out Material existing) ? existing : null;
        }

        void MoveCatalogEntry(Material material, RuntimeMaterialDescriptor descriptor)
        {
            if (material == null)
                return;

            if (_materialKeys.TryGetValue(material, out string oldKey))
            {
                if (_materials.TryGetValue(oldKey, out Material current) && current == material)
                    _materials.Remove(oldKey);
            }

            string newKey = descriptor.Key;
            _materials[newKey] = material;
            _materialKeys[material] = newKey;
        }

        static Material CreateMaterial(RuntimeMaterialDescriptor descriptor)
        {
            Shader shader =
                Shader.Find("Universal Render Pipeline/Simple Lit") ??
                Shader.Find("Universal Render Pipeline/Lit") ??
                Shader.Find("Standard");

            if (shader == null)
            {
                Debug.LogWarning("[RuntimeMaterialCatalog] Could not find URP Simple Lit, URP Lit, or Standard shader.");
                return null;
            }

            Material material = new Material(shader)
            {
                name = "Runtime " + descriptor.name
            };
            ApplyDescriptor(material, descriptor);
            return material;
        }

        static bool MaterialMatches(Material material, RuntimeMaterialDescriptor descriptor, float colorTolerance)
        {
            if (material == null)
                return false;

            Color color = Color.white;
            bool hasColor = false;
            if (material.HasProperty("_BaseColor"))
            {
                color = material.GetColor("_BaseColor");
                hasColor = true;
            }
            else if (material.HasProperty("_Color"))
            {
                color = material.GetColor("_Color");
                hasColor = true;
            }

            if (!hasColor || ColorDistance(color, descriptor.color) > colorTolerance)
                return false;

            if (descriptor.metallic > 0.5f && material.HasProperty("_Metallic") && material.GetFloat("_Metallic") < 0.5f)
                return false;

            if (descriptor.smoothness <= 0.25f && TryGetSmoothness(material, out float smoothness) && smoothness > 0.35f)
                return false;

            if (descriptor.smoothness >= 0.75f && TryGetSmoothness(material, out smoothness) && smoothness < 0.55f)
                return false;

            return true;
        }

        static bool TryGetSmoothness(Material material, out float smoothness)
        {
            if (material.HasProperty("_Smoothness"))
            {
                smoothness = material.GetFloat("_Smoothness");
                return true;
            }

            if (material.HasProperty("_Glossiness"))
            {
                smoothness = material.GetFloat("_Glossiness");
                return true;
            }

            smoothness = 0f;
            return false;
        }

        static float ColorDistance(Color a, Color b)
        {
            float dr = a.r - b.r;
            float dg = a.g - b.g;
            float db = a.b - b.b;
            return Mathf.Sqrt(dr * dr + dg * dg + db * db);
        }

        static void ApplyDescriptor(Material material, RuntimeMaterialDescriptor descriptor)
        {
            if (material == null)
                return;

            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", descriptor.color);
            else if (material.HasProperty("_Color"))
                material.SetColor("_Color", descriptor.color);

            if (material.HasProperty("_Metallic"))
                material.SetFloat("_Metallic", descriptor.metallic);
            if (material.HasProperty("_Smoothness"))
                material.SetFloat("_Smoothness", descriptor.smoothness);
            if (material.HasProperty("_Glossiness"))
                material.SetFloat("_Glossiness", descriptor.smoothness);

            material.name = "Runtime " + descriptor.name;
        }

        static bool IsUsedOutsideTarget(Material material, GameObject target)
        {
            Renderer[] renderers = FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null || renderer.transform.IsChildOf(target.transform))
                    continue;

                Material[] materials = renderer.sharedMaterials;
                if (materials == null)
                    continue;

                foreach (Material candidate in materials)
                {
                    if (candidate == material)
                        return true;
                }
            }

            return false;
        }

        static bool TryParseColor(string lower, out Color color, out string name)
        {
            if (ContainsAny(lower, "grey", "gray")) return Named(new Color(0.5f, 0.5f, 0.5f, 1f), "gray", out color, out name);
            if (lower.Contains("red")) return Named(Color.red, "red", out color, out name);
            if (lower.Contains("blue")) return Named(Color.blue, "blue", out color, out name);
            if (lower.Contains("green")) return Named(Color.green, "green", out color, out name);
            if (lower.Contains("yellow")) return Named(Color.yellow, "yellow", out color, out name);
            if (lower.Contains("orange")) return Named(new Color(1f, 0.45f, 0f, 1f), "orange", out color, out name);
            if (lower.Contains("purple")) return Named(new Color(0.55f, 0.2f, 0.9f, 1f), "purple", out color, out name);
            if (lower.Contains("pink")) return Named(new Color(1f, 0.35f, 0.7f, 1f), "pink", out color, out name);
            if (lower.Contains("black")) return Named(Color.black, "black", out color, out name);
            if (lower.Contains("white")) return Named(Color.white, "white", out color, out name);
            if (lower.Contains("silver")) return Named(new Color(0.75f, 0.75f, 0.72f, 1f), "silver", out color, out name);
            if (lower.Contains("gold")) return Named(new Color(1f, 0.72f, 0.16f, 1f), "gold", out color, out name);

            color = Color.white;
            name = "";
            return false;
        }

        static bool Named(Color value, string label, out Color color, out string name)
        {
            color = value;
            name = label;
            return true;
        }

        static string BuildName(string colorName, bool metallic, bool matte, bool glossy)
        {
            string finish = metallic ? "metallic" : (matte ? "matte" : (glossy ? "glossy" : ""));
            return string.IsNullOrWhiteSpace(finish) ? colorName : $"{colorName} {finish}";
        }

        static bool ContainsAny(string value, params string[] needles)
        {
            foreach (string needle in needles)
            {
                if (value.Contains(needle))
                    return true;
            }
            return false;
        }
    }
}
