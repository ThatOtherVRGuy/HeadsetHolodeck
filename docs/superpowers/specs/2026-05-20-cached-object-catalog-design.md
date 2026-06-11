# Cached Object Catalog Design

## Goal

Generated or imported 3D objects should become reusable local assets. When the user asks for an object, Headset Holodeck should first check whether a suitable saved object already exists, then ask whether to use the saved one or create a new one. Scene-specific changes stay in the current world's `world.json`.

## Storage

Add a persistent object cache beside `CachedWorlds`:

```text
Application.persistentDataPath/
  Worlds/
    CachedWorlds/
    CachedObjects/
      teddy_bear_8f3a2c/
        object.json
        model.glb
        thumbnail.png
```

Each cached object folder owns the reusable asset data:

- `object.json`: canonical name, aliases, tags, provider, source prompt, task id, model URL, created/modified dates, file size, thumbnail path, model path.
- `model.glb`: the generated or imported object file.
- `thumbnail.png`: optional preview image for catalog cards and disambiguation UI.

Do not store provider API keys or other secrets in cache metadata.

## World JSON

World configs should store object instances, not duplicated model data. A saved object can reference a cached object id/path, then store transform and component modifications through the existing component serializer system.

Conceptually:

```json
{
  "instance_id": "scene_teddy_01",
  "prefab_name": null,
  "display_name": "Teddy Bear",
  "cached_object_id": "teddy_bear_8f3a2c",
  "components": [
    { "type": "Transform", "data": { "...": "..." } },
    { "type": "Material", "data": { "...": "..." } }
  ]
}
```

If schema compatibility is easier, the cached object reference can initially be implemented as a registered saved component instead of adding direct fields to `SavedObject`. The runtime behavior is the same: restore imports the cached GLB, then applies per-world components.

## Creation Flow

For a command like `create a teddy bear 1 meter in front of me`:

1. Parse the command as `PlaceObject`, preserving object name and placement data.
2. Check local primitives and known prefabs first. These stay immediate.
3. Search the cached object catalog by canonical name and aliases.
4. If no match exists, generate via the selected object provider, save the GLB into `CachedObjects`, add metadata, then instantiate it at the requested pose.
5. If one or more matches exist, enter a pending object-choice state instead of generating immediately.

## Saved Or New Decision Flow

When matches exist:

- Voice says: `I found a saved teddy bear. Use it, or create a new one?`
- UI opens an object-choice panel showing matching cached objects with thumbnails where available.
- User can answer by voice or touch:
  - `use saved`
  - `use that one`
  - `create a new one`
  - `cancel`

The pending decision must retain:

- original transcript
- requested object name
- placement command
- spatial snapshot
- matching cached object ids

When the user chooses a saved object, import the cached GLB and apply the original placement. When the user chooses new, call the provider, cache the result, and apply the same placement.

## Restore Flow

When loading a world:

1. For each saved object, check whether it references a cached object.
2. If it does, import the cached GLB under the placed objects parent.
3. Wrap it as interactable using the existing object placement/interactable path.
4. Apply saved components such as transform, material, audio/proxy state, or future behavior components.
5. Register the restored object with interaction memory.

Missing cached model files should not break world loading. Log a warning, show a status message, and skip that object or create a placeholder.

## UI

Add an object catalog panel after the storage/restore foundation is working.

The panel should support:

- thumbnail card grid/list
- object name
- provider/source
- created date
- use/place
- create new
- rename
- delete
- regenerate thumbnail later

The same card component can be reused for the saved-or-new choice UI.

## Testing

Add batch/editor tests for:

- object metadata path generation and canonical-name matching
- provider result writes `model.glb` and `object.json`
- voice command with no cached match starts generation
- voice command with cached match creates a pending saved/new choice
- saved choice preserves original placement
- world restore imports cached GLB before applying transform components
- missing cached GLB reports a warning instead of failing restore

## Implementation Order

1. Add cache metadata models and store service.
2. Save provider-generated GLBs into `CachedObjects`.
3. Add cached object import/instantiation path.
4. Add world JSON reference serialization/restoration.
5. Add pending saved-or-new decision flow.
6. Add the object catalog/disambiguation UI.
