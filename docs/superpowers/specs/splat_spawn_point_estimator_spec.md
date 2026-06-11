# Gaussian Splat Spawn Point Estimator

Implementation spec for integrating automatic spawn-point and look-at estimation into a Unity / XR Gaussian Splat environment viewer.

This document is written for Codex to ingest and implement. It assumes the host project already has some way to load `.ply` and/or `.spz` Gaussian splat files and render them in Unity or a related viewer.

---

## 1. Goal

Many immersive Gaussian splat captures represent environments rather than orbitable single objects. These files often lack camera metadata, spawn metadata, floor metadata, or a recommended initial view. The goal is to generate useful sidecar metadata automatically:

```json
{
  "spawn": [0.0, 1.6, 0.0],
  "lookAt": [0.0, 1.4, 2.0],
  "up": [0.0, 1.0, 0.0],
  "confidence": 0.82,
  "method": "normal_line_consensus_v1",
  "notes": "Auto-generated from splat orientation/scale/opacity."
}
```

The primary heuristic is that many environment splats are locally surface-like and their anisotropic ellipsoids tend to face toward one or more useful viewing regions. The estimator samples splats, infers approximate surface normals from each splat's rotation and scale, casts lines through those normals, and solves for a consensus point that minimizes distance to those lines.

This consensus point is not always the final spawn point. It is better understood as a likely scene focus or interior center. A separate candidate-selection stage converts that center into a safe XR spawn pose.

---

## 2. Definitions

- **Splat center**: The 3D position of a Gaussian splat.
- **Splat normal**: An approximate normal inferred from the splat ellipsoid. For 3DGS-like data, use the local axis with the smallest scale, transformed by the splat rotation.
- **Consensus point**: The 3D point that minimizes weighted squared distance to all sampled normal lines.
- **Look-at target**: The point the camera should face at startup. Usually the consensus point, optionally adjusted upward/downward.
- **Spawn point**: The initial XR head/camera position. This should be near, but not necessarily equal to, the consensus point.
- **Sidecar metadata**: A JSON file saved next to the splat asset, such as `kitchen.spz.spawn.json` or `kitchen.ply.spawn.json`.

---

## 3. Assumptions About Splat Data

The estimator should work after any `.ply`, `.spz`, `.splat`, `.ksplat`, or future glTF splat loader has exposed a common in-memory representation:

```csharp
public readonly struct SplatSample
{
    public readonly Vector3 Position;
    public readonly Quaternion Rotation;
    public readonly Vector3 Scale;
    public readonly float Opacity;
    public readonly bool HasRotation;
    public readonly bool HasScale;
    public readonly bool HasOpacity;
}
```

Typical 3DGS-style data uses fields similar to:

```text
x, y, z
scale_0, scale_1, scale_2
rot_0, rot_1, rot_2, rot_3
opacity
f_dc_0, f_dc_1, f_dc_2
optional spherical harmonics fields
```

Important implementation notes:

1. Some loaders expose already-decoded values. Others expose pre-activation training values.
2. If scale values look like unconstrained logs, apply `exp(scale)` before using them.
3. If opacity values look like logits, apply `sigmoid(opacity)` before using them.
4. Normalize quaternions before use.
5. Confirm quaternion component order. 3DGS conventions often use `rot_0, rot_1, rot_2, rot_3` as `w, x, y, z`; Unity `Quaternion` is constructed as `(x, y, z, w)`.
6. Coordinate-system conversion should happen before estimation. The estimator should operate in the same coordinate space used by the viewer.

---

## 4. Integration Overview

Add a new subsystem with these responsibilities:

```text
Assets/Scripts/Splats/SpawnEstimation/
    SplatSpawnMetadata.cs
    SplatSpawnEstimator.cs
    SplatAttributeNormalizer.cs
    SplatSampleProvider.cs
    SplatSpawnSidecarStore.cs
    SplatSpawnDebugGizmos.cs
    Editor/SplatSpawnBatchProcessor.cs        optional
    Tests/SplatSpawnEstimatorTests.cs         optional
```

Suggested runtime flow:

```text
User selects or loads splat file
    ↓
Check for sidecar spawn metadata
    ↓
If sidecar exists and version is current:
    use saved spawn pose
Else:
    ask loaded splat provider for sampled splat attributes
    run SplatSpawnEstimator
    save sidecar metadata
    apply spawn pose
```

Do not block the headset UI for large files. Prefer either:

- generate metadata in an import/editor/batch step, or
- run estimation asynchronously while showing a temporary default pose.

---

## 5. Public API

### 5.1 Metadata model

```csharp
[Serializable]
public sealed class SplatSpawnMetadata
{
    public int version = 1;
    public string method = "normal_line_consensus_v1";
    public string sourceAssetPath;
    public string sourceAssetHash;
    public Vector3 spawn;
    public Quaternion rotation;
    public Vector3 lookAt;
    public Vector3 up = Vector3.up;
    public Bounds bounds;
    public float confidence;
    public float medianResidual;
    public float sceneDiagonal;
    public int totalSplats;
    public int sampledSplats;
    public int acceptedSplats;
    public string[] warnings;
}
```

### 5.2 Estimator settings

```csharp
[Serializable]
public sealed class SplatSpawnEstimatorSettings
{
    public int randomSeed = 12345;
    public int maxSamples = 30000;
    public int minAcceptedSamples = 500;

    public bool applyExpToScale = false;       // set true only if loader exposes raw 3DGS scale logits
    public bool applySigmoidToOpacity = false; // set true only if loader exposes raw opacity logits

    public float minOpacity = 0.02f;
    public float minFlatnessRatio = 1.25f;     // maxScale / minScale
    public float maxFlatnessRatioForWeight = 8.0f;

    public float minScalePercentile = 0.01f;
    public float maxScalePercentile = 0.99f;

    public int robustIterations = 5;
    public float huberResidualFractionOfDiagonal = 0.02f;

    public bool assumeUnityYUp = true;
    public float defaultEyeHeightMeters = 1.6f;
    public float candidateRingRadiusFractionOfDiagonal = 0.08f;
    public float minCandidateClearanceFractionOfDiagonal = 0.015f;

    public int candidateRingCount = 2;
    public int candidateDirectionsPerRing = 12;
}
```

### 5.3 Main estimator interface

```csharp
public interface ISplatSampleProvider
{
    int Count { get; }
    Bounds Bounds { get; }
    bool TryGetSample(int index, out SplatSample sample);
}

public static class SplatSpawnEstimator
{
    public static SplatSpawnMetadata Estimate(
        ISplatSampleProvider provider,
        SplatSpawnEstimatorSettings settings,
        string sourceAssetPath = null,
        string sourceAssetHash = null);
}
```

---

## 6. Algorithm

### 6.1 Sample splats

For large splat files, do not process every splat unless the count is small. Use deterministic random sampling so repeated runs produce the same result.

```csharp
int sampleCount = Mathf.Min(provider.Count, settings.maxSamples);
IEnumerable<int> indices = DeterministicRandomIndices(provider.Count, sampleCount, settings.randomSeed);
```

Also compute or reuse global bounds.

```csharp
Bounds bounds = provider.Bounds;
float sceneDiagonal = bounds.size.magnitude;
```

### 6.2 Normalize fields

For each sample:

```csharp
Vector3 p = sample.Position;
Quaternion q = Normalize(sample.Rotation);
Vector3 s = sample.Scale;
float alpha = sample.Opacity;

if (settings.applyExpToScale)
    s = new Vector3(Mathf.Exp(s.x), Mathf.Exp(s.y), Mathf.Exp(s.z));

if (settings.applySigmoidToOpacity)
    alpha = 1.0f / (1.0f + Mathf.Exp(-alpha));
```

Reject samples with NaN/Inf, non-positive scale, very low opacity, or missing rotation/scale.

### 6.3 Infer approximate normal

A Gaussian splat ellipsoid has three local scale axes. The smallest scale axis is the likely local surface normal because a surface-like splat tends to be flattened perpendicular to the surface.

```csharp
int minAxis = IndexOfSmallest(s);
Vector3 localNormal = minAxis switch
{
    0 => Vector3.right,
    1 => Vector3.up,
    _ => Vector3.forward
};

Vector3 n = (q * localNormal).normalized;
```

The sign does not matter because the estimator uses an infinite line, not a directed ray.

Reject or downweight samples where the scale is nearly isotropic:

```csharp
float minScale = Mathf.Min(s.x, Mathf.Min(s.y, s.z));
float maxScale = Mathf.Max(s.x, Mathf.Max(s.y, s.z));
float flatnessRatio = maxScale / Mathf.Max(minScale, 1e-8f);

if (flatnessRatio < settings.minFlatnessRatio)
    reject;
```

### 6.4 Weight each line

A reasonable default weight:

```csharp
float opacityWeight = Mathf.Clamp01(alpha);
float flatnessWeight = Mathf.Clamp01(
    (flatnessRatio - settings.minFlatnessRatio) /
    (settings.maxFlatnessRatioForWeight - settings.minFlatnessRatio));

float scaleWeight = ComputeScaleWeightFromQuantileRange(s, settings);
float w = opacityWeight * flatnessWeight * scaleWeight;
```

The scale quantile filter should remove tiny numerical noise and giant blurry background/floater splats.

### 6.5 Solve weighted closest point to many lines

For each accepted sample, define a line through `p_i` along unit vector `n_i`.

The squared distance from a point `c` to the line is:

```text
|| (I - n_i n_i^T) (c - p_i) ||^2
```

The least-squares solution is:

```text
A = Σ w_i * (I - n_i n_i^T)
b = Σ w_i * (I - n_i n_i^T) * p_i
c = inverse(A) * b
```

Implementation detail: avoid allocating matrices inside the loop. Accumulate the six unique values of a symmetric 3x3 matrix and the three values of `b`.

Pseudo-C#:

```csharp
private static bool SolveLineConsensus(
    IReadOnlyList<WeightedLine> lines,
    out Vector3 c)
{
    double a00 = 0, a01 = 0, a02 = 0, a11 = 0, a12 = 0, a22 = 0;
    double b0 = 0, b1 = 0, b2 = 0;

    foreach (var line in lines)
    {
        Vector3 n = line.Direction.normalized;
        Vector3 p = line.Point;
        double w = line.Weight;

        // P = I - n n^T
        double p00 = 1.0 - n.x * n.x;
        double p01 =     - n.x * n.y;
        double p02 =     - n.x * n.z;
        double p11 = 1.0 - n.y * n.y;
        double p12 =     - n.y * n.z;
        double p22 = 1.0 - n.z * n.z;

        a00 += w * p00;
        a01 += w * p01;
        a02 += w * p02;
        a11 += w * p11;
        a12 += w * p12;
        a22 += w * p22;

        b0 += w * (p00 * p.x + p01 * p.y + p02 * p.z);
        b1 += w * (p01 * p.x + p11 * p.y + p12 * p.z);
        b2 += w * (p02 * p.x + p12 * p.y + p22 * p.z);
    }

    return SolveSymmetric3x3(
        a00, a01, a02,
             a11, a12,
                  a22,
        b0, b1, b2,
        out c);
}
```

Use double precision for accumulation. Convert back to `Vector3` at the end.

### 6.6 Robust refinement

The simple least-squares solution can be dominated by floors, ceilings, sky, vegetation, and noisy floaters. Add robust reweighting.

Procedure:

```text
1. Build initial weighted lines.
2. Solve consensus point c.
3. Compute residual for each line:
      r_i = distance(c, line_i)
4. Estimate robust scale using median residual.
5. Apply Huber or Tukey weight.
6. Repeat 3-5 times.
```

Huber weighting:

```csharp
float huberK = Mathf.Max(
    settings.huberResidualFractionOfDiagonal * sceneDiagonal,
    1e-5f);

float robustWeight = residual <= huberK ? 1.0f : huberK / residual;
line.Weight = line.BaseWeight * robustWeight;
```

This is usually enough. RANSAC can be added later if line consensus is still unstable.

### 6.7 Confidence score

After robust refinement:

```csharp
float medianResidual = Median(residuals);
float normalizedResidual = medianResidual / Mathf.Max(sceneDiagonal, 1e-5f);
float confidence = 1.0f - Mathf.Clamp01(normalizedResidual / 0.08f);
```

Also reduce confidence if:

- accepted sample count is below `minAcceptedSamples`,
- the solution is outside or far outside bounds,
- the matrix solve is ill-conditioned,
- residual distribution is broad or multi-modal,
- candidate spawn selection fails.

Example:

```csharp
if (acceptedCount < settings.minAcceptedSamples)
    confidence *= 0.25f;

if (!bounds.ExpandByFraction(0.25f).Contains(consensus))
    confidence *= 0.4f;
```

---

## 7. Spawn Candidate Selection

The normal-line consensus point is a likely scene center or look-at point. A VR spawn point should be chosen separately.

### 7.1 Estimate up and floor

For the first implementation, assume Unity Y-up unless the loader or file format provides a better up vector.

```csharp
Vector3 up = settings.assumeUnityYUp ? Vector3.up : EstimateUp(provider);
```

For floor height, use a conservative percentile of sampled positions along the up axis:

```csharp
float floorY = Percentile(sampledHighOpacityYValues, 0.03f);
```

This is not a true floor detector, but it is usually better than using `bounds.min.y` because splats can include noise below the visible floor.

Potential later improvement: voxelize high-opacity splats, find the largest mostly-horizontal low surface, and use that as a floor proxy.

### 7.2 Candidate positions

Generate candidates around the consensus point. Include the center and a few rings.

```text
candidate 0:
    xz = consensus xz
    y = floorY + eyeHeight

ring candidates:
    radius = sceneDiagonal * candidateRingRadiusFractionOfDiagonal * ringIndex
    direction = evenly spaced around up axis
    y = floorY + eyeHeight
```

For each candidate:

1. Reject if outside expanded bounds.
2. Reject if too close to dense splats near head position.
3. Compute look direction toward `lookAt`.
4. Score visibility and comfort.

### 7.3 Candidate clearance score

Approximate collision by measuring local splat density near the candidate head position. This does not need to be exact physics. It only needs to avoid spawning inside walls or dense objects.

```csharp
float clearanceRadius = settings.minCandidateClearanceFractionOfDiagonal * sceneDiagonal;
int nearby = CountHighOpacitySplatsWithin(candidate, clearanceRadius);
float clearanceScore = 1.0f / (1.0f + nearby);
```

### 7.4 Candidate view score

A simple first-pass score:

```csharp
float distanceToLookAt = Vector3.Distance(candidate, lookAt);
float idealDistance = 0.10f * sceneDiagonal;
float distanceScore = Mathf.Exp(-Mathf.Abs(distanceToLookAt - idealDistance) / idealDistance);

float centerScore = 1.0f - Mathf.Clamp01(
    Vector3.Distance(candidate, bounds.center) / sceneDiagonal);

float score =
    0.45f * clearanceScore +
    0.35f * distanceScore +
    0.20f * centerScore;
```

Pick the highest-scoring candidate. Save the camera rotation as:

```csharp
Quaternion rotation = Quaternion.LookRotation((lookAt - spawn).normalized, up);
```

---

## 8. Sidecar Metadata

### 8.1 File naming

For source file:

```text
Assets/Splats/CafeParis.spz
```

Save:

```text
Assets/Splats/CafeParis.spz.spawn.json
```

Alternative if the project uses an asset database:

```text
Application.persistentDataPath/SplatSpawnMetadata/<hash>.json
```

The sidecar should include a source hash so metadata can be invalidated when the splat file changes.

### 8.2 JSON example

```json
{
  "version": 1,
  "method": "normal_line_consensus_v1",
  "sourceAssetPath": "Assets/Splats/CafeParis.spz",
  "sourceAssetHash": "sha256:...",
  "spawn": [0.18, 1.58, -0.42],
  "rotation": [0.0, 0.707, 0.0, 0.707],
  "lookAt": [0.04, 1.35, 1.62],
  "up": [0.0, 1.0, 0.0],
  "boundsMin": [-4.8, -0.12, -5.1],
  "boundsMax": [5.0, 3.2, 5.8],
  "confidence": 0.82,
  "medianResidual": 0.31,
  "sceneDiagonal": 12.4,
  "totalSplats": 1843200,
  "sampledSplats": 30000,
  "acceptedSplats": 9472,
  "warnings": []
}
```

Use arrays for vectors/quaternions if the project's JSON serializer does not handle Unity types cleanly.

---

## 9. Unity Runtime Behavior

When a splat environment is loaded:

```csharp
public async Task ApplyBestSpawnPoseAsync(SplatAsset asset)
{
    var metadata = await sidecarStore.TryLoadAsync(asset);

    if (metadata == null || metadata.version != CurrentVersion || metadata.sourceAssetHash != asset.Hash)
    {
        metadata = await spawnEstimator.EstimateAsync(asset.SampleProvider, settings, asset.Path, asset.Hash);
        await sidecarStore.SaveAsync(asset, metadata);
    }

    xrRig.transform.SetPositionAndRotation(metadata.spawn, metadata.rotation);
}
```

If confidence is low:

- still use the result if it is sane,
- display a subtle “auto placement may be approximate” debug note in development builds,
- optionally expose “recenter here” or “save current pose as spawn” in the wrist/watch UI.

Recommended UX:

```text
Developer/debug build:
    show confidence, accepted sample count, residual, and candidate score

User-facing build:
    no message unless placement is obviously bad
    allow user to manually save current pose as preferred spawn
```

---

## 10. Manual Override

Add a way for the user/developer to stand in a good place and save the current XR rig pose as metadata.

```csharp
public void SaveCurrentPoseAsSpawn(SplatAsset asset)
{
    var head = xrCamera.transform;
    var metadata = sidecarStore.TryLoad(asset) ?? new SplatSpawnMetadata();

    metadata.spawn = head.position;
    metadata.rotation = head.rotation;
    metadata.lookAt = head.position + head.forward * 2.0f;
    metadata.confidence = 1.0f;
    metadata.method = "manual_override";

    sidecarStore.Save(asset, metadata);
}
```

Manual override should always win over auto-estimation unless the asset hash changes and the user chooses to regenerate.

---

## 11. Debug Visualization

Add optional gizmos:

- bounds box,
- consensus point as a sphere,
- selected spawn point as a camera/head icon,
- look-at ray,
- a small subset of normal lines,
- rejected candidate positions in red,
- accepted candidate positions in green,
- confidence/residual text label.

Example debug flags:

```csharp
public bool drawBounds = true;
public bool drawConsensus = true;
public bool drawSpawnPose = true;
public bool drawNormalLines = false;
public int maxDebugLines = 250;
```

---

## 12. Failure Cases and Fallbacks

### 12.1 Corridors / streets / long tunnels

One global center may not exist. The line consensus may land midway in a corridor or outside useful view.

Fallback:

- Use bounds center projected to floor.
- Or cluster the scene spatially and find the largest local consensus cluster.

### 12.2 Outdoor landscapes / drone captures

Normals may point in many directions, and the useful view may be aerial or edge-based.

Fallback:

- Use bounds center plus elevated view.
- Use a ring of candidate views and score by visible splat density.

### 12.3 Multi-room interiors

The algorithm may produce a center inside a wall between rooms.

Fallback:

- Build multiple local clusters.
- Use candidate clearance rejection.
- Provide manual override.

### 12.4 Object-like captures

The consensus point may correctly hit the object center, but the spawn should orbit outside the object rather than inside it.

Fallback:

- If the consensus point has high density nearby, choose a ring candidate outside the dense region.

### 12.5 Missing rotation or scale

Cannot infer normals reliably.

Fallback:

- Use bounds-based spawn.
- Set confidence low.
- Do not save method as `normal_line_consensus_v1`; use `bounds_fallback_v1`.

---

## 13. Optional Advanced Fallback: Local Consensus Clustering

If global confidence is low, estimate several candidate centers.

Possible implementation:

```text
1. Partition accepted splats into a coarse 3D grid or spatial clusters.
2. Run line consensus per cluster if cluster has enough samples.
3. Reject cluster solutions outside local expanded bounds.
4. Score clusters by:
      sample count
      low residual
      candidate clearance
      centrality
5. Choose best cluster.
```

This is simpler and more reliable than attempting all pairwise line intersections.

---

## 14. Batch Processing Tool

Add an editor or command-line utility to precompute metadata for all splats.

Suggested editor menu:

```text
Tools / Splats / Generate Spawn Metadata For Selected
Tools / Splats / Generate Spawn Metadata For Folder
Tools / Splats / Clear Auto Spawn Metadata
```

Suggested command-line Unity invocation:

```bash
Unity \
  -batchmode \
  -projectPath /path/to/project \
  -executeMethod SplatSpawnBatchProcessor.GenerateForAllSplats \
  -quit
```

For very large assets, prefer batch processing over runtime processing.

---

## 15. Test Plan

### 15.1 Synthetic unit tests

Create synthetic splat clouds with known expected centers.

Test cases:

1. **Sphere shell facing center**
   - Generate points on a sphere.
   - Normals point inward/outward.
   - Expected consensus near sphere center.

2. **Room box**
   - Generate six wall/floor/ceiling planes around a box.
   - Normals point toward interior.
   - Expected consensus near room center.

3. **Corridor**
   - Generate a long rectangular corridor.
   - Expected low-to-medium confidence or center near corridor midpoint.

4. **Noisy floaters**
   - Add random outlier splats.
   - Robust estimator should remain close to expected center.

5. **Isotropic blobs**
   - Use equal scales.
   - Estimator should reject most samples and use fallback.

6. **Quaternion component order test**
   - Verify known rotations produce expected axes.

### 15.2 Real asset tests

Use at least 10 real `.ply`/`.spz` assets:

- small interior room,
- multi-room interior,
- courtyard,
- object scan,
- street/corridor,
- outdoor natural environment,
- noisy capture with floaters,
- large-scale geospatial or drone-like asset if available.

For each asset, record:

```text
asset name
splat count
runtime
accepted sample count
confidence
spawn position
lookAt position
human rating: good / acceptable / bad
manual override needed: yes/no
```

---

## 16. Performance Targets

Runtime estimator target for Quest-class hardware:

```text
Small file, < 250k splats:      under 0.5 sec if already loaded
Medium file, 1-3M splats:       under 2 sec with 30k samples
Large file, > 10M splats:       batch/editor processing preferred
```

Implementation tips:

- Never allocate per splat in the main loop.
- Use deterministic random sampling rather than copying full arrays.
- Use double precision for matrix accumulation only.
- Use arrays/lists pooled if this runs repeatedly.
- Do not require GPU readback if the CPU-side loader already has attributes.

---

## 17. Minimal Implementation Checklist

Implement in this order:

1. Add `SplatSample`, `ISplatSampleProvider`, and metadata classes.
2. Write a provider adapter for the project's existing PLY/SPZ loader.
3. Add field normalization settings for scale, opacity, and quaternion order.
4. Implement sample filtering and normal inference.
5. Implement weighted line consensus solve.
6. Add robust reweighting.
7. Add confidence scoring.
8. Add simple floor/eye-height candidate selection.
9. Save/load sidecar JSON.
10. Apply spawn pose to XR rig on splat load.
11. Add manual override.
12. Add debug gizmos.
13. Add batch processor.
14. Add synthetic tests.

---

## 18. Pseudocode: End-to-End Estimation

```csharp
public static SplatSpawnMetadata Estimate(
    ISplatSampleProvider provider,
    SplatSpawnEstimatorSettings settings,
    string sourceAssetPath = null,
    string sourceAssetHash = null)
{
    var bounds = provider.Bounds;
    float sceneDiagonal = bounds.size.magnitude;

    var rawSamples = SampleProvider(provider, settings.maxSamples, settings.randomSeed);
    var scaleStats = ComputeScaleStats(rawSamples, settings);
    var lines = new List<WeightedLine>(rawSamples.Count);
    var yValues = new List<float>(rawSamples.Count);

    foreach (var sample in rawSamples)
    {
        if (!TryNormalize(sample, settings, out var p, out var q, out var s, out var alpha))
            continue;

        if (alpha < settings.minOpacity)
            continue;

        if (!PassesScaleFilters(s, scaleStats, settings))
            continue;

        if (!TryInferNormal(q, s, settings, out var n, out var flatnessWeight))
            continue;

        float w = alpha * flatnessWeight;
        if (w <= 0.0f)
            continue;

        lines.Add(new WeightedLine(p, n, w));
        yValues.Add(p.y);
    }

    if (lines.Count < settings.minAcceptedSamples)
        return BoundsFallback(provider, settings, sourceAssetPath, sourceAssetHash, "Not enough valid splat normals.");

    if (!SolveRobustLineConsensus(lines, bounds, settings, out var consensus, out var medianResidual))
        return BoundsFallback(provider, settings, sourceAssetPath, sourceAssetHash, "Line consensus solve failed.");

    float confidence = ComputeConfidence(consensus, bounds, sceneDiagonal, medianResidual, lines.Count, settings);

    Vector3 up = Vector3.up;
    float floorY = EstimateFloorY(yValues, bounds);
    Vector3 lookAt = consensus;
    lookAt.y = Mathf.Clamp(lookAt.y, floorY + 0.8f, floorY + 1.8f);

    Vector3 spawn = SelectBestSpawnCandidate(
        provider,
        bounds,
        consensus,
        lookAt,
        floorY,
        up,
        settings,
        out float candidateScore,
        out string candidateWarning);

    Quaternion rotation = Quaternion.LookRotation((lookAt - spawn).normalized, up);

    return new SplatSpawnMetadata
    {
        version = 1,
        method = "normal_line_consensus_v1",
        sourceAssetPath = sourceAssetPath,
        sourceAssetHash = sourceAssetHash,
        spawn = spawn,
        rotation = rotation,
        lookAt = lookAt,
        up = up,
        bounds = bounds,
        confidence = Mathf.Clamp01(confidence * candidateScore),
        medianResidual = medianResidual,
        sceneDiagonal = sceneDiagonal,
        totalSplats = provider.Count,
        sampledSplats = rawSamples.Count,
        acceptedSplats = lines.Count,
        warnings = string.IsNullOrEmpty(candidateWarning) ? Array.Empty<string>() : new [] { candidateWarning }
    };
}
```

---

## 19. Bounds Fallback

If normal consensus cannot run, use a conservative fallback.

```csharp
private static SplatSpawnMetadata BoundsFallback(
    ISplatSampleProvider provider,
    SplatSpawnEstimatorSettings settings,
    string sourceAssetPath,
    string sourceAssetHash,
    string warning)
{
    Bounds b = provider.Bounds;
    float floorY = b.min.y;
    Vector3 lookAt = b.center;
    lookAt.y = floorY + settings.defaultEyeHeightMeters * 0.9f;

    Vector3 spawn = b.center;
    spawn.y = floorY + settings.defaultEyeHeightMeters;
    spawn.z = b.center.z - b.size.z * 0.15f;

    Quaternion rotation = Quaternion.LookRotation((lookAt - spawn).normalized, Vector3.up);

    return new SplatSpawnMetadata
    {
        version = 1,
        method = "bounds_fallback_v1",
        sourceAssetPath = sourceAssetPath,
        sourceAssetHash = sourceAssetHash,
        spawn = spawn,
        rotation = rotation,
        lookAt = lookAt,
        up = Vector3.up,
        bounds = b,
        confidence = 0.2f,
        totalSplats = provider.Count,
        warnings = new [] { warning }
    };
}
```

---

## 20. References / Format Notes

These are implementation references, not runtime dependencies.

- PDAL SPZ reader/writer documentation describes SPZ as compressed Gaussian splat data with position, scale, rotation, opacity, color, and spherical harmonics fields.
- PDAL documents common 3DGS-style field names including `X`, `Y`, `Z`, `opacity`, `scale_0..2`, and `rot_0..3`, with quaternion order `W, X, Y, Z`.
- Khronos announced a release candidate glTF Gaussian Splatting extension in 2026, with splat attributes including position, orientation, scale, color, and opacity.
- PlayCanvas SplatTransform is a useful external reference for splat conversion, filtering, analysis, and collision generation workflows.

---

## 21. Acceptance Criteria

The implementation is acceptable when:

1. Loading a splat with no metadata produces a sidecar JSON file.
2. Reloading the same splat uses the sidecar instead of recomputing.
3. If the source file hash changes, metadata is regenerated or marked stale.
4. The estimator handles both `.ply` and `.spz` as long as the project loader exposes common splat attributes.
5. For a synthetic room, the consensus point lands near the room center.
6. For a synthetic sphere, the consensus point lands near the sphere center.
7. For missing rotation/scale, the system falls back gracefully.
8. Runtime never blocks the headset indefinitely on large assets.
9. A developer can manually save the current XR rig pose as the preferred spawn.
10. Debug mode can show the consensus point, spawn point, look direction, bounds, and optional normal lines.
