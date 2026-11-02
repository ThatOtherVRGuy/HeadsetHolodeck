# P2 — Pico build and immersive launch (evidence)

Date: 2026-10-27. Author: DeepSeek session. Target: PICO Swan / PICO OS 6 emulator 6.1.0
(`Holodeck_Pico_Swan`, Android 16 / API 36, x86_64 with ARM translation).
**No hardware pass is claimed here.** Emulator results are intermediate evidence only.

## Artifact

| Field | Value |
|---|---|
| Path | `Builds/Pico-Swan-parity.apk` |
| Size | 309 MB (release), 328 MB (earlier development build) |
| Package | `com.QLC.HeadsetHolodeckPico` |
| Credentials | `-credentialMode device` — the APK embeds nothing |
| Scripting | IL2CPP, ARM64 only (`arm64-v8a`) |
| Graphics | Vulkan, OpenXR MultiPass |

## What blocked the build, in the order it appeared

Each was found by building; none were visible from reading the plan.

1. **`PICOFeature` disabled.** The SDK's manifest processor returns early unless
   `PXR_BuildProcessor.IsLoaderExists()` is true, which (with Unity's OpenXRLoader) requires
   `PICOFeature.enabled`. Consequence: no `pvr.app.type=vr`, and the runtime rejects the app with
   `no vr tag in the activity` / `XR_ERROR_VALIDATION_FAILURE`.
2. **`PXR SDK validation failed: Please use Activity instead of GameActivity!`** The Meta baseline
   uses Unity 6's `UnityPlayerGameActivity`; PICO requires the classic activity.
3. **`Resource Not Found ... BaseUnityGameActivityTheme`.** Switching the entry point alone is not
   enough: the custom main manifest hardcoded the GameActivity theme and activity name, so Gradle
   failed. The manifest therefore became a per-target template.
4. **Burst AOT `System.UnauthorizedAccessException`.** Burst's native compiler cannot write its
   response file in this environment. `ProjectSettings/BurstAotSettings_Android.json` now has
   `EnableBurstCompilation: false` for Android — a deliberate compromise, revisit before shipping.
5. **Development build never presented.** The development APK stalled before first scene (see below);
   parity builds are now release builds.

A sixth, external defect is handled by `Assets/App/Shared/Editor/PicoSdkLoaderPrep.cs`: for OpenXR
>= 1.16 the SDK declares `CustomRuntimeLoaderName = "LoaderForUnitySDK_1_1_0"` but ships only
`LoaderForUnitySDK.aar` (no tags, every branch). The prep step renames the asset in the resolved
package before each Pico build.

## Verified on the emulator

- Merged manifest (`aapt2 dump xmltree`): `pvr.app.type=vr`, `use.pxr.sdk`, `pxr.sdk.generation`,
  `pxr.sdk.version_code`, activity `com.unity3d.player.UnityPlayerActivity`.
- Install succeeds; the launcher starts the app as an XR container.
- OpenXR session reached `IDLE -> READY -> BEGAN` with `PICOOpenXRExtensions` callbacks.
- Release build boots through `SubsystemRegistration -> BeforeSceneLoad (Vulkan, xrActive=True) ->
  AfterSceneLoad scene=Holodeck roots=13` and renders the LCARS "HOLODECK OPS" panel.
- `[RuntimeDotEnv] Loaded 15 variable(s) from /storage/emulated/0/Android/data/com.QLC.HeadsetHolodeckPico/files/.env`
  and `[EnvLoader] Loaded 15 variables` from the same path.
- Pico granted `MICROPHONE` privacy and `MicrophoneSource` ran diagnostics.

## Development vs release — the startup stall

A development build of identical code stopped after `applyWindowUIChanges`, created no OpenXR
session, and blocked with all threads idle (compositor logged `No 3D activity resumed`). The release
build of the same code boots and renders. `TargetBuildMenu` now clears
`EditorUserBuildSettings.development/allowDebugging` for parity builds. `PicoBootProbe` keeps markers
in place so a future stall localises itself.

## Not verified

- An authenticated provider call. `worlds:list` has not yet been exercised after real keys landed.
- Save / force-stop / restore on Pico.
- Interaction beyond the launcher (no controllers paired; hand/gaze path untested).
- Anything requiring hardware: microphone input, tracking quality, passthrough, room alignment.
- The Meta path after the Pico changes (deliberately deferred; the switch round-trips byte-identically).

## Emulator stability

The emulator host crashed twice with our app running (dumps 19:06 and 20:06, ~101–103 MB). No app
FATAL or `E/Unity` exception accompanies either crash, and the release build had run ~15 minutes
before the second. Treat emulator sessions as short and bounded; a third crash means moving
acceptance to hardware.

## Reproduce

```powershell
$u = 'C:\Users\daren\AppData\Local\Unity\bin\unity.exe'
& $u status
& $u command editor.invoke_menu --path "Headset Holodeck/Build/Build Pico APK (device)" --detach
& $u job status <job-id> --json
```

```powershell
pwsh tools/Verify-PicoDevice.ps1 -Serial emulator-5554 -PushEnv
```
