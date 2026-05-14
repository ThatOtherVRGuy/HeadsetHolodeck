using System.Text;
using UnityEngine;

namespace SpeechIntent
{
    public class SpatialContextProvider : MonoBehaviour
    {
        [Header("Sources")]
        public PointingSource leftHandSource;
        public PointingSource rightHandSource;
        public Transform headTransform;

        [Header("Raycast")]
        public LayerMask raycastMask = ~0;
        [Range(0.1f, 1000f)] public float maxRayDistance = 100f;
        public QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.Ignore;

        [Header("Pointing Heuristics")]
        public bool inferPointingFromHeadPose = true;
        [Range(0f, 1f)] public float minPointingDistanceFromHead = 0.18f;
        [Range(5f, 180f)] public float maxPointingAngleFromHead = 85f;

        public SpatialSnapshot CaptureSnapshot()
        {
            SpatialSnapshot snapshot = new SpatialSnapshot();

            if (headTransform != null)
            {
                snapshot.head_position = headTransform.position;
                snapshot.head_forward = headTransform.forward;
            }

            snapshot.left_hand = BuildHandSnapshot(leftHandSource);
            snapshot.right_hand = BuildHandSnapshot(rightHandSource);

            if (snapshot.left_hand.is_available && snapshot.right_hand.is_available)
            {
                snapshot.has_hand_midpoint = true;
                snapshot.hand_midpoint = (snapshot.left_hand.origin + snapshot.right_hand.origin) * 0.5f;
            }

            return snapshot;
        }

        private HandRaySnapshot BuildHandSnapshot(PointingSource source)
        {
            HandRaySnapshot hand = new HandRaySnapshot();

            if (source == null || source.Origin == null)
            {
                return hand;
            }

            hand.is_available = source.isAvailable;
            hand.source_name = source.name;
            hand.origin = source.Origin.position;
            hand.direction = source.Origin.forward;
            ApplyPointingHeuristics(source, hand);

            if (hand.is_available)
            {
                Ray ray = new Ray(hand.origin, hand.direction);
                if (Physics.Raycast(ray, out RaycastHit hit, maxRayDistance, raycastMask, triggerInteraction))
                {
                    hand.has_hit = true;
                    hand.hit_point = hit.point;
                    hand.hit_normal = hit.normal;
                    hand.hit_object_name = hit.collider != null ? hit.collider.name : string.Empty;
                    hand.hit_object_path = hit.collider != null ? BuildHierarchyPath(hit.collider.transform) : string.Empty;
                    hand.hit_root_name = hit.collider != null && hit.collider.transform.root != null
                        ? hit.collider.transform.root.name
                        : string.Empty;
                }
            }

            return hand;
        }

        private void ApplyPointingHeuristics(PointingSource source, HandRaySnapshot hand)
        {
            hand.is_pointing = source.isPointing;
            hand.pointing_confidence = hand.is_pointing ? 0.5f : 0f;

            if (headTransform == null || !inferPointingFromHeadPose)
            {
                if (hand.is_pointing) hand.pointing_confidence = 1f;
                return;
            }

            Vector3 headToHand = hand.origin - headTransform.position;
            hand.distance_from_head = headToHand.magnitude;
            Vector3 headForward = headTransform.forward.sqrMagnitude > 0.0001f
                ? headTransform.forward.normalized
                : Vector3.forward;
            Vector3 handDirection = hand.direction.sqrMagnitude > 0.0001f
                ? hand.direction.normalized
                : Vector3.zero;

            hand.forward_alignment = handDirection == Vector3.zero
                ? 0f
                : Mathf.Clamp01(Vector3.Dot(headForward, handDirection));

            float angle = handDirection == Vector3.zero
                ? 180f
                : Vector3.Angle(headForward, handDirection);
            bool farEnough = hand.distance_from_head >= minPointingDistanceFromHead;
            bool forwardEnough = angle <= maxPointingAngleFromHead;
            hand.is_pointing = source.isPointing && farEnough && forwardEnough;

            float distanceScore = Mathf.InverseLerp(minPointingDistanceFromHead, minPointingDistanceFromHead + 0.45f, hand.distance_from_head);
            float angleScore = 1f - Mathf.InverseLerp(0f, maxPointingAngleFromHead, angle);
            hand.pointing_confidence = hand.is_pointing
                ? Mathf.Clamp01(distanceScore * 0.55f + angleScore * 0.45f)
                : 0f;
        }

        private string BuildHierarchyPath(Transform target)
        {
            if (target == null)
            {
                return string.Empty;
            }

            StringBuilder sb = new StringBuilder();
            AppendHierarchyPath(target, sb);
            return sb.ToString();
        }

        private void AppendHierarchyPath(Transform current, StringBuilder sb)
        {
            if (current.parent != null)
            {
                AppendHierarchyPath(current.parent, sb);
                sb.Append('/');
            }

            sb.Append(current.name);
        }
    }
}
