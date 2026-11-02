# Existing Holodeck functionality on the Pico emulator

Checked 2026-10-27. First milestone: load the existing Holodeck scene as an immersive OpenXR app
on Swan, then validate existing functions before adding new features.

## Current machine and project state

| Item | Observed state |
| --- | --- |
| Unity | 6000.2.10f1, Android target; live editor MCP responding |
| Editor console | Current error/exception query returned zero entries |
| SDK | Pico XR 6.1.1 installed; initialization marker complete in OpenXR mode |
| AI tooling | Unity/Pico MCP tools visible and responding; package info/status probes succeed |
| Emulator | Running and adb-online as `emulator-5590`, Swan emulator 6.1.0 |
| OS and ABI | Android 16/API 36; x86_64 guest advertises arm64-v8a and x86_64, native bridge libhoudini.so |
| Graphics | Vulkan 1.3 reported; compute support and RTX 2070 Super GPU exposed by vkjson |
| Storage | About 26.5 GiB free on C: before the diagnostic installation; monitor during builds |
| Current Android configuration | ARM64, Vulkan, OpenXR loader, MultiPass |
| Target isolation | Still missing: Meta and Android XR support features enabled; Pico features disabled |
| Main scene | `Assets/Scenes/Holodeck.unity` enabled in build settings; live editor initially has an empty scene |
| Previous Meta build | Successful before the later Pico SDK/package integration; regression after integration pending |

The tools and emulator are available. The application is not yet configured or accepted as a Pico
app. A clean console and an initialization marker do not establish player/runtime parity.

## First diagnostic launch — completed

Installed the existing credential-free `Builds/Meta-folder-validation.apk` on the emulator without
altering the Unity project. Installation succeeded; Android cold launch returned `Status: ok`, and
the process ran as ARM64 under the emulator's compatibility layer. The screenshot shows a black
flat app window, not the intended immersive scene.

Runtime diagnostics show:

```text
APxrRuntime: no vr tag in the activity
APxrRuntime: no vr tag in the application
APxrRuntime: current activity is not marked as an xr app
xrCreateInstance: XR_ERROR_VALIDATION_FAILURE
```

This is a **failed XR startup probe**, not an app acceptance pass. It confirms that installing the
Meta APK unchanged is insufficient. Correct the Pico feature/build/manifest configuration first;
only then investigate any remaining runtime failures. The broker also logged an unsuccessful first
runtime lookup before loading the vendor runtime, so preserve full logs if the next build still fails.

Local ignored evidence:

- `Logs/pico-vulkan-capabilities.json`
- `Logs/pico-meta-apk-probe-logcat.txt`
- `Logs/pico-meta-apk-unity-log.txt`
- `Logs/pico-meta-apk-crash-log.txt`
- `Logs/pico-meta-apk-probe.png`

No credentials were copied and no live provider generation was requested. Native-library loading,
microphone/ASR, and splat functionality are not proven by successful package installation.

## Work sequence

### P1 — Reproducible Meta/Pico configuration

1. Capture the present Meta scene, player settings, manifest and enabled XR feature state before
   changing them. Preserve original GUIDs and the existing verified APK.
2. Implement explicit target selection with separate Meta and Pico settings. Profile selection
   must also handle the global XR settings and generated Android manifest; a profile name alone
   does not do that.
3. Pico baseline: OpenXR loader, Pico OpenXR support/features, appropriate interaction profiles,
   Vulkan and MultiPass. Disable conflicting vendor platform-support features for the Pico build.
   Retain generic XR Hands/XRI functionality and existing shared application components.
4. Verify the installed Pico SDK build hooks produce the required immersive-app metadata/category.
   Inspect the **merged APK manifest**, not just the source manifest. Do not fix this by editing a
   generated Gradle file that disappears on the next build.
5. Use a separate Pico application identifier for independent app data and credentials. Reconcile
   Player Settings overrides stored in the existing Pico profile. Preserve the Holodeck scene and
   existing rig; do not create an unrelated agent XR Origin just because the status tool reports none.

Exit: switching Meta → Pico → Meta reproduces each configuration without overwriting shared scene
content. Record settings snapshots and manifest evidence. Keep package changes out of this step
unless an actual compatibility failure requires one.

### P2 — First proper Pico build and immersive launch

1. Run the existing scene/missing-script validation against Holodeck.
2. Build `Builds/Pico-Swan-parity.apk` with `credentialMode=device`; preserve original build logs.
3. Inspect package ID, ARM64 libraries, manifest and graphics requirements before installing.
4. Install to explicit serial `emulator-5590`, grant/request the permissions needed by each test,
   launch, and collect process logs plus a screenshot.
5. Require successful OpenXR instance/session startup and visible Holodeck UI/world rendering.
   Distinguish an Android launch return code from a running immersive application.
6. Check for native-library, Vulkan/compute, input-subsystem and shader errors. ARM64 compatibility
   is a starting path, not proof every library works under translation. If a real ABI failure occurs,
   evaluate emulator-specific libraries/build architecture separately from physical-headset builds.

Exit: immersive scene renders and remains alive without fatal XR/native errors; repeat launch passes.

### P3 — Offline existing-functionality checks

Run in this order so cloud failures do not obscure application/platform failures:

| Test | Required evidence |
| --- | --- |
| Navigation and LCARS UI | Open panels, interact with controls, move/turn using supported emulator inputs |
| Cached world loading | Load a small panorama and splat; verify placement, renderer, floor/collision and unloading |
| Objects and commands | Create/select/transform primitives; replay local/typed commands where available |
| Speech and audio | Permission behavior, microphone input, VAD/ASR wake, push-to-talk, TTS and playback |
| Persistence | Save world/spawn/object/audio, force-stop, relaunch, restore |
| Repeatability | Several load/unload and launch cycles; record memory growth and failures |

Label emulator input substitutions explicitly. Do not count synthetic gaze/hand input as physical
tracking acceptance. Track each case as pass, fail, unsupported, or not tested.

Exit: cached/offline workflow passes, or each concrete emulator limitation has a fallback and a
separate hardware test. Never silently drop the shared feature.

### P4 — Existing provider-backed functions

Configure credentials only in the Pico app's own device `.env`, using its verified application ID.
Do not embed keys, expose values in logs, or copy the Meta app's persistent data wholesale. Test
current World Labs text/image generation and current 3dAIStudio/Tripo object generation, download,
textured import/cache, errors/retry, save and reload. Record API failures separately from platform
failures. Start with small outputs and verify current configured providers before paid calls.

Exit: existing provider workflows pass on the configured Pico target, with evidence and limits.

### P5 — Protect Meta and close emulator parity

Switch back to Meta, build after the SDK/Input System changes, and repeat the baseline cached flow.
Record emulator-only gaps separately from hardware acceptance (passthrough/camera permissions,
physical hands, eye calibration, room alignment, thermals). New holodeck room painting, portals,
API migrations and eye-reference features remain later work packages in the shared XR plan.

## Completion criteria

The milestone is complete when the Pico build has valid immersive startup, the existing scene/UI
and cached-content workflow work, current feature results are recorded, save/restart/restore passes,
and the Meta configuration is still reproducible. An installed SDK, available MCP tool, successful
APK build or Android process launch alone does not close this milestone.

## Handoff

This file is the current entry point for emulator work. Continue with P1. Read `../PLAN.md`,
`DEVELOPMENT_ENVIRONMENT.md`, and `../meta/FOLDER_VALIDATION.md` for architecture, installation
history and prior build evidence. Inspect the working tree before changing it: existing folder moves,
SDK integration and initialization are uncommitted. Do not rerun initialization or replace the scene.
