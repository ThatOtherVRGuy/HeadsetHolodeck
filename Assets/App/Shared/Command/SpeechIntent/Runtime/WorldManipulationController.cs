using Holodeck.Save;
using SpeechIntent.HandInput;
using UnityEngine;
using UnityEngine.XR;

namespace SpeechIntent
{
    /// <summary>
    /// Voice/UI-armed direct manipulation of the currently loaded world. One hand
    /// preserves its grab offset; adding the other hand enables uniform scale plus
    /// midpoint translation and vector-based rotation.
    /// </summary>
    public class WorldManipulationController : MonoBehaviour
    {
        const string PositionSnapKey = "HeadsetHolodeck.World.PositionSnapMeters";
        const string AngleSnapKey = "HeadsetHolodeck.World.AngleSnapDegrees";
        const string DominantHandKey = "HeadsetHolodeck.World.DominantHand";
        const string SnapEnabledKey = "HeadsetHolodeck.TransformSnap.Enabled";

        [Header("Dependencies")]
        public InteractionMemory interactionMemory;
        public SpatialContextProvider spatialContextProvider;
        public HandOnlyInputCoordinator handOnlyInput;
        public WorldConfigAutoSave worldConfigAutoSave;

        [Header("Manipulation")]
        public HandedInputSide dominantHand = HandedInputSide.Right;
        [Min(0.01f)] public float minimumScaleFactor = 0.05f;
        [Min(0.05f)] public float maximumScaleFactor = 20f;
        public bool applySnappingToAllXrGrabInteractables = true;

        [Header("Snapping")]
        [Min(0f)] public float positionSnapMeters;
        [Min(0f)] public float angleSnapDegrees;
        public bool snapEnabled = true;

        bool _armed;
        bool _secondaryGrip;
        bool _controllerLeftGrip;
        bool _controllerRightGrip;
        Transform _target;
        bool _twoHanded;

        Pose _oneHandStart;
        Vector3 _oneHandStartPosition;
        Quaternion _oneHandStartRotation;

        Vector3 _twoHandStartMidpoint;
        Vector3 _twoHandStartVector;
        float _twoHandStartDistance;
        Vector3 _twoHandStartPosition;
        Quaternion _twoHandStartRotation;
        Vector3 _twoHandStartScale;

        public bool IsArmed => _armed;
        public bool IsTwoHanded => _twoHanded;
        public bool SnapEnabled => snapEnabled;
        public float PositionSnapMeters => snapEnabled ? positionSnapMeters : 0f;
        public float AngleSnapDegrees => snapEnabled ? angleSnapDegrees : 0f;
        public float ConfiguredPositionSnapMeters => positionSnapMeters;
        public float ConfiguredAngleSnapDegrees => angleSnapDegrees;

        void Awake()
        {
            ResolveDependencies();
            positionSnapMeters = Mathf.Max(0f, PlayerPrefs.GetFloat(PositionSnapKey, positionSnapMeters));
            angleSnapDegrees = Mathf.Max(0f, PlayerPrefs.GetFloat(AngleSnapKey, angleSnapDegrees));
            dominantHand = (HandedInputSide)PlayerPrefs.GetInt(DominantHandKey, (int)dominantHand);
            snapEnabled = PlayerPrefs.GetInt(SnapEnabledKey, snapEnabled ? 1 : 0) != 0;
        }

        void OnEnable()
        {
            ResolveDependencies();
            SubscribeHandEvents();
            if (applySnappingToAllXrGrabInteractables)
                AttachSnappingToExistingInteractables();
        }

        void OnDisable()
        {
            UnsubscribeHandEvents();
            if (_armed)
                ReleaseWorld(save: true);
        }

        void Update()
        {
            PollControllerGrips();
            if (!_armed)
                return;

            if (_target == null)
            {
                _target = ResolveWorldRoot();
                if (_target == null)
                    return;
            }

            if (!TryGetPose(dominantHand, out Pose dominantPose))
                return;

            HandedInputSide secondarySide = Opposite(dominantHand);
            Pose secondaryPose = default;
            bool wantsTwoHands = _secondaryGrip && TryGetPose(secondarySide, out secondaryPose);
            if (wantsTwoHands)
            {
                if (!_twoHanded)
                    BeginTwoHand(dominantPose, secondaryPose);
                ApplyTwoHand(dominantPose, secondaryPose);
            }
            else
            {
                if (_twoHanded)
                    BeginOneHand(dominantPose);
                else if (_oneHandStartRotation == default)
                    BeginOneHand(dominantPose);
                ApplyOneHand(dominantPose);
            }
        }

        public bool GrabWorld()
        {
            ResolveDependencies();
            _target = ResolveWorldRoot();
            if (_target == null)
            {
                ArchStatusBus.Warning("No world is loaded to grab.", "WORLD");
                return false;
            }

            _armed = true;
            _twoHanded = false;
            _secondaryGrip = CurrentGrip(Opposite(dominantHand));
            _oneHandStartRotation = default;
            if (TryGetPose(dominantHand, out Pose pose))
                BeginOneHand(pose);

            ArchStatusBus.Info($"World grabbed with {dominantHand.ToString().ToLowerInvariant()} hand.", "WORLD");
            return true;
        }

        public void ReleaseWorld(bool save = true)
        {
            Transform released = _target;
            _armed = false;
            _twoHanded = false;
            _secondaryGrip = false;
            _target = null;
            _oneHandStartRotation = default;

            if (save && released != null && worldConfigAutoSave != null)
            {
                worldConfigAutoSave.RegisterObjectMutation(new VoiceIntentCommand
                {
                    transcript = "grab world",
                    intent = VoiceIntentType.GrabWorld,
                    should_execute = true
                }, released.gameObject);
            }

            ArchStatusBus.Info("World released.", "WORLD");
        }

        public void SetPositionSnap(float meters)
        {
            positionSnapMeters = Mathf.Max(0f, meters);
            if (positionSnapMeters > 0f)
                snapEnabled = true;
            if (applySnappingToAllXrGrabInteractables)
                AttachSnappingToExistingInteractables();
            PlayerPrefs.SetFloat(PositionSnapKey, positionSnapMeters);
            PlayerPrefs.SetInt(SnapEnabledKey, snapEnabled ? 1 : 0);
            PlayerPrefs.Save();
        }

        public void SetAngleSnap(float degrees)
        {
            angleSnapDegrees = Mathf.Max(0f, degrees);
            if (angleSnapDegrees > 0f)
                snapEnabled = true;
            if (applySnappingToAllXrGrabInteractables)
                AttachSnappingToExistingInteractables();
            PlayerPrefs.SetFloat(AngleSnapKey, angleSnapDegrees);
            PlayerPrefs.SetInt(SnapEnabledKey, snapEnabled ? 1 : 0);
            PlayerPrefs.Save();
        }

        public void SetSnapEnabled(bool enabled)
        {
            snapEnabled = enabled;
            PlayerPrefs.SetInt(SnapEnabledKey, enabled ? 1 : 0);
            PlayerPrefs.Save();
        }

        public void SetDominantHand(HandedInputSide side)
        {
            if (dominantHand == side)
                return;

            dominantHand = side;
            PlayerPrefs.SetInt(DominantHandKey, (int)side);
            PlayerPrefs.Save();
            if (_armed)
                GrabWorld();
        }

        void BeginOneHand(Pose handPose)
        {
            if (_target == null)
                return;
            _twoHanded = false;
            _oneHandStart = handPose;
            _oneHandStartPosition = _target.position;
            _oneHandStartRotation = _target.rotation;
        }

        void ApplyOneHand(Pose handPose)
        {
            Quaternion deltaRotation = handPose.rotation * Quaternion.Inverse(_oneHandStart.rotation);
            Vector3 position = handPose.position + deltaRotation * (_oneHandStartPosition - _oneHandStart.position);
            Quaternion rotation = deltaRotation * _oneHandStartRotation;
            _target.SetPositionAndRotation(SnapPosition(position, positionSnapMeters), SnapRotation(rotation, angleSnapDegrees));
        }

        void BeginTwoHand(Pose dominantPose, Pose secondaryPose)
        {
            _twoHanded = true;
            _twoHandStartMidpoint = (dominantPose.position + secondaryPose.position) * 0.5f;
            _twoHandStartVector = secondaryPose.position - dominantPose.position;
            _twoHandStartDistance = Mathf.Max(0.001f, _twoHandStartVector.magnitude);
            _twoHandStartPosition = _target.position;
            _twoHandStartRotation = _target.rotation;
            _twoHandStartScale = _target.localScale;
        }

        void ApplyTwoHand(Pose dominantPose, Pose secondaryPose)
        {
            Vector3 midpoint = (dominantPose.position + secondaryPose.position) * 0.5f;
            Vector3 vector = secondaryPose.position - dominantPose.position;
            float distance = Mathf.Max(0.001f, vector.magnitude);
            float factor = Mathf.Clamp(distance / _twoHandStartDistance, minimumScaleFactor, maximumScaleFactor);
            Quaternion deltaRotation = Quaternion.FromToRotation(_twoHandStartVector, vector);

            Vector3 position = midpoint + deltaRotation * ((_twoHandStartPosition - _twoHandStartMidpoint) * factor);
            Quaternion rotation = deltaRotation * _twoHandStartRotation;
            _target.position = SnapPosition(position, positionSnapMeters);
            _target.rotation = SnapRotation(rotation, angleSnapDegrees);
            _target.localScale = _twoHandStartScale * factor;
        }

        void HandleGripRequested(HandSemanticEvent evt)
        {
            if (!_armed || evt.side == dominantHand)
                return;
            _secondaryGrip = true;
        }

        void HandleGripReleased(HandSemanticEvent evt)
        {
            if (evt.side == Opposite(dominantHand))
                _secondaryGrip = false;
        }

        void PollControllerGrips()
        {
            bool left = ReadControllerGrip(XRNode.LeftHand);
            bool right = ReadControllerGrip(XRNode.RightHand);
            if (_armed)
            {
                HandedInputSide secondary = Opposite(dominantHand);
                bool secondaryControllerGrip = secondary == HandedInputSide.Left ? left : right;
                bool wasSecondaryControllerGrip = secondary == HandedInputSide.Left ? _controllerLeftGrip : _controllerRightGrip;
                if (secondaryControllerGrip && !wasSecondaryControllerGrip)
                    _secondaryGrip = true;
                else if (!secondaryControllerGrip && wasSecondaryControllerGrip)
                    _secondaryGrip = false;
            }
            _controllerLeftGrip = left;
            _controllerRightGrip = right;
        }

        static bool ReadControllerGrip(XRNode node)
        {
            InputDevice device = InputDevices.GetDeviceAtXRNode(node);
            return device.isValid && device.TryGetFeatureValue(CommonUsages.gripButton, out bool pressed) && pressed;
        }

        bool CurrentGrip(HandedInputSide side)
        {
            if (side == HandedInputSide.Left)
                return _controllerLeftGrip || (handOnlyInput != null && handOnlyInput.LeftGripActive);
            return _controllerRightGrip || (handOnlyInput != null && handOnlyInput.RightGripActive);
        }

        bool TryGetPose(HandedInputSide side, out Pose pose)
        {
            PointingSource source = side == HandedInputSide.Left
                ? spatialContextProvider?.leftHandSource
                : spatialContextProvider?.rightHandSource;
            if (source != null && source.isAvailable && source.Origin != null)
            {
                pose = new Pose(source.Origin.position, source.Origin.rotation);
                return true;
            }

            pose = default;
            return false;
        }

        Transform ResolveWorldRoot()
        {
            return interactionMemory != null && interactionMemory.currentWorldRoot != null
                ? interactionMemory.currentWorldRoot.transform
                : null;
        }

        void ResolveDependencies()
        {
            interactionMemory ??= FindFirstObjectByType<InteractionMemory>(FindObjectsInactive.Include);
            spatialContextProvider ??= FindFirstObjectByType<SpatialContextProvider>(FindObjectsInactive.Include);
            handOnlyInput ??= FindFirstObjectByType<HandOnlyInputCoordinator>(FindObjectsInactive.Include);
            worldConfigAutoSave ??= FindFirstObjectByType<WorldConfigAutoSave>(FindObjectsInactive.Include);
        }

        void AttachSnappingToExistingInteractables()
        {
            UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable[] grabs =
                FindObjectsByType<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None);
            foreach (UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable grab in grabs)
            {
                if (grab != null && grab.GetComponent<SnapWhileGrabbed>() == null)
                    grab.gameObject.AddComponent<SnapWhileGrabbed>();
            }
        }

        void SubscribeHandEvents()
        {
            if (handOnlyInput == null)
                return;
            handOnlyInput.Router.GripRequested -= HandleGripRequested;
            handOnlyInput.Router.GripReleased -= HandleGripReleased;
            handOnlyInput.Router.GripRequested += HandleGripRequested;
            handOnlyInput.Router.GripReleased += HandleGripReleased;
        }

        void UnsubscribeHandEvents()
        {
            if (handOnlyInput == null)
                return;
            handOnlyInput.Router.GripRequested -= HandleGripRequested;
            handOnlyInput.Router.GripReleased -= HandleGripReleased;
        }

        static HandedInputSide Opposite(HandedInputSide side) =>
            side == HandedInputSide.Left ? HandedInputSide.Right : HandedInputSide.Left;

        public static Vector3 SnapPosition(Vector3 value, float increment)
        {
            if (increment <= 0f)
                return value;
            return new Vector3(
                Mathf.Round(value.x / increment) * increment,
                Mathf.Round(value.y / increment) * increment,
                Mathf.Round(value.z / increment) * increment);
        }

        public static Quaternion SnapRotation(Quaternion value, float incrementDegrees)
        {
            if (incrementDegrees <= 0f)
                return value;
            Vector3 euler = value.eulerAngles;
            euler.x = Mathf.Round(euler.x / incrementDegrees) * incrementDegrees;
            euler.y = Mathf.Round(euler.y / incrementDegrees) * incrementDegrees;
            euler.z = Mathf.Round(euler.z / incrementDegrees) * incrementDegrees;
            return Quaternion.Euler(euler);
        }
    }
}
