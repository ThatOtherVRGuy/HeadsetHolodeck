using UnityEngine;

namespace SpeechIntent.HandInput
{
    public static class HandGestureMath
    {
        public static bool UpdateBinaryGesture(float value, float enterThreshold, float exitThreshold, ref bool active)
        {
            bool wasActive = active;
            if (!active && value <= enterThreshold)
                active = true;
            else if (active && value >= exitThreshold)
                active = false;

            return !wasActive && active;
        }

        public static bool UpdateBinaryGestureFromValue(float value, float enterThreshold, float exitThreshold, ref bool active)
        {
            bool wasActive = active;
            if (!active && value >= enterThreshold)
                active = true;
            else if (active && value <= exitThreshold)
                active = false;

            return !wasActive && active;
        }

        public static bool UpdateThumbTap(
            float thumbToTargetDistance,
            float previousThumbToTargetDistance,
            float deltaTime,
            ThumbTapSettings settings,
            ref ThumbTapState state)
        {
            float safeDelta = Mathf.Max(0.0001f, deltaTime);
            if (state.hasTapped)
                state.secondsSinceLastTap += safeDelta;

            if (state.isContacting)
            {
                if (thumbToTargetDistance >= settings.releaseDistance)
                    state.isContacting = false;
                return false;
            }

            float approachSpeed = (previousThumbToTargetDistance - thumbToTargetDistance) / safeDelta;
            bool contacted = thumbToTargetDistance <= settings.contactDistance;
            bool fastEnough = approachSpeed >= settings.minApproachSpeed;
            bool debounceReady = !state.hasTapped || state.secondsSinceLastTap >= settings.minSecondsBetweenTaps;

            if (contacted && fastEnough && debounceReady)
            {
                state.isContacting = true;
                state.hasTapped = true;
                state.secondsSinceLastTap = 0f;
                return true;
            }

            return false;
        }
    }
}
