# Floor Loader Integration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Integrate `RuntimeSplatFloorLoader` into the voice-driven world-loading flow so that each loaded splat is floor-detected and placed with its floor at Y=0, with SPZ bytes downloaded exactly once.

**Architecture:** The WorldLabs package is embedded as a local editable package (`Packages/com.worldlabs.gaussian-splatting/`) so `WorldLabsWorldManager` can be extended with three public methods that expose its event infrastructure to external callers. `VoiceToWorldLabsPluginCoordinator` gains an optional `floorLoader` field; when assigned, it downloads SPZ bytes directly, calls `RuntimeSplatFloorLoader.LoadPlacedRuntimeWorld`, and manually fires the world-lifecycle events via the new methods. When `floorLoader` is null, the existing `LoadWorldAsync` path is used unchanged.

**Tech Stack:** Unity C#, `WorldLabsWorldManager` (`WorldLabs.Runtime`), `RuntimeSplatFloorLoader` / `SplatFloorAnalyzer` (`WorldLabs.Runtime.Tools`), `GaussianSplatRenderer` (`GaussianSplatting.Runtime`)

**Spec:** `docs/superpowers/specs/2026-03-28-floor-loader-integration-design.md`

---

## File Map

| Action | File | Purpose |
|--------|------|---------|
| **Shell copy** | `Library/PackageCache/com.worldlabs.gaussian-splatting@d573119ed976/` → `Packages/com.worldlabs.gaussian-splatting/` | Create local editable package |
| **Modify** | `Packages/manifest.json:17` | Switch reference from git URL to `file:` path |
| **Modify** | `Packages/com.worldlabs.gaussian-splatting/Runtime/WorldLabs/WorldLabsWorldManager.cs` | Add three event-infrastructure methods after the `RestoreDefaultWorld` method (~line 283) |
| **Modify** | `Assets/App/Scripts/Direct/VoiceToWorldLabsPluginCoordinator.cs` | Add `using`, `floorLoader` field, `ResolveSpzUrl` helper, replace load block |

---

## Task 1: Embed the WorldLabs Package Locally

Unity reads `file:` package references relative to the `Packages/` directory. Once copied, Unity will compile from the local source. The git URL reference is dropped.

**Files:**
- Shell copy: `Library/PackageCache/com.worldlabs.gaussian-splatting@d573119ed976/` → `Packages/com.worldlabs.gaussian-splatting/`
- Modify: `Packages/manifest.json:17`

- [ ] **Step 1: Copy the package from the cache**

Run this from the project root (`/Users/davidarendash/Documents/Projects/Unity/HeadsetHolodeck`):

```bash
cp -R "Library/PackageCache/com.worldlabs.gaussian-splatting@d573119ed976/" \
      "Packages/com.worldlabs.gaussian-splatting/"
```

Expected: directory `Packages/com.worldlabs.gaussian-splatting/` is created with the same contents as the cache directory.

- [ ] **Step 2: Update `manifest.json`**

In `Packages/manifest.json`, line 17, change:

```json
"com.worldlabs.gaussian-splatting": "https://github.com/nigelhartman/worldlabs_unity.git",
```

to:

```json
"com.worldlabs.gaussian-splatting": "file:com.worldlabs.gaussian-splatting",
```

- [ ] **Step 3: Verify Unity resolves the local package**

Switch to Unity Editor. Unity will detect the manifest change and re-resolve packages. Wait for the spinning progress indicator in the bottom-right to finish.

Expected: no Package Manager errors. The Console should show no errors related to `com.worldlabs.gaussian-splatting`. The package appears in **Window → Package Manager → In Project** as a local package.

If Unity shows "Cannot find package": confirm the `Packages/com.worldlabs.gaussian-splatting/package.json` file exists (it should — it was part of the cache copy).

- [ ] **Step 4: Commit**

```bash
git add Packages/manifest.json Packages/com.worldlabs.gaussian-splatting/
git commit -m "feat: embed WorldLabs package as local editable copy"
```

Note: the package directory may be large. If `.gitignore` excludes it, add a `!Packages/com.worldlabs.gaussian-splatting/` exception or use `git add --force`. Confirm the commit contains the package files before proceeding.

---

## Task 2: Add Three Event-Infrastructure Methods to `WorldLabsWorldManager`

These three methods let external callers (the coordinator) participate in the world-lifecycle event system without calling `LoadWorldAsync`. They follow the exact same null-guard style and event invocation patterns already in the file.

**Files:**
- Modify: `Packages/com.worldlabs.gaussian-splatting/Runtime/WorldLabs/WorldLabsWorldManager.cs`

Insert the three methods into the **Public API** region, immediately after the closing brace of `RestoreDefaultWorld` (currently around line 283 — after you copy the file it will be at the same position). Add `using System;` at the top if it isn't already there (it is — line 3).

- [ ] **Step 1: Add `NotifyWorldLoadStarted`**

Add this method after `RestoreDefaultWorld`:

```csharp
/// <summary>
/// Adds <paramref name="worldId"/> to the loading set and fires <see cref="OnWorldLoadStarted"/>.
/// Call this before downloading SPZ bytes when bypassing <see cref="LoadWorldAsync"/>.
/// </summary>
public void NotifyWorldLoadStarted(string worldId)
{
    if (string.IsNullOrEmpty(worldId))
        throw new ArgumentException("worldId must not be null or empty.", nameof(worldId));

    _loadingWorlds.Add(worldId);
    OnWorldLoadStarted?.Invoke(worldId);
}
```

- [ ] **Step 2: Add `RegisterExternalWorld`**

Add this method after `NotifyWorldLoadStarted`:

```csharp
/// <summary>
/// Registers an externally-created renderer, dismisses the default placeholder,
/// and fires <see cref="OnWorldLoaded"/> / <see cref="onWorldLoaded"/>.
/// Call this after <see cref="RuntimeSplatFloorLoader.LoadPlacedRuntimeWorld"/> succeeds.
/// </summary>
public void RegisterExternalWorld(string worldId, GaussianSplatRenderer renderer)
{
    if (string.IsNullOrEmpty(worldId))
        throw new ArgumentException("worldId must not be null or empty.", nameof(worldId));
    if (renderer == null)
        throw new ArgumentNullException(nameof(renderer));

    _loadedWorlds[worldId] = renderer;
    _loadingWorlds.Remove(worldId);

    if (_loadedWorlds.ContainsKey("__default__"))
        UnloadWorld("__default__");

    OnWorldLoaded?.Invoke(worldId, renderer);
    onWorldLoaded?.Invoke(worldId, renderer);
}
```

- [ ] **Step 3: Add `NotifyWorldLoadFailed`**

Add this method after `RegisterExternalWorld`:

```csharp
/// <summary>
/// Removes <paramref name="worldId"/> from the loading set and fires
/// <see cref="OnWorldLoadFailed"/> / <see cref="onWorldLoadFailed"/>.
/// Call this if SPZ download, floor analysis, or renderer creation fails.
/// </summary>
public void NotifyWorldLoadFailed(string worldId, string error)
{
    if (string.IsNullOrEmpty(worldId))
        throw new ArgumentException("worldId must not be null or empty.", nameof(worldId));

    _loadingWorlds.Remove(worldId);

    OnWorldLoadFailed?.Invoke(worldId, error ?? string.Empty);
    onWorldLoadFailed?.Invoke(worldId, error ?? string.Empty);
}
```

- [ ] **Step 4: Verify compilation**

Switch to Unity Editor. Check the Console for compile errors.

Expected: no errors. If you see "The type or namespace 'ArgumentException' could not be found" — `System` is already imported at line 3, so this should not occur.

- [ ] **Step 5: Commit**

```bash
git add Packages/com.worldlabs.gaussian-splatting/Runtime/WorldLabs/WorldLabsWorldManager.cs
git commit -m "feat: add NotifyWorldLoadStarted, RegisterExternalWorld, NotifyWorldLoadFailed to WorldLabsWorldManager"
```

---

## Task 3: Update `VoiceToWorldLabsPluginCoordinator`

Adds an optional `floorLoader` field, a `ResolveSpzUrl` helper, and replaces the `RestoreDefaultWorld` + `LoadWorldAsync` block with a branching block: floor-loader path (single download) or fallback to `LoadWorldAsync` (unchanged).

**Files:**
- Modify: `Assets/App/Scripts/Direct/VoiceToWorldLabsPluginCoordinator.cs`

### Context: current load block

The block to replace is at lines 320–338 (after the panorama download section):

```csharp
// RestoreDefaultWorld and LoadWorldAsync are called here in the coroutine (main thread)
// rather than inside the async task, ensuring Unity scene operations are thread-safe.
worldManager.RestoreDefaultWorld();

Task loadTask = worldManager.LoadWorldAsync(world);
while (!loadTask.IsCompleted)
{
    yield return null;
}

if (loadTask.IsFaulted)
{
    isBusy = false;
    Exception baseException = loadTask.Exception != null
        ? loadTask.Exception.GetBaseException()
        : null;
    stateMachine.SetError(baseException != null ? baseException.Message : "World load failed.");
    yield break;
}
```

Everything after this block (lines 340–361) is unchanged.

- [ ] **Step 1: Add `using WorldLabs.Runtime.Tools;`**

At the top of `VoiceToWorldLabsPluginCoordinator.cs`, add after the existing `using WorldLabs.Runtime;` (line 10):

```csharp
using WorldLabs.Runtime.Tools;
```

- [ ] **Step 2: Add the `floorLoader` serialized field**

Inside the class, after line 30 (`[SerializeField] private bool logDebugMessages = true;`) and before the `[Header("Runtime Debug")]` block, add a new header and field:

```csharp
[Header("Floor Detection")]
[SerializeField] private RuntimeSplatFloorLoader floorLoader;
```

No `Awake` null check — if null, the coordinator falls back to `LoadWorldAsync`.

- [ ] **Step 3: Add the `ResolveSpzUrl` helper method**

Add this private method anywhere in the private helpers section of the class (e.g. after the `GenerateWorldAsync` method at the bottom):

```csharp
private string ResolveSpzUrl(World world)
{
    string resKey = worldManager.preferredResolution switch
    {
        WorldLabsWorldManager.SplatResolution.FullRes => "full_res",
        WorldLabsWorldManager.SplatResolution._100k   => "100k",
        _                                              => "500k",
    };
    return world.assets?.splats?.GetUrl(resKey)
        ?? world.assets?.splats?.GetBestResolutionUrl();
}
```

This mirrors the URL resolution logic inside `WorldLabsWorldManager.LoadWorldAsync` so the coordinator downloads the same resolution the manager would have.

- [ ] **Step 4: Replace the load block**

Replace the existing load block (the `RestoreDefaultWorld` + `LoadWorldAsync` block shown in Context above) with:

```csharp
if (floorLoader != null)
{
    // ── Floor-loader path (single download) ──────────────────────────
    string spzUrl = ResolveSpzUrl(world);
    if (string.IsNullOrEmpty(spzUrl))
    {
        isBusy = false;
        stateMachine.SetError("No SPZ URL found in world assets.");
        yield break;
    }

    worldManager.RestoreDefaultWorld();
    worldManager.NotifyWorldLoadStarted(lastWorldId);

    // Download SPZ bytes once — reused by LoadPlacedRuntimeWorld internally
    Task<byte[]> spzTask = WorldLabsClientExtensions.DownloadBinaryAsync(spzUrl);
    while (!spzTask.IsCompleted)
        yield return null;

    if (spzTask.IsFaulted)
    {
        isBusy = false;
        worldManager.NotifyWorldLoadFailed(lastWorldId, spzTask.Exception?.GetBaseException().Message ?? "SPZ download failed.");
        stateMachine.SetError(spzTask.Exception?.GetBaseException().Message ?? "SPZ download failed.");
        yield break;
    }

    byte[] spzBytes = spzTask.Result;

    // Load and place renderer (floor analysis runs inside LoadPlacedRuntimeWorld)
    RuntimeSplatFloorLoader.LoadResult placed;
    try
    {
        placed = floorLoader.LoadPlacedRuntimeWorld(
            spzBytes,
            worldId: lastWorldId,
            worldName: world.display_name,
            thumbnailUrl: world.assets?.thumbnail_url,
            gameObjectName: $"World_{world.display_name ?? lastWorldId}");
    }
    catch (Exception ex)
    {
        isBusy = false;
        worldManager.NotifyWorldLoadFailed(lastWorldId, ex.Message);
        stateMachine.SetError(ex.Message);
        yield break;
    }

    if (logDebugMessages && placed.floorEstimate != null)
    {
        SplatFloorEstimate est = placed.floorEstimate;
        Debug.Log($"[VoiceToWorldLabsPluginCoordinator] Floor analysis: success={est.success} floorY={est.estimatedFloorY:F3} message='{est.message}'", this);
    }

    worldManager.RegisterExternalWorld(lastWorldId, placed.renderer);
}
else
{
    // ── Fallback: standard WorldLabsWorldManager load ─────────────────
    // RestoreDefaultWorld and LoadWorldAsync are called here in the coroutine (main thread)
    // rather than inside the async task, ensuring Unity scene operations are thread-safe.
    worldManager.RestoreDefaultWorld();

    Task loadTask = worldManager.LoadWorldAsync(world);
    while (!loadTask.IsCompleted)
    {
        yield return null;
    }

    if (loadTask.IsFaulted)
    {
        isBusy = false;
        Exception baseException = loadTask.Exception != null
            ? loadTask.Exception.GetBaseException()
            : null;
        stateMachine.SetError(baseException != null ? baseException.Message : "World load failed.");
        yield break;
    }
}
```

The `TryTransitionTo(Ready)`, `thumbnailSkybox.StartFadeOut()`, and `isBusy = false` lines that follow are **not changed**.

- [ ] **Step 5: Verify compilation**

Switch to Unity Editor. Check the Console for compile errors.

Expected: no errors. Common issues and fixes:
- "The type or namespace 'RuntimeSplatFloorLoader' could not be found" — confirm `using WorldLabs.Runtime.Tools;` was added (Step 1) and the package is embedded (Task 1)
- "'SplatFloorEstimate' does not contain a definition for 'success'" — confirm `SplatFloorAnalyzer.cs` is present at `Assets/App/Scripts/Direct/SplatFloorAnalyzer.cs` and that the `SplatFloorEstimate` class it defines has a `public bool success` field
- "WorldLabsClientExtensions does not exist in the context" — `WorldLabs.API` is already imported at line 9; no additional using needed

- [ ] **Step 6: Commit**

```bash
git add Assets/App/Scripts/Direct/VoiceToWorldLabsPluginCoordinator.cs
git commit -m "feat: integrate RuntimeSplatFloorLoader into voice world-loading flow"
```

---

## Task 4: Wire and Test in the Editor

No code changes. Wire the new field in the Inspector and verify the floor-loader path fires correctly.

- [ ] **Step 1: Add `RuntimeSplatFloorLoader` component to the scene**

In the Hierarchy, find the GameObject that holds `VoiceToWorldLabsPluginCoordinator` (likely the Systems or Direct GameObject).

In the Inspector, click **Add Component** → search for `RuntimeSplatFloorLoader` → add it.

The component's shaders auto-populate on `Reset`/`Awake` from the package path. After adding, confirm the **Shaders** section in the Inspector shows non-null shader references. If they appear null, right-click the component header → **Reset** to trigger the `Reset()` editor callback.

- [ ] **Step 2: Wire `floorLoader` on the coordinator**

Select the same GameObject. On `VoiceToWorldLabsPluginCoordinator`, find the **Floor Detection** header. Drag the `RuntimeSplatFloorLoader` component (from the same or any other GameObject) into the **Floor Loader** field.

- [ ] **Step 3: Verify floor detection settings**

On the `RuntimeSplatFloorLoader` component, confirm:
- **Apply WorldLabs Default Rotation**: checked (true) — this applies the -180° X rotation the WorldLabs renderer expects
- **Auto Place At Origin**: checked (true) — this moves the splat so the floor lands at Y=0
- **Quality**: Medium (default) — matches `WorldLabsWorldManager`'s default

Adjust quality to match `WorldLabsWorldManager.quality` if you've changed it from the default.

- [ ] **Step 4: Test voice flow in Play mode**

Press Play. Trigger the voice flow (speak a world description or use the debug trigger button if one exists).

Expected sequence:
1. Voice captured → state transitions to `ListeningForCommand` → `Interpreting`
2. World generation begins — panorama sphere appears
3. `OnWorldLoadStarted` fires → `HolodeckModelController` shows `TNGHolodeck` mesh
4. SPZ bytes download (single download)
5. `LoadPlacedRuntimeWorld` runs — splat spawns with floor at Y=0
6. Console shows `[VoiceToWorldLabsPluginCoordinator] Floor analysis: success=True floorY=...`
7. `OnWorldLoaded` fires → `HolodeckModelController` hides `TNGHolodeck` mesh
8. State transitions to `Ready` → `HolodeckAudioFeedback` plays `splatLoadedClip`

**If the floor log line does not appear:** Confirm `logDebugMessages` is checked on the coordinator. If it appears but `success=False`: the floor analysis ran but found no strong floor candidate — placement still occurs (at `recommendedLocalPosition` which falls back to `Vector3.zero`), so the world still loads.

**If the splat appears floating or sunken:** The floor detection may have found an incorrect plane. Check the Console log for `floorY` — if it's very large or small, the splat data may have an unusual coordinate range. No code change is needed; this is a tuning concern for `SplatFloorAnalysisOptions`.

**If `TNGHolodeck` never appears:** Confirm `HolodeckModelController` is wired (Task from `2026-03-28-holodeck-model-controller.md`) and that `NotifyWorldLoadStarted` fires. Add a temporary `Debug.Log` inside `NotifyWorldLoadStarted` to confirm.

- [ ] **Step 5: Verify fallback path (optional)**

Temporarily clear the **Floor Loader** field on the coordinator (set to None). Run the voice flow again.

Expected: world loads via `LoadWorldAsync` — no floor correction, no floor log line, identical behavior to before this feature was added.

Re-assign the field after confirming.
