# Shared XR application: Meta Quest and PICO Swan

Date: 2026-10-26. Status: folder organization completed; platform integration planned.

Updated 2026-10-27: SDK initialization, live Unity/Pico MCP connection, and Swan emulator boot are
complete. The current execution plan is [Pico emulator parity](pico/EMULATOR_PARITY_PLAN.md).
The first unchanged Meta APK launch installs/starts but fails XR startup because Pico's runtime
does not recognize the app as immersive. Begin with target/manifest isolation, then a proper Pico build.
Pico target: Project Swan / PICO OS 6, provisional. Meta reference: existing Quest workflow.

## Objective

Maintain one application with two headset implementations. Preserve existing Meta functionality,
prove parity on Swan where its capabilities allow, then add room holodeck rendering, portals,
provider updates, and eye-aware commands. SDK availability and runtime capabilities determine
supported features; OpenXR alone does not guarantee camera, environment, or passthrough parity.

## Folder layout

```text
Assets/App/
  Shared/
    Scripts/                   # Holodeck runtime and provider clients
    Command/                   # SpeechIntent, interaction, targeting, UI
    Save/                      # persistence runtime and tests
    Editor/                    # build/setup/validation tooling and editor tests
  Platforms/
    Meta/                      # Meta adapters and target composition (planned)
    Pico/
      Settings/Pico.asset      # existing profile, GUID preserved
  Prefabs/, Materials/, Input/, UI/, Sounds/, ...
                               # common content retained at existing locations
Assets/Scenes/Holodeck.unity    # retained working Meta baseline
Assets/XR/                     # current global XR settings; target switching pending
Assets/Plugins/                # native libraries/Android manifest; target selection pending
Packages/                      # pinned dependencies and modified vendored packages
docs/
  xr/PLAN.md                   # architecture, migration, acceptance gates
  xr/meta/README.md            # retained Meta baseline and regression checklist
  xr/pico/README.md            # Swan work queue and evidence location
  PICO_SWAN.md                 # initial inventory, source references, toolchain findings
  stabilization/              # existing Quest evidence preserved as historical records
```

Current organization moved Scripts, Command, Save, and Editor into Shared, keeping each original
`.meta` file. Namespaces and assembly names are unchanged. Editor helper asset paths and active
setup documentation were updated. The Pico profile was moved with its original GUID. Historical
handoffs retain their original paths; use the mapping above when following them.

No scene was duplicated. No package, runtime behavior, scene wiring, Android manifest, or XR feature
selection was changed by this folder migration. Meta-specific details still embedded in shared
code are migration work, not an assertion that platform isolation is complete.

## Ownership and dependency rules

| Shared | Platform-specific |
| --- | --- |
| State machine, speech/intent routing, wake/ASR/TTS coordination | Device microphone/permission quirks if needed |
| World Labs, 3dAIStudio/Tripo, object caches and networking | SDK access, device capabilities and lifecycle |
| XR Hands/XRI interaction and command target policy | Vendor input extensions and tracking acquisition |
| Save schema, recovery, world/object/audio persistence | Camera, passthrough, room geometry and anchors |
| LCARS UI, world manipulation, content loading | Target profiles, manifest additions and bootstrapping |
| Portal rendering and gray/yellow room materials | Vendor compositor/passthrough integration |

Planned dependency direction: platform composition → adapters + shared contracts → shared feature
logic. Shared contracts should expose capabilities and results, not SDK types. Use the existing
Holodeck and SpeechIntent namespaces; folders do not require namespace renaming.

Introduce only contracts needed by a concrete integration: capability discovery, camera frames,
passthrough control, environment geometry, and timestamped gaze. Each optional capability reports
unsupported, denied, unavailable, or ready. Reuse generic OpenXR/XR Hands providers when possible.
Provide editor fixtures for room geometry and gaze. User-visible fallback behavior is shared.

Do not add asmdefs to all existing code in this organization step. Most runtime/editor code uses
Unity's predefined assemblies, while Save and credential tests have existing asmdefs. Introducing
new boundaries requires an explicit reference graph and migration of any consumers that cannot
reference Assembly-CSharp. Vendor assemblies must be gated so a missing Pico SDK cannot break
the Meta build, and vice versa. Keep native plugin import settings intentional for both devices
and the emulator ABI. Avoid scattering `#if` directives through command and UI code.

## Work packages and exit criteria

### XR-01 — Folder migration and baseline preservation

- [x] Move shared code with metadata and retain namespaces/assemblies.
- [x] Add Meta/Pico ownership folders and relocate the Pico profile with metadata.
- [x] Update active path references and document the migration.
- [x] Import and compile in Unity; load the build scene and confirm zero missing scripts.
- [ ] Run existing editor regression tests.
- [ ] Build and run the Meta cached-content flow to establish the reorganized baseline.

Exit: asset GUIDs retained, path checks pass, Unity compiles, and Meta smoke test evidence exists.
Static checks alone do not close this gate.

### XR-02 — Swan toolchain and reproducible target configuration

- [x] Install official PICO CLI 0.6.1; initial diagnostic reports virtualization ready.
- [x] Resolve disk shortage, finish emulator 6.1.0 installation, create/start one named AVD.
- [ ] Record emulator OS/ABI, OpenXR extensions, GPU/Vulkan behavior, microphone support.
- [ ] Select and pin the current Swan-compatible Unity SDK/OpenXR combination.
- [ ] Snapshot current Quest configuration into a dedicated Meta build profile.
- [ ] Implement explicit Meta/Pico configuration selection for global XR features and manifests.
- [ ] Verify target switching Meta → Pico → Meta, including a clean build of each.

Use target settings assets plus editor build validation. The build must reject a mismatched target
configuration rather than silently using the previous headset's global settings. Inspect the merged
Android manifest, app identifier, loader, interaction profiles, permissions, Vulkan, multi-pass, and
GaussianSplatURPFeature. Keep credentialMode=device and target-device credential storage.

Exit: reproducible launchable builds with the intended runtime configuration and no unintended
cross-vendor dependency. Current storage blocker is recorded in `../../docs/PICO_SWAN.md`.

### XR-03 — Existing functionality parity

Use the detailed matrix in [PICO_SWAN.md](../PICO_SWAN.md). Cover launch/render, controllers/hands,
voice, UI, world generation/loading/manipulation, object generation/cache, audio, image capture,
save/restart/recovery, and repeated load/unload. Start with cached/offline fixtures; live provider
checks follow. Preserve existing Quest limitations from stabilization reports rather than treating
them as new Pico regressions.

Record each capability as pass, fail, unsupported, or not tested. Attach SHA, SDK versions, target,
steps, expected/actual behavior, logs and screenshots. Separate emulator evidence from hardware
evidence. Unsupported features require an explicit capability fallback and documented rationale.

Exit: emulator-supported parity cases pass, hardware-only cases are identified, and Meta regressions
are closed. Hardware acceptance remains required for tracking, room alignment, camera/passthrough,
eye calibration and sustained performance.

### XR-04 — Environment capture and holodeck appearance

Implement a platform environment adapter and shared room renderer. Start with an editor room
fixture, then captured planes/meshes. Apply metric world-space gray/yellow grid materials; handle
floor origin, occlusion/collision, tracking changes, permission denial, partial scans and rescan.
Keep room geometry local unless a specific cloud feature requires it.

Exit: fixture and supported target room geometry render consistently; unavailable capture falls
back clearly; physical alignment and scale are verified on hardware.

### XR-05 — Passthrough/world portals

Prototype a single aperture using a passthrough adapter and shared portal rendering. Test the splat
renderer early for depth/stencil/clipping compatibility before committing to the effect. Add
traversal, exit/recovery, collision and spatial audio transitions after compositing is proven.

Exit: both eyes render correctly, clipping/occlusion and world loading work, repeated transitions
do not leak resources, and room alignment is verified. State any unsupported compositor behavior.

### XR-06 — World Labs and Tripo API refresh

Audit schemas/model IDs, status/error parsing, asset formats, download expiry, retries, timeouts,
cancellation and credit estimates. Keep provider changes independent of headset selection.
Current Tripo implementation uses 3dAIStudio, not standalone Tripo. Preserve the vendored World Labs
rendering and credential changes. Add fixtures for changed response contracts, then live smoke tests.

Exit: text/image generation and textured asset loading work with pinned, documented API contracts;
failure/retry cases pass and existing cached content/save records still load on both platforms.

### XR-07 — Eye-aware references and spatial commands

Feed timestamped, valid eye poses into the existing spatial snapshot/focus resolver. Freeze target
context near utterance time so network latency cannot select a later look target. Define precedence
between held objects, pointing, eye gaze, selection and discourse memory. Resolve `this/that` to
objects and `there` to a surface/position; retain `it` discourse behavior.

Add a focus indicator, confidence/staleness limits, calibration/permission handling and tracking-loss
fallback. Keep head gaze distinct from measured eye gaze. Reuse the semantic rig harness for
conflicting cues, no hit, stale gaze, moving objects and delayed intent responses.

Exit: deterministic fixture tests and supported-device gaze commands pass; fallback interaction
continues to work on Meta and devices without valid eye tracking.

## Validation and reporting

Folder-migration checks completed on 2026-10-26:

- Compared all 465 relocated files with their original Git blobs: content is unchanged except
  intended path substitutions and line endings.
- Verified all 249 original metadata/assembly-definition blobs match at their new paths.
- Verified 23 local documentation links and found no obsolete application/profile paths in active
  source and the updated setup documents/audit harness.
- `git diff --check` passed.

The [Unity validation report](meta/FOLDER_VALIDATION.md) records successful import, compilation,
scene loading (1,789 objects, zero missing scripts), and an Android build with the original
scripting flags restored. Unity exited 0 and produced `Builds/Meta-folder-validation.apk`.
Existing warnings remain. Editor regression tests and a headset smoke test remain pending.

For organization changes: verify original metadata hashes/GUIDs, unchanged assembly definitions,
active asset-path resolution, and Unity import/compile before claiming runtime parity. Run existing
save, credential, targeting and hand-input checks appropriate to later code changes. Use cached
content before live generations. Keep Meta and Pico evidence under separate target documentation;
do not overwrite the retained Quest stabilization evidence.

After each work package, record changed files, validated target/version, evidence, remaining
limitations and next step. No Pico acceptance or fresh Unity test pass is claimed by this plan.
