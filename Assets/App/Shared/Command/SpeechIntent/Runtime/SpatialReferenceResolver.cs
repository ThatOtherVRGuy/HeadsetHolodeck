using System;
using UnityEngine;

namespace SpeechIntent
{
    public enum SpatialResolveStatus
    {
        Resolved,
        Missing,
        Ambiguous
    }

    public readonly struct SpatialResolveResult
    {
        public SpatialResolveResult(SpatialResolveStatus status, string message)
        {
            this.status = status;
            this.message = message ?? string.Empty;
        }

        public readonly SpatialResolveStatus status;
        public readonly string message;
        public bool resolved => status == SpatialResolveStatus.Resolved;

        public static SpatialResolveResult Resolved() =>
            new SpatialResolveResult(SpatialResolveStatus.Resolved, string.Empty);

        public static SpatialResolveResult Missing(string message) =>
            new SpatialResolveResult(SpatialResolveStatus.Missing, message);

        public static SpatialResolveResult Ambiguous(string message) =>
            new SpatialResolveResult(SpatialResolveStatus.Ambiguous, message);
    }

    public static class SpatialReferenceResolver
    {
        const float AmbiguousConfidenceDelta = 0.15f;

        public static bool TryResolvePointingHand(
            SpatialSnapshot spatial,
            HandSelection selection,
            out HandRaySnapshot hand,
            out SpatialResolveResult result)
        {
            hand = null;

            if (spatial == null)
            {
                result = SpatialResolveResult.Missing("Where?");
                return false;
            }

            if (selection == HandSelection.Left)
                return TryResolveExplicit(spatial.left_hand, "left", out hand, out result);

            if (selection == HandSelection.Right)
                return TryResolveExplicit(spatial.right_hand, "right", out hand, out result);

            bool leftPointing = IsPointing(spatial.left_hand);
            bool rightPointing = IsPointing(spatial.right_hand);

            if (leftPointing && rightPointing)
            {
                float delta = Mathf.Abs(spatial.left_hand.pointing_confidence - spatial.right_hand.pointing_confidence);
                if (delta < AmbiguousConfidenceDelta || selection == HandSelection.Both)
                {
                    result = SpatialResolveResult.Ambiguous("Which hand?");
                    return false;
                }

                hand = spatial.left_hand.pointing_confidence > spatial.right_hand.pointing_confidence
                    ? spatial.left_hand
                    : spatial.right_hand;
                result = SpatialResolveResult.Resolved();
                return true;
            }

            if (rightPointing)
            {
                hand = spatial.right_hand;
                result = SpatialResolveResult.Resolved();
                return true;
            }

            if (leftPointing)
            {
                hand = spatial.left_hand;
                result = SpatialResolveResult.Resolved();
                return true;
            }

            result = SpatialResolveResult.Missing("Where?");
            return false;
        }

        public static bool TryResolvePointingVector(
            SpatialSnapshot spatial,
            HandSelection selection,
            bool allowHeadForward,
            out Vector3 vector,
            out SpatialResolveResult result)
        {
            vector = Vector3.zero;

            if (TryResolvePointingHand(spatial, selection, out HandRaySnapshot hand, out result))
            {
                vector = hand.direction.normalized;
                return vector.sqrMagnitude > 0.0001f;
            }

            if (allowHeadForward && spatial != null && spatial.head_forward.sqrMagnitude > 0.0001f)
            {
                vector = spatial.head_forward.normalized;
                result = SpatialResolveResult.Resolved();
                return true;
            }

            return false;
        }

        static bool TryResolveExplicit(
            HandRaySnapshot candidate,
            string handName,
            out HandRaySnapshot hand,
            out SpatialResolveResult result)
        {
            hand = null;
            if (!IsPointing(candidate))
            {
                result = SpatialResolveResult.Missing($"Point with your {handName} hand.");
                return false;
            }

            hand = candidate;
            result = SpatialResolveResult.Resolved();
            return true;
        }

        static bool IsPointing(HandRaySnapshot hand) =>
            hand != null &&
            hand.is_available &&
            hand.is_pointing &&
            hand.direction.sqrMagnitude > 0.0001f;
    }
}
