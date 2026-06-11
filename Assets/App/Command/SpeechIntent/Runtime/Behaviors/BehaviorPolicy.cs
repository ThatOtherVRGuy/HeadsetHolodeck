using UnityEngine;

namespace SpeechIntent.Behaviors
{
    public sealed class BehaviorPolicy
    {
        public float maxSpinDegreesPerSecond = 720f;
        public float maxOrbitDegreesPerSecond = 360f;
        public float minOrbitRadius = 0.05f;
        public float maxOrbitRadius = 25f;
        public float maxThrowSpeedMetersPerSecond = 8f;
        public int maxTargets = 1;

        public bool CanModify(GameObject target, out string reason)
        {
            reason = "";
            if (target == null)
            {
                reason = "No target.";
                return false;
            }

            SpeechIntentTrackable trackable = target.GetComponent<SpeechIntentTrackable>();
            if (IsProtectedName(target.name) ||
                IsProtectedName(trackable != null ? trackable.EffectiveName : "") ||
                HasProtectedParent(target.transform))
            {
                reason = "I cannot attach that behavior to a protected Holodeck object.";
                return false;
            }

            if (trackable == null)
            {
                reason = "That object is not available for runtime behaviors.";
                return false;
            }

            return true;
        }

        public float ClampSpinSpeed(float value)
        {
            if (Mathf.Approximately(value, 0f))
                value = 90f;

            return Mathf.Clamp(value, -maxSpinDegreesPerSecond, maxSpinDegreesPerSecond);
        }

        public float ClampOrbitSpeed(float value)
        {
            if (Mathf.Approximately(value, 0f))
                value = 30f;

            return Mathf.Clamp(value, -maxOrbitDegreesPerSecond, maxOrbitDegreesPerSecond);
        }

        public float ClampOrbitRadius(float value)
        {
            return Mathf.Clamp(value, minOrbitRadius, maxOrbitRadius);
        }

        public float ClampThrowSpeed(float value)
        {
            if (Mathf.Approximately(value, 0f))
                value = 5f;

            return Mathf.Clamp(value, 0.1f, maxThrowSpeedMetersPerSecond);
        }

        static bool HasProtectedParent(Transform transform)
        {
            Transform current = transform;
            while (current != null)
            {
                if (IsProtectedName(current.name))
                    return true;

                current = current.parent;
            }

            return false;
        }

        static bool IsProtectedName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            string normalized = value.Trim().ToLowerInvariant();
            string compact = normalized.Replace(" ", "");
            return normalized == "me" ||
                   normalized == "main camera" ||
                   normalized == "arch" ||
                   normalized == "archlcars" ||
                   normalized == "systems" ||
                   normalized == "lcars" ||
                   normalized == "world manager" ||
                   normalized == "speechintent" ||
                   compact.Contains("worldmanager") ||
                   compact.Contains("speechintent") ||
                   normalized.Contains("lcars");
        }
    }
}
