using UnityEngine;

namespace SpeechIntent.HandInput
{
    public class HandInputContextResolver
    {
        public bool TryResolve(HandGestureSnapshot snapshot, GameObject heldObject, out HandSemanticEvent semanticEvent)
        {
            semanticEvent = default;

            if (!snapshot.isTracked)
                return false;

            semanticEvent = new HandSemanticEvent
            {
                side = snapshot.side,
                heldObject = heldObject,
                pose = snapshot.hasAimPose ? snapshot.aimPose : Pose.identity
            };

            bool isHolding = heldObject != null;
            if (isHolding)
            {
                if (snapshot.thumbTapDown)
                {
                    semanticEvent.type = HandSemanticEventType.TriggerSelect;
                    return true;
                }

                if (snapshot.gripUp)
                {
                    semanticEvent.type = HandSemanticEventType.GripUp;
                    return true;
                }

                return false;
            }

            if (snapshot.pinchDown)
            {
                semanticEvent.type = HandSemanticEventType.SelectDown;
                return true;
            }

            if (snapshot.pinchUp)
            {
                semanticEvent.type = HandSemanticEventType.SelectUp;
                return true;
            }

            if (snapshot.gripDown)
            {
                semanticEvent.type = HandSemanticEventType.GripDown;
                return true;
            }

            return false;
        }
    }
}
