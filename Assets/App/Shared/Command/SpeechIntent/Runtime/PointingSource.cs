using UnityEngine;

namespace SpeechIntent
{
    public class PointingSource : MonoBehaviour
    {
        [Tooltip("Optional explicit origin. Defaults to this transform if not assigned.")]
        public Transform rayOrigin;

        [Tooltip("External hand/controller code should update these flags.")]
        public bool isAvailable = true;
        public bool isPointing = true;

        public Transform Origin => rayOrigin != null ? rayOrigin : transform;

        public void SetState(bool available, bool pointing)
        {
            isAvailable = available;
            isPointing = pointing;
        }
    }
}
