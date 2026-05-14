using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpeechIntent
{
    public class ObjectPlacementController : MonoBehaviour
    {
        [Header("Catalog")]
        public List<NamedPrefabEntry> namedPrefabs = new List<NamedPrefabEntry>();

        [Header("Fallback")]
        public bool createDebugPlaceholderIfMissing = true;
        public Vector3 fallbackScale = Vector3.one * 0.25f;
        public Transform defaultParent;

        [Header("Runtime Interactables")]
        public bool makePlacedObjectsInteractable = true;
        public bool addColliderIfMissing = true;
        public bool addRigidbody = true;
        public bool addGrabInteractable = true;
        public bool useGravity = false;
        public bool isKinematic = false;
        public float mass = 1f;

        [Header("Generated Materials")]
        public RuntimeMaterialCatalog materialCatalog;
        [Tooltip("Optional material for generated primitives and fallback mesh material. Leave empty to use runtime URP/Simple Lit medium gray.")]
        public Material defaultGeneratedMaterial;
        public Color defaultGeneratedColor = new Color(0.5f, 0.5f, 0.5f, 1f);
        public bool applyDefaultMaterialToGeneratedPrimitives = true;
        public bool applyDefaultMaterialWhenMissing = true;

        [Header("Placement")]
        public float defaultDistance = 2f;
        public float surfaceOffset = 0.01f;
        public bool autoAddTrackableComponent = true;
        public bool requirePointingForPlacement = true;

        public string LastFailureMessage { get; private set; } = "";

        void Awake()
        {
            ResolveMaterialCatalog();
        }

        public GameObject Place(VoiceIntentCommand command, SpatialSnapshot spatial)
        {
            if (command == null)
            {
                LastFailureMessage = "Where?";
                Debug.LogWarning("Place request had null command.");
                return null;
            }

            if (spatial == null &&
                command.spatial_reference != SpatialReferenceMode.BodyAnchor &&
                command.spatial_reference != SpatialReferenceMode.WorldOrigin)
            {
                LastFailureMessage = "Where?";
                Debug.LogWarning("Place request had null spatial snapshot.");
                return null;
            }

            if (!TryResolvePlacementPose(command, spatial, out Vector3 position, out Quaternion rotation))
            {
                if (string.IsNullOrWhiteSpace(LastFailureMessage))
                    LastFailureMessage = "Where?";
                Debug.LogWarning("Could not resolve placement pose. " + LastFailureMessage);
                return null;
            }

            GameObject prefab = FindPrefab(command.object_name);
            GameObject instance = null;

            if (prefab != null)
            {
                instance = Instantiate(prefab, position, rotation, defaultParent);
            }
            else if (TryCreatePrimitive(command.object_name, position, rotation, out instance))
            {
                instance.transform.SetParent(defaultParent, true);
            }
            else if (createDebugPlaceholderIfMissing)
            {
                instance = GameObject.CreatePrimitive(PrimitiveType.Cube);
                instance.name = $"GeneratedPlaceholder_{command.object_name}";
                instance.transform.SetParent(defaultParent, true);
                instance.transform.SetPositionAndRotation(position, rotation);
                instance.transform.localScale = fallbackScale;
            }

            if (instance != null)
            {
                EnsureObjectMetadataAndInteraction(instance, command.object_name);
                Debug.Log($"Placed object '{command.object_name}' at {position}");
            }
            else
            {
                Debug.LogWarning($"No prefab found for '{command.object_name}', and placeholder creation is disabled.");
            }

            return instance;
        }

        public GameObject WrapExistingGeometry(GameObject instance, string canonicalName)
        {
            if (instance == null)
                return null;

            EnsureObjectMetadataAndInteraction(instance, canonicalName);
            return instance;
        }

        private void EnsureObjectMetadataAndInteraction(GameObject instance, string requestedName)
        {
            if (instance == null)
                return;

            if (makePlacedObjectsInteractable)
            {
                InteractableObjectWrapper.Wrap(
                    instance,
                    requestedName,
                    autoAddTrackableComponent,
                    addColliderIfMissing,
                    addRigidbody,
                    addGrabInteractable,
                    useGravity,
                    isKinematic,
                    mass,
                    ResolveDefaultGeneratedMaterial(),
                    ResolveDefaultGeneratedColor(),
                    applyDefaultMaterialWhenMissing);
                return;
            }

            EnsureTrackable(instance, requestedName);
        }

        private void EnsureTrackable(GameObject instance, string requestedName)
        {
            if (!autoAddTrackableComponent || instance == null)
            {
                return;
            }

            SpeechIntentTrackable trackable = instance.GetComponent<SpeechIntentTrackable>();
            if (trackable == null)
            {
                trackable = instance.AddComponent<SpeechIntentTrackable>();
            }

            if (string.IsNullOrWhiteSpace(trackable.canonicalName))
            {
                trackable.canonicalName = string.IsNullOrWhiteSpace(requestedName) ? instance.name : requestedName;
            }
        }

        public GameObject FindPrefab(string objectName)
        {
            if (string.IsNullOrWhiteSpace(objectName))
            {
                return null;
            }

            foreach (NamedPrefabEntry entry in namedPrefabs)
            {
                if (entry != null &&
                    entry.prefab != null &&
                    !string.IsNullOrWhiteSpace(entry.name) &&
                    string.Equals(entry.name, objectName, StringComparison.OrdinalIgnoreCase))
                {
                    return entry.prefab;
                }
            }

            return null;
        }

        private bool TryCreatePrimitive(string objectName, Vector3 position, Quaternion rotation, out GameObject instance)
        {
            instance = null;
            if (!TryResolvePrimitiveType(objectName, out PrimitiveType primitiveType, out string label))
                return false;

            instance = GameObject.CreatePrimitive(primitiveType);
            instance.name = $"Generated_{label}";
            instance.transform.SetPositionAndRotation(position, rotation);
            instance.transform.localScale = ResolvePrimitiveScale(primitiveType);
            ReplacePlaneColliderIfNeeded(instance, primitiveType);
            ApplyDefaultMaterialToGeneratedObject(instance);
            return true;
        }

        private bool TryResolvePrimitiveType(string objectName, out PrimitiveType primitiveType, out string label)
        {
            primitiveType = PrimitiveType.Cube;
            label = "Cube";

            string normalized = NormalizeObjectName(objectName);
            switch (normalized)
            {
                case "box":
                case "cube":
                case "block":
                    primitiveType = PrimitiveType.Cube;
                    label = "Cube";
                    return true;

                case "ball":
                case "orb":
                case "sphere":
                    primitiveType = PrimitiveType.Sphere;
                    label = "Sphere";
                    return true;

                case "capsule":
                    primitiveType = PrimitiveType.Capsule;
                    label = "Capsule";
                    return true;

                case "cylinder":
                case "column":
                    primitiveType = PrimitiveType.Cylinder;
                    label = "Cylinder";
                    return true;

                case "plane":
                case "floor":
                case "platform":
                    primitiveType = PrimitiveType.Plane;
                    label = "Plane";
                    return true;

                default:
                    return false;
            }
        }

        private Vector3 ResolvePrimitiveScale(PrimitiveType primitiveType)
        {
            if (primitiveType == PrimitiveType.Plane)
            {
                return fallbackScale * 0.4f;
            }

            return fallbackScale;
        }

        private void ReplacePlaneColliderIfNeeded(GameObject instance, PrimitiveType primitiveType)
        {
            if (instance == null || primitiveType != PrimitiveType.Plane)
                return;

            MeshCollider meshCollider = instance.GetComponent<MeshCollider>();
            if (meshCollider != null)
                Destroy(meshCollider);

            BoxCollider boxCollider = instance.AddComponent<BoxCollider>();
            boxCollider.center = Vector3.zero;
            boxCollider.size = new Vector3(10f, 0.02f, 10f);
        }

        private void ApplyDefaultMaterialToGeneratedObject(GameObject instance)
        {
            if (!applyDefaultMaterialToGeneratedPrimitives)
                return;

            InteractableObjectWrapper.ApplyMaterial(
                instance,
                ResolveDefaultGeneratedMaterial(),
                ResolveDefaultGeneratedColor(),
                true);
        }

        private Material ResolveDefaultGeneratedMaterial()
        {
            ResolveMaterialCatalog();
            if (defaultGeneratedMaterial != null)
                return defaultGeneratedMaterial;

            return materialCatalog != null ? materialCatalog.DefaultMaterial : null;
        }

        private Color ResolveDefaultGeneratedColor()
        {
            ResolveMaterialCatalog();
            return materialCatalog != null ? materialCatalog.defaultDescriptor.color : defaultGeneratedColor;
        }

        private void ResolveMaterialCatalog()
        {
            if (materialCatalog == null)
                materialCatalog = FindFirstObjectByType<RuntimeMaterialCatalog>(FindObjectsInactive.Include);
        }

        private string NormalizeObjectName(string objectName)
        {
            if (string.IsNullOrWhiteSpace(objectName))
                return "";

            string normalized = objectName.Trim().ToLowerInvariant();
            if (normalized.StartsWith("a ", StringComparison.Ordinal))
                normalized = normalized.Substring(2).Trim();
            else if (normalized.StartsWith("an ", StringComparison.Ordinal))
                normalized = normalized.Substring(3).Trim();
            else if (normalized.StartsWith("the ", StringComparison.Ordinal))
                normalized = normalized.Substring(4).Trim();

            if (normalized.EndsWith("s", StringComparison.Ordinal) && normalized.Length > 1)
                normalized = normalized.Substring(0, normalized.Length - 1);

            return normalized;
        }

        private bool TryResolvePlacementPose(VoiceIntentCommand command, SpatialSnapshot spatial, out Vector3 position, out Quaternion rotation)
        {
            LastFailureMessage = "";
            position = Vector3.zero;
            rotation = Quaternion.identity;

            if (command.spatial_reference == SpatialReferenceMode.BodyAnchor)
            {
                if (BodyAnchorResolver.TryResolve(spatial, command.body_anchor, command.target_hand, out position, out rotation))
                    return true;

                LastFailureMessage = ResolveBodyAnchorFailure(command);
                return false;
            }

            if (command.spatial_reference == SpatialReferenceMode.WorldOrigin)
            {
                position = Vector3.zero;
                Vector3 forward = spatial != null && spatial.head_forward.sqrMagnitude > 0.0001f
                    ? Vector3.ProjectOnPlane(spatial.head_forward, Vector3.up).normalized
                    : Vector3.forward;
                if (forward.sqrMagnitude <= 0.0001f)
                    forward = Vector3.forward;
                rotation = Quaternion.LookRotation(forward, Vector3.up);
                return true;
            }

            if (command.spatial_reference == SpatialReferenceMode.HandMidpoint && spatial.has_hand_midpoint)
            {
                position = spatial.hand_midpoint;
                rotation = Quaternion.LookRotation(spatial.head_forward == Vector3.zero ? Vector3.forward : spatial.head_forward, Vector3.up);
                return true;
            }

            if (command.spatial_reference == SpatialReferenceMode.HeadForward && spatial.head_forward.sqrMagnitude > 0.0001f)
            {
                position = spatial.head_position + spatial.head_forward.normalized * defaultDistance;
                rotation = Quaternion.LookRotation(spatial.head_forward == Vector3.zero ? Vector3.forward : spatial.head_forward, Vector3.up);
                return true;
            }

            if (!SpatialReferenceResolver.TryResolvePointingHand(
                    spatial,
                    command.target_hand,
                    out HandRaySnapshot rayHand,
                    out SpatialResolveResult result))
            {
                if (requirePointingForPlacement ||
                    command.spatial_reference == SpatialReferenceMode.PointingHit ||
                    command.spatial_reference == SpatialReferenceMode.PointingRay ||
                    command.spatial_reference == SpatialReferenceMode.None)
                {
                    LastFailureMessage = result.message;
                    return false;
                }
            }

            if (rayHand == null)
            {
                LastFailureMessage = "Where?";
                return false;
            }

            if (command.spatial_reference == SpatialReferenceMode.PointingHit && rayHand.has_hit)
            {
                position = rayHand.hit_point + rayHand.hit_normal * surfaceOffset;
                rotation = Quaternion.LookRotation(ProjectForwardOnPlane(spatial.head_forward, rayHand.hit_normal), rayHand.hit_normal);
                return true;
            }

            Vector3 direction = rayHand.direction.normalized;
            position = rayHand.origin + direction * defaultDistance;
            rotation = Quaternion.LookRotation(-direction, Vector3.up);
            return true;
        }

        private string ResolveBodyAnchorFailure(VoiceIntentCommand command)
        {
            BodyAnchor anchor = command.body_anchor != BodyAnchor.None
                ? command.body_anchor
                : BodyAnchorResolver.FromHandSelection(command.target_hand);

            return anchor switch
            {
                BodyAnchor.Head => "I could not find your head camera.",
                BodyAnchor.LeftHand => "I could not find your left hand or controller.",
                BodyAnchor.RightHand => "I could not find your right hand or controller.",
                _ => "Which part of you should I use?"
            };
        }

        private Vector3 ProjectForwardOnPlane(Vector3 forward, Vector3 normal)
        {
            Vector3 projected = Vector3.ProjectOnPlane(forward, normal);
            if (projected.sqrMagnitude < 0.0001f)
            {
                projected = Vector3.forward;
            }
            return projected.normalized;
        }
    }
}
