# Voice Audio Feedback — World Events Extension Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extend `HolodeckAudioFeedback` with four new audio cues: panorama loaded, splat loaded, failed to load, and splat disabled.

**Architecture:** Single file edit. New using statements, four new serialized clip fields, one optional `WorldLabsWorldManager` dependency, two new cases in `HandleStateChanged`, a new `HandleWorldLoaded` handler subscribed to `WorldLabsWorldManager.OnWorldLoaded`, and a public `PlayPanoramaLoadedClip()` method wired from the Inspector.

**Tech Stack:** Unity C#, `WorldLabsWorldManager.OnWorldLoaded` (`WorldLabs.Runtime`), `GaussianSplatRenderer` (`GaussianSplatting.Runtime`), `AudioSource.PlayOneShot`

**Spec:** `docs/superpowers/specs/2026-03-28-audio-feedback-world-events-design.md`

---

## File Map

| Action | File | Purpose |
|--------|------|---------|
| **Modify** | `Assets/App/Scripts/State/HolodeckAudioFeedback.cs` | Add world-lifecycle audio cues |

---

## Task 1: Extend `HolodeckAudioFeedback`

**Files:**
- Modify: `Assets/App/Scripts/State/HolodeckAudioFeedback.cs`

- [ ] **Step 1: Replace the file with the updated implementation**

Replace `Assets/App/Scripts/State/HolodeckAudioFeedback.cs` with:

```csharp
using GaussianSplatting.Runtime;
using UnityEngine;
using WorldLabs.Runtime;

namespace Holodeck.State
{
    public sealed class HolodeckAudioFeedback : MonoBehaviour
    {
        [Header("Dependencies")]
        [SerializeField] private HolodeckStateMachine stateMachine;
        [SerializeField] private AudioSource audioSource;

        [Header("World Dependencies")]
        [SerializeField] private WorldLabsWorldManager worldManager;

        [Header("Clips")]
        [SerializeField] private AudioClip listeningClip;
        [SerializeField] private AudioClip heardClip;

        [Header("World Clips")]
        [SerializeField] private AudioClip panoramaLoadedClip;
        [SerializeField] private AudioClip splatLoadedClip;
        [SerializeField] private AudioClip failedToLoadClip;
        [SerializeField] private AudioClip splatDisabledClip;

        private void Awake()
        {
            if (stateMachine == null)
                Debug.LogError($"{nameof(HolodeckAudioFeedback)} is missing a HolodeckStateMachine.", this);

            if (audioSource == null)
                Debug.LogError($"{nameof(HolodeckAudioFeedback)} is missing an AudioSource.", this);
        }

        private void OnEnable()
        {
            if (stateMachine != null)
                stateMachine.StateChanged += HandleStateChanged;

            if (worldManager != null)
                worldManager.OnWorldLoaded += HandleWorldLoaded;
        }

        private void OnDisable()
        {
            if (stateMachine != null)
                stateMachine.StateChanged -= HandleStateChanged;

            if (worldManager != null)
                worldManager.OnWorldLoaded -= HandleWorldLoaded;
        }

        private void HandleStateChanged(HolodeckState previous, HolodeckState next)
        {
            switch (next)
            {
                case HolodeckState.ListeningForCommand:
                    if (listeningClip != null)
                        audioSource.PlayOneShot(listeningClip);
                    break;

                case HolodeckState.Interpreting:
                    if (heardClip != null)
                        audioSource.PlayOneShot(heardClip);
                    break;

                case HolodeckState.Ready:
                    if (splatLoadedClip != null)
                        audioSource.PlayOneShot(splatLoadedClip);
                    break;

                case HolodeckState.Error:
                    if (failedToLoadClip != null)
                        audioSource.PlayOneShot(failedToLoadClip);
                    break;
            }
        }

        private void HandleWorldLoaded(string worldId, GaussianSplatRenderer renderer)
        {
            if (worldId == "__default__" && splatDisabledClip != null)
                audioSource.PlayOneShot(splatDisabledClip);
        }

        /// <summary>
        /// Wire this to ThumbnailSkyboxController.onThumbnailShown in the Inspector.
        /// </summary>
        public void PlayPanoramaLoadedClip()
        {
            if (panoramaLoadedClip != null)
                audioSource.PlayOneShot(panoramaLoadedClip);
        }
    }
}
```

- [ ] **Step 2: Verify compilation**

Switch to Unity Editor. Check the Console for compile errors.

Expected: no errors. `WorldLabsWorldManager` and `GaussianSplatRenderer` are resolved by the two new using statements. All existing `HolodeckState` values are in the same namespace and require no new using.

- [ ] **Step 3: Commit**

```bash
git add Assets/App/Scripts/State/HolodeckAudioFeedback.cs
git commit -m "feat: add world-lifecycle audio cues to HolodeckAudioFeedback"
```

---

## Task 2: Wire and verify in the Editor

- [ ] **Step 1: Wire `worldManager`**

On the `HolodeckAudioFeedback` component in the Inspector, drag the GameObject that holds `WorldLabsWorldManager` into the **World Manager** field.

- [ ] **Step 2: Wire `PlayPanoramaLoadedClip` to `onThumbnailShown`**

Select the GameObject that holds `ThumbnailSkyboxController`. In the Inspector, find the **On Thumbnail Shown** UnityEvent. Click **+** to add a listener:
- Object: the GameObject holding `HolodeckAudioFeedback`
- Function: `HolodeckAudioFeedback → PlayPanoramaLoadedClip`

- [ ] **Step 3: Assign clips**

On `HolodeckAudioFeedback`, assign audio assets to:
- **Panorama Loaded Clip**
- **Splat Loaded Clip**
- **Failed To Load Clip**
- **Splat Disabled Clip**

All are optional — leave empty to test wiring before sourcing clips.

- [ ] **Step 4: Test in Play mode**

Run through a full generation cycle. Expected audio sequence:
1. Press A / Space → listening clip
2. Press A / Space again → heard clip
3. Panorama sphere appears → panorama loaded clip
4. Old world clears, default model re-appears → splat disabled clip
5. New splat finishes loading → splat loaded clip

To test the error cue: disconnect network or enter a nonsense prompt and observe the failed clip on error state.

**If splat disabled clip never plays:**
- Confirm `worldManager` is wired in Inspector
- Confirm `WorldLabsWorldManager.RestoreDefaultWorld()` is being called (check Console for `[WorldLabsWorldManager]` logs)
- The default asset must be assigned on `WorldLabsWorldManager` for `OnWorldLoaded("__default__", ...)` to fire
