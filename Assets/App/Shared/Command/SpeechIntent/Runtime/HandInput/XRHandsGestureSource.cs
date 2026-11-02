using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Hands;

namespace SpeechIntent.HandInput
{
    public class XRHandsGestureSource : MonoBehaviour, IHandGestureSource
    {
        [SerializeField] float pinchEnterDistance = 0.025f;
        [SerializeField] float pinchExitDistance = 0.045f;
        [SerializeField, Range(0f, 1f)] float pinchValueEnter = 0.75f;
        [SerializeField, Range(0f, 1f)] float pinchValueExit = 0.45f;
        [SerializeField, Range(0f, 1f)] float gripValueEnter = 0.75f;
        [SerializeField, Range(0f, 1f)] float gripValueExit = 0.35f;
        [SerializeField] ThumbTapSettings thumbTapSettings = ThumbTapSettings.Default;

        readonly List<XRHandSubsystem> handSubsystems = new List<XRHandSubsystem>();

        XRHandSubsystem handSubsystem;
        bool leftPinchActive;
        bool rightPinchActive;
        bool leftGripActive;
        bool rightGripActive;
        ThumbTapState leftThumbTapState;
        ThumbTapState rightThumbTapState;
        float leftPreviousKnuckleDistance;
        float rightPreviousKnuckleDistance;
        bool leftHasPreviousKnuckleDistance;
        bool rightHasPreviousKnuckleDistance;
        bool loggedDerivedAim;

        void OnEnable()
        {
            ResolveRunningSubsystem();
        }

        public bool TryGetSnapshot(HandedInputSide side, out HandGestureSnapshot snapshot)
        {
            snapshot = new HandGestureSnapshot
            {
                side = side
            };

            if (!HasRunningSubsystem())
                return false;

            XRHand hand = side == HandedInputSide.Left ? handSubsystem.leftHand : handSubsystem.rightHand;
            snapshot.isTracked = hand.isTracked;

            if (!hand.isTracked)
            {
                ResetState(side);
                return true;
            }

            XRCommonHandGestures commonGestures = side == HandedInputSide.Left
                ? handSubsystem.leftHandCommonGestures
                : handSubsystem.rightHandCommonGestures;

            snapshot.hasAimPose = commonGestures.TryGetAimPose(out snapshot.aimPose);

            // Aim pose is the one gesture with no fallback, and hand pointing requires it: a runtime that
            // reports joints but no aim pose would silently disable pointing altogether. Derive a usable
            // ray from the index knuckle through the fingertip instead.
            if (!snapshot.hasAimPose && TryDeriveAimPose(hand, out Pose derivedAim))
            {
                snapshot.aimPose = derivedAim;
                snapshot.hasAimPose = true;
                if (!loggedDerivedAim)
                {
                    loggedDerivedAim = true;
                    Debug.Log("[XRHandsGestureSource] Runtime supplied no aim pose; deriving hand aim from " +
                              "the index knuckle and fingertip.", this);
                }
            }

            bool hasTipDistance = TryGetJointDistance(
                hand,
                XRHandJointID.ThumbTip,
                XRHandJointID.IndexTip,
                out snapshot.thumbIndexTipDistance);

            bool hasKnuckleDistance = TryGetThumbToIndexKnuckleDistance(hand, out snapshot.thumbIndexKnuckleDistance);

            bool hasPinchValue = commonGestures.TryGetPinchValue(out float pinchValue);
            pinchValue = Mathf.Clamp01(pinchValue);
            snapshot.pinchValue = hasPinchValue
                ? pinchValue
                : (hasTipDistance ? Mathf.InverseLerp(pinchExitDistance, pinchEnterDistance, snapshot.thumbIndexTipDistance) : 0f);

            bool previousPinchActive = GetPinchActive(side);
            bool wasPinchActive = previousPinchActive;
            bool pinchDown = hasPinchValue
                ? HandGestureMath.UpdateBinaryGestureFromValue(pinchValue, pinchValueEnter, pinchValueExit, ref wasPinchActive)
                : hasTipDistance && HandGestureMath.UpdateBinaryGesture(snapshot.thumbIndexTipDistance, pinchEnterDistance, pinchExitDistance, ref wasPinchActive);
            SetPinchActive(side, wasPinchActive);
            snapshot.pinchActive = wasPinchActive;
            snapshot.pinchDown = pinchDown;
            snapshot.pinchUp = previousPinchActive && !wasPinchActive;

            bool previousGripActive = GetGripActive(side);
            bool wasGripActive = previousGripActive;
            bool gripDown = commonGestures.TryGetGraspValue(out float graspValue) &&
                HandGestureMath.UpdateBinaryGestureFromValue(graspValue, gripValueEnter, gripValueExit, ref wasGripActive);
            SetGripActive(side, wasGripActive);
            snapshot.gripActive = wasGripActive;
            snapshot.gripDown = gripDown;
            snapshot.gripUp = previousGripActive && !wasGripActive;

            if (hasKnuckleDistance)
            {
                float previousKnuckleDistance = GetPreviousKnuckleDistance(side, snapshot.thumbIndexKnuckleDistance);
                ThumbTapState thumbTapState = GetThumbTapState(side);
                snapshot.thumbTapDown = HandGestureMath.UpdateThumbTap(
                    snapshot.thumbIndexKnuckleDistance,
                    previousKnuckleDistance,
                    Time.deltaTime,
                    thumbTapSettings,
                    ref thumbTapState);
                SetThumbTapState(side, thumbTapState);
                SetPreviousKnuckleDistance(side, snapshot.thumbIndexKnuckleDistance);
            }
            else
            {
                ClearPreviousKnuckleDistance(side);
            }

            return true;
        }

        bool HasRunningSubsystem()
        {
            return handSubsystem != null && handSubsystem.running || ResolveRunningSubsystem();
        }

        bool ResolveRunningSubsystem()
        {
            handSubsystems.Clear();
            SubsystemManager.GetSubsystems(handSubsystems);

            for (int i = 0; i < handSubsystems.Count; i++)
            {
                XRHandSubsystem candidate = handSubsystems[i];
                if (candidate != null && candidate.running)
                {
                    handSubsystem = candidate;
                    return true;
                }
            }

            handSubsystem = null;
            return false;
        }

        /// <summary>
        /// Builds an aim ray from the index knuckle through the fingertip, for runtimes that expose hand
        /// joints but no aim pose. Origin at the knuckle keeps the ray where a pointer is expected, rather
        /// than at the wrist.
        /// </summary>
        static bool TryDeriveAimPose(XRHand hand, out Pose pose)
        {
            pose = default;

            if (!hand.GetJoint(XRHandJointID.IndexProximal).TryGetPose(out Pose knuckle))
                return false;
            if (!hand.GetJoint(XRHandJointID.IndexTip).TryGetPose(out Pose tip))
                return false;

            Vector3 forward = tip.position - knuckle.position;
            if (forward.sqrMagnitude < 0.0001f)
                return false;

            pose = new Pose(knuckle.position, Quaternion.LookRotation(forward.normalized, Vector3.up));
            return true;
        }

        static bool TryGetJointDistance(
            XRHand hand,
            XRHandJointID firstJointId,
            XRHandJointID secondJointId,
            out float distance)
        {
            if (hand.GetJoint(firstJointId).TryGetPose(out Pose firstPose) &&
                hand.GetJoint(secondJointId).TryGetPose(out Pose secondPose))
            {
                distance = Vector3.Distance(firstPose.position, secondPose.position);
                return true;
            }

            distance = 0f;
            return false;
        }

        static bool TryGetThumbToIndexKnuckleDistance(XRHand hand, out float distance)
        {
            if (!hand.GetJoint(XRHandJointID.ThumbTip).TryGetPose(out Pose thumbTipPose))
            {
                distance = 0f;
                return false;
            }

            bool hasIntermediate = hand.GetJoint(XRHandJointID.IndexIntermediate).TryGetPose(out Pose intermediatePose);
            bool hasProximal = hand.GetJoint(XRHandJointID.IndexProximal).TryGetPose(out Pose proximalPose);
            if (!hasIntermediate && !hasProximal)
            {
                distance = 0f;
                return false;
            }

            Vector3 target = hasIntermediate && hasProximal
                ? (intermediatePose.position + proximalPose.position) * 0.5f
                : hasIntermediate ? intermediatePose.position : proximalPose.position;
            distance = Vector3.Distance(thumbTipPose.position, target);
            return true;
        }

        bool GetPinchActive(HandedInputSide side)
        {
            return side == HandedInputSide.Left ? leftPinchActive : rightPinchActive;
        }

        void SetPinchActive(HandedInputSide side, bool active)
        {
            if (side == HandedInputSide.Left)
                leftPinchActive = active;
            else
                rightPinchActive = active;
        }

        bool GetGripActive(HandedInputSide side)
        {
            return side == HandedInputSide.Left ? leftGripActive : rightGripActive;
        }

        void SetGripActive(HandedInputSide side, bool active)
        {
            if (side == HandedInputSide.Left)
                leftGripActive = active;
            else
                rightGripActive = active;
        }

        ThumbTapState GetThumbTapState(HandedInputSide side)
        {
            return side == HandedInputSide.Left ? leftThumbTapState : rightThumbTapState;
        }

        void SetThumbTapState(HandedInputSide side, ThumbTapState state)
        {
            if (side == HandedInputSide.Left)
                leftThumbTapState = state;
            else
                rightThumbTapState = state;
        }

        float GetPreviousKnuckleDistance(HandedInputSide side, float fallbackDistance)
        {
            if (side == HandedInputSide.Left)
                return leftHasPreviousKnuckleDistance ? leftPreviousKnuckleDistance : fallbackDistance;

            return rightHasPreviousKnuckleDistance ? rightPreviousKnuckleDistance : fallbackDistance;
        }

        void SetPreviousKnuckleDistance(HandedInputSide side, float distance)
        {
            if (side == HandedInputSide.Left)
            {
                leftPreviousKnuckleDistance = distance;
                leftHasPreviousKnuckleDistance = true;
            }
            else
            {
                rightPreviousKnuckleDistance = distance;
                rightHasPreviousKnuckleDistance = true;
            }
        }

        void ClearPreviousKnuckleDistance(HandedInputSide side)
        {
            if (side == HandedInputSide.Left)
                leftHasPreviousKnuckleDistance = false;
            else
                rightHasPreviousKnuckleDistance = false;
        }

        void ResetState(HandedInputSide side)
        {
            if (side == HandedInputSide.Left)
            {
                leftPinchActive = false;
                leftGripActive = false;
                leftThumbTapState = default;
                leftHasPreviousKnuckleDistance = false;
            }
            else
            {
                rightPinchActive = false;
                rightGripActive = false;
                rightThumbTapState = default;
                rightHasPreviousKnuckleDistance = false;
            }
        }
    }
}
