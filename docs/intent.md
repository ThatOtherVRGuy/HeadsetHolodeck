# Intent

`intent` is the app's machine-readable answer to **"what did the user ask for?"** An accepted
utterance becomes one `VoiceIntentCommand`: the `intent` field names the action, and the remaining
fields carry its arguments. Exactly one dispatcher executes it.

This page documents the speech-intent feature end to end — the pipeline, the command object, every
intent type with an example utterance, several worked examples, the routing and safety rules, how to
add an intent, and how to debug one. It was checked against the source in this checkout on
2026-10-29; if this page and the code disagree, the enum and the schema are authoritative.

*Written by **DeepSeek** on 2026-10-29. This page is intentionally open to other DeepSeek agents to
correct, extend, or reorganize; see [Provenance](#provenance).*

> **Not the build credential "intent".** A build's *credential intent* (`device`, `private`,
> `distribution`) is a different use of the word. That boundary is documented in the
> [README](../README.md#build-credential-boundary) and [AGENTS.md](../AGENTS.md). This page is only
> about spoken-command intent.

- [Why it exists](#why-it-exists)
- [Examples at a glance](#examples-at-a-glance)
- [Pipeline](#pipeline)
- [The command object](#the-command-object)
- [The intent catalog](#the-intent-catalog)
- [Worked examples](#worked-examples)
- [How routing chooses](#how-routing-chooses)
- [Configuration](#configuration)
- [Adding a new intent](#adding-a-new-intent)
- [Debugging an intent](#debugging-an-intent)
- [Limits and gotchas](#limits-and-gotchas)
- [Source map](#source-map)
- [Provenance](#provenance)

## Why it exists

Speech is noisy. One transcript can be a command, a question, a chat sentence, or a mishearing of
the last thing the user said. The intent layer exists to keep three decisions separate:

1. **What was said** — the transcript, preserved verbatim on the command.
2. **What was meant** — `intent` plus typed, named fields, not free text parsed later by each
   controller.
3. **Whether to act** — `should_execute`, plus the router's dialogue and safety rules.

That separation is what makes the same command reachable from several inputs (wake-word transcript,
push-to-talk audio, the LCARS keyboard, an Android broadcast) without four copies of the command
grammar. It also gives the app a principled way to say *"I don't know"*: `Unknown` and
`AskClarification` are ordinary outcomes, not errors, and neither one executes.

## Examples at a glance

| You say | Intent | What happens |
|---|---|---|
| "Computer, create a cafe in Paris in the 1920s on a rainy day." | `GenerateWorld` | Sends the prompt to World Labs ([details](#1-create-a-world-from-a-sentence)) |
| "Computer, create a ball in my left hand." | `PlaceObject` | Places a primitive at the left-hand anchor ([details](#2-put-an-object-in-your-hand)) |
| "Computer, make all red cubes blue." | `SetTargetMaterial` | Applies the material to every matching target ([details](#3-change-every-matching-target)) |
| "Computer, load Cozy Christmas Cabin." | `LoadWorldConfig` | Restores the saved world instead of generating one ([details](#4-load-a-saved-world-by-name)) |
| "Computer, add forest birds and wind." | `CreateAudioSource` | Creates two ambience layers without touching the visuals ([details](#5-add-layered-ambience-then-adjust-it)) |
| "Computer, show panorama." | `ShowPanoWorld` | Switches view locally, with no API call ([details](#6-switch-views-without-the-network)) |
| "Computer, make the cube spin." | `AttachBehavior` | Adds a continuous runtime behavior ([details](#7-a-behavior-and-a-physics-tweak)) |
| "Computer, rotate it." → "45." | `RotateTarget` | Asks one follow-up, then rotates ([details](#8-one-clarification-round-trip)) |

The rest of this page explains how each of those becomes a `VoiceIntentCommand` and how to extend
the set.

## Pipeline

```text
wake word / push-to-talk / LCARS keyboard / debug broadcast
        │
        ▼
HeadsetHolodeckVoiceController
  (inline text, on-device ASR text, or command audio + fallback text)
        │
        ▼
HeadsetHolodeckCommandRouter  ── normalizes all-caps ASR and "create a" mishearings
        │  text                                  audio (WAV) + fallback text
        ▼                                        ▼
VoiceCommandRouter.SubmitTypedCommand      VoiceCommandRouter.SubmitAudioCommand
        │                                        │
        └───────────────┬────────────────────────┘
                        ▼
             OpenAiSpeechIntentService
        ┌───────────────────────────────────────────────┐
        │ audio path:  Whisper transcript               │
        │ both paths:  local preflight (8 intents)      │
        │ otherwise:   OpenAI Responses API, strict     │
        │              JSON schema → VoiceIntentCommand │
        └───────────────────────────────────────────────┘
                        │
                        ▼
             VoiceCommandRouter.HandleResult
   pending dialogue · local overrides · generation safety · speech
                        │
                        ▼
             WorldActionDispatcher.Execute
                        │
                        ▼
 placement · transform · audio · lights · save/load · view · UI · app
```

| Stage | Code | Responsibility |
|---|---|---|
| Wake | `Assets/App/Shared/Command/SpeechIntent/Runtime/VoiceActivation/VadAsrWakeTrigger.cs` | Local VAD/ASR; emits `WakeTriggerResult` with an inline command or a command window |
| Voice control | `.../VoiceActivation/HeadsetHolodeckVoiceController.cs` | Listening state, cues, cooldown; forwards text or audio |
| App bridge | `.../VoiceActivation/HeadsetHolodeckCommandRouter.cs` | Trims, de-capitalizes all-caps ASR, rewrites common "create a" mishearings, forwards |
| Router | `.../VoiceCommandRouter.cs` | Single entry point; clarification, overrides, safety, TTS |
| Interpreter | `.../OpenAiSpeechIntentService.cs` | Transcription, local preflight, structured model request, JSON decode |
| Local grammar | `.../LocalTypedIntentParser.cs` | Deterministic parses; fallback and preflight |
| Dialogue state | `Assets/App/Shared/Command/CommandUnderstanding/Runtime/CommandDialogStateManager.cs` | One pending clarification and its slot |
| Dispatcher | `.../WorldActionDispatcher.cs` | `switch (command.intent)` → one handler per intent |
| Controllers/services | `ObjectPlacementController`, `TargetTransformController`, `AudioWorldActionController`, `LightRigController`, `ViewModeController`, `WorldConfigStore`/`WorldConfigRestorer`, `UiPanelController`, … | Apply the effect |

There are two ways to arrive with audio:

- **Audio path** (`SubmitAudioCommand`): the recognizer supplied WAV bytes. The service asks OpenAI
  `whisper-1` for a transcript, applies the local preflight to that transcript, and otherwise asks
  the intent model.
- **Text path** (`SubmitTypedCommand`): the bytes are not available (on-device ASR already produced
  text), or the input was the keyboard/broadcast. The router applies the same preflight, then the
  model if configured, then a local-only parse if it is not.

The local grammar is therefore a **preflight and fallback**, not the primary interpreter for every
phrase. Only the eight deterministic intents below skip the model on a normal configured build:

`LoadWorldConfig`, `SetPositionSnap`, `SetAngleSnap`, `SetSnapEnabled`, `GrabWorld`, `Show3dWorld`,
`ShowPanoWorld`, `ShowMeshWorld`.

They are listed in `OpenAiSpeechIntentService.TryBuildLocalPreflightCommand`. The rule for adding one
is: *the phrase is unambiguous, deterministic, and a model round-trip can only add latency or a
misclassification* (view switches, snapping, grabbing the world, loading a saved world by name).

## The command object

`VoiceIntentCommand` lives in
[`VoiceIntentSchemas.cs`](../Assets/App/Shared/Command/SpeechIntent/Runtime/VoiceIntentSchemas.cs).
Every field has a default. The model schema requires every **model-facing** field; `user_confirmed`
and `object_choice_action` belong to the dialogue layer and are deliberately not in the schema, while
the local parser sets only what it knows.

### Identity and control

| Field | Type | Meaning |
|---|---|---|
| `transcript` | string | The words the app acted on, verbatim. Used by guards and logs; never rewritten by handlers |
| `intent` | `VoiceIntentType` | The action. Serialized as its name, e.g. `"PlaceObject"` |
| `confidence` | float 0–1 | **Advisory only.** It is carried through and logged; nothing gates execution on it |
| `should_execute` | bool | The hard gate. `false` means "do not act". The dispatcher logs `Command not executed` and returns |
| `spoken_response` | string | Optional acknowledgement/question for TTS and the status panel. Handlers may overwrite it with a failure message |
| `user_confirmed` | bool | Set only when the user answered a pending confirmation. Lets a follow-up like "yes" authorize generation without its own prompt |
| `reason` | string | Free-text diagnostic from the producer |

`SpeechIntentResult` wraps the command for callers that need more than the command:

| Field | Meaning |
|---|---|
| `success` | The pipeline reached a result. A successful result may still be `Unknown`/`AskClarification` |
| `transcript` | The transcript for this turn |
| `command` | The `VoiceIntentCommand` |
| `raw_model_json` | The model's raw output when the intent came from OpenAI; empty for local/preflight results |
| `error` | Transport, transcription, or JSON failure text; shown to the user through the router's mapping |

### What to act on

| Field | Used for | Meaning |
|---|---|---|
| `target_reference` | target-taking intents | `None`, `LastCreatedObject`, `LastInteractedTarget`, `LastCreatedOrInteracted` ("it"), `PointedObject` ("this"/"that"), `NamedObject`, `CurrentSelection`, `CurrentWorld`, `All` |
| `target_name` | target-taking intents | Object class or name, e.g. `cube` |
| `target_material_prompt` | target resolution | Material/color qualifier used to **find** the target, e.g. `red` in "the red cube" |
| `target_spatial_qualifier` | target resolution | `topmost` / `bottommost` for "the one on top" |
| `describe_query` | `DescribeTarget` | `description`, `name`, or `full` |
| `target_entity` | world/light/audio/generation | A coarse entity such as `world`, `Me`, `sun`, `object`, `all` |

Resolution order and failure text live in `SceneEntityResolver` / `SceneFocusContext`. A
target-taking intent that cannot resolve asks a question (see
[clarification slots](#clarification-slots)) instead of picking a guess.

### Where

| Field | Values | Meaning |
|---|---|---|
| `spatial_reference` | `None`, `PointingRay`, `PointingHit`, `HandMidpoint`, `HeadForward`, `WorldOrigin`, `RelativeToMe`, `BodyAnchor`, `RelativeToTarget` | Which frame the action is placed in |
| `body_anchor` | `None`, `Head`, `LeftHand`, `RightHand` | The user's body part, for `BodyAnchor` |
| `target_hand` | `None`, `Left`, `Right`, `Either`, `Both` | Hand used for the action / hand attachment |
| `relative_direction` | `Forward`, `Back`, `Left`, `Right`, `Up`, `Down`, `InFront`, `Behind` | Direction in **Me's local frame**; "my left" is not world left |
| `relative_distance_meters` | meters | Distance for relative movement/placement; `0` uses the controller default |
| `placement_mode` | `surface`, `me_frame`, `target_local`, `under_foot`, … | How the position is interpreted |

Distances are always meters in the command. The model prompt includes conversion examples
(10 inches → 0.254, 3 feet → 0.9144, 50 cm → 0.5), and the local `DistanceUnitParser` does the same
for spoken units.

### What kind of action

| Group | Fields | Notes |
|---|---|---|
| World generation | `world_prompt`, `generation_model` | `world_prompt` is the descriptive text sent to World Labs; `generation_model` is `draft`/`fast`/`standard`/`high` |
| Object creation | `object_name`, `object_width_meters`, `object_weightless`, `object_choice_action` | A primitive name places a built-in; an unknown noun can go to an object-generation provider. `object_choice_action` is the answer to a cached-object choice |
| Transform | `scale_multiplier`, `reset_to_default_scale`, `rotation_axis`, `rotation_degrees` | `rotation_axis` defaults to Y for spoken rotation |
| World manipulation | `position_snap_meters`, `angle_snap_degrees`, `snap_enabled`, `world_manipulation_action` | `"grab"` / `"release"` for `GrabWorld` |
| Content | `content_path`, `config_name` | Local path or URL for splat/panorama; saved-world name for save/load |
| Audio | `sound_prompt`, `sound_prompts`, `sound_category`, `sound_species`, `sound_provider`, `sound_count`, `sound_max_duration_seconds`, `audio_loop`, `audio_volume`, `audio_volume_delta`, `audio_playback_mode`, `audio_interval_seconds`, `audio_interval_variance_seconds`, `audio_control`, `audio_muted`, `audio_play_now`, `audio_spatial_blend` | `sound_prompts` carries multiple layers; `audio_control` is `stop`, `louder`, `softer`, `mute`, `unmute`, `play_now`, `make_ambient`, `make_spatialized`, … |
| Lights | `light_type`, `light_color_prompt`, `light_action`, `light_intensity`, `light_range`, `light_spot_angle` | `light_type`: `point`, `spot`, `directional`, `ambient`, `flashlight` |
| Behaviours | `behavior_name`, `behavior_action`, `behavior_secondary_target_name`, `behavior_speed`, `behavior_radius`, `behavior_axis`, `behavior_stop_all` | `behavior_name`: `spin`, `orbit`, `throw`, `follow_hand`, `attach_to_hand` |
| Physics | `physics_action`, `physics_mass` | `set_weightless`, `disable_gravity`, `enable_gravity`, `set_mass` |
| Images/UI | `image_search_query`, `ui_panel` | `ui_panel` is normalized by `UiPanelController` (`arch`/`menu` → `arch_menu`, `hide_arch` hides it) |

The full field list with tooltips is the class itself; the table groups it so you can find the right
field without reading 150 lines of declarations.

## The intent catalog

`VoiceIntentType` currently has **55 values** (0–54). The enum in `VoiceIntentSchemas.cs` is the
source of truth; if you add a value, add it here, to the model schema when the model can produce it,
and to the dispatcher.

### World and session

| Intent | Say something like | Effect |
|---|---|---|
| `GenerateWorld` | "create a Japanese garden" | Starts a World Labs generation through `VoiceToWorldLabsPluginCoordinator`. Guarded: the transcript must start with create/generate/build, "make a world/scene/environment", or "put/place/take/bring/send/transport me into/on/to…" |
| `SwitchToStaticWorld` | "end program" | Destroys world audio, restores the static holodeck, resets `Me`, clears the info panel, shows the Arch |
| `LoadWorldConfig` | "load Redwood Dawn" | Finds a saved world by display name (exact, then prefix, then contained, punctuation-insensitive) and restores it. An empty `config_name` opens My Worlds |
| `SaveWorldConfig` | "save this world" / "save as Redwood Dawn" | Journaled save; auto-captures a thumbnail if the world has none. A save-as name collision asks overwrite/cancel |
| `SetGenerationModel` | "use high quality" | Selects `draft`, `fast`, `standard`, or `high` |

### View, UI, and inspection

| Intent | Say something like | Effect |
|---|---|---|
| `ShowUi` | "show arch" / "hide arch" / "show status" | Shows or hides the named LCARS panel |
| `Show3dWorld` | "show 3d" | Switches to the splat view; says "3D is not available for this world." when there is none |
| `ShowPanoWorld` | "show panorama" | Switches to the panorama view; same availability check |
| `ShowMeshWorld` | "show mesh" | Switches to the collision-mesh view |
| `SetProxyVisibility` | "show light proxies" | Shows/hides runtime proxy visuals (`all`, `light`, `audio`) |
| `DescribeTarget` | "what is this?" | Describes the focused/held/named object; "what is this called?" returns its name |

### Objects, targets, and behaviors

| Intent | Say something like | Effect |
|---|---|---|
| `PlaceObject` | "create a red cube in front of me" | Places a built-in primitive, or starts object generation for an unknown noun when a provider is configured. Keeps color/size/weight attributes on the command |
| `SelectCachedObject` | "use saved" / "create new" / "cancel" | Answers a pending cached-object choice; not a standalone command and not in the model schema (it is produced by the local reply parser) |
| `DeleteTarget` | "delete all cubes" | Deletes matching app-created objects or world audio, never UX or system objects |
| `SetTargetMaterial` | "make it red" | Applies a material/color/finish to one or all matching targets |
| `ModifyPhysics` | "make it weightless" | Changes gravity, kinematic state, or mass on an existing Rigidbody |
| `AttachBehavior` | "make the cube spin" | Adds a curated runtime behavior: spin, orbit, throw, follow hand, attach to hand |
| `StopBehavior` | "stop all behaviors" | Stops one behavior, behaviors on a target, or every behavior |
| `MoveTarget` | "move the cube up one meter" | Translates a target, the world, or `Me` in a relative frame |
| `ScaleTarget` | "make it twice as big" | Scales a target (or `Me`) by `scale_multiplier` |
| `RotateTarget` | "rotate it 45 degrees" | Rotates a target/world around X, Y, or Z |
| `ResetTransform` | "reset me" | Restores position, rotation, and scale |

### Snap, world grab, and spawn points

| Intent | Say something like | Effect |
|---|---|---|
| `SetPositionSnap` | "snap to 10 centimeters" | Sets the world-manipulation translation grid; zero disables |
| `SetAngleSnap` | "snap to 30 degrees" | Sets the rotation increment; zero disables |
| `SetSnapEnabled` | "snap off" | Master switch for both snap modes, preserving the increments |
| `GrabWorld` | "grab world" / "release world" | Arms/commits one- or two-hand world manipulation |
| `SaveSpawnPoint` | "save spawn point" | Stores the current `Me` pose in the world's spawn list |
| `NextSpawnPoint` | "next spawn point" | Moves `Me` to the next saved spawn |
| `PreviousSpawnPoint` | "previous spawn point" | Moves `Me` to the previous saved spawn |
| `RemoveSpawnPoint` | "remove this spawn point" | Removes the current/last-used spawn |
| `RemoveAllSpawnPoints` | "delete all spawn points" | Clears the world's spawn list |
| `SuggestSpawnPoint` | "suggest spawn point" | Moves `Me` to the estimated pose without saving it |

### Content, camera, and images

| Intent | Say something like | Effect |
|---|---|---|
| `LoadSplat` | "`/sdcard/Download/scene.spz`" | Loads a local or remote `.spz`/`.ply` |
| `LoadPanorama` | "`/sdcard/Download/pano.jpg`" | Loads a local or remote panoramic image |
| `CaptureWorldThumbnail` | "capture thumbnail" | Renders the loaded world's card image after a 3-second countdown |
| `CaptureWorldPanorama` | "capture panorama" | Renders a 360 panorama into the world folder |
| `CaptureHeadsetCamera` | "open camera" | Opens the live headset-camera preview |
| `ConfirmHeadsetCameraCapture` | "OK" / "shoot" / "capture" | Saves the current preview frame as the image prompt |
| `GenerateWorldFromCapture` | "create world from image" | Uses the captured image plus guidance as a world prompt |
| `GenerateObjectFromCapture` | "create object from image" | Uses the captured image plus guidance as an object prompt |
| `SearchImages` | "search images redwood forest" | Searches the configured image provider (Pixabay) |
| `SelectImageSearchResult` | "use this image" | Makes the selected result the current image prompt |
| `NextImageSearchResult` | "next image" | Advances the image search selection |
| `PreviousImageSearchResult` | "previous image" | Goes back one image search result |

### Audio and lighting

| Intent | Say something like | Effect |
|---|---|---|
| `CreateAudioSource` | "add forest birds and wind" | Searches providers and creates one or more world audio sources without changing graphics |
| `ControlAudioSource` | "make the river quieter" / "stop all sounds" | Changes playback, volume, looping, interval, or spatial blend on existing sources |
| `SetSunDirection` | "point the sun this way" | Aims the directional sun from the pointing/gaze direction |
| `SetLightingPreset` | "make it night time" | Applies a lighting preset |
| `CreateLight` | "add a yellow light" | Creates a point/spot/directional/ambient light, or a flashlight |
| `ModifyLight` | "make this light redder" | Changes color, intensity, range, cone, or sun role |

### Generation control, app, and dialogue

| Intent | Say something like | Effect |
|---|---|---|
| `CancelGeneration` | "cancel generation" | Cancels a pending object/model/world generation job (`target_entity` = object, world, all) |
| `ContinueGeneration` | "keep waiting" | Acknowledges a long-running job and keeps waiting |
| `QuitApplication` | "exit holodeck" | Stops world audio, clears the active config, calls `Application.Quit()` |
| `Unknown` | "what's the weather?" | Does not act. The router speaks "Sorry, I didn't understand that." if the producer said nothing |
| `AskClarification` | "rotate it" (no amount) | Does not act. The question is spoken and, when it matches a clarification slot, the pending command is stored for the next turn |

## Worked examples

The model examples show the **shape the prompt directs the model to produce**, not a captured API
response; the local examples are deterministic and are covered by
[`SpeechIntentBatchTests`](../Assets/App/Shared/Editor/SpeechIntentBatchTests.cs). Fields that are
not listed are schema defaults or empty. Transcripts are shown as Whisper tends to punctuate them;
on-device ASR usually has no punctuation and may be all-caps.

### 1. Create a world from a sentence

**Say:** "Computer, create a cafe in Paris in the 1920s on a rainy day."

**Route:** intent model.

| Field | Value |
|---|---|
| `intent` | `GenerateWorld` |
| `should_execute` | `true` |
| `world_prompt` | `a cafe in Paris in the 1920s on a rainy day` |
| `spoken_response` | typically empty — progress comes from the coordinator's status messages |

**What happens:** the router's explicit-generation guard sees "create" and lets the command through.
`WorldActionDispatcher.HandleGenerateWorld` fires `onGenerateWorldPrompt`, which the scene wires to
`VoiceToWorldLabsPluginCoordinator.TriggerWorldGeneration`; the state machine moves through
`Generating` to `Ready` and the LCARS panels show progress.

**Log:** `Generate world: a cafe in Paris in the 1920s on a rainy day`

**Counter-example:** "Computer, Tokyo at night" with no create/travel verb. If the model returns
`GenerateWorld` anyway, the router rewrites it to `Unknown`, sets `should_execute=false`, and speaks
*"I didn't understand that as a command. Say create a world, or load a saved world by name."*
Nothing is sent to World Labs.

### 2. Put an object in your hand

**Say:** "Computer, create a ball in my left hand."

**Route:** intent model, with a local fallback that produces the same fields.

| Field | Value |
|---|---|
| `intent` | `PlaceObject` |
| `object_name` | `ball` |
| `spatial_reference` | `BodyAnchor` |
| `body_anchor` | `LeftHand` |
| `target_hand` | `Left` |
| `should_execute` | `true` |
| `spoken_response` | `Creating ball.` (local-fallback wording) |

**What happens:** `ObjectPlacementController` resolves the active left hand (tracked hand or
controller) and places the primitive there; the dispatcher then registers it in `InteractionMemory`
and applies any creation attributes.

**Log:** `Placed object 'ball' at (x, y, z)`

### 3. Change every matching target

**Say:** "Computer, make all red cubes blue."

**Route:** intent model.

| Field | Value |
|---|---|
| `intent` | `SetTargetMaterial` |
| `target_reference` | `All` |
| `target_name` | `cube` |
| `target_material_prompt` | `red` |
| `material_prompt` | `blue` |
| `should_execute` | `true` |

**What happens:** `MaterialTargetController` selects every cube whose current material matches
"red", applies "blue", and fires `OnObjectMutated` for each, which the autosave listens to.

**Log:** `[WorldActionDispatcher] Updated 2 materials.` (or `Updated <name> material.` for one)

**Ambiguity:** "Computer, make the red cube blue" with two red cubes asks *"Which red cube?"* and
stores a `Target` clarification. "The top one" completes it with
`target_spatial_qualifier = topmost`; "this one" switches it to `PointedObject`.

### 4. Load a saved world by name

**Say:** "Computer, load Cozy Christmas Cabin"

**Route:** local preflight — the model is never asked.

| Field | Value |
|---|---|
| `intent` | `LoadWorldConfig` |
| `config_name` | `Cozy Christmas Cabin` |
| `should_execute` | `true` |
| `spoken_response` | `Looking for saved world Cozy Christmas Cabin.` |

**What happens:** `WorldActionDispatcher.HandleLoadWorldConfig` searches the in-memory config list
with `FindBestDisplayNameMatch` (punctuation and case are normalized away, so a trailing transcript
full stop does not matter). A hit restores the config — transform, spawn point, objects, audio, and
cached content — without touching World Labs.

**Log:** `[WorldActionDispatcher] Loading saved world 'Cozy Christmas Cabin' from local storage before any World Labs fallback.`

**On a miss:** the same command becomes a confirmation, not a generation:

```text
intent = GenerateWorld
should_execute = false
world_prompt = Cozy Christmas Cabin
spoken_response = "No saved world called Cozy Christmas Cabin. Shall I create it?"
```

The next "yes" is completed by `CommandDialogStateManager` into a `GenerateWorld` with
`user_confirmed = true`; "no" becomes `AskClarification` with "Okay, I won't create it." Anything
that is not a yes/no does not spend money.

### 5. Add layered ambience, then adjust it

**Say:** "Computer, add forest birds and wind."

**Route:** intent model.

| Field | Value |
|---|---|
| `intent` | `CreateAudioSource` |
| `sound_prompts` | `["forest birds", "wind"]` |
| `sound_count` | `2` |
| `should_execute` | `true` |

**What happens:** `AudioWorldActionController` searches Freesound/xeno-canto/Openverse per layer,
creates up to `sound_count` sources, and records attribution. `world_prompt` is untouched, so the
visual world does not change.

**Then:** "Computer, make the river quieter."

| Field | Value |
|---|---|
| `intent` | `ControlAudioSource` |
| `target_name` | `river` |
| `audio_control` | `softer` |
| `audio_volume_delta` | `-0.15` |

`ControlAudioSource` targets an existing source; it does not download a new clip.

### 6. Switch views without the network

**Say:** "Computer, show panorama."

**Route:** local preflight.

| Field | Value |
|---|---|
| `intent` | `ShowPanoWorld` |
| `should_execute` | `true` |
| `spoken_response` | `Showing the panorama.` |

**What happens:** `ViewModeController.RequestPanoView()` switches the world to its panorama. If the
world has no panorama, the dispatcher replaces the response with
*"Panorama is not available for this world."* so the user never hears a false confirmation.

**Log:** `[OpenAiSpeechIntentService] Short-circuiting intent API for local command 'Show panorama.' as ShowPanoWorld.`

This is the pattern for all eight preflight intents: no API key, no latency, no chance of a
misclassification. The same holds for "show 3d", "show mesh", "snap to 10 centimeters", "snap off",
"grab world", "release world", and "load `<name>`".

### 7. A behavior and a physics tweak

**Say:** "Computer, make the cube spin."

**Route:** intent model, with local fallback.

| Field | Value |
|---|---|
| `intent` | `AttachBehavior` |
| `behavior_name` | `spin` |
| `behavior_action` | `start` |
| `target_reference` | `NamedObject` |
| `target_name` | `cube` |

**What happens:** `BehaviorCommandController` adds a `RuntimeBehaviorHost` to the cube and starts a
local-axis spin. The response is `Started spinning cube.` — behavior is continuous, not a one-time
transform edit.

**Then:** "Computer, make it weightless."

| Field | Value |
|---|---|
| `intent` | `ModifyPhysics` |
| `target_reference` | `LastCreatedOrInteracted` |
| `physics_action` | `set_weightless` |
| `object_weightless` | `true` |

**What happens:** the target's `Rigidbody` gets `useGravity = false`, becomes kinematic, and has its
linear/angular velocity zeroed. The response is `Updated cube physics.` The inverse is "make the
cube have weight" (`enable_gravity`). The local parser handles both; `SpeechIntentBatchTests`
asserts the Rigidbody state.

### 8. One clarification round-trip

**Say:** "Computer, rotate it."

**Route:** intent model.

| Field | Value |
|---|---|
| `intent` | `RotateTarget` |
| `target_reference` | `LastCreatedOrInteracted` |
| `rotation_axis` | `Y` |
| `rotation_degrees` | `0` |
| `should_execute` | `false` |
| `spoken_response` | `How many degrees should I rotate it?` |

**What happens:** the dispatcher refuses the command (`should_execute` is false). The router sees a
`RotateTarget` with no degrees, stores a `RotationDegrees` clarification, and the app waits.

**Next turn:** "45."

`CommandDialogStateManager.TryComplete` clones the pending command, fills
`rotation_degrees = 45`, sets `should_execute = true`, and speaks `Rotating target.` Only one
clarification is held at a time; an unrelated executable command clears it.

### 9. From code

Tools and tests can build a command directly when they do not want to go through speech:

```csharp
using SpeechIntent;

SpatialSnapshot spatial = spatialContextProvider.CaptureSnapshot();

var command = new VoiceIntentCommand
{
    transcript = "make a cube",
    intent = VoiceIntentType.PlaceObject,
    should_execute = true,
    object_name = "cube",
    spatial_reference = SpatialReferenceMode.RelativeToMe,
    relative_direction = RelativeDirection.InFront,
    relative_distance_meters = 1f
};

dispatcher.Execute(command, spatial);
```

`WorldActionDispatcher.Execute` is the same entry point the router uses, so the real handler,
resolution, save/autosave, and status paths run. Tests use this pattern with
`new SpatialSnapshot()` for non-spatial commands.

## How routing chooses

`VoiceCommandRouter.HandleResult` is deliberately more than a pass-through. In order:

| Situation | What happens |
|---|---|
| A pending save-as collision exists | "overwrite"/"cancel" resolves it; anything else drops the question and continues as a new command |
| The utterance answers a pending cached-object choice | Becomes `SelectCachedObject` (`use_saved`, `create_new`, `cancel`) |
| The utterance answers a pending generation warning | Becomes `CancelGeneration`/`ContinueGeneration` from the local parser |
| The utterance completes a pending clarification | The pending command is cloned, the slot is filled, and it executes |
| The model returned `Unknown`/`AskClarification` but the local grammar has an executable parse | The local command replaces the model result (`Using local executable override…`) |
| The transcript is a saved-world load | The local `LoadWorldConfig` always wins, even over a model `GenerateWorld` (`Enforcing local saved-world load…`) |
| The model returned `GenerateWorld` for a non-create/non-travel transcript | Blocked to `Unknown`, with the "Say create a world…" message, unless the turn is a confirmed clarification; the dispatcher repeats the check as a last line of defense |
| Nothing actionable and no response was supplied | `spoken_response = "Sorry, I didn't understand that."` |
| The command is `AskClarification` | It is spoken but never executed; the dialogue stays open |

### Clarification slots

| Slot | Trigger | Completion |
|---|---|---|
| `Placement` | `PlaceObject` asks "Where?" | "here", "there", "in front of me", "left hand", "in my hand", "world origin" |
| `RotationDegrees` | `RotateTarget` with `rotation_degrees == 0` | A number, e.g. "45" |
| `RotationAxis` | `RotateTarget` with `rotation_axis == None` | "x axis", "yaw", "roll", "z" |
| `Target` | A target-taking intent asks "Which …?" | "this one", "the top one", or a material qualifier |
| `Confirmation` | A saved-world miss offered creation | A yes/no; only "yes" sets `user_confirmed` |

`CommandDialogStateManager` holds one pending command, so a clarification is a two-turn exchange,
not a conversation stack.

### Execution order

The router **executes before speaking**. A handler may replace `spoken_response` with a failure
("Panorama is not available for this world."), and the user should hear the failure, not a
confirmation followed by it. After dispatch it updates the pending clarification, applies the
silence guard, and passes `spoken_response` to `onAssistantResponse` (TTS and the status panel).

## Configuration

The interpreter is a `MonoBehaviour` with a `ScriptableObject` config:

- Scene component: `OpenAiSpeechIntentService`
- Config asset:
  [`Assets/App/Shared/Command/SpeechIntent/OpenAiSpeechIntentConfig.asset`](../Assets/App/Shared/Command/SpeechIntent/OpenAiSpeechIntentConfig.asset)

| Setting | Checked-in value | Notes |
|---|---|---|
| `useProxyServer` | `false` | When true, the service POSTs WAV + spatial/scene JSON to `proxyInterpretUrl` and expects a `SpeechIntentResult` JSON. Typed interpretation is not available in proxy mode |
| `proxyInterpretUrl` | `http://localhost:8000/speech/interpret` | Only used in proxy mode |
| `transcriptionModel` | `whisper-1` | Audio path only |
| `transcriptionLanguage` | `en` | Empty lets Whisper auto-detect; specifying it improves accuracy |
| `intentModel` | `gpt-5.4` in the asset (the field default in code is `gpt-4o`) | Confirm the id against your OpenAI account before relying on it |
| `intentTemperature` | `0.1` | Low temperature for deterministic structured output |
| `storeRequests` | `false` | Asks OpenAI not to store the request |
| `timeoutSeconds` | `60` | Per HTTP request |
| `additionalDeveloperInstructions` | Long routing-hint block | Editable in the Inspector; appended after the structural rules |

The structural rules live in `OpenAiSpeechIntentService.BuildDeveloperInstructions`; the
domain-specific hints live in `additionalDeveloperInstructions` so they can be edited without
recompiling. **Both must agree with the schema and the enum** — the schema is the machine-enforced
part, and the hints are what make the model choose the right intent.

The schema is built by `BuildCommandJsonSchema` and sent through the Responses API
(`/responses`, `text.format.type = "json_schema"`, name `voice_intent_command`, `strict = true`).
It sets `additionalProperties: false` and lists **every** property as required, so a model response
always contains the full field set. Responses are decoded with `SpeechIntentJson` into
`VoiceIntentCommand`; enums are strings (`StringEnumConverter`).

API key resolution is `config.openAiApiKey` first, then `OPENAI_API_KEY` from the runtime
`.env`/process environment (`RuntimeDotEnv`). A build with no key and no proxy can still use the
local preflight and local fallback on the text path; the audio path falls back to the text path only
when the recognizer supplied fallback text, otherwise it reports
*"OpenAI speech intent is not configured, and no fallback command text is available."*

### Model choice and failure

The service does not silently retry locally after a failed API call. It reports the error; the
router maps it to a user-facing sentence:

| Failure | What the user hears |
|---|---|
| `insufficient_quota` / "exceeded your quota" | "OpenAI quota is exhausted. I heard you, but I can't transcribe or interpret commands until the API quota or billing is fixed." |
| HTTP 429 / `rate_limit_exceeded` | "OpenAI is rate limiting command processing. Please wait a moment and try again." (+ reset time when supplied) |
| 401 / `invalid_api_key` | "OpenAI rejected the API key. Please check the OpenAI key configured for this build." |
| `response_format` / schema errors | "OpenAI rejected the command parser request format. The app needs an OpenAI schema/configuration update." |
| Other OpenAI errors | "OpenAI command processing failed. Check the status panel or device log for details." |

An empty capture is not treated as an alarm: `Interpret` returns *"I didn't catch that."* and the
router logs it without speaking the OpenAI-error path.

## Adding a new intent

Use this checklist; missing one step is how an intent ends up reachable in the schema but dead at
runtime.

1. **Enum** — add the value to `VoiceIntentType` in
   [`VoiceIntentSchemas.cs`](../Assets/App/Shared/Command/SpeechIntent/Runtime/VoiceIntentSchemas.cs).
   Append new values and do not renumber existing ones.
2. **Schema** — for a model-produced intent, add the name to the `intent` enum in
   `BuildCommandJsonSchema`, and add any new fields to `required` and `properties` with a type,
   range/enum, and a description. Local-only reply intents (such as `SelectCachedObject`) are
   deliberately absent. The schema is strict, so a missing property is a request error, not a silent
   default.
3. **Prompt** — add the routing rule to `BuildDeveloperInstructions` (structural) or to
   `additionalDeveloperInstructions` on the config asset (domain hints). Say what the intent is
   for, what it is **not** for, and one example utterance/field mapping. Update the asset copy if
   you use it.
4. **Local grammar (optional)** — add a `TryParse…` branch in `LocalTypedIntentParser` if the phrase
   is deterministic. Only add it to `TryBuildLocalPreflightCommand` if the model must **never** see
   it (view switches, snapping, world grab, saved-world load).
5. **Dispatcher** — add `case VoiceIntentType.YourIntent:` in `WorldActionDispatcher.Execute` and a
   handler that validates, calls the controller, and sets `spoken_response` on failure. Do not act
   when `should_execute` is false; `Execute` already guards that.
6. **Controller** — implement the effect in the owning controller/service, not in the dispatcher.
   Keep UI/status messages in `ArchStatusBus` and object changes in `OnObjectMutated` so autosave
   sees them.
7. **Clarification (if target-taking)** — if the intent can ask "Which …?", add it to
   `IsTargetTakingIntent` in `VoiceCommandRouter` and, if it has a missing numeric/axis slot, to
   `TryGetClarificationSlot` and `CommandDialogStateManager`.
8. **Tests** — add assertions to `SpeechIntentBatchTests.RunDistanceAndMovementTests` for the local
   parse, the router override, and the dispatcher effect where applicable. Run it in batchmode.
9. **This page** — add the row to the catalog, and a worked example if the shape is non-obvious.

Two consistency traps:

- The schema enum and `VoiceIntentType` must match for **model-produced** intents. Local-only reply
  intents (such as `SelectCachedObject`) are intentionally absent. If a model-produced value is
  missing from the schema, the model cannot return it; if the model returns a value the enum cannot
  parse, the user sees `Intent parse failed: …`.
- The prompt hints and the dispatcher must agree about field meaning. A prompt that says "put the
  object name in `object_name`" while the handler reads `target_name` produces a plausible command
  that does nothing.

## Debugging an intent

Start with the transcript. The app logs it before anything else:

```text
[OpenAiSpeechIntentService] Transcript: "Load Cozy Christmas Cabin"
[VoiceCommandRouter] HandleResult router=24398 pending=False/None transcript='Load Cozy Christmas Cabin' intent=LoadWorldConfig execute=True response='Looking for saved world Cozy Christmas Cabin.'
```

Useful log lines:

| Log | Means |
|---|---|
| `[OpenAiSpeechIntentService] Short-circuiting intent API for local command '…' as …` | The preflight skipped the model |
| `[VoiceCommandRouter] Using local executable override for '…' as …` | The model was unusable; the local grammar took over |
| `[VoiceCommandRouter] Enforcing local saved-world load for '…' before world generation.` | A load phrase was about to be treated as generation |
| `[VoiceCommandRouter] Blocked implicit world generation for transcript '…'.` | The create/travel guard fired |
| `[VoiceCommandRouter] Stored pending … clarification …` | The app is waiting for a follow-up |
| `Command not executed. Intent=…, Reason=…` | Dispatcher refused `should_execute=false` / `AskClarification` |
| `[WorldActionDispatcher] …` | The handler's effect or failure |

Ways to issue a command without speaking:

- **Typed command** — the LCARS virtual keyboard calls `VoiceCommandRouter.SubmitTypedCommand`,
  which runs the same preflight/override path as the wake-word transcript.
- **Android broadcast** — `DebugCommandIntentBridge` (auto-installed on the router's object) accepts
  the same path:

  ```bash
  adb shell am broadcast -a com.QLC.HeadsetHolodeck.COMMAND --es command "load rain room"
  ```

- **Command file** — with `enableFileChannel` on, the bridge polls
  `Application.persistentDataPath/debug_command.txt` (on Quest
  `/sdcard/Android/data/com.QLC.HeadsetHolodeck/files/debug_command.txt`), dispatches the contents,
  and deletes the file. Handy where a runtime broadcast receiver is awkward.

  ```bash
  adb push command.txt /sdcard/Android/data/com.QLC.HeadsetHolodeck/files/debug_command.txt
  ```

  The bridge is a test affordance: `enableInReleaseBuilds` defaults to on, and any app on the device
  could send commands while it is enabled. Turn it off for a shipped build.

- **Error simulation** — typed commands `simulate openai quota error` and
  `simulate openai rate limit` exercise the status/TTS error path without a network call
  (`enableOpenAiErrorSimulationCommands`).

`SpeechIntentResult.raw_model_json` is available to `onResult` listeners when you need the exact
model output; it is empty for local/preflight commands.

### Tests

`Assets/App/Shared/Editor/SpeechIntentBatchTests.cs` is the regression suite for the intent layer:
local parsing, unit conversion, target resolution/clarification, router overrides, behavior
commands, save/load routing, and dispatcher effects. It is a batch entry point:

```bash
Unity -batchmode -quit -projectPath <repo> \
  -executeMethod HeadsetHolodeck.EditorTests.SpeechIntentBatchTests.RunDistanceAndMovementTests \
  -logFile <path>
```

It logs `[SpeechIntentBatchTests] All distance and movement tests passed.` and exits non-zero on
failure.

## Limits and gotchas

- **`confidence` is not a gate.** It is produced and carried; execution is controlled by
  `should_execute` and the router's rules. Do not add a confidence threshold without deciding what
  should happen to the rejected command.
- **The model is still the primary interpreter for most phrases.** The local grammar covers a
  subset and is a fallback; a phrase outside both is `Unknown`, not an error.
- **The local grammar is not a language model.** It matches keywords/regex and is intentionally
  literal. "Make the cube spin" works; a novel paraphrase may rely on the model.
- **Some intents are model-only.** `SaveWorldConfig`, `ShowUi`, and `SetGenerationModel` have no
  local grammar; without a key or proxy those phrases are `Unknown`, though the equivalent LCARS
  buttons still work.
- **Some intents are dialogue replies, not standalone commands.** `SelectCachedObject` and completed
  clarifications need pending state; `CancelGeneration`/`ContinueGeneration` are most useful while a
  job is running and report "No generation is currently running." otherwise.
- **One pending clarification at a time.** `CommandDialogStateManager` stores a single command; a
  new executable command clears it.
- **Load-by-name is deliberately not generation.** A miss becomes a yes/no offer; only an explicit
  "yes" authorizes World Labs spending. A misheard name is a common cause of the offer.
- **A loose splat cannot be loaded by bare filename.** `load yoyo.spz` is parsed as a saved-world
  name, not a file. Use the Local/URL panel or a URL/path phrasing that `TryParseContentPath`
  recognizes. This is a recorded finding (see `USER_MANUAL.md` limitations).
- **Local "rotate it" keeps `it` as a name.** `TryParseRotateTarget` maps "this"/"that" to
  `PointedObject`, but it does not map "it" to `LastCreatedOrInteracted`; the model does. Without a
  model, rotate a named object or point at it.
- **Audio path vs text path differ in transcript shape.** Whisper usually punctuates and
  capitalizes; on-device ASR may return all caps and no punctuation. `HeadsetHolodeckCommandRouter`
  lowercases all-caps post-wake text and rewrites common "create a" mishearings, but the model sees
  the raw transcript for Whisper audio.
- **Proxy mode does not support typed commands.** `InterpretText` returns
  "Typed intent interpretation is not available in proxy mode yet."
- **No conversational history.** Each turn is interpreted independently; the only cross-turn state
  is the one pending clarification, the cached-object/save-as questions, and the scene focus memory
  that resolves "it"/"this".

## Source map

| Path | Role |
|---|---|
| [`VoiceIntentSchemas.cs`](../Assets/App/Shared/Command/SpeechIntent/Runtime/VoiceIntentSchemas.cs) | `VoiceIntentType` enum, `VoiceIntentCommand`, `SpeechIntentResult`, spatial/scene snapshots |
| [`OpenAiSpeechIntentService.cs`](../Assets/App/Shared/Command/SpeechIntent/Runtime/OpenAiSpeechIntentService.cs) | Transcription, preflight, schema, model request, decode |
| [`OpenAiSpeechIntentConfig.cs`](../Assets/App/Shared/Command/SpeechIntent/Runtime/OpenAiSpeechIntentConfig.cs) and [`.asset`](../Assets/App/Shared/Command/SpeechIntent/OpenAiSpeechIntentConfig.asset) | Model/transport settings and the editable routing hints |
| [`VoiceCommandRouter.cs`](../Assets/App/Shared/Command/SpeechIntent/Runtime/VoiceCommandRouter.cs) | Entry points, dialogue, overrides, guards, speech |
| [`LocalTypedIntentParser.cs`](../Assets/App/Shared/Command/SpeechIntent/Runtime/LocalTypedIntentParser.cs) | Deterministic grammar and fallback |
| [`WorldActionDispatcher.cs`](../Assets/App/Shared/Command/SpeechIntent/Runtime/WorldActionDispatcher.cs) | One handler per intent; owns dispatch order and effects |
| [`CommandDialogStateManager.cs`](../Assets/App/Shared/Command/CommandUnderstanding/Runtime/CommandDialogStateManager.cs) | Pending clarification slots and completion |
| [`SpeechIntentBatchTests.cs`](../Assets/App/Shared/Editor/SpeechIntentBatchTests.cs) | Regression suite |
| [`HeadsetHolodeckCommandRouter.cs`](../Assets/App/Shared/Command/SpeechIntent/Runtime/VoiceActivation/HeadsetHolodeckCommandRouter.cs) | Wake/audio → router bridge and ASR normalization |
| [`DebugCommandIntentBridge.cs`](../Assets/App/Shared/Command/SpeechIntent/Runtime/DebugCommandIntentBridge.cs) | Broadcast/file command injection for tests |
| [`SpeechIntentJson.cs`](../Assets/App/Shared/Command/SpeechIntent/Runtime/SpeechIntentJson.cs) | Enum-string JSON settings |

## Related

- [AGENTS.md](../AGENTS.md) — architecture map, including the command-routing paragraph and the
  distinction between the direct pipeline and this one.
- [USER_MANUAL.md](../USER_MANUAL.md#voice-input) — the user-facing command list and wake-word
  behavior.
- [README.md](../README.md) — setup, credentials, and the headset run flow.
- [STATUS.md](stabilization/STATUS.md) — verified state and the order queue.
- Historical design records (not current behavior): the
  [speech-intent core integration design](superpowers/specs/2026-03-30-speech-intent-core-integration-design.md),
  its [plan](superpowers/plans/2026-03-30-speech-intent-core-integration.md), and the
  [original feature brief](superpowers/plans/Unity_VR_Speech_Intent_Feature.md).

## Provenance

Written by **DeepSeek** on 2026-10-29, during the speech-intent documentation pass. This page is
deliberately shared: other DeepSeek agents (and any other agent or maintainer) may correct, extend,
or reorganize it without asking first. The cross-agent ledger in
[docs/agent-worklog.md](agent-worklog.md) records this change. If you change the catalog or the
field tables, keep `VoiceIntentType` and `BuildCommandJsonSchema` in sync so the page and the code
do not drift.
