# SPZ / Gaussian Splat Renderer Handoff Notes

> **Status: historical investigation, partly implemented, partly still open.** This note was written
> against an earlier checkout (`…/HeadsetHolodeck`); the current project is
> `…/HeadsetHolodeckDev`. Its agreed changes are present — runtime-created loose splats still get
> `m_SHOrder = 0` and World Labs splats keep `m_SHOrder = 3` through
> `RuntimeSplatFloorLoader.GetSHOrderForSource`, with `m_OptimizeForQuest` set by
> `ShouldOptimizeForQuest` — so treat the "already made" section as landed, not as a proposal. The
> remaining "things worth trying" are unstarted ideas, not a backlog: loose-splat visual fidelity is
> still an open limitation and the notes make no verified fidelity claim. Current project status
> lives in [docs/stabilization/STATUS.md](docs/stabilization/STATUS.md) and
> [docs/stabilization/handoff-06.md](docs/stabilization/handoff-06.md).

Context: this thread investigated why Meta Hyperscape Capture "loose" `.spz` files look worse in our Unity renderer than WorldLabs splats, even though the same captures look excellent in Meta's own Hyperscape renderer on Quest.

Primary project discussed:

- `/Users/davidarendash/Documents/Projects/Unity/HeadsetHolodeck`

Key files touched or inspected:

- `/Users/davidarendash/Documents/Projects/Unity/HeadsetHolodeck/Assets/App/Scripts/Direct/RuntimeSplatFloorLoader.cs`
- `/Users/davidarendash/Documents/Projects/Unity/HeadsetHolodeck/Assets/App/Editor/RuntimeSplatProcessingBatchTests.cs`
- `/Users/davidarendash/Documents/Projects/Unity/HeadsetHolodeck/Packages/com.worldlabs.gaussian-splatting/Runtime/GaussianSplatting/SPZFileReader.cs`
- `/Users/davidarendash/Documents/Projects/Unity/HeadsetHolodeck/Packages/com.worldlabs.gaussian-splatting/Runtime/GaussianSplatting/GaussianSplatRenderer.cs`
- `/Users/davidarendash/Documents/Projects/Unity/HeadsetHolodeck/Packages/com.worldlabs.gaussian-splatting/Shaders/GaussianSplatting.hlsl`
- `/Users/davidarendash/Documents/Projects/Unity/HeadsetHolodeck/Packages/com.worldlabs.gaussian-splatting/Shaders/RenderGaussianSplats.shader`

## User-Visible Findings

The Meta Hyperscape loose splat looked too transparent / impressionistic in our renderer.

Scaling helped a little, but also made some splats appear stretched. That suggested scale alone was not the root issue.

In Unity Editor, changing `GaussianSplatRenderer.m_SHOrder` to `0` made the loose Meta/Hyperscape file look much better.

`m_SHOrder` values `1`, `2`, and `3` all looked different from each other, but none looked as good as `0`.

This strongly suggests the base color path is mostly okay, while the spherical harmonics/view-dependent color path is incompatible for these loose files, possibly due to source-specific SH convention, coordinate conversion, flags, or undocumented extensions.

Meta's own Hyperscape renderer displays the same capture beautifully on device, so the capture itself is not inherently low quality or intentionally impressionistic.

## Files Compared

Loose Meta/Hyperscape-style file:

- `/Users/davidarendash/Documents/Projects/Worlds/34_coffee_Tracy_9M.spz`
- Gzip SPZ v2
- Magic: `NGSP`
- Point count: `360,677`
- SH degree: `3`
- Fractional bits: `12`
- Flags: `0x0c`
- Compressed size: about `9.4 MB`
- Decompressed size: `23,083,344` bytes

WorldLabs file:

- `/Users/davidarendash/Documents/Projects/Worlds/CachedWorlds/MartianOutpost_51a737c1-8465-4918-a3d9-bc23d739f193.spz`
- Gzip SPZ v2
- Point count: `500,000`
- SH degree: `0`
- Flags: `0`
- Compressed size: about `7.8 MB`
- Decompressed size: `9,500,016` bytes

## Alpha / Transparency Findings

The loose Meta/Hyperscape file does not appear to have lower raw alpha.

Observed alpha distributions:

- Loose file: median alpha about `255`, mean about `251`
- WorldLabs file: median alpha about `121`, mean about `119`

So the "too transparent" look is probably not caused by raw alpha being too small, and it is unlikely to be a simple endian issue in alpha.

## Scale / Shape Findings

The loose Meta/Hyperscape file has much more anisotropic splats than the WorldLabs file.

Approximate ratio of largest scale axis to smallest scale axis:

- Loose file:
  - Median: about `58x`
  - P90: about `708x`
  - P99: about `4617x`
  - Max: about `1.86M`
- WorldLabs file:
  - Median: about `2.1x`
  - P90: about `6.5x`
  - P99: about `42.5x`

This explains why scale tweaks can make the file look stretched. The file already contains very elongated splats, and a renderer/convention mismatch can make that worse.

## SPZ Spec Status

The public definitive SPZ spec found so far is Niantic's:

- https://github.com/nianticlabs/spz
- File format section: https://github.com/nianticlabs/spz#file-format
- Spherical harmonics section: https://github.com/nianticlabs/spz#spherical-harmonics

No public Meta-published `.spz` specification was found.

Meta appears to use `.spz` or an SPZ-family format for Hyperscape-related captures, but we should treat these files as possibly vendor-extended rather than guaranteed vanilla Niantic SPZ.

The loose file's flags value `0x0c` is suspicious because the public Niantic spec currently documents different public flag bits, such as antialiasing and extension flags in newer versions. This may indicate Meta-specific/private flags or old/alternate semantics.

## Public Niantic SPZ Details Relevant To This Bug

The public SPZ spec says:

- Default coordinate system is RUB.
- If converting coordinate systems, SH coefficients should be rotated/flipped using the appropriate Wigner D-matrix.
- SH data is coefficient-major with RGB as the faster axis.
- Example ordering is like `sh1n1_r, sh1n1_g, sh1n1_b, sh10_r, ...`.

The Niantic reference implementation inspected locally at `/private/tmp/niantic-spz` confirms:

- Public layout for SH is coefficient-major, channel-inner.
- Pack/unpack performs coordinate conversion and rotates/flips SH.
- Version 2 rotations store the first three quaternion components directly.
- Version 3+ uses the "smallest three" quaternion encoding.

## Current Reader / Renderer Observations

`SPZFileReader.cs` parses header flags but currently discards them in `ReadImpl`, using an `out _` pattern.

The reader appears to support legacy gzip SPZ v2. It does not appear to expose flags/extensions to higher-level runtime code.

For SPZ v2, the reader handles:

- Positions as 24-bit little-endian values.
- Scales as log values converted with `exp`.
- Rotations as first-three quaternion components.
- SH order in the public coefficient-major/RGB-inner order.

The renderer default:

- `GaussianSplatRenderer.m_SHOrder` defaults to `3`.
- `GaussianSplatRenderer.m_OptimizeForQuest` defaults to `false`.

The shader path computes an object-space view direction and feeds it to `ShadeSH`.

`GaussianSplatting.hlsl` includes a `dir *= -1` inside `ShadeSH` and then uses standard 3DGS SH basis logic.

## Runtime Behavior Found

Changing a prefab did not matter because active splat renderers were being created at runtime.

`RuntimeSplatFloorLoader` creates a new `GameObject` and calls `AddComponent<GaussianSplatRenderer>()`, then applies splat data.

The active app paths for loose and WorldLabs splats go through `RuntimeSplatFloorLoader`.

## Code Changes Already Made In This Thread

File:

- `/Users/davidarendash/Documents/Projects/Unity/HeadsetHolodeck/Assets/App/Scripts/Direct/RuntimeSplatFloorLoader.cs`

Added centralized runtime renderer configuration:

```csharp
internal static bool ShouldOptimizeForQuest(RuntimePlatform platform)
{
    return platform == RuntimePlatform.Android;
}

internal static int GetSHOrderForSource(SplatSourceKind sourceKind)
{
    return sourceKind == SplatSourceKind.LooseSplat ? 0 : 3;
}
```

Also added a `ConfigureRuntimeRenderer` helper that:

- Assigns shaders.
- Sets `m_OptimizeForQuest` based on platform.
- Sets `m_SHOrder` based on source type.

Current behavior after the patch:

- Runtime-created loose splat renderers use `m_SHOrder = 0`.
- Runtime-created WorldLabs splat renderers retain `m_SHOrder = 3`.
- Runtime-created renderers set `m_OptimizeForQuest = true` on Android / Quest.

Important caveat:

- This applies only when a renderer is created. Already-created renderer instances keep their current Inspector values until reloaded or changed manually.

File:

- `/Users/davidarendash/Documents/Projects/Unity/HeadsetHolodeck/Assets/App/Editor/RuntimeSplatProcessingBatchTests.cs`

Added a reflection-based batch test covering:

- Android platform should optimize for Quest.
- Editor platform should not use the Quest optimization path.
- Loose splats should use SH order `0`.
- WorldLabs splats should use SH order `3`.

Unity batch verification passed after the change.

Command shape used:

```bash
/Applications/Unity/Hub/Editor/6000.2.10f1/Unity.app/Contents/MacOS/Unity \
  -batchmode \
  -quit \
  -projectPath /Users/davidarendash/Documents/Projects/Unity/HeadsetHolodeck \
  -executeMethod HeadsetHolodeck.EditorTests.RuntimeSplatProcessingBatchTests.RuntimeSplatFloorLoaderUsesQuestRendererPath \
  -logFile /private/tmp/headsetholodeck-loose-sh-green.log
```

The log contained:

```text
[RuntimeSplatProcessingBatchTests] Quest renderer configuration test passed.
```

Also ran:

```bash
git diff --check
```

No whitespace errors were reported.

## Working Hypotheses

Most likely:

- Meta/Hyperscape loose files use valid base color data, but their SH data is incompatible with the current renderer's interpretation.

Possible causes:

- Different SH basis convention.
- Different SH sign or axis convention.
- Coordinate conversion applied to positions/rotations but not correctly to SH.
- Meta-specific SPZ flags or extensions not understood by our reader.
- Vendor-specific meaning for SPZ v2 header flags `0x0c`.
- Renderer shader direction convention mismatch.

Less likely based on evidence:

- Simple alpha endian mismatch.
- Raw opacity values being too low.
- Purely aesthetic/source-quality issue in the Meta capture.

Still possible:

- Some scale or covariance convention mismatch, especially because the loose file's splats are extremely anisotropic.

## Things Worth Trying Next

1. Expose SPZ header metadata from `SPZFileReader`.

   Return or log:

   - Version
   - Point count
   - SH degree
   - Fractional bits
   - Flags
   - Whether any extension bytes are present

   This would let runtime code choose compatibility modes based on actual file metadata instead of only `SplatSourceKind.LooseSplat`.

2. Add a debug/compatibility enum for loose splat SH interpretation.

   Possible values:

   - `Disabled`
   - `Current`
   - `FlipViewDirection`
   - `SwapX`
   - `SwapY`
   - `SwapZ`
   - `RUBToUnity`
   - `MetaHyperscape`

   This should ideally be testable in Editor without rebuilding.

3. Add a serialized loader setting for loose splat SH order.

   Instead of hard-coding loose splats to `0`, expose something like:

   ```csharp
   [SerializeField, Range(0, 3)]
   int looseSplatSHOrder = 0;
   ```

   Default would remain `0`, but live testing could compare values.

4. Add file-level detection for suspicious Meta/Hyperscape SPZs.

   Current source-type fallback is useful, but better detection may use:

   - SPZ version `2`
   - SH degree `3`
   - Flags `0x0c`
   - Possibly filename/source path when known

5. Inspect whether the loose file contains undocumented trailing or extension data.

   Public v2 gzip SPZ has a predictable decompressed byte count based on point count and SH degree. If there are extra bytes, they may be extension data.

6. Compare our decoded values against Niantic's reference decoder.

   Use `/private/tmp/niantic-spz` as a reference implementation. Export or inspect positions, rotations, scales, alpha, base color, and SH coefficients.

7. Try rendering loose files with SH disabled but keep all other data unchanged.

   This is already effectively what `m_SHOrder = 0` does in the renderer. Use it as the visual baseline.

8. Try correcting SH rather than disabling it.

   This is the ideal longer-term fix. Experiments should be isolated behind a compatibility setting so WorldLabs content is not regressed.

9. Verify on Quest with `m_OptimizeForQuest = true`.

   The renderer has this checkbox, but because renderers are created at runtime it must be set programmatically. The patch now does that for Android-created renderers.

10. Check whether package-level or manager-level runtime creation paths bypass `RuntimeSplatFloorLoader`.

   The app's active paths seemed to go through `RuntimeSplatFloorLoader`, but package code such as `WorldLabsWorldManager.cs` may create `GaussianSplatRenderer` directly.

## Suggested Next Implementation Step

The highest-value next step is to expose SPZ header flags from `SPZFileReader` and log them during runtime loading.

That would let us replace the broad rule:

```text
all LooseSplat => SH order 0
```

with a more precise compatibility rule:

```text
SPZ v2 + SH degree 3 + flags 0x0c => Meta/Hyperscape compatibility mode
```

After that, add a runtime/editor-selectable SH compatibility mode and test sign/axis transforms against the loose file.

## Important Caution

Do not globally change the shader or all SPZ loading based only on the Meta/Hyperscape loose file.

WorldLabs files and other vanilla SPZ files may depend on the existing interpretation. Keep compatibility handling source-specific or metadata-specific until the root cause is proven.
