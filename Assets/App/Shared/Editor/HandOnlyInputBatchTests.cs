using System;
using SpeechIntent.HandInput;
using UnityEditor;
using UnityEngine;

namespace HeadsetHolodeck.EditorTests
{
    public static class HandOnlyInputBatchTests
    {
        public static void RunAll()
        {
            try
            {
                TestPinchUsesHysteresis();
                TestThumbTapRequiresApproachThenContact();
                TestThumbTapResetsAfterSeparation();
                TestEmptyHandPinchRoutesToSelect();
                TestHeldObjectThumbTapRoutesToTriggerSelect();
                TestHeldObjectPinchDoesNotReplaceThumbTapTrigger();
                TestHeldObjectConsumesTrigger();
                TestUnconsumedHeldTriggerFallsBackToGlobalSelect();
                Debug.Log("[HandOnlyInputBatchTests] All hand-only input tests passed.");
            }
            catch (Exception ex)
            {
                Debug.LogError("[HandOnlyInputBatchTests] Failed: " + ex);
                EditorApplication.Exit(1);
                throw;
            }
        }

        static void TestPinchUsesHysteresis()
        {
            bool active = false;
            bool down = HandGestureMath.UpdateBinaryGesture(0.018f, 0.025f, 0.04f, ref active);
            AssertTrue(down, "Expected pinch to begin under enter threshold.");
            AssertTrue(active, "Expected pinch to stay active.");

            bool secondDown = HandGestureMath.UpdateBinaryGesture(0.03f, 0.025f, 0.04f, ref active);
            AssertFalse(secondDown, "Expected no second down event while still inside exit threshold.");
            AssertTrue(active, "Expected pinch to remain active until exit threshold.");

            HandGestureMath.UpdateBinaryGesture(0.05f, 0.025f, 0.04f, ref active);
            AssertFalse(active, "Expected pinch to clear after exit threshold.");
        }

        static void TestThumbTapRequiresApproachThenContact()
        {
            ThumbTapState state = new ThumbTapState();
            ThumbTapSettings settings = ThumbTapSettings.Default;

            bool first = HandGestureMath.UpdateThumbTap(
                thumbToTargetDistance: 0.07f,
                previousThumbToTargetDistance: 0.08f,
                deltaTime: 0.016f,
                settings,
                ref state);

            AssertFalse(first, "Expected no tap while still outside contact distance.");

            bool tap = HandGestureMath.UpdateThumbTap(
                thumbToTargetDistance: 0.018f,
                previousThumbToTargetDistance: 0.05f,
                deltaTime: 0.016f,
                settings,
                ref state);

            AssertTrue(tap, "Expected tap after fast approach into contact distance.");
            AssertTrue(state.isContacting, "Expected tap state to mark contact.");
        }

        static void TestThumbTapResetsAfterSeparation()
        {
            ThumbTapState state = new ThumbTapState();
            ThumbTapSettings settings = ThumbTapSettings.Default;

            HandGestureMath.UpdateThumbTap(0.018f, 0.05f, 0.016f, settings, ref state);
            bool secondWhileContacting = HandGestureMath.UpdateThumbTap(0.017f, 0.018f, 0.016f, settings, ref state);
            AssertFalse(secondWhileContacting, "Expected no repeated tap while thumb remains in contact.");

            HandGestureMath.UpdateThumbTap(0.055f, 0.018f, 0.016f, settings, ref state);
            AssertFalse(state.isContacting, "Expected tap contact to clear after separation.");

            bool immediateSecondTap = HandGestureMath.UpdateThumbTap(0.018f, 0.055f, 0.016f, settings, ref state);
            AssertFalse(immediateSecondTap, "Expected debounce to block immediate second tap after separation.");

            HandGestureMath.UpdateThumbTap(0.055f, 0.018f, settings.minSecondsBetweenTaps, settings, ref state);
            bool secondTap = HandGestureMath.UpdateThumbTap(0.018f, 0.055f, 0.016f, settings, ref state);
            AssertTrue(secondTap, "Expected a second tap after separation and debounce expiry.");
        }

        static void TestEmptyHandPinchRoutesToSelect()
        {
            HandInputContextResolver resolver = new HandInputContextResolver();
            HandGestureSnapshot snapshot = new HandGestureSnapshot
            {
                side = HandedInputSide.Right,
                isTracked = true,
                pinchDown = true
            };

            AssertTrue(resolver.TryResolve(snapshot, null, out HandSemanticEvent evt), "Expected pinch down to resolve.");
            AssertEqual(HandSemanticEventType.SelectDown, evt.type, "Expected empty-hand pinch to become SelectDown.");
        }

        static void TestHeldObjectThumbTapRoutesToTriggerSelect()
        {
            HandInputContextResolver resolver = new HandInputContextResolver();
            GameObject held = new GameObject("Gun");
            try
            {
                HandGestureSnapshot snapshot = new HandGestureSnapshot
                {
                    side = HandedInputSide.Right,
                    isTracked = true,
                    thumbTapDown = true
                };

                AssertTrue(resolver.TryResolve(snapshot, held, out HandSemanticEvent evt), "Expected held thumb tap to resolve.");
                AssertEqual(HandSemanticEventType.TriggerSelect, evt.type, "Expected held thumb tap to become TriggerSelect.");
                AssertSame(held, evt.heldObject, "Expected held object to be preserved.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(held);
            }
        }

        static void TestHeldObjectPinchDoesNotReplaceThumbTapTrigger()
        {
            HandInputContextResolver resolver = new HandInputContextResolver();
            GameObject held = new GameObject("Tool");
            try
            {
                HandGestureSnapshot snapshot = new HandGestureSnapshot
                {
                    side = HandedInputSide.Right,
                    isTracked = true,
                    pinchDown = true
                };

                AssertFalse(resolver.TryResolve(snapshot, held, out _), "Expected held-object pinch not to trigger use.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(held);
            }
        }

        class TestTriggerConsumer : MonoBehaviour, IHandTriggerConsumer
        {
            public bool consume;
            public int callCount;

            public bool TryConsumeHandTrigger(HandSemanticEvent evt)
            {
                callCount++;
                return consume;
            }
        }

        static void TestHeldObjectConsumesTrigger()
        {
            GameObject held = new GameObject("Gun");
            try
            {
                TestTriggerConsumer consumer = held.AddComponent<TestTriggerConsumer>();
                consumer.consume = true;
                HandInputEventRouter router = new HandInputEventRouter();
                int fallbackCount = 0;
                router.GlobalSelectFallback += _ => fallbackCount++;

                bool consumed = router.Route(new HandSemanticEvent
                {
                    type = HandSemanticEventType.TriggerSelect,
                    side = HandedInputSide.Right,
                    heldObject = held
                });

                AssertTrue(consumed, "Expected held object to consume trigger.");
                AssertEqual(1, consumer.callCount, "Expected consumer to be called once.");
                AssertEqual(0, fallbackCount, "Expected no fallback when consumed.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(held);
            }
        }

        static void TestUnconsumedHeldTriggerFallsBackToGlobalSelect()
        {
            GameObject held = new GameObject("Cube");
            try
            {
                TestTriggerConsumer consumer = held.AddComponent<TestTriggerConsumer>();
                consumer.consume = false;
                HandInputEventRouter router = new HandInputEventRouter();
                int fallbackCount = 0;
                router.GlobalSelectFallback += _ => fallbackCount++;

            bool handled = router.Route(new HandSemanticEvent
            {
                type = HandSemanticEventType.TriggerSelect,
                side = HandedInputSide.Right,
                heldObject = held
            });

            AssertTrue(handled, "Expected unconsumed trigger to be handled by fallback.");
            AssertEqual(1, consumer.callCount, "Expected consumer to be called once.");
            AssertEqual(1, fallbackCount, "Expected fallback global select.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(held);
            }
        }

        static void AssertEqual<T>(T expected, T actual, string message)
        {
            if (!Equals(expected, actual))
                throw new Exception(message + $" Expected '{expected}', got '{actual}'.");
        }

        static void AssertSame(UnityEngine.Object expected, UnityEngine.Object actual, string message)
        {
            if (!ReferenceEquals(expected, actual))
                throw new Exception(message);
        }

        static void AssertTrue(bool value, string message)
        {
            if (!value) throw new Exception(message);
        }

        static void AssertFalse(bool value, string message)
        {
            if (value) throw new Exception(message);
        }
    }
}
