# Hitem Object Generation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Connect the existing Headset Holodeck object-generation UI and voice intent to Hitem image-to-3D generation.

**Architecture:** Add a small provider abstraction with Hitem as the first provider. The runtime service submits the current captured/selected image, polls Hitem, downloads a GLB, imports it with glTFast, places it near the user, and passes it through the existing interactable object wrapper.

**Tech Stack:** Unity 6.2 C#, UnityWebRequest, Newtonsoft.Json, glTFast, existing `HeadsetCameraCaptureService`, `ObjectPlacementController`, and `ArchStatusBus`.

---

### Task 1: Provider Contract And Hitem Client

**Files:**
- Create: `Assets/App/Scripts/Direct/ObjectGeneration/ObjectGenerationModels.cs`
- Create: `Assets/App/Scripts/Direct/ObjectGeneration/IObjectGenerationProvider.cs`
- Create: `Assets/App/Scripts/Direct/ObjectGeneration/HitemObjectGenerationProvider.cs`

- [x] Define request/result types for image-to-3D.
- [x] Implement Hitem Basic-auth token request using `HITEM_ACCESS_KEY` and `HITEM_SECRET_KEY`.
- [x] Submit multipart `/open-api/v1/submit-task` requests with one encoded image and `format=2` for GLB.
- [x] Poll `/open-api/v1/query-task` until `success` or `failed`.
- [x] Download the returned model URL as bytes.

### Task 2: Runtime Orchestrator

**Files:**
- Create: `Assets/App/Scripts/Direct/ObjectGeneration/ObjectGenerationService.cs`

- [x] Resolve the Hitem provider and current capture service.
- [x] Prevent overlapping object-generation jobs.
- [x] Import the returned GLB bytes using glTFast.
- [x] Place the imported object 2 meters in front of the user by default.
- [x] Call `ObjectPlacementController.WrapExistingGeometry()` so the generated object becomes interactable.

### Task 3: Wire Existing UI And Voice Stubs

**Files:**
- Modify: `Assets/App/Command/SpeechIntent/Runtime/WorldActionDispatcher.cs`
- Modify: `Assets/App/Command/SpeechIntent/Runtime/UI/HeadsetCameraPreviewPanel.cs`
- Modify: `Assets/App/Command/SpeechIntent/Runtime/UI/ImageSearchPanel.cs`
- Modify: `Assets/App/Scripts/Direct/ObjectGenerationApiConfig.cs`

- [x] Replace “object creator not connected” with calls into `ObjectGenerationService`.
- [x] Keep API-key-disabled button behavior.
- [x] Update config checks to recognize Hitem access/secret credentials.

### Task 4: Verify

**Files:**
- Unity project compile and batch run.

- [ ] Run Unity batch compilation.
- [ ] Fix compile errors.
- [ ] Report any runtime/API behavior that cannot be verified without live Hitem credentials.
