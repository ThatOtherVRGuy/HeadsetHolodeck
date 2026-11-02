# Headset Holodeck Project Learning Log

This log records bug fixes, implementation lessons, and feature decisions in a compact form that can be reused by the web-based project or future agents. Add a new entry whenever a fix or feature reveals a reusable pattern, integration gotcha, provider behavior, or verification technique.

## Entry template

```md
## YYYY-MM-DD — Short title

- Area:
- Symptoms:
- Root cause:
- Fix / decision:
- Verification:
- Files:
- Reuse notes for web project:
```

## 2026-07-08 — Prefer textured 3dAIStudio Tripo assets over geometry-only GLBs

- Area: Runtime generated objects; 3dAIStudio Tripo text-to-3D/image-to-3D provider.
- Symptoms: Generated objects arrived as valid meshes but rendered gray, suggesting the object model loaded successfully while texture/material data was missing or ignored.
- Root cause: 3dAIStudio/Tripo can expose multiple model-like URLs in a finished task response. The previous URL selection path could accept the first GLB/model candidate instead of scoring all candidates for textured/PBR/final asset hints. If a geometry-only GLB appeared first, the app could download and cache an untextured model even though a textured model was also available.
- Fix / decision: Keep using Tripo for now. Harden `ThreeDAIStudioObjectGenerationProvider` so raw response URL scoring runs before simple structured fields, structured `assets/results` arrays score all model candidates, textured/PBR/material/final candidates receive a higher score, and untextured/no-texture/geometry-only candidates are penalized.
- Verification: Added an editor regression test with a simulated 3dAIStudio response containing `geometry_only_model.glb` first and `textured_pbr_model.glb` second. Confirmed the test failed before the fix and the textured URL assertion passed afterward. Also generated a `treasure chest` in the Unity Editor and confirmed it arrived textured. The newest cached GLB contained embedded JPEG color, ORM, and normal images referenced by `baseColorTexture`, `metallicRoughnessTexture`, and `normalTexture`.
- Files:
  - `Assets/App/Scripts/Direct/ObjectGeneration/ThreeDAIStudioObjectGenerationProvider.cs`
  - `Assets/App/Editor/CachedObjectBatchTests.cs`
- Reuse notes for web project: When consuming provider responses, do not blindly use the first model URL. Prefer explicit textured/PBR/final assets, inspect all result candidates, and preserve enough diagnostics to tell whether a downloaded GLB contains embedded images and material texture references. If a generated mesh is gray, first distinguish “provider returned untextured asset” from “renderer/import path dropped textures.”

## 2026-07-08 — Repair Quest generated-object texture binding and default physics

- Area: Runtime generated objects; Quest GLB import/rendering; generated object physics.
- Symptoms: A generated penny-farthing looked gray/untextured in-headset and quickly fell through the scene. Pulling the Quest cache showed the GLB itself was valid and contained embedded color/ORM/normal images, so the provider download was not the immediate problem.
- Root cause: The runtime path could instantiate valid GLB geometry while imported Unity materials ended up without a usable base-color texture on Quest, likely due to material/shader binding differences or stripping. Separately, generated imports could rely on child colliders/rigidbodies or missing colliders instead of guaranteeing a root physics body that collides with the scene.
- Fix / decision: Add generated-object diagnostics at import phases, salvage the first embedded GLB `baseColorTexture` directly from the GLB when imported materials lack a usable texture, and replace those materials with a simple textured URP/Standard-compatible material. Force generated objects to receive a root bounding-box `BoxCollider` plus root `Rigidbody` with `detectCollisions` enabled while preserving any child colliders/rigidbodies from the importer.
- Verification: Pulled the Quest screenshot and cached GLB. Confirmed the screenshot looked gray while the cached GLB contained embedded texture images. Added targeted Unity batch tests for root generated-object physics and embedded GLB base-color texture extraction; both passed. The broader cached-object suite still has an unrelated dispatcher placement expectation failure.
- Files:
  - `Assets/App/Scripts/Direct/ObjectGeneration/GeneratedObjectGlbTextureRepair.cs`
  - `Assets/App/Scripts/Direct/ObjectGeneration/ObjectGenerationService.cs`
  - `Assets/App/Command/SpeechIntent/Runtime/InteractableObjectWrapper.cs`
  - `Assets/App/Editor/CachedObjectBatchTests.cs`
- Reuse notes for web project: For runtime-generated GLBs, treat “gray mesh” as a two-stage diagnostic: first verify the cached GLB actually contains images/material texture references, then verify the runtime renderer binds them. A robust fallback can parse embedded GLB image bufferViews and reapply the base-color texture to a known-good runtime material. Generated objects that use gravity should also get an explicit root collider/rigidbody so provider meshes do not immediately fall through or miss collision.

## 2026-07-16 — Surface OpenAI quota/API failures to the user

- Area: Voice command routing; OpenAI transcription/intent errors; headset status UX.
- Symptoms: The wake/listen pipeline reached `VoiceCommandRouter`, but commands appeared to do nothing. Device log showed OpenAI transcription returning HTTP 429 with `insufficient_quota`.
- Root cause: Raw OpenAI failures were logged and routed as generic errors, but they were not converted into concise user-facing speech/status messages. The user could not tell that command processing was blocked by account quota/billing or other OpenAI API-side failures.
- Fix / decision: Normalize OpenAI-related failures in `VoiceCommandRouter.EmitError`. Quota, rate limit, API-key/auth, and schema/request-format errors now become short human-readable messages. These are posted to `ArchStatusBus` with mode `OPENAI`, emitted through `onError`, and spoken through `onAssistantResponse` for OpenAI-related failures while preserving raw details in the Unity log. Real OpenAI failures now preserve retry/reset headers when available. The router supports exact typed diagnostics, `simulate openai quota error` and `simulate openai rate limit`, to test the status/TTS path without spending API credit or waiting for a live failure. The LCARS operations panel also creates dev diagnostic buttons for `OPENAI QUOTA` and `OPENAI RATE` when a `VoiceCommandRouter` is present.
- Verification: Added batch-test helpers covering the exact `HTTP/1.1 429 Too Many Requests` / `insufficient_quota` payload seen in logcat and a `retry-after` rate-limit payload. Ran `git diff --check` on the changed files and checked the Unity editor log tail for compile errors.
- Files:
  - `Assets/App/Command/SpeechIntent/Runtime/VoiceCommandRouter.cs`
  - `Assets/App/Command/SpeechIntent/Runtime/OpenAiSpeechIntentService.cs`
  - `Assets/App/Command/SpeechIntent/Runtime/UI/ArchOperationsPanel.cs`
  - `Assets/App/Editor/SpeechIntentBatchTests.cs`
- Reuse notes for web project: Provider/API failures should be classified at the boundary between backend/provider code and user UX. Keep raw provider diagnostics in logs, but show/say actionable summaries such as “quota exhausted,” “rate limited,” “invalid API key,” or “request schema rejected.” Silent cloud failures feel like broken voice recognition even when local wake/listen worked correctly.

## 2026-07-16 — Semantic test rig for gaze/hand pointing

- Area: Automated spatial-command testing; gaze/hand/controller pointing semantics.
- Symptoms: Hand-authored `SpatialSnapshot` mocks could test resolver logic, but they did not exercise the real rig-to-ray-to-hit path used by headset pointing. This made it harder to verify “this,” “that,” “it,” held-object priority, and floor-vs-object hit behavior as an integrated system.
- Root cause: The runtime had a good `SpatialContextProvider` and `PointingSource` abstraction, but no deterministic scene-local way to drive those sources without live XR hardware.
- Fix / decision: Add a semantic `TestRig_Me` harness. `TestRigScenario` records intent-level pose data such as gaze target, right-hand target, left-hand target, held object, and select state. `HolodeckRigTestHarness` procedurally poses a reusable test rig, temporarily swaps the active `Me` identity, wires the real `SpatialContextProvider` to test `PointingSource`s, applies held-object state to `SceneFocusContext`, captures snapshots, and restores the real rig afterward. An editor setup menu, `Holodeck/Testing/Setup Semantic Test Rig`, creates/wires the disabled scene rig and saves a prefab at `Assets/App/Prefabs/TestRig_Me.prefab`.
- Verification: Added editor batch tests proving the harness can aim a right-hand ray through `SpatialContextProvider` to hit a cube, safely swaps/restores the `Me` identity, and preserves held-object priority over a pointed object through `SceneEntityResolver`. Ran `git diff --check` and checked the Unity editor log for compile errors.
- Files:
  - `Assets/App/Command/SpeechIntent/Runtime/Testing/TestRigScenario.cs`
  - `Assets/App/Command/SpeechIntent/Runtime/Testing/HolodeckRigTestHarness.cs`
  - `Assets/App/Editor/HolodeckRigTestHarnessSetup.cs`
  - `Assets/App/Editor/SpeechIntentBatchTests.cs`
- Reuse notes for web project: Prefer semantic scenario records over full hierarchy transform recordings for command/selection tests. Store “right hand aims at cube” or “user is holding blaster,” then procedurally pose the rig. Keep exact transform playback as an escape hatch for gesture/finger-shape tests where the skeleton pose itself is the subject.

## 2026-07-16 — Set explicit Quest launcher app label

- Area: Android/Quest manifest; Horizon launcher app identity.
- Symptoms: The Quest/Horizon launcher/home UI showed `App name unknown` for the APK even though Unity `ProjectSettings` had `productName: HeadsetHolodeck`.
- Root cause: The project uses a custom `Assets/Plugins/Android/AndroidManifest.xml`. Its `<application>` and launcher `<activity>` entries did not declare `android:label`, so the generated APK could lack the display label that Horizon reads for launcher surfaces.
- Fix / decision: Set `android:label="HeadsetHolodeck"` on both the manifest `<application>` and the `UnityPlayerGameActivity` launcher activity.
- Verification: Pending next Android build/install on Quest; expected result is that the Horizon launcher shows `HeadsetHolodeck` instead of `App name unknown`.
- Files:
  - `Assets/Plugins/Android/AndroidManifest.xml`
- Reuse notes for web project: If an Android/XR shell shows an unknown app name, check the final merged manifest and not just Unity `productName`. Custom manifests can bypass Unity’s generated label unless `android:label` is explicitly present on the app and/or launch activity.

## 2026-07-16 — Resolve “what is this” from all actionable rays before asking ambiguity

- Area: Spatial command target resolution; gaze/hand pointing; conversational focus.
- Symptoms: Holding an object could answer `what is this`, but directly gazing at and pointing both hands at an object sometimes produced `Which object?`.
- Root cause: The indicated-target resolver chose one preferred hand hit and then fell back to gaze. If that preferred hand ray hit a non-actionable support collider such as the floor/shell while another hand ray or gaze had the actual object, resolution could fail. Gaze fallback also used a plain first-hit raycast instead of scanning all hits for the first actionable collider.
- Fix / decision: Resolve indicated targets by checking held objects first, then actionable hits from the allowed hand rays, then raw hand hits, then gaze. Gaze now uses the same sorted `RaycastAll` / first-actionable policy as hand snapshots, so support colliders are skipped when an object lies behind them.
- Verification: Added batch regressions for right-hand support hit plus left-hand actionable hit, and for gaze skipping a nearer support collider to identify a cube. Ran `SpeechIntentBatchTests.RunDistanceAndMovementTests` in Unity batchmode; it passed.
- Files:
  - `Assets/App/Command/SpeechIntent/Runtime/SceneFocusContext.cs`
  - `Assets/App/Command/SpeechIntent/Runtime/SceneEntityResolver.cs`
  - `Assets/App/Editor/SpeechIntentBatchTests.cs`
- Reuse notes for web project: Do not equate “first physics hit” with “user-selected object” in spatial UIs. Preserve support/floor hits for placement commands like “over there,” but target-selection commands like “this/that” should scan all ray hits and prefer actionable/trackable objects across every available pointer before asking a clarification question.

## 2026-08-15 — Restore world transform before activating saved spawn 1

- Area: Saved-world restoration; Gaussian-splat activation; XR player spawn.
- Symptoms: Loading an existing world could expose the loader's provisional position/rotation/scale before `world.json` was restored. The `OnWorldLoaded` event then teleported the user while the root was still untransformed, so the first saved spawn could be interpreted against the wrong world root or visibly jump.
- Root cause: `WorldConfigRestorer` waited for the generic loaded event before applying `world_transform`; that same event is consumed by `PlayerOriginController` to activate the spawn. Cached splat loading also allowed a frame between renderer creation and external-world registration.
- Fix / decision: Add a pre-activation hook to `WorldLabsWorldManager` and an equivalent pre-renderer hook to `RuntimeSplatFloorLoader` / `LocalRemoteSplatLoader`. During a saved-world restore, apply `world_transform` first, then reproject saved spawn point 1 into that transformed root, then create/activate the renderer and issue the public loaded event. `PlayerOriginController` now reads `SplatSpawnPose.TryGetWorldPose`, so estimated poses also follow any subsequently restored root transform.
- Verification: Added `TestRestoredWorldTransformPrecedesFirstSavedSpawn`, which starts with a deliberately incorrect root transform and provisional pose, restores a scaled/rotated/translated world plus spawn 1, and verifies both the root transform and resolved world-space spawn. Ran `SplatSpawnAndOrientationBatchTests.RunAll` in Unity batchmode; it passed.
- Files:
  - `Assets/App/Save/WorldConfigRestorer.cs`
  - `Assets/App/Scripts/Direct/RuntimeSplatFloorLoader.cs`
  - `Assets/App/Command/SpeechIntent/Runtime/LocalRemoteSplatLoader.cs`
  - `Packages/com.worldlabs.gaussian-splatting/Runtime/WorldLabs/WorldLabsWorldManager.cs`
  - `Assets/App/Command/SpeechIntent/Runtime/PlayerOriginController.cs`
  - `Assets/App/Editor/SplatSpawnAndOrientationBatchTests.cs`
- Reuse notes for web project: Treat a saved world transform and saved player pose as one ordered restoration transaction. Apply the world root transform before deriving/applying player pose, and do so before making the renderer observable to scene/UI listeners; otherwise event order produces transient visual jumps and stale local-to-world spawn conversions.

## 2026-08-15 — “Load <name>” is always a local saved-world lookup

- Area: Voice intent routing; saved-world loading; World Labs generation safety.
- Symptoms: A spoken request such as `load Cozy Christmas Cabin` could depend on the AI intent classifier returning `LoadWorldConfig`. If it instead classified the phrase as `GenerateWorld`, the app could send an existing-world name to World Labs as a new generation prompt.
- Root cause: The dispatcher already restored matching `world.json` configs, but local parsing did not reserve the `load <name>` verb, and router fallback logic did not override an otherwise successful but incorrect generation intent.
- Fix / decision: Parse `load <name>` and `load world <name>` locally as `LoadWorldConfig`. Immediately after speech transcription, `OpenAiSpeechIntentService` now returns that local command before making the intent-model request. `VoiceCommandRouter` enforces the original transcript on the command and retains its post-interpretation override. `WorldActionDispatcher` adds a final guard that reroutes any surviving `GenerateWorld` load transcript to local lookup. The dispatcher waits for the local config scan, matches normalized saved display names (exact, prefix, then contained), and restores the match. A miss logs the available local world names, reports `No saved world named …`, and explicitly does not start World Labs generation. Restoration itself retains its existing cache-first behavior for saved splats/panoramas.
- Verification: Added speech-intent regressions that parse `load Cozy Christmas Cabin`, verify it overrides a deliberately incorrect `GenerateWorld` result, and verify `Load Japanese Garden` is intercepted by the pre-intent API guard with the requested name intact. Ran `SpeechIntentBatchTests.RunDistanceAndMovementTests` in Unity batchmode; it passed.
- Files:
  - `Assets/App/Command/SpeechIntent/Runtime/LocalTypedIntentParser.cs`
  - `Assets/App/Command/SpeechIntent/Runtime/VoiceCommandRouter.cs`
  - `Assets/App/Command/SpeechIntent/Runtime/WorldActionDispatcher.cs`
  - `Assets/App/Save/Runtime/WorldConfigStore.cs`
  - `Assets/App/Editor/SpeechIntentBatchTests.cs`
- Reuse notes for web project: Reserve destructive or expensive verbs locally where possible. For `load`, always query saved/local content first and treat a missing name as an explicit miss; never silently reinterpret it as permission to create or purchase a new cloud resource.

## 2026-08-16 — Voice/UI snapping and direct one-/two-hand world manipulation

- Area: World transforms; hand/controller interaction; voice commands; LCARS Ops UI.
- Symptoms: World translation/rotation had no configurable grid or angle increment, and users could only change a world through discrete transform commands. There was no direct whole-world grab gesture or two-hand sizing workflow.
- Root cause: Object interaction already had grab semantics, but the loaded world root was not connected to a persistent manipulation session. Hand-only grip events and controller grip state also entered the app through different layers.
- Fix / decision: Add `WorldManipulationController`, armed by `grab world` or the Ops-panel `GRAB WORLD` button. The dominant hand preserves the world-to-hand offset for direct move/rotate. A grip from the other hand upgrades the session to midpoint translation, vector rotation, and uniform two-hand scaling while preserving any pre-existing non-uniform/orientation correction scale. `release world` commits the final world transform through `WorldConfigAutoSave`. Position and angle snap settings persist in `PlayerPrefs`, parse locally before any AI request (`snap to 10 centimeters`, `snap to 30 degrees`), and are exposed as conventional Ops-panel buttons alongside release and dominant-hand selection. A separate persisted master state supports `snap on` / `enable snap` and `snap off` / `disable snap`, with matching `SNAP ON` / `SNAP OFF` buttons; disabling preserves the selected increments for later re-enable. The same settings are scene-wide: `TargetTransformController` snaps arbitrary voice-moved/rotated objects, and `SnapWhileGrabbed` post-processes XRI-grabbed objects after the interactor updates them. Runtime-wrapped objects receive that component immediately; existing XR grab interactables are upgraded when the settings component starts or changes.
- Verification: Added parser/preflight, schema, and snap-math regressions. Ran `SpeechIntentBatchTests.RunDistanceAndMovementTests` in Unity 6000.2.10f1 batchmode; all tests passed. `git diff --check` passed.
- Files:
  - `Assets/App/Command/SpeechIntent/Runtime/WorldManipulationController.cs`
  - `Assets/App/Command/SpeechIntent/Runtime/SnapWhileGrabbed.cs`
  - `Assets/App/Command/SpeechIntent/Runtime/InteractableObjectWrapper.cs`
  - `Assets/App/Command/SpeechIntent/Runtime/TargetTransformController.cs`
  - `Assets/App/Command/SpeechIntent/Runtime/HandInput/HandOnlyInputCoordinator.cs`
  - `Assets/App/Command/SpeechIntent/Runtime/LocalTypedIntentParser.cs`
  - `Assets/App/Command/SpeechIntent/Runtime/VoiceIntentSchemas.cs`
  - `Assets/App/Command/SpeechIntent/Runtime/OpenAiSpeechIntentService.cs`
  - `Assets/App/Command/SpeechIntent/Runtime/OpenAiSpeechIntentConfig.cs`
  - `Assets/App/Command/SpeechIntent/Runtime/WorldActionDispatcher.cs`
  - `Assets/App/Command/SpeechIntent/Runtime/UI/ArchOperationsPanel.cs`
  - `Assets/App/Editor/SpeechIntentBatchTests.cs`
- Reuse notes for web project: Model one- and two-pointer manipulation as a single latched session. Preserve the initial object/world transform, compute two-pointer translation from the midpoint, rotation from the start/current pointer vectors, and scale from their distance ratio. Multiply the original scale uniformly instead of replacing it, because imported spatial content may use non-uniform scale for coordinate-system correction. Keep user-facing unit parsing local and store all internal distances in meters.

## 2026-09-13 — Documentation drifted from behaviour; reconcile with an executable claim audit

- Area: Setup and architecture documentation; credential boundary; object generation; hand input; wake words.
- Symptoms: Documentation contradicted the shipped code in ways that would mislead a new tester. `AGENTS.md` still described a hardcoded OpenAI key and a `StreamingAssets/.env` copy that no longer exists, and listed three stale package versions; `README_Holodeck_DirectPlugin.md` repeated the same credential story; `USER_MANUAL.md` said object generation from image was "not connected" after two providers shipped; the wake-word alias list looked like an ASR bug when it is intentional; some docs understated hand-only input and two-hand world manipulation.
- Root cause: the docs were written in different eras. Design plans and handoffs kept their original text, no single document owned "current verified state", and nothing executed the docs against the code, so drift was invisible until someone read carefully.
- Fix / decision: give each kind of document one job. `docs/stabilization/STATUS.md` owns verified state and the order queue; `handoff-NN.md` files stay untouched as historical records and are superseded by reference from a new index handoff; `README.md` owns the single tester setup path (device credential intent → `adb push .env` → install → wake → cached load → save → restart); `AGENTS.md` owns the architecture map for agents. Mark stale-but-historical files (`README_Holodeck_DirectPlugin.md`, `spz-renderer-handoff-notes.md`, `docs/superpowers/plans/`) as superseded/historical instead of rewriting them, and add a plans README stating that unchecked boxes are not a backlog. Back the whole thing with a re-runnable harness that asserts each documented claim against the code (`docs/stabilization/evidence-06/docs-claim-audit.sh`), so future drift fails a check instead of surviving review.
- Verification: ran the audit in this checkout. Before the edits it reported 17 failures, exactly the stale points above (three package versions, the `StreamingAssets/.env` claim, missing device/alias/object-generation/hand-interaction statements, plans-index gap). After the edits it passes with 0 failures across 84 checks, and the Markdown link check reports one pre-existing broken reference in `handoff-05b-device-env.md` (`evidence-05b/cred-e2e-02-results.txt`; the retained file is `cred-e2e-02-summary.json`), which is recorded rather than edited because handoffs stay as-is.
- Files:
  - `AGENTS.md`, `README.md`, `USER_MANUAL.md`, `README_Holodeck_DirectPlugin.md`, `spz-renderer-handoff-notes.md` (`.env.example` was already correct from order 05b and was not changed)
  - `docs/superpowers/plans/README.md`
  - `docs/stabilization/handoff-06.md`, `docs/stabilization/STATUS.md`, `docs/stabilization/evidence-06/`
- Reuse notes for web project: keep one "verified state" document and make every other setup/architecture doc defer to it. When docs and code disagree, fix the doc that owns setup and mark the older narrative as superseded by link rather than rewriting history. Better: encode the claims a doc makes as an executable check (version pins, paths, provider lists, allowlists, alias sets), because a documentation audit that runs is the only one that stays true. Never print credential values in such a check — compare by key name and count matches.
