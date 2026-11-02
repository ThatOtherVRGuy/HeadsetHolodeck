# Hand-only Input Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add no-controller hand-only select/grip/use support, including held-object thumb-tap trigger behavior that falls back to normal select when the held object does not consume it.

**Architecture:** Implement a small hand-input stack under `SpeechIntent`: pure gesture math, runtime XR Hands sampling, context resolution, semantic event routing, and pointing-source updates. Keep controller input untouched and make hand-only behavior additive, testable, and tunable from the Inspector.

**Tech Stack:** Unity 6000.2.10f1, C#, Unity XR Hands 1.7.3, XR Interaction Toolkit 3.5.0, Unity Editor batch tests.

---

## File structure

- Create `Assets/App/Command/SpeechIntent/Runtime/HandInput/HandInputTypes.cs`
  - Small enums/structs/delegates shared by the hand-input stack.
- Create `Assets/App/Command/SpeechIntent/Runtime/HandInput/HandGestureMath.cs`
  - Pure static gesture calculations; no Unity XR subsystem dependency.
- Create `Assets/App/Command/SpeechIntent/Runtime/HandInput/IHandGestureSource.cs`
  - Interface for anything that provides per-hand gesture snapshots.
- Create `Assets/App/Command/SpeechIntent/Runtime/HandInput/XRHandsGestureSource.cs`
  - Runtime Unity XR Hands sampler that converts OpenXR/XRHand data into `HandGestureSnapshot`.
- Create `Assets/App/Command/SpeechIntent/Runtime/HandInput/HandInputContextResolver.cs`
  - Converts raw gestures into semantic events based on held-object context.
- Create `Assets/App/Command/SpeechIntent/Runtime/HandInput/IHandTriggerConsumer.cs`
  - Optional interface for held objects/tools that can consume trigger/use.
- Create `Assets/App/Command/SpeechIntent/Runtime/HandInput/HandInputEventRouter.cs`
  - Dispatches semantic events to held object first, then global select fallback.
- Create `Assets/App/Command/SpeechIntent/Runtime/HandInput/HandPointingSourceDriver.cs`
  - Updates existing `PointingSource` transforms/state from hand tracking.
- Create `Assets/App/Command/SpeechIntent/Runtime/HandInput/HandOnlyInputCoordinator.cs`
  - Scene-facing coordinator that connects source → resolver → router → pointing source.
- Create `Assets/App/Editor/HandOnlyInputBatchTests.cs`
  - Batch tests for gesture math, context resolution, and routing.
- Modify `Assets/App/Editor/SpeechIntentSceneSetup.cs`
  - Add optional scene wiring for `HandOnlyInputCoordinator` and per-hand pointing drivers.
- Modify `Assets/Scenes/Holodeck.unity`
  - Wire the coordinator and references after tests pass.

## Task 1: Pure gesture data and math

**Files:**
- Create: `Assets/App/Command/SpeechIntent/Runtime/HandInput/HandInputTypes.cs`
- Create: `Assets/App/Command/SpeechIntent/Runtime/HandInput/HandGestureMath.cs`
- Create: `Assets/App/Editor/HandOnlyInputBatchTests.cs`

- [ ] **Step 1: Create the failing tests**

Create `Assets/App/Editor/HandOnlyInputBatchTests.cs` with:

```csharp
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

            bool secondTap = HandGestureMath.UpdateThumbTap(0.018f, 0.055f, 0.016f, settings, ref state);
            AssertTrue(secondTap, "Expected a second tap after separation and re-contact.");
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
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```bash
/Applications/Unity/Hub/Editor/6000.2.10f1/Unity.app/Contents/MacOS/Unity -batchmode -quit -projectPath /Users/davidarendash/Documents/Projects/Unity/HeadsetHolodeckDev -executeMethod HeadsetHolodeck.EditorTests.HandOnlyInputBatchTests.RunAll
```

Expected: compile failure because `SpeechIntent.HandInput`, `HandGestureMath`, `ThumbTapState`, and `ThumbTapSettings` do not exist.

- [ ] **Step 3: Add shared hand-input types**

Create `Assets/App/Command/SpeechIntent/Runtime/HandInput/HandInputTypes.cs`:

```csharp
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
```

- [ ] **Step 4: Add pure gesture math**

Create `Assets/App/Command/SpeechIntent/Runtime/HandInput/HandGestureMath.cs`:

```csharp
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
            bool debounceReady = state.secondsSinceLastTap >= settings.minSecondsBetweenTaps;

            if (contacted && fastEnough && debounceReady)
            {
                state.isContacting = true;
                state.secondsSinceLastTap = 0f;
                return true;
            }

            return false;
        }
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run the same Unity command from Step 2.

Expected: `[HandOnlyInputBatchTests] All hand-only input tests passed.`

- [ ] **Step 6: Commit**

```bash
git add Assets/App/Command/SpeechIntent/Runtime/HandInput/HandInputTypes.cs Assets/App/Command/SpeechIntent/Runtime/HandInput/HandGestureMath.cs Assets/App/Editor/HandOnlyInputBatchTests.cs
git commit -m "Add hand gesture math"
```

## Task 2: Context resolver

**Files:**
- Modify: `Assets/App/Editor/HandOnlyInputBatchTests.cs`
- Create: `Assets/App/Command/SpeechIntent/Runtime/HandInput/HandInputContextResolver.cs`

- [ ] **Step 1: Add failing resolver tests**

Append these methods to `HandOnlyInputBatchTests`, call them from `RunAll()` after the Task 1 tests:

```csharp
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
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```bash
/Applications/Unity/Hub/Editor/6000.2.10f1/Unity.app/Contents/MacOS/Unity -batchmode -quit -projectPath /Users/davidarendash/Documents/Projects/Unity/HeadsetHolodeckDev -executeMethod HeadsetHolodeck.EditorTests.HandOnlyInputBatchTests.RunAll
```

Expected: compile failure because `HandInputContextResolver` does not exist.

- [ ] **Step 3: Implement resolver**

Create `Assets/App/Command/SpeechIntent/Runtime/HandInput/HandInputContextResolver.cs`:

```csharp
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
```

- [ ] **Step 4: Run tests to verify they pass**

Run the Unity command from Step 2.

Expected: hand-only tests pass.

- [ ] **Step 5: Commit**

```bash
git add Assets/App/Command/SpeechIntent/Runtime/HandInput/HandInputContextResolver.cs Assets/App/Editor/HandOnlyInputBatchTests.cs
git commit -m "Add hand input context resolver"
```

## Task 3: Trigger consumer interface and event router

**Files:**
- Modify: `Assets/App/Editor/HandOnlyInputBatchTests.cs`
- Create: `Assets/App/Command/SpeechIntent/Runtime/HandInput/IHandTriggerConsumer.cs`
- Create: `Assets/App/Command/SpeechIntent/Runtime/HandInput/HandInputEventRouter.cs`

- [ ] **Step 1: Add failing router tests**

Append these methods to `HandOnlyInputBatchTests`, call them from `RunAll()`:

```csharp
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

        bool handled = router.Route(new HandSemanticEvent
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

        bool consumed = router.Route(new HandSemanticEvent
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
```

- [ ] **Step 2: Run tests to verify they fail**

Run the hand-only Unity batch command.

Expected: compile failure because `IHandTriggerConsumer` and `HandInputEventRouter` do not exist.

- [ ] **Step 3: Add optional trigger consumer interface**

Create `Assets/App/Command/SpeechIntent/Runtime/HandInput/IHandTriggerConsumer.cs`:

```csharp
namespace SpeechIntent.HandInput
{
    public interface IHandTriggerConsumer
    {
        bool TryConsumeHandTrigger(HandSemanticEvent evt);
    }
}
```

- [ ] **Step 4: Add event router**

Create `Assets/App/Command/SpeechIntent/Runtime/HandInput/HandInputEventRouter.cs`:

```csharp
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
```

- [ ] **Step 5: Run tests to verify they pass**

Run the hand-only Unity batch command.

Expected: hand-only tests pass.

- [ ] **Step 6: Commit**

```bash
git add Assets/App/Command/SpeechIntent/Runtime/HandInput/IHandTriggerConsumer.cs Assets/App/Command/SpeechIntent/Runtime/HandInput/HandInputEventRouter.cs Assets/App/Editor/HandOnlyInputBatchTests.cs
git commit -m "Add hand trigger routing"
```

## Task 4: XR Hands gesture source

**Files:**
- Create: `Assets/App/Command/SpeechIntent/Runtime/HandInput/IHandGestureSource.cs`
- Create: `Assets/App/Command/SpeechIntent/Runtime/HandInput/XRHandsGestureSource.cs`

- [ ] **Step 1: Create gesture-source interface**

Create `Assets/App/Command/SpeechIntent/Runtime/HandInput/IHandGestureSource.cs`:

```csharp
namespace SpeechIntent.HandInput
{
    public interface IHandGestureSource
    {
        bool TryGetSnapshot(HandedInputSide side, out HandGestureSnapshot snapshot);
    }
}
```

- [ ] **Step 2: Add XR Hands sampler**

Create `Assets/App/Command/SpeechIntent/Runtime/HandInput/XRHandsGestureSource.cs`:

```csharp
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Hands;

namespace SpeechIntent.HandInput
{
    public class XRHandsGestureSource : MonoBehaviour, IHandGestureSource
    {
        [Header("Pinch")]
        public float pinchEnterDistance = 0.025f;
        public float pinchExitDistance = 0.045f;
        [Range(0f, 1f)] public float pinchValueEnter = 0.75f;
        [Range(0f, 1f)] public float pinchValueExit = 0.45f;

        [Header("Grip")]
        [Range(0f, 1f)] public float gripValueEnter = 0.75f;
        [Range(0f, 1f)] public float gripValueExit = 0.35f;

        [Header("Thumb Tap")]
        public ThumbTapSettings thumbTapSettings = ThumbTapSettings.Default;

        XRHandSubsystem _subsystem;
        bool _leftPinch;
        bool _rightPinch;
        bool _leftGrip;
        bool _rightGrip;
        ThumbTapState _leftTap;
        ThumbTapState _rightTap;
        float _leftPreviousKnuckleDistance = 1f;
        float _rightPreviousKnuckleDistance = 1f;

        void OnEnable()
        {
            ResolveSubsystem();
        }

        public bool TryGetSnapshot(HandedInputSide side, out HandGestureSnapshot snapshot)
        {
            snapshot = default;
            if (_subsystem == null || !_subsystem.running)
                ResolveSubsystem();
            if (_subsystem == null || !_subsystem.running)
                return false;

            XRHand hand = side == HandedInputSide.Left ? _subsystem.leftHand : _subsystem.rightHand;
            snapshot.side = side;
            snapshot.isTracked = hand.isTracked;
            if (!hand.isTracked)
                return true;

            bool pinchActive = side == HandedInputSide.Left ? _leftPinch : _rightPinch;
            bool gripActive = side == HandedInputSide.Left ? _leftGrip : _rightGrip;
            ThumbTapState tapState = side == HandedInputSide.Left ? _leftTap : _rightTap;
            float previousKnuckleDistance = side == HandedInputSide.Left ? _leftPreviousKnuckleDistance : _rightPreviousKnuckleDistance;

            snapshot.hasAimPose = TryGetAimPose(side, out snapshot.aimPose);
            snapshot.pinchValue = TryGetPinchValue(side, out float pinchValue) ? pinchValue : 0f;

            bool hasThumbTip = TryGetJointPosition(hand, XRHandJointID.ThumbTip, out Vector3 thumbTip);
            bool hasIndexTip = TryGetJointPosition(hand, XRHandJointID.IndexTip, out Vector3 indexTip);
            bool hasIndexIntermediate = TryGetJointPosition(hand, XRHandJointID.IndexIntermediate, out Vector3 indexIntermediate);
            bool hasIndexProximal = TryGetJointPosition(hand, XRHandJointID.IndexProximal, out Vector3 indexProximal);

            snapshot.thumbIndexTipDistance = hasThumbTip && hasIndexTip ? Vector3.Distance(thumbTip, indexTip) : 1f;
            Vector3 knuckleTarget = hasIndexIntermediate && hasIndexProximal
                ? (indexIntermediate + indexProximal) * 0.5f
                : hasIndexIntermediate ? indexIntermediate : indexProximal;
            snapshot.thumbIndexKnuckleDistance = hasThumbTip && (hasIndexIntermediate || hasIndexProximal)
                ? Vector3.Distance(thumbTip, knuckleTarget)
                : 1f;

            bool pinchDown;
            if (pinchValue > 0f)
            {
                pinchDown = HandGestureMath.UpdateBinaryGestureFromValue(
                    pinchValue,
                    pinchValueEnter,
                    pinchValueExit,
                    ref pinchActive);
            }
            else
            {
                pinchDown = HandGestureMath.UpdateBinaryGesture(
                    snapshot.thumbIndexTipDistance,
                    pinchEnterDistance,
                    pinchExitDistance,
                    ref pinchActive);
            }

            bool gripDown = false;
            float gripValue = TryGetGripValue(side, out float resolvedGrip) ? resolvedGrip : 0f;
            if (gripValue > 0f)
            {
                gripDown = HandGestureMath.UpdateBinaryGestureFromValue(
                    gripValue,
                    gripValueEnter,
                    gripValueExit,
                    ref gripActive);
            }

            bool thumbTapDown = HandGestureMath.UpdateThumbTap(
                snapshot.thumbIndexKnuckleDistance,
                previousKnuckleDistance,
                Time.deltaTime,
                thumbTapSettings,
                ref tapState);

            snapshot.pinchActive = pinchActive;
            snapshot.pinchDown = pinchDown;
            snapshot.pinchUp = !pinchActive && (side == HandedInputSide.Left ? _leftPinch : _rightPinch);
            snapshot.gripActive = gripActive;
            snapshot.gripDown = gripDown;
            snapshot.gripUp = !gripActive && (side == HandedInputSide.Left ? _leftGrip : _rightGrip);
            snapshot.thumbTapDown = thumbTapDown;

            if (side == HandedInputSide.Left)
            {
                _leftPinch = pinchActive;
                _leftGrip = gripActive;
                _leftTap = tapState;
                _leftPreviousKnuckleDistance = snapshot.thumbIndexKnuckleDistance;
            }
            else
            {
                _rightPinch = pinchActive;
                _rightGrip = gripActive;
                _rightTap = tapState;
                _rightPreviousKnuckleDistance = snapshot.thumbIndexKnuckleDistance;
            }

            return true;
        }

        void ResolveSubsystem()
        {
            List<XRHandSubsystem> subsystems = new List<XRHandSubsystem>();
            SubsystemManager.GetSubsystems(subsystems);
            _subsystem = null;
            foreach (XRHandSubsystem subsystem in subsystems)
            {
                if (subsystem != null && subsystem.running)
                {
                    _subsystem = subsystem;
                    break;
                }
            }
        }

        bool TryGetAimPose(HandedInputSide side, out Pose pose)
        {
            XRCommonHandGestures gestures = side == HandedInputSide.Left
                ? _subsystem.leftHandCommonGestures
                : _subsystem.rightHandCommonGestures;
            return gestures.TryGetAimPose(out pose);
        }

        bool TryGetPinchValue(HandedInputSide side, out float value)
        {
            XRCommonHandGestures gestures = side == HandedInputSide.Left
                ? _subsystem.leftHandCommonGestures
                : _subsystem.rightHandCommonGestures;
            return gestures.TryGetPinchValue(out value);
        }

        bool TryGetGripValue(HandedInputSide side, out float value)
        {
            XRCommonHandGestures gestures = side == HandedInputSide.Left
                ? _subsystem.leftHandCommonGestures
                : _subsystem.rightHandCommonGestures;
            return gestures.TryGetGraspValue(out value);
        }

        static bool TryGetJointPosition(XRHand hand, XRHandJointID id, out Vector3 position)
        {
            XRHandJoint joint = hand.GetJoint(id);
            if (joint.TryGetPose(out Pose pose))
            {
                position = pose.position;
                return true;
            }

            position = default;
            return false;
        }
    }
}
```

- [ ] **Step 3: Compile-check**

Run:

```bash
/Applications/Unity/Hub/Editor/6000.2.10f1/Unity.app/Contents/MacOS/Unity -batchmode -quit -projectPath /Users/davidarendash/Documents/Projects/Unity/HeadsetHolodeckDev -executeMethod HeadsetHolodeck.EditorTests.HandOnlyInputBatchTests.RunAll
```

Expected: hand-only tests pass and no compile errors.

- [ ] **Step 4: Commit**

```bash
git add Assets/App/Command/SpeechIntent/Runtime/HandInput/IHandGestureSource.cs Assets/App/Command/SpeechIntent/Runtime/HandInput/XRHandsGestureSource.cs
git commit -m "Add XR Hands gesture source"
```

## Task 5: Pointing source driver and coordinator

**Files:**
- Create: `Assets/App/Command/SpeechIntent/Runtime/HandInput/HandPointingSourceDriver.cs`
- Create: `Assets/App/Command/SpeechIntent/Runtime/HandInput/HandOnlyInputCoordinator.cs`

- [ ] **Step 1: Add pointing-source driver**

Create `Assets/App/Command/SpeechIntent/Runtime/HandInput/HandPointingSourceDriver.cs`:

```csharp
using UnityEngine;

namespace SpeechIntent.HandInput
{
    public class HandPointingSourceDriver : MonoBehaviour
    {
        public PointingSource pointingSource;
        public Transform rayTransform;
        public bool requireAimPose = true;

        public void ApplySnapshot(HandGestureSnapshot snapshot)
        {
            if (pointingSource == null)
                return;

            bool available = snapshot.isTracked && (!requireAimPose || snapshot.hasAimPose);
            pointingSource.SetState(available, available);

            if (!available)
                return;

            Transform target = rayTransform != null ? rayTransform : pointingSource.transform;
            if (snapshot.hasAimPose)
            {
                target.SetPositionAndRotation(snapshot.aimPose.position, snapshot.aimPose.rotation);
            }
        }
    }
}
```

- [ ] **Step 2: Add coordinator**

Create `Assets/App/Command/SpeechIntent/Runtime/HandInput/HandOnlyInputCoordinator.cs`:

```csharp
using UnityEngine;

namespace SpeechIntent.HandInput
{
    public class HandOnlyInputCoordinator : MonoBehaviour
    {
        public MonoBehaviour gestureSourceBehaviour;
        public HandPointingSourceDriver leftPointingDriver;
        public HandPointingSourceDriver rightPointingDriver;
        public bool logDebugMessages = true;

        readonly HandInputContextResolver _resolver = new HandInputContextResolver();
        readonly HandInputEventRouter _router = new HandInputEventRouter();
        IHandGestureSource _gestureSource;
        GameObject _leftHeldObject;
        GameObject _rightHeldObject;

        public HandInputEventRouter Router => _router;

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
        }

        void ProcessHand(HandedInputSide side, HandPointingSourceDriver pointingDriver, GameObject heldObject)
        {
            if (!_gestureSource.TryGetSnapshot(side, out HandGestureSnapshot snapshot))
                return;

            pointingDriver?.ApplySnapshot(snapshot);

            if (_resolver.TryResolve(snapshot, heldObject, out HandSemanticEvent evt))
            {
                bool consumed = _router.Route(evt);
                if (logDebugMessages)
                    Debug.Log($"[HandOnlyInput] {side} {evt.type} held={heldObject?.name ?? "none"} consumed={consumed}", this);
            }
        }
    }
}
```

- [ ] **Step 3: Compile-check**

Run the hand-only Unity batch command.

Expected: hand-only tests pass and no compile errors.

- [ ] **Step 4: Commit**

```bash
git add Assets/App/Command/SpeechIntent/Runtime/HandInput/HandPointingSourceDriver.cs Assets/App/Command/SpeechIntent/Runtime/HandInput/HandOnlyInputCoordinator.cs
git commit -m "Add hand-only input coordinator"
```

## Task 6: Scene/editor wiring

**Files:**
- Modify: `Assets/App/Editor/SpeechIntentSceneSetup.cs`
- Modify: `Assets/Scenes/Holodeck.unity`

- [ ] **Step 1: Add editor wiring helper**

Modify `Assets/App/Editor/SpeechIntentSceneSetup.cs` to import the hand-input namespace:

```csharp
using SpeechIntent.HandInput;
```

Then extend the existing setup code near the section that wires `PointingSource` components so it creates or finds:

```csharp
XRHandsGestureSource gestureSource = GetOrAdd<XRHandsGestureSource>(speechRoot);
HandOnlyInputCoordinator handCoordinator = GetOrAdd<HandOnlyInputCoordinator>(speechRoot);
handCoordinator.gestureSourceBehaviour = gestureSource;

HandPointingSourceDriver leftDriver = GetOrAdd<HandPointingSourceDriver>(leftTransform.gameObject);
leftDriver.pointingSource = spatial.leftHandSource;
leftDriver.rayTransform = leftTransform;

HandPointingSourceDriver rightDriver = GetOrAdd<HandPointingSourceDriver>(rightTransform.gameObject);
rightDriver.pointingSource = spatial.rightHandSource;
rightDriver.rayTransform = rightTransform;

handCoordinator.leftPointingDriver = leftDriver;
handCoordinator.rightPointingDriver = rightDriver;
```

If the existing setup method names differ, place this code in the method that already assigns `spatial.leftHandSource` and `spatial.rightHandSource`; do not create a second scene setup path.

- [ ] **Step 2: Run scene setup in Unity**

Open Unity and run the existing Speech Intent scene setup menu item that owns `SpeechIntentSceneSetup`. Save `Assets/Scenes/Holodeck.unity`.

Expected scene changes:

- Speech/system root has `XRHandsGestureSource`.
- Speech/system root has `HandOnlyInputCoordinator`.
- Left hand source has `HandPointingSourceDriver`.
- Right hand source has `HandPointingSourceDriver`.
- Existing `PushToTalkTrigger` and controller input remain unchanged.

- [ ] **Step 3: Compile-check**

Run the hand-only Unity batch command.

Expected: tests pass and no compile errors.

- [ ] **Step 4: Commit**

```bash
git add Assets/App/Editor/SpeechIntentSceneSetup.cs Assets/Scenes/Holodeck.unity
git commit -m "Wire hand-only input scene components"
```

## Task 7: Device validation and tuning

**Files:**
- Modify as needed: `Assets/Scenes/Holodeck.unity`
- Modify as needed: serialized threshold defaults in `Assets/App/Command/SpeechIntent/Runtime/HandInput/XRHandsGestureSource.cs`

- [ ] **Step 1: Build and run on Quest**

Use Unity Editor Build & Run for Android headset.

Expected:

- Existing controller thumbstick movement still works.
- Existing controller trigger select still works.
- Bare hands track when controllers are absent/inactive.

- [ ] **Step 2: Validate empty-hand select**

In headset:

1. Put controllers down or disable active controller input.
2. Use empty-hand thumb/index pinch.
3. Confirm select down/up is logged once per pinch.

Expected log shape:

```text
[HandOnlyInput] Right SelectDown held=none consumed=True
```

- [ ] **Step 3: Validate held-object trigger**

In headset:

1. Hold a grabbable object.
2. Keep fingers curled around the object.
3. Tap thumb against the middle/proximal segment or knuckle region of the index finger.

Expected:

- Grip does not release.
- One `TriggerSelect` event appears.
- If the object has no `IHandTriggerConsumer`, the event falls back to global select.

- [ ] **Step 4: Tune thresholds**

If thumb taps are too hard to trigger, increase `thumbTapSettings.contactDistance` by 0.005.

If thumb taps fire accidentally, decrease `thumbTapSettings.contactDistance` by 0.005 or increase `minApproachSpeed` by 0.1.

If repeated taps double-fire, increase `minSecondsBetweenTaps` by 0.05.

- [ ] **Step 5: Commit tuning**

```bash
git add Assets/App/Command/SpeechIntent/Runtime/HandInput/XRHandsGestureSource.cs Assets/Scenes/Holodeck.unity
git commit -m "Tune hand-only gesture thresholds"
```

## Self-review

- Spec coverage:
  - Empty-hand pinch select: Task 1, Task 2, Task 4, Task 7.
  - Held-object thumb tap trigger: Task 1, Task 2, Task 3, Task 7.
  - Held-object consume then fallback: Task 3.
  - Grip independent from trigger: Task 1, Task 2, Task 7.
  - Existing `PointingSource` integration: Task 5, Task 6.
  - Controller coexistence: Task 6 and Task 7 validation; implementation does not modify controller actions.
  - Debug logging: Task 5.
- Placeholder scan: no placeholder markers or fill-in steps remain.
- Type consistency:
  - `HandGestureSnapshot`, `HandSemanticEvent`, `HandedInputSide`, and `HandSemanticEventType` are defined before use.
  - `IHandGestureSource` is defined before `HandOnlyInputCoordinator` depends on it.
  - `IHandTriggerConsumer` is defined before router tests and implementation use it.
