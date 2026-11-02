using System;
using UnityEngine;

namespace SpeechIntent.Testing
{
    [CreateAssetMenu(fileName = "TestRigScenario", menuName = "Speech Intent/Testing/Test Rig Scenario")]
    public class TestRigScenario : ScriptableObject
    {
        public string scenarioName = "Test Rig Scenario";

        [Header("Rig Pose")]
        public Vector3 rigWorldPosition = Vector3.zero;
        public float rigYawDegrees;
        public Vector3 headLocalPosition = new Vector3(0f, 1.65f, 0f);
        public Vector3 leftHandLocalPosition = new Vector3(-0.35f, 1.25f, 0.35f);
        public Vector3 rightHandLocalPosition = new Vector3(0.35f, 1.25f, 0.35f);

        [Header("Semantic Aiming")]
        public AimSpec gaze = AimSpec.Disabled;
        public AimSpec leftHand = AimSpec.Disabled;
        public AimSpec rightHand = AimSpec.Disabled;

        [Header("Held Object")]
        public string heldObjectName = "";
        public HandSelection heldHand = HandSelection.Right;

        [Header("Input State")]
        public bool leftSelectDown;
        public bool rightSelectDown;

        [Serializable]
        public struct AimSpec
        {
            public bool enabled;
            public string targetName;
            public Vector3 targetOffset;
            [Min(0.05f)] public float fallbackDistanceMeters;

            public static AimSpec Disabled => new AimSpec
            {
                enabled = false,
                targetName = "",
                targetOffset = Vector3.zero,
                fallbackDistanceMeters = 2f
            };
        }
    }
}
