# Hand-only input design

Date: 2026-07-02

## Goal

Add no-controller hand-only support for the same core actions that currently work with controllers:

- Controller thumbstick behavior remains unchanged.
- Controller trigger remains `select`.
- Bare-hand users can select, grip, hold, and trigger/use held objects without requiring a controller, band, or glove.

The immediate design target is Quest/OpenXR optical hand tracking through Unity XR Hands and XRI. Meta Display Band-style EMG intent is out of scope unless that hardware/API is later available; this design uses tracked hand joints and OpenXR hand interaction values only.

## Existing project context

The project already has the required package substrate:

- `com.unity.xr.hands` 1.7.3
- `com.unity.xr.interaction.toolkit` 3.5.0
- `com.unity.xr.openxr` 1.17.0-pre.2
- `com.unity.inputsystem` 1.15.0

Current app-level input is controller-oriented:

- `PushToTalkTrigger` bridges an Input System action to speech push-to-talk.
- `PointingSource` represents hand/controller ray state, but does not derive that state from XR hand joints.
- `SpatialContextProvider` consumes `PointingSource` data for spatial grounding.

There is no app-level XRHand/thumb gesture module yet.

## Interaction model

Hand gestures should map to semantic actions, not directly to one-off object behavior.

| Context | Gesture | Semantic action |
| --- | --- | --- |
| Empty hand | thumb/index pinch or thumb/index fingertip touch | Select |
| Empty hand | stable hand ray/aim pose | Point |
| Interactable under hand or near hand | grip gesture | Grip / hold |
| Holding object | thumb tap on index middle/proximal segment or knuckle region | TriggerSelect / use |
| Holding object | hand opens past release threshold | Release grip |

The key design choice: while holding an object, thumb-index pinch should not be required for trigger/select, because moving toward a pinch can weaken the grip and accidentally release the held object.

Instead, a held-object trigger is a thumb-side tap: the thumb tip taps or presses against the middle/proximal region of the curled index finger, allowing the index finger and remaining fingers to keep holding the object.

## Routing behavior

Held-object trigger should use fallback bubbling:

1. Detect thumb tap while a hand is holding an object.
2. Offer `TriggerSelect` to the held object first.
3. If the held object consumes it, stop.
4. If the held object does not consume it, route the event as normal world/UI select.

Examples:

- Gun consumes `TriggerSelect` and fires.
- Flashlight consumes `TriggerSelect` and toggles.
- Grabbed cube ignores `TriggerSelect`, so the tap can still click UI or confirm a world action.
- Empty hand uses pinch as ordinary `Select`.

## Components

### `HandGestureProvider`

Reads XR Hands/OpenXR data and emits raw gesture state, with no app semantics:

- `PinchDown`
- `PinchUp`
- `GripDown`
- `GripUp`
- `ThumbTapDown`
- hand availability/tracking confidence
- hand aim/ray pose when available

Preferred data sources:

1. OpenXR/XR Hands common gesture values where available, especially pinch value and aim pose.
2. Joint-derived fallback using `XRHandJointID` poses, especially thumb tip, index tip, index intermediate, index proximal, palm/wrist.

The provider must use hysteresis and debounce:

- A gesture has separate enter/exit thresholds.
- A single tap emits a single event.
- Tracking loss cancels active transient gestures safely.
- Grip release requires a real open-hand/release threshold, not merely thumb movement.

### `HandInputContextResolver`

Maps raw gestures to semantic input based on hand state:

```text
if hand is holding an object:
    ThumbTapDown -> TriggerSelect
else:
    PinchDown / PinchUp -> SelectDown / SelectUp
```

The resolver is responsible for preserving grip while triggering. Thumb movement must not imply `GripUp`.

### `HandInputEventRouter`

Routes semantic input to the right consumer:

```text
TriggerSelect while holding:
    if held object consumes event:
        stop
    else:
        route as global/world/UI select

Select while empty:
    route as global/world/UI select
```

The router should avoid special-case knowledge of individual tools. Held objects opt in by implementing or exposing a small trigger/use interface.

### `HandPointingSourceDriver`

Updates existing `PointingSource` objects from hand tracking:

- available/unavailable
- pointing or not pointing
- ray origin
- ray direction
- confidence/debug values

Preferred ray source:

1. OpenXR aim pose, if available and stable.
2. Fallback ray from palm/index geometry.

This keeps `SpatialContextProvider` usable without redesigning the spatial-awareness pipeline.

## Gesture details

### Empty-hand select

Use pinch because it is the most conventional and best-supported optical hand-tracking select gesture.

Detection priority:

1. XR Hands/OpenXR pinch value.
2. Thumb-tip to index-tip distance.

### Held-object trigger

Use thumb tap against index middle/proximal/knuckle region.

Detection priority:

1. Thumb-tip proximity to index intermediate/proximal joint region.
2. Thumb-tip velocity toward that region followed by separation.
3. Optional fallback: a short, sharp inward thumb motion while grip remains active.

This should be recognized only while a hand is in a held-object context. It should not require the index fingertip to move toward the thumb.

### Grip

Grip should be independent of trigger/select.

Grip may initially use existing XRI grab behavior or a simple hand-shape threshold, but its release threshold must be explicit and conservative. A trigger tap must not accidentally release the grip.

## Controller coexistence

Controller behavior is the source of truth when controllers are active:

- Thumbstick movement/turning remains as-is.
- Trigger select remains as-is.
- Hand-only gestures should not steal input from active controllers.

Activation policy:

- If controllers are tracked/active, keep controller input primary.
- If hands are tracked and controllers are absent/inactive, enable hand-only gestures.
- If both are present, prefer explicit controller input unless a later design intentionally supports simultaneous hybrid use.

## Debugging and tuning

Add optional debug logging or an in-headset debug overlay for:

- left/right hand tracked
- active input source: controller, left hand, right hand
- pinch value
- thumb-index-tip distance
- thumb-index-middle/proximal distance
- grip state
- held object name
- `SelectDown`, `SelectUp`, `GripDown`, `GripUp`, `TriggerSelect`
- event consumed vs bubbled

This is important because optical hand gestures usually need threshold tuning on-device.

## Testing

Unit or edit-mode tests should cover the pure gesture/state logic with synthetic hand data:

- empty-hand pinch emits select down/up
- held-object thumb tap emits `TriggerSelect`
- held-object thumb tap does not emit `GripUp`
- held object can consume trigger
- unconsumed trigger bubbles to global select
- tracking loss clears transient gesture state without stuck select/grip
- repeated taps debounce cleanly

Device testing should cover:

- no-controller Quest hand tracking
- controller present/inactive
- controller active
- object held while thumb taps index knuckle/segment
- fast repeated trigger taps
- hand occlusion and reacquisition

## Non-goals

- Replacing controller movement/turning.
- Depending on Meta Display Band hardware.
- Full custom hand physics.
- Large rewrite of XRI interaction.
- Solving all possible hand gestures in the first pass.

## Open implementation notes

- The first implementation should prefer minimal integration with the existing `PointingSource` and XRI setup.
- Gesture thresholds should be serialized fields so they can be tuned in the Unity Inspector.
- If OpenXR aim/pinch values are not reliable on Quest in this project configuration, fall back to joint-derived geometry.
- The held-object trigger interface should be tiny and optional so ordinary objects do not need to know about hand input.
