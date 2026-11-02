# PICO Swan sub-project

Started 2026-10-26. Target: Project Swan / PICO OS 6 (provisional, selected by owner).

Implementation tracking now lives in [the shared Meta/Pico plan](xr/PLAN.md). Application code
moved under `Assets/App/Shared`; the Pico profile moved under `Assets/App/Platforms/Pico/Settings`
with its original GUID. This inventory remains the initial toolchain assessment.

Later setup and validation are recorded in [the Pico development environment report](xr/pico/DEVELOPMENT_ENVIRONMENT.md)
and [the successful Meta import/build report](xr/meta/FOLDER_VALIDATION.md). The initial inventory
below is historical; its earlier free-space numbers predate the completed builds.

## Baseline and local setup

- Synced `main` from `df8590b` to `c1fe017` with a fast-forward of 24 commits.
- Preserved the existing untracked `.vsconfig`.
- Read `stabilization/STATUS.md` and `stabilization/handoff-06.md`. Their Quest evidence is historical; it is not Pico acceptance evidence.
- Unity 6000.2.10f1 and Android Build Support with SDK, NDK, and OpenJDK are installed. Android Studio is installed.
- Installed official `@picoxr/pico-cli` globally: `pico-cli/0.6.1 (public) win32-x64 node-v24.13.0`. Command shim: `C:/Users/daren/AppData/Roaming/npm/pico-cli.cmd`.
- Emulator doctor reports Windows virtualization ready. Initial Java detection failed; setting process-local `JAVA_HOME` to `C:/Program Files/Android/Android Studio/jbr` allowed the installer to run without installing another JDK.
- Installer selected Swan emulator 6.1.0 (3,472.64 MB archive) and configured `PICO_HOME` at `C:/Users/daren/AppData/Local/PICO/sdk`. Download was interrupted before extraction after disk inventory found only about 24.4 GB free on the only ready volume, below the documented 32 GB requirement. No emulator/AVD is installed or running. Check for retained partial download cache when resuming.
- Storage is the immediate setup blocker: free at least 8 GB more to meet the documented minimum, plus Unity build/import headroom. No existing user files were deleted.
- Unity's bundled adb could not start its server in the restricted execution environment. Device connectivity is unverified.
- `Assets/App/Platforms/Pico/Settings/Pico.asset` already exists. It is an Android profile, not proof of Pico runtime configuration.
- `Packages/manifest.json` has no Pico SDK dependency. Shared OpenXR settings enable MetaQuestFeature, AndroidXRSupportFeature, MetaHandTrackingAim, and Quest controller profiles. Android EyeGazeInteraction is disabled.
- The Android manifest contains Oculus launch category, hand permissions, and Horizon headset-camera permission. Audit generated Pico manifests rather than assuming these provide Pico support.

## Delivery order

### 1. Establish the Swan toolchain

Install official PICO CLI and inspect its help and doctor output. Install the Swan emulator, check Windows virtualization/GPU requirements and disk capacity, create one named AVD, start it, and record OS, ABI, runtime extensions, and graphics capabilities. Official guidance reserves at least 32 GB of disk and recommends 12 GB available RAM.

Choose and pin a current Swan-compatible PICO Unity SDK/OpenXR route after checking its Unity, URP, OpenXR, and Vulkan compatibility. Do not treat the older standalone PICO OpenXR GitHub package as the current Swan SDK. Preserve the modified vendored World Labs and Sherpa packages. Keep provider credentials on the target device using `credentialMode=device`; derive the device path from the Pico application identifier.

Keep Quest and Swan settings reproducible independently. The existing build profile includes Player Settings overrides, but shared OpenXR assets also need platform selection. Check loader, controller/hand profiles, permissions, render mode, Vulkan, and GaussianSplatURPFeature. Verify native Sherpa libraries and emulator ABI compatibility before claiming voice support.

### 2. Prove feature parity before new features

Record each case as pass, fail, unsupported, or not tested, with build SHA, SDK/emulator versions, logs, and reproduction steps. Use existing rig harness and batch tests where applicable.

| Area | Acceptance cases |
| --- | --- |
| Launch/render | Cold launch, stereo tracking, floor/spawn, URP splats, panoramas, scene transitions |
| Voice | Microphone permissions, local wake/VAD/ASR, TTS, push-to-talk, intent routing, recovery |
| Input/UI | Controllers, XR Hands pinch/grip/point, hand-only mode, LCARS panels, teleport/navigation |
| Worlds | Text/image generation, progress/error/retry, cached/offline load, world manipulation |
| Objects | Primitives, generated textured GLB, selection, transforms, behaviors, cache |
| Audio | Playback, placement, provider failures, persistence |
| Persistence | Save, saved spawn, force-stop/restart/restore, backup recovery |
| Camera | Permission, capture, preview, image-to-world/object; distinguish camera image access from passthrough |
| Performance | Frame time, memory, load stalls, repeated load/unload, thermal behavior on hardware |

Emulator success is an intermediate gate. Real microphone behavior, tracking quality, camera access, physical room alignment, passthrough quality, eye calibration, and sustained rendering require hardware acceptance. Official emulator guidance explicitly limits spatial interaction automation.

### 3. Gray/yellow holodeck room

Introduce a capability-based environment provider for room meshes/planes, with an editor fixture fallback. Render a world-space gray material with yellow grid lines on captured geometry. Maintain metric scale, floor alignment, collision, and tracking-origin changes. Handle permission denial, partial scans, rescan, and unavailable mesh support. This paints reconstructed geometry; passthrough imagery alone does not provide the mesh.

### 4. Passthrough/world portals

Introduce a passthrough adapter and portal renderer. Prototype an aperture between passthrough and a loaded world, then bidirectional traversal, clipping/occlusion, collision, and audio transition. Prove splat depth/stencil compatibility early: mesh portal techniques may not clip the existing splat renderer. Validate both eyes, overlapping portals, room alignment, and exit/recovery paths. Feature availability is runtime-dependent.

### 5. Provider API refresh

World Labs currently uses `/marble/v1` and hardcoded `marble-1.0-draft`, `marble-1.0`, `marble-1.1`, and `marble-1.1-plus` model IDs. Audit against the current official schema, authentication, model availability, operation lifecycle, cancellation/timeouts, and assets. Do not replace the vendored package wholesale: it includes rendering and credential-boundary fixes.

Tripo currently runs through `ThreeDAIStudioObjectGenerationProvider`, using unversioned `/v1/3d-models/tripo/{text,image}-to-3d/` endpoints. Official 3dAIStudio documentation says unversioned text generation defaults to 3.0 and offers explicit 3.2. Audit image endpoint versions, parameter compatibility, task statuses, textured GLB selection, expiring downloads, and credit estimates. Standalone Tripo v3 is a separate provider/authentication contract; `TRIPO_API_KEY` being present does not implement that provider.

Use fixture/contract tests before paid generation smoke tests. Record live provider results separately from offline parsing tests.

### 6. Eye-aware command references

Extend the existing `SpatialContextProvider`, `SceneFocusContext`, resolver, and `InteractionMemory` rather than introducing a second targeting system. Add valid, timestamped eye-gaze samples, confidence/tracking loss, permission/calibration status, and a visible focus indicator. Bind the indicated object or surface point near utterance time so delayed intent responses do not retarget when the user looks away.

Define object references (`this`, `that`), surface placement (`there`), discourse memory (`it`), and held/pointed-object precedence explicitly. Preserve current hand/selection/head-direction fallback when eye tracking is missing or invalid. Test ambiguous references, conflicting hand and gaze, moving targets, stale samples, no hit, and delayed interpretation. Never label head-direction gaze as measured eye gaze.

## Primary references

- [PICO resources and Swan emulator availability](https://developer.picoxr.com/resources/?platform=unity)
- [PICO CLI installation/setup](https://developer.picoxr.com/document/pico-cli/quickstart-common/)
- [PICO emulator lifecycle and limitations](https://developer.picoxr.com/document/pico-cli/emulator-guide/)
- [PICO Unity quickstart](https://developer.picoxr.com/document/pico-cli/quickstart-unity/)
- [3dAIStudio Tripo API](https://www.3daistudio.com/Platform/API/Documentation/3d-generation/tripo)
- [Standalone Tripo API](https://developers.tripo3d.ai/en/docs/introduction)
- [World Labs official API examples](https://github.com/worldlabsai/worldlabs-api-examples)

No Pico build, emulator feature pass, hardware pass, or API migration is claimed by this assessment.
