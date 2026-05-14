# Voice Audio Feedback

**Date:** 2026-03-28
**Status:** Approved

## Overview

A new `HolodeckAudioFeedback` MonoBehaviour subscribes to `HolodeckStateMachine.StateChanged` and plays 2D audio cues at the right moments: a "listening" clip when the system begins recording voice input, and a "heard" clip when the voice input has been captured and is being processed.

## Goals

- Give the user auditory confirmation that the A button (or Space in Editor) was registered
- Signal state transitions clearly without requiring visual attention
- No changes to any existing file

## Scope

Single new file: `Assets/App/Scripts/State/HolodeckAudioFeedback.cs`

---

## Component: `HolodeckAudioFeedback`

**Namespace:** `Holodeck.State`

### Serialized fields

```csharp
[Header("Dependencies")]
[SerializeField] private HolodeckStateMachine stateMachine;
[SerializeField] private AudioSource audioSource;

[Header("Clips")]
[SerializeField] private AudioClip listeningClip;
[SerializeField] private AudioClip heardClip;
```

Both clips are optional. If unassigned, the corresponding cue is silently skipped — no error logged.

### Behavior

**`Awake`:** Log errors if `stateMachine` or `audioSource` is null.

**`OnEnable`:** Subscribe to `stateMachine.StateChanged` (guard against null).

**`OnDisable`:** Unsubscribe from `stateMachine.StateChanged` (guard against null).

**`HandleStateChanged(HolodeckState previous, HolodeckState next)`:**

| `next` state | Action |
|---|---|
| `ListeningForCommand` | `audioSource.PlayOneShot(listeningClip)` if `listeningClip != null` |
| `Interpreting` | `audioSource.PlayOneShot(heardClip)` if `heardClip != null` |
| All others | No-op |

`PlayOneShot` is used so sounds do not interrupt each other and do not loop.

### AudioSource requirements

The `AudioSource` component must have:
- `spatialBlend = 0` (fully 2D)
- `playOnAwake = false`

These are configured in the Inspector — not enforced in code.

---

## Wiring

1. Add `HolodeckAudioFeedback` to the Systems GameObject
2. Add an `AudioSource` component to the same GameObject (set `spatialBlend = 0`, `playOnAwake = false`)
3. Wire `stateMachine` and `audioSource` in the Inspector
4. Assign `listeningClip` and `heardClip` audio assets

## Out of Scope

- Error state audio cue
- Generation complete / world loaded audio cue
- Volume control
