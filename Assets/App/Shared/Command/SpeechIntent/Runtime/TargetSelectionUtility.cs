using UnityEngine;

namespace SpeechIntent
{
    public static class TargetSelectionUtility
    {
        public static bool IsNonActionableSupportTarget(GameObject target)
        {
            if (target == null)
                return false;

            if (target.GetComponentInParent<SpeechIntentTrackable>() != null)
                return false;

            Transform current = target.transform;
            while (current != null)
            {
                string normalized = NormalizeName(current.name);
                if (normalized.Contains("floorcollider") ||
                    normalized.Contains("defaultfloorcollider") ||
                    normalized.Contains("passthroughfloorcollider"))
                {
                    return true;
                }

                current = current.parent;
            }

            return false;
        }

        public static bool TryGetSortedHits(
            Ray ray,
            float maxDistance,
            LayerMask mask,
            QueryTriggerInteraction triggerInteraction,
            out RaycastHit[] hits)
        {
            hits = Physics.RaycastAll(ray, maxDistance, mask, triggerInteraction);
            if (hits == null || hits.Length == 0)
                return false;

            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            return true;
        }

        public static bool TryGetFirstActionableHit(RaycastHit[] hits, out RaycastHit hit)
        {
            hit = default;
            if (hits == null || hits.Length == 0)
                return false;

            for (int i = 0; i < hits.Length; i++)
            {
                GameObject candidate = hits[i].collider != null ? hits[i].collider.gameObject : null;
                if (IsNonActionableSupportTarget(candidate))
                    continue;

                hit = hits[i];
                return true;
            }

            return false;
        }

        public static bool TryGetFirstActionableHit(
            Ray ray,
            float maxDistance,
            LayerMask mask,
            QueryTriggerInteraction triggerInteraction,
            out RaycastHit hit)
        {
            hit = default;
            return TryGetSortedHits(ray, maxDistance, mask, triggerInteraction, out RaycastHit[] hits) &&
                   TryGetFirstActionableHit(hits, out hit);
        }

        static string NormalizeName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";

            string lower = value.Trim().ToLowerInvariant();
            return lower
                .Replace(" ", "")
                .Replace("_", "")
                .Replace("-", "");
        }
    }
}
