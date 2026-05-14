# Sub-project 3: Object/World Manipulation Commands — Design

## Goal

Wire the already-implemented `SceneEntityResolver` and `TargetTransformController` components into `SpeechIntentSceneSetup` so that ScaleTarget, RotateTarget, and MoveTarget voice commands work end-to-end.

## Background

All runtime logic is already implemented:
- `TargetTransformController` — executes scale, rotate, move on a resolved `GameObject`
- `SceneEntityResolver` — resolves target from `VoiceIntentCommand` (by reference mode, name, or spatial hit)
- `WorldActionDispatcher` — already routes ScaleTarget/RotateTarget/MoveTarget to `TargetTransformController` (warns if null)
- `InteractionMemory` — tracks last created/interacted/world root objects
- `SceneSemanticContextProvider` — snapshot already includes `last_created_target_name`, `last_interacted_target_name`, `current_world_root_name`

The only gap: `SpeechIntentSceneSetup` does not create or wire `SceneEntityResolver` or `TargetTransformController`, leaving `WorldActionDispatcher.targetTransformController` null at runtime. Additionally, `SceneSemanticContextProvider.interactionMemory` and `.entityResolver` are never wired, so `named_scene_objects` is always empty.

## Named Object Resolution

`SceneEntityResolver.FindByNameOrAlias()` performs a two-pass search:
1. All `SpeechIntentTrackable` components (aliases + display names via `Matches()`)
2. All scene transforms matched by exact `gameObject.name` (case-insensitive, no substring/fuzzy)

**Runtime resolution** works for any object with a meaningful `gameObject.name` — no `SpeechIntentTrackable` required.

**Snapshot discoverability** (what GPT-4o sees in the prompt) is narrower: `named_scene_objects` only lists `SpeechIntentTrackable`-tagged objects. However, the snapshot also includes `last_created_target_name`, `last_interacted_target_name`, and `current_world_root_name` as dedicated fields, which covers the common use cases. Objects without a tag that are not the most recent created/interacted will not appear in the model's context by name — the model may fall back to `LastCreatedOrInteracted` or return `AskClarification`.

When no target resolves and `should_execute = false`, the model returns `AskClarification` — no silent failures.

## Change

**One file:** `Assets/App/Editor/SpeechIntentSceneSetup.cs`

### Components added (via existing `GetOrAdd<T>`)

| Component | Purpose |
|---|---|
| `SceneEntityResolver` | Resolves target GameObject from command + spatial context |
| `TargetTransformController` | Executes scale/rotate/move on resolved target |

### Wires added

| Field | Assigned value | Note |
|---|---|---|
| `dispatcher.targetTransformController` | `targetTransform` | Was null — causes silent no-op |
| `targetTransform.entityResolver` | `entityResolver` | Required for name/reference resolution |
| `targetTransform.interactionMemory` | `memory` | Required for LastCreatedOrInteracted fallback |
| `entityResolver.interactionMemory` | `memory` | Required for reference mode resolution |
| `semantic.interactionMemory` | `memory` | Currently unwired — snapshot missing world/object names |
| `semantic.entityResolver` | `entityResolver` | Currently unwired — named_scene_objects always empty |

All mutated objects — including the two new components and the existing `semantic` — are included in the `Undo.RecordObjects` call: `{ service, dispatcher, memory, router, trigger, entityResolver, targetTransform, semantic }`.

## Out of Scope

- `SceneEntityResolver.searchRoot`: left null (whole-scene search)
- Adding `SpeechIntentTrackable` to placed objects automatically
- Extending `CollectTrackableNames` with `InteractionMemory` objects (deferred — see memory file)
- Any changes to runtime scripts

## Testing

After running `Holodeck > Setup SpeechIntent`:
1. Verify `SpeechIntent` GameObject has both new components in Inspector
2. Verify all six wires are set (check each component's Inspector fields)
3. In Play mode: say "make it bigger" after generating a world → world splat should scale up
4. Say "rotate it 90 degrees" → world splat should rotate
5. Say "scale the [worldName] by half" → confirm NamedObject resolution works by name
