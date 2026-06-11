using UnityEngine;

namespace SpeechIntent
{
    public class TargetTransformController : MonoBehaviour
    {
        public SceneEntityResolver entityResolver;
        public InteractionMemory interactionMemory;

        [Header("Origin")]
        [Tooltip("World-space origin point used for spatial_reference=WorldOrigin. Defaults to Vector3.zero if null.")]
        public Transform originPoint;

        [Tooltip("Frame of reference for spatial_reference=RelativeToMe. Defaults to a GameObject named 'Me'.")]
        public Transform meReference;

        [Header("Defaults")]
        public float defaultScaleUpMultiplier = 1.25f;
        public float defaultScaleDownMultiplier = 0.8f;
        public float defaultRotationDegrees = 45f;
        public float defaultMoveDistance = 2f;
        public float defaultRelativeToMeDistance = 2f;
        public float minScaleComponent = 0.01f;
        public float pointingSurfaceOffset = 0.01f;
        public bool debugMovementLogging = true;

        public string LastFailureMessage { get; private set; } = "";

        public bool TryMoveTarget(VoiceIntentCommand command, SpatialSnapshot spatial, out GameObject target)
        {
            LastFailureMessage = "";
            target = ResolveTarget(command, spatial);
            if (target == null)
            {
                LogMoveDebug(command, null, "ResolveTarget failed. " + LastFailureMessage);
                return false;
            }

            Vector3 before = target.transform.position;
            if (!TryResolveDestination(command, spatial, target, out Vector3 destination))
            {
                LogMoveDebug(command, target, "TryResolveDestination failed. " + LastFailureMessage);
                return false;
            }

            target.transform.position = destination;
            LogMoveDebug(command, target, $"Moved from {FormatVector(before)} to {FormatVector(destination)} delta={FormatVector(destination - before)}.");
            RegisterInteraction(target);
            return true;
        }

        public bool TryScaleTarget(VoiceIntentCommand command, SpatialSnapshot spatial, out GameObject target)
        {
            LastFailureMessage = "";
            target = ResolveTarget(command, spatial);
            if (target == null)
            {
                return false;
            }

            if (command.reset_to_default_scale)
            {
                target.transform.localScale = Vector3.one;
                RegisterInteraction(target);
                return true;
            }

            float multiplier = command.scale_multiplier;
            if (Mathf.Approximately(multiplier, 0f))
            {
                multiplier = InferDefaultScaleMultiplier(command.transcript);
            }

            if (multiplier <= 0f)
            {
                return false;
            }

            Vector3 newScale = target.transform.localScale * multiplier;
            newScale.x = Mathf.Max(newScale.x, minScaleComponent);
            newScale.y = Mathf.Max(newScale.y, minScaleComponent);
            newScale.z = Mathf.Max(newScale.z, minScaleComponent);
            target.transform.localScale = newScale;
            RegisterInteraction(target);
            return true;
        }

        public bool TryResetTransform(VoiceIntentCommand command, SpatialSnapshot spatial, out GameObject target)
        {
            LastFailureMessage = "";
            target = ResolveTarget(command, spatial);
            if (target == null)
            {
                return false;
            }

            target.transform.position   = originPoint != null ? originPoint.position : Vector3.zero;
            target.transform.rotation   = Quaternion.identity;
            target.transform.localScale = Vector3.one;
            RegisterInteraction(target);
            return true;
        }

        public bool TryRotateTarget(VoiceIntentCommand command, SpatialSnapshot spatial, out GameObject target)
        {
            LastFailureMessage = "";
            target = ResolveTarget(command, spatial);
            if (target == null)
            {
                return false;
            }

            RotationAxis axis = command.rotation_axis == RotationAxis.None ? InferDefaultRotationAxis(command.transcript) : command.rotation_axis;
            float degrees = Mathf.Approximately(command.rotation_degrees, 0f) ? InferDefaultRotationDegrees(command.transcript) : command.rotation_degrees;
            Vector3 axisVector = ToAxisVector(axis);

            if (axisVector == Vector3.zero || Mathf.Approximately(degrees, 0f))
            {
                return false;
            }

            target.transform.rotation = Quaternion.AngleAxis(degrees, axisVector) * target.transform.rotation;
            RegisterInteraction(target);
            return true;
        }

        private GameObject ResolveTarget(VoiceIntentCommand command, SpatialSnapshot spatial)
        {
            if (IsMeTarget(command))
            {
                Transform me = ResolveMeReference();
                if (me != null)
                {
                    return me.gameObject;
                }

                LastFailureMessage = "No Me object found.";
                return null;
            }

            if (entityResolver == null)
            {
                return interactionMemory != null ? interactionMemory.GetLastCreatedOrInteracted() : null;
            }

            SceneTargetResolution resolution = entityResolver.ResolveTargetDetailed(command, spatial);
            if (resolution.status == SceneTargetResolutionStatus.Ambiguous)
            {
                LastFailureMessage = resolution.message;
                return null;
            }

            if (resolution.status == SceneTargetResolutionStatus.None)
            {
                LastFailureMessage = resolution.message;
                return null;
            }

            return resolution.Target;
        }

        private void RegisterInteraction(GameObject target)
        {
            if (interactionMemory != null && target != null)
            {
                interactionMemory.RegisterInteraction(target);
            }
        }

        private bool TryResolveDestination(VoiceIntentCommand command, SpatialSnapshot spatial, GameObject target, out Vector3 destination)
        {
            destination = Vector3.zero;

            if (ShouldUseRelativeMove(command))
            {
                return TryResolveRelativeToMeDestination(command, target, out destination);
            }

            if (command.spatial_reference == SpatialReferenceMode.BodyAnchor)
            {
                if (BodyAnchorResolver.TryResolve(spatial, command.body_anchor, command.target_hand, out destination, out _))
                    return true;

                return false;
            }

            if (spatial == null)
            {
                return false;
            }

            if (command.spatial_reference == SpatialReferenceMode.PointingHit)
            {
                if (TryGetPreferredHand(command.target_hand, spatial, out HandRaySnapshot hand) && hand.has_hit)
                {
                    destination = hand.hit_point + hand.hit_normal * pointingSurfaceOffset;
                    return true;
                }
            }

            if (command.spatial_reference == SpatialReferenceMode.HandMidpoint && spatial.has_hand_midpoint)
            {
                destination = spatial.hand_midpoint;
                return true;
            }

            if (command.spatial_reference == SpatialReferenceMode.PointingRay)
            {
                if (TryGetPreferredHand(command.target_hand, spatial, out HandRaySnapshot rayHand))
                {
                    destination = rayHand.origin + rayHand.direction.normalized * defaultMoveDistance;
                    return true;
                }
            }

            if (command.spatial_reference == SpatialReferenceMode.HeadForward && spatial.head_forward.sqrMagnitude > 0.0001f)
            {
                destination = spatial.head_position + spatial.head_forward.normalized * defaultMoveDistance;
                return true;
            }

            if (command.spatial_reference == SpatialReferenceMode.WorldOrigin)
            {
                destination = originPoint != null ? originPoint.position : Vector3.zero;
                return true;
            }

            if (TryGetPreferredHand(command.target_hand, spatial, out HandRaySnapshot fallbackHand))
            {
                if (fallbackHand.has_hit)
                {
                    destination = fallbackHand.hit_point + fallbackHand.hit_normal * pointingSurfaceOffset;
                    return true;
                }

                destination = fallbackHand.origin + fallbackHand.direction.normalized * defaultMoveDistance;
                return true;
            }

            if (spatial.head_forward.sqrMagnitude > 0.0001f)
            {
                destination = spatial.head_position + spatial.head_forward.normalized * defaultMoveDistance;
                return true;
            }

            return false;
        }

        private bool TryResolveRelativeToMeDestination(VoiceIntentCommand command, GameObject target, out Vector3 destination)
        {
            destination = Vector3.zero;

            Transform me = ResolveMeReference();
            if (me == null)
            {
                return false;
            }

            RelativeDirection direction = command.relative_direction == RelativeDirection.None
                ? InferRelativeDirection(command.transcript)
                : command.relative_direction;

            Vector3 directionVector = ResolveRelativeDirection(me, direction);
            if (directionVector.sqrMagnitude <= 0.0001f)
            {
                return false;
            }

            float distance = ResolveRelativeDistanceMeters(command);

            if (ShouldOffsetFromCurrentTarget(command, target))
            {
                destination = target.transform.position + directionVector.normalized * distance;
                LogMoveDebug(command, target,
                    $"RelativeToMe target-offset direction={direction} vector={FormatVector(directionVector.normalized)} distance={distance:0.####} me={FormatVector(me.position)} current={FormatVector(target.transform.position)} destination={FormatVector(destination)}.");
                return true;
            }

            destination = me.position + directionVector.normalized * distance;
            LogMoveDebug(command, target,
                $"RelativeToMe me-anchored direction={direction} vector={FormatVector(directionVector.normalized)} distance={distance:0.####} me={FormatVector(me.position)} current={(target != null ? FormatVector(target.transform.position) : "<null>")} destination={FormatVector(destination)}.");
            return true;
        }

        private static bool ShouldUseRelativeMove(VoiceIntentCommand command)
        {
            if (command == null)
                return false;

            return command.spatial_reference == SpatialReferenceMode.RelativeToMe ||
                   command.relative_direction != RelativeDirection.None ||
                   command.relative_distance_meters > 0f;
        }

        private bool ShouldOffsetFromCurrentTarget(VoiceIntentCommand command, GameObject target)
        {
            if (target == null || IsMeTargetObject(target))
                return false;

            RelativeDirection direction = command.relative_direction == RelativeDirection.None
                ? InferRelativeDirection(command.transcript)
                : command.relative_direction;

            switch (direction)
            {
                case RelativeDirection.Up:
                case RelativeDirection.Down:
                case RelativeDirection.Left:
                case RelativeDirection.Right:
                case RelativeDirection.Forward:
                case RelativeDirection.Back:
                    return true;
                default:
                    return false;
            }
        }

        private float ResolveRelativeDistanceMeters(VoiceIntentCommand command)
        {
            if (DistanceUnitParser.TryExtractMeters(command?.transcript, out float transcriptMeters))
            {
                if (command == null || command.relative_distance_meters <= 0f)
                    return transcriptMeters;

                if (Mathf.Abs(command.relative_distance_meters - transcriptMeters) > 0.001f)
                {
                    Debug.LogWarning(
                        "[TargetTransformController] MoveDebug distance mismatch: " +
                        $"command.relative_distance_meters={command.relative_distance_meters:0.####}, " +
                        $"transcriptMeters={transcriptMeters:0.####}, transcript='{command.transcript}'. Using transcript-derived meters.");
                    return transcriptMeters;
                }
            }

            return command != null && command.relative_distance_meters > 0f
                ? command.relative_distance_meters
                : defaultRelativeToMeDistance;
        }

        private Transform ResolveMeReference()
        {
            if (meReference != null)
            {
                return meReference;
            }

            GameObject me = GameObject.Find("Me");
            if (me != null)
            {
                meReference = me.transform;
                return meReference;
            }

            return null;
        }

        private static bool IsMeTarget(VoiceIntentCommand command)
        {
            if (command == null)
                return false;

            if (IsMeName(command.target_name) || IsMeName(command.object_name))
                return true;

            bool hasExplicitNamedTarget = !string.IsNullOrWhiteSpace(command.target_name) ||
                                          !string.IsNullOrWhiteSpace(command.object_name);

            return !hasExplicitNamedTarget && IsMeName(command.target_entity);
        }

        private static bool IsMeTargetObject(GameObject target)
        {
            if (target == null)
                return false;

            Transform current = target.transform;
            while (current != null)
            {
                if (IsMeName(current.name))
                    return true;
                current = current.parent;
            }

            return false;
        }

        private static bool IsMeName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            string normalized = value.Trim().ToLowerInvariant();
            return normalized == "me" ||
                   normalized == "i" ||
                   normalized == "user" ||
                   normalized == "myself" ||
                   normalized == "player" ||
                   normalized == "xr origin";
        }

        private RelativeDirection InferRelativeDirection(string transcript)
        {
            string text = (transcript ?? string.Empty).ToLowerInvariant();
            if (text.Contains("in front of me"))
                return RelativeDirection.InFront;
            if (text.Contains("behind me"))
                return RelativeDirection.Behind;
            if (text.Contains("behind") || text.Contains("backward") || text.Contains("backwards") || text.Contains(" back"))
                return RelativeDirection.Back;
            if (text.Contains("left"))
                return RelativeDirection.Left;
            if (text.Contains("right"))
                return RelativeDirection.Right;
            if (text.Contains("above") || text.Contains(" up"))
                return RelativeDirection.Up;
            if (text.Contains("below") || text.Contains(" down"))
                return RelativeDirection.Down;
            return RelativeDirection.Forward;
        }

        private static Vector3 ResolveRelativeDirection(Transform me, RelativeDirection direction)
        {
            if (me == null)
                return Vector3.zero;

            Vector3 forward = Vector3.ProjectOnPlane(me.forward, Vector3.up);
            if (forward.sqrMagnitude <= 0.0001f)
                forward = Vector3.ProjectOnPlane(me.rotation * Vector3.forward, Vector3.up);
            if (forward.sqrMagnitude <= 0.0001f)
                forward = Vector3.forward;
            forward.Normalize();

            Vector3 right = Vector3.ProjectOnPlane(me.right, Vector3.up);
            if (right.sqrMagnitude <= 0.0001f)
                right = Vector3.Cross(Vector3.up, forward);
            right.Normalize();

            switch (direction)
            {
                case RelativeDirection.Forward:
                case RelativeDirection.InFront:
                    return forward;
                case RelativeDirection.Back:
                case RelativeDirection.Behind:
                    return -forward;
                case RelativeDirection.Left:
                    return -right;
                case RelativeDirection.Right:
                    return right;
                case RelativeDirection.Up:
                    return Vector3.up;
                case RelativeDirection.Down:
                    return Vector3.down;
                default:
                    return Vector3.zero;
            }
        }

        private bool TryGetPreferredHand(HandSelection selection, SpatialSnapshot spatial, out HandRaySnapshot hand)
        {
            hand = null;

            if (selection == HandSelection.Left && spatial.left_hand != null && spatial.left_hand.is_available)
            {
                hand = spatial.left_hand;
                return true;
            }

            if (selection == HandSelection.Right && spatial.right_hand != null && spatial.right_hand.is_available)
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

            if (spatial.right_hand != null && spatial.right_hand.is_available)
            {
                hand = spatial.right_hand;
                return true;
            }

            if (spatial.left_hand != null && spatial.left_hand.is_available)
            {
                hand = spatial.left_hand;
                return true;
            }

            return false;
        }

        private float InferDefaultScaleMultiplier(string transcript)
        {
            string text = (transcript ?? string.Empty).ToLowerInvariant();
            if (text.Contains("smaller") || text.Contains("too big") || text.Contains("shrink") || text.Contains("reduce"))
            {
                return defaultScaleDownMultiplier;
            }

            return defaultScaleUpMultiplier;
        }

        private RotationAxis InferDefaultRotationAxis(string transcript)
        {
            string text = (transcript ?? string.Empty).ToLowerInvariant();
            if (text.Contains("upside down") || text.Contains("flip"))
            {
                return RotationAxis.X;
            }

            return RotationAxis.Y;
        }

        private float InferDefaultRotationDegrees(string transcript)
        {
            string text = (transcript ?? string.Empty).ToLowerInvariant();
            if (text.Contains("upside down") || text.Contains("flip"))
            {
                return 180f;
            }

            return defaultRotationDegrees;
        }

        private Vector3 ToAxisVector(RotationAxis axis)
        {
            switch (axis)
            {
                case RotationAxis.X:
                    return Vector3.right;
                case RotationAxis.Y:
                    return Vector3.up;
                case RotationAxis.Z:
                    return Vector3.forward;
                default:
                    return Vector3.zero;
            }
        }

        private void LogMoveDebug(VoiceIntentCommand command, GameObject target, string detail)
        {
            if (!debugMovementLogging)
                return;

            string targetInfo = target != null
                ? $"{target.name} pos={FormatVector(target.transform.position)}"
                : "<null>";

            Debug.Log(
                "[TargetTransformController] MoveDebug " +
                $"target={targetInfo}; " +
                $"intent={command?.intent}; " +
                $"transcript='{command?.transcript}'; " +
                $"target_reference={command?.target_reference}; " +
                $"target_name='{command?.target_name}'; " +
                $"object_name='{command?.object_name}'; " +
                $"target_entity='{command?.target_entity}'; " +
                $"spatial_reference={command?.spatial_reference}; " +
                $"relative_direction={command?.relative_direction}; " +
                $"relative_distance_meters={command?.relative_distance_meters:0.####}; " +
                detail);
        }

        private static string FormatVector(Vector3 value)
        {
            return $"({value.x:0.####}, {value.y:0.####}, {value.z:0.####})";
        }
    }
}
