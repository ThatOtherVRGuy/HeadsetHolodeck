# Unity VR Speech Intent System

## Overview

This feature adds a speech-driven intent layer to a Unity VR application so the user can speak naturally and have the app interpret both:

1. the **transcript** of what was said, and  
2. a **structured command** describing the user's intent.

The system is designed for VR interaction where speech may be combined with spatial context such as:

- where the user is pointing
- where the user's hands are
- what object was last created or interacted with
- what world is currently loaded

The immediate use case is to support commands such as:

- "Put me on a beach on a sunny day"
- "End program"
- "Arch"
- "Exit"
- "Put the sun there"
- "Make it night time"
- "Place a chair here"
- "Make it bigger"
- "Make it twice as big"
- "That is too big"
- "Make it 10% smaller"
- "Rotate it by 45 degrees"
- "Flip it upside down"
- "Move that here"

---

## Goals

The system should:

- record user speech in Unity
- transcribe speech using OpenAI
- convert the transcript into a strict, structured command model
- incorporate VR spatial context into interpretation
- resolve ambiguous references like "it" and "that"
- support follow-up clarification when intent is ambiguous
- dispatch commands cleanly into Unity scene behaviors
- remain extensible for future world-editing and object-editing features

---

## High-Level Architecture

The feature is divided into several layers.

### 1. Audio Capture Layer

Unity records microphone input into a WAV clip.

Responsibilities:

- start/stop microphone recording
- package audio into WAV bytes
- provide the bytes to the speech service

Primary component:

- `MicrophoneWavRecorder`

---

### 2. Speech + Intent Interpretation Layer

This layer performs two separate AI calls:

#### Stage A: Transcription
Send recorded audio to OpenAI transcription endpoint and receive plain text transcript.

#### Stage B: Intent Extraction
Send the transcript plus scene/spatial context to OpenAI using a structured JSON schema so the model returns a strict command object.

This separation is useful because:

- you retain the raw transcript for logs/debugging/UI
- you can interpret the same transcript differently depending on current context
- the structured command is easier and safer to consume in code than free-form text

Primary component:

- `OpenAiSpeechIntentService`

Outputs:

- raw transcript
- structured command object
- optional clarification question

---

### 3. Context Gathering Layer

Speech alone is not enough for commands like:

- "Put the sun there"
- "Move that here"
- "Make it bigger"

The system therefore gathers scene context before asking the AI to interpret intent.

#### Spatial Context
Examples:

- left/right hand pointing rays
- hit point where a hand ray intersects the world
- hand midpoint
- head pose
- current gaze or controller forward vector

Primary component:

- `SpatialContextProvider`

#### Semantic Scene Context
Examples:

- current world root
- available scene entities
- object names / aliases
- current lighting state
- whether a UI panel is available
- current selection or last interacted target

Primary component:

- `SceneSemanticContextProvider`

---

### 4. Reference Resolution and Memory Layer

Many natural commands rely on memory and pronouns.

Examples:

- "Make it bigger"
- "That is too big"
- "Rotate it by 45 degrees"
- "Move that here"

The system therefore maintains a lightweight interaction memory.

#### Interaction Memory stores things like:
- current world root
- last created object
- last interacted object
- last moved/scaled/rotated target
- last pointed target

Primary component:

- `InteractionMemory`

#### Entity Resolution
Maps phrases like:

- "it"
- "that"
- "the tree"
- "the sun"
- "that chair"

into a concrete Unity object.

Primary component:

- `SceneEntityResolver`

---

### 5. Action Dispatch Layer

Once a structured command is available, a dispatcher routes it to the correct Unity behavior.

Primary component:

- `WorldActionDispatcher`

This layer should be kept separate from AI logic. The AI decides **what** the user means; Unity code decides **how** that action is executed.

---

## Why Structured Commands Matter

A speech system should not directly execute free-form LLM text. Instead, it should convert natural language into a limited command schema.

Benefits:

- safer execution
- simpler code
- easier debugging
- predictable branching
- easier future expansion

Example:

Instead of returning:

> "The user wants the currently selected object to become a bit larger."

The system returns something like:

```json
{
  "intent": "ScaleTarget",
  "targetReference": "LastInteracted",
  "scaleMode": "RelativeMultiplier",
  "scaleMultiplier": 1.2
}
```

That is much easier for Unity to execute reliably.

---

## Command Model

The revised command model includes both high-level scene commands and object/world transformation commands.

### Core Intents

- `GenerateWorld`
- `SwitchToStaticWorld`
- `ShowUi`
- `HideUi`
- `SetSunDirection`
- `SetLightingPreset`
- `PlaceObject`
- `MoveTarget`
- `ScaleTarget`
- `RotateTarget`
- `AskClarification`
- `NoOp`

---

## Intent Details

### GenerateWorld

Used for utterances such as:

- "Put me on a beach on a sunny day"
- "Take me to a snowy mountain village"
- "I want to be in a desert at sunset"

Typical fields:

- `worldPrompt`
- `styleHints`
- optional environmental metadata

Execution:

- send prompt to WorldLabs
- receive/load resulting world
- register returned world root into interaction memory

---

### SwitchToStaticWorld

Used for utterances such as:

- "End program"
- "Stop this"
- "Go back"
- "Load the default environment"

Execution:

- disable or unload generated world
- enable pre-authored static model
- update interaction memory world root

---

### ShowUi / HideUi

Used for utterances such as:

- "Arch"
- "Exit"
- "Open menu"
- "Show controls"

Execution:

- display or hide a UI object
- can also trigger voice/TTS confirmation if needed

---

### SetSunDirection

Used for utterances such as:

- "Put the sun there"
- "Move the sun over there"

Required context:

- user hand or pointing vector
- possibly a hit point or direction in world space

Execution:

- align the directional light using the resolved pointing direction

---

### SetLightingPreset

Used for utterances such as:

- "Make it night time"
- "Set it to dusk"
- "Bright daylight"
- "Make it cloudy"

Execution:

- apply a named lighting preset
- possibly modify skybox, exposure, ambient, fog, and directional light color/intensity

---

### PlaceObject

Used for utterances such as:

- "Place a chair here"
- "Put a tree there"
- "Create a rock here"

Required context:

- object name / semantic description
- placement point, usually derived from hand rays or midpoint
- optional orientation and scale

Execution:

- call an object-generation or retrieval service
- instantiate object
- place it at resolved position
- register as last created object in memory

---

### MoveTarget

Used for utterances such as:

- "Move that here"
- "Put it over there"
- "Move the chair to here"

Required pieces:

- target reference
- destination reference

Destination may come from:

- pointed location
- hand midpoint
- named anchor
- explicit coordinates in future versions

Execution:

- resolve the target
- resolve destination
- reposition target transform

---

### ScaleTarget

Used for utterances such as:

- "Make it bigger"
- "Make it twice as big"
- "Make it 10% smaller"
- "That is too big"
- "Shrink it"

Possible modes:

- qualitative increase/decrease
- relative multiplier
- percentage adjustment
- absolute target scale (future)

Examples:

- "Make it bigger" → multiplier like `1.2`
- "That is too big" → multiplier like `0.8`
- "Make it twice as big" → multiplier `2.0`
- "Make it 10% smaller" → multiplier `0.9`

Execution:

- resolve target from memory or scene reference
- apply scale change
- update interaction memory

This can apply to:
- current generated world root
- last created object
- last interacted object
- explicitly named object

---

### RotateTarget

Used for utterances such as:

- "Rotate it by 45 degrees"
- "Turn it 90 degrees"
- "Flip it upside down"
- "Rotate the chair around"

Defaults:

- if no axis is specified, default to Y axis
- if "upside down" is used, interpret as X axis rotation by 180 degrees

Possible fields:

- target reference
- axis (`X`, `Y`, `Z`, or `Unknown`)
- degrees
- relative vs absolute rotation mode

Examples:

- "Rotate it by 45 degrees" → Y + 45
- "Flip it upside down" → X + 180

If ambiguous, the system may produce `AskClarification`.

---

### AskClarification

Used when the system is not confident enough to safely execute.

Examples:

- "Rotate it" with no clear axis or angle
- "Move that there" when neither object nor destination is clear
- "Make it smaller" when no target can be resolved

Example response:

```json
{
  "intent": "AskClarification",
  "question": "About which axis would you like to rotate?"
}
```

Unity can then:

- speak the question with TTS
- display it in a world-space UI
- wait for follow-up speech input

---

## Reference Resolution

A key feature is handling pronouns and implied targets.

### Why it matters

Natural VR speech will often omit explicit object names.

Examples:

- "Make it bigger"
- "Move that here"
- "Rotate it"
- "That is too large"

The system must resolve the referent from context.

### Target Reference Types

Suggested target references include:

- `CurrentWorld`
- `LastCreatedObject`
- `LastInteractedObject`
- `LastPointedObject`
- `NamedObject`
- `ExplicitSemanticTarget`
- `Unknown`

### Resolution Priority

A reasonable resolution order is:

1. explicitly named object
2. currently pointed object
3. last interacted object
4. last created object
5. current world root
6. fail into clarification

This priority may be tuned depending on app behavior.

---

## Spatial Grounding

Several commands depend on hand and spatial context.

### Example: "Put the sun there"

Interpretation steps:

1. determine which hand is active or pointing
2. capture the pointing vector
3. transform that direction into world space
4. align the directional light accordingly

### Example: "Move that here"

Interpretation steps:

1. resolve "that" to a target object
2. resolve "here" to a destination point
3. move the target to that point

### Example: "Place a chair here"

Interpretation steps:

1. extract object description: `chair`
2. determine placement point from pointing or hands
3. instantiate object there
4. register it as last created target

---

## Unity Components

The package currently includes or assumes the following main components.

### `MicrophoneWavRecorder`

Handles microphone recording and WAV packaging.

Responsibilities:

- begin recording
- stop recording
- convert captured clip to WAV bytes

---

### `OpenAiSpeechIntentService`

Handles AI communication.

Responsibilities:

- send audio for transcription
- send transcript plus context for intent extraction
- parse returned command JSON
- surface transcript and structured command to the rest of the app

---

### `SpatialContextProvider`

Collects pose and pointing data.

Responsibilities may include:

- left/right hand transform access
- forward vectors
- raycasts
- hit points
- head/camera transform
- hand midpoint

---

### `SceneSemanticContextProvider`

Builds a semantic summary of the current scene for the AI prompt.

Examples:

- current world name or prompt
- scene entities and aliases
- current UI availability
- known lighting states
- active interactive targets

---

### `InteractionMemory`

Stores recent references used for pronoun resolution.

Tracks things like:

- current world root
- last created object
- last interacted object
- last transformed object

---

### `SceneEntityResolver`

Resolves target references from command objects into actual GameObjects or transforms.

Can resolve:

- pronouns
- aliases
- canonical names
- current world root

---

### `SpeechIntentTrackable`

A component to attach to important objects so they can participate in semantic resolution.

May include:

- canonical name
- aliases
- category
- whether object is movable/scalable/rotatable
- whether object is world root or environment anchor

---

### `TargetTransformController`

Executes transform operations on resolved targets.

Responsibilities:

- move targets
- scale targets
- rotate targets
- clamp or constrain operations if needed

---

### `WorldActionDispatcher`

Central router that maps structured intent to scene actions.

Responsibilities:

- call WorldLabs bridge for `GenerateWorld`
- toggle UI
- switch to static world
- update lighting
- move/scale/rotate objects
- emit clarification messages

---

## Interaction Flow

A typical end-to-end flow looks like this.

### Example: "Make it twice as big"

1. User speaks.
2. Unity records microphone input.
3. Audio is sent to OpenAI transcription.
4. Transcript returned: `"Make it twice as big"`
5. Unity gathers context:
   - current world loaded
   - last interacted target
   - last created object
6. Transcript + context sent to structured intent endpoint.
7. Command returned:

```json
{
  "intent": "ScaleTarget",
  "targetReference": "LastInteractedObject",
  "scaleMode": "RelativeMultiplier",
  "scaleMultiplier": 2.0
}
```

8. Resolver finds the correct target.
9. `TargetTransformController` applies scaling.
10. `InteractionMemory` updates last transformed target.

---

## Ambiguity Handling

The system should not guess recklessly when the result could be confusing or destructive.

### When to ask instead of act

Examples:

- no clear target
- no clear destination
- missing axis for rotation when not inferable
- multiple equally likely objects
- utterance too vague

### Example

User says:

- "Rotate it"

Potential safe response:

- "About which axis would you like to rotate?"

Or:

- "How many degrees should I rotate it?"

This is represented as `AskClarification`.

---

## Suggested Command Schema Concepts

A practical command object may include fields like these:

- `intent`
- `confidence`
- `rawTranscript`
- `targetReference`
- `targetName`
- `destinationReference`
- `worldPrompt`
- `objectDescription`
- `lightingPreset`
- `axis`
- `degrees`
- `scaleMode`
- `scaleMultiplier`
- `question`

Not every intent uses every field.

---

## Example Interpretations

### Example 1
Speech:
> Put me on a beach on a sunny day

Command:
- `GenerateWorld`
- `worldPrompt = "beach on a sunny day"`

### Example 2
Speech:
> End program

Command:
- `SwitchToStaticWorld`

### Example 3
Speech:
> Put the sun there

Command:
- `SetSunDirection`
- direction resolved from hand ray

### Example 4
Speech:
> Make it night time

Command:
- `SetLightingPreset`
- `lightingPreset = Night`

### Example 5
Speech:
> Place a chair here

Command:
- `PlaceObject`
- `objectDescription = chair`
- placement point resolved from hand context

### Example 6
Speech:
> Make it bigger

Command:
- `ScaleTarget`
- target from memory
- moderate upscale multiplier

### Example 7
Speech:
> That is too big

Command:
- `ScaleTarget`
- target from memory
- moderate downscale multiplier

### Example 8
Speech:
> Rotate it by 45 degrees

Command:
- `RotateTarget`
- axis defaults to Y
- degrees = 45

### Example 9
Speech:
> Flip it upside down

Command:
- `RotateTarget`
- axis = X
- degrees = 180

### Example 10
Speech:
> Move that here

Command:
- `MoveTarget`
- target resolved from context
- destination resolved from pointing / hand location

---

## World vs Object Targeting

One subtle but important requirement is that commands may refer to either:

- the whole generated world, or
- a spawned object inside that world

Examples:

- after a new world loads, "Make it bigger" may refer to the world root
- after placing a chair, "Make it bigger" may refer to the chair

This is why `InteractionMemory` must be updated carefully whenever:

- a world is loaded
- an object is spawned
- an object is selected
- a transform operation completes

The memory model is what lets follow-up speech feel natural.

---

## Safety and Practical Constraints

For now, direct OpenAI calls from the Unity app may be acceptable for prototype work, but it is not appropriate for a production application to expose a secret API key in a client build.

Later, the speech service can be moved behind a proxy/backend. The architecture already supports that transition because the Unity-side system is separated into:

- recorder
- context providers
- intent service
- dispatcher

Only the service transport needs to change.

---

## Extension Points

This system is designed to grow.

Future commands may include:

- duplicate object
- delete object
- undo last action
- recolor object
- move sun higher/lower
- set weather
- create path or road
- resize only along one axis
- snap object to floor
- place object on top of another object
- multi-step world editing dialogs

Potential future reference types:

- selected object
- nearest object of type X
- object under left hand
- object under right hand
- object currently in focus

Potential future contextual signals:

- gaze target
- pinch selection
- controller trigger target
- semantic room anchors
- world terrain sampling for object placement

---

## Suggested Development Order

A practical rollout order is:

1. Implement transcript + structured intent roundtrip.
2. Support:
   - `GenerateWorld`
   - `SwitchToStaticWorld`
   - `ShowUi`
   - `SetLightingPreset`
3. Add `InteractionMemory`.
4. Add `ScaleTarget`, `RotateTarget`, `MoveTarget`.
5. Add clarification flow.
6. Add object placement / generation integration.
7. Improve semantic resolution and scene awareness.

This lets the basic speech UX become useful early while still leaving room to grow into a richer world-editing assistant.

---

## Summary

This feature is best understood as a **speech-driven command interpretation layer for VR**.

It combines:

- microphone capture
- OpenAI transcription
- structured intent extraction
- hand/spatial context
- scene memory
- target resolution
- Unity action dispatch

The key idea is that natural language should not directly control scene logic. Instead, natural speech is converted into a constrained command model that Unity can execute safely and predictably.

This is what makes advanced interactions possible, including:

- world generation by voice
- contextual object editing
- pronoun-based follow-up commands
- point-and-speak spatial manipulation
- clarification when ambiguity is too high

With that structure in place, commands like "Move that here," "Make it bigger," and "Put the sun there" become manageable, extensible, and robust enough for a VR workflow.
