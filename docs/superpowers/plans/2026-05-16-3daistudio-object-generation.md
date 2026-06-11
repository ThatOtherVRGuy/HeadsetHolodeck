# 3dAIStudio Object Generation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add 3dAIStudio Tripo as a second object-generation provider and select providers by capability.

**Architecture:** Extend the existing object-generation abstraction with capabilities and provider IDs. Add a 3dAIStudio Tripo image-to-3D provider using Bearer auth, then update `ObjectGenerationService` to choose 3dAIStudio first and Hitem second for image-to-3D.

**Tech Stack:** Unity 6.2 C#, UnityWebRequest, Newtonsoft.Json, glTFast, existing `ObjectGenerationService`, `HeadsetCameraCaptureService`, and `ArchStatusBus`.

---

### Task 1: Capability-Aware Provider Contract

**Files:**
- Modify: `Assets/App/Scripts/Direct/ObjectGeneration/ObjectGenerationModels.cs`
- Modify: `Assets/App/Scripts/Direct/ObjectGeneration/IObjectGenerationProvider.cs`
- Modify: `Assets/App/Scripts/Direct/ObjectGeneration/HitemObjectGenerationProvider.cs`

- [x] Add `ObjectGenerationCapability` with `ImageTo3D` and `TextTo3D`.
- [x] Add `ObjectGenerationProviderId` with `Auto`, `Hitem`, and `ThreeDAIStudioTripo`.
- [x] Add `SupportsCapability(ObjectGenerationCapability capability)` to the provider contract.
- [x] Make Hitem report support for `ImageTo3D` only.

### Task 2: 3dAIStudio Tripo Provider

**Files:**
- Create: `Assets/App/Scripts/Direct/ObjectGeneration/ThreeDAIStudioObjectGenerationProvider.cs`

- [x] Read `THREEDAISTUDIO_API_KEY` and optional `THREEDAISTUDIO_BASE_URL`.
- [x] Submit base64 JPEG data URI to `/v1/3d-models/tripo/image-to-3d/`.
- [x] Poll `/v1/generation-request/<task_id>/status/`.
- [x] Download the returned GLB asset.

### Task 3: Provider Selection And Config

**Files:**
- Modify: `Assets/App/Scripts/Direct/ObjectGeneration/ObjectGenerationService.cs`
- Modify: `Assets/App/Scripts/Direct/ObjectGenerationApiConfig.cs`
- Modify: `.env.example`
- Modify: `Assets/App/Editor/HeadsetHolodeckInstallValidator.cs`

- [x] Add provider mode field to `ObjectGenerationService`.
- [x] Prefer 3dAIStudio Tripo over Hitem in `Auto`.
- [x] Keep explicit provider selection available from the Inspector.
- [x] Include 3dAIStudio key checks in runtime config and install validation.

### Task 4: Verify

**Files:**
- Unity project compile and batch run.

- [ ] Run Unity batch compilation.
- [ ] Fix compile errors.
- [ ] Report that live API generation was not run unless the user asks to spend credits.
