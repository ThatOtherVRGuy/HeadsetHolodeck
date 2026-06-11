using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SpeechIntent
{
    public enum SceneTargetResolutionStatus
    {
        None = 0,
        Single = 1,
        Ambiguous = 2,
        All = 3
    }

    public class SceneTargetResolution
    {
        public SceneTargetResolutionStatus status = SceneTargetResolutionStatus.None;
        public List<GameObject> targets = new List<GameObject>();
        public string message = "";

        public GameObject Target => targets.Count > 0 ? targets[0] : null;

        public static SceneTargetResolution None(string message)
        {
            return new SceneTargetResolution { status = SceneTargetResolutionStatus.None, message = message };
        }

        public static SceneTargetResolution FromTargets(List<GameObject> targets, string label, bool allowMultiple)
        {
            targets = targets ?? new List<GameObject>();
            if (targets.Count == 0)
                return None(string.IsNullOrWhiteSpace(label) ? "No matching target found." : $"No matching {label} found.");

            if (targets.Count == 1)
                return new SceneTargetResolution { status = SceneTargetResolutionStatus.Single, targets = targets };

            return new SceneTargetResolution
            {
                status = allowMultiple ? SceneTargetResolutionStatus.All : SceneTargetResolutionStatus.Ambiguous,
                targets = targets,
                message = allowMultiple ? "" : $"Which {label}?"
            };
        }
    }

    public class SceneEntityResolver : MonoBehaviour
    {
        public InteractionMemory interactionMemory;
        public RuntimeMaterialCatalog materialCatalog;

        [Tooltip("Optional search root. If null, searches the full active scene.")]
        public Transform searchRoot;

        [Header("Indicated Target Fallback")]
        public LayerMask gazeRaycastMask = ~0;
        [Range(0.1f, 100f)] public float gazeMaxDistance = 25f;
        public QueryTriggerInteraction gazeTriggerInteraction = QueryTriggerInteraction.Ignore;

        public GameObject ResolveTarget(VoiceIntentCommand command, SpatialSnapshot spatial)
        {
            SceneTargetResolution resolution = ResolveTargetDetailed(command, spatial);
            return resolution.status == SceneTargetResolutionStatus.Single ||
                   resolution.status == SceneTargetResolutionStatus.All
                ? resolution.Target
                : null;
        }

        public SceneTargetResolution ResolveTargetDetailed(VoiceIntentCommand command, SpatialSnapshot spatial)
        {
            if (command == null)
                return SceneTargetResolution.None("No command.");

            bool all = command.target_reference == TargetReferenceMode.All || IsAllToken(command.target_name);
            string targetLabel = FirstNonEmpty(command.target_name, command.object_name, command.target_entity);
            string materialPrompt = command.target_material_prompt;
            string spatialQualifier = NormalizeSpatialQualifier(command.target_spatial_qualifier);
            ExtractMaterialQualifierForCommand(ref targetLabel, ref materialPrompt);
            string displayLabel = BuildDisplayLabel(materialPrompt, targetLabel, spatialQualifier);

            if (command.target_reference == TargetReferenceMode.PointedObject)
            {
                GameObject pointed = ResolvePointedObject(spatial, command.target_hand);
                if (pointed == null)
                    return SceneTargetResolution.None("Which object?");

                pointed = NormalizeTrackableRoot(pointed);
                if (!MatchesNameAndMaterial(pointed, targetLabel, materialPrompt))
                {
                    string mismatch = string.IsNullOrWhiteSpace(displayLabel)
                        ? "That object does not match."
                        : $"That is not a {displayLabel}.";
                    return SceneTargetResolution.None(mismatch);
                }

                if (!MatchesSpatialQualifier(pointed, targetLabel, materialPrompt, spatialQualifier))
                {
                    string mismatch = string.IsNullOrWhiteSpace(displayLabel)
                        ? "That object does not match."
                        : $"That is not the {displayLabel}.";
                    return SceneTargetResolution.None(mismatch);
                }

                return SceneTargetResolution.FromTargets(new List<GameObject> { pointed }, displayLabel, false);
            }

            if (command.target_reference == TargetReferenceMode.CurrentWorld)
                return FromDirectTarget(interactionMemory != null ? interactionMemory.currentWorldRoot : null, displayLabel, materialPrompt);

            if (command.target_reference == TargetReferenceMode.LastCreatedObject)
                return FromDirectTarget(interactionMemory != null ? interactionMemory.lastCreatedObject : null, displayLabel, materialPrompt);

            if (command.target_reference == TargetReferenceMode.LastInteractedTarget)
                return FromDirectTarget(interactionMemory != null ? interactionMemory.lastInteractedTarget : null, displayLabel, materialPrompt);

            if (command.target_reference == TargetReferenceMode.LastCreatedOrInteracted ||
                (string.IsNullOrWhiteSpace(targetLabel) && string.IsNullOrWhiteSpace(materialPrompt)))
            {
                return FromDirectTarget(interactionMemory != null ? interactionMemory.GetLastCreatedOrInteracted() : null, displayLabel, materialPrompt);
            }

            if (command.target_reference == TargetReferenceMode.CurrentSelection)
                return FromDirectTarget(interactionMemory != null ? interactionMemory.currentSelection : null, displayLabel, materialPrompt);

            List<GameObject> matches = FindMatchingTargets(targetLabel, materialPrompt, spatialQualifier);
            if (!all && !string.IsNullOrWhiteSpace(spatialQualifier) && matches.Count > 1)
            {
                return new SceneTargetResolution
                {
                    status = SceneTargetResolutionStatus.Ambiguous,
                    targets = matches,
                    message = BuildUnclearSpatialQualifierMessage(materialPrompt, targetLabel, spatialQualifier)
                };
            }
            return SceneTargetResolution.FromTargets(matches, displayLabel, all);
        }

        public GameObject ResolveTarget(TargetReferenceMode mode, string targetName, SpatialSnapshot spatial, HandSelection handSelection)
        {
            switch (mode)
            {
                case TargetReferenceMode.CurrentWorld:
                    return interactionMemory != null ? interactionMemory.currentWorldRoot : null;

                case TargetReferenceMode.LastCreatedObject:
                    return interactionMemory != null ? interactionMemory.lastCreatedObject : null;

                case TargetReferenceMode.LastInteractedTarget:
                    return interactionMemory != null ? interactionMemory.lastInteractedTarget : null;

                case TargetReferenceMode.LastCreatedOrInteracted:
                    return interactionMemory != null ? interactionMemory.GetLastCreatedOrInteracted() : null;

                case TargetReferenceMode.CurrentSelection:
                    return interactionMemory != null ? interactionMemory.currentSelection : null;

                case TargetReferenceMode.PointedObject:
                    return ResolvePointedObject(spatial, handSelection);

                case TargetReferenceMode.NamedObject:
                    return FindByNameOrAlias(targetName);

                case TargetReferenceMode.None:
                default:
                    if (!string.IsNullOrWhiteSpace(targetName))
                    {
                        return FindByNameOrAlias(targetName);
                    }
                    return null;
            }
        }

        public SceneTargetResolution ResolveTargets(VoiceIntentCommand command, SpatialSnapshot spatial)
        {
            return ResolveTargetDetailed(command, spatial);
        }

        public List<string> CollectTrackableNames(int maxCount = 32)
        {
            List<string> names = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            SpeechIntentTrackable[] trackables = FindTrackables();

            for (int i = 0; i < trackables.Length && names.Count < maxCount; i++)
            {
                SpeechIntentTrackable trackable = trackables[i];
                if (trackable == null)
                {
                    continue;
                }

                string name = trackable.EffectiveName;
                if (!string.IsNullOrWhiteSpace(name) && seen.Add(name))
                {
                    names.Add(name);
                }
            }

            return names;
        }

        private GameObject ResolvePointedObject(SpatialSnapshot spatial, HandSelection handSelection)
        {
            if (spatial == null)
            {
                return null;
            }

            if (TryGetPreferredHand(spatial, handSelection, out HandRaySnapshot hand))
            {
                GameObject byPath = FindByHierarchyPath(hand.hit_object_path);
                if (byPath != null)
                {
                    return byPath;
                }

                GameObject byName = FindByNameOrAlias(hand.hit_object_name);
                if (byName != null)
                {
                    return byName;
                }
            }

            return ResolveGazeObject(spatial);
        }

        private GameObject ResolveGazeObject(SpatialSnapshot spatial)
        {
            if (spatial == null || spatial.head_forward.sqrMagnitude <= 0.0001f)
            {
                return null;
            }

            Ray ray = new Ray(spatial.head_position, spatial.head_forward.normalized);
            if (!Physics.Raycast(ray, out RaycastHit hit, gazeMaxDistance, gazeRaycastMask, gazeTriggerInteraction))
            {
                return null;
            }

            GameObject target = hit.collider != null ? hit.collider.gameObject : null;
            return NormalizeTrackableRoot(target);
        }

        private bool TryGetPreferredHand(SpatialSnapshot spatial, HandSelection handSelection, out HandRaySnapshot hand)
        {
            hand = null;

            if (handSelection == HandSelection.Left && spatial.left_hand != null && spatial.left_hand.has_hit)
            {
                hand = spatial.left_hand;
                return true;
            }

            if (handSelection == HandSelection.Right && spatial.right_hand != null && spatial.right_hand.has_hit)
            {
                hand = spatial.right_hand;
                return true;
            }

            if (spatial.right_hand != null && spatial.right_hand.has_hit)
            {
                hand = spatial.right_hand;
                return true;
            }

            if (spatial.left_hand != null && spatial.left_hand.has_hit)
            {
                hand = spatial.left_hand;
                return true;
            }

            return false;
        }

        private GameObject FindByNameOrAlias(string targetName)
        {
            if (string.IsNullOrWhiteSpace(targetName))
            {
                return null;
            }

            SpeechIntentTrackable[] trackables = FindTrackables();
            for (int i = 0; i < trackables.Length; i++)
            {
                if (trackables[i] != null && trackables[i].Matches(targetName))
                {
                    return trackables[i].gameObject;
                }
            }

            Transform[] transforms = FindSearchTransforms();
            for (int i = 0; i < transforms.Length; i++)
            {
                Transform transform = transforms[i];
                if (transform != null && string.Equals(transform.name, targetName, StringComparison.OrdinalIgnoreCase))
                {
                    return transform.gameObject;
                }
            }

            return null;
        }

        private List<GameObject> FindMatchingTargets(string targetName, string materialPrompt, string spatialQualifier = "")
        {
            var targets = new List<GameObject>();
            var seen = new HashSet<int>();

            SpeechIntentTrackable[] trackables = FindTrackables();
            foreach (SpeechIntentTrackable trackable in trackables)
            {
                if (trackable == null || trackable.gameObject == null)
                    continue;

                GameObject candidate = trackable.gameObject;
                if (!MatchesNameAndMaterial(candidate, targetName, materialPrompt))
                    continue;

                if (seen.Add(candidate.GetInstanceID()))
                    targets.Add(candidate);
            }

            Transform[] transforms = FindSearchTransforms();
            foreach (Transform transform in transforms)
            {
                if (transform == null || transform.gameObject == null)
                    continue;

                GameObject candidate = NormalizeTrackableRoot(transform.gameObject);
                if (candidate == null)
                    continue;

                if (!MatchesNameAndMaterial(candidate, targetName, materialPrompt))
                    continue;

                if (seen.Add(candidate.GetInstanceID()))
                    targets.Add(candidate);
            }

            return ApplySpatialQualifier(targets, spatialQualifier);
        }

        private SceneTargetResolution FromDirectTarget(GameObject target, string label, string materialPrompt)
        {
            target = NormalizeTrackableRoot(target);
            if (target == null)
                return SceneTargetResolution.None(string.IsNullOrWhiteSpace(label) ? "No target found." : $"No matching {label} found.");

            if (!string.IsNullOrWhiteSpace(materialPrompt) && !MatchesMaterial(target, materialPrompt))
                return SceneTargetResolution.None($"No matching {label} found.");

            return SceneTargetResolution.FromTargets(new List<GameObject> { target }, label, false);
        }

        private bool MatchesNameAndMaterial(GameObject candidate, string targetName, string materialPrompt)
        {
            if (candidate == null)
                return false;

            if (!string.IsNullOrWhiteSpace(targetName))
            {
                SpeechIntentTrackable trackable = candidate.GetComponent<SpeechIntentTrackable>();
                if (trackable != null)
                {
                    if (!MatchesTargetName(trackable, targetName))
                        return false;
                }
                else if (!string.Equals(NormalizeTargetName(candidate.name), NormalizeTargetName(targetName), StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return string.IsNullOrWhiteSpace(materialPrompt) || MatchesMaterial(candidate, materialPrompt);
        }

        private static bool MatchesTargetName(SpeechIntentTrackable trackable, string targetName)
        {
            if (trackable == null)
                return false;

            if (trackable.Matches(targetName))
                return true;

            string normalizedTarget = NormalizeTargetName(targetName);
            if (NormalizeTargetName(trackable.EffectiveName) == normalizedTarget)
                return true;

            foreach (string alias in trackable.aliases)
            {
                if (NormalizeTargetName(alias) == normalizedTarget)
                    return true;
            }

            return false;
        }

        private static string NormalizeTargetName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";

            string normalized = value.Trim().ToLowerInvariant();
            if (normalized.StartsWith("the ", StringComparison.Ordinal))
                normalized = normalized.Substring(4).Trim();
            else if (normalized.StartsWith("a ", StringComparison.Ordinal))
                normalized = normalized.Substring(2).Trim();
            else if (normalized.StartsWith("an ", StringComparison.Ordinal))
                normalized = normalized.Substring(3).Trim();

            if (normalized.EndsWith("s", StringComparison.Ordinal) && normalized.Length > 1)
                normalized = normalized.Substring(0, normalized.Length - 1);
            return normalized;
        }

        private bool MatchesMaterial(GameObject candidate, string materialPrompt)
        {
            if (string.IsNullOrWhiteSpace(materialPrompt))
                return true;

            if (materialCatalog == null)
                materialCatalog = FindFirstObjectByType<RuntimeMaterialCatalog>(FindObjectsInactive.Include);

            if (materialCatalog != null)
                return materialCatalog.ObjectMatches(candidate, materialPrompt);

            if (!RuntimeMaterialCatalog.TryParseDescriptor(materialPrompt, RuntimeMaterialDescriptor.Default, out RuntimeMaterialDescriptor descriptor))
                return true;

            return RuntimeMaterialCatalog.ObjectMatches(candidate, descriptor);
        }

        public static void ExtractMaterialQualifierForCommand(ref string targetLabel, ref string materialPrompt)
        {
            if (!string.IsNullOrWhiteSpace(materialPrompt) || string.IsNullOrWhiteSpace(targetLabel))
                return;

            string working = targetLabel.Trim();
            if (working.StartsWith("all ", StringComparison.OrdinalIgnoreCase))
                working = working.Substring(4).Trim();
            else if (working.StartsWith("every ", StringComparison.OrdinalIgnoreCase))
                working = working.Substring(6).Trim();

            string[] words = working.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length < 2)
            {
                targetLabel = working;
                return;
            }

            for (int i = words.Length - 1; i > 0; i--)
            {
                string possibleMaterial = string.Join(" ", words, 0, i);
                if (!RuntimeMaterialCatalog.TryParseDescriptor(possibleMaterial, RuntimeMaterialDescriptor.Default, out _))
                    continue;

                string possibleName = string.Join(" ", words, i, words.Length - i);
                targetLabel = possibleName;
                materialPrompt = possibleMaterial;
                return;
            }
        }

        private bool MatchesSpatialQualifier(GameObject candidate, string targetName, string materialPrompt, string spatialQualifier)
        {
            if (string.IsNullOrWhiteSpace(spatialQualifier))
                return true;

            List<GameObject> matches = FindMatchingTargets(targetName, materialPrompt, "");
            List<GameObject> filtered = ApplySpatialQualifier(matches, spatialQualifier);
            foreach (GameObject target in filtered)
            {
                if (target == candidate)
                    return true;
            }
            return false;
        }

        private static List<GameObject> ApplySpatialQualifier(List<GameObject> targets, string spatialQualifier)
        {
            if (targets == null || targets.Count <= 1 || string.IsNullOrWhiteSpace(spatialQualifier))
                return targets ?? new List<GameObject>();

            string qualifier = NormalizeSpatialQualifier(spatialQualifier);
            if (qualifier != "topmost" && qualifier != "bottommost")
                return targets;

            var result = new List<GameObject>();
            float best = qualifier == "topmost" ? float.NegativeInfinity : float.PositiveInfinity;
            const float tolerance = 0.01f;

            foreach (GameObject target in targets)
            {
                if (target == null)
                    continue;

                float value = GetBoundsCenterY(target);
                if (qualifier == "topmost")
                {
                    if (value > best + tolerance)
                    {
                        result.Clear();
                        result.Add(target);
                        best = value;
                    }
                    else if (Mathf.Abs(value - best) <= tolerance)
                    {
                        result.Add(target);
                    }
                }
                else
                {
                    if (value < best - tolerance)
                    {
                        result.Clear();
                        result.Add(target);
                        best = value;
                    }
                    else if (Mathf.Abs(value - best) <= tolerance)
                    {
                        result.Add(target);
                    }
                }
            }

            if (result.Count == 1 && !HasHorizontalOverlapWithAny(result[0], targets))
                return targets;

            return result;
        }

        private static bool HasHorizontalOverlapWithAny(GameObject target, List<GameObject> targets)
        {
            if (target == null || targets == null || !TryGetWorldBounds(target, out Bounds targetBounds))
                return false;

            foreach (GameObject other in targets)
            {
                if (other == null || other == target || !TryGetWorldBounds(other, out Bounds otherBounds))
                    continue;

                if (OverlapsHorizontal(targetBounds, otherBounds))
                    return true;
            }

            return false;
        }

        private static bool OverlapsHorizontal(Bounds a, Bounds b)
        {
            return a.min.x <= b.max.x &&
                   a.max.x >= b.min.x &&
                   a.min.z <= b.max.z &&
                   a.max.z >= b.min.z;
        }

        private static float GetBoundsCenterY(GameObject target)
        {
            if (target == null)
                return 0f;

            if (TryGetWorldBounds(target, out Bounds bounds))
                return bounds.center.y;

            return target.transform.position.y;
        }

        private static bool TryGetWorldBounds(GameObject target, out Bounds bounds)
        {
            bounds = default;
            bool hasBounds = false;

            Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
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

            Collider[] colliders = target.GetComponentsInChildren<Collider>(true);
            foreach (Collider collider in colliders)
            {
                if (collider == null)
                    continue;

                if (!hasBounds)
                {
                    bounds = collider.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(collider.bounds);
                }
            }

            return hasBounds;
        }

        private static string NormalizeSpatialQualifier(string value)
        {
            string lower = (value ?? "").Trim().ToLowerInvariant();
            if (lower == "top" || lower == "on top" || lower == "topmost" || lower == "upper" || lower == "uppermost" || lower == "highest")
                return "topmost";
            if (lower == "bottom" || lower == "on bottom" || lower == "bottommost" || lower == "lower" || lower == "lowermost" || lower == "lowest")
                return "bottommost";
            return lower;
        }

        private static string BuildUnclearSpatialQualifierMessage(string materialPrompt, string targetLabel, string spatialQualifier)
        {
            string label = BuildDisplayLabel(materialPrompt, targetLabel, "");
            if (string.IsNullOrWhiteSpace(label))
                label = "object";

            string relation = NormalizeSpatialQualifier(spatialQualifier) == "topmost" ? "top" : "bottom";
            return $"I can't tell which {label} is {relation}; can you be more specific?";
        }

        private static string BuildDisplayLabel(string materialPrompt, string targetLabel, string spatialQualifier = "")
        {
            string spatial = NormalizeSpatialQualifier(spatialQualifier);
            if (!string.IsNullOrWhiteSpace(spatial))
                spatial = spatial + " ";
            else
                spatial = "";

            if (string.IsNullOrWhiteSpace(materialPrompt))
                return spatial + targetLabel;
            if (string.IsNullOrWhiteSpace(targetLabel))
                return spatial + materialPrompt;
            return spatial + materialPrompt + " " + targetLabel;
        }

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (string value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }
            return "";
        }

        private static bool IsAllToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            string normalized = value.Trim().ToLowerInvariant();
            return normalized == "all" ||
                   normalized == "everything" ||
                   normalized.StartsWith("all ", StringComparison.Ordinal) ||
                   normalized.StartsWith("every ", StringComparison.Ordinal);
        }

        private GameObject NormalizeTrackableRoot(GameObject target)
        {
            if (target == null)
            {
                return null;
            }

            SpeechIntentTrackable trackable = target.GetComponentInParent<SpeechIntentTrackable>();
            return trackable != null ? trackable.gameObject : target;
        }

        private SpeechIntentTrackable[] FindTrackables()
        {
            if (searchRoot != null)
            {
                return searchRoot.GetComponentsInChildren<SpeechIntentTrackable>(true);
            }

            return FindObjectsByType<SpeechIntentTrackable>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        }

        private Transform[] FindSearchTransforms()
        {
            if (searchRoot != null)
            {
                return searchRoot.GetComponentsInChildren<Transform>(true);
            }

            List<Transform> transforms = new List<Transform>();
            Scene activeScene = SceneManager.GetActiveScene();
            GameObject[] roots = activeScene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                transforms.AddRange(roots[i].GetComponentsInChildren<Transform>(true));
            }
            return transforms.ToArray();
        }

        private GameObject FindByHierarchyPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            string[] segments = path.Split('/');
            if (segments.Length == 0)
            {
                return null;
            }

            GameObject current = null;

            if (searchRoot != null)
            {
                Transform rootCandidate = FindChildByName(searchRoot, segments[0], includeSelf: true);
                current = rootCandidate != null ? rootCandidate.gameObject : null;
            }
            else
            {
                Scene activeScene = SceneManager.GetActiveScene();
                GameObject[] roots = activeScene.GetRootGameObjects();
                for (int i = 0; i < roots.Length; i++)
                {
                    if (string.Equals(roots[i].name, segments[0], StringComparison.OrdinalIgnoreCase))
                    {
                        current = roots[i];
                        break;
                    }
                }
            }

            if (current == null)
            {
                return null;
            }

            for (int i = 1; i < segments.Length; i++)
            {
                Transform child = FindChildByName(current.transform, segments[i], includeSelf: false);
                if (child == null)
                {
                    return null;
                }
                current = child.gameObject;
            }

            return current;
        }

        private Transform FindChildByName(Transform parent, string name, bool includeSelf)
        {
            if (includeSelf && string.Equals(parent.name, name, StringComparison.OrdinalIgnoreCase))
            {
                return parent;
            }

            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                if (string.Equals(child.name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return child;
                }
            }

            return null;
        }
    }
}
