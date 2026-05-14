# Local & Remote Content Loading — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Enable voice-commanded loading of `.spz`/`.ply` splat files and `.jpg`/`.png` panoramas from local headset storage or any HTTP/HTTPS URL.

**Architecture:** Two new MonoBehaviour loaders (`LocalRemoteSplatLoader`, `LocalRemotePanoLoader`) handle byte acquisition and format-specific processing. A runtime PLY reader (`RuntimePlyReader`) ports the Editor-only PLY pipeline to run on-device. All three integrate with the existing voice intent dispatch chain via two new intent types (`LoadSplat`, `LoadPanorama`).

**Spec:** `docs/superpowers/specs/2026-04-08-local-remote-content-loading.md`

**Tech Stack:** Unity C#, `System.IO.File`, `UnityEngine.Networking.UnityWebRequest`, `Unity.Collections.NativeArray`, `GaussianSplatting.Runtime.RuntimeSplatProcessing`, `GaussianSplatting.Runtime.SPZFileReader`, `GaussianSplatting.Runtime.InputSplatData`, `WorldLabs.Runtime.Tools.RuntimeSplatFloorLoader`, `WorldLabs.Runtime.Tools.SplatFloorAnalyzer`

---

## File Map

| File | Status | Responsibility |
|---|---|---|
| `Assets/App/GaussianSplatting/Runtime/RuntimePlyReader.cs` | **NEW** | Runtime-safe PLY parser → `NativeArray<InputSplatData>` |
| `Assets/App/Scripts/Direct/RuntimeSplatFloorLoader.cs` | **MODIFY** | Add `LoadPlacedRuntimeWorldFromSplatsAsync` overload |
| `Assets/App/Command/SpeechIntent/Runtime/LocalRemotePanoLoader.cs` | **NEW** | Load image from local path or URL → `ThumbnailSkyboxController.Show` |
| `Assets/App/Command/SpeechIntent/Runtime/LocalRemoteSplatLoader.cs` | **NEW** | Load SPZ/PLY from local path or URL → floor-placed renderer |
| `Assets/App/Command/SpeechIntent/Runtime/VoiceIntentSchemas.cs` | **MODIFY** | Add `LoadSplat=14`, `LoadPanorama=15`, `content_path` field |
| `Assets/App/Command/SpeechIntent/Runtime/WorldActionDispatcher.cs` | **MODIFY** | Add loader fields + coroutine handlers |
| `Assets/App/Editor/SpeechIntentSceneSetup.cs` | **MODIFY** | Auto-wire both loaders |

---

## Task 1: Create RuntimePlyReader

**Files:**
- Create: `Assets/App/GaussianSplatting/Runtime/RuntimePlyReader.cs`

The Editor already has `PLYFileReader` + `GaussianFileReader` in `Packages/.../Editor/`. They have zero `UnityEditor.*` imports — they only use `System.IO`, `Unity.Collections`, `Unity.Jobs`, `Unity.Mathematics`, `UnityEngine`. The plan is to port them into a single runtime class.

- [ ] **Step 1: Verify there is no existing runtime PLY support**

Run in Unity Console or terminal:
```bash
grep -r "PLYFileReader\|GaussianFileReader" \
  Assets/App/GaussianSplatting/Runtime 2>/dev/null
```
Expected: no output (confirming this file doesn't exist yet).

- [ ] **Step 2: Create the directory if needed**

```bash
mkdir -p Assets/App/GaussianSplatting/Runtime
```

- [ ] **Step 3: Create RuntimePlyReader.cs**

```csharp
// Assets/App/GaussianSplatting/Runtime/RuntimePlyReader.cs
// Runtime-safe PLY reader: port of GaussianSplatting.Editor.Utils.GaussianFileReader
// + PLYFileReader. No UnityEditor.* dependencies.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using GaussianSplatting.Runtime;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Assertions;

namespace WorldLabs.Runtime.Tools
{
    /// <summary>
    /// Runtime-safe parser for Gaussian Splat PLY files.
    /// Produces <see cref="InputSplatData"/> arrays compatible with
    /// <see cref="GaussianSplatting.Runtime.RuntimeSplatProcessing.Process"/>.
    /// Caller is responsible for disposing the output NativeArray.
    /// </summary>
    public static class RuntimePlyReader
    {
        public enum ElementType { None, Float, Double, UChar, Int }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>Read PLY from a file path. Caller must dispose <paramref name="splats"/>.</summary>
        public static void ReadFromFile(string filePath, out NativeArray<InputSplatData> splats)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"PLY file not found: {filePath}");
            byte[] bytes = File.ReadAllBytes(filePath);
            ReadFromBytes(bytes, filePath, out splats);
        }

        /// <summary>Read PLY from raw bytes. Caller must dispose <paramref name="splats"/>.</summary>
        public static void ReadFromBytes(byte[] plyBytes, out NativeArray<InputSplatData> splats)
            => ReadFromBytes(plyBytes, "<bytes>", out splats);

        // ── Internal ──────────────────────────────────────────────────────────

        static void ReadFromBytes(byte[] plyBytes, string label, out NativeArray<InputSplatData> splats)
        {
            using var ms = new MemoryStream(plyBytes);
            ReadHeader(ms, label, out int vertexCount, out int vertexStride,
                out List<(string name, ElementType type)> attributes);

            string attrError = CheckRequiredAttributes(attributes);
            if (!string.IsNullOrEmpty(attrError))
                throw new IOException($"PLY '{label}' is not a Gaussian Splat file. Missing: {attrError}");

            // Read raw vertex bytes from current stream position
            NativeArray<byte> rawData = new NativeArray<byte>(vertexCount * vertexStride, Allocator.Persistent);
            unsafe
            {
                void* ptr = rawData.GetUnsafePtr();
                byte[] buf = new byte[vertexCount * vertexStride];
                int readBytes = ms.Read(buf, 0, buf.Length);
                if (readBytes != buf.Length)
                    throw new IOException($"PLY '{label}': expected {buf.Length} data bytes, got {readBytes}");
                fixed (byte* src = buf)
                    Buffer.MemoryCopy(src, ptr, buf.Length, buf.Length);
            }

            try
            {
                splats = PLYDataToSplats(rawData, vertexCount, vertexStride, attributes);
                ReorderSHs(vertexCount, splats);
                LinearizeData(splats);
            }
            finally
            {
                rawData.Dispose();
            }
        }

        static void ReadHeader(Stream stream, string label,
            out int vertexCount, out int vertexStride,
            out List<(string, ElementType)> attributes)
        {
            vertexCount = 0;
            vertexStride = 0;
            attributes = new List<(string, ElementType)>();
            const int kMaxLines = 9000;
            for (int i = 0; i < kMaxLines; i++)
            {
                string line = ReadLine(stream);
                if (line == "end_header" || line.Length == 0) break;
                var tokens = line.Split(' ');
                if (tokens.Length == 3 && tokens[0] == "element" && tokens[1] == "vertex")
                    vertexCount = int.Parse(tokens[2]);
                if (tokens.Length == 3 && tokens[0] == "property")
                {
                    ElementType type = tokens[1] switch
                    {
                        "float"  => ElementType.Float,
                        "double" => ElementType.Double,
                        "uchar"  => ElementType.UChar,
                        "int"    => ElementType.Int,
                        _        => ElementType.None
                    };
                    vertexStride += TypeToSize(type);
                    attributes.Add((tokens[2], type));
                }
            }
            Debug.Log($"[RuntimePlyReader] {label}: {vertexCount} vertices, stride={vertexStride}, attrs={attributes.Count}");
        }

        static string CheckRequiredAttributes(List<(string, ElementType)> attributes)
        {
            string[] required = { "x", "y", "z", "f_dc_0", "f_dc_1", "f_dc_2",
                                   "opacity", "scale_0", "scale_1", "scale_2",
                                   "rot_0", "rot_1", "rot_2", "rot_3" };
            var missing = required
                .Where(r => !attributes.Contains((r, ElementType.Float)))
                .ToList();
            return missing.Count == 0 ? null : string.Join(", ", missing);
        }

        static unsafe NativeArray<InputSplatData> PLYDataToSplats(
            NativeArray<byte> input, int count, int stride,
            List<(string name, ElementType type)> attributes)
        {
            // Build per-attribute byte offsets
            NativeArray<int> fileAttrOffsets = new NativeArray<int>(attributes.Count, Allocator.Temp);
            int offset = 0;
            for (int ai = 0; ai < attributes.Count; ai++)
            {
                fileAttrOffsets[ai] = offset;
                offset += TypeToSize(attributes[ai].type);
            }

            // Canonical attribute order matches InputSplatData field layout (all float32)
            string[] splatAttrs =
            {
                "x","y","z","nx","ny","nz",
                "f_dc_0","f_dc_1","f_dc_2",
                "f_rest_0","f_rest_1","f_rest_2","f_rest_3","f_rest_4",
                "f_rest_5","f_rest_6","f_rest_7","f_rest_8","f_rest_9",
                "f_rest_10","f_rest_11","f_rest_12","f_rest_13","f_rest_14",
                "f_rest_15","f_rest_16","f_rest_17","f_rest_18","f_rest_19",
                "f_rest_20","f_rest_21","f_rest_22","f_rest_23","f_rest_24",
                "f_rest_25","f_rest_26","f_rest_27","f_rest_28","f_rest_29",
                "f_rest_30","f_rest_31","f_rest_32","f_rest_33","f_rest_34",
                "f_rest_35","f_rest_36","f_rest_37","f_rest_38","f_rest_39",
                "f_rest_40","f_rest_41","f_rest_42","f_rest_43","f_rest_44",
                "opacity","scale_0","scale_1","scale_2","rot_0","rot_1","rot_2","rot_3",
            };
            Assert.AreEqual(UnsafeUtility.SizeOf<InputSplatData>() / 4, splatAttrs.Length);

            NativeArray<int> srcOffsets = new NativeArray<int>(splatAttrs.Length, Allocator.Temp);
            for (int ai = 0; ai < splatAttrs.Length; ai++)
            {
                int attrIndex = attributes.IndexOf((splatAttrs[ai], ElementType.Float));
                srcOffsets[ai] = attrIndex >= 0 ? fileAttrOffsets[attrIndex] : -1;
            }

            NativeArray<InputSplatData> dst = new NativeArray<InputSplatData>(count, Allocator.Persistent);
            ReorderPLYData(count, (byte*)input.GetUnsafeReadOnlyPtr(), stride,
                           (byte*)dst.GetUnsafePtr(), UnsafeUtility.SizeOf<InputSplatData>(),
                           (int*)srcOffsets.GetUnsafeReadOnlyPtr());

            srcOffsets.Dispose();
            fileAttrOffsets.Dispose();
            return dst;
        }

        [BurstCompile]
        static unsafe void ReorderPLYData(
            int splatCount, byte* src, int srcStride,
            byte* dst, int dstStride, int* srcOffsets)
        {
            for (int i = 0; i < splatCount; i++)
            {
                for (int attr = 0; attr < dstStride / 4; attr++)
                {
                    if (srcOffsets[attr] >= 0)
                        *(int*)(dst + attr * 4) = *(int*)(src + srcOffsets[attr]);
                }
                src += srcStride;
                dst += dstStride;
            }
        }

        static unsafe void ReorderSHs(int splatCount, NativeArray<InputSplatData> splats)
        {
            float* data = (float*)splats.GetUnsafePtr();
            int splatStride = UnsafeUtility.SizeOf<InputSplatData>() / 4;
            int shStartOffset = 9, shCount = 15;
            float* tmp = stackalloc float[shCount * 3];
            int idx = shStartOffset;
            for (int i = 0; i < splatCount; i++)
            {
                for (int j = 0; j < shCount; j++)
                {
                    tmp[j * 3 + 0] = data[idx + j];
                    tmp[j * 3 + 1] = data[idx + j + shCount];
                    tmp[j * 3 + 2] = data[idx + j + shCount * 2];
                }
                for (int j = 0; j < shCount * 3; j++)
                    data[idx + j] = tmp[j];
                idx += splatStride;
            }
        }

        [BurstCompile]
        struct LinearizeDataJob : IJobParallelFor
        {
            public NativeArray<InputSplatData> splatData;
            public void Execute(int index)
            {
                var s = splatData[index];
                var q  = s.rot;
                var qq = GaussianUtils.NormalizeSwizzleRotation(new float4(q.x, q.y, q.z, q.w));
                qq = GaussianUtils.PackSmallest3Rotation(qq);
                s.rot     = new Quaternion(qq.x, qq.y, qq.z, qq.w);
                s.scale   = GaussianUtils.LinearScale(s.scale);
                s.dc0     = GaussianUtils.SH0ToColor(s.dc0);
                s.opacity = GaussianUtils.Sigmoid(s.opacity);
                splatData[index] = s;
            }
        }

        static void LinearizeData(NativeArray<InputSplatData> splats)
        {
            var job = new LinearizeDataJob { splatData = splats };
            job.Schedule(splats.Length, 4096).Complete();
        }

        static int TypeToSize(ElementType t) => t switch
        {
            ElementType.Float  => 4,
            ElementType.Double => 8,
            ElementType.UChar  => 1,
            ElementType.Int    => 4,
            _                  => 0
        };

        static string ReadLine(Stream stream)
        {
            var bytes = new List<byte>();
            while (true)
            {
                int b = stream.ReadByte();
                if (b == -1 || b == '\n') break;
                bytes.Add((byte)b);
            }
            if (bytes.Count > 0 && bytes[^1] == '\r') bytes.RemoveAt(bytes.Count - 1);
            return Encoding.UTF8.GetString(bytes.ToArray());
        }
    }
}
```

- [ ] **Step 4: Verify it compiles**

Open Unity. The Console should show no errors related to `RuntimePlyReader`. If `GaussianUtils` is not found, check its namespace — it's in `GaussianSplatting.Runtime`. Add `using GaussianSplatting.Runtime;` if not already present.

- [ ] **Step 5: Commit**

```bash
git add Assets/App/GaussianSplatting/Runtime/RuntimePlyReader.cs \
        Assets/App/GaussianSplatting/Runtime/RuntimePlyReader.cs.meta
git commit -m "feat: add RuntimePlyReader — runtime-safe PLY parser for Gaussian Splats"
```

---

## Task 2: Extend RuntimeSplatFloorLoader with Splat-Input Overload

**Files:**
- Modify: `Assets/App/Scripts/Direct/RuntimeSplatFloorLoader.cs`

`LoadPlacedRuntimeWorldAsync` currently only accepts `byte[] spzBytes`. We need a second overload that accepts already-parsed `NativeArray<InputSplatData>`, enabling the PLY path to share the same floor-placement logic without re-parsing.

- [ ] **Step 1: Read the existing file** (already done in exploration — lines 129-200 contain the async method)

- [ ] **Step 2: Add the new overload after the existing `LoadPlacedRuntimeWorldAsync` method (after line 201)**

Insert the following method into `RuntimeSplatFloorLoader.cs`, after the closing `}` of `LoadPlacedRuntimeWorldAsync`:

```csharp
/// <summary>
/// Async variant that accepts already-parsed <see cref="InputSplatData"/> instead of raw SPZ bytes.
/// Use this for PLY files: parse with <see cref="RuntimePlyReader"/>, then call this method.
/// <para><paramref name="inputSplats"/> is disposed by this method after processing.</para>
/// </summary>
public Task<LoadResult> LoadPlacedRuntimeWorldFromSplatsAsync(
    NativeArray<InputSplatData> inputSplats,
    string worldId = null,
    string worldName = null,
    string gameObjectName = null)
{
    if (!inputSplats.IsCreated || inputSplats.Length == 0)
        throw new ArgumentException("inputSplats is empty or not created.", nameof(inputSplats));

    EnsureShaders();

    Quaternion localRotation = applyWorldLabsDefaultRotation
        ? Quaternion.Euler(-180f, 0f, 0f)
        : Quaternion.identity;

    SplatFloorAnalysisOptions opts = floorAnalysis ?? new SplatFloorAnalysisOptions();
    opts.positionTransform = Matrix4x4.Rotate(localRotation);

    var (posFormat, scaleFormat, colorFormat, shFormat) = GetFormats(quality);

    TaskScheduler mainThread = TaskScheduler.FromCurrentSynchronizationContext();

    return Task.Run(() =>
    {
        try
        {
            SplatFloorEstimate floorEstimate = SplatFloorAnalyzer.AnalyzeSplats(inputSplats, opts);

            RuntimeSplatData data = RuntimeSplatProcessing.Process(
                inputSplats, posFormat, scaleFormat, colorFormat, shFormat);

            data.worldId   = worldId;
            data.worldName = worldName;

            return (floorEstimate, data);
        }
        finally
        {
            inputSplats.Dispose();
        }
    }).ContinueWith(t =>
    {
        if (t.IsFaulted)
            throw t.Exception!.GetBaseException();

        var (floorEstimate, data) = t.Result;

        string goName = !string.IsNullOrWhiteSpace(gameObjectName)
            ? gameObjectName
            : (!string.IsNullOrWhiteSpace(worldName) ? $"World_{worldName}" : "World");

        var go = new GameObject(goName);
        go.transform.SetParent(worldParent, false);
        go.transform.localRotation = localRotation;
        go.transform.localPosition = autoPlaceAtOrigin && floorEstimate != null && floorEstimate.success
            ? floorEstimate.recommendedLocalPosition
            : Vector3.zero;

        var renderer = go.AddComponent<GaussianSplatRenderer>();
        AssignShaders(renderer);
        renderer.LoadFromRuntimeData(data);

        return new LoadResult
        {
            gameObject    = go,
            renderer      = renderer,
            runtimeData   = data,
            floorEstimate = floorEstimate,
        };
    }, mainThread);
}
```

- [ ] **Step 3: Add `using GaussianSplatting.Runtime;` if not already present** at the top of `RuntimeSplatFloorLoader.cs` (it uses `NativeArray<InputSplatData>` and `RuntimeSplatProcessing.Process`).

- [ ] **Step 4: Verify it compiles** — open Unity, check Console for errors.

- [ ] **Step 5: Commit**

```bash
git add Assets/App/Scripts/Direct/RuntimeSplatFloorLoader.cs
git commit -m "feat: add LoadPlacedRuntimeWorldFromSplatsAsync overload to RuntimeSplatFloorLoader"
```

---

## Task 3: Create LocalRemotePanoLoader

**Files:**
- Create: `Assets/App/Command/SpeechIntent/Runtime/LocalRemotePanoLoader.cs`

- [ ] **Step 1: Create the file**

```csharp
// Assets/App/Command/SpeechIntent/Runtime/LocalRemotePanoLoader.cs

using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace SpeechIntent
{
    /// <summary>
    /// Loads an equirectangular panoramic image (.jpg, .png) from a local file path
    /// or HTTP/HTTPS URL and displays it via <see cref="ThumbnailSkyboxController"/>.
    /// </summary>
    public class LocalRemotePanoLoader : MonoBehaviour
    {
        [Header("References")]
        public ThumbnailSkyboxController thumbnailSkybox;
        public ViewModeController        viewModeController;

        [Header("Local Storage")]
        [Tooltip("Base directory for local files. Partial filenames are resolved against this path.")]
        public string localBasePath = "";

        [Header("Events")]
        public StringEvent onLoadStarted;
        public StringEvent onLoadFailed;

        void Awake()
        {
            if (string.IsNullOrWhiteSpace(localBasePath))
                localBasePath = Path.Combine(Application.persistentDataPath, "WorldContent");
        }

        /// <summary>
        /// Load a panoramic image from a local path or URL and display it.
        /// Caller can <c>yield return StartCoroutine(LoadCoroutine(...))</c> or fire-and-forget.
        /// </summary>
        public void LoadAsync(string pathOrUrl)
        {
            if (string.IsNullOrWhiteSpace(pathOrUrl))
            {
                Debug.LogWarning("[LocalRemotePanoLoader] pathOrUrl is empty.");
                onLoadFailed?.Invoke("No path or URL provided.");
                return;
            }
            StartCoroutine(LoadCoroutine(pathOrUrl));
        }

        System.Collections.IEnumerator LoadCoroutine(string pathOrUrl)
        {
            onLoadStarted?.Invoke(pathOrUrl);

            bool isUrl = pathOrUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                      || pathOrUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

            byte[] imageBytes = null;
            string error = null;

            if (isUrl)
            {
                using UnityWebRequest req = UnityWebRequest.Get(pathOrUrl);
                yield return req.SendWebRequest();

                if (req.result != UnityWebRequest.Result.Success)
                    error = $"Download failed: {req.error}";
                else
                    imageBytes = req.downloadHandler.data;
            }
            else
            {
                string resolved = ResolveLocalPath(pathOrUrl);
                if (!File.Exists(resolved))
                {
                    error = $"File not found: {resolved}";
                }
                else
                {
                    // Read on background thread to avoid hitching main thread
                    Task<byte[]> readTask = Task.Run(() => File.ReadAllBytes(resolved));
                    while (!readTask.IsCompleted) yield return null;
                    if (readTask.IsFaulted)
                        error = $"Read failed: {readTask.Exception?.GetBaseException().Message}";
                    else
                        imageBytes = readTask.Result;
                }
            }

            if (error != null)
            {
                Debug.LogError($"[LocalRemotePanoLoader] {error}");
                onLoadFailed?.Invoke(error);
                yield break;
            }

            Texture2D tex = new Texture2D(2, 2, TextureFormat.RGB24, false);
            if (!tex.LoadImage(imageBytes))
            {
                Destroy(tex);
                string msg = $"Failed to decode image from '{pathOrUrl}'.";
                Debug.LogError($"[LocalRemotePanoLoader] {msg}");
                onLoadFailed?.Invoke(msg);
                yield break;
            }

            if (thumbnailSkybox == null)
            {
                Destroy(tex);
                Debug.LogError("[LocalRemotePanoLoader] thumbnailSkybox is not assigned.");
                onLoadFailed?.Invoke("ThumbnailSkyboxController not assigned.");
                yield break;
            }

            // ThumbnailSkyboxController.Show() takes ownership of the texture.
            thumbnailSkybox.Show(tex);
            viewModeController?.RequestPanoView();

            Debug.Log($"[LocalRemotePanoLoader] Panorama loaded from '{pathOrUrl}'.");
        }

        string ResolveLocalPath(string pathOrUrl)
        {
            if (Path.IsPathRooted(pathOrUrl))
                return pathOrUrl;
            return Path.Combine(localBasePath, pathOrUrl);
        }
    }
}
```

- [ ] **Step 2: Verify it compiles** — open Unity, check Console.

- [ ] **Step 3: Commit**

```bash
git add Assets/App/Command/SpeechIntent/Runtime/LocalRemotePanoLoader.cs \
        Assets/App/Command/SpeechIntent/Runtime/LocalRemotePanoLoader.cs.meta
git commit -m "feat: add LocalRemotePanoLoader — load pano from local file or URL"
```

---

## Task 4: Create LocalRemoteSplatLoader

**Files:**
- Create: `Assets/App/Command/SpeechIntent/Runtime/LocalRemoteSplatLoader.cs`

- [ ] **Step 1: Create the file**

```csharp
// Assets/App/Command/SpeechIntent/Runtime/LocalRemoteSplatLoader.cs

using System;
using System.IO;
using System.Threading.Tasks;
using GaussianSplatting.Runtime;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Networking;
using WorldLabs.Runtime;
using WorldLabs.Runtime.Tools;

namespace SpeechIntent
{
    /// <summary>
    /// Loads a Gaussian Splat file (.spz or .ply) from a local path or HTTP/HTTPS URL,
    /// applies floor placement, and registers it with <see cref="WorldLabsWorldManager"/>.
    /// </summary>
    public class LocalRemoteSplatLoader : MonoBehaviour
    {
        [Header("References")]
        public WorldLabsWorldManager   worldManager;
        public RuntimeSplatFloorLoader floorLoader;

        [Header("Local Storage")]
        [Tooltip("Base directory for local files. Partial filenames are resolved against this path.")]
        public string localBasePath = "";

        [Header("Events")]
        public StringEvent onLoadStarted;
        public StringEvent onLoadFailed;

        void Awake()
        {
            if (string.IsNullOrWhiteSpace(localBasePath))
                localBasePath = Path.Combine(Application.persistentDataPath, "WorldContent");
        }

        /// <summary>
        /// Load an SPZ or PLY splat from a local path or URL.
        /// </summary>
        public void LoadAsync(string pathOrUrl, string displayName = null)
        {
            if (string.IsNullOrWhiteSpace(pathOrUrl))
            {
                Debug.LogWarning("[LocalRemoteSplatLoader] pathOrUrl is empty.");
                onLoadFailed?.Invoke("No path or URL provided.");
                return;
            }
            StartCoroutine(LoadCoroutine(pathOrUrl, displayName));
        }

        System.Collections.IEnumerator LoadCoroutine(string pathOrUrl, string displayName)
        {
            string resolved = ResolveLocalPath(pathOrUrl);
            string worldId  = "local_" + Path.GetFileNameWithoutExtension(resolved)
                                       + "_" + DateTime.UtcNow.Ticks;
            string worldName = !string.IsNullOrWhiteSpace(displayName)
                ? displayName
                : Path.GetFileNameWithoutExtension(resolved);

            onLoadStarted?.Invoke(worldId);

            bool isUrl = pathOrUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                      || pathOrUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

            byte[] fileBytes = null;
            string error = null;

            // ── Acquire bytes ────────────────────────────────────────────────
            if (isUrl)
            {
                using UnityWebRequest req = UnityWebRequest.Get(pathOrUrl);
                yield return req.SendWebRequest();

                if (req.result != UnityWebRequest.Result.Success)
                    error = $"Download failed: {req.error}";
                else
                    fileBytes = req.downloadHandler.data;
            }
            else
            {
                if (!File.Exists(resolved))
                {
                    error = $"File not found: {resolved}";
                }
                else
                {
                    Task<byte[]> readTask = Task.Run(() => File.ReadAllBytes(resolved));
                    while (!readTask.IsCompleted) yield return null;
                    if (readTask.IsFaulted)
                        error = $"Read failed: {readTask.Exception?.GetBaseException().Message}";
                    else
                        fileBytes = readTask.Result;
                }
            }

            if (error != null)
            {
                Debug.LogError($"[LocalRemoteSplatLoader] {error}");
                onLoadFailed?.Invoke(error);
                yield break;
            }

            // ── Detect format ────────────────────────────────────────────────
            string ext = Path.GetExtension(isUrl ? pathOrUrl : resolved)
                             .ToLowerInvariant();

            if (ext != ".spz" && ext != ".ply")
            {
                string msg = $"Unsupported format '{ext}'. Expected .spz or .ply.";
                Debug.LogError($"[LocalRemoteSplatLoader] {msg}");
                onLoadFailed?.Invoke(msg);
                yield break;
            }

            if (floorLoader == null)
            {
                onLoadFailed?.Invoke("RuntimeSplatFloorLoader not assigned.");
                yield break;
            }

            // ── Load (format-specific) ───────────────────────────────────────
            worldManager?.NotifyWorldLoadStarted(worldId);

            Task<RuntimeSplatFloorLoader.LoadResult> loadTask;

            if (ext == ".spz")
            {
                loadTask = floorLoader.LoadPlacedRuntimeWorldAsync(
                    fileBytes, worldId, worldName, gameObjectName: worldName);
            }
            else // .ply
            {
                Task<RuntimeSplatFloorLoader.LoadResult> plyTask = null;
                Task parseTask = Task.Run(() =>
                {
                    RuntimePlyReader.ReadFromBytes(fileBytes, out NativeArray<InputSplatData> splats);
                    // Kick off the continuation from the background thread is fine;
                    // LoadPlacedRuntimeWorldFromSplatsAsync uses ContinueWith(mainThread).
                    plyTask = floorLoader.LoadPlacedRuntimeWorldFromSplatsAsync(
                        splats, worldId, worldName, gameObjectName: worldName);
                });
                while (!parseTask.IsCompleted) yield return null;
                if (parseTask.IsFaulted)
                {
                    string msg = $"PLY parse failed: {parseTask.Exception?.GetBaseException().Message}";
                    Debug.LogError($"[LocalRemoteSplatLoader] {msg}");
                    worldManager?.NotifyWorldLoadFailed(worldId, msg);
                    onLoadFailed?.Invoke(msg);
                    yield break;
                }
                loadTask = plyTask;
            }

            while (!loadTask.IsCompleted) yield return null;

            if (loadTask.IsFaulted)
            {
                string msg = $"Load failed: {loadTask.Exception?.GetBaseException().Message}";
                Debug.LogError($"[LocalRemoteSplatLoader] {msg}");
                worldManager?.NotifyWorldLoadFailed(worldId, msg);
                onLoadFailed?.Invoke(msg);
                yield break;
            }

            RuntimeSplatFloorLoader.LoadResult result = loadTask.Result;
            worldManager?.RegisterExternalWorld(worldId, result.renderer);

            Debug.Log($"[LocalRemoteSplatLoader] Loaded '{worldName}' ({ext}) as worldId='{worldId}'.");
        }

        string ResolveLocalPath(string pathOrUrl)
        {
            if (pathOrUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
             || pathOrUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return pathOrUrl;  // URL — no resolution
            if (Path.IsPathRooted(pathOrUrl))
                return pathOrUrl;
            return Path.Combine(localBasePath, pathOrUrl);
        }
    }
}
```

- [ ] **Step 2: Verify it compiles** — open Unity, check Console.

- [ ] **Step 3: Commit**

```bash
git add Assets/App/Command/SpeechIntent/Runtime/LocalRemoteSplatLoader.cs \
        Assets/App/Command/SpeechIntent/Runtime/LocalRemoteSplatLoader.cs.meta
git commit -m "feat: add LocalRemoteSplatLoader — load SPZ/PLY splat from local file or URL"
```

---

## Task 5: Extend VoiceIntentSchemas

**Files:**
- Modify: `Assets/App/Command/SpeechIntent/Runtime/VoiceIntentSchemas.cs`

- [ ] **Step 1: Add new intent types**

In `VoiceIntentSchemas.cs`, find the `VoiceIntentType` enum (currently ends at `ResetTransform = 13`). Add:

```csharp
    LoadSplat     = 14,  // load a local/remote .spz or .ply file
    LoadPanorama  = 15,  // load a local/remote panoramic image
```

- [ ] **Step 2: Add `content_path` field to `VoiceIntentCommand`**

In `VoiceIntentCommand`, find the `[Header("Local/Remote Content")]` section — if it doesn't exist, add it after the `[Header("Transform Commands")]` block:

```csharp
[Header("Local/Remote Content")]
[Tooltip("File name, relative path, or full URL for LoadSplat and LoadPanorama intents.")]
public string content_path = "";
```

- [ ] **Step 3: Verify it compiles**

- [ ] **Step 4: Commit**

```bash
git add Assets/App/Command/SpeechIntent/Runtime/VoiceIntentSchemas.cs
git commit -m "feat: add LoadSplat/LoadPanorama intent types and content_path field"
```

---

## Task 6: Add Handlers in WorldActionDispatcher

**Files:**
- Modify: `Assets/App/Command/SpeechIntent/Runtime/WorldActionDispatcher.cs`

- [ ] **Step 1: Read the current WorldActionDispatcher.cs** — locate the public fields section and the `Execute` switch statement.

- [ ] **Step 2: Add public fields for the two new loaders**

After the existing `public PlayerOriginController playerOriginController;` field, add:

```csharp
public LocalRemoteSplatLoader splatLoader;
public LocalRemotePanoLoader  panoLoader;
```

- [ ] **Step 3: Add cases to the `Execute` switch statement**

In the `Execute` method's `switch (command.intent)` block, add after the existing cases:

```csharp
case VoiceIntentType.LoadSplat:
    HandleLoadSplat(command);
    break;

case VoiceIntentType.LoadPanorama:
    HandleLoadPanorama(command);
    break;
```

- [ ] **Step 4: Add handler methods**

At the bottom of the class (before the closing `}`), add:

```csharp
private void HandleLoadSplat(VoiceIntentCommand command)
{
    if (splatLoader == null)
    {
        Debug.LogWarning("[WorldActionDispatcher] HandleLoadSplat: splatLoader not assigned.");
        return;
    }
    if (string.IsNullOrWhiteSpace(command.content_path))
    {
        Debug.LogWarning("[WorldActionDispatcher] HandleLoadSplat: content_path is empty.");
        return;
    }
    Debug.Log($"[WorldActionDispatcher] Loading splat from '{command.content_path}'.");
    splatLoader.LoadAsync(command.content_path);
}

private void HandleLoadPanorama(VoiceIntentCommand command)
{
    if (panoLoader == null)
    {
        Debug.LogWarning("[WorldActionDispatcher] HandleLoadPanorama: panoLoader not assigned.");
        return;
    }
    if (string.IsNullOrWhiteSpace(command.content_path))
    {
        Debug.LogWarning("[WorldActionDispatcher] HandleLoadPanorama: content_path is empty.");
        return;
    }
    Debug.Log($"[WorldActionDispatcher] Loading panorama from '{command.content_path}'.");
    panoLoader.LoadAsync(command.content_path);
}
```

- [ ] **Step 5: Verify it compiles**

- [ ] **Step 6: Commit**

```bash
git add Assets/App/Command/SpeechIntent/Runtime/WorldActionDispatcher.cs
git commit -m "feat: add LoadSplat/LoadPanorama handlers to WorldActionDispatcher"
```

---

## Task 7: Update OpenAiSpeechIntentService JSON Schema

**Files:**
- Modify: `Assets/App/Command/SpeechIntent/Runtime/OpenAiSpeechIntentService.cs`

The service builds a JSON schema sent to the AI. The new intents and `content_path` field must be added to it so the AI knows to populate them.

- [ ] **Step 1: Open `OpenAiSpeechIntentService.cs` and find `BuildJsonSchema()`** (the method that constructs the JSON schema string sent to the model). Look for the intent enum list and the properties object.

- [ ] **Step 2: Add `"LoadSplat"` and `"LoadPanorama"` to the intent enum array**

Find the line containing `"ResetTransform"` in the intent enum list. Add after it:
```json
"LoadSplat", "LoadPanorama"
```

- [ ] **Step 3: Add `content_path` to the properties object**

Find where other string properties like `"world_prompt"` are defined. Add:
```json
"content_path": {
  "type": "string",
  "description": "File name, relative path, or full URL for LoadSplat or LoadPanorama."
}
```

- [ ] **Step 4: Add `content_path` to the required array for LoadSplat/LoadPanorama**

If the schema uses a `required` array, ensure `content_path` is included alongside other always-present fields (like `intent`, `should_execute`, `transcript`). If required fields are not intent-conditional, just add it to the top-level required array.

- [ ] **Step 5: Verify it compiles**

- [ ] **Step 6: Commit**

```bash
git add Assets/App/Command/SpeechIntent/Runtime/OpenAiSpeechIntentService.cs
git commit -m "feat: add LoadSplat/LoadPanorama and content_path to JSON schema"
```

---

## Task 8: Wire in SpeechIntentSceneSetup

**Files:**
- Modify: `Assets/App/Editor/SpeechIntentSceneSetup.cs`

- [ ] **Step 1: Add `GetOrAdd` calls for both new loaders** in `SetupSpeechIntent()`, after the `PlayerOriginController` line:

```csharp
LocalRemoteSplatLoader splatLoader = GetOrAdd<LocalRemoteSplatLoader>(speechRoot);
LocalRemotePanoLoader  panoLoader  = GetOrAdd<LocalRemotePanoLoader>(speechRoot);
```

- [ ] **Step 2: Add both to the `Undo.RecordObjects` array**

Find the `Undo.RecordObjects(new Object[] { ... }, ...)` call and add `splatLoader` and `panoLoader` to the array.

- [ ] **Step 3: Wire the loaders**

After the `dispatcher.playerOriginController = playerOrigin;` line, add:

```csharp
dispatcher.splatLoader = splatLoader;
dispatcher.panoLoader  = panoLoader;

Undo.RecordObject(splatLoader, "Wire LocalRemoteSplatLoader");
splatLoader.worldManager = worldManager;
// floorLoader lives under Systems — find or get it
RuntimeSplatFloorLoader floorLoader =
    systems.GetComponentInChildren<RuntimeSplatFloorLoader>(true);
if (floorLoader != null)
    splatLoader.floorLoader = floorLoader;
else
    Debug.LogWarning("[SpeechIntentSceneSetup] RuntimeSplatFloorLoader not found under Systems. " +
                     "Assign splatLoader.floorLoader manually.");

Undo.RecordObject(panoLoader, "Wire LocalRemotePanoLoader");
panoLoader.thumbnailSkybox    = systems.GetComponentInChildren<ThumbnailSkyboxController>(true);
panoLoader.viewModeController = viewMode;
```

- [ ] **Step 4: Verify it compiles**

- [ ] **Step 5: Run the menu item** — **Holodeck > Setup SpeechIntent** — confirm no new warnings in Console.

- [ ] **Step 6: Commit**

```bash
git add Assets/App/Editor/SpeechIntentSceneSetup.cs
git commit -m "feat: auto-wire LocalRemoteSplatLoader and LocalRemotePanoLoader in scene setup"
```

---

## Task 9: Update Routing Hints in OpenAiSpeechIntentConfig

**Files:**
- Modify: `Assets/App/Command/SpeechIntent/OpenAiSpeechIntentConfig.asset` (via Inspector)

The `additionalDeveloperInstructions` field in the config asset holds domain-specific routing hints. Open the asset in the Inspector and append the following lines to the existing hints:

```
- LoadPanorama: user wants to show a local or remote panoramic image (.jpg or .png). Extract the filename, path, or full URL into content_path. Use for "load panorama from", "show pano", "open the panorama image". Example: "show me mountains.jpg" → intent=LoadPanorama, content_path="mountains.jpg". Example: "load panorama from https://cdn.example.com/pano.jpg" → intent=LoadPanorama, content_path="https://cdn.example.com/pano.jpg".
- LoadSplat: user wants to load a 3D Gaussian Splat file (.spz or .ply) from local storage or a URL. Extract the filename, path, or full URL into content_path. Use for "load the splat", "open the ply file", "load scene from". Example: "load landscapes.spz" → intent=LoadSplat, content_path="landscapes.spz". Example: "load splat from https://example.com/scene.spz" → intent=LoadSplat, content_path="https://example.com/scene.spz".
```

- [ ] **Step 1: Open `OpenAiSpeechIntentConfig` in the Inspector** (it's at `Assets/App/Command/SpeechIntent/OpenAiSpeechIntentConfig.asset`)

- [ ] **Step 2: Append the two new routing hints** to the `Additional Developer Instructions` text field.

- [ ] **Step 3: Save the asset** (Ctrl+S)

- [ ] **Step 4: Commit**

```bash
git add Assets/App/Command/SpeechIntent/OpenAiSpeechIntentConfig.asset
git commit -m "feat: add LoadSplat/LoadPanorama routing hints to OpenAiSpeechIntentConfig"
```

---

## Task 10: End-to-End Verification

- [ ] **Step 1: Deploy a test file to the headset**

```bash
# Create test content
echo "test" > /tmp/test.txt  # just to confirm adb works
adb push /path/to/a/real.spz \
    /sdcard/Android/data/<packageName>/files/WorldContent/test.spz
```

- [ ] **Step 2: Enter Play mode in the Editor (or build to headset)**

- [ ] **Step 3: Test panorama loading**
  - Say: **"load panorama from [filename].jpg"** (put a test JPG in `WorldContent/` first)
  - Expected: panorama appears on the skybox sphere
  - Check Console for `[LocalRemotePanoLoader] Panorama loaded from...`

- [ ] **Step 4: Test SPZ loading**
  - Say: **"load splat test.spz"**
  - Expected: splat world appears, floor aligned to Y=0
  - Check Console for `[LocalRemoteSplatLoader] Loaded 'test' (.spz)...`

- [ ] **Step 5: Test PLY loading** (if a `.ply` file is available)
  - Say: **"load the ply file test.ply"**
  - Expected: splat world appears
  - Check Console for `[LocalRemoteSplatLoader] Loaded 'test' (.ply)...`

- [ ] **Step 6: Test URL loading**
  - Say: **"load panorama from https://upload.wikimedia.org/wikipedia/commons/thumb/9/9e/Milky_Way_Arch.jpg/1280px-Milky_Way_Arch.jpg"**
  - Expected: panorama appears

- [ ] **Step 7: Test error path**
  - Say: **"load splat from nonexistent.spz"**
  - Expected: `onLoadFailed` fires, Console shows `File not found:...`

- [ ] **Step 8: Commit final scene if any Inspector changes were made**

```bash
git add -A
git commit -m "feat: complete local/remote content loading — SPZ, PLY, panorama from file and URL"
```

---

## Self-Review Checklist

- [x] **Spec coverage:** RuntimePlyReader (Task 1) ✓, SPZ path (Task 4) ✓, PLY path (Tasks 1+2+4) ✓, pano local (Task 3) ✓, pano URL (Task 3) ✓, voice integration (Tasks 5+6+7+9) ✓, scene setup (Task 8) ✓
- [x] **No placeholders** — all code is complete
- [x] **Type consistency** — `NativeArray<InputSplatData>` used consistently in Tasks 1, 2, 4; `RuntimeSplatFloorLoader.LoadResult` used in Tasks 2 and 4; `StringEvent` used in Tasks 3 and 4 (matches existing type from VoiceIntentSchemas)
- [x] **`content_path` field** added in Task 5, read in Task 6 — consistent name
- [x] **`localBasePath` Awake default** — both loaders set default in `Awake()` when empty, consistent
- [x] **NativeArray lifecycle** — `LoadPlacedRuntimeWorldFromSplatsAsync` disposes in `finally` block (Task 2); PLY parse then hands off to that method (Task 4)
