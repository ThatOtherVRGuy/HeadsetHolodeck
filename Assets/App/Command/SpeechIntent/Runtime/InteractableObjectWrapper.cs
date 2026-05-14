using UnityEngine;
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
                bool createdBody = false;
                if (body == null)
                {
                    body = root.AddComponent<Rigidbody>();
                    createdBody = true;
                }

                if (createdBody)
                {
                    body.useGravity = useGravity;
                    body.isKinematic = isKinematic;
                    body.mass = Mathf.Max(0.001f, mass);
                    body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                }
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
