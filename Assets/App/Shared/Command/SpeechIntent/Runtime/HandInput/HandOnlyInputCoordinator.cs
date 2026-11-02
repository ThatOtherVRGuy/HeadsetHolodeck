using UnityEngine;

namespace SpeechIntent.HandInput
{
    public class HandOnlyInputCoordinator : MonoBehaviour
    {
        public MonoBehaviour gestureSourceBehaviour;
        public HandPointingSourceDriver leftPointingDriver;
        public HandPointingSourceDriver rightPointingDriver;
        public SceneFocusContext focusContext;
        public bool logDebugMessages = true;

        readonly HandInputContextResolver _resolver = new HandInputContextResolver();
        readonly HandInputEventRouter _router = new HandInputEventRouter();
        IHandGestureSource _gestureSource;
        GameObject _leftHeldObject;
        GameObject _rightHeldObject;
        HandGestureSnapshot _leftSnapshot;
        HandGestureSnapshot _rightSnapshot;
        bool _hasLeftSnapshot;
        bool _hasRightSnapshot;

        public HandInputEventRouter Router => _router;
        public bool LeftGripActive => _hasLeftSnapshot && _leftSnapshot.isTracked && _leftSnapshot.gripActive;
        public bool RightGripActive => _hasRightSnapshot && _rightSnapshot.isTracked && _rightSnapshot.gripActive;

        void Awake()
        {
            _gestureSource = gestureSourceBehaviour as IHandGestureSource;
            if (_gestureSource == null)
                _gestureSource = GetComponent<IHandGestureSource>();
        }

        void Update()
        {
            if (_gestureSource == null)
                return;

            ProcessHand(HandedInputSide.Left, leftPointingDriver, _leftHeldObject);
            ProcessHand(HandedInputSide.Right, rightPointingDriver, _rightHeldObject);
        }

        public void SetHeldObject(HandedInputSide side, GameObject heldObject)
        {
            if (side == HandedInputSide.Left)
                _leftHeldObject = heldObject;
            else
                _rightHeldObject = heldObject;

            focusContext?.SetHeldObject(side, heldObject);
        }

        void ProcessHand(HandedInputSide side, HandPointingSourceDriver pointingDriver, GameObject heldObject)
        {
            if (!_gestureSource.TryGetSnapshot(side, out HandGestureSnapshot snapshot))
                return;

            if (side == HandedInputSide.Left)
            {
                _leftSnapshot = snapshot;
                _hasLeftSnapshot = true;
            }
            else
            {
                _rightSnapshot = snapshot;
                _hasRightSnapshot = true;
            }

            pointingDriver?.ApplySnapshot(snapshot);

            if (_resolver.TryResolve(snapshot, heldObject, out HandSemanticEvent evt))
            {
                bool routed = _router.Route(evt);
                if (logDebugMessages)
                    Debug.Log($"[HandOnlyInput] {side} {evt.type} held={heldObject?.name ?? "none"} routed={routed}", this);
            }
        }
    }
}
