using SpeechIntent.HandInput;
using UnityEngine;

namespace SpeechIntent.Testing
{
    /// <summary>
    /// Deterministic scene-local harness for exercising gaze/hand pointing semantics without live XR input.
    /// It drives the same PointingSource/SpatialContextProvider/SceneFocusContext path used by runtime commands.
    /// </summary>
    public class HolodeckRigTestHarness : MonoBehaviour
    {
        [Header("Rigs")]
        public GameObject realMeRoot;
        public GameObject testMeRoot;
        public bool activateTestRigDisablesRealRig = true;
        public bool temporarilyNameTestRigMe = true;

        [Header("Test Rig Transforms")]
        public Transform testHead;
        public PointingSource testLeftHandSource;
        public PointingSource testRightHandSource;

        [Header("Runtime Systems")]
        public SpatialContextProvider spatialContextProvider;
        public SceneFocusContext focusContext;

        [Header("Optional Target Root")]
        public Transform targetSearchRoot;

        [Header("State")]
        [SerializeField] private bool isTestRigActive;
        [SerializeField] private bool leftSelectDown;
        [SerializeField] private bool rightSelectDown;

        string _realMeOriginalName;
        string _testMeOriginalName;
        bool _realMeWasActive;
        bool _testMeWasActive;
        Transform _previousHeadTransform;
        PointingSource _previousLeftHandSource;
        PointingSource _previousRightHandSource;

        public bool IsTestRigActive => isTestRigActive;
        public bool LeftSelectDown => leftSelectDown;
        public bool RightSelectDown => rightSelectDown;

        public void ActivateTestRig()
        {
            ResolveReferences();

            if (isTestRigActive)
                return;

            _realMeOriginalName = realMeRoot != null ? realMeRoot.name : "";
            _testMeOriginalName = testMeRoot != null ? testMeRoot.name : "";
            _realMeWasActive = realMeRoot != null && realMeRoot.activeSelf;
            _testMeWasActive = testMeRoot != null && testMeRoot.activeSelf;

            if (spatialContextProvider != null)
            {
                _previousHeadTransform = spatialContextProvider.headTransform;
                _previousLeftHandSource = spatialContextProvider.leftHandSource;
                _previousRightHandSource = spatialContextProvider.rightHandSource;
            }

            if (realMeRoot != null && activateTestRigDisablesRealRig)
            {
                if (temporarilyNameTestRigMe && string.Equals(realMeRoot.name, "Me", System.StringComparison.OrdinalIgnoreCase))
                    realMeRoot.name = _realMeOriginalName + "_DisabledForTest";
                realMeRoot.SetActive(false);
            }

            if (testMeRoot != null)
            {
                testMeRoot.SetActive(true);
                if (temporarilyNameTestRigMe)
                    testMeRoot.name = "Me";
            }

            WireSpatialProviderToTestRig();
            isTestRigActive = true;
        }

        public void RestoreRealRig()
        {
            if (!isTestRigActive)
                return;

            if (spatialContextProvider != null)
            {
                spatialContextProvider.headTransform = _previousHeadTransform;
                spatialContextProvider.leftHandSource = _previousLeftHandSource;
                spatialContextProvider.rightHandSource = _previousRightHandSource;
            }

            if (focusContext != null)
            {
                focusContext.SetHeldObject(HandedInputSide.Left, null);
                focusContext.SetHeldObject(HandedInputSide.Right, null);
            }

            if (testMeRoot != null)
            {
                if (!string.IsNullOrWhiteSpace(_testMeOriginalName))
                    testMeRoot.name = _testMeOriginalName;
                testMeRoot.SetActive(_testMeWasActive);
            }

            if (realMeRoot != null)
            {
                if (!string.IsNullOrWhiteSpace(_realMeOriginalName))
                    realMeRoot.name = _realMeOriginalName;
                realMeRoot.SetActive(_realMeWasActive);
            }

            leftSelectDown = false;
            rightSelectDown = false;
            isTestRigActive = false;
        }

        public void ApplyScenario(TestRigScenario scenario)
        {
            if (scenario == null)
            {
                Debug.LogWarning("[HolodeckRigTestHarness] ApplyScenario ignored: scenario is null.", this);
                return;
            }

            ActivateTestRig();

            if (testMeRoot != null)
            {
                testMeRoot.transform.position = scenario.rigWorldPosition;
                testMeRoot.transform.rotation = Quaternion.Euler(0f, scenario.rigYawDegrees, 0f);
            }

            if (testHead != null)
                testHead.localPosition = scenario.headLocalPosition;

            Transform leftOrigin = testLeftHandSource != null ? testLeftHandSource.Origin : null;
            if (leftOrigin != null)
                leftOrigin.localPosition = scenario.leftHandLocalPosition;

            Transform rightOrigin = testRightHandSource != null ? testRightHandSource.Origin : null;
            if (rightOrigin != null)
                rightOrigin.localPosition = scenario.rightHandLocalPosition;

            AimTransform(testHead, scenario.gaze);
            AimPointingSource(testLeftHandSource, scenario.leftHand);
            AimPointingSource(testRightHandSource, scenario.rightHand);

            ApplyHeldObject(scenario);
            leftSelectDown = scenario.leftSelectDown;
            rightSelectDown = scenario.rightSelectDown;
        }

        public SpatialSnapshot CaptureSnapshot()
        {
            ResolveReferences();
            return spatialContextProvider != null
                ? spatialContextProvider.CaptureSnapshot()
                : new SpatialSnapshot();
        }

        public void AimGazeAt(GameObject target, Vector3 offset = default)
        {
            ActivateTestRig();
            AimTransformAt(testHead, target, offset);
        }

        public void AimLeftHandAt(GameObject target, Vector3 offset = default)
        {
            ActivateTestRig();
            AimPointingSourceAt(testLeftHandSource, target, offset);
        }

        public void AimRightHandAt(GameObject target, Vector3 offset = default)
        {
            ActivateTestRig();
            AimPointingSourceAt(testRightHandSource, target, offset);
        }

        public void HoldObject(GameObject target, HandSelection hand = HandSelection.Right)
        {
            ActivateTestRig();
            if (focusContext == null)
                return;

            if (hand == HandSelection.Left || hand == HandSelection.Both)
                focusContext.SetHeldObject(HandedInputSide.Left, target);
            if (hand == HandSelection.Right || hand == HandSelection.Both || hand == HandSelection.Either || hand == HandSelection.None)
                focusContext.SetHeldObject(HandedInputSide.Right, target);
        }

        public void SetSelectState(bool leftDown, bool rightDown)
        {
            leftSelectDown = leftDown;
            rightSelectDown = rightDown;
        }

        void ResolveReferences()
        {
            if (testMeRoot == null)
                testMeRoot = gameObject;
            if (testHead == null)
                testHead = transform.Find("TestHead") ?? transform.Find("Head");
            if (testLeftHandSource == null)
                testLeftHandSource = FindNamedPointingSource("TestLeftHand", "Left");
            if (testRightHandSource == null)
                testRightHandSource = FindNamedPointingSource("TestRightHand", "Right");
            if (spatialContextProvider == null)
                spatialContextProvider = FindFirstObjectByType<SpatialContextProvider>(FindObjectsInactive.Include);
            if (focusContext == null)
                focusContext = FindFirstObjectByType<SceneFocusContext>(FindObjectsInactive.Include);
            if (realMeRoot == null)
                realMeRoot = GameObject.Find("Me");
        }

        PointingSource FindNamedPointingSource(string preferredName, string fallbackNameContains)
        {
            PointingSource[] sources = GetComponentsInChildren<PointingSource>(true);
            foreach (PointingSource source in sources)
            {
                if (source != null && source.name == preferredName)
                    return source;
            }

            foreach (PointingSource source in sources)
            {
                if (source != null && source.name.IndexOf(fallbackNameContains, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return source;
            }

            return null;
        }

        void WireSpatialProviderToTestRig()
        {
            if (spatialContextProvider == null)
                return;

            spatialContextProvider.headTransform = testHead;
            spatialContextProvider.leftHandSource = testLeftHandSource;
            spatialContextProvider.rightHandSource = testRightHandSource;
        }

        void AimPointingSource(PointingSource source, TestRigScenario.AimSpec aim)
        {
            if (source == null)
                return;

            source.SetState(aim.enabled, aim.enabled);
            if (!aim.enabled)
                return;

            GameObject target = FindTarget(aim.targetName);
            if (target != null)
            {
                AimPointingSourceAt(source, target, aim.targetOffset);
                return;
            }

            AimTransformTowardFallback(source.Origin, aim.fallbackDistanceMeters);
        }

        void AimPointingSourceAt(PointingSource source, GameObject target, Vector3 offset)
        {
            if (source == null)
                return;

            source.SetState(target != null, target != null);
            AimTransformAt(source.Origin, target, offset);
        }

        void AimTransform(Transform origin, TestRigScenario.AimSpec aim)
        {
            if (origin == null || !aim.enabled)
                return;

            GameObject target = FindTarget(aim.targetName);
            if (target != null)
                AimTransformAt(origin, target, aim.targetOffset);
            else
                AimTransformTowardFallback(origin, aim.fallbackDistanceMeters);
        }

        static void AimTransformAt(Transform origin, GameObject target, Vector3 offset)
        {
            if (origin == null || target == null)
                return;

            Vector3 targetPoint = ResolveAimPoint(target) + offset;
            Vector3 direction = targetPoint - origin.position;
            if (direction.sqrMagnitude <= 0.000001f)
                return;

            origin.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        }

        static void AimTransformTowardFallback(Transform origin, float distanceMeters)
        {
            if (origin == null)
                return;

            Vector3 targetPoint = origin.position + origin.forward * Mathf.Max(0.05f, distanceMeters);
            Vector3 direction = targetPoint - origin.position;
            if (direction.sqrMagnitude > 0.000001f)
                origin.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        }

        static Vector3 ResolveAimPoint(GameObject target)
        {
            Collider collider = target.GetComponentInChildren<Collider>(true);
            if (collider != null)
                return collider.bounds.center;

            Renderer renderer = target.GetComponentInChildren<Renderer>(true);
            if (renderer != null)
                return renderer.bounds.center;

            return target.transform.position;
        }

        void ApplyHeldObject(TestRigScenario scenario)
        {
            if (focusContext == null)
                return;

            focusContext.SetHeldObject(HandedInputSide.Left, null);
            focusContext.SetHeldObject(HandedInputSide.Right, null);

            GameObject held = FindTarget(scenario.heldObjectName);
            if (held == null)
                return;

            if (scenario.heldHand == HandSelection.Left || scenario.heldHand == HandSelection.Both)
                focusContext.SetHeldObject(HandedInputSide.Left, held);

            if (scenario.heldHand == HandSelection.Right ||
                scenario.heldHand == HandSelection.Both ||
                scenario.heldHand == HandSelection.Either ||
                scenario.heldHand == HandSelection.None)
                focusContext.SetHeldObject(HandedInputSide.Right, held);
        }

        GameObject FindTarget(string targetName)
        {
            if (string.IsNullOrWhiteSpace(targetName))
                return null;

            if (targetSearchRoot != null)
            {
                Transform[] children = targetSearchRoot.GetComponentsInChildren<Transform>(true);
                foreach (Transform child in children)
                {
                    if (child != null && string.Equals(child.name, targetName, System.StringComparison.OrdinalIgnoreCase))
                        return child.gameObject;
                }
            }

            GameObject direct = GameObject.Find(targetName);
            if (direct != null)
                return direct;

            foreach (GameObject candidate in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (candidate == null ||
                    candidate.hideFlags != HideFlags.None ||
                    !string.Equals(candidate.name, targetName, System.StringComparison.OrdinalIgnoreCase))
                    continue;

                return candidate;
            }

            return null;
        }
    }
}
