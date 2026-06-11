using System;
using UnityEngine;

namespace WorldLabs.Runtime.Tools
{
    [Serializable]
    public sealed class SplatSpawnPose : MonoBehaviour
    {
        public bool hasPose;
        public Vector3 playerPosition = new Vector3(0f, 1.6f, 0f);
        public Quaternion playerRotation = Quaternion.identity;
        public Vector3 lookAt = new Vector3(0f, 1.4f, 2f);
        public bool hasLocalPose;
        public Vector3 localPlayerPosition = new Vector3(0f, 1.6f, 0f);
        public Quaternion localPlayerRotation = Quaternion.identity;
        public Vector3 localLookAt = new Vector3(0f, 1.4f, 2f);
        [Range(0f, 1f)] public float confidence = 0.5f;
        public string method = "floor_bounds_center_v1";

        public void SetWorldPose(Transform worldRoot, Vector3 worldPosition, Quaternion worldRotation, Vector3 worldLookAt)
        {
            hasPose = true;
            playerPosition = worldPosition;
            playerRotation = worldRotation;
            lookAt = worldLookAt;

            if (worldRoot == null)
            {
                hasLocalPose = false;
                return;
            }

            hasLocalPose = true;
            localPlayerPosition = worldRoot.InverseTransformPoint(worldPosition);
            localPlayerRotation = Quaternion.Inverse(worldRoot.rotation) * worldRotation;
            localLookAt = worldRoot.InverseTransformPoint(worldLookAt);
        }

        public bool TryGetWorldPose(Transform worldRoot, out Vector3 worldPosition, out Quaternion worldRotation, out Vector3 worldLookAt)
        {
            if (worldRoot != null && hasLocalPose)
            {
                worldPosition = worldRoot.TransformPoint(localPlayerPosition);
                worldRotation = worldRoot.rotation * localPlayerRotation;
                worldLookAt = worldRoot.TransformPoint(localLookAt);
                return true;
            }

            worldPosition = playerPosition;
            worldRotation = playerRotation;
            worldLookAt = lookAt;
            return hasPose;
        }
    }
}
