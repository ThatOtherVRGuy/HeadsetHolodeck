# Command Understanding Parser — Codex Implementation Brief

## Purpose

Build a command-understanding layer for a voice-driven interactive 3D application. The current app is in Unity for Quest 3, but this design should be implemented in a platform-agnostic way so it can be reused from Unity, another engine, a server process, or tests.

The goal is to take arbitrary English instructions and convert them into structured, validated, executable commands. The system should support incomplete, ambiguous, and context-dependent utterances, especially commands that refer to objects in a scene using language like “this,” “that,” “the red one,” “the last one,” or “the one on the left.”

The core design should not treat ambiguity as a parsing failure. A command may be linguistically complete while still being operationally unresolved because the scene contains multiple possible targets. The system should preserve such commands in a pending state, ask a clarification question, and then use the user’s follow-up answer to complete the command.

## Core Mental Model

A useful way to understand most commands is:

```text
Do [something]
to/with/on [what]
optionally where
optionally relative to what
optionally when
optionally how
optionally with what constraints
```

More explicitly:

```text
Do what action?
To or with what object?
Relative to what object or place?
Where should it happen?
When should it happen?
How should it happen?
With what constraints?
```

Examples:

```text
Create a cube.
```

Maps to:

```text
Do: create
What: cube
Where: default location
When: now
How: default creation parameters
```

```text
Create a cube on top of the sphere.
```

Maps to:

```text
Do: create
What: cube
Where: on top of the sphere
When: now
How: default creation parameters
```

```text
Make the red sphere bigger.
```

Maps to:

```text
Do: scale/enlarge
What: red sphere
Where: same location
When: now
How: larger by default amount
```

```text
Move that over there.
```

Maps to:

```text
Do: move
What: that
Where: over there
When: now
How: default move behavior
```

## Why Traditional Sentence Diagramming Is Useful but Insufficient

Traditional English sentence diagramming can help identify useful grammatical roles:

- Imperative verb → command intent
- Direct object → primary object or new object
- Prepositional phrase → spatial relation, destination, source, reference object, or constraint
- Adjectives → object attributes
- Adverbs → manner or modifier
- Pronouns/demonstratives → references requiring context
- Conjunctions → multiple commands or multiple objects

For example:

```text
Create a cube on top of the sphere.
```

A grammatical analysis may identify:

```text
Verb: create
Direct object: cube
Prepositional phrase: on top of the sphere
```

That is helpful, but not enough. The app must also know which sphere is meant. If there are multiple spheres in the scene, the parser has not failed; the command contains an unresolved reference.

Therefore, the implementation should be based on a semantic command frame plus scene-grounded reference resolution, not sentence diagramming alone.

## Recommended Architecture

Use this pipeline:

```text
User utterance
  -> command parser
  -> normalized command frame
  -> reference resolver / scene grounding
  -> validation
  -> clarification if needed
  -> executable command
  -> command executor
```

Major components:

1. `CommandParser`
   - Converts natural language into a structured command frame.
   - May use rules, an LLM, a grammar parser, or a hybrid approach.
   - Should not directly mutate the scene.

2. `CommandOntology`
   - Maps synonyms and phrases to canonical intents, object types, attributes, and spatial relations.
   - Examples:
     - “make,” “create,” “spawn,” “add” → `create`
     - “delete,” “remove,” “get rid of” → `delete`
     - “bigger,” “scale up,” “enlarge” → `scale` with positive amount
     - “on top of,” “above,” “place on” → `on_top_of` or `above`, depending on desired semantics

3. `SceneGraph`
   - Stores live objects and their queryable properties.
   - Should support queries by type, color, size, position, recency, selection state, visibility, and interaction history.

4. `ReferenceResolver`
   - Resolves object references such as “the sphere,” “the red one,” “this,” “that,” “the last one,” “the one on the left.”
   - Returns zero, one, or multiple candidate objects.

5. `DialogStateManager`
   - Stores pending commands when references are ambiguous or missing.
   - Interprets follow-up fragments in the context of the pending command.

6. `ClarificationGenerator`
   - Produces questions such as:
     - “Which sphere?”
     - “Do you mean the red sphere or the blue sphere?”
     - “Where should I put it?”

7. `CommandValidator`
   - Ensures the command is executable and safe.
   - Verifies required slots are filled, object IDs are valid, and action parameters are allowed.

8. `CommandExecutor`
   - Executes only fully resolved and validated commands.
   - Should receive concrete IDs, positions, values, and parameters, not raw English.

## Important Design Principle

Do not let the language parser or LLM directly mutate the scene.

The parser should produce structured intent. The deterministic application layer should validate, resolve, clarify, and execute.

This separation makes the system safer, testable, debuggable, and easier to extend.

## Command Frame Schema

A command frame should represent the user’s intent independently from whether all references have been resolved.

A possible generic schema:

```json
{
  "intent": "create | move | delete | scale | rotate | set_property | select | group | ungroup | unknown",
  "status": "parsed | unresolved | ambiguous | executable | invalid",
  "target": null,
  "new_object": null,
  "destination": null,
  "reference": null,
  "property": null,
  "value": null,
  "quantity": null,
  "when": "now",
  "how": {},
  "constraints": {},
  "result_binding": null,
  "raw_utterance": "",
  "confidence": null,
  "problems": []
}
```

For creation commands, `new_object` is usually filled and `target` may be null.

For modification commands, `target` is usually an existing object reference.

For spatial commands, `destination` usually contains a spatial relation and a reference object or location.

## Object Reference Schema

Object references should support both already-resolved IDs and unresolved descriptions.

```json
{
  "kind": "object_reference",
  "type": "sphere",
  "attributes": {
    "color": "red",
    "size": "large"
  },
  "deictic": null,
  "relation": null,
  "relative_to": null,
  "resolved_id": null,
  "candidates": [],
  "resolution_status": "unresolved | ambiguous | resolved | none_found"
}
```

Examples of references:

```json
{
  "kind": "object_reference",
  "type": "sphere",
  "attributes": {},
  "resolved_id": null,
  "candidates": [],
  "resolution_status": "unresolved"
}
```

```json
{
  "kind": "object_reference",
  "type": null,
  "attributes": {
    "color": "red"
  },
  "resolved_id": null,
  "candidates": [],
  "resolution_status": "unresolved"
}
```

```json
{
  "kind": "object_reference",
  "type": null,
  "attributes": {},
  "deictic": "this",
  "resolved_id": null,
  "candidates": [],
  "resolution_status": "unresolved"
}
```

## Scene Object Schema

The scene graph should expose objects in a queryable form, independent of the rendering engine.

Example:

```json
{
  "id": "sphere_18",
  "type": "sphere",
  "name": "Sphere 18",
  "color": "red",
  "size_label": "large",
  "scale": [1.5, 1.5, 1.5],
  "position": [1.2, 0.5, -2.8],
  "rotation": [0, 0, 0, 1],
  "created_at_sequence": 42,
  "last_interacted_sequence": 58,
  "selected": false,
  "visible_to_user": true,
  "tags": []
}
```

The resolver should not require Unity-specific object instances. It should operate on plain data structures and return IDs or commands that Unity can apply later.

## Dialog State Schema

When a command cannot yet be executed, store the pending command and the missing or ambiguous slot.

Example:

```json
{
  "pending_command": {
    "intent": "create",
    "new_object": {
      "type": "cube"
    },
    "destination": {
      "relation": "on_top_of",
      "reference": {
        "type": "sphere",
        "resolution_status": "ambiguous",
        "candidates": ["sphere_12", "sphere_18", "sphere_22"]
      }
    }
  },
  "awaiting": "destination.reference",
  "clarification_question": "Which sphere?",
  "candidate_ids": ["sphere_12", "sphere_18", "sphere_22"]
}
```

If the next user utterance is a fragment such as:

```text
The red one.
```

The system should interpret it as a resolution for the pending slot, not as a standalone command.

Resolution frame:

```json
{
  "resolution": {
    "slot": "destination.reference",
    "constraints": {
      "color": "red"
    }
  }
}
```

## Command Statuses

Use explicit status values. Suggested values:

```text
parsed        The utterance was converted into a command frame.
unresolved    A needed reference or slot is missing.
ambiguous     A reference matches multiple candidates.
none_found    A reference matches no candidates.
executable    All required references and parameters are resolved.
invalid       The command cannot be executed as stated.
executed      The command was executed.
failed        Execution failed after validation.
```

Do not collapse `ambiguous`, `none_found`, and `invalid` into a single error state. They require different user responses.

## Example: Create a Cube on the Sphere

User says:

```text
Create a cube on top of the sphere.
```

Parser output:

```json
{
  "intent": "create",
  "new_object": {
    "type": "cube",
    "attributes": {}
  },
  "destination": {
    "relation": "on_top_of",
    "reference": {
      "type": "sphere",
      "attributes": {},
      "resolved_id": null,
      "candidates": [],
      "resolution_status": "unresolved"
    }
  },
  "when": "now",
  "how": {},
  "raw_utterance": "Create a cube on top of the sphere."
}
```

If the scene contains exactly one sphere, resolver output:

```json
{
  "intent": "create",
  "status": "executable",
  "new_object": {
    "type": "cube",
    "attributes": {}
  },
  "destination": {
    "relation": "on_top_of",
    "reference": {
      "type": "sphere",
      "resolved_id": "sphere_12",
      "resolution_status": "resolved"
    }
  }
}
```

If the scene contains three spheres, resolver output:

```json
{
  "intent": "create",
  "status": "ambiguous",
  "new_object": {
    "type": "cube",
    "attributes": {}
  },
  "destination": {
    "relation": "on_top_of",
    "reference": {
      "type": "sphere",
      "resolved_id": null,
      "candidates": ["sphere_12", "sphere_18", "sphere_22"],
      "resolution_status": "ambiguous"
    }
  },
  "problems": [
    {
      "type": "ambiguous_reference",
      "slot": "destination.reference",
      "question": "Which sphere?"
    }
  ]
}
```

The dialog manager should ask:

```text
Which sphere?
```

If user replies:

```text
The red one.
```

The pending command should be updated by filtering candidates to the red sphere.

If only one red sphere remains, the final command becomes executable.

## Example: Move the Red Cube Behind the Blue Sphere

User says:

```text
Move the red cube behind the blue sphere.
```

Command frame:

```json
{
  "intent": "move",
  "target": {
    "type": "cube",
    "attributes": {
      "color": "red"
    },
    "resolved_id": null,
    "resolution_status": "unresolved"
  },
  "destination": {
    "relation": "behind",
    "reference": {
      "type": "sphere",
      "attributes": {
        "color": "blue"
      },
      "resolved_id": null,
      "resolution_status": "unresolved"
    }
  },
  "when": "now",
  "how": {
    "movement": "default"
  }
}
```

This command has two references that may need resolution:

1. The red cube being moved.
2. The blue sphere used as the spatial reference.

Each reference should be resolved separately. If either is ambiguous, the system should ask about that specific slot.

## Example: Make It Bigger

User says:

```text
Make it bigger.
```

Command frame:

```json
{
  "intent": "scale",
  "target": {
    "reference": "it",
    "resolved_id": null,
    "resolution_status": "unresolved"
  },
  "how": {
    "scale_direction": "larger",
    "amount": "default"
  }
}
```

The resolver should interpret `it` using the current context, such as:

1. Currently selected object.
2. Object under gaze or pointer.
3. Last interacted object.
4. Last created object.
5. Pending command context.

The order should be configurable.

## Example: Put That Over There

User says:

```text
Put that over there.
```

Command frame:

```json
{
  "intent": "move",
  "target": {
    "deictic": "that",
    "resolved_id": null,
    "resolution_status": "unresolved"
  },
  "destination": {
    "deictic": "there",
    "position": null,
    "resolution_status": "unresolved"
  }
}
```

This requires non-linguistic input from the app, such as gaze, hand ray, controller ray, pointer hit, or current selection.

The resolver should support multimodal grounding:

```text
"this" / "that" -> object indicated by gaze, hand ray, pointer ray, or selection
"there" -> position indicated by gaze, hand ray, pointer ray, or cursor
```

## Example: Create a Cube and Put It on the Sphere

User says:

```text
Create a cube and put it on the sphere.
```

The parser should output multiple command frames with a binding between them.

```json
[
  {
    "intent": "create",
    "new_object": {
      "type": "cube"
    },
    "result_binding": "new_object_1"
  },
  {
    "intent": "move",
    "target": {
      "reference": "new_object_1"
    },
    "destination": {
      "relation": "on_top_of",
      "reference": {
        "type": "sphere",
        "resolution_status": "unresolved"
      }
    }
  }
]
```

This requires anaphora resolution. The word “it” refers to the cube created in the previous clause.

## Reference Resolution Rules

The reference resolver should support at least these patterns:

### Type references

```text
the sphere
the cube
the cylinder
```

Resolve by object type.

### Attribute references

```text
the red sphere
the big cube
the small blue object
```

Resolve by type plus attributes.

### Fragment answers

```text
the red one
the left one
the bigger one
that one
this one
```

If there is a pending ambiguity, apply these constraints to the pending candidates rather than the whole scene, unless configured otherwise.

### Recency references

```text
the last one
the one I just made
the previous object
```

Resolve by creation sequence or interaction sequence.

### Spatial references

```text
the one on the left
the one near the cube
the sphere behind the table
the top object
```

Resolve relative to user/camera orientation, world axes, or another object. The coordinate frame should be explicit and configurable.

### Deictic references

```text
this
that
this one
that one
there
over there
```

Resolve using current multimodal context from the app:

```json
{
  "gaze_hit_object_id": "sphere_12",
  "pointer_hit_object_id": null,
  "hand_ray_hit_object_id": "sphere_18",
  "selected_object_ids": ["cube_4"],
  "indicated_world_position": [0.3, 1.1, -2.2]
}
```

The app should provide this context to the resolver with each utterance.

## Suggested Resolver Priority

For pronouns like `it`:

1. Pending command slot, if currently clarifying.
2. Explicitly selected object.
3. Object currently indicated by pointer/hand/gaze.
4. Last interacted object.
5. Last created object.
6. Ask for clarification.

For deictic words like `this` or `that`:

1. Object indicated by pointer/hand ray.
2. Object under gaze.
3. Selected object.
4. Ask for clarification.

For “the last one”:

1. Most recently created object.
2. Most recently interacted object.
3. Ask for clarification.

These priorities should be configurable because different UX designs may prefer different behavior.

## Clarification Behavior

When resolving a reference, the resolver should return one of:

```text
resolved exactly one object
matched zero objects
matched multiple objects
needs multimodal input
needs missing required slot
```

Example questions:

```text
Which sphere?
I found three spheres. Do you mean the red one, the blue one, or the green one?
I do not see a sphere. Should I create one first?
Where should I put it?
What should I make bigger?
```

Clarification questions should be generated from the blocked slot and candidates.

If candidate objects have distinguishing properties, use them in the clarification.

For example, prefer:

```text
Do you mean the red sphere or the blue sphere?
```

instead of:

```text
Do you mean sphere_12 or sphere_18?
```

Internal IDs should normally not be spoken to the user.

## Internal Command Language

Treat the resolved commands as a small internal programming language:

```text
CREATE object WITH attributes
MOVE object TO relation(reference_object)
MOVE object TO position
SCALE object BY amount
ROTATE object AROUND axis BY degrees
DELETE object
SET property ON object TO value
SELECT object
ASK clarification FOR unresolved_slot
```

Natural language is one input method that compiles into this internal command language. UI, gaze, hand input, controller input, and other modalities should be able to fill the same command fields.

## Example Executable Commands

```json
{
  "intent": "create",
  "status": "executable",
  "new_object": {
    "type": "cube",
    "attributes": {
      "color": "blue",
      "size": "small"
    }
  },
  "destination": {
    "relation": "on_top_of",
    "reference_object_id": "sphere_18"
  }
}
```

```json
{
  "intent": "scale",
  "status": "executable",
  "target_object_id": "cube_4",
  "how": {
    "scale_multiplier": 1.25
  }
}
```

```json
{
  "intent": "set_property",
  "status": "executable",
  "target_object_id": "sphere_12",
  "property": "color",
  "value": "red"
}
```

## Implementation Requirements

The first implementation should be testable without Unity.

Suggested language-agnostic interfaces:

```text
parse(utterance: string, context: CommandContext) -> ParseResult
resolve(command: CommandFrame, scene: SceneSnapshot, context: CommandContext) -> ResolutionResult
advanceDialog(utterance: string, dialogState: DialogState, scene: SceneSnapshot, context: CommandContext) -> DialogResult
validate(command: ResolvedCommand, scene: SceneSnapshot) -> ValidationResult
execute(command: ResolvedCommand) -> ExecutionResult
```

Where:

```text
CommandContext = dialog state + selected objects + gaze/pointer/hand references + last-created/last-interacted history
SceneSnapshot = plain data representation of objects and properties
```

Keep the core parser/resolver separate from engine-specific code.

Unity should act as an adapter:

```text
Unity scene objects -> SceneSnapshot
ResolvedCommand -> Unity scene mutation
Unity gaze/hand/controller state -> CommandContext
```

## Suggested Initial Implementation Strategy

Start with a deterministic rules-based parser and resolver for a small command vocabulary. Later, optionally add an LLM-based parser that emits the same command frame schema.

Phase 1 commands:

```text
Create a cube.
Create a sphere.
Create a cube on top of the sphere.
Move the cube to the sphere.
Move the cube on top of the sphere.
Make the cube bigger.
Make it bigger.
Delete the sphere.
Select the red sphere.
```

Phase 1 object attributes:

```text
type: cube, sphere, cylinder, object
color: red, blue, green, yellow, white, black
size: small, big, large
recency: last, previous, just made
spatial: left, right, top, bottom, near
```

Phase 1 spatial relations:

```text
on_top_of
above
below
behind
in_front_of
next_to
near
left_of
right_of
```

Phase 1 clarification:

```text
which object?
which sphere?
which cube?
where should I put it?
what should I modify?
```

Phase 2 should add:

```text
multi-command utterances
anaphora: it, them, the first one, the second one
quantities: create three cubes
groups: select all red objects
richer spatial relations
undo/redo command history
LLM parser fallback
confidence scoring
```

## Parser Approach

The parser should normalize utterances into canonical forms.

Examples:

```text
"make a cube" -> create cube
"spawn a cube" -> create cube
"add a cube" -> create cube
"make the cube bigger" -> scale cube larger
"turn the sphere red" -> set_property sphere color red
"get rid of the red cube" -> delete red cube
```

The parser should not require perfect grammar. It should handle casual fragments and speech-recognition artifacts where possible.

Suggested parser output wrapper:

```json
{
  "ok": true,
  "commands": [],
  "unparsed_text": null,
  "warnings": [],
  "confidence": 0.0
}
```

If using an LLM, require it to output strict JSON matching the schema and validate the JSON before using it.

## Validation Rules

A command is executable only if:

1. The intent is recognized.
2. All required slots for the intent are filled.
3. All object references that must resolve to existing objects have concrete IDs.
4. Creation specs contain valid object types and allowed attributes.
5. Spatial relations are supported.
6. Property values are allowed for the target property.
7. The command is not blocked by safety or domain rules.

Examples:

```text
Create a cube.
```

Executable if cube is a supported creatable type.

```text
Delete the sphere.
```

Executable only if “the sphere” resolves to exactly one existing object.

```text
Make it bigger.
```

Executable only if “it” resolves to exactly one existing object.

## Test Cases

Use plain data tests before integrating into Unity.

### Test 1: Simple create

Scene: empty

Input:

```text
Create a cube.
```

Expected:

```text
intent=create
new_object.type=cube
status=executable
```

### Test 2: Create relative to one object

Scene: one sphere

Input:

```text
Create a cube on top of the sphere.
```

Expected:

```text
intent=create
new_object.type=cube
destination.relation=on_top_of
destination.reference.resolved_id=<only sphere id>
status=executable
```

### Test 3: Ambiguous reference

Scene: three spheres

Input:

```text
Create a cube on top of the sphere.
```

Expected:

```text
status=ambiguous
blocked slot=destination.reference
question="Which sphere?"
pending command stored
```

### Test 4: Clarify by color

Scene: three spheres, one red

Prior state: pending command from Test 3

Input:

```text
The red one.
```

Expected:

```text
pending destination reference resolves to red sphere
status=executable
```

### Test 5: Pronoun resolution

Scene: one selected cube

Input:

```text
Make it bigger.
```

Expected:

```text
intent=scale
target.resolved_id=<selected cube id>
scale direction=larger
status=executable
```

### Test 6: Deictic resolution

Scene: multiple objects, hand ray hits sphere_18

Input:

```text
Delete that.
```

Expected:

```text
intent=delete
target.resolved_id=sphere_18
status=executable
```

### Test 7: Multi-command with binding

Scene: one sphere

Input:

```text
Create a cube and put it on the sphere.
```

Expected:

```text
first command=create cube with result binding
second command=move bound cube on_top_of sphere
status=executable or staged executable sequence
```

### Test 8: None found

Scene: no spheres

Input:

```text
Delete the sphere.
```

Expected:

```text
status=none_found
question or recovery message indicates no sphere was found
```

## Clarification Follow-Up Examples

If pending command asks “Which sphere?” then these should be valid follow-ups:

```text
the red one
this one
that one
the left one
the last one
the big one
the one near the cube
```

The follow-up parser should understand that these are not complete commands. They are slot-resolution fragments for the pending command.

## Notes for Codex

Implement this as a small, modular library with unit tests first. Do not embed logic directly in Unity scene components.

Recommended module layout:

```text
command_understanding/
  schema.py or schema.cs
  ontology.py or ontology.cs
  parser.py or parser.cs
  resolver.py or resolver.cs
  dialog_state.py or dialog_state.cs
  validator.py or validator.cs
  executor_interface.py or executor_interface.cs
  tests/
```

If the target project is C#, use plain serializable classes or records. If the target project is Python, use dataclasses or Pydantic models. In either case, keep the JSON representation stable so an LLM parser or external service can be plugged in later.

The first milestone is not perfect English understanding. The first milestone is a robust loop:

```text
parse -> resolve -> ask clarification when needed -> apply follow-up -> execute when resolved
```

The most important behavior is this:

```text
If the system understands the action but not the exact object, keep the command alive and ask a specific clarification question.
```

