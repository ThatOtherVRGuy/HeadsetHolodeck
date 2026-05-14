# Voice Audio Feedback Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Create `HolodeckAudioFeedback`, a MonoBehaviour that plays a 2D "listening" cue when voice capture starts and a "heard" cue when it stops.

**Architecture:** A single new component subscribes to `HolodeckStateMachine.StateChanged`. On `→ ListeningForCommand` it plays `listeningClip`; on `→ Interpreting` it plays `heardClip`. Both clips are optional. No existing files are modified.

**Tech Stack:** Unity C#, `AudioSource.PlayOneShot`, `HolodeckStateMachine.StateChanged`

**Spec:** `docs/superpowers/specs/2026-03-28-audio-feedback-design.md`

---

## File Map

| Action | File | Purpose |
|--------|------|---------|
| **Create** | `Assets/App/Scripts/State/HolodeckAudioFeedback.cs` | Plays audio cues on state transitions |

---

## Task 1: Create `HolodeckAudioFeedback`

**Files:**
- Create: `Assets/App/Scripts/State/HolodeckAudioFeedback.cs`

- [ ] **Step 1: Create the script**

Create `Assets/App/Scripts/State/HolodeckAudioFeedback.cs`:

```csharp
using UnityEngine;

namespace Holodeck.State
{
    public sealed class HolodeckAudioFeedback : MonoBehaviour
    {
        [Header("Dependencies")]
        [SerializeField] private HolodeckStateMachine stateMachine;
        [SerializeField] private AudioSource audioSource;

        [Header("Clips")]
        [SerializeField] private AudioClip listeningClip;
        [SerializeField] private AudioClip heardClip;

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
        }

        private void OnDisable()
        {
            if (stateMachine != null)
                stateMachine.StateChanged -= HandleStateChanged;
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
            }
        }
    }
}
```

- [ ] **Step 2: Verify compilation**

Switch to Unity Editor. Check the Console for compile errors.

Expected: no errors. `HolodeckStateMachine` and `HolodeckState` are in the same `Holodeck.State` namespace — no extra using statement needed.

- [ ] **Step 3: Commit**

```bash
git add Assets/App/Scripts/State/HolodeckAudioFeedback.cs
git commit -m "feat: add HolodeckAudioFeedback for voice input audio cues"
```

---

## Task 2: Wire and verify in the Editor

- [ ] **Step 1: Add components to the Systems GameObject**

In the Unity scene hierarchy, select the `Systems` GameObject. In the Inspector:
1. Click **Add Component** → add `HolodeckAudioFeedback`
2. Click **Add Component** → add `Audio Source`
   - Set **Spatial Blend** to `0` (fully 2D)
   - Uncheck **Play On Awake**

- [ ] **Step 2: Wire references**

On `HolodeckAudioFeedback` in the Inspector:
- Drag the `Systems` GameObject (or wherever `HolodeckStateMachine` lives) into **State Machine**
- Drag the `Audio Source` component into **Audio Source**

- [ ] **Step 3: Assign audio clips**

Drag your audio clip assets into **Listening Clip** and **Heard Clip**.

If you don't have clips yet, leave them empty — the component silently skips unassigned clips. You can add them later without any code changes.

- [ ] **Step 4: Test in Play mode**

Press Play. Press Space (Editor) or A (Quest 3).

Expected:
- "Listening" clip plays immediately
- Say something, press Space / A again
- "Heard" clip plays

**If no sound plays:**
- Check Console for `HolodeckAudioFeedback is missing...` errors → wire the references
- Check that `Audio Source` Spatial Blend is 0 and volume is > 0
- Check that clips are assigned and are valid audio assets
