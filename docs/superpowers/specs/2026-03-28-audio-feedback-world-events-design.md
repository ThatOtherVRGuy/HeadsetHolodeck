# Voice Audio Feedback — World Events Extension

**Date:** 2026-03-28
**Status:** Approved

## Overview

Extend `HolodeckAudioFeedback` with four additional audio cues covering the world-loading lifecycle: panorama loaded, splat loaded, failed to load, and splat disabled (default world re-enabled). No new files are created; no other existing files are modified.

## Goals

- Give the user auditory feedback at each stage of world generation and loading
- Reuse the existing `HolodeckAudioFeedback` component and `AudioSource`
- All new clips optional — silently skipped if unassigned

## Scope

Single modified file: `Assets/App/Scripts/State/HolodeckAudioFeedback.cs`

---

## Event Sources

| Cue | Source |
|-----|--------|
| Panorama loaded | `ThumbnailSkyboxController.onThumbnailShown` UnityEvent — wired in Inspector to `PlayPanoramaLoadedClip()` |
| Splat loaded | `HolodeckStateMachine.StateChanged` → `Ready` |
| Failed to load | `HolodeckStateMachine.StateChanged` → `Error` |
| Splat disabled | `WorldLabsWorldManager.OnWorldLoaded` where `worldId == "__default__"` |

`onThumbnailShown` is a private serialized `UnityEvent` on `ThumbnailSkyboxController` and cannot be subscribed to in code. It is wired via the Unity Inspector instead. `HolodeckAudioFeedback` exposes a public method as the target.

---

## Changes to `HolodeckAudioFeedback`

### New using statements

```csharp
using GaussianSplatting.Runtime;
using WorldLabs.Runtime;
```

### New serialized fields

```csharp
[Header("World Dependencies")]
[SerializeField] private WorldLabsWorldManager worldManager;

[Header("World Clips")]
[SerializeField] private AudioClip panoramaLoadedClip;
[SerializeField] private AudioClip splatLoadedClip;
[SerializeField] private AudioClip failedToLoadClip;
[SerializeField] private AudioClip splatDisabledClip;
```

`worldManager` is optional — no error logged if unassigned. Skipping it disables the splat-disabled cue only.

### `Awake` — unchanged

No new null checks needed. `worldManager` is optional.

### `OnEnable` — extended

Add after the existing `stateMachine.StateChanged` subscription:

```csharp
if (worldManager != null)
    worldManager.OnWorldLoaded += HandleWorldLoaded;
```

### `OnDisable` — extended

Add after the existing `stateMachine.StateChanged` unsubscription:

```csharp
if (worldManager != null)
    worldManager.OnWorldLoaded -= HandleWorldLoaded;
```

### `HandleStateChanged` — extended

Add two cases to the existing switch:

```csharp
case HolodeckState.Ready:
    if (splatLoadedClip != null)
        audioSource.PlayOneShot(splatLoadedClip);
    break;

case HolodeckState.Error:
    if (failedToLoadClip != null)
        audioSource.PlayOneShot(failedToLoadClip);
    break;
```

### New private method: `HandleWorldLoaded`

```csharp
private void HandleWorldLoaded(string worldId, GaussianSplatRenderer renderer)
{
    if (worldId == "__default__" && splatDisabledClip != null)
        audioSource.PlayOneShot(splatDisabledClip);
}
```

### New public method: `PlayPanoramaLoadedClip`

```csharp
/// <summary>
/// Wire this to ThumbnailSkyboxController.onThumbnailShown in the Inspector.
/// </summary>
public void PlayPanoramaLoadedClip()
{
    if (panoramaLoadedClip != null)
        audioSource.PlayOneShot(panoramaLoadedClip);
}
```

---

## Wiring

1. On `ThumbnailSkyboxController`: in the `On Thumbnail Shown` UnityEvent, add a listener → `HolodeckAudioFeedback.PlayPanoramaLoadedClip`
2. On `HolodeckAudioFeedback`: wire `worldManager` to the GameObject that holds `WorldLabsWorldManager`
3. Assign the four new audio clips

## Out of Scope

- Generation started / generation progress audio
- World unloaded audio (non-default worlds)
- Volume per-cue control
