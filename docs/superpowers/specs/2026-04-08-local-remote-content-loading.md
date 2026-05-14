# Spec: Local & Remote Content Loading

**Date:** 2026-04-08  
**Status:** Approved

---

## Goal

Allow users to load 3D Gaussian Splat files (`.spz`, `.ply`) and equirectangular panoramic images (`.jpg`, `.jpeg`, `.png`) into the headset experience from two sources:

1. **Local storage** — files copied to a known folder on the headset via ADB or a companion app
2. **Remote URL** — any publicly accessible HTTP/HTTPS URL

Loading is triggered by voice command, with the AI extracting the file path or URL from the transcript.

---

## User Stories

| Command | Expected Behavior |
|---|---|
| "load the splat from landscapes.spz" | Load `<base>/landscapes.spz` as a 3D splat world |
| "load the ply file robot.ply" | Load `<base>/robot.ply` as a 3D splat world |
| "show me the panorama mountains.jpg" | Load `<base>/mountains.jpg` as a panorama |
| "load splat from https://example.com/scene.spz" | Download and load remote SPZ |
| "load panorama from https://cdn.example.com/pano.jpg" | Download and show remote panorama |

The AI extracts the filename or full URL from the spoken transcript. Short filenames are resolved against the configured local base path. Full URLs (starting with `http://` or `https://`) are fetched over the network.

---

## Local Base Path

On Quest, Unity's writable storage lives at:
```
Application.persistentDataPath
  → /sdcard/Android/data/<packageName>/files/
```

The loaders expose a `localBasePath` Inspector field that defaults to:
```
Application.persistentDataPath + "/WorldContent/"
```

Users copy files there via ADB:
```bash
adb push landscapes.spz /sdcard/Android/data/com.yourcompany.app/files/WorldContent/
```

The AI resolves partial paths (e.g., `landscapes.spz`) against `localBasePath`. Full absolute paths and `http://`/`https://` URLs bypass this resolution.

---

## Supported Formats

### Splat Files
| Extension | Format | Parser |
|---|---|---|
| `.spz` | Niantic/Scaniverse SPZ (gzip-compressed binary) | `SPZFileReader` (already in runtime) |
| `.ply` | Standard Gaussian Splat PLY (binary_little_endian) | `RuntimePlyReader` (new runtime port of Editor's `GaussianFileReader`) |

### Panoramic Images
| Extension | Notes |
|---|---|
| `.jpg` / `.jpeg` | Equirectangular, any aspect ratio. Typical 4096×2048 or 8192×4096 |
| `.png` | Same requirements |

Other image formats (`.webp`, `.exr`, `.hdr`) are **out of scope**.

---

## Architecture Overview

```
Voice command
    │
    ▼
WorldActionDispatcher
    ├── LoadSplat intent → LocalRemoteSplatLoader.LoadAsync(path_or_url)
    │       ├── local path → File.ReadAllBytes
    │       └── URL → UnityWebRequest download
    │       │
    │       ├── .spz → SPZFileReader.ReadFile → NativeArray<InputSplatData>
    │       └── .ply → RuntimePlyReader.ReadFromBytes → NativeArray<InputSplatData>
    │       │
    │       └── RuntimeSplatFloorLoader.LoadPlacedRuntimeWorldFromSplatsAsync(splats)
    │                  └── WorldLabsWorldManager.RegisterExternalWorld(worldId, renderer)
    │
    └── LoadPanorama intent → LocalRemotePanoLoader.LoadAsync(path_or_url)
            ├── local path → File.ReadAllBytes
            └── URL → UnityWebRequest download
            │
            └── Texture2D.LoadImage(bytes)
                └── ThumbnailSkyboxController.Show(tex)
                    └── ViewModeController.RequestPanoView()
```

---

## New Components

### `LocalRemoteSplatLoader` (new MonoBehaviour)
**Path:** `Assets/App/Command/SpeechIntent/Runtime/LocalRemoteSplatLoader.cs`

```
Inspector fields:
  worldManager       : WorldLabsWorldManager
  floorLoader        : RuntimeSplatFloorLoader
  localBasePath      : string  (default = Application.persistentDataPath + "/WorldContent/")
  
Public API:
  Task<bool> LoadAsync(string pathOrUrl, string displayName = null)
    — detects local vs URL, SPZ vs PLY, loads and registers with worldManager
  
Events (Inspector-wirable):
  StringEvent onLoadStarted    (fires with resolved worldId)
  StringEvent onLoadFailed     (fires with error message)
```

### `LocalRemotePanoLoader` (new MonoBehaviour)
**Path:** `Assets/App/Command/SpeechIntent/Runtime/LocalRemotePanoLoader.cs`

```
Inspector fields:
  thumbnailSkybox    : ThumbnailSkyboxController
  viewModeController : ViewModeController
  localBasePath      : string  (default = Application.persistentDataPath + "/WorldContent/")

Public API:
  Task<bool> LoadAsync(string pathOrUrl)
    — detects local vs URL, downloads/reads image, calls Show()

Events (Inspector-wirable):
  StringEvent onLoadStarted
  StringEvent onLoadFailed
```

### `RuntimePlyReader` (new static class)
**Path:** `Assets/App/GaussianSplatting/Runtime/RuntimePlyReader.cs`

```
Public API:
  static void ReadFromBytes(byte[] plyBytes, out NativeArray<InputSplatData> splats)
  static void ReadFromFile(string filePath, out NativeArray<InputSplatData> splats)
```

Port of `GaussianSplatting.Editor.Utils.GaussianFileReader` + `PLYFileReader` with no `UnityEditor.*` imports.

---

## Voice Intent Schema Changes

### New intent types (VoiceIntentType enum)
```csharp
LoadSplat     = 14,  // load a local/remote .spz or .ply file
LoadPanorama  = 15,  // load a local/remote panoramic image
```

### New field on VoiceIntentCommand
```csharp
[Header("Local/Remote Content")]
public string content_path = "";  // file name, relative path, or full URL
```

### JSON Schema additions (OpenAiSpeechIntentService)
```json
"LoadSplat", "LoadPanorama"  added to intent enum
"content_path"               added as string field, required for LoadSplat and LoadPanorama
```

### Routing hints (additionalDeveloperInstructions)
```
- LoadSplat: user wants to load a local or remote .spz or .ply splat file.
  Extract the filename, path, or URL into content_path. Example: "load landscapes.spz" → intent=LoadSplat, content_path="landscapes.spz"
- LoadPanorama: user wants to show a local or remote panoramic image (.jpg/.png).
  Extract the filename, path, or URL into content_path. Example: "show pano mountains.jpg" → intent=LoadPanorama, content_path="mountains.jpg"
```

---

## Modified Files

| File | Change |
|---|---|
| `Assets/App/GaussianSplatting/Runtime/RuntimePlyReader.cs` | **NEW** — runtime PLY → InputSplatData parser |
| `Assets/App/Scripts/Direct/RuntimeSplatFloorLoader.cs` | Add `LoadPlacedRuntimeWorldFromSplatsAsync(NativeArray<InputSplatData>, ...)` overload |
| `Assets/App/Command/SpeechIntent/Runtime/LocalRemoteSplatLoader.cs` | **NEW** — local/URL splat loader |
| `Assets/App/Command/SpeechIntent/Runtime/LocalRemotePanoLoader.cs` | **NEW** — local/URL pano loader |
| `Assets/App/Command/SpeechIntent/Runtime/VoiceIntentSchemas.cs` | Add `LoadSplat`, `LoadPanorama` enums; add `content_path` field |
| `Assets/App/Command/SpeechIntent/Runtime/WorldActionDispatcher.cs` | Add loader fields + `HandleLoadSplat`, `HandleLoadPanorama` coroutine handlers |
| `Assets/App/Editor/SpeechIntentSceneSetup.cs` | Auto-wire both new loaders |
| `Assets/App/Command/SpeechIntent/OpenAiSpeechIntentConfig.asset` | Update routing hints (via Inspector) |

---

## Out of Scope

- File browser / picker UI
- File transfer from PC to headset (user handles via ADB)
- `.splat` format (Antimatter15 format) — use `.ply` or `.spz` instead
- EXR / HDR panoramas
- Generating a `WorldLabsWorldManager`-compatible `World` object from local files (local worlds use `RegisterExternalWorld` directly)
- Progress bar / streaming load for large files
- Caching loaded files across sessions

---

## Risks & Notes

1. **PLY file size**: PLY files are uncompressed and can be 200–800 MB. Reading all bytes at once may OOM on Quest. The plan accepts this risk for an initial implementation; streaming support is deferred.

2. **Background thread safety**: `File.ReadAllBytes` and `UnityWebRequest` must be called carefully (WebRequest must be on main thread to initiate; the actual wait can be in a coroutine). The `LoadAsync` methods are coroutine-based via `StartCoroutine` in `WorldActionDispatcher`.

3. **`NativeArray` lifecycle**: `RuntimePlyReader.ReadFromBytes` allocates with `Allocator.Persistent`. The caller (`LocalRemoteSplatLoader`) must dispose after `LoadPlacedRuntimeWorldFromSplatsAsync` completes.

4. **`RuntimePlyReader` is a port, not a reimplementation**: Copy the logic from `GaussianFileReader.cs` + `PLYFileReader.cs` (both in `Editor/`), strip all `UnityEditor.*` imports, change namespace to `GaussianSplatting.Runtime`. No algorithmic changes needed.
