# HeadsetHolodeckWeb Design

Date: 2026-07-02

## Summary

HeadsetHolodeckWeb is a parallel WebXR project based on HeadsetHolodeckDev. It should live as a sibling project at `/Users/davidarendash/Documents/Projects/Unity/HeadsetHolodeckWeb`, not inside the Unity repository.

The first milestone is a local-first Meta Immersive Web SDK app that places the user inside the exported static holodeck/arch shell, uses in-world spatial UI, records a voice prompt, transcribes it through a local backend, generates a World Labs world, and displays the returned panorama in WebXR.

This is a port/rebuild, not a Unity export. The Unity project remains the reference implementation for product behavior, state flow, modularity, algorithms, asset staging, and visual intent.

This document is a planning bridge stored in HeadsetHolodeckDev. Implementation should happen in the separate `HeadsetHolodeckWeb` git repository once it exists.

## Core Decisions

- Use Meta Immersive Web SDK as the primary app framework.
- Use IWSDK Spatial UI/UIKitML for in-world arch/status/control panels.
- Build local-first before planning hosted deployment.
- Keep OpenAI and World Labs secrets server-side in a local Node/TypeScript backend.
- Export static Unity assets, especially the holodeck/arch shell, instead of recreating the room procedurally.
- Use Unity MCP to inspect scenes/prefabs and assist export staging.
- Use existing C# architecture as the guide for TypeScript boundaries and runtime behavior.
- Do not require Claude Code. Codex remains the coordinating agent. IWSDK MCP/dev tools may be used from Codex if available, but they are optional.

## Source References

- Meta Web overview: https://developers.meta.com/horizon/develop/web/
- IWSDK overview: https://developers.meta.com/horizon/documentation/web/iwsdk-overview/
- IWSDK spatial UI: https://developers.meta.com/horizon/documentation/web/iwsdk-guide-spatial-ui/
- IWSDK AI-assisted tooling: https://developers.meta.com/horizon/documentation/web/iwsdk-ai-assisted-dev-tooling/
- WebXR workflow: https://developers.meta.com/horizon/documentation/web/webxr-workflow/
- WebXR Layers: https://developers.meta.com/horizon/documentation/web/webxr-layers/
- WebXR Hands: https://developers.meta.com/horizon/documentation/web/webxr-hands/
- WebXR Mixed Reality: https://developers.meta.com/horizon/documentation/web/webxr-mixed-reality/

## Feasibility

### Feasible For Milestone One

- Browser microphone capture via web media APIs.
- Local server-side OpenAI transcription.
- Local server-side World Labs text-to-world generation and operation polling.
- World Labs panorama display in WebXR.
- Exported static Unity holodeck/arch geometry loaded as GLB/GLTF.
- In-world arch/status/control UI using IWSDK Spatial UI/UIKitML.
- Desktop development with IWSDK/IWER emulation.
- Quest Browser testing over local HTTPS or another local development path.

### Feasible Later

- Web splat rendering once a World Labs-compatible renderer and `.spz` path are validated.
- Mesh rendering from World Labs collider mesh GLB assets.
- Controller and hand interaction.
- Grab/physics interaction.
- Spatial audio.
- Scene understanding, plane/mesh detection, and persistent anchors.
- Mixed reality/passthrough placement.
- WebXR Layers for higher-quality panorama rendering.
- World config save/load, cached worlds, and speech intent commands.

### Not Directly Portable

- Unity scene hierarchy and MonoBehaviour lifecycle.
- Unity coroutines, ScriptableObjects, Unity Input System, XR Interaction Toolkit, URP renderer features, and Unity shaders.
- The World Labs Unity Gaussian splat rendering path.
- Unity UI Toolkit/canvas behavior.
- Client-side `.env` and hardcoded-secret patterns.

## Proposed Project Shape

The new sibling project should use a structure like:

```text
HeadsetHolodeckWeb/
  apps/
    web/
      src/
        app/
        components/
        systems/
        renderers/
        ui/
        worldlabs/
      public/
        assets/
    server/
      src/
        openai/
        worldlabs/
        routes/
        config/
  assets/
    unity-export/
    test-worlds/
  docs/
    export/
    iwsdk-audit/
    architecture/
```

Exact folders may adapt to the IWSDK scaffold, but the boundaries should remain clear:

- `apps/web`: IWSDK frontend, ECS systems, spatial UI, renderers, WebXR runtime.
- `apps/server`: local API proxy for OpenAI and World Labs.
- `assets/unity-export`: exported holodeck/arch GLBs, textures, and export manifests.
- `assets/test-worlds`: cached metadata or test panoramas for repeatable development.
- `docs`: export notes, IWSDK capability audit, and architecture notes.

## Runtime Architecture

### State Machine

Port the Unity state model conceptually:

```text
Idle -> ListeningForCommand -> Interpreting -> Generating -> Ready
  ^                                                        |
  |------------------------- Error ------------------------|
```

The TypeScript state machine should keep guarded transitions, user-facing status messages, and explicit error recovery. It should be independent of UI rendering so UIKitML panels and future input sources can observe the same state.

### Voice Capture

The web voice capture module replaces Unity `Microphone` with browser media APIs.

Responsibilities:

- Request microphone permission.
- Start/stop recording.
- Produce audio data for server upload.
- Detect empty recordings and permission failures.
- Publish state and errors to the app state machine/status bus.

### Local Server

The local backend owns secrets and external API calls.

Responsibilities:

- Load local `.env` values for `OPENAI_API_KEY` and `WORLDLABS_API_KEY`.
- Receive recorded audio from the frontend.
- Transcribe audio with OpenAI.
- Submit text prompts to World Labs.
- Poll World Labs operations until completion or timeout.
- Normalize World Labs responses into a frontend-friendly `WorldResult`.
- Optionally proxy panorama/SPZ/mesh assets if CORS requires it.

`WorldResult` should include:

- `worldId`
- `displayName`
- `prompt`
- `transcript`
- `panoUrl`
- `thumbnailUrl`
- `spzUrls`
- `meshUrl`
- `raw`

### Coordinator

Port the role of `VoiceToWorldLabsPluginCoordinator` into a frontend coordinator/system that drives the user flow:

1. Begin recording.
2. Stop recording.
3. Send audio to local server.
4. Receive transcript.
5. Request generation.
6. Track long-running generation status.
7. Load world assets.
8. Activate the current renderer.
9. Transition to `Ready` or `Error`.

The coordinator should not know renderer internals. It should hand a `WorldResult` to the selected renderer.

### Renderers

Define a renderer boundary early:

```ts
interface WorldRenderer {
  readonly mode: "static" | "panorama" | "splat" | "mesh";
  load(world: WorldResult): Promise<void>;
  show(): void;
  hide(): void;
  dispose(): void;
}
```

Milestone one implementations:

- `StaticShellRenderer`: loads the exported Unity holodeck/arch shell and manages static shell visibility.
- `PanoramaRenderer`: displays `WorldResult.panoUrl` as the active generated world.

Later implementations:

- `SplatRenderer`: integrates a web splat renderer compatible with World Labs assets.
- `MeshRenderer`: loads World Labs collider mesh GLBs for mesh view or collision support.

## UI Design

Use IWSDK Spatial UI/UIKitML as the primary UI system. Browser DOM overlay UI is allowed only as a development fallback.

Initial UI panels:

- Status panel: idle, recording, transcribing, generating, loading, ready, error.
- Prompt/transcript panel: last captured transcript and active generation prompt.
- Controls panel: record/stop, cancel generation, reset to static holodeck.
- World info panel: world id, display name, model tier, asset availability.

The LCARS/arch visual language should be ported into UIKitML style classes and theme tokens. The exported arch geometry provides physical context and anchor positions; UIKitML provides live, interactive controls.

## Unity Asset Export Strategy

Use Unity MCP to inspect and stage export candidates:

- `Assets/Models/TNGHolodeck.prefab`
- holodeck/arch scene objects in `Assets/Scenes/Holodeck.unity`
- door geometry and simple open/close animation data where exportable
- relevant materials and textures under `Assets/Models/Materials`
- UI anchor transforms for positioning WebXR panels

The project currently includes `com.unity.cloud.gltfast` for GLB import, not necessarily export. If no exporter is already available, add or use an appropriate Unity GLTF exporter, or use an export bridge such as Blender only if Unity export is not viable.

Expected export workflow:

1. Inspect the Unity scene/prefab with Unity MCP.
2. Create a staging export scene or prefab containing only static exportable objects.
3. Strip scripts, Unity-only components, and runtime-only objects.
4. Export GLB/GLTF with materials/textures.
5. Import into the IWSDK project.
6. Verify scale, orientation, material fidelity, and panel anchor locations.
7. Document export steps under `HeadsetHolodeckWeb/docs/export/`.

## C# Architecture-Guided Port

The existing C# code should guide boundaries and behavior:

- `HolodeckStateMachine`: state names, transition rules, error flow.
- `VoiceCaptureManager`: capture lifecycle and failure handling.
- `OpenAITranscriptionClient`: transcription request shape and error handling, moved server-side.
- `VoiceToWorldLabsPluginCoordinator`: orchestration, long-running generation handling, cancellation.
- `WorldLabsClient` and models: TypeScript API models and normalized response types.
- `ThumbnailSkyboxController` and panorama loaders: panorama display behavior.
- `ViewModeController`: future static/pano/splat/mesh mode switching.
- `WorldMeshController`: later GLB mesh loading path.
- Speech intent modules: later command architecture, not milestone one.

This should be a conceptual port, not a line-by-line translation.

## IWSDK Capability Audit

Before implementation, perform and document an IWSDK capability audit that covers:

- ECS runtime patterns and system/component conventions.
- Spatial UI/UIKitML authoring, styling, events, and anchoring.
- Spatial Editor workflow and asset placement.
- Asset optimization/import pipeline.
- Controller and hand input.
- Locomotion, comfort, and interaction affordances.
- Grab interactions and physics integration.
- Spatial audio.
- Scene understanding, planes, meshes, and anchors.
- Passthrough/MR capabilities and Quest Browser constraints.
- WebXR Layers for panorama rendering.
- IWER desktop emulation and debugging.
- Optional MCP/runtime inspection tools usable from Codex.

The audit should inform implementation details without blocking the high-level milestone-one shape.

## Milestones

### Milestone 0: Audit And Scaffold Plan

- Read the relevant IWSDK docs/API surface.
- Decide exact scaffold command and project structure.
- Decide local server package shape.
- Decide Unity export package/tooling path.
- Write a short implementation plan.

### Milestone 1: Local Voice-To-Panorama Holodeck

Success criteria:

- `HeadsetHolodeckWeb` exists as a sibling project.
- IWSDK app runs locally.
- Exported Unity holodeck/arch shell appears at correct scale/orientation.
- UIKitML in-world UI is attached to/near the arch.
- Browser microphone recording works.
- Local server transcribes audio with OpenAI.
- Local server generates and polls a World Labs world.
- Frontend displays the returned panorama.
- User can reset to static holodeck.
- Error states are visible and recoverable.

### Milestone 2: Richer Rendering

- Evaluate World Labs-compatible web splat renderers.
- Add `SplatRenderer` behind the existing renderer boundary.
- Add `MeshRenderer` if the World Labs collider mesh is useful for view mode or interaction.
- Add view switching between static, panorama, splat, and mesh.

### Milestone 3: Spatial Interaction And MR

- Add hand/controller shortcuts for voice recording and reset.
- Add grab/physics interactions where appropriate.
- Add scene understanding/anchors for MR placement.
- Add passthrough/MR mode.
- Add spatial audio and richer command handling.

## Risks And Mitigations

- Unity asset export may not preserve all materials or animations. Mitigate with staging prefabs, export tests, and targeted web-side replacements.
- Browser microphone permission can behave differently on desktop and Quest. Test early on both.
- Local Quest testing may require HTTPS or port forwarding. Include this in setup docs.
- World Labs asset URLs may have CORS constraints. Proxy assets through the local server if needed.
- WebXR Layers may improve panorama quality but should not block the first renderer.
- Splat rendering is promising but not guaranteed until `.spz` compatibility is tested. Keep it behind `WorldRenderer`.
- Secrets must never be exposed to frontend code.

## Verification

- Unit-test local server wrappers with mocked OpenAI and World Labs responses.
- Smoke-test the real local API flow with configured keys.
- Test frontend state transitions for idle, recording, transcribing, generating, loading, ready, and error.
- Verify exported GLB scale/orientation/materials in desktop and Quest.
- Use IWSDK/IWER desktop emulation for quick WebXR interaction testing.
- Test on Quest Browser early for microphone permission, WebXR session start, performance, and comfort.
- Keep cached World Labs metadata/assets for repeatable local testing.

## Open Questions For Implementation Planning

- Which Unity GLTF exporter should be used if no exporter is already installed?
- Which IWSDK scaffold options enable the cleanest Spatial UI and debug workflow without Claude Code?
- Should the local server be a separate package or integrated into one monorepo dev command?
- Which panorama rendering path should milestone one start with: sphere/cube texture or WebXR Layers?
- Which World Labs-compatible web splat renderer should be evaluated first?
