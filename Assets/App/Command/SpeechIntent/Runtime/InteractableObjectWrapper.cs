using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace SpeechIntent
{
    /// <summary>
    /// Adds the common runtime pieces that make generated or imported geometry behave
    /// like a Headset Holodeck object: voice-trackable, physics-backed, and grabbable.
    /// </summary>
    public static class InteractableObjectWrapper
    {
        public static void Wrap(
            GameObject root,
            string canonicalName,
            bool addTrackable,
            bool addColliderIfMissing,
            bool addRigidbody,
            bool addGrabInteractable,
            bool useGravity,
            bool isKinematic,
            float mass,
            Material fallbackMaterial,
            Color fallbackColor,
            bool applyMaterialWhenMissing)
        {
            if (root == null)
                return;

            NormalizeRendering(root, fallbackMaterial, fallbackColor);

            if (applyMaterialWhenMissing)
                ApplyMaterial(root, fallbackMaterial, fallbackColor, false);

            if (addTrackable)
                EnsureTrackable(root, canonicalName);

            if (addColliderIfMissing)
                EnsureCollider(root);

            Rigidbody body = null;
            if (addRigidbody)
            {
                body = root.GetComponent<Rigidbody>();
                if (body == null)
                    body = root.GetComponentInChildren<Rigidbody>(true);
                if (body == null)
                {
                    body = root.AddComponent<Rigidbody>();
                }

                body.useGravity = useGravity;
                body.isKinematic = isKinematic;
                body.mass = Mathf.Max(0.001f, mass);
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            }

            if (addGrabInteractable)
            {
                XRGrabInteractable grab = root.GetComponent<XRGrabInteractable>();
                if (grab == null)
                    grab = root.AddComponent<XRGrabInteractable>();

                grab.movementType = XRBaseInteractable.MovementType.Instantaneous;
                grab.trackPosition = true;
                grab.trackRotation = true;
                grab.trackScale = true;
                grab.throwOnDetach = true;

                if (body != null && !isKinematic)
                    body.interpolation = RigidbodyInterpolation.Interpolate;
            }
        }

        public static void EnsureTrackable(GameObject root, string canonicalName)
        {
            if (root == null)
                return;

            SpeechIntentTrackable trackable = root.GetComponent<SpeechIntentTrackable>();
            if (trackable == null)
                trackable = root.AddComponent<SpeechIntentTrackable>();

            if (string.IsNullOrWhiteSpace(trackable.canonicalName))
                trackable.canonicalName = string.IsNullOrWhiteSpace(canonicalName) ? root.name : canonicalName;
        }

        public static void ApplyMaterial(GameObject root, Material material, Color fallbackColor, bool force)
        {
            if (root == null)
                return;

            Material resolved = material != null ? material : GetRuntimeDefaultMaterial(fallbackColor);
            if (resolved == null)
                return;

            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null)
                    continue;

                if (force || renderer.sharedMaterial == null || renderer.sharedMaterials == null || renderer.sharedMaterials.Length == 0)
                    renderer.sharedMaterial = resolved;
            }
        }

        static Material _runtimeDefaultMaterial;
        static Color _runtimeDefaultColor;

        public static void NormalizeRendering(GameObject root, Material fallbackMaterial, Color fallbackColor)
        {
            if (root == null)
                return;

            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null)
                    continue;

                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;
                NormalizeRendererMaterials(renderer, fallbackMaterial, fallbackColor);
            }
        }

        static void NormalizeRendererMaterials(Renderer renderer, Material fallbackMaterial, Color fallbackColor)
        {
            Material[] materials = renderer.sharedMaterials;
            if (materials == null || materials.Length == 0)
            {
                renderer.sharedMaterial = fallbackMaterial != null ? fallbackMaterial : GetRuntimeDefaultMaterial(fallbackColor);
                return;
            }

            bool changed = false;
            for (int i = 0; i < materials.Length; i++)
            {
                Material material = materials[i];
                if (material == null)
                {
                    materials[i] = fallbackMaterial != null ? fallbackMaterial : GetRuntimeDefaultMaterial(fallbackColor);
                    changed = true;
                    continue;
                }

                if (!IsProbablyUnlit(material))
                    continue;

                Material lit = CreateLitCopy(material, fallbackColor);
                if (lit == null)
                    continue;

                materials[i] = lit;
                changed = true;
            }

            if (changed)
                renderer.sharedMaterials = materials;
        }

        static bool IsProbablyUnlit(Material material)
        {
            if (material == null || material.shader == null)
                return true;

            string shaderName = material.shader.name ?? "";
            return shaderName.IndexOf("Unlit", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                   shaderName.IndexOf("glTFast/Unlit", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static Material CreateLitCopy(Material source, Color fallbackColor)
        {
            Shader shader =
                Shader.Find("Universal Render Pipeline/Lit") ??
                Shader.Find("Universal Render Pipeline/Simple Lit") ??
                Shader.Find("Standard");
            if (shader == null)
                return null;

            Material lit = new Material(shader)
            {
                name = source.name + " Lit"
            };

            Texture texture = GetFirstTexture(source, "_BaseMap", "_MainTex", "baseColorTexture");
            if (texture != null)
            {
                if (lit.HasProperty("_BaseMap"))
                    lit.SetTexture("_BaseMap", texture);
                else if (lit.HasProperty("_MainTex"))
                    lit.SetTexture("_MainTex", texture);
            }

            Color color = ReadColor(source, fallbackColor);
            if (lit.HasProperty("_BaseColor"))
                lit.SetColor("_BaseColor", color);
            else if (lit.HasProperty("_Color"))
                lit.SetColor("_Color", color);

            if (lit.HasProperty("_Metallic"))
                lit.SetFloat("_Metallic", source.HasProperty("_Metallic") ? source.GetFloat("_Metallic") : 0f);
            if (lit.HasProperty("_Smoothness"))
                lit.SetFloat("_Smoothness", source.HasProperty("_Smoothness") ? source.GetFloat("_Smoothness") : 0.45f);

            return lit;
        }

        static Texture GetFirstTexture(Material material, params string[] propertyNames)
        {
            foreach (string propertyName in propertyNames)
            {
                if (material.HasProperty(propertyName))
                {
                    Texture texture = material.GetTexture(propertyName);
                    if (texture != null)
                        return texture;
                }
            }

            return material.mainTexture;
        }

        static Color ReadColor(Material material, Color fallbackColor)
        {
            if (material.HasProperty("_BaseColor"))
                return material.GetColor("_BaseColor");
            if (material.HasProperty("_Color"))
                return material.GetColor("_Color");
            return fallbackColor;
        }

        static Material GetRuntimeDefaultMaterial(Color color)
        {
            if (_runtimeDefaultMaterial != null && _runtimeDefaultColor == color)
                return _runtimeDefaultMaterial;

            Shader shader =
                Shader.Find("Universal Render Pipeline/Simple Lit") ??
                Shader.Find("Universal Render Pipeline/Lit") ??
                Shader.Find("Standard");
            if (shader == null)
            {
                Debug.LogWarning("[InteractableObjectWrapper] Could not find a URP or Standard shader for generated object material.");
                return null;
            }

            _runtimeDefaultMaterial = new Material(shader)
            {
                name = "Generated URP Medium Gray"
            };
            _runtimeDefaultColor = color;

            if (_runtimeDefaultMaterial.HasProperty("_BaseColor"))
                _runtimeDefaultMaterial.SetColor("_BaseColor", color);
            else if (_runtimeDefaultMaterial.HasProperty("_Color"))
                _runtimeDefaultMaterial.SetColor("_Color", color);

            return _runtimeDefaultMaterial;
        }

        static void EnsureCollider(GameObject root)
        {
            Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
            if (colliders != null && colliders.Length > 0)
                return;

            Bounds bounds;
            if (!TryGetRendererBounds(root, out bounds))
            {
                BoxCollider defaultCollider = root.AddComponent<BoxCollider>();
                defaultCollider.size = Vector3.one;
                defaultCollider.center = Vector3.zero;
                return;
            }

            BoxCollider box = root.AddComponent<BoxCollider>();
            Vector3 localCenter = root.transform.InverseTransformPoint(bounds.center);
            Vector3 localSize = root.transform.InverseTransformVector(bounds.size);
            box.center = localCenter;
            box.size = new Vector3(
                Mathf.Abs(localSize.x),
                Mathf.Abs(localSize.y),
                Mathf.Abs(localSize.z));
        }

        static bool TryGetRendererBounds(GameObject root, out Bounds bounds)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            bounds = new Bounds(root.transform.position, Vector3.zero);
            bool hasBounds = false;

            foreach (Renderer renderer in renderers)
            {
                if (renderer == null)
                    continue;

                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            return hasBounds;
        }
    }
}
