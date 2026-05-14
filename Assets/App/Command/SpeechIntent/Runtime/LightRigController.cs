using UnityEngine;

namespace SpeechIntent
{
    public class LightRigController : MonoBehaviour
    {
        public Light sunLight;
        public Transform skyReferenceOrigin;

        [Header("Presets")]
        public float dayIntensity = 1.1f;
        public float nightIntensity = 0.12f;
        public float sunsetIntensity = 0.65f;
        public string LastFailureMessage { get; private set; } = "";

        public void ApplyPreset(string preset)
        {
            if (sunLight == null)
            {
                Debug.LogWarning("No sun light assigned.");
                return;
            }

            string normalized = (preset ?? "").Trim().ToLowerInvariant();

            switch (normalized)
            {
                case "night":
                case "nighttime":
                    sunLight.intensity = nightIntensity;
                    sunLight.transform.rotation = Quaternion.Euler(340f, 210f, 0f);
                    RenderSettings.ambientIntensity = 0.2f;
                    break;

                case "sunset":
                case "evening":
                    sunLight.intensity = sunsetIntensity;
                    sunLight.transform.rotation = Quaternion.Euler(15f, 250f, 0f);
                    RenderSettings.ambientIntensity = 0.6f;
                    break;

                case "day":
                case "daytime":
                case "sunny":
                default:
                    sunLight.intensity = dayIntensity;
                    sunLight.transform.rotation = Quaternion.Euler(50f, 30f, 0f);
                    RenderSettings.ambientIntensity = 1.0f;
                    break;
            }

            Debug.Log($"Applied lighting preset: {preset}");
        }

        public bool TryAlignSun(VoiceIntentCommand command, SpatialSnapshot spatial)
        {
            if (sunLight == null || command == null || spatial == null)
            {
                LastFailureMessage = "Point where the sun should be.";
                return false;
            }

            if (TryGetReferenceVector(command, spatial, out Vector3 towardSun))
            {
                Quaternion sunRotation = Quaternion.LookRotation(-towardSun.normalized, Vector3.up);
                sunLight.transform.rotation = sunRotation;
                return true;
            }

            if (string.IsNullOrWhiteSpace(LastFailureMessage))
                LastFailureMessage = "Point where the sun should be.";
            return false;
        }

        private bool TryGetReferenceVector(VoiceIntentCommand command, SpatialSnapshot spatial, out Vector3 towardSun)
        {
            LastFailureMessage = "";
            towardSun = Vector3.zero;

            switch (command.spatial_reference)
            {
                case SpatialReferenceMode.PointingHit:
                    if (SpatialReferenceResolver.TryResolvePointingHand(spatial, command.target_hand, out HandRaySnapshot hitHand, out SpatialResolveResult hitResult) && hitHand.has_hit)
                    {
                        Vector3 origin = skyReferenceOrigin != null ? skyReferenceOrigin.position : hitHand.origin;
                        towardSun = (hitHand.hit_point - origin).normalized;
                        return towardSun.sqrMagnitude > 0.0001f;
                    }
                    LastFailureMessage = hitResult.message;
                    break;

                case SpatialReferenceMode.PointingRay:
                    if (SpatialReferenceResolver.TryResolvePointingHand(spatial, command.target_hand, out HandRaySnapshot rayHand, out SpatialResolveResult rayResult))
                    {
                        towardSun = rayHand.direction.normalized;
                        return towardSun.sqrMagnitude > 0.0001f;
                    }
                    LastFailureMessage = rayResult.message;
                    break;

                case SpatialReferenceMode.HeadForward:
                    towardSun = spatial.head_forward.normalized;
                    return towardSun.sqrMagnitude > 0.0001f;
            }

            if (SpatialReferenceResolver.TryResolvePointingHand(spatial, command.target_hand, out HandRaySnapshot fallbackHand, out SpatialResolveResult fallbackResult))
            {
                towardSun = fallbackHand.direction.normalized;
                return towardSun.sqrMagnitude > 0.0001f;
            }

            LastFailureMessage = fallbackResult.message;
            return false;
        }
    }
}
