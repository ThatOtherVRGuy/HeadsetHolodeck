# Holodeck Model Controller Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Create `HolodeckModelController`, a MonoBehaviour that shows the TNGHolodeck static mesh while a world is loading and hides it once the world finishes (or fails), with an optional one-shot audio cue when loading begins.

**Architecture:** Single new file. Subscribes to three `WorldLabsWorldManager` C# events (`OnWorldLoadStarted`, `OnWorldLoaded`, `OnWorldLoadFailed`). On `OnWorldLoadStarted` for any non-default world, activates `holodeckModel` and plays an optional clip. On `OnWorldLoaded` or `OnWorldLoadFailed`, deactivates it.

**Tech Stack:** Unity C#, `WorldLabsWorldManager` (`WorldLabs.Runtime`), `GaussianSplatRenderer` (`GaussianSplatting.Runtime`), `AudioSource.PlayOneShot`

**Spec:** `docs/superpowers/specs/2026-03-28-holodeck-model-controller-design.md`

---

## File Map

| Action | File | Purpose |
|--------|------|---------|
| **Create** | `Assets/App/Scripts/State/HolodeckModelController.cs` | Show/hide TNGHolodeck during world load |

---

## Task 1: Create `HolodeckModelController`

**Files:**
- Create: `Assets/App/Scripts/State/HolodeckModelController.cs`

- [ ] **Step 1: Create the script**

Create `Assets/App/Scripts/State/HolodeckModelController.cs`:

```csharp
using GaussianSplatting.Runtime;
using UnityEngine;
using WorldLabs.Runtime;

namespace Holodeck.State
{
    public sealed class HolodeckModelController : MonoBehaviour
    {
        [Header("Dependencies")]
        [SerializeField] private WorldLabsWorldManager worldManager;
        [SerializeField] private AudioSource audioSource;

        [Header("Model")]
        [SerializeField] private GameObject holodeckModel;

        [Header("Clips")]
        [SerializeField] private AudioClip worldLoadingClip;

        // Sentinel worldId emitted by WorldLabsWorldManager for the built-in default asset.
        private const string DefaultWorldId = "__default__";

        private void Awake()
        {
            if (worldManager == null)
                Debug.LogError($"{nameof(HolodeckModelController)} is missing a WorldLabsWorldManager.", this);

            if (audioSource == null)
                Debug.LogError($"{nameof(HolodeckModelController)} is missing an AudioSource.", this);

            if (holodeckModel == null)
                Debug.LogError($"{nameof(HolodeckModelController)} is missing a holodeck model.", this);
        }

        private void OnEnable()
        {
            if (worldManager != null)
            {
                worldManager.OnWorldLoadStarted += HandleWorldLoadStarted;
                worldManager.OnWorldLoaded      += HandleWorldLoaded;
                worldManager.OnWorldLoadFailed  += HandleWorldLoadFailed;
            }
        }

        private void OnDisable()
        {
            if (worldManager != null)
            {
                worldManager.OnWorldLoadStarted -= HandleWorldLoadStarted;
                worldManager.OnWorldLoaded      -= HandleWorldLoaded;
                worldManager.OnWorldLoadFailed  -= HandleWorldLoadFailed;
            }
        }

        private void HandleWorldLoadStarted(string worldId)
        {
            if (worldId == DefaultWorldId) return;
            if (holodeckModel == null) return;

            holodeckModel.SetActive(true);

            if (audioSource != null && worldLoadingClip != null)
                audioSource.PlayOneShot(worldLoadingClip);
        }

        private void HandleWorldLoaded(string worldId, GaussianSplatRenderer renderer)
        {
            if (worldId == DefaultWorldId) return;
            if (holodeckModel == null) return;

            holodeckModel.SetActive(false);
        }

        private void HandleWorldLoadFailed(string worldId, string error)
        {
            if (holodeckModel == null) return;

            holodeckModel.SetActive(false);
        }
    }
}
```

- [ ] **Step 2: Verify compilation**

Switch to Unity Editor. Check the Console for compile errors.

Expected: no errors. `WorldLabsWorldManager` and `GaussianSplatRenderer` are resolved by the two new using statements.

- [ ] **Step 3: Commit**

```bash
git add Assets/App/Scripts/State/HolodeckModelController.cs
git commit -m "feat: add HolodeckModelController for world-load model visibility"
```

---

## Task 2: Wire and verify in the Editor

- [ ] **Step 1: Disable TNGHolodeck in the scene**

In the Hierarchy, find the `TNGHolodeck` GameObject. In the Inspector, uncheck the checkbox next to its name to set it inactive (`activeSelf = false`). Save the scene.

This is the required initial state — `HolodeckModelController` will activate it at runtime when a world load begins.

- [ ] **Step 2: Add `HolodeckModelController` to the Systems GameObject**

In the Hierarchy, select the Systems GameObject (the one that holds `HolodeckAudioFeedback`). In the Inspector, click **Add Component** and add `HolodeckModelController`.

- [ ] **Step 3: Wire references**

On the `HolodeckModelController` component:
- **World Manager**: drag the GameObject that holds `WorldLabsWorldManager`
- **Audio Source**: drag the `AudioSource` component already on the Systems GameObject (same one used by `HolodeckAudioFeedback`)
- **Holodeck Model**: drag the `TNGHolodeck` GameObject

- [ ] **Step 4: Assign clip (optional)**

Drag an audio asset into **World Loading Clip**. Leave empty to test wiring first — no sound plays but model show/hide still works.

- [ ] **Step 5: Test in Play mode via WorldBrowserController**

1. Press Play
2. Open the world browser UI
3. Click a world card to load a world

Expected sequence:
- World card clicked → TNGHolodeck appears immediately (world load started)
- World finishes loading → TNGHolodeck disappears
- If `worldLoadingClip` was assigned → clip plays when TNGHolodeck appears

**If TNGHolodeck never appears:**
- Confirm `worldManager` is wired in Inspector
- Confirm `OnWorldLoadStarted` fires: add a temporary `Debug.Log` inside `HandleWorldLoadStarted`
- Confirm TNGHolodeck was set inactive before pressing Play (not just `SetActive(false)` at runtime)

**If TNGHolodeck appears but never disappears:**
- Confirm `OnWorldLoaded` fires: check Console for `[WorldLabsWorldManager]` loaded logs
- If load failed silently, check for `[WorldLabsWorldManager]` error logs — `HandleWorldLoadFailed` should hide it

- [ ] **Step 6: Test via voice flow**

Run a voice-driven generation cycle. Expected:
- Panorama sphere appears and expands (covers the scene)
- TNGHolodeck activates behind the sphere (invisible due to sphere coverage) — acceptable
- New splat loads → TNGHolodeck deactivates

No special-casing needed. The panorama sphere at scale 500 visually covers the holodeck mesh.
