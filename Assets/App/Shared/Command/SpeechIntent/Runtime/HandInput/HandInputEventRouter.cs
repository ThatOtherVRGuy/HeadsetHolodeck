using System;
using UnityEngine;

namespace SpeechIntent.HandInput
{
    public class HandInputEventRouter
    {
        public event Action<HandSemanticEvent> GlobalSelectFallback;
        public event Action<HandSemanticEvent> GlobalSelectDown;
        public event Action<HandSemanticEvent> GlobalSelectUp;
        public event Action<HandSemanticEvent> GripRequested;
        public event Action<HandSemanticEvent> GripReleased;

        /// <summary>
        /// Routes a semantic hand event to the most specific consumer available.
        /// Returns true when the event type was recognized and routed.
        /// </summary>
        public bool Route(HandSemanticEvent evt)
        {
            switch (evt.type)
            {
                case HandSemanticEventType.TriggerSelect:
                    if (TryHeldObjectConsume(evt))
                        return true;
                    GlobalSelectFallback?.Invoke(evt);
                    return true;
                case HandSemanticEventType.SelectDown:
                    GlobalSelectDown?.Invoke(evt);
                    return true;
                case HandSemanticEventType.SelectUp:
                    GlobalSelectUp?.Invoke(evt);
                    return true;
                case HandSemanticEventType.GripDown:
                    GripRequested?.Invoke(evt);
                    return true;
                case HandSemanticEventType.GripUp:
                    GripReleased?.Invoke(evt);
                    return true;
                default:
                    return false;
            }
        }

        static bool TryHeldObjectConsume(HandSemanticEvent evt)
        {
            GameObject held = evt.heldObject;
            if (held == null)
                return false;
            MonoBehaviour[] behaviours = held.GetComponentsInChildren<MonoBehaviour>(true);
            foreach (MonoBehaviour behaviour in behaviours)
            {
                if (behaviour is IHandTriggerConsumer consumer && consumer.TryConsumeHandTrigger(evt))
                    return true;
            }
            return false;
        }
    }
}
