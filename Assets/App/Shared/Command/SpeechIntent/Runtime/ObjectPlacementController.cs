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
        public bool useGravity = true;
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
        public float defaultCreationDistance = 1f;
        public float defaultTargetRelativeDistance = 1f;
        public float footPlacementRaycastHeight = 2.5f;
        public float footPlacementRaycastDistance = 8f;
        public LayerMask footPlacementLayers = ~0;
        public float surfaceOffset = 0.01f;
        public bool autoAddTrackableComponent = true;
        public bool requirePointingForPlacement = true;
        public SceneEntityResolver entityResolver;

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
                command.spatial_reference != SpatialReferenceMode.WorldOrigin &&
                command.spatial_reference != SpatialReferenceMode.RelativeToMe &&
                command.spatial_reference != SpatialReferenceMode.RelativeToTarget &&
                command.spatial_reference != SpatialReferenceMode.None)
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
                ApplyPlacementSpecificMetadata(instance, command);
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

            if (IsTeleportTarget(instance))
            {
                EnsureTrackable(instance, requestedName);
                return;
            }

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

            InteractableObjectWrapper.NormalizeRendering(
                instance,
                ResolveDefaultGeneratedMaterial(),
                ResolveDefaultGeneratedColor());
            EnsureTrackable(instance, requestedName);
        }

        private void ApplyPlacementSpecificMetadata(GameObject instance, VoiceIntentCommand command)
        {
            if (instance == null || command == null)
                return;

            if (!IsTeleportPadName(command.object_name) && !IsTeleportTarget(instance))
                return;

            if (IsTeleportPadName(command.object_name) || instance.name == "Teleport Anchor" || string.IsNullOrWhiteSpace(instance.name))
                instance.name = NextUniqueSceneName("Teleport Pad");

            SpeechIntentTrackable trackable = instance.GetComponent<SpeechIntentTrackable>();
            if (trackable == null)
                trackable = instance.AddComponent<SpeechIntentTrackable>();

            trackable.canonicalName = "teleport pad";
            AddAlias(trackable, "teleport");
            AddAlias(trackable, "teleports");
            AddAlias(trackable, "teleporter");
            AddAlias(trackable, "teleporters");
            AddAlias(trackable, "teleport pad");
            AddAlias(trackable, "teleport pads");
            AddAlias(trackable, "teleport anchor");
            AddAlias(trackable, "teleport anchors");
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

        private static string NormalizeObjectName(string objectName)
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

        public bool TryResolvePlacementPose(VoiceIntentCommand command, SpatialSnapshot spatial, out Vector3 position, out Quaternion rotation)
        {
            LastFailureMessage = "";
            position = Vector3.zero;
            rotation = Quaternion.identity;

            if (command != null &&
                string.Equals(command.placement_mode, "under_foot", StringComparison.OrdinalIgnoreCase))
            {
                ResolveUnderFootPlacementPose(spatial, out position, out rotation);
                return true;
            }

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

            if (command.spatial_reference == SpatialReferenceMode.RelativeToMe)
            {
                ResolveRelativePlacementPose(command, spatial, out position, out rotation);
                return true;
            }

            if (command.spatial_reference == SpatialReferenceMode.RelativeToTarget)
            {
                return TryResolveTargetRelativePlacementPose(command, spatial, out position, out rotation);
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

            if (command.intent == VoiceIntentType.PlaceObject && command.spatial_reference == SpatialReferenceMode.None)
            {
                ResolveDefaultCreationPose(spatial, out position, out rotation);
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

        public bool CanPlaceLocally(string objectName)
        {
            return FindPrefab(objectName) != null || CanCreatePrimitive(objectName);
        }

        private bool CanCreatePrimitive(string objectName)
        {
            return TryResolvePrimitiveType(objectName, out _, out _);
        }

        private void ResolveRelativePlacementPose(VoiceIntentCommand command, SpatialSnapshot spatial, out Vector3 position, out Quaternion rotation)
        {
            Vector3 headPosition = spatial != null && spatial.head_position != Vector3.zero
                ? spatial.head_position
                : ResolveCameraPosition();
            Vector3 forward = spatial != null && spatial.head_forward.sqrMagnitude > 0.0001f
                ? Vector3.ProjectOnPlane(spatial.head_forward, Vector3.up).normalized
                : ResolveCameraForward();
            if (forward.sqrMagnitude <= 0.0001f)
                forward = Vector3.forward;

            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            float fallbackDistance = command.intent == VoiceIntentType.PlaceObject ? defaultCreationDistance : defaultDistance;
            float distance = command.relative_distance_meters > 0f ? command.relative_distance_meters : fallbackDistance;
            Vector3 direction = command.relative_direction switch
            {
                RelativeDirection.Behind or RelativeDirection.Back => -forward,
                RelativeDirection.Left => -right,
                RelativeDirection.Right => right,
                RelativeDirection.Up => Vector3.up,
                RelativeDirection.Down => Vector3.down,
                _ => forward
            };

            position = headPosition + direction * distance;
            rotation = Quaternion.LookRotation(forward, Vector3.up);
        }

        private void ResolveDefaultCreationPose(SpatialSnapshot spatial, out Vector3 position, out Quaternion rotation)
        {
            Vector3 headPosition = spatial != null && spatial.head_position != Vector3.zero
                ? spatial.head_position
                : ResolveCameraPosition();
            Vector3 forward = spatial != null && spatial.head_forward.sqrMagnitude > 0.0001f
                ? Vector3.ProjectOnPlane(spatial.head_forward, Vector3.up).normalized
                : ResolveCameraForward();
            if (forward.sqrMagnitude <= 0.0001f)
                forward = Vector3.forward;

            position = headPosition + forward * defaultCreationDistance;
            rotation = Quaternion.LookRotation(forward, Vector3.up);
        }

        private bool TryResolveTargetRelativePlacementPose(VoiceIntentCommand command, SpatialSnapshot spatial, out Vector3 position, out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;

            GameObject target = ResolvePlacementTarget(command, spatial);
            if (target == null)
            {
                LastFailureMessage = string.IsNullOrWhiteSpace(command.target_name)
                    ? "Which object?"
                    : $"No matching {command.target_name} found.";
                return false;
            }

            Vector3 forward;
            Vector3 right;
            bool targetLocal = string.Equals(command.placement_mode, "target_local", StringComparison.OrdinalIgnoreCase);
            if (targetLocal)
            {
                forward = Vector3.ProjectOnPlane(target.transform.forward, Vector3.up).normalized;
                if (forward.sqrMagnitude <= 0.0001f)
                    forward = Vector3.forward;
                right = Vector3.ProjectOnPlane(target.transform.right, Vector3.up).normalized;
                if (right.sqrMagnitude <= 0.0001f)
                    right = Vector3.Cross(Vector3.up, forward).normalized;
            }
            else
            {
                forward = spatial != null && spatial.head_forward.sqrMagnitude > 0.0001f
                    ? Vector3.ProjectOnPlane(spatial.head_forward, Vector3.up).normalized
                    : ResolveCameraForward();
                if (forward.sqrMagnitude <= 0.0001f)
                    forward = Vector3.forward;
                right = Vector3.Cross(Vector3.up, forward).normalized;
            }

            float distance = command.relative_distance_meters > 0f ? command.relative_distance_meters : defaultTargetRelativeDistance;
            Vector3 direction = command.relative_direction switch
            {
                RelativeDirection.Behind or RelativeDirection.Back => -forward,
                RelativeDirection.Left => -right,
                RelativeDirection.Right => right,
                RelativeDirection.Up => Vector3.up,
                RelativeDirection.Down => Vector3.down,
                _ => forward
            };

            position = target.transform.position + direction * distance;
            rotation = Quaternion.LookRotation(forward, Vector3.up);
            return true;
        }

        private GameObject ResolvePlacementTarget(VoiceIntentCommand command, SpatialSnapshot spatial)
        {
            if (command == null)
                return null;

            if (entityResolver == null)
                entityResolver = FindFirstObjectByType<SceneEntityResolver>(FindObjectsInactive.Include);

            if (entityResolver != null)
            {
                var targetCommand = new VoiceIntentCommand
                {
                    target_reference = TargetReferenceMode.NamedObject,
                    target_name = command.target_name,
                    object_name = command.target_name,
                    target_material_prompt = command.target_material_prompt,
                    target_hand = command.target_hand
                };
                return entityResolver.ResolveTarget(targetCommand, spatial);
            }

            return GameObject.Find(command.target_name);
        }

        private void ResolveUnderFootPlacementPose(SpatialSnapshot spatial, out Vector3 position, out Quaternion rotation)
        {
            Vector3 footPosition = ResolvePlayerRootPosition();
            Vector3 headPosition = spatial != null && spatial.head_position != Vector3.zero
                ? spatial.head_position
                : ResolveCameraPosition();

            if (footPosition == Vector3.zero && headPosition != Vector3.zero)
                footPosition = new Vector3(headPosition.x, 0f, headPosition.z);

            Vector3 rayOrigin = headPosition != Vector3.zero
                ? headPosition
                : footPosition + Vector3.up * footPlacementRaycastHeight;
            if (rayOrigin.y < footPosition.y + 0.25f)
                rayOrigin = footPosition + Vector3.up * footPlacementRaycastHeight;

            if (TryRaycastFloor(rayOrigin, out RaycastHit hit))
                position = hit.point + hit.normal * surfaceOffset;
            else
                position = footPosition;

            Vector3 forward = spatial != null && spatial.head_forward.sqrMagnitude > 0.0001f
                ? Vector3.ProjectOnPlane(spatial.head_forward, Vector3.up).normalized
                : ResolveCameraForward();
            if (forward.sqrMagnitude <= 0.0001f)
                forward = Vector3.forward;

            rotation = Quaternion.LookRotation(forward, Vector3.up);
        }

        private bool TryRaycastFloor(Vector3 rayOrigin, out RaycastHit hit)
        {
            Transform playerRoot = ResolvePlayerRoot();
            RaycastHit[] hits = Physics.RaycastAll(
                rayOrigin,
                Vector3.down,
                footPlacementRaycastDistance,
                footPlacementLayers,
                QueryTriggerInteraction.Ignore);

            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (RaycastHit candidate in hits)
            {
                if (candidate.collider == null)
                    continue;

                if (playerRoot != null && candidate.collider.transform.IsChildOf(playerRoot))
                    continue;

                hit = candidate;
                return true;
            }

            hit = default;
            return false;
        }

        private static Vector3 ResolvePlayerRootPosition()
        {
            Transform playerRoot = ResolvePlayerRoot();
            if (playerRoot != null)
                return playerRoot.position;

            return Vector3.zero;
        }

        private static Transform ResolvePlayerRoot()
        {
            GameObject me = GameObject.Find("Me");
            if (me != null)
                return me.transform;

            GameObject xrOrigin = GameObject.Find("XR Origin");
            if (xrOrigin != null)
                return xrOrigin.transform;

            return null;
        }

        private static Vector3 ResolveCameraPosition()
        {
            if (Camera.main != null)
                return Camera.main.transform.position;

            GameObject mainCamera = GameObject.Find("Main Camera");
            return mainCamera != null ? mainCamera.transform.position : Vector3.zero;
        }

        private static Vector3 ResolveCameraForward()
        {
            if (Camera.main != null)
                return Vector3.ProjectOnPlane(Camera.main.transform.forward, Vector3.up).normalized;

            GameObject mainCamera = GameObject.Find("Main Camera");
            return mainCamera != null
                ? Vector3.ProjectOnPlane(mainCamera.transform.forward, Vector3.up).normalized
                : Vector3.forward;
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

        private static bool IsTeleportTarget(GameObject instance)
        {
            if (instance == null)
                return false;

            Component[] components = instance.GetComponentsInChildren<Component>(true);
            foreach (Component component in components)
            {
                if (component == null)
                    continue;

                Type type = component.GetType();
                string name = type.Name;
                string fullName = type.FullName ?? "";
                if (name == "TeleportationAnchor" ||
                    name == "TeleportationArea" ||
                    fullName.EndsWith(".TeleportationAnchor", StringComparison.Ordinal) ||
                    fullName.EndsWith(".TeleportationArea", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsTeleportPadName(string objectName)
        {
            string normalized = NormalizeObjectName(objectName);
            return normalized == "teleport" ||
                   normalized == "teleporter" ||
                   normalized == "teleport pad" ||
                   normalized == "teleportpad" ||
                   normalized == "teleport anchor" ||
                   normalized == "teleportanchor";
        }

        private static void AddAlias(SpeechIntentTrackable trackable, string alias)
        {
            if (trackable == null || string.IsNullOrWhiteSpace(alias))
                return;

            if (trackable.aliases == null)
                trackable.aliases = new List<string>();

            foreach (string existing in trackable.aliases)
            {
                if (string.Equals(existing, alias, StringComparison.OrdinalIgnoreCase))
                    return;
            }

            trackable.aliases.Add(alias);
        }

        private static string NextUniqueSceneName(string baseName)
        {
            if (string.IsNullOrWhiteSpace(baseName))
                baseName = "Object";

            HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Transform[] transforms = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (Transform transform in transforms)
            {
                if (transform != null)
                    names.Add(transform.name);
            }

            for (int i = 1; i < 100000; i++)
            {
                string candidate = $"{baseName} {i}";
                if (!names.Contains(candidate))
                    return candidate;
            }

            return $"{baseName} {Guid.NewGuid():N}".Substring(0, Math.Min(baseName.Length + 9, baseName.Length + 33));
        }
    }
}
