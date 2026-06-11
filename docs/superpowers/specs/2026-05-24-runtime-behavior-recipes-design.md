# Runtime Behavior Recipes Design

## Purpose

Headset Holodeck already has working voice, typed UI, gaze, hand/controller, and pose-driven commands for world generation, object creation, transforms, materials, lighting, audio, and UI control. The next architecture step is to add a behavior layer that lets the user say things like "make this spin," "make the ball orbit the table," "throw the cube at the wall," or "make the blaster follow my right hand" without letting AI-generated text directly mutate arbitrary Unity APIs.

This design is the first incremental slice of the broader AI-to-Unity scene-control architecture described in `codex_ai_unity_scene_control_architecture_brief.md`. It preserves the current `VoiceIntentCommand` pipeline and adds a curated behavior lane that can later migrate into a full `SceneCommand -> Resolver -> Policy -> PrimitiveExecutor` stack.

## Current System Fit

The current command path is:

```text
Wake word / push-to-talk / typed UI
  -> VoiceCommandRouter
  -> OpenAiSpeechIntentService or LocalTypedIntentParser
  -> VoiceIntentCommand
  -> WorldActionDispatcher
  -> specialized controllers
```

Spatial and semantic context already exist:

- `SpatialContextProvider` captures head pose, hand/controller rays, hit points, hit normals, and hand midpoint.
- `BodyAnchorResolver` resolves head, left hand, and right hand anchors from the active hand/controller sources.
- `SpeechIntentTrackable` gives runtime objects canonical names, aliases, and persistent config IDs.
- `InteractionMemory` tracks current world, last created object, last interacted object, and current selection.
- `SceneEntityResolver` resolves named, material-qualified, pointed, remembered, selected, and `all` targets.

The behavior system should reuse these pieces rather than replacing them.

## Scope

The first behavior pass supports:

- `spin`: rotate one or more target objects over time.
- `orbit`: move a target object around another target, a point, or a user/body anchor.
- `throw`: apply a clamped impulse toward a target object, hit point, or direction.
- `follow_hand`: keep an object following a left/right hand/controller pose without parenting.
- `attach_to_hand`: make an object behave as if held in the specified or inferred hand.
- `stop_behavior`: stop a named behavior, all behaviors on a target, or all runtime behaviors.

Out of scope for this slice:

- General JSON behavior recipe interpretation.
- Arbitrary generated C#.
- Full primitive manifest authoring.
- Destructive behaviors.
- Network, file, account, or secrets access from behavior recipes.
- Complex simulation recipes such as solar systems, fluid ripples, or agents.

## Design Recommendation

Use an additive behavior lane, not a replacement of the command pipeline.

```text
VoiceIntentCommand
  -> WorldActionDispatcher
  -> BehaviorCommandController
  -> SceneEntityResolver
  -> BehaviorPolicy
  -> RuntimeBehaviorHost
  -> Built-in behavior implementation
```

This keeps working object/material/transform features intact while establishing the new rule: AI may request a behavior by name and parameters, but Unity chooses whether that behavior exists, whether the target is allowed, and how it executes.

## New Runtime Components

### BehaviorCommandController

`BehaviorCommandController` is the dispatcher-facing entry point. It receives a `VoiceIntentCommand` and `SpatialSnapshot`, resolves targets through `SceneEntityResolver`, validates behavior safety, and attaches or stops runtime behaviors.

Responsibilities:

- Resolve target objects for behavior commands.
- Resolve secondary targets for orbit/throw.
- Resolve body anchors for hand-follow and hand-attach.
- Apply safety policy before mutation.
- Create or update `RuntimeBehaviorHost` components.
- Emit user-facing success, clarification, and failure messages.
- Emit `MissingCapabilityReport` for known-but-unsupported behavior requests.

### RuntimeBehaviorHost

`RuntimeBehaviorHost` is a component attached to the target object. It owns one or more active behavior instances for that target.

Responsibilities:

- Tick per-frame behaviors.
- Store behavior IDs, names, parameters, and resolved runtime references.
- Stop one behavior, all behaviors on the object, or all hosts globally.
- Clean up safely when the target is destroyed or disabled.
- Avoid hidden static state except a small registry for global stop and diagnostics.

### BuiltInBehaviorLibrary

`BuiltInBehaviorLibrary` maps behavior names and aliases to safe implementations.

Initial aliases:

- `spin`: spin, rotate continuously, turn around.
- `orbit`: orbit, circle, go around, revolve around.
- `throw`: throw, toss, fling, launch.
- `follow_hand`: follow my hand, follow left hand, follow right hand.
- `attach_to_hand`: give me, put in my hand, hold this, attach to my hand.
- `stop_behavior`: stop spinning, stop orbiting, stop following, stop all behaviors.

### BehaviorPolicy

`BehaviorPolicy` validates target eligibility and clamps dangerous parameters.

Initial rules:

- Do not attach behaviors to protected system objects: `Me`, `Main Camera`, `Arch`, LCARS UI panels, world manager objects, command system objects, or service objects.
- Allow behaviors on user-created or explicitly trackable runtime objects.
- Allow behavior on generated/cached objects and primitives wrapped with `SpeechIntentTrackable`.
- Clamp spin speed, orbit speed, orbit radius, throw impulse, follow offset, and maximum target count.
- Treat `stop all behaviors` as safe.
- Treat multi-target physics behavior as requiring a future confirmation hook; for now, reject or ask for a narrower target.

### MissingCapabilityReport

Unsupported behavior requests should produce structured diagnostics, not a vague failure.

Example user-facing message:

```text
I do not have a melt behavior yet. I can currently spin, orbit, throw, or follow a hand.
```

Example diagnostic payload:

```json
{
  "status": "missing_capability",
  "user_request": "make the cube melt",
  "requested_behavior": "melt",
  "available_behaviors": ["spin", "orbit", "throw", "follow_hand", "attach_to_hand"],
  "needed_capabilities": ["deform_mesh", "animate_material_dissolve"],
  "possible_approximation": "Scale the object down while fading opacity."
}
```

For this slice, diagnostics may be logged to the console and `ArchStatusBus`. A file-backed backlog can be added later.

## VoiceIntentCommand Additions

Add behavior fields to `VoiceIntentCommand`:

```csharp
public string behavior_name = "";
public string behavior_action = "";
public string behavior_target_name = "";
public string behavior_secondary_target_name = "";
public float behavior_speed = 0f;
public float behavior_radius = 0f;
public string behavior_axis = "";
public bool behavior_stop_all = false;
```

Add intent enum values:

```csharp
AttachBehavior
StopBehavior
```

These are intentionally lightweight. They are a migration bridge, not the final command model.

## Command Interpretation

Update OpenAI developer instructions and local parsing so examples map consistently:

```text
"make this spin" -> AttachBehavior, behavior_name="spin", target_reference=PointedObject
"make the cube spin" -> AttachBehavior, behavior_name="spin", target_name="cube"
"make the ball orbit the table" -> AttachBehavior, behavior_name="orbit", target_name="ball", behavior_secondary_target_name="table"
"throw this at the wall" -> AttachBehavior, behavior_name="throw", target_reference=PointedObject, behavior_secondary_target_name="wall"
"give me the blaster" -> AttachBehavior, behavior_name="attach_to_hand", target_name="blaster", target_hand=Either
"make this follow my left hand" -> AttachBehavior, behavior_name="follow_hand", target_reference=PointedObject, target_hand=Left
"stop it spinning" -> StopBehavior, behavior_name="spin", target_reference=LastCreatedOrInteracted
"stop all behaviors" -> StopBehavior, behavior_stop_all=true
```

If the user omits the target, use the current selection or last created/interacted object. If that is missing, ask which object.

If the user omits the hand for a hand behavior:

1. Prefer explicitly indicated hand if one hand is pointing or extended.
2. Prefer the hand named in the transcript.
3. If both are plausible, ask "Which hand?"

## Behavior Details

### Spin

Target: one resolved object.

Default:

- Axis: local Y / up.
- Speed: 90 degrees per second.
- Space: local by default.

Safety:

- Clamp speed to a reasonable maximum, for example 720 degrees per second.
- Reject protected/system targets.

### Orbit

Target: one resolved subject and one resolved center.

Default:

- Plane: horizontal.
- Radius: current distance from center, or command radius if specified.
- Speed: 30 degrees per second.

Resolution:

- Secondary target comes from `behavior_secondary_target_name`, pointed hit, or current selection depending on transcript.
- If no center is available, ask "Orbit around what?"

Safety:

- Clamp speed and radius.
- Do not orbit system UI or `Me` unless explicitly allowed later.

### Throw

Target: one resolved subject.

Destination:

- Secondary target object if named.
- Pointing hit if the phrase uses "there" or "at that."
- Head/hand direction if no target is given and the utterance implies a forward throw.

Execution:

- Ensure or reuse a `Rigidbody`.
- Apply clamped impulse.
- Optionally add clamped random torque.

Safety:

- Do not throw system objects or large world roots.
- Clamp impulse and angular velocity.
- Avoid multi-target throw in the first slice.

### Follow Hand

Target: one resolved object.

Anchor:

- `BodyAnchor.LeftHand` or `BodyAnchor.RightHand`.
- Uses active hand tracking or controller transforms through the existing spatial/body-anchor path.

Behavior:

- Does not reparent.
- Each tick sets position and optionally rotation from the resolved hand pose plus configurable offset.
- If the hand is temporarily unavailable, keep the last pose for a short grace period, then pause.

Safety:

- Reject protected targets.
- Stop automatically if target is destroyed.

### Attach To Hand

Target: one resolved object.

Behavior:

- First implementation can be a stronger version of `follow_hand`: follows position and rotation with a tighter offset and no physics force.
- It should not require parenting in the first pass. Parenting can break saved-world transforms and XR interaction ownership.
- Later, this can integrate with XR grab interactables or attach transforms.

Phrases:

- "give me the blaster"
- "put the cube in my left hand"
- "attach this to my right hand"

## UI and Controller Inputs

The first slice does not require new visible UI. Existing UI/typed command entry can submit the same text commands through `VoiceCommandRouter.SubmitTypedCommand`.

Future UI affordances can call `BehaviorCommandController` directly with a constructed command, but the first pass should preserve the single command path so voice, typed UI, and button-initiated actions behave consistently.

Controller and hand inputs are consumed as context, not as behavior commands by themselves. Hand/controller pose informs references such as `this`, `there`, and `my left hand`.

## Error and Clarification Behavior

Return user-facing messages through the same channels used today:

- `VoiceIntentCommand.spoken_response`
- `ArchStatusBus`
- existing TTS response path

Examples:

- No target: "Which object should spin?"
- Ambiguous target: "Which cube?"
- No hand: "Which hand?"
- No orbit center: "Orbit around what?"
- Protected target: "I cannot attach that behavior to the Holodeck UI."
- Missing behavior: "I cannot do that behavior yet. I can spin, orbit, throw, or follow a hand."

## Tests

Add batch tests for:

- Local parser maps "make cube spin" to `AttachBehavior`.
- Local parser maps "stop all behaviors" to `StopBehavior`.
- `BehaviorCommandController` attaches a spin host to a trackable cube.
- `RuntimeBehaviorHost` spin changes rotation over simulated ticks.
- Orbit preserves approximate radius around a center object.
- Throw ensures rigidbody and clamps impulse.
- Follow-hand resolves left/right body anchors from a supplied `SpatialSnapshot`.
- Protected targets are rejected.
- Unknown behavior creates a missing-capability report.

## Migration Path

This slice should be implemented so it can later become:

```text
VoiceIntentCommand
  -> SceneCommand
  -> ReferenceResolver
  -> CommandPolicyValidator
  -> PrimitiveExecutor
  -> RuntimeBehaviorHost
```

The first pass may translate directly from `VoiceIntentCommand` to behavior objects, but names and file boundaries should avoid baking behavior logic into `WorldActionDispatcher`.

## Acceptance Criteria

The behavior recipe pilot is successful when:

- Existing voice, typed UI, object, material, transform, light, audio, and world-generation commands still work.
- The user can say "make this spin" while pointing and the indicated object spins.
- The user can say "make the cube orbit the table" and get an orbit if both resolve unambiguously.
- The user can say "throw this at the wall" and the object receives a clamped physics impulse.
- The user can say "make this follow my left hand" and the object follows the left hand/controller pose.
- The user can say "give me the blaster" and the blaster attaches/follows an inferred or specified hand.
- The user can say "stop all behaviors" and active runtime behaviors stop.
- Unsupported behaviors produce a clear missing-capability message and diagnostic log.
- Protected system objects are not modified by behavior commands.
