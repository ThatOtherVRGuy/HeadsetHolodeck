using System;
using UnityEngine;

namespace SpeechIntent.HandInput
{
    public enum HandedInputSide { Left, Right }

    public enum HandSemanticEventType
    {
        SelectDown,
        SelectUp,
        GripDown,
        GripUp,
        TriggerSelect
    }

    [Serializable]
    public struct ThumbTapSettings
    {
        public float contactDistance;
        public float releaseDistance;
        public float minApproachSpeed;
        public float minSecondsBetweenTaps;

        public static ThumbTapSettings Default => new ThumbTapSettings
        {
            contactDistance = 0.025f,
            releaseDistance = 0.045f,
            minApproachSpeed = 0.45f,
            minSecondsBetweenTaps = 0.16f
        };
    }

    [Serializable]
    public struct ThumbTapState
    {
        public bool isContacting;
        public bool hasTapped;
        public float secondsSinceLastTap;
    }

    public struct HandGestureSnapshot
    {
        public HandedInputSide side;
        public bool isTracked;
        public bool hasAimPose;
        public Pose aimPose;
        public bool pinchActive;
        public bool pinchDown;
        public bool pinchUp;
        public bool gripActive;
        public bool gripDown;
        public bool gripUp;
        public bool thumbTapDown;
        public float pinchValue;
        public float thumbIndexTipDistance;
        public float thumbIndexKnuckleDistance;
    }

    public struct HandSemanticEvent
    {
        public HandedInputSide side;
        public HandSemanticEventType type;
        public GameObject heldObject;
        public Pose pose;
    }
}
