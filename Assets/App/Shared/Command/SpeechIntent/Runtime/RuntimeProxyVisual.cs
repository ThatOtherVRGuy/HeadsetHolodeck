using UnityEngine;

namespace SpeechIntent
{
    public enum RuntimeProxyCategory
    {
        General = 0,
        Light = 1,
        Audio = 2
    }

    public class RuntimeProxyVisual : MonoBehaviour
    {
        public RuntimeProxyCategory category = RuntimeProxyCategory.General;
        public Transform visualRoot;
        public bool visibleByDefault = false;
        public bool IsVisible { get; private set; }

        void Awake()
        {
            EnsureVisualRoot();
            SetVisible(visibleByDefault);
        }

        public void SetVisible(bool visible)
        {
            IsVisible = visible;
            EnsureVisualRoot();
            if (visualRoot != null)
                visualRoot.gameObject.SetActive(visible);
        }

        public void EnsureVisualRoot()
        {
            if (visualRoot != null)
                return;

            Transform existing = transform.Find("ProxyVisual");
            if (existing != null)
            {
                visualRoot = existing;
                return;
            }

            GameObject root = new GameObject("ProxyVisual");
            root.transform.SetParent(transform, false);
            visualRoot = root.transform;
        }

        public void RebuildPrimitive(RuntimeProxyCategory targetCategory, Color color, RuntimeLightKind lightKind = RuntimeLightKind.Point)
        {
            category = targetCategory;
            EnsureVisualRoot();
            ClearChildren(visualRoot);

            switch (targetCategory)
            {
                case RuntimeProxyCategory.Audio:
                    BuildAudioProxy(color);
                    break;
                case RuntimeProxyCategory.Light:
                    BuildLightProxy(color, lightKind);
                    break;
                default:
                    BuildSphere(color, 0.12f, "Proxy");
                    break;
            }

            SetVisible(IsVisible || visibleByDefault);
        }

        void BuildAudioProxy(Color color)
        {
            BuildSphere(color, 0.11f, "AudioCore");
            GameObject pulse = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pulse.name = "AudioPulse";
            pulse.transform.SetParent(visualRoot, false);
            pulse.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            pulse.transform.localScale = new Vector3(0.18f, 0.012f, 0.18f);
            ApplyMaterial(pulse, color);
            DestroyRuntimeCollider(pulse);
        }

        void BuildLightProxy(Color color, RuntimeLightKind lightKind)
        {
            BuildSphere(color, 0.1f, "LightCore");

            if (lightKind == RuntimeLightKind.Spot)
            {
                GameObject cone = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                cone.name = "SpotDirection";
                cone.transform.SetParent(visualRoot, false);
                cone.transform.localPosition = Vector3.forward * 0.18f;
                cone.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                cone.transform.localScale = new Vector3(0.07f, 0.18f, 0.07f);
                ApplyMaterial(cone, color);
                DestroyRuntimeCollider(cone);
            }
            else if (lightKind == RuntimeLightKind.Directional)
            {
                GameObject arrow = GameObject.CreatePrimitive(PrimitiveType.Cube);
                arrow.name = "DirectionalRay";
                arrow.transform.SetParent(visualRoot, false);
                arrow.transform.localPosition = Vector3.forward * 0.22f;
                arrow.transform.localScale = new Vector3(0.04f, 0.04f, 0.38f);
                ApplyMaterial(arrow, color);
                DestroyRuntimeCollider(arrow);
            }
        }

        void BuildSphere(Color color, float scale, string name)
        {
            GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = name;
            sphere.transform.SetParent(visualRoot, false);
            sphere.transform.localScale = Vector3.one * scale;
            ApplyMaterial(sphere, color);
            DestroyRuntimeCollider(sphere);
        }

        static void ApplyMaterial(GameObject target, Color color)
        {
            Renderer renderer = target != null ? target.GetComponent<Renderer>() : null;
            if (renderer == null)
                return;

            Shader shader =
                Shader.Find("Universal Render Pipeline/Simple Lit") ??
                Shader.Find("Universal Render Pipeline/Lit") ??
                Shader.Find("Standard");

            if (shader == null)
                return;

            Material material = new Material(shader)
            {
                name = "Runtime Proxy " + ColorUtility.ToHtmlStringRGB(color)
            };
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
            else if (material.HasProperty("_Color"))
                material.SetColor("_Color", color);
            renderer.sharedMaterial = material;
        }

        static void DestroyRuntimeCollider(GameObject target)
        {
            Collider collider = target != null ? target.GetComponent<Collider>() : null;
            if (collider != null)
            {
                if (Application.isPlaying)
                    Destroy(collider);
                else
                    DestroyImmediate(collider);
            }
        }

        static void ClearChildren(Transform root)
        {
            if (root == null)
                return;

            for (int i = root.childCount - 1; i >= 0; i--)
            {
                Transform child = root.GetChild(i);
                if (Application.isPlaying)
                    Destroy(child.gameObject);
                else
                    DestroyImmediate(child.gameObject);
            }
        }
    }
}
