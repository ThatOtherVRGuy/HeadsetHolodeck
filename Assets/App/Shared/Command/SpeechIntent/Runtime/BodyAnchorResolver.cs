using UnityEngine;

namespace SpeechIntent
{
    public static class BodyAnchorResolver
    {
        public static bool TryResolve(
            SpatialSnapshot spatial,
            BodyAnchor anchor,
            HandSelection handSelection,
            out Vector3 position,
            out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;

            BodyAnchor resolvedAnchor = anchor;
            if (resolvedAnchor == BodyAnchor.None)
                resolvedAnchor = FromHandSelection(handSelection);

            switch (resolvedAnchor)
            {
                case BodyAnchor.Head:
                    return TryResolveHead(spatial, out position, out rotation);
                case BodyAnchor.LeftHand:
                    return TryResolveHand(spatial?.left_hand, out position, out rotation);
                case BodyAnchor.RightHand:
                    return TryResolveHand(spatial?.right_hand, out position, out rotation);
                default:
                    return false;
            }
        }

        public static BodyAnchor FromHandSelection(HandSelection selection)
        {
            return selection switch
            {
                HandSelection.Left => BodyAnchor.LeftHand,
                HandSelection.Right => BodyAnchor.RightHand,
                _ => BodyAnchor.None
            };
        }

        static bool TryResolveHead(SpatialSnapshot spatial, out Vector3 position, out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;

            if (spatial != null && spatial.head_forward.sqrMagnitude > 0.0001f)
            {
                position = spatial.head_position;
                rotation = Quaternion.LookRotation(spatial.head_forward.normalized, Vector3.up);
                return true;
            }

            Camera mainCamera = Camera.main;
            if (mainCamera == null)
            {
                GameObject cameraObject = GameObject.Find("Main Camera");
                if (cameraObject != null)
                    mainCamera = cameraObject.GetComponent<Camera>();
            }

            Transform cameraTransform = mainCamera != null ? mainCamera.transform : GameObject.Find("Main Camera")?.transform;
            if (cameraTransform == null)
                return false;

            position = cameraTransform.position;
            rotation = cameraTransform.rotation;
            return true;
        }

        static bool TryResolveHand(HandRaySnapshot hand, out Vector3 position, out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;

            if (hand == null || !hand.is_available)
                return false;

            position = hand.origin;
            Vector3 forward = hand.direction.sqrMagnitude > 0.0001f ? hand.direction.normalized : Vector3.forward;
            rotation = Quaternion.LookRotation(forward, Vector3.up);
            return true;
        }
    }
}
