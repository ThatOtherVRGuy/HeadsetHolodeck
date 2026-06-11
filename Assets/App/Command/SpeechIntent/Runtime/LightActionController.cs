using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpeechIntent
{
    public class LightActionController : MonoBehaviour
    {
        [Header("Scene")]
        public Transform defaultParent;
        public Transform headTransform;
        public SceneEntityResolver entityResolver;
        public InteractionMemory interactionMemory;
        public RuntimeMaterialCatalog materialCatalog;
        public LightRigController lightRig;

        [Header("Defaults")]
        public float defaultForwardDistance = 1f;
        public float defaultHeadHeightOffset = 1f;
        public float defaultPointIntensity = 2.5f;
        public float defaultSpotIntensity = 3f;
        public float defaultDirectionalIntensity = 1f;
        public float defaultRange = 8f;
        public float defaultSpotAngle = 45f;
        public float flashlightSpotAngle = 22f;
        public float proxyScale = 0.12f;
        public bool createVisibleProxy = false;
        public Color defaultLightColor = Color.white;

        public string LastFailureMessage { get; private set; } = "";

        void Awake()
        {
            ResolveReferences();
        }

        void OnEnable()
        {
            RuntimeLightIdentity.RuntimeSunDestroyed += HandleRuntimeSunDestroyed;
        }

        void OnDisable()
        {
            RuntimeLightIdentity.RuntimeSunDestroyed -= HandleRuntimeSunDestroyed;
        }

        public GameObject CreateLight(VoiceIntentCommand command, SpatialSnapshot spatial)
        {
            LastFailureMessage = "";
            ResolveReferences();

            RuntimeLightKind kind = ParseLightKind(command);
            bool flashlight = IsFlashlight(command);
            if (flashlight)
                kind = RuntimeLightKind.Spot;

            Color color = ResolveLightColor(command, defaultLightColor);

            if (kind == RuntimeLightKind.Ambient)
            {
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = color;
                if (command != null && command.light_intensity > 0f)
                    RenderSettings.ambientIntensity = command.light_intensity;
                return null;
            }

            if (!TryResolveLightPose(command, spatial, kind, flashlight, out Vector3 position, out Quaternion rotation))
            {
                if (string.IsNullOrWhiteSpace(LastFailureMessage))
                    LastFailureMessage = flashlight ? "Which hand?" : "Where?";
                return null;
            }

            GameObject go = createVisibleProxy
                ? GameObject.CreatePrimitive(PrimitiveType.Sphere)
                : new GameObject();
            go.name = BuildLightName(kind, color, flashlight);
            go.transform.SetParent(ResolveParent(), true);
            go.transform.SetPositionAndRotation(position, rotation);
            go.transform.localScale = Vector3.one * proxyScale;
            EnsureSelectionProxy(go);

            Light light = go.AddComponent<Light>();
            light.type = ToUnityLightType(kind);
            ApplyLightColor(light, color);
            light.intensity = ResolveIntensity(command, kind);
            light.shadows = LightShadows.Soft;
            light.range = command != null && command.light_range > 0f ? command.light_range : defaultRange;
            if (kind == RuntimeLightKind.Spot)
                light.spotAngle = command != null && command.light_spot_angle > 0f
                    ? command.light_spot_angle
                    : (flashlight ? flashlightSpotAngle : defaultSpotAngle);

            ApplyProxyMaterial(go, color);
            RuntimeLightIdentity identity = go.AddComponent<RuntimeLightIdentity>();
            identity.kind = kind;
            identity.isFlashlight = flashlight;
            RuntimeProxyVisual proxy = go.AddComponent<RuntimeProxyVisual>();
            proxy.visibleByDefault = createVisibleProxy;
            proxy.SetVisible(createVisibleProxy);
            proxy.RebuildPrimitive(RuntimeProxyCategory.Light, color, kind);

            SpeechIntentTrackable trackable = go.GetComponent<SpeechIntentTrackable>() ?? go.AddComponent<SpeechIntentTrackable>();
            trackable.canonicalName = flashlight ? "flashlight" : KindDisplayName(kind);
            AddAlias(trackable, "light");
            AddAlias(trackable, KindDisplayName(kind));
            AddAlias(trackable, $"{KindDisplayName(kind)} light");
            if (flashlight)
            {
                AddAlias(trackable, "spot light");
                AddAlias(trackable, "spotlight");
            }

            if (kind == RuntimeLightKind.Directional && FindRuntimeSun() == null)
                AssignSun(identity);

            if (interactionMemory != null)
                interactionMemory.RegisterCreatedObject(go);

            return go;
        }

        public bool TryModifyLight(VoiceIntentCommand command, SpatialSnapshot spatial, out List<GameObject> targets)
        {
            LastFailureMessage = "";
            targets = ResolveLightTargets(command, spatial);
            if (targets.Count == 0)
            {
                LastFailureMessage = "No matching light found.";
                return false;
            }

            string action = (command?.light_action ?? "").Trim().ToLowerInvariant();
            string colorPrompt = FirstNonEmpty(command?.light_color_prompt, command?.material_prompt);
            bool changed = false;

            foreach (GameObject target in targets)
            {
                if (target == null)
                    continue;

                RuntimeLightIdentity identity = target.GetComponent<RuntimeLightIdentity>();
                Light light = target.GetComponent<Light>();

                if (action == "set_sun")
                {
                    if (identity == null || identity.kind != RuntimeLightKind.Directional)
                        continue;
                    AssignSun(identity);
                    changed = true;
                    continue;
                }

                if (light == null)
                    continue;

                if (IsRelativeColorAction(action))
                {
                    ApplyLightColor(light, AdjustColor(light.color, action));
                    ApplyProxyMaterial(target, light.color);
                    UpdateProxyVisual(target, light.color);
                    changed = true;
                }
                else if (!string.IsNullOrWhiteSpace(colorPrompt))
                {
                    ApplyLightColor(light, ResolveColor(colorPrompt, light.color));
                    ApplyProxyMaterial(target, light.color);
                    UpdateProxyVisual(target, light.color);
                    changed = true;
                }

                if (action == "brighter")
                {
                    light.intensity *= 1.25f;
                    changed = true;
                }
                else if (action == "dimmer" || action == "softer")
                {
                    light.intensity *= 0.8f;
                    changed = true;
                }

                if (command != null && command.light_intensity > 0f)
                {
                    light.intensity = command.light_intensity;
                    changed = true;
                }

                if (command != null && command.light_range > 0f)
                {
                    light.range = command.light_range;
                    changed = true;
                }

                if (command != null && command.light_spot_angle > 0f && light.type == LightType.Spot)
                {
                    light.spotAngle = command.light_spot_angle;
                    changed = true;
                }
            }

            if (!changed)
            {
                LastFailureMessage = "What light parameter should I change?";
                return false;
            }

            if (interactionMemory != null && targets.Count == 1)
                interactionMemory.RegisterInteraction(targets[0]);

            return true;
        }

        public void NotifyDeleting(GameObject target)
        {
            RuntimeLightIdentity identity = target != null ? target.GetComponent<RuntimeLightIdentity>() : null;
            if (identity == null || !identity.isSun)
                return;

            RuntimeLightIdentity replacement = FindFirstDirectionalExcept(identity);
            if (replacement == null)
                return;

            string message = "The sun was removed. Should another directional light become the sun?";
            Debug.Log("[LightActionController] " + message);
            ArchStatusBus.Warning(message, "LIGHT");
            identity.isSun = false;
        }

        private bool TryResolveLightPose(VoiceIntentCommand command, SpatialSnapshot spatial, RuntimeLightKind kind, bool flashlight, out Vector3 position, out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);

            if (flashlight)
            {
                if (command != null && command.spatial_reference == SpatialReferenceMode.BodyAnchor &&
                    BodyAnchorResolver.TryResolve(spatial, command.body_anchor, command.target_hand, out position, out rotation))
                {
                    return true;
                }

                if (TryResolvePointingHand(spatial, command != null ? command.target_hand : HandSelection.None, out HandRaySnapshot hand))
                {
                    position = hand.origin;
                    rotation = Quaternion.LookRotation(SafeDirection(hand.direction, Vector3.forward), Vector3.up);
                    return true;
                }

                LastFailureMessage = "Which hand?";
                return false;
            }

            if (command != null && command.spatial_reference == SpatialReferenceMode.BodyAnchor)
            {
                if (BodyAnchorResolver.TryResolve(spatial, command.body_anchor, command.target_hand, out position, out rotation))
                    return true;

                LastFailureMessage = "Which part of you should I use?";
                return false;
            }

            bool explicitHere = command != null &&
                                (command.spatial_reference == SpatialReferenceMode.PointingHit ||
                                 command.spatial_reference == SpatialReferenceMode.PointingRay);
            if (explicitHere)
            {
                if (!TryResolvePointingHand(spatial, command.target_hand, out HandRaySnapshot hand))
                {
                    LastFailureMessage = "Where?";
                    return false;
                }

                Vector3 direction = SafeDirection(hand.direction, Vector3.forward);
                position = hand.has_hit ? hand.hit_point + hand.hit_normal * 0.02f : hand.origin + direction * defaultForwardDistance;
                rotation = kind == RuntimeLightKind.Point
                    ? Quaternion.identity
                    : Quaternion.LookRotation(direction, Vector3.up);
                return true;
            }

            Transform head = ResolveHead(spatial);
            if (head != null)
            {
                position = head.position + head.forward.normalized * defaultForwardDistance + Vector3.up * defaultHeadHeightOffset;
                rotation = kind == RuntimeLightKind.Point
                    ? Quaternion.identity
                    : Quaternion.LookRotation(Vector3.down, Vector3.forward);
                return true;
            }

            if (spatial != null && spatial.head_forward.sqrMagnitude > 0.0001f)
            {
                position = spatial.head_position + spatial.head_forward.normalized * defaultForwardDistance + Vector3.up * defaultHeadHeightOffset;
                rotation = kind == RuntimeLightKind.Point
                    ? Quaternion.identity
                    : Quaternion.LookRotation(Vector3.down, Vector3.forward);
                return true;
            }

            LastFailureMessage = "I could not find your head camera.";
            return false;
        }

        private List<GameObject> ResolveLightTargets(VoiceIntentCommand command, SpatialSnapshot spatial)
        {
            var results = new List<GameObject>();
            if (command == null)
                return results;

            string label = FirstNonEmpty(command.target_name, command.object_name, command.target_entity);
            bool all = command.target_reference == TargetReferenceMode.All || IsAll(label);
            bool sun = IsSunLabel(label);

            if (sun && !all)
            {
                RuntimeLightIdentity sunIdentity = FindRuntimeSun();
                if (sunIdentity != null)
                {
                    results.Add(sunIdentity.gameObject);
                    return results;
                }

                RuntimeLightIdentity directional = FindFirstDirectionalExcept(null);
                if (directional != null)
                    LastFailureMessage = "No sun is assigned. Should another directional light become the sun?";
                return results;
            }

            if (command.target_reference == TargetReferenceMode.PointedObject && entityResolver != null)
            {
                GameObject pointed = entityResolver.ResolveTarget(TargetReferenceMode.PointedObject, "", spatial, command.target_hand);
                RuntimeLightIdentity identity = pointed != null ? pointed.GetComponentInParent<RuntimeLightIdentity>() : null;
                if (identity != null)
                    results.Add(identity.gameObject);
                return results;
            }

            RuntimeLightIdentity[] identities = FindObjectsByType<RuntimeLightIdentity>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (RuntimeLightIdentity identity in identities)
            {
                if (identity == null || identity.gameObject == null || !identity.isRuntimeLight)
                    continue;

                if (!all && !MatchesLight(identity, label))
                    continue;

                results.Add(identity.gameObject);
            }

            return results;
        }

        private bool MatchesLight(RuntimeLightIdentity identity, string label)
        {
            if (identity == null)
                return false;

            string normalized = Normalize(label);
            if (string.IsNullOrWhiteSpace(normalized) || normalized == "light")
                return true;

            if (identity.isSun && normalized == "sun")
                return true;

            if (identity.isFlashlight && normalized == "flashlight")
                return true;

            string kind = Normalize(KindDisplayName(identity.kind));
            return normalized == kind ||
                   normalized == kind + " light" ||
                   normalized.Contains(kind, StringComparison.Ordinal) ||
                   normalized.Contains("light", StringComparison.Ordinal);
        }

        private void AssignSun(RuntimeLightIdentity identity)
        {
            if (identity == null || identity.kind != RuntimeLightKind.Directional)
                return;

            RuntimeLightIdentity[] identities = FindObjectsByType<RuntimeLightIdentity>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (RuntimeLightIdentity candidate in identities)
                if (candidate != null)
                    candidate.isSun = false;

            identity.isSun = true;
            SpeechIntentTrackable trackable = identity.GetComponent<SpeechIntentTrackable>() ?? identity.gameObject.AddComponent<SpeechIntentTrackable>();
            AddAlias(trackable, "sun");
            trackable.canonicalName = "sun";

            if (lightRig != null)
                lightRig.sunLight = identity.GetComponent<Light>();
        }

        private RuntimeLightIdentity FindRuntimeSun()
        {
            RuntimeLightIdentity[] identities = FindObjectsByType<RuntimeLightIdentity>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (RuntimeLightIdentity identity in identities)
                if (identity != null && identity.isSun && identity.kind == RuntimeLightKind.Directional)
                    return identity;
            return null;
        }

        private RuntimeLightIdentity FindFirstDirectionalExcept(RuntimeLightIdentity except)
        {
            RuntimeLightIdentity[] identities = FindObjectsByType<RuntimeLightIdentity>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (RuntimeLightIdentity identity in identities)
            {
                if (identity == null || identity == except || identity.kind != RuntimeLightKind.Directional)
                    continue;
                return identity;
            }
            return null;
        }

        private void HandleRuntimeSunDestroyed(RuntimeLightIdentity destroyed)
        {
            RuntimeLightIdentity replacement = FindFirstDirectionalExcept(destroyed);
            if (replacement == null)
                return;

            string message = "The sun was removed. Should another directional light become the sun?";
            Debug.Log("[LightActionController] " + message);
            ArchStatusBus.Warning(message, "LIGHT");
        }

        private RuntimeLightKind ParseLightKind(VoiceIntentCommand command)
        {
            string value = Normalize(FirstNonEmpty(command?.light_type, command?.object_name, command?.target_entity, command?.transcript));
            if (value.Contains("ambient", StringComparison.Ordinal))
                return RuntimeLightKind.Ambient;
            if (value.Contains("directional", StringComparison.Ordinal) || value == "sun")
                return RuntimeLightKind.Directional;
            if (value.Contains("spot", StringComparison.Ordinal) || value.Contains("flashlight", StringComparison.Ordinal))
                return RuntimeLightKind.Spot;
            return RuntimeLightKind.Point;
        }

        private bool IsFlashlight(VoiceIntentCommand command)
        {
            string value = Normalize(FirstNonEmpty(command?.light_type, command?.object_name, command?.target_entity, command?.transcript));
            return value.Contains("flashlight", StringComparison.Ordinal) || value.Contains("torch", StringComparison.Ordinal);
        }

        private LightType ToUnityLightType(RuntimeLightKind kind)
        {
            return kind switch
            {
                RuntimeLightKind.Spot => LightType.Spot,
                RuntimeLightKind.Directional => LightType.Directional,
                _ => LightType.Point
            };
        }

        private float ResolveIntensity(VoiceIntentCommand command, RuntimeLightKind kind)
        {
            if (command != null && command.light_intensity > 0f)
                return command.light_intensity;

            return kind switch
            {
                RuntimeLightKind.Spot => defaultSpotIntensity,
                RuntimeLightKind.Directional => defaultDirectionalIntensity,
                _ => defaultPointIntensity
            };
        }

        private Color ResolveLightColor(VoiceIntentCommand command, Color fallback)
        {
            return ResolveColor(
                FirstNonEmpty(
                    command?.light_color_prompt,
                    command?.material_prompt,
                    command?.object_name,
                    command?.target_entity,
                    command?.transcript),
                fallback);
        }

        private static void ApplyLightColor(Light light, Color color)
        {
            if (light == null)
                return;

            color.a = 1f;
            light.useColorTemperature = false;
            light.color = color;
        }

        private Color ResolveColor(string prompt, Color fallback)
        {
            if (RuntimeMaterialCatalog.TryParseDescriptor(prompt, RuntimeMaterialDescriptor.Default, out RuntimeMaterialDescriptor descriptor))
                return descriptor.color;

            string normalized = Normalize(prompt);
            if (normalized.Contains("warm", StringComparison.Ordinal))
                return new Color(1f, 0.82f, 0.55f, 1f);
            if (normalized.Contains("cool", StringComparison.Ordinal))
                return new Color(0.65f, 0.78f, 1f, 1f);

            return fallback;
        }

        private static Color AdjustColor(Color color, string action)
        {
            color.a = 1f;
            switch (action)
            {
                case "redder":
                    color.r = Mathf.Clamp01(color.r + 0.18f);
                    color.g = Mathf.Clamp01(color.g - 0.06f);
                    color.b = Mathf.Clamp01(color.b - 0.06f);
                    break;
                case "greener":
                    color.g = Mathf.Clamp01(color.g + 0.18f);
                    color.r = Mathf.Clamp01(color.r - 0.04f);
                    color.b = Mathf.Clamp01(color.b - 0.04f);
                    break;
                case "bluer":
                    color.b = Mathf.Clamp01(color.b + 0.18f);
                    color.r = Mathf.Clamp01(color.r - 0.04f);
                    color.g = Mathf.Clamp01(color.g - 0.04f);
                    break;
                case "warmer":
                    color.r = Mathf.Clamp01(color.r + 0.14f);
                    color.g = Mathf.Clamp01(color.g + 0.06f);
                    color.b = Mathf.Clamp01(color.b - 0.1f);
                    break;
                case "cooler":
                    color.b = Mathf.Clamp01(color.b + 0.14f);
                    color.g = Mathf.Clamp01(color.g + 0.04f);
                    color.r = Mathf.Clamp01(color.r - 0.08f);
                    break;
            }
            return color;
        }

        private static bool IsRelativeColorAction(string action)
        {
            return action == "redder" ||
                   action == "greener" ||
                   action == "bluer" ||
                   action == "warmer" ||
                   action == "cooler";
        }

        private bool TryResolvePointingHand(SpatialSnapshot spatial, HandSelection handSelection, out HandRaySnapshot hand)
        {
            hand = null;
            if (spatial == null)
                return false;

            if (handSelection == HandSelection.Left && spatial.left_hand != null && spatial.left_hand.is_available)
            {
                hand = spatial.left_hand;
                return true;
            }

            if (handSelection == HandSelection.Right && spatial.right_hand != null && spatial.right_hand.is_available)
            {
                hand = spatial.right_hand;
                return true;
            }

            if (spatial.right_hand != null && spatial.right_hand.is_available && spatial.right_hand.is_pointing)
            {
                hand = spatial.right_hand;
                return true;
            }

            if (spatial.left_hand != null && spatial.left_hand.is_available && spatial.left_hand.is_pointing)
            {
                hand = spatial.left_hand;
                return true;
            }

            return false;
        }

        private Transform ResolveHead(SpatialSnapshot spatial)
        {
            if (headTransform != null)
                return headTransform;

            Camera camera = Camera.main;
            return camera != null ? camera.transform : null;
        }

        private Transform ResolveParent()
        {
            if (defaultParent != null)
                return defaultParent;

            GameObject root = GameObject.Find("GeneratedWorldRoot") ?? GameObject.Find("RuntimeLights");
            if (root == null)
                root = new GameObject("RuntimeLights");
            defaultParent = root.transform;
            return defaultParent;
        }

        private void ResolveReferences()
        {
            if (entityResolver == null)
                entityResolver = FindFirstObjectByType<SceneEntityResolver>(FindObjectsInactive.Include);
            if (interactionMemory == null)
                interactionMemory = FindFirstObjectByType<InteractionMemory>(FindObjectsInactive.Include);
            if (materialCatalog == null)
                materialCatalog = FindFirstObjectByType<RuntimeMaterialCatalog>(FindObjectsInactive.Include);
            if (lightRig == null)
                lightRig = FindFirstObjectByType<LightRigController>(FindObjectsInactive.Include);
            if (headTransform == null && Camera.main != null)
                headTransform = Camera.main.transform;
        }

        private void ApplyProxyMaterial(GameObject target, Color color)
        {
            if (target == null || !createVisibleProxy)
                return;

            if (materialCatalog != null)
            {
                RuntimeMaterialDescriptor descriptor = new RuntimeMaterialDescriptor
                {
                    name = "light " + ColorUtility.ToHtmlStringRGB(color).ToLowerInvariant(),
                    color = color,
                    metallic = 0f,
                    smoothness = 0.65f
                };
                materialCatalog.ApplyTo(target, descriptor, false);
                return;
            }

            Renderer renderer = target.GetComponent<Renderer>();
            if (renderer != null)
                renderer.sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Simple Lit") ?? Shader.Find("Standard")) { color = color };
        }

        private static void UpdateProxyVisual(GameObject target, Color color)
        {
            RuntimeProxyVisual proxy = target != null ? target.GetComponent<RuntimeProxyVisual>() : null;
            RuntimeLightIdentity identity = target != null ? target.GetComponent<RuntimeLightIdentity>() : null;
            if (proxy == null)
                return;

            proxy.RebuildPrimitive(RuntimeProxyCategory.Light, color, identity != null ? identity.kind : RuntimeLightKind.Point);
        }

        private void EnsureSelectionProxy(GameObject target)
        {
            if (target == null)
                return;

            Collider collider = target.GetComponent<Collider>();
            if (collider == null)
            {
                SphereCollider sphere = target.AddComponent<SphereCollider>();
                sphere.radius = 0.5f;
                sphere.isTrigger = true;
            }
            else
            {
                collider.isTrigger = true;
            }

            Rigidbody body = target.GetComponent<Rigidbody>();
            if (body != null)
            {
                Destroy(body);
            }
        }

        private static string BuildLightName(RuntimeLightKind kind, Color color, bool flashlight)
        {
            if (flashlight)
                return "Runtime_Flashlight";
            return $"Runtime_{KindDisplayName(kind).Replace(" ", "")}_{ColorUtility.ToHtmlStringRGB(color)}";
        }

        private static string KindDisplayName(RuntimeLightKind kind)
        {
            return kind switch
            {
                RuntimeLightKind.Spot => "spotlight",
                RuntimeLightKind.Directional => "directional light",
                RuntimeLightKind.Ambient => "ambient light",
                _ => "point light"
            };
        }

        private static void AddAlias(SpeechIntentTrackable trackable, string alias)
        {
            if (trackable == null || string.IsNullOrWhiteSpace(alias))
                return;
            if (!trackable.aliases.Exists(existing => string.Equals(existing, alias, StringComparison.OrdinalIgnoreCase)))
                trackable.aliases.Add(alias);
        }

        private static Vector3 SafeDirection(Vector3 direction, Vector3 fallback)
        {
            return direction.sqrMagnitude > 0.0001f ? direction.normalized : fallback.normalized;
        }

        private static bool IsAll(string value)
        {
            string normalized = Normalize(value);
            return normalized == "all" || normalized == "all light" || normalized == "every light" || normalized == "lights";
        }

        private static bool IsSunLabel(string value)
        {
            return Normalize(value) == "sun";
        }

        private static string Normalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";

            string normalized = value.Trim().ToLowerInvariant();
            string[] prefixes = { "all of the ", "all the ", "all ", "every ", "the ", "a ", "an " };
            foreach (string prefix in prefixes)
            {
                if (normalized.StartsWith(prefix, StringComparison.Ordinal))
                {
                    normalized = normalized.Substring(prefix.Length).Trim();
                    break;
                }
            }

            normalized = normalized.Replace("_", " ").Replace("-", " ");
            if (normalized.EndsWith("s", StringComparison.Ordinal) && normalized.Length > 3)
                normalized = normalized.Substring(0, normalized.Length - 1);
            return normalized;
        }

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (string value in values)
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            return "";
        }
    }
}
