# Codex Implementation Brief: Robust AI-to-Unity Scene Control Architecture

## Purpose

This document is intended for Codex to use while inspecting an existing Unity XR application that already accepts verbal commands, UI input, head pose, and hand pose, and attempts to perform scene actions from those inputs.

The goal is not to discard the existing app. The goal is to introspect what is already present, identify where the current system maps language/input directly to brittle actions, and augment or replace those parts with a more robust architecture:

```text
User Input
  -> Intent / command interpretation
  -> Scene reference resolution
  -> Safe command validation
  -> Behavior recipe generation or lookup
  -> Unity execution through curated primitives
  -> Result, clarification, or structured failure
```

The system should eventually support requests like:

```text
create a green box on top of the table
move the box to the right
make the box spin
make the box orbit the table
delete all green boxes
make 10 green boxes
throw the box against the table
make a solar system with planets and moons which orbit according to known physics, but 100 times faster
change the spatialization of this audio source to 30%
```

The target application context is Unity 6.x, XR/VR, Quest-class deployment, OpenXR/XR Hands/Unity Input System-style input, and a Headset Holodeck / WorldLens-like interaction model. The app may already include world generation, object placement, lighting edits, UI commands, saved worlds, head/hand/gaze context, and voice-driven command parsing.

## Guiding Principle

Do not make arbitrary AI-generated code the authority over the Unity runtime.

Instead, make the AI produce structured commands, behavior recipes, and missing-capability reports. Unity should retain authority through a constrained execution layer.

The AI may plan. Unity executes.

The AI may synthesize recipes. Unity validates and interprets them.

The AI may propose new primitives. The app should not silently grant new low-level engine authority at runtime.

## Codex Task Summary

Codex should inspect the existing project and produce an incremental implementation plan plus code changes that move the app toward this architecture.

Codex should:

1. Discover the current input, command, intent, and execution flow.
2. Identify existing command handlers and scene manipulation code.
3. Identify existing object metadata, selection, gaze, hand, pointer, or last-object tracking systems.
4. Preserve working features where possible.
5. Introduce a semantic scene graph if one does not already exist.
6. Introduce a safe scene command model.
7. Introduce a resolver that turns phrases like “the box,” “the table,” “there,” “right,” and “all green boxes” into concrete scene references.
8. Introduce curated primitive adapters for Unity systems such as Transform, Renderer, Rigidbody, Collider, AudioSource, Light, Camera, Animator, UI, and XR interactables.
9. Introduce runtime behavior recipes that compose primitives.
10. Add missing-capability reporting instead of vague failures.
11. Add tests or diagnostic scenes where practical.
12. Keep all changes incremental and reversible.

## Do Not Do

Do not replace the entire app in one pass.

Do not expose the whole Unity API directly to AI output.

Do not execute arbitrary generated C# at runtime.

Do not let generated behavior code access secrets, files, network APIs, local storage, user accounts, or unrestricted object references.

Do not couple the language model directly to Unity object mutation.

Do not remove existing working command paths unless the replacement has equivalent or better behavior.

Do not assume there is only one object matching a description.

Do not silently perform destructive multi-object actions without a policy check or confirmation hook.

## Desired Architecture

### 1. Input Layer

The app may already have several input sources:

```text
Voice / transcript
UI controls
Head pose
Hand pose
Controller pose
Gaze / ray pointer
Recent interaction history
World-generation UI
Object-placement UI
```

Codex should preserve these inputs but normalize them into a shared input context object.

Suggested model:

```csharp
public sealed class InteractionContext
{
    public string RawText;
    public string Source; // voice, ui, gesture, system
    public Pose? HeadPose;
    public Pose? LeftHandPose;
    public Pose? RightHandPose;
    public Ray? GazeRay;
    public Ray? PointerRay;
    public string LastSelectedObjectId;
    public string LastCreatedObjectId;
    public string LastInteractedObjectId;
    public Dictionary<string, object> Extra;
}
```

This object should be available to command interpretation and reference resolution.

### 2. Intent / Command Interpretation Layer

The language model or local parser should convert user input into a structured scene command, not direct Unity calls.

Suggested top-level command model:

```csharp
public enum SceneCommandType
{
    Unknown,
    CreateObject,
    DeleteObject,
    MoveObject,
    RotateObject,
    ScaleObject,
    SetProperty,
    DuplicateObject,
    SelectObject,
    AttachBehavior,
    DetachBehavior,
    CreateSimulation,
    QueryScene,
    Undo,
    Clarify
}
```

Suggested command container:

```csharp
[Serializable]
public sealed class SceneCommand
{
    public string CommandId;
    public SceneCommandType Type;
    public TargetSelector Target;
    public ObjectSpec Object;
    public PlacementSpec Placement;
    public MotionSpec Motion;
    public PropertySpec Property;
    public BehaviorSpec Behavior;
    public SimulationSpec Simulation;
    public CommandPolicy Policy;
    public Dictionary<string, object> Parameters;
}
```

The exact C# model may differ based on existing code. Prefer adapting to existing project style rather than forcing this exact shape.

### 3. Scene Graph / Object Registry

The system needs a semantic model of the live scene.

Each relevant object should have a stable runtime ID and metadata.

Suggested model:

```csharp
public sealed class SceneEntity : MonoBehaviour
{
    public string EntityId;
    public string DisplayName;
    public string Kind;
    public List<string> Tags;
    public Color? SemanticColor;
    public bool UserCreated;
    public bool DestructibleByAI;
    public bool SelectableByAI;
    public bool MovableByAI;
    public float LastInteractedTime;
    public Bounds WorldBounds;
}
```

If the app already has similar metadata, Codex should reuse or adapt it.

The registry should support queries such as:

```text
Find all objects where kind == box
Find all objects tagged green
Find the most recent object matching box
Find objects on top of table
Find nearest object to gaze ray
Find nearest object to hand ray
Find all destructible green boxes
Find object by stable ID
```

Suggested interface:

```csharp
public interface ISceneRegistry
{
    SceneEntity GetById(string id);
    IReadOnlyList<SceneEntity> Query(TargetSelector selector, InteractionContext context);
    void Register(SceneEntity entity);
    void Unregister(SceneEntity entity);
}
```

### 4. Reference Resolver

The resolver turns user-level references into concrete scene entities.

Examples:

```text
the box -> most recent / selected / nearest matching box
the green box -> object with kind box and semantic color green
all green boxes -> all matching green boxes
the table -> single table if unambiguous, otherwise clarify
there -> pointer ray hit, hand target, gaze target, or UI-provided location
right -> user head pose right vector, usually projected onto horizontal plane
in my hand -> object currently held or nearest to hand pose
on top of the table -> placement relation using collider/bounds top surface
```

Suggested output:

```csharp
public sealed class ResolutionResult
{
    public bool Success;
    public bool NeedsClarification;
    public string ClarificationQuestion;
    public IReadOnlyList<SceneEntity> Entities;
    public Vector3? WorldPoint;
    public Vector3? Direction;
    public string FailureReason;
}
```

The resolver should be deterministic where possible. The language model can propose selectors, but Unity should decide which runtime objects they refer to.

### 5. Safe Primitive Adapters

Primitives are the trusted engine capabilities that behavior recipes may use.

Do not expose arbitrary Unity API calls directly. Expose curated adapters.

Suggested adapter categories:

```text
TransformAdapter
RendererAdapter
MaterialAdapter
RigidbodyAdapter
ColliderAdapter
AudioSourceAdapter
LightAdapter
CameraAdapter
AnimatorAdapter
ParticleSystemAdapter
XRInteractableAdapter
UIAdapter
WorldGenerationAdapter
```

Example primitive operations:

```text
create_object
delete_object
duplicate_object
set_position
set_rotation
set_scale
move_by
move_to
look_at
set_color
set_material
set_visible
set_opacity
ensure_rigidbody
set_velocity
apply_force
apply_impulse
apply_torque
stop_motion
raycast
get_bounds
closest_point
set_light_intensity
set_light_color
set_audio_volume
set_audio_pitch
set_audio_looping
set_audio_spatial_blend
play_audio
stop_audio
attach_behavior
detach_behavior
```

Each primitive should have:

```text
name
description
input schema
allowed target component types
range constraints
side-effect classification
whether it is destructive
whether it requires confirmation
whether it is allowed on system objects
```

Suggested capability manifest entry:

```json
{
  "name": "set_audio_spatial_blend",
  "description": "Set how much an AudioSource behaves like 2D audio versus 3D spatial audio.",
  "component": "AudioSource",
  "property": "spatialBlend",
  "inputs": {
    "target": "SceneEntity",
    "value": "float"
  },
  "constraints": {
    "min": 0.0,
    "max": 1.0
  },
  "aliases": [
    "spatialization",
    "3D sound blend",
    "make the sound more 3D",
    "set spatialization to percent"
  ],
  "destructive": false
}
```

### 6. Behavior Recipes

A behavior recipe is a runtime-authored or built-in composition of primitives.

Examples:

```text
spin object
orbit object around target
throw subject against target
arrange objects in grid
make object pulse with light
make object follow hand
make solar system simulation
```

A behavior recipe should be data, not arbitrary C#.

Suggested representation:

```json
{
  "name": "throw_against_target",
  "description": "Applies an impulse to a subject object toward a target object.",
  "inputs": ["subject", "target"],
  "steps": [
    { "op": "ensure_rigidbody", "object": "$subject" },
    { "op": "compute_closest_point", "from": "$subject.bounds.center", "target": "$target" },
    { "op": "compute_direction", "from": "$subject.bounds.center", "to": "$computed.closestPoint" },
    { "op": "apply_impulse", "object": "$subject", "direction": "$computed.direction", "speed": 6.0 },
    { "op": "apply_torque", "object": "$subject", "axis": "random_horizontal", "strength": 0.8 }
  ]
}
```

A recipe may be saved, reused, versioned, tested, and eventually promoted to a native implementation.

Runtime behavior expansion is allowed only when the requested behavior can be expressed with primitives already present in the installed app.

If a new low-level primitive is required, the system should produce a missing-capability report.

### 7. Runtime Behavior Interpreter

Codex should look for a clean place to add a behavior interpreter.

The interpreter receives:

```text
BehaviorRecipe
Resolved inputs
InteractionContext
SceneRegistry
PrimitiveExecutor
```

The interpreter should:

```text
validate recipe schema
validate primitive names
validate parameter ranges
resolve variables like $subject and $target
execute steps in order
support per-frame behaviors where required
support stop conditions
report success/failure with details
```

For per-frame behaviors like spin, orbit, follow, or gravity simulation, use a managed runtime behavior host rather than generating new C# component types.

Suggested Unity component:

```csharp
public sealed class RuntimeBehaviorHost : MonoBehaviour
{
    public string BehaviorId;
    public BehaviorRecipe Recipe;
    public Dictionary<string, object> State;

    private void Update()
    {
        // Interpret tick steps through the primitive executor.
    }

    public void Stop()
    {
        // Cleanup, unregister, detach.
    }
}
```

### 8. Missing Capability Reports

When the system cannot satisfy a request, it should produce structured information.

Example:

```json
{
  "status": "missing_capability",
  "user_request": "make the water ripple when I touch it",
  "needed_capabilities": [
    "detect_hand_touch_on_surface",
    "modify_water_shader_ripple_origin",
    "animate_shader_float"
  ],
  "available_near_matches": [
    "raycast",
    "set_material_float",
    "play_particle_effect"
  ],
  "possible_approximation": "Spawn a ripple particle effect at the contact point."
}
```

This should be logged in a way that can later become a development backlog.

Do not fail with only “I do not know how to do that.”

### 9. Safety and Policy Layer

Every command and recipe should be classified before execution.

Policy dimensions:

```text
read-only
visual-only
motion/physics
creates objects
destroys objects
multi-object destructive
changes persistent world state
network/world-generation operation
requires user confirmation
unsafe/disallowed
```

Examples:

```text
set color of one selected object -> allowed
move one selected object -> allowed
throw object -> allowed if target objects are safe and forces are clamped
delete the box -> allowed if object is user-created/destructible
delete all green boxes -> confirmation required
hide all UI -> confirmation or disallow depending on app state
read saved passwords -> disallowed
send files/network data -> disallowed unless an explicit trusted feature already exists
```

Do not rely only on the language model to detect malicious requests. The Unity host must validate every operation.

### 10. Scene Command Examples

#### Create a green box on top of the table

```json
{
  "type": "CreateObject",
  "object": {
    "kind": "box",
    "quantity": 1,
    "color": "green"
  },
  "placement": {
    "relation": "on_top_of",
    "target": {
      "kind": "table",
      "quantifier": "one",
      "disambiguation": "ask_if_multiple"
    }
  }
}
```

#### Move the box to the right

```json
{
  "type": "MoveObject",
  "target": {
    "kind": "box",
    "reference": "most_recent_matching"
  },
  "motion": {
    "direction": "right",
    "referenceFrame": "user_head_pose",
    "distanceMeters": 0.5,
    "projectOntoHorizontalPlane": true
  }
}
```

#### Make the box spin

```json
{
  "type": "AttachBehavior",
  "target": {
    "kind": "box",
    "reference": "most_recent_matching"
  },
  "behavior": {
    "name": "spin",
    "parameters": {
      "axis": "up",
      "speedDegreesPerSecond": 90
    }
  }
}
```

#### Make the box orbit the table

```json
{
  "type": "AttachBehavior",
  "target": {
    "kind": "box",
    "reference": "most_recent_matching"
  },
  "behavior": {
    "name": "orbit",
    "parameters": {
      "around": {
        "kind": "table",
        "disambiguation": "ask_if_multiple"
      },
      "radius": "current_distance",
      "plane": "horizontal",
      "speedDegreesPerSecond": 30
    }
  }
}
```

#### Delete all green boxes

```json
{
  "type": "DeleteObject",
  "target": {
    "kind": "box",
    "filters": {
      "color": "green"
    },
    "quantifier": "all"
  },
  "policy": {
    "requiresConfirmation": true,
    "reason": "destructive_multi_object_action"
  }
}
```

#### Make 10 green boxes

```json
{
  "type": "CreateObject",
  "object": {
    "kind": "box",
    "quantity": 10,
    "color": "green"
  },
  "placement": {
    "strategy": "grid_near_user",
    "spacingMeters": 0.75,
    "avoidCollisions": true
  }
}
```

#### Throw the box against the table

```json
{
  "type": "AttachBehavior",
  "target": {
    "kind": "box",
    "reference": "most_recent_matching"
  },
  "behavior": {
    "name": "throw_against_target",
    "parameters": {
      "target": {
        "kind": "table",
        "disambiguation": "ask_if_multiple"
      },
      "speedMetersPerSecond": 6.0,
      "addSpin": true
    }
  }
}
```

#### Make a solar system simulation

```json
{
  "type": "CreateSimulation",
  "simulation": {
    "kind": "solar_system",
    "physicsModel": "newtonian_approximation",
    "timeScale": 100,
    "visualScale": "compressed",
    "includeMoons": true,
    "separateVisualScaleFromPhysicsScale": true
  }
}
```

## Implementation Phases

### Phase 1: Repository Introspection

Codex should inspect the repository and answer these questions before making large changes:

```text
Where is voice input handled?
Where is UI command input handled?
Where are head pose, hand pose, gaze, and pointer pose consumed?
Where does transcript text become an intent or action?
Where are scene objects created, moved, modified, or deleted?
Is there already a registry, selection manager, object metadata component, or command dispatcher?
Is there already a last-created / last-selected / last-interacted object concept?
Are commands currently represented as strings, enums, JSON, classes, ScriptableObjects, or direct method calls?
Where should a new command architecture be inserted with minimal disruption?
```

Codex should summarize findings in a short implementation note before editing code.

### Phase 2: Add Core Abstractions

Add or adapt:

```text
InteractionContext
SceneCommand
TargetSelector
SceneEntity
SceneRegistry
ReferenceResolver
CommandValidator
PrimitiveManifest
PrimitiveExecutor
CommandDispatcher
ExecutionResult
MissingCapabilityReport
```

Keep this phase small. It is acceptable for many methods to be stubs or to wrap existing behavior at first.

### Phase 3: Wrap Existing Features

Map existing commands into the new command architecture.

Examples:

```text
existing world generation -> SceneCommand/CreateSimulation or GenerateWorld command
existing object placement -> CreateObject + PlacementSpec
existing lighting edit -> SetProperty or LightAdapter primitive
existing UI open/close -> UIAdapter primitive
existing hand-point placement -> InteractionContext pointer/hand ray + PlacementSpec
```

Do not break existing working features.

### Phase 4: Add First Primitive Adapters

Start with:

```text
TransformAdapter
RendererAdapter
MaterialAdapter
RigidbodyAdapter
ColliderAdapter
AudioSourceAdapter
LightAdapter
```

Each adapter should expose a small, safe set of operations.

### Phase 5: Add Behavior Recipes

Add built-in recipes for:

```text
spin
orbit
throw_against_target
duplicate_grid
follow_target
pulse_material
scaled_solar_system_basic
```

Use the runtime interpreter where practical. If a native implementation already exists, wrap it behind the same behavior interface.

### Phase 6: Add Missing-Capability Logging

When command execution fails because no primitive or behavior exists, return and log:

```text
requested behavior
missing primitive(s)
available near matches
possible approximation
whether rebuild/new primitive is required
```

### Phase 7: Add Diagnostics and Tests

Add an editor/debug scene or test harness that can issue example commands without requiring full voice input.

Suggested tests:

```text
Create green box.
Create green box on table.
Move box right relative to head pose.
Delete all green boxes with confirmation required.
Attach spin behavior.
Attach orbit behavior.
Throw box toward table.
Set AudioSource spatial blend to 30%.
Generate missing-capability report for unsupported operation.
```

## Suggested File/Folder Layout

Adapt to the existing project layout, but prefer a structure like:

```text
Assets/App/AICommands/
  Runtime/
    InteractionContext.cs
    SceneCommand.cs
    TargetSelector.cs
    CommandDispatcher.cs
    CommandValidator.cs
    ExecutionResult.cs
  SceneGraph/
    SceneEntity.cs
    SceneRegistry.cs
    ReferenceResolver.cs
  Primitives/
    IPrimitive.cs
    PrimitiveManifest.cs
    PrimitiveExecutor.cs
    TransformPrimitives.cs
    RendererPrimitives.cs
    RigidbodyPrimitives.cs
    AudioSourcePrimitives.cs
    LightPrimitives.cs
  Behaviors/
    BehaviorRecipe.cs
    RuntimeBehaviorHost.cs
    BehaviorInterpreter.cs
    BuiltInBehaviorLibrary.cs
  Policy/
    CommandPolicy.cs
    SafetyClassifier.cs
    ConfirmationPolicy.cs
  Diagnostics/
    CommandDebugPanel.cs
    MissingCapabilityLog.cs
  Tests/
    EditMode/
    PlayMode/
```

## Acceptance Criteria

Codex should consider the first useful implementation successful when:

```text
Existing voice/UI/head/hand command features still work.
At least one existing command path is routed through SceneCommand -> Resolver -> Executor.
Objects can be registered as SceneEntity objects with kind/tags/color metadata.
The system can resolve “the box,” “the green box,” “all green boxes,” and “the table” in a test scene.
The system can create a green box on top of a table.
The system can move an object right relative to the user/head pose.
The system can attach a spin or orbit behavior through a behavior recipe or common behavior interface.
The system can set AudioSource spatial blend through a curated primitive.
Unsupported requests produce MissingCapabilityReport instead of vague failure.
Destructive multi-object operations are classified as requiring confirmation.
```

## Important Design Notes

### Runtime Library Growth

The runtime library can grow in two ways:

```text
1. New behavior recipes using existing primitives.
2. New engine primitives added in future app builds.
```

Runtime behavior recipes should be allowed when they compose existing safe primitives.

New low-level engine authority should require a new curated primitive and therefore usually a rebuild/redeploy.

### Capability Discovery

Codex may add editor-time tooling that scans known component types and proposes candidate primitive manifest entries, but it should not expose everything automatically.

A generated candidate list is useful. A curated manifest is required.

Useful candidates include runtime-modifiable fields on:

```text
Transform
Renderer
Material
Rigidbody
Collider
AudioSource
Light
Camera
Animator
ParticleSystem
XR interactables
```

Each candidate should be wrapped with semantic names, ranges, policies, and aliases.

### Ambiguity Handling

If a command refers to multiple possible targets and does not say “all,” ask or produce a clarification result.

Example:

```text
User: delete the box
Scene: three boxes
Result: clarification required
Question: Which box should I delete: the green box on the table, the red box near you, or the blue box by the wall?
```

If the user says “all green boxes,” no target ambiguity exists, but policy may still require confirmation because the action is destructive and multi-object.

### Reference Frames

Spatial language must specify a reference frame.

Examples:

```text
right -> user head pose right vector
left -> user head pose left vector
forward -> user head pose forward projected to horizontal plane, unless pointing context says otherwise
there -> pointer/gaze/hand ray hit point
on top of -> target bounds/collider top surface
in front of table -> table forward direction, if defined; otherwise user-relative or clarify
```

### Physics and Safety

Physics operations should be clamped.

Examples:

```text
maximum impulse
maximum velocity
maximum angular velocity
allowed object mass range
allowed targets
collision safety
no throwing system UI or non-movable scene anchors
```

### Solar-System Simulation

The solar-system request should be treated as a simulation recipe.

Important details:

```text
Use separate visual scale and physics scale.
Use compressed distances for visibility.
Use enlarged body visuals.
Use stable integration or simplified orbital approximations initially.
Do not attempt literal real-scale rendering unless explicitly requested.
Expose timeScale = 100 as a parameter.
```

A basic implementation may use simple circular/elliptical orbit behaviors first. A later implementation can add an n-body approximation.

## Final Instruction to Codex

Make the smallest coherent set of changes that establishes this architecture in the existing app.

Prefer additive wrappers and adapters before replacement.

Preserve the current working app behavior.

Create clear seams where the current command parser, OpenAI call, UI input, or gesture input can feed a structured SceneCommand.

Create a testable path from command to resolution to primitive execution.

Log unsupported requests as structured missing-capability reports.

Do not attempt to solve every possible Unity action in the first pass. Establish the capability architecture so the system can grow safely.

