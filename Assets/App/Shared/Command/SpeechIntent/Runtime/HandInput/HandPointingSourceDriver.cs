using UnityEngine;

namespace SpeechIntent.HandInput
{
    public class HandPointingSourceDriver : MonoBehaviour
    {
        public PointingSource pointingSource;
        public Transform rayTransform;
        public bool requireAimPose = true;

        public void ApplySnapshot(HandGestureSnapshot snapshot)
        {
            if (pointingSource == null)
                return;

            bool available = snapshot.isTracked && (!requireAimPose || snapshot.hasAimPose);
            pointingSource.SetState(available, available);

            if (!available)
                return;

            Transform target = rayTransform != null ? rayTransform : pointingSource.transform;
            if (snapshot.hasAimPose)
            {
                target.SetPositionAndRotation(snapshot.aimPose.position, snapshot.aimPose.rotation);
            }
        }
    }
}
