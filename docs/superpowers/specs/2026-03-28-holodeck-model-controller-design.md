# Holodeck Model Controller

**Date:** 2026-03-28
**Status:** Approved

## Overview

A new `HolodeckModelController` MonoBehaviour shows the static TNGHolodeck mesh while a world is loading and hides it once the world finishes loading (or fails). It also plays a one-shot audio cue when loading begins. No existing files are modified.

## Goals

- Show TNGHolodeck during the gap between world unload and world load (both browser and voice flows)
- Play an audio cue when world loading starts
- Hide TNGHolodeck once the new world is loaded or load fails

## Scope

Single new file: `Assets/App/Scripts/State/HolodeckModelController.cs`

---

## Component: `HolodeckModelController`

**Namespace:** `Holodeck.State`

**Class declaration:** `public sealed class HolodeckModelController : MonoBehaviour` — `sealed`, matching the pattern of all other `Holodeck.State` MonoBehaviours.

### Using Statements

```csharp
using GaussianSplatting.Runtime;
using UnityEngine;
using WorldLabs.Runtime;
```

### Serialized Fields

```csharp
[Header("Dependencies")]
[SerializeField] private WorldLabsWorldManager worldManager;
[SerializeField] private AudioSource audioSource;

[Header("Model")]
[SerializeField] private GameObject holodeckModel;

[Header("Clips")]
[SerializeField] private AudioClip worldLoadingClip;
```

All four fields are required except `worldLoadingClip`, which is optional — if unassigned the audio cue is silently skipped.

### Behavior

**`Awake`:** Log errors using `Debug.LogError($"...", this)` (passing `this` so Unity highlights the component) if `worldManager`, `audioSource`, or `holodeckModel` is null.

**`OnEnable`:** Subscribe to (guard each against null):
- `worldManager.OnWorldLoadStarted`
- `worldManager.OnWorldLoaded`
- `worldManager.OnWorldLoadFailed`

**`OnDisable`:** Unsubscribe from all three (guard each against null).

All three handlers guard `holodeckModel != null` before calling `SetActive`, and `HandleWorldLoadStarted` additionally guards `audioSource != null` before calling `PlayOneShot`. If a required field is unassigned (caught in `Awake`), the handler returns early rather than throwing.

**`HandleWorldLoadStarted(string worldId)`:**

| Condition | Action |
|-----------|--------|
| `worldId != "__default__"` | `holodeckModel.SetActive(true)`, `audioSource.PlayOneShot(worldLoadingClip)` if clip assigned |
| `worldId == "__default__"` | No-op |

**`HandleWorldLoaded(string worldId, GaussianSplatRenderer renderer)`:**

| Condition | Action |
|-----------|--------|
| `worldId != "__default__"` | `holodeckModel.SetActive(false)` |
| `worldId == "__default__"` | No-op |

**`HandleWorldLoadFailed(string worldId, string error)`:** `holodeckModel.SetActive(false)` unconditionally. If `worldId == "__default__"` the model was never shown, so this is a harmless no-op — acceptable by design.

`PlayOneShot` is used so the cue does not interrupt other sounds and does not loop.

### Default World Sentinel

```csharp
private const string DefaultWorldId = "__default__";
```

---

## Event Sources

| Trigger | Source |
|---------|--------|
| Show model + play sound | `WorldLabsWorldManager.OnWorldLoadStarted` (non-default worldId) |
| Hide model | `WorldLabsWorldManager.OnWorldLoaded` (non-default worldId) |
| Hide model | `WorldLabsWorldManager.OnWorldLoadFailed` (any worldId) |

`OnWorldLoadStarted` is a C# event on `WorldLabsWorldManager` (not a UnityEvent). It fires at the start of `LoadWorldAsync`, before download begins.

---

## Wiring

1. Add `HolodeckModelController` to the same Systems GameObject that holds `HolodeckAudioFeedback`
2. Wire `worldManager`, `audioSource`, and `holodeckModel` (TNGHolodeck) in the Inspector
3. Assign `worldLoadingClip` audio asset (optional)
4. In the scene, ensure `holodeckModel` (TNGHolodeck) starts **inactive** (`activeSelf = false`). `HolodeckModelController` will activate and deactivate it at runtime. If it starts active, it will be visible until the first non-default world load completes.

The same `AudioSource` component already on the Systems GameObject (used by `HolodeckAudioFeedback`) can be reused.

---

## Behavior During Voice Flow

`OnWorldLoadStarted` fires during the voice-driven flow as well. TNGHolodeck becomes active behind the panorama sphere (scale 500), which visually covers it. This is acceptable — no special-casing needed.

---

## Out of Scope

- Transition animation on show/hide (instantaneous `SetActive`)
- Volume control per cue
- Load progress indication
