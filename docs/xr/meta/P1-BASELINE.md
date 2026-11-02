# P1.0 — Meta baseline captured before target isolation

Date: 2026-10-27. Captured by the DeepSeek session. Nothing was changed by this step; it is the
"before" picture that P1 must not break for the Meta target.

Machine-readable copy: [docs/xr/meta/baseline/meta-baseline.json](baseline/meta-baseline.json).

## Method

- Parsed from serialized project assets: `ProjectSettings/ProjectSettings.asset`,
  `ProjectSettings/EditorBuildSettings.asset`, `Assets/XR/XRGeneralSettingsPerBuildTarget.asset`,
  `Assets/XR/Settings/OpenXR Package Settings.asset`, `Assets/Plugins/Android/AndroidManifest.xml`.
- `graphicsAPIs`, `scriptingBackend`, `openxr.renderMode` and `openxr.depthSubmissionMode` were read
  from the live editor via `unity command script.eval`, not guessed from YAML.
- YAML keys here are case-sensitive: `m_Enabled` is the component flag, `m_enabled` is the OpenXR
  feature flag. An initial parse that ignored case reported 76/77 features enabled; the real number
  is 7 of 50 Android entries.

## Player / build configuration

| Setting | Value |
|---|---|
| Active build target | Android |
| Application identifier | `com.QLC.HeadsetHolodeck` |
| Bundle version | 9.2.1 (code 1) |
| minSdk / targetSdk | 32 / automatic |
| Target architectures | ARM64 (`AndroidTargetArchitectures: 2`) |
| Graphics APIs | Vulkan (explicit, not automatic) |
| Scripting backend | IL2CPP |
| Active input handler | 1 (Input System) |
| OpenXR render mode | MultiPass |
| Depth submission | None |
| Android defines | `USE_INPUT_SYSTEM_POSE_CONTROL;UNITY_POST_PROCESSING_STACK_V2;USE_STICK_CONTROL_THUMBSTICKS;SHERPA_ONNX;PICO_SPATIALIZER;ENABLE_PICO_OPENXR_SDK;ENABLE_PICO_INTERACTION_PACK;UNITY_MCP_READY;UNITY_MCP_DEPS_17` |

## XR management

| Group | Installed loaders | Init manager on start | Automatic loading / running |
|---|---|---|---|
| Android | `UnityEngine.XR.OpenXR.OpenXRLoader` | true | false / false |
| Standalone | `UnityEngine.XR.OpenXR.OpenXRLoader` | — | false / false |

Both groups use Unity's OpenXR loader; the Pico SDK contributes features to it rather than replacing
it. `PXR_Loader.asset` exists in the project but is **not** installed in either provider list.

## OpenXR features — Android (50 entries, 7 enabled)

Enabled, all Meta/Unity:

`HandTracking`, `MetaHandTrackingAim`, `MetaQuestFeature`, `MetaQuestTouchPlusControllerProfile`,
`MetaQuestTouchProControllerProfile`, `OculusTouchControllerProfile`, `AndroidXRSupportFeature`.

PICO features registered but **all 15 disabled**:

`BodyTrackingFeature`, `DisplayRefreshRateFeature`, `FacialSimulationFeature`, `FoveationFeature`,
`LayerSecureContentFeature`, `OpenXRExtensions`, `PassthroughFeature`, `PICO4ControllerProfile`,
`PICO4UltraControllerProfile`, `PICOFeature`, `PICOG3ControllerProfile`, `PICONeo3ControllerProfile`,
`PICOSceneCapture`, `PICOSpatialAnchor`, `PICOSpatialMesh`.

The remaining 28 entries are other Unity/Unity-Technologies features, also disabled
(`SpaceWarpFeature`, `EyeGazeInteraction`, the AR Foundation set, `MockRuntime`, …).

## Build scenes

`Assets/Scenes/Holodeck.unity` (`2d01f3713ded2496ab416f6c079957de`), enabled.

## Source Android manifest — the finding that explains the failed launch

Present: `INTERNET`, `CAMERA`, `RECORD_AUDIO`, `MODIFY_AUDIO_SETTINGS`, `HAND_TRACKING`,
`com.oculus.permission.HAND_TRACKING`, `horizonos.permission.HEADSET_CAMERA`, Vulkan + camera +
microphone features, `oculus.software.handtracking`, and the immersive marker
`<category android:name="com.oculus.intent.category.VR" />` inside the launcher intent-filter.

Absent: any Pico immersive marker. There is no `pvr.app.type` meta-data and no
`android.hardware.vr.headtracking` uses-feature. The activity class is Unity 6's
`com.unity3d.player.UnityPlayerGameActivity`.

That maps directly onto the emulator's recorded failure — `APxrRuntime: no vr tag in the activity`,
`no vr tag in the application`, `current activity is not marked as an xr app`,
`xrCreateInstance: XR_ERROR_VALIDATION_FAILURE` — so the failing launch is explained by missing
markers plus disabled Pico features, not by an unexplained runtime defect.

## What P1 has to change (and must not break)

1. **Enable Pico features for a Pico build.** All 15 are off today; at minimum `PICOFeature` and a
   controller profile are required, and passthrough/spatial features are optional capabilities.
2. **Disable the Meta features for a Pico build.** All 7 enabled entries are Meta/Unity ones; a Pico
   session should not request Meta extensions.
3. **Add the Pico immersive markers to the manifest** for the Pico target only, while keeping the
   Oculus category for Meta.
4. **Decide the loader story.** Unity's OpenXR loader with Pico features may be sufficient; installing
   `PXR_Loader.asset` instead is the alternative. This needs a build to settle, not speculation.
5. **Separate the application identifier for Pico** so on-device data and `.env` are independent
   (today there is one id, `com.QLC.HeadsetHolodeck`).
6. **Preserve the Meta baseline above** — Meta features, Vulkan/IL2CPP/MultiPass, ARM64, and the
   existing app id for the Quest build.

## Not captured here

- The **merged** APK manifest — only available after a build; the source manifest is not proof.
- Emulator runtime extension/feature support beyond the earlier `vkjson` report.
- A Meta build regression check after the SDK integration (still unverified; see the worklog).

## Reproduce

```powershell
$u = 'C:\Users\daren\AppData\Local\Unity\bin\unity.exe'
& $u status
& $u command script.eval_file <path-to-probe.cs> --timeout 180 --result-only
```

The JSON can be regenerated by re-running the same parse over the five files listed under Method.
