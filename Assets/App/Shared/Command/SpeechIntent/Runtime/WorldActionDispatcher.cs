using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using WorldLabs.API;      // MarbleModel
using WorldLabs.Runtime;  // WorldBrowserController
using Holodeck.Direct;    // VoiceToWorldLabsPluginCoordinator
using Holodeck.Save;
using SpeechIntent.Audio;

namespace SpeechIntent
{
    public class WorldActionDispatcher : MonoBehaviour
    {
        [Header("Scene Controllers")]
        public LightRigController lightRig;
        public LightActionController lightActionController;
        public UiPanelController uiPanels;
        public ObjectPlacementController objectPlacement;
        public StaticWorldController staticWorldController;
        public TargetTransformController targetTransformController;
        public InteractionMemory interactionMemory;
        public ViewModeController viewModeController;
        public PlayerOriginController playerOriginController;
        public LocalRemoteSplatLoader splatLoader;
        public LocalRemotePanoLoader  panoLoader;
        public WorldMeshController worldMeshController;
        public AudioWorldActionController audioWorldActionController;
        public MaterialTargetController materialTargetController;
        public RuntimeProxyVisibilityController proxyVisibilityController;
        public SpeechIntent.Behaviors.BehaviorCommandController behaviorCommandController;
        public VoiceToWorldLabsPluginCoordinator coordinator;
        public WorldBrowserController worldBrowser;
        public HeadsetCameraCaptureService headsetCameraCapture;
        public ObjectGenerationService objectGenerationService;
        public CachedObjectStore cachedObjectStore;
        public CachedObjectChoiceController cachedObjectChoiceController;
        public CachedObjectChoicePanel cachedObjectChoicePanel;
        public ImageSearchPanel imageSearchPanel;
        public WorldViewCaptureService worldViewCaptureService;
        public WorldManipulationController worldManipulationController;

        [Header("Delete / Indicated Target")]
        public LayerMask deleteGazeRaycastMask = ~0;
        [Range(0.1f, 100f)] public float deleteGazeMaxDistance = 25f;
        public QueryTriggerInteraction deleteGazeTriggerInteraction = QueryTriggerInteraction.Ignore;

        [Header("Save System")]
        public WorldConfigStore    worldConfigStore;
        public WorldConfigRestorer worldConfigRestorer;
        public WorldConfigAutoSave worldConfigAutoSave;

        [Header("Inspector Hooks")]
        public StringEvent onGenerateWorldPrompt;
        public UnityEvent onSwitchToStaticWorld;
        public StringEvent onUnhandledAction;

        /// <summary>Fired after PlaceObject, MoveTarget, ScaleTarget, RotateTarget, ResetTransform.
        /// First arg = the command; second = the affected GameObject.</summary>
        public event Action<VoiceIntentCommand, GameObject> OnObjectMutated;

        bool _syncingGenerationModel;

        private void Awake()
        {
            if (audioWorldActionController == null)
                audioWorldActionController = GetComponent<AudioWorldActionController>()
                                             ?? FindFirstObjectByType<AudioWorldActionController>();
            if (materialTargetController == null)
                materialTargetController = GetComponent<MaterialTargetController>()
                                           ?? FindFirstObjectByType<MaterialTargetController>();
            if (lightActionController == null)
                lightActionController = GetComponent<LightActionController>()
                                        ?? FindFirstObjectByType<LightActionController>();
            if (lightActionController == null)
                lightActionController = gameObject.AddComponent<LightActionController>();
            if (proxyVisibilityController == null)
                proxyVisibilityController = GetComponent<RuntimeProxyVisibilityController>()
                                            ?? FindFirstObjectByType<RuntimeProxyVisibilityController>();
            if (proxyVisibilityController == null)
                proxyVisibilityController = gameObject.AddComponent<RuntimeProxyVisibilityController>();
            if (objectGenerationService == null)
                objectGenerationService = FindFirstObjectByType<ObjectGenerationService>(FindObjectsInactive.Include);
            if (cachedObjectStore == null)
                cachedObjectStore = FindFirstObjectByType<CachedObjectStore>(FindObjectsInactive.Include);
            if (cachedObjectChoiceController == null)
                cachedObjectChoiceController = FindFirstObjectByType<CachedObjectChoiceController>(FindObjectsInactive.Include);
            if (cachedObjectChoicePanel == null)
                cachedObjectChoicePanel = FindFirstObjectByType<CachedObjectChoicePanel>(FindObjectsInactive.Include);
            if (behaviorCommandController == null)
                behaviorCommandController = GetComponent<SpeechIntent.Behaviors.BehaviorCommandController>()
                                            ?? FindFirstObjectByType<SpeechIntent.Behaviors.BehaviorCommandController>(FindObjectsInactive.Include);
            if (behaviorCommandController == null)
                behaviorCommandController = gameObject.AddComponent<SpeechIntent.Behaviors.BehaviorCommandController>();
            if (worldManipulationController == null)
                worldManipulationController = GetComponent<WorldManipulationController>()
                                              ?? FindFirstObjectByType<WorldManipulationController>(FindObjectsInactive.Include);
            if (worldManipulationController == null)
                worldManipulationController = gameObject.AddComponent<WorldManipulationController>();

            if (behaviorCommandController.entityResolver == null && targetTransformController != null)
                behaviorCommandController.entityResolver = targetTransformController.entityResolver;
            if (behaviorCommandController.interactionMemory == null)
                behaviorCommandController.interactionMemory = interactionMemory;
            if (behaviorCommandController.spatialContextProvider == null)
                behaviorCommandController.spatialContextProvider = FindFirstObjectByType<SpatialContextProvider>(FindObjectsInactive.Include);
        }

        void OnEnable()
        {
            ResolveGenerationModelControllers();
            EnsureModelModeRadioGroups();

            if (worldBrowser != null)
                worldBrowser.OnGenerationModelChanged += HandleBrowserGenerationModelChanged;
            if (coordinator != null)
                coordinator.OnGenerationModelChanged += HandleCoordinatorGenerationModelChanged;
        }

        void OnDisable()
        {
            if (worldBrowser != null)
                worldBrowser.OnGenerationModelChanged -= HandleBrowserGenerationModelChanged;
            if (coordinator != null)
                coordinator.OnGenerationModelChanged -= HandleCoordinatorGenerationModelChanged;
        }

        void ResolveGenerationModelControllers()
        {
            if (worldBrowser == null)
                worldBrowser = FindFirstObjectByType<WorldBrowserController>();
            if (coordinator == null)
                coordinator = FindFirstObjectByType<VoiceToWorldLabsPluginCoordinator>();
        }

        void EnsureModelModeRadioGroups()
        {
            RectTransform[] rects = FindObjectsByType<RectTransform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (RectTransform rect in rects)
            {
                if (rect == null || rect.name != "ModelSelectorRow")
                    continue;

                ModelModeRadioGroup group = rect.GetComponent<ModelModeRadioGroup>();
                if (group == null)
                    group = rect.gameObject.AddComponent<ModelModeRadioGroup>();

                group.coordinator = coordinator;
                group.worldBrowser = worldBrowser;
                group.worldManager = FindFirstObjectByType<WorldLabsWorldManager>(FindObjectsInactive.Include);
                group.worldConfigAutoSave = worldConfigAutoSave;
                group.Refresh();
            }
        }

        public void Execute(VoiceIntentCommand command, SpatialSnapshot spatial)
        {
            if (command == null)
            {
                Debug.LogWarning("Dispatcher received a null command.");
                return;
            }

            // A "save as" that collided with an existing world is waiting on an answer. Anything that
            // is not overwrite/cancel is treated as a new command, and the question is dropped rather
            // than left to ambush a later utterance.
            if (!string.IsNullOrEmpty(_pendingSaveAsName))
            {
                if (TryResolvePendingSaveAs(command))
                    return;
            }

            if (command.intent == VoiceIntentType.AskClarification || !command.should_execute)
            {
                Debug.Log($"Command not executed. Intent={command.intent}, Reason={command.reason}");
                return;
            }

            switch (command.intent)
            {
                case VoiceIntentType.DescribeTarget:
                    HandleDescribeTarget(command, spatial);
                    break;

                case VoiceIntentType.SetPositionSnap:
                    HandleSetPositionSnap(command);
                    break;

                case VoiceIntentType.SetAngleSnap:
                    HandleSetAngleSnap(command);
                    break;

                case VoiceIntentType.SetSnapEnabled:
                    HandleSetSnapEnabled(command);
                    break;

                case VoiceIntentType.SetWorldVolume:
                    HandleSetWorldVolume(command);
                    break;

                case VoiceIntentType.GrabWorld:
                    HandleGrabWorld(command);
                    break;

                case VoiceIntentType.GenerateWorld:
                    HandleGenerateWorld(command);
                    break;

                case VoiceIntentType.SwitchToStaticWorld:
                    HandleSwitchToStaticWorld();
                    break;

                case VoiceIntentType.ShowUi:
                    HandleShowUi(command);
                    break;

                case VoiceIntentType.SetSunDirection:
                    HandleSetSunDirection(command, spatial);
                    break;

                case VoiceIntentType.SetLightingPreset:
                    HandleSetLightingPreset(command);
                    break;

                case VoiceIntentType.CreateLight:
                    HandleCreateLight(command, spatial);
                    break;

                case VoiceIntentType.ModifyLight:
                    HandleModifyLight(command, spatial);
                    break;

                case VoiceIntentType.PlaceObject:
                    HandlePlaceObject(command, spatial);
                    break;

                case VoiceIntentType.SelectCachedObject:
                    HandleSelectCachedObject(command);
                    break;

                case VoiceIntentType.CancelGeneration:
                    HandleCancelGeneration(command);
                    break;

                case VoiceIntentType.ContinueGeneration:
                    HandleContinueGeneration(command);
                    break;

                case VoiceIntentType.MoveTarget:
                    HandleMoveTarget(command, spatial);
                    break;

                case VoiceIntentType.ScaleTarget:
                    HandleScaleTarget(command, spatial);
                    break;

                case VoiceIntentType.RotateTarget:
                    HandleRotateTarget(command, spatial);
                    break;

                case VoiceIntentType.ResetTransform:
                    HandleResetTransform(command, spatial);
                    break;

                case VoiceIntentType.AttachBehavior:
                case VoiceIntentType.StopBehavior:
                    HandleBehaviorCommand(command, spatial);
                    break;

                case VoiceIntentType.SetTargetMaterial:
                    HandleSetTargetMaterial(command, spatial);
                    break;

                case VoiceIntentType.ModifyPhysics:
                    HandleModifyPhysics(command, spatial);
                    break;

                case VoiceIntentType.SaveSpawnPoint:
                    HandleSaveSpawnPoint(command);
                    break;

                case VoiceIntentType.NextSpawnPoint:
                    HandleStepSpawnPoint(command, next: true);
                    break;

                case VoiceIntentType.PreviousSpawnPoint:
                    HandleStepSpawnPoint(command, next: false);
                    break;

                case VoiceIntentType.RemoveSpawnPoint:
                    HandleRemoveSpawnPoint(command);
                    break;

                case VoiceIntentType.RemoveAllSpawnPoints:
                    HandleRemoveAllSpawnPoints(command);
                    break;

                case VoiceIntentType.SuggestSpawnPoint:
                    HandleSuggestSpawnPoint(command);
                    break;

                case VoiceIntentType.LoadSplat:
                    HandleLoadSplat(command);
                    break;

                case VoiceIntentType.LoadPanorama:
                    HandleLoadPanorama(command);
                    break;

                case VoiceIntentType.Show3dWorld:
                    if (viewModeController != null)
                    {
                        if (!viewModeController.IsSplatAvailable)
                            command.spoken_response = "3D is not available for this world.";
                        viewModeController.RequestSplatView();
                    }
                    else
                        Debug.LogWarning("[WorldActionDispatcher] viewModeController is null.");
                    break;

                case VoiceIntentType.ShowPanoWorld:
                    if (viewModeController != null)
                    {
                        if (!viewModeController.IsPanoAvailable)
                            command.spoken_response = "Panorama is not available for this world.";
                        viewModeController.RequestPanoView();
                    }
                    else
                        Debug.LogWarning("[WorldActionDispatcher] viewModeController is null.");
                    break;

                case VoiceIntentType.SetGenerationModel:
                    HandleSetGenerationModel(command);
                    break;

                case VoiceIntentType.ShowMeshWorld:
                    if (viewModeController != null)
                        viewModeController.RequestMeshView(); // TODO: add RequestMeshView() to ViewModeController in Task 8
                    else
                        Debug.LogWarning("[WorldActionDispatcher] viewModeController is null.");
                    break;

                case VoiceIntentType.SaveWorldConfig:
                    HandleSaveWorldConfig(command);
                    break;

                case VoiceIntentType.LoadWorldConfig:
                    HandleLoadWorldConfig(command);
                    break;

                case VoiceIntentType.CreateAudioSource:
                    HandleCreateAudioSource(command, spatial);
                    break;

                case VoiceIntentType.ControlAudioSource:
                    HandleControlAudioSource(command);
                    break;

                case VoiceIntentType.DeleteTarget:
                    HandleDeleteTarget(command, spatial);
                    break;

                case VoiceIntentType.SetProxyVisibility:
                    HandleSetProxyVisibility(command);
                    break;

                case VoiceIntentType.QuitApplication:
                    HandleQuitApplication();
                    break;

                case VoiceIntentType.CaptureHeadsetCamera:
                    HandleCaptureHeadsetCamera();
                    break;

                case VoiceIntentType.ConfirmHeadsetCameraCapture:
                    HandleConfirmHeadsetCameraCapture();
                    break;

                case VoiceIntentType.GenerateWorldFromCapture:
                    HandleGenerateWorldFromCapture(command);
                    break;

                case VoiceIntentType.GenerateObjectFromCapture:
                    HandleGenerateObjectFromCapture(command);
                    break;

                case VoiceIntentType.SearchImages:
                    HandleSearchImages(command);
                    break;

                case VoiceIntentType.SelectImageSearchResult:
                    HandleSelectImageSearchResult();
                    break;

                case VoiceIntentType.NextImageSearchResult:
                    HandleStepImageSearchResult(next: true);
                    break;

                case VoiceIntentType.PreviousImageSearchResult:
                    HandleStepImageSearchResult(next: false);
                    break;

                case VoiceIntentType.CaptureWorldThumbnail:
                    HandleCaptureWorldThumbnail();
                    break;

                case VoiceIntentType.CaptureWorldPanorama:
                    HandleCaptureWorldPanorama();
                    break;

                default:
                    Debug.Log($"Unhandled or unknown command: {command.intent}");
                    onUnhandledAction?.Invoke(command.rawSummary());
                    break;
            }
        }

        public void RegisterGeneratedWorld(GameObject worldRoot)
        {
            if (interactionMemory != null)
            {
                interactionMemory.RegisterCurrentWorld(worldRoot);
            }
        }

        public void RegisterGeneratedWorldWithDescription(GameObject worldRoot, string worldDescription)
        {
            if (interactionMemory != null)
            {
                interactionMemory.RegisterCurrentWorld(worldRoot, worldDescription);
            }
        }

        void HandleSaveSpawnPoint(VoiceIntentCommand command)
        {
            if (playerOriginController == null)
                playerOriginController = FindFirstObjectByType<PlayerOriginController>(FindObjectsInactive.Include);

            bool saved = playerOriginController != null && playerOriginController.SaveCurrentSpawnPoint();
            command.spoken_response = saved
                ? "Saved spawn point."
                : "I could not save a spawn point. No active world is loaded.";
        }

        void HandleStepSpawnPoint(VoiceIntentCommand command, bool next)
        {
            if (playerOriginController == null)
                playerOriginController = FindFirstObjectByType<PlayerOriginController>(FindObjectsInactive.Include);

            bool moved = playerOriginController != null &&
                         (next ? playerOriginController.GoToNextSpawnPoint() : playerOriginController.GoToPreviousSpawnPoint());
            command.spoken_response = moved
                ? (next ? "Moved to next spawn point." : "Moved to previous spawn point.")
                : "No saved spawn points are available.";
        }

        void HandleRemoveSpawnPoint(VoiceIntentCommand command)
        {
            if (playerOriginController == null)
                playerOriginController = FindFirstObjectByType<PlayerOriginController>(FindObjectsInactive.Include);

            bool removed = playerOriginController != null && playerOriginController.RemoveCurrentSpawnPoint();
            command.spoken_response = removed
                ? "Removed spawn point."
                : "No saved spawn point is available to remove.";
        }

        void HandleRemoveAllSpawnPoints(VoiceIntentCommand command)
        {
            if (playerOriginController == null)
                playerOriginController = FindFirstObjectByType<PlayerOriginController>(FindObjectsInactive.Include);

            bool removed = playerOriginController != null && playerOriginController.RemoveAllSpawnPoints();
            command.spoken_response = removed
                ? "Removed all spawn points."
                : "No saved spawn points are available to remove.";
        }

        void HandleSuggestSpawnPoint(VoiceIntentCommand command)
        {
            if (playerOriginController == null)
                playerOriginController = FindFirstObjectByType<PlayerOriginController>(FindObjectsInactive.Include);

            bool moved = playerOriginController != null && playerOriginController.SuggestSpawnPoint();
            command.spoken_response = moved
                ? "Moved to a suggested spawn point."
                : "No estimated spawn point is available.";
        }

        private void HandleGenerateWorld(VoiceIntentCommand command)
        {
            // Last-line safety: a load phrase is never permission to create a new API
            // world, even if it somehow reaches the generation case.
            if (OpenAiSpeechIntentService.TryBuildLocalPreflightCommand(command?.transcript, out VoiceIntentCommand localLoad))
            {
                Debug.LogWarning($"[WorldActionDispatcher] Blocked GenerateWorld for load transcript '{command.transcript}'; routing to saved-world lookup.");
                HandleLoadWorldConfig(localLoad);
                command.spoken_response = localLoad.spoken_response;
                return;
            }

            // This is the final authorization boundary for voice world generation.
            // UI creation invokes the coordinator directly and therefore has no
            // transcript; every voiced request must express a clear intent to create
            // or travel into a new world.
            if (!(command?.user_confirmed ?? false) &&
                !string.IsNullOrWhiteSpace(command?.transcript) &&
                !LocalTypedIntentParser.IsExplicitWorldGenerationRequest(command.transcript))
            {
                Debug.LogWarning($"[WorldActionDispatcher] Blocked implicit world generation for transcript '{command.transcript}'.");
                command.spoken_response =
                    "I didn't understand that as a command. Say create a world, or load a saved world by name.";
                ArchStatusBus.Warning("World creation needs an explicit create or travel request.", "WORLD");
                return;
            }

            if (coordinator == null)
                coordinator = FindFirstObjectByType<VoiceToWorldLabsPluginCoordinator>(FindObjectsInactive.Include);
            if (coordinator != null && coordinator.IsBusy)
            {
                command.spoken_response = coordinator.BusyMessage;
                ArchStatusBus.Warning(coordinator.BusyMessage, "WORLD");
                return;
            }

            Debug.Log($"Generate world: {command.world_prompt}");
            onGenerateWorldPrompt?.Invoke(command.world_prompt);
        }

        private void HandleCaptureHeadsetCamera()
        {
            if (headsetCameraCapture == null)
                headsetCameraCapture = FindFirstObjectByType<HeadsetCameraCaptureService>();

            if (headsetCameraCapture == null)
            {
                Debug.LogWarning("[WorldActionDispatcher] CaptureHeadsetCamera: service not assigned.");
                ArchStatusBus.Warning("Headset camera capture service not assigned.", "CAPTURE");
                return;
            }

            FindFirstObjectByType<ArchOperationsPanel>(FindObjectsInactive.Include)?.ShowCameraCapture();
            headsetCameraCapture.BeginPreview();
        }

        private void HandleConfirmHeadsetCameraCapture()
        {
            if (headsetCameraCapture == null)
                headsetCameraCapture = FindFirstObjectByType<HeadsetCameraCaptureService>();

            if (headsetCameraCapture == null)
            {
                Debug.LogWarning("[WorldActionDispatcher] ConfirmHeadsetCameraCapture: service not assigned.");
                ArchStatusBus.Warning("Headset camera capture service not assigned.", "CAPTURE");
                return;
            }

            headsetCameraCapture.ConfirmPreviewCapture();
        }

        private void HandleGenerateWorldFromCapture(VoiceIntentCommand command)
        {
            if (coordinator == null)
                coordinator = FindFirstObjectByType<VoiceToWorldLabsPluginCoordinator>();

            if (coordinator == null)
            {
                Debug.LogWarning("[WorldActionDispatcher] GenerateWorldFromCapture: coordinator not assigned.");
                ArchStatusBus.Warning("World generation coordinator not assigned.", "CAPTURE");
                return;
            }

            if (coordinator.IsBusy)
            {
                command.spoken_response = coordinator.BusyMessage;
                ArchStatusBus.Warning(coordinator.BusyMessage, "WORLD");
                return;
            }

            coordinator.TriggerWorldGenerationFromLastCapture(command.world_prompt);
        }

        private void HandleGenerateObjectFromCapture(VoiceIntentCommand command)
        {
            if (!ObjectGenerationApiConfig.IsAnyProviderConfigured())
            {
                string message = "Object generator API key missing. Set THREEDAISTUDIO_API_KEY, or HITEM_ACCESS_KEY and HITEM_SECRET_KEY.";
                command.spoken_response = message;
                ArchStatusBus.Warning(message, "OBJECT");
                return;
            }

            string prompt = !string.IsNullOrWhiteSpace(command.object_name)
                ? command.object_name
                : command.world_prompt;
            Debug.Log($"[WorldActionDispatcher] GenerateObjectFromCapture requested: {prompt}");

            if (objectGenerationService == null)
                objectGenerationService = ObjectGenerationService.GetOrCreate();

            if (objectGenerationService == null || !objectGenerationService.GenerateFromLastCapture(prompt))
            {
                string message = objectGenerationService != null && !string.IsNullOrWhiteSpace(objectGenerationService.LastFailureMessage)
                    ? objectGenerationService.LastFailureMessage
                    : "Object generation could not start.";
                command.spoken_response = message;
                Debug.LogWarning("[WorldActionDispatcher] GenerateObjectFromCapture could not start object generation: " + message);
            }
        }

        private void HandleSearchImages(VoiceIntentCommand command)
        {
            if (imageSearchPanel == null)
                imageSearchPanel = FindFirstObjectByType<ImageSearchPanel>(FindObjectsInactive.Include);

            if (imageSearchPanel == null)
            {
                Debug.LogWarning("[WorldActionDispatcher] SearchImages: image search panel not assigned.");
                ArchStatusBus.Warning("Image search panel not assigned.", "IMAGE");
                return;
            }

            FindFirstObjectByType<ArchOperationsPanel>(FindObjectsInactive.Include)?.ShowImageSearch();
            string query = !string.IsNullOrWhiteSpace(command.image_search_query)
                ? command.image_search_query
                : command.world_prompt;
            imageSearchPanel.Search(query);
        }

        private void HandleSelectImageSearchResult()
        {
            if (imageSearchPanel == null)
                imageSearchPanel = FindFirstObjectByType<ImageSearchPanel>(FindObjectsInactive.Include);

            if (imageSearchPanel == null)
            {
                Debug.LogWarning("[WorldActionDispatcher] SelectImageSearchResult: image search panel not assigned.");
                ArchStatusBus.Warning("Image search panel not assigned.", "IMAGE");
                return;
            }

            imageSearchPanel.UseSelectedImage();
        }

        private void HandleStepImageSearchResult(bool next)
        {
            if (imageSearchPanel == null)
                imageSearchPanel = FindFirstObjectByType<ImageSearchPanel>(FindObjectsInactive.Include);

            if (imageSearchPanel == null)
            {
                Debug.LogWarning("[WorldActionDispatcher] StepImageSearchResult: image search panel not assigned.");
                ArchStatusBus.Warning("Image search panel not assigned.", "IMAGE");
                return;
            }

            FindFirstObjectByType<ArchOperationsPanel>(FindObjectsInactive.Include)?.ShowImageSearch();
            if (next)
                imageSearchPanel.Next();
            else
                imageSearchPanel.Previous();
        }

        private void HandleCaptureWorldThumbnail()
        {
            if (worldViewCaptureService == null)
                worldViewCaptureService = FindFirstObjectByType<WorldViewCaptureService>(FindObjectsInactive.Include);

            if (worldViewCaptureService == null)
            {
                Debug.LogWarning("[WorldActionDispatcher] CaptureWorldThumbnail: service not assigned.");
                ArchStatusBus.Warning("World view capture service not assigned.", "CAPTURE");
                return;
            }

            EnsureCapturableActiveConfig();
            worldViewCaptureService.CaptureThumbnail();
        }

        private void HandleCaptureWorldPanorama()
        {
            if (worldViewCaptureService == null)
                worldViewCaptureService = FindFirstObjectByType<WorldViewCaptureService>(FindObjectsInactive.Include);

            if (worldViewCaptureService == null)
            {
                Debug.LogWarning("[WorldActionDispatcher] CaptureWorldPanorama: service not assigned.");
                ArchStatusBus.Warning("World view capture service not assigned.", "CAPTURE");
                return;
            }

            EnsureCapturableActiveConfig();
            worldViewCaptureService.CapturePanorama();
        }

        /// <summary>
        /// Turns an in-memory draft into a real on-disk config, keeping everything captured so far.
        /// Used both by an explicit save and by captures, which need a folder to write into.
        /// </summary>
        WorldConfig MaterialiseDraftConfig(WorldConfig draft, string requestedName)
        {
            // One implementation, in WorldConfigAutoSave, so a draft materialised by a save, a capture or a
            // mutation cannot drift apart.
            if (worldConfigAutoSave != null)
            {
                if (draft != null && !ReferenceEquals(worldConfigAutoSave.ActiveConfig, draft))
                    worldConfigAutoSave.ActiveConfig = draft;

                WorldConfig materialised = worldConfigAutoSave.EnsureMaterialisedActiveConfig(requestedName);
                if (materialised != null)
                    return materialised;
            }

            string name = string.IsNullOrWhiteSpace(requestedName)
                ? WorldConfigAutoSave.DefaultDraftName()
                : requestedName.Trim();

            var source = draft.world_source ?? new WorldSourceData { type = "static" };
            source.display_name = name;

            WorldConfig created = worldConfigStore.CreateConfig(source, name, null);
            CopyCapturedState(draft, created);
            worldConfigAutoSave.ActiveConfig = created;
            worldConfigStore.SaveConfig(created);

            Debug.Log($"[WorldActionDispatcher] Materialised draft into world '{name}' ({created.config_id}).");
            return created;
        }

        /// <summary>
        /// A draft config has no folder, and the capture services write their png into the config folder.
        /// Capturing is as strong a signal as saving that the world is worth keeping, so materialise the
        /// draft first instead of failing with "Loaded world has no config folder".
        /// </summary>
        void EnsureCapturableActiveConfig()
        {
            if (worldConfigAutoSave == null || worldConfigStore == null)
                return;

            WorldConfig active = worldConfigAutoSave.ActiveConfig;
            if (active == null)
                active = worldConfigAutoSave.EnsureStaticDraft();

            if (!string.IsNullOrEmpty(active.config_id))
                return;

            WorldConfig created = MaterialiseDraftConfig(active, null);
            ArchStatusBus.Info($"Saved as {created.display_name} so the capture has somewhere to write.", "CAPTURE");
        }

        private void HandleSwitchToStaticWorld()
        {
            audioWorldActionController?.StopAndDestroyAllWorldAudio();
            // Keep a draft rather than clearing outright: the static room is a place the user can still
            // add to and save, so it needs something for those edits to attach to.
            worldConfigAutoSave?.EnsureStaticDraft();

            if (staticWorldController != null)
            {
                staticWorldController.SwitchToStaticWorld();
                if (interactionMemory != null && staticWorldController.staticWorldRoot != null)
                {
                    interactionMemory.RegisterInteraction(staticWorldController.staticWorldRoot);
                }
            }

            playerOriginController?.ResetToOrigin();
            ArchWorldInfoPanel infoPanel = FindFirstObjectByType<ArchWorldInfoPanel>();
            infoPanel?.ResetToNoWorldLoaded();
            uiPanels?.Show("arch_menu");
            onSwitchToStaticWorld?.Invoke();
        }

        private void HandleShowUi(VoiceIntentCommand command)
        {
            if (uiPanels != null)
            {
                uiPanels.Show(command.ui_panel);
            }
            else
            {
                Debug.Log($"Show UI requested: {command.ui_panel}");
            }
        }

        private void HandleQuitApplication()
        {
            Debug.Log("[WorldActionDispatcher] QuitApplication requested.");
            audioWorldActionController?.StopAndDestroyAllWorldAudio();
            if (worldConfigAutoSave != null)
                worldConfigAutoSave.ActiveConfig = null;
            Application.Quit();
        }

        private void HandleSetSunDirection(VoiceIntentCommand command, SpatialSnapshot spatial)
        {
            if (lightRig == null)
            {
                Debug.LogWarning("No LightRigController assigned.");
                return;
            }

            if (!lightRig.TryAlignSun(command, spatial))
            {
                string message = string.IsNullOrWhiteSpace(lightRig.LastFailureMessage)
                    ? "Point where the sun should be."
                    : lightRig.LastFailureMessage;
                Debug.LogWarning("Could not align sun from current spatial context. " + message);
                command.spoken_response = message;
                ArchStatusBus.Warning(message, "SPATIAL");
            }
        }

        private void HandleSetLightingPreset(VoiceIntentCommand command)
        {
            if (lightRig != null)
            {
                lightRig.ApplyPreset(command.lighting_preset);
            }
            else
            {
                Debug.Log($"Lighting preset requested: {command.lighting_preset}");
            }
        }

        private void HandleCreateLight(VoiceIntentCommand command, SpatialSnapshot spatial)
        {
            if (lightActionController == null)
            {
                Debug.LogWarning("[WorldActionDispatcher] LightActionController not assigned.");
                ArchStatusBus.Warning("Light controller not assigned.", "LIGHT");
                return;
            }

            GameObject created = lightActionController.CreateLight(command, spatial);
            if (created == null && !string.Equals(command.light_type, "ambient", StringComparison.OrdinalIgnoreCase))
            {
                string message = FirstNonEmpty(lightActionController.LastFailureMessage, "Could not create light.");
                command.spoken_response = message;
                ArchStatusBus.Warning(message, "LIGHT");
                Debug.LogWarning("[WorldActionDispatcher] CreateLight: " + message);
                return;
            }

            string status = string.Equals(command.light_type, "ambient", StringComparison.OrdinalIgnoreCase)
                ? "Updated ambient light."
                : $"Created {created.name}.";
            Debug.Log("[WorldActionDispatcher] " + status);
            ArchStatusBus.Info(status, "LIGHT");
            if (created != null)
                OnObjectMutated?.Invoke(command, created);
        }

        private void HandleModifyLight(VoiceIntentCommand command, SpatialSnapshot spatial)
        {
            if (lightActionController == null)
            {
                Debug.LogWarning("[WorldActionDispatcher] LightActionController not assigned.");
                ArchStatusBus.Warning("Light controller not assigned.", "LIGHT");
                return;
            }

            if (!lightActionController.TryModifyLight(command, spatial, out List<GameObject> targets))
            {
                string message = FirstNonEmpty(lightActionController.LastFailureMessage, "No matching light found.");
                command.spoken_response = message;
                ArchStatusBus.Warning(message, "LIGHT");
                Debug.LogWarning("[WorldActionDispatcher] ModifyLight: " + message);
                return;
            }

            string status = targets.Count == 1 ? $"Updated {targets[0].name}." : $"Updated {targets.Count} lights.";
            Debug.Log("[WorldActionDispatcher] " + status);
            ArchStatusBus.Info(status, "LIGHT");
            foreach (GameObject target in targets)
                OnObjectMutated?.Invoke(command, target);
        }

        private void HandlePlaceObject(VoiceIntentCommand command, SpatialSnapshot spatial)
        {
            string objectName = command != null ? command.object_name : "";
            bool canPlaceLocally = objectPlacement != null && objectPlacement.CanPlaceLocally(objectName);
            if (!canPlaceLocally && !string.IsNullOrWhiteSpace(objectName))
            {
                if (TryBlockForActiveObjectGeneration(command))
                    return;

                if (!TryValidateGeneratedObjectPlacement(command, spatial, out string placementFailure))
                {
                    command.spoken_response = placementFailure;
                    ArchStatusBus.Warning(placementFailure, "SPATIAL");
                    Debug.LogWarning($"[WorldActionDispatcher] Generate text object placement unresolved for '{objectName}': {placementFailure}");
                    return;
                }

                if (TryBeginCachedObjectChoice(command, spatial, objectName))
                    return;

                if (ObjectGenerationApiConfig.IsAnyProviderConfigured())
                {
                    if (objectGenerationService == null)
                        objectGenerationService = ObjectGenerationService.GetOrCreate();

                    if (objectGenerationService != null && objectGenerationService.GenerateFromText(objectName, command, spatial))
                    {
                        Debug.Log($"[WorldActionDispatcher] Generate text object requested: {objectName}");
                        ArchStatusBus.Info($"Creating {objectName}.", "OBJECT");
                        return;
                    }

                    string message = objectGenerationService != null && !string.IsNullOrWhiteSpace(objectGenerationService.LastFailureMessage)
                        ? objectGenerationService.LastFailureMessage
                        : "Object generation could not start.";
                    command.spoken_response = message;
                    ArchStatusBus.Warning(message, "OBJECT");
                    Debug.LogWarning("[WorldActionDispatcher] Generate text object did not start: " + message);
                    return;
                }
            }

            if (objectPlacement != null)
            {
                GameObject placed = objectPlacement.Place(command, spatial);
                if (interactionMemory != null && placed != null)
                {
                    interactionMemory.RegisterCreatedObject(placed);
                }
                if (placed != null)
                {
                    ApplyCreatedObjectAttributes(command, spatial, placed);
                    OnObjectMutated?.Invoke(command, placed);
                }
                else
                {
                    string message = string.IsNullOrWhiteSpace(objectPlacement.LastFailureMessage)
                        ? "Where?"
                        : objectPlacement.LastFailureMessage;
                    command.spoken_response = message;
                    ArchStatusBus.Warning(message, "SPATIAL");
                }
            }
            else
            {
                Debug.Log($"Place object requested: {command.object_name}");
            }
        }

        private void ApplyCreatedObjectAttributes(VoiceIntentCommand command, SpatialSnapshot spatial, GameObject placed)
        {
            if (command == null || placed == null)
                return;

            if (!string.IsNullOrWhiteSpace(command.material_prompt) && materialTargetController != null)
            {
                VoiceIntentCommand materialCommand = new VoiceIntentCommand
                {
                    transcript = command.transcript,
                    intent = VoiceIntentType.SetTargetMaterial,
                    should_execute = true,
                    target_reference = TargetReferenceMode.LastCreatedObject,
                    material_prompt = command.material_prompt
                };

                if (!materialTargetController.TryApplyMaterial(materialCommand, spatial, out _))
                    Debug.LogWarning("[WorldActionDispatcher] Could not apply create-time material: " + materialTargetController.LastFailureMessage);
            }

            if (command.object_width_meters > 0f)
                ScaleObjectToWidth(placed, command.object_width_meters);

            if (command.object_weightless)
                MakeObjectWeightless(placed);
        }

        private static void ScaleObjectToWidth(GameObject target, float widthMeters)
        {
            if (target == null || widthMeters <= 0f)
                return;

            Bounds bounds = CalculateRendererBounds(target);
            if (bounds.size.x <= 0.0001f)
                return;

            float scaleFactor = widthMeters / bounds.size.x;
            target.transform.localScale *= scaleFactor;
        }

        private static Bounds CalculateRendererBounds(GameObject target)
        {
            Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
            Bounds bounds = new Bounds(target.transform.position, Vector3.zero);
            bool hasBounds = false;
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null)
                    continue;

                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            return bounds;
        }

        private static void MakeObjectWeightless(GameObject target)
        {
            if (target == null)
                return;

            Rigidbody body = target.GetComponent<Rigidbody>() ?? target.GetComponentInChildren<Rigidbody>();
            if (body == null)
                body = target.AddComponent<Rigidbody>();

            body.useGravity = false;
            body.mass = 0.0001f;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.isKinematic = true;
        }

        public bool TryBeginCachedObjectChoiceForTests(VoiceIntentCommand command, SpatialSnapshot spatial)
        {
            string objectName = command != null ? command.object_name : "";
            bool canPlaceLocally = objectPlacement != null && objectPlacement.CanPlaceLocally(objectName);
            return !canPlaceLocally && TryBeginCachedObjectChoice(command, spatial, objectName);
        }

        bool TryBeginCachedObjectChoice(VoiceIntentCommand command, SpatialSnapshot spatial, string objectName)
        {
            if (command == null || string.IsNullOrWhiteSpace(objectName))
                return false;

            if (cachedObjectStore == null)
                cachedObjectStore = FindFirstObjectByType<CachedObjectStore>(FindObjectsInactive.Include);
            if (cachedObjectChoiceController == null)
                cachedObjectChoiceController = FindFirstObjectByType<CachedObjectChoiceController>(FindObjectsInactive.Include);
            if (cachedObjectStore == null || cachedObjectChoiceController == null)
                return false;

            List<CachedObjectRecord> matches = cachedObjectStore.FindAllByName(objectName);
            if (matches == null || matches.Count == 0)
                return false;

            cachedObjectChoiceController.BeginChoice(command, spatial, matches);
            if (!cachedObjectChoiceController.HasPendingChoice)
                return false;

            command.spoken_response = matches.Count == 1
                ? $"I found a saved {objectName}. Use it, or create a new one?"
                : $"I found {matches.Count} saved {objectName} objects. Which one should I use, or should I create a new one?";

            cachedObjectChoicePanel?.Show(
                matches,
                record => UseSavedCachedObject(record),
                CreateNewFromCachedObjectChoice,
                CancelCachedObjectChoice);

            ArchStatusBus.Info(command.spoken_response, "OBJECT");
            Debug.Log($"[WorldActionDispatcher] Cached object choice pending for '{objectName}' ({matches.Count} match(es)).");
            return true;
        }

        void HandleSelectCachedObject(VoiceIntentCommand command)
        {
            string action = (command != null ? command.object_choice_action : "").Trim().ToLowerInvariant();
            if ((action == "use_saved" || action == "create_new") && TryBlockForActiveObjectGeneration(command))
                return;

            switch (action)
            {
                case "use_saved":
                    UseSavedCachedObject(null);
                    break;
                case "create_new":
                    CreateNewFromCachedObjectChoice();
                    break;
                case "cancel":
                    CancelCachedObjectChoice();
                    if (command != null)
                        command.spoken_response = "Cancelled.";
                    break;
                default:
                    if (command != null)
                        command.spoken_response = "Say use saved, create new, or cancel.";
                    ArchStatusBus.Warning("Say use saved, create new, or cancel.", "OBJECT");
                    break;
            }
        }

        void HandleCancelGeneration(VoiceIntentCommand command)
        {
            string target = NormalizeGenerationTarget(command);
            bool handled = false;

            if (target == "object" || target == "all")
            {
                if (objectGenerationService == null)
                    objectGenerationService = FindFirstObjectByType<ObjectGenerationService>(FindObjectsInactive.Include);
                handled |= objectGenerationService != null &&
                           objectGenerationService.CancelActiveGeneration("Cancelled object generation.");
            }

            if (target == "world" || target == "all")
            {
                if (coordinator == null)
                    coordinator = FindFirstObjectByType<VoiceToWorldLabsPluginCoordinator>(FindObjectsInactive.Include);
                handled |= coordinator != null &&
                           coordinator.CancelActiveGeneration("Cancelled world generation.");
            }

            if (handled)
            {
                if (command != null)
                    command.spoken_response = "";
                return;
            }

            string message = target == "object"
                ? "No object generation is currently running."
                : target == "world"
                    ? "No world generation is currently running."
                    : "No generation is currently running.";
            if (command != null)
                command.spoken_response = message;
            ArchStatusBus.Info(message, "GEN");
        }

        void HandleContinueGeneration(VoiceIntentCommand command)
        {
            string target = NormalizeGenerationTarget(command);
            bool handled = false;

            if (target == "object" || target == "all")
            {
                if (objectGenerationService == null)
                    objectGenerationService = FindFirstObjectByType<ObjectGenerationService>(FindObjectsInactive.Include);
                handled |= objectGenerationService != null &&
                           objectGenerationService.ContinueActiveGeneration();
            }

            if (target == "world" || target == "all")
            {
                if (coordinator == null)
                    coordinator = FindFirstObjectByType<VoiceToWorldLabsPluginCoordinator>(FindObjectsInactive.Include);
                handled |= coordinator != null &&
                           coordinator.ContinueActiveGeneration();
            }

            if (handled)
            {
                if (command != null)
                    command.spoken_response = "";
                return;
            }

            string message = target == "object"
                ? "No object generation is currently running."
                : target == "world"
                    ? "No world generation is currently running."
                    : "No generation is currently running.";
            if (command != null)
                command.spoken_response = message;
            ArchStatusBus.Info(message, "GEN");
        }

        public void UseSavedCachedObject(CachedObjectRecord selectedRecord)
        {
            if (TryBlockForActiveObjectGeneration(null))
                return;

            VoiceIntentCommand pendingCommand = null;
            SpatialSnapshot pendingSpatial = null;
            CachedObjectRecord record = null;
            bool consumed = cachedObjectChoiceController != null &&
                (selectedRecord != null
                    ? cachedObjectChoiceController.TryConsumeUseSaved(selectedRecord, out pendingCommand, out pendingSpatial, out record)
                    : cachedObjectChoiceController.TryConsumeUseSaved(out pendingCommand, out pendingSpatial, out record));

            if (!consumed)
            {
                ArchStatusBus.Warning("No saved object choice is pending.", "OBJECT");
                return;
            }

            cachedObjectChoicePanel?.Hide();
            StartCoroutine(ImportSavedCachedObject(record, pendingCommand, pendingSpatial));
        }

        public void CreateNewFromCachedObjectChoice()
        {
            if (TryBlockForActiveObjectGeneration(null))
                return;

            if (cachedObjectChoiceController == null ||
                !cachedObjectChoiceController.TryConsumeCreateNew(out VoiceIntentCommand pendingCommand, out SpatialSnapshot pendingSpatial))
            {
                ArchStatusBus.Warning("No saved object choice is pending.", "OBJECT");
                return;
            }

            cachedObjectChoicePanel?.Hide();
            string objectName = pendingCommand != null ? pendingCommand.object_name : "";
            if (string.IsNullOrWhiteSpace(objectName))
            {
                ArchStatusBus.Warning("No object name is pending.", "OBJECT");
                return;
            }

            if (!TryValidateGeneratedObjectPlacement(pendingCommand, pendingSpatial, out string placementFailure))
            {
                pendingCommand.spoken_response = placementFailure;
                ArchStatusBus.Warning(placementFailure, "SPATIAL");
                return;
            }

            if (!ObjectGenerationApiConfig.IsAnyProviderConfigured())
            {
                string message = "Object generator credentials missing.";
                pendingCommand.spoken_response = message;
                ArchStatusBus.Warning(message, "OBJECT");
                return;
            }

            if (objectGenerationService == null)
                objectGenerationService = ObjectGenerationService.GetOrCreate();

            // The user was offered the cached model and asked for a new one, so bypass the cache.
            if (objectGenerationService != null &&
                objectGenerationService.GenerateFromText(objectName, pendingCommand, pendingSpatial, allowCacheReuse: false))
            {
                Debug.Log($"[WorldActionDispatcher] Generate new text object requested after cache choice: {objectName}");
                ArchStatusBus.Info($"Creating {objectName}.", "OBJECT");
                return;
            }

            string startFailure = objectGenerationService != null && !string.IsNullOrWhiteSpace(objectGenerationService.LastFailureMessage)
                ? objectGenerationService.LastFailureMessage
                : "Object generation could not start.";
            pendingCommand.spoken_response = startFailure;
            ArchStatusBus.Warning(startFailure, "OBJECT");
        }

        public void CancelCachedObjectChoice()
        {
            cachedObjectChoiceController?.Cancel();
            cachedObjectChoicePanel?.Hide();
            ArchStatusBus.Info("Object choice cancelled.", "OBJECT");
        }

        System.Collections.IEnumerator ImportSavedCachedObject(CachedObjectRecord record, VoiceIntentCommand pendingCommand, SpatialSnapshot pendingSpatial)
        {
            if (record == null)
            {
                ArchStatusBus.Warning("Cached object was not found.", "OBJECT");
                yield break;
            }

            if (objectGenerationService == null)
                objectGenerationService = ObjectGenerationService.GetOrCreate();
            if (objectGenerationService == null)
            {
                ArchStatusBus.Warning("Object generation service is missing.", "OBJECT");
                yield break;
            }

            if (cachedObjectStore != null && objectGenerationService.cachedObjectStore == null)
                objectGenerationService.cachedObjectStore = cachedObjectStore;

            GameObject imported = null;
            string importError = null;
            ArchStatusBus.Info($"Loading saved {record.canonical_name}.", "OBJECT");
            yield return objectGenerationService.ImportCachedObject(record, pendingCommand, pendingSpatial, (go, error) =>
            {
                imported = go;
                importError = error;
            });

            if (imported == null)
            {
                string message = string.IsNullOrWhiteSpace(importError) ? "Could not load saved object." : importError;
                ArchStatusBus.Warning(message, "OBJECT");
                Debug.LogWarning("[WorldActionDispatcher] Cached object import failed: " + message);
                yield break;
            }

            interactionMemory?.RegisterCreatedObject(imported);
            OnObjectMutated?.Invoke(pendingCommand, imported);
            ArchStatusBus.Success($"Loaded saved {record.canonical_name}.", "OBJECT");
        }

        bool TryValidateGeneratedObjectPlacement(VoiceIntentCommand command, SpatialSnapshot spatial, out string failureMessage)
        {
            failureMessage = "";
            if (objectPlacement == null)
                return true;

            if (objectPlacement.TryResolvePlacementPose(command, spatial, out _, out _))
                return true;

            failureMessage = string.IsNullOrWhiteSpace(objectPlacement.LastFailureMessage)
                ? "Where?"
                : objectPlacement.LastFailureMessage;
            return false;
        }

        bool TryBlockForActiveObjectGeneration(VoiceIntentCommand command)
        {
            if (objectGenerationService == null)
                objectGenerationService = FindFirstObjectByType<ObjectGenerationService>(FindObjectsInactive.Include);

            if (objectGenerationService == null || !objectGenerationService.IsBusy)
                return false;

            string message = objectGenerationService.BusyMessage;
            if (command != null)
                command.spoken_response = message;
            ArchStatusBus.Warning(message, "OBJECT");
            Debug.LogWarning("[WorldActionDispatcher] " + message);
            return true;
        }

        private void HandleMoveTarget(VoiceIntentCommand command, SpatialSnapshot spatial)
        {
            if (targetTransformController == null)
            {
                Debug.LogWarning("No TargetTransformController assigned.");
                return;
            }

            if (!targetTransformController.TryMoveTarget(command, spatial, out GameObject target))
            {
                string message = FirstNonEmpty(targetTransformController.LastFailureMessage, "Could not move requested target.");
                command.spoken_response = message;
                ArchStatusBus.Warning(message, "TARGET");
                Debug.LogWarning(message);
                return;
            }

            Debug.Log($"Moved target '{target.name}'.");
            OnObjectMutated?.Invoke(command, target);
        }

        private void HandleBehaviorCommand(VoiceIntentCommand command, SpatialSnapshot spatial)
        {
            if (behaviorCommandController == null)
            {
                Debug.LogWarning("[WorldActionDispatcher] BehaviorCommandController not assigned.");
                command.spoken_response = "Behavior controller not assigned.";
                ArchStatusBus.Warning(command.spoken_response, "BEHAVIOR");
                return;
            }

            SpeechIntent.Behaviors.BehaviorCommandResult result = behaviorCommandController.Execute(command, spatial);
            if (result == null)
            {
                command.spoken_response = "Behavior command returned no result.";
                ArchStatusBus.Warning(command.spoken_response, "BEHAVIOR");
                return;
            }

            command.spoken_response = result.message;
            if (result.success)
                ArchStatusBus.Info(result.message, "BEHAVIOR");
            else
                ArchStatusBus.Warning(result.message, "BEHAVIOR");
        }

        private void HandleScaleTarget(VoiceIntentCommand command, SpatialSnapshot spatial)
        {
            if (targetTransformController == null)
            {
                Debug.LogWarning("No TargetTransformController assigned.");
                return;
            }

            if (!targetTransformController.TryScaleTarget(command, spatial, out GameObject target))
            {
                string message = FirstNonEmpty(targetTransformController.LastFailureMessage, "Could not scale requested target.");
                command.spoken_response = message;
                ArchStatusBus.Warning(message, "TARGET");
                Debug.LogWarning(message);
                return;
            }

            Debug.Log($"Scaled target '{target.name}'.");
            OnObjectMutated?.Invoke(command, target);
        }

        private void HandleRotateTarget(VoiceIntentCommand command, SpatialSnapshot spatial)
        {
            if (targetTransformController == null)
            {
                Debug.LogWarning("No TargetTransformController assigned.");
                return;
            }

            if (!targetTransformController.TryRotateTarget(command, spatial, out GameObject target))
            {
                string message = FirstNonEmpty(targetTransformController.LastFailureMessage, "Could not rotate requested target.");
                command.spoken_response = message;
                ArchStatusBus.Warning(message, "TARGET");
                Debug.LogWarning(message);
                return;
            }

            Debug.Log($"Rotated target '{target.name}'.");
            OnObjectMutated?.Invoke(command, target);
        }

        private void HandleResetTransform(VoiceIntentCommand command, SpatialSnapshot spatial)
        {
            if (targetTransformController == null)
            {
                Debug.LogWarning("No TargetTransformController assigned.");
                return;
            }

            if (!targetTransformController.TryResetTransform(command, spatial, out GameObject target))
            {
                string message = FirstNonEmpty(targetTransformController.LastFailureMessage, "Could not reset transform of requested target.");
                command.spoken_response = message;
                ArchStatusBus.Warning(message, "TARGET");
                Debug.LogWarning(message);
                return;
            }

            Debug.Log($"Reset transform of '{target.name}'.");
            OnObjectMutated?.Invoke(command, target);
        }

        private void HandleSetTargetMaterial(VoiceIntentCommand command, SpatialSnapshot spatial)
        {
            if (materialTargetController == null)
            {
                Debug.LogWarning("[WorldActionDispatcher] MaterialTargetController not assigned.");
                ArchStatusBus.Warning("Material controller not assigned.", "MATERIAL");
                return;
            }

            if (!materialTargetController.TryApplyMaterial(command, spatial, out List<GameObject> targets))
            {
                string message = FirstNonEmpty(materialTargetController.LastFailureMessage, "No matching material target found.");
                Debug.LogWarning("[WorldActionDispatcher] SetTargetMaterial: " + message);
                command.spoken_response = message;
                ArchStatusBus.Warning(message, "MATERIAL");
                return;
            }

            string status = targets.Count == 1 ? $"Updated {targets[0].name} material." : $"Updated {targets.Count} materials.";
            Debug.Log("[WorldActionDispatcher] " + status);
            ArchStatusBus.Info(status, "MATERIAL");
            foreach (GameObject target in targets)
                OnObjectMutated?.Invoke(command, target);
        }

        private void HandleDescribeTarget(VoiceIntentCommand command, SpatialSnapshot spatial)
        {
            SceneEntityResolver resolver = ResolveEntityResolver();
            if (resolver == null)
            {
                command.spoken_response = "I don't have an object resolver.";
                ArchStatusBus.Warning(command.spoken_response, "INSPECT");
                return;
            }

            SceneTargetResolution resolution = resolver.ResolveTargetDetailed(command, spatial);
            if (resolution.status == SceneTargetResolutionStatus.None || resolution.Target == null)
            {
                command.spoken_response = string.IsNullOrWhiteSpace(resolution.message)
                    ? "Which object?"
                    : resolution.message;
                ArchStatusBus.Warning(command.spoken_response, "INSPECT");
                return;
            }

            if (resolution.status == SceneTargetResolutionStatus.Ambiguous)
            {
                command.spoken_response = string.IsNullOrWhiteSpace(resolution.message)
                    ? "Which object?"
                    : resolution.message;
                ArchStatusBus.Warning(command.spoken_response, "INSPECT");
                return;
            }

            GameObject target = resolution.Target;
            interactionMemory?.RegisterInteraction(target);

            string objectType = InferObjectType(target);
            string objectName = GetDisplayObjectName(target);
            string materialDescription = DescribeMaterial(target);
            string describeQuery = (command.describe_query ?? "").Trim().ToLowerInvariant();

            if (describeQuery == "name")
            {
                command.spoken_response = $"This is called '{objectName}'.";
            }
            else if (describeQuery == "full")
            {
                if (!string.IsNullOrWhiteSpace(materialDescription) && !string.IsNullOrWhiteSpace(objectType))
                    command.spoken_response = $"This is a {materialDescription} {objectType} called '{objectName}'.";
                else if (!string.IsNullOrWhiteSpace(objectType))
                    command.spoken_response = $"This is a {objectType} called '{objectName}'.";
                else if (!string.IsNullOrWhiteSpace(materialDescription))
                    command.spoken_response = $"This is a {materialDescription} object called '{objectName}'.";
                else
                    command.spoken_response = $"This is called '{objectName}'.";
            }
            else if (!string.IsNullOrWhiteSpace(materialDescription) && !string.IsNullOrWhiteSpace(objectType))
            {
                command.spoken_response = $"This is a {materialDescription} {objectType}.";
            }
            else if (!string.IsNullOrWhiteSpace(objectType))
            {
                command.spoken_response = $"This is a {objectType}.";
            }
            else if (!string.IsNullOrWhiteSpace(materialDescription))
            {
                command.spoken_response = $"This is a {materialDescription} object.";
            }
            else
            {
                command.spoken_response = $"This is called '{objectName}'.";
            }

            ArchStatusBus.Info(command.spoken_response, "INSPECT");
            Debug.Log("[WorldActionDispatcher] " + command.spoken_response);
        }

        private SceneEntityResolver ResolveEntityResolver()
        {
            if (materialTargetController != null && materialTargetController.entityResolver != null)
                return materialTargetController.entityResolver;

            if (targetTransformController != null && targetTransformController.entityResolver != null)
                return targetTransformController.entityResolver;

            return FindFirstObjectByType<SceneEntityResolver>(FindObjectsInactive.Include);
        }

        private static string GetDisplayObjectName(GameObject target)
        {
            if (target == null)
                return "object";

            SpeechIntentTrackable trackable = target.GetComponent<SpeechIntentTrackable>();
            if (trackable != null && !string.IsNullOrWhiteSpace(trackable.configInstanceId))
            {
                // Keep the stable ID available internally, but the spoken name should remain human-friendly.
            }

            return string.IsNullOrWhiteSpace(target.name)
                ? FirstNonEmpty(trackable != null ? trackable.EffectiveName : "", "object")
                : target.name;
        }

        private static string InferObjectType(GameObject target)
        {
            if (target == null)
                return "object";

            SpeechIntentTrackable trackable = target.GetComponent<SpeechIntentTrackable>();
            string searchable = (FirstNonEmpty(trackable != null ? trackable.EffectiveName : "", target.name) ?? "").ToLowerInvariant();
            string fromText = InferObjectTypeFromText(searchable);
            if (!string.IsNullOrWhiteSpace(fromText))
                return fromText;

            MeshFilter meshFilter = target.GetComponentInChildren<MeshFilter>(true);
            string meshName = meshFilter != null && meshFilter.sharedMesh != null ? meshFilter.sharedMesh.name : "";
            string fromMesh = InferObjectTypeFromText(meshName.ToLowerInvariant());
            if (!string.IsNullOrWhiteSpace(fromMesh))
                return fromMesh;

            if (target.GetComponentInChildren<Light>(true) != null)
                return "light";
            if (target.GetComponentInChildren<AudioSource>(true) != null)
                return "sound source";

            return "object";
        }

        private static string InferObjectTypeFromText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return "";

            if (text.Contains("sphere", StringComparison.Ordinal) || text.Contains("ball", StringComparison.Ordinal) || text.Contains("orb", StringComparison.Ordinal))
                return "sphere";
            if (text.Contains("cube", StringComparison.Ordinal) || text.Contains("box", StringComparison.Ordinal) || text.Contains("block", StringComparison.Ordinal))
                return "cube";
            if (text.Contains("capsule", StringComparison.Ordinal))
                return "capsule";
            if (text.Contains("cylinder", StringComparison.Ordinal) || text.Contains("column", StringComparison.Ordinal))
                return "cylinder";
            if (text.Contains("plane", StringComparison.Ordinal) || text.Contains("floor", StringComparison.Ordinal) || text.Contains("platform", StringComparison.Ordinal))
                return "plane";
            if (text.Contains("light", StringComparison.Ordinal) || text.Contains("lamp", StringComparison.Ordinal))
                return "light";
            if (text.Contains("audio", StringComparison.Ordinal) || text.Contains("sound", StringComparison.Ordinal))
                return "sound source";

            return "";
        }

        private static string DescribeMaterial(GameObject target)
        {
            if (target == null)
                return "";

            Renderer renderer = target.GetComponentInChildren<Renderer>(true);
            Material material = renderer != null ? renderer.sharedMaterial : null;
            if (material == null)
                return "";

            if (!TryGetMaterialColor(material, out Color color))
                return "";

            string colorName = GetNearestColorName(color);
            bool metallic = material.HasProperty("_Metallic") && material.GetFloat("_Metallic") > 0.5f;
            bool matte = TryGetMaterialSmoothness(material, out float smoothness) && smoothness < 0.3f;
            bool glossy = smoothness > 0.7f;

            if (metallic)
                return colorName + " metallic";
            if (matte)
                return "matte " + colorName;
            if (glossy)
                return "glossy " + colorName;
            return colorName;
        }

        private static bool TryGetMaterialColor(Material material, out Color color)
        {
            color = Color.white;
            if (material == null)
                return false;

            if (material.HasProperty("_BaseColor"))
            {
                color = material.GetColor("_BaseColor");
                return true;
            }

            if (material.HasProperty("_Color"))
            {
                color = material.GetColor("_Color");
                return true;
            }

            return false;
        }

        private static bool TryGetMaterialSmoothness(Material material, out float smoothness)
        {
            smoothness = 0f;
            if (material == null)
                return false;

            if (material.HasProperty("_Smoothness"))
            {
                smoothness = material.GetFloat("_Smoothness");
                return true;
            }

            if (material.HasProperty("_Glossiness"))
            {
                smoothness = material.GetFloat("_Glossiness");
                return true;
            }

            return false;
        }

        private static string GetNearestColorName(Color color)
        {
            (string name, Color color)[] colors =
            {
                ("black", Color.black),
                ("white", Color.white),
                ("gray", new Color(0.5f, 0.5f, 0.5f, 1f)),
                ("red", Color.red),
                ("blue", Color.blue),
                ("green", Color.green),
                ("yellow", Color.yellow),
                ("orange", new Color(1f, 0.45f, 0f, 1f)),
                ("purple", new Color(0.55f, 0.2f, 0.9f, 1f)),
                ("pink", new Color(1f, 0.35f, 0.7f, 1f)),
                ("silver", new Color(0.75f, 0.75f, 0.72f, 1f)),
                ("gold", new Color(1f, 0.72f, 0.16f, 1f))
            };

            string bestName = "colored";
            float best = float.PositiveInfinity;
            for (int i = 0; i < colors.Length; i++)
            {
                float distance = ColorDistanceRgb(color, colors[i].color);
                if (distance < best)
                {
                    best = distance;
                    bestName = colors[i].name;
                }
            }

            return bestName;
        }

        private static float ColorDistanceRgb(Color a, Color b)
        {
            float dr = a.r - b.r;
            float dg = a.g - b.g;
            float db = a.b - b.b;
            return Mathf.Sqrt(dr * dr + dg * dg + db * db);
        }

        private void HandleModifyPhysics(VoiceIntentCommand command, SpatialSnapshot spatial)
        {
            List<GameObject> targets = ResolvePhysicsTargets(command, spatial);
            if (targets.Count == 0)
            {
                string message = "No matching physics target found.";
                command.spoken_response = message;
                ArchStatusBus.Warning(message, "PHYSICS");
                Debug.LogWarning("[WorldActionDispatcher] ModifyPhysics: " + message);
                return;
            }

            foreach (GameObject target in targets)
            {
                ApplyPhysicsCommand(command, target);
                interactionMemory?.RegisterModifiedTarget(target);
                OnObjectMutated?.Invoke(command, target);
            }

            string status = targets.Count == 1 ? $"Updated {targets[0].name} physics." : $"Updated {targets.Count} physics targets.";
            command.spoken_response = status;
            ArchStatusBus.Info(status, "PHYSICS");
            Debug.Log("[WorldActionDispatcher] " + status);
        }

        private List<GameObject> ResolvePhysicsTargets(VoiceIntentCommand command, SpatialSnapshot spatial)
        {
            List<GameObject> targets = new List<GameObject>();
            if (command == null)
                return targets;

            SceneEntityResolver resolver = null;
            if (targetTransformController != null && targetTransformController.entityResolver != null)
                resolver = targetTransformController.entityResolver;
            else if (materialTargetController != null && materialTargetController.entityResolver != null)
                resolver = materialTargetController.entityResolver;
            else
                resolver = FindFirstObjectByType<SceneEntityResolver>(FindObjectsInactive.Include);

            if (resolver != null)
            {
                SceneTargetResolution resolution = resolver.ResolveTargets(command, spatial);
                if (resolution.status == SceneTargetResolutionStatus.Single ||
                    resolution.status == SceneTargetResolutionStatus.All)
                {
                    targets.AddRange(resolution.targets);
                    return targets;
                }
            }

            GameObject remembered = interactionMemory != null ? interactionMemory.GetLastCreatedOrInteracted() : null;
            if (remembered != null && PhysicsTargetMatches(command, remembered))
                targets.Add(remembered);

            if (targets.Count == 0 && !string.IsNullOrWhiteSpace(command.target_name))
                targets.AddRange(FindPhysicsTargetsByName(command.target_name));

            return targets;
        }

        private static bool PhysicsTargetMatches(VoiceIntentCommand command, GameObject target)
        {
            if (target == null || command == null)
                return false;

            if (command.target_reference == TargetReferenceMode.LastCreatedOrInteracted ||
                string.IsNullOrWhiteSpace(command.target_name))
                return true;

            string expected = NormalizePhysicsTargetName(command.target_name);
            SpeechIntentTrackable trackable = target.GetComponent<SpeechIntentTrackable>();
            if (trackable != null)
            {
                if (NormalizePhysicsTargetName(trackable.EffectiveName) == expected)
                    return true;
                foreach (string alias in trackable.aliases)
                    if (NormalizePhysicsTargetName(alias) == expected)
                        return true;
            }

            return NormalizePhysicsTargetName(target.name) == expected;
        }

        private static List<GameObject> FindPhysicsTargetsByName(string targetName)
        {
            List<GameObject> targets = new List<GameObject>();
            string expected = NormalizePhysicsTargetName(targetName);
            if (string.IsNullOrWhiteSpace(expected))
                return targets;

            SpeechIntentTrackable[] trackables = FindObjectsByType<SpeechIntentTrackable>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (SpeechIntentTrackable trackable in trackables)
            {
                if (trackable == null || trackable.gameObject == null)
                    continue;

                if (NormalizePhysicsTargetName(trackable.EffectiveName) == expected)
                    targets.Add(trackable.gameObject);
            }

            if (targets.Count == 0)
            {
                GameObject byName = GameObject.Find(targetName);
                if (byName != null)
                    targets.Add(byName);
            }

            return targets;
        }

        private static string NormalizePhysicsTargetName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";

            string normalized = value.Trim().ToLowerInvariant();
            normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"^(the|a|an)\s+", "").Trim();
            normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"^generated[_\s-]+", "").Trim();
            if (normalized.EndsWith("s", StringComparison.Ordinal) && normalized.Length > 1)
                normalized = normalized.Substring(0, normalized.Length - 1);
            return normalized;
        }

        private static void ApplyPhysicsCommand(VoiceIntentCommand command, GameObject target)
        {
            if (target == null || command == null)
                return;

            Rigidbody body = target.GetComponent<Rigidbody>() ?? target.GetComponentInChildren<Rigidbody>();
            if (body == null)
                body = target.AddComponent<Rigidbody>();

            string action = command.physics_action ?? "";
            if (action == "enable_gravity")
            {
                body.useGravity = true;
                body.isKinematic = false;
                if (body.mass <= 0.0001f)
                    body.mass = 1f;
                return;
            }

            if (action == "set_mass" && command.physics_mass > 0f)
            {
                body.mass = command.physics_mass;
                return;
            }

            body.useGravity = false;
            if (action == "set_weightless" || command.object_weightless)
            {
                body.mass = 0.0001f;
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.isKinematic = true;
            }
        }

        private void HandleSetProxyVisibility(VoiceIntentCommand command)
        {
            if (proxyVisibilityController == null)
            {
                Debug.LogWarning("[WorldActionDispatcher] RuntimeProxyVisibilityController not assigned.");
                ArchStatusBus.Warning("Proxy controller not assigned.", "PROXY");
                return;
            }

            int changed = proxyVisibilityController.SetVisibility(command.proxy_category, command.proxy_visible);
            if (changed <= 0)
            {
                string message = FirstNonEmpty(proxyVisibilityController.LastFailureMessage, "No proxies found.");
                command.spoken_response = message;
                ArchStatusBus.Warning(message, "PROXY");
                return;
            }

            string verb = command.proxy_visible ? "Showing" : "Hiding";
            string category = string.IsNullOrWhiteSpace(command.proxy_category) ? "all" : command.proxy_category;
            string status = $"{verb} {changed} {category} proxy object(s).";
            Debug.Log("[WorldActionDispatcher] " + status);
            ArchStatusBus.Info(status, "PROXY");
        }

        private void HandleDeleteTarget(VoiceIntentCommand command, SpatialSnapshot spatial)
        {
            if (IsAudioDeleteCommand(command))
            {
                if (audioWorldActionController == null)
                {
                    Debug.LogWarning("[WorldActionDispatcher] DeleteTarget: audioWorldActionController not assigned.");
                    ArchStatusBus.Warning("Audio controller not assigned.", "DELETE");
                    return;
                }

                int deletedAudio = audioWorldActionController.DeleteAudioTargets(command);
                if (deletedAudio <= 0)
                    ArchStatusBus.Warning("No matching audio sources found.", "DELETE");
                else
                    ArchStatusBus.Info($"Deleted {deletedAudio} audio source(s).", "DELETE");
                return;
            }

            List<GameObject> targets = ResolveDeleteTargets(command, spatial);
            if (targets.Count == 0)
            {
                string label = FirstNonEmpty(command.target_name, command.object_name, command.target_entity);
                string message = IsClarificationResponse(command.spoken_response)
                    ? command.spoken_response
                    : (string.IsNullOrWhiteSpace(label)
                        ? "What should I delete?"
                        : $"No matching {label} found.");
                Debug.LogWarning("[WorldActionDispatcher] DeleteTarget: " + message);
                command.spoken_response = message;
                ArchStatusBus.Warning(message, "DELETE");
                return;
            }

            int deleted = 0;
            foreach (GameObject target in targets)
            {
                if (target == null || IsProtectedDeleteTarget(target))
                    continue;

                if (interactionMemory != null)
                {
                    if (interactionMemory.lastCreatedObject == target)
                        interactionMemory.lastCreatedObject = null;
                    if (interactionMemory.lastModifiedTarget == target)
                        interactionMemory.lastModifiedTarget = null;
                    if (interactionMemory.lastInteractedTarget == target)
                        interactionMemory.lastInteractedTarget = null;
                    if (interactionMemory.currentSelection == target)
                        interactionMemory.currentSelection = null;
                }

                if (lightActionController != null)
                    lightActionController.NotifyDeleting(target);

                Destroy(target);
                deleted++;
            }

            if (deleted <= 0)
            {
                ArchStatusBus.Warning("No deletable targets found.", "DELETE");
                return;
            }

            Debug.Log($"[WorldActionDispatcher] Deleted {deleted} target(s).");
            ArchStatusBus.Info($"Deleted {deleted} target(s).", "DELETE");
        }

        private List<GameObject> ResolveDeleteTargets(VoiceIntentCommand command, SpatialSnapshot spatial)
        {
            var targets = new List<GameObject>();
            if (command == null)
                return targets;

            if (command.target_reference == TargetReferenceMode.PointedObject)
            {
                SceneEntityResolver pointedResolver = targetTransformController != null
                    ? targetTransformController.entityResolver
                    : null;
                if (pointedResolver != null && HasDeleteTargetConstraints(command))
                {
                    SceneTargetResolution resolution = pointedResolver.ResolveTargets(command, spatial);
                    if (resolution.status == SceneTargetResolutionStatus.Single ||
                        resolution.status == SceneTargetResolutionStatus.All)
                    {
                        foreach (GameObject resolved in resolution.targets)
                        {
                            if (resolved != null && !IsProtectedDeleteTarget(resolved))
                                targets.Add(resolved);
                        }

                        return targets;
                    }

                    command.spoken_response = string.IsNullOrWhiteSpace(resolution.message)
                        ? "That object does not match."
                        : resolution.message;
                    return targets;
                }

                if (TryResolveIndicatedDeleteTarget(command, spatial, out GameObject indicated) &&
                    indicated != null &&
                    !IsProtectedDeleteTarget(indicated))
                {
                    targets.Add(indicated);
                }

                return targets;
            }

            string requestedName = FirstNonEmpty(command.target_name, command.object_name, command.target_entity);
            bool all = command.target_reference == TargetReferenceMode.All || IsAllToken(requestedName);
            string normalizedNeedle = NormalizeDeleteName(requestedName);

            SceneEntityResolver resolver = targetTransformController != null
                ? targetTransformController.entityResolver
                : null;
            if (resolver != null)
            {
                SceneTargetResolution resolution = resolver.ResolveTargets(command, spatial);
                if (resolution.status == SceneTargetResolutionStatus.Single ||
                    resolution.status == SceneTargetResolutionStatus.All)
                {
                    foreach (GameObject target in resolution.targets)
                    {
                        if (target != null && !IsProtectedDeleteTarget(target))
                            targets.Add(target);
                    }

                    return targets;
                }

                if (resolution.status == SceneTargetResolutionStatus.Ambiguous)
                {
                    command.spoken_response = string.IsNullOrWhiteSpace(resolution.message)
                        ? $"Which {requestedName}?"
                        : resolution.message;
                    command.should_execute = false;
                    return targets;
                }

                if (!string.IsNullOrWhiteSpace(command.target_material_prompt))
                {
                    command.spoken_response = string.IsNullOrWhiteSpace(resolution.message)
                        ? $"No matching {command.target_material_prompt} {requestedName} found."
                        : resolution.message;
                    return targets;
                }
            }

            if (!all)
            {
                GameObject remembered = ResolveRememberedDeleteTarget(command);
                if (remembered != null && !IsProtectedDeleteTarget(remembered))
                    targets.Add(remembered);
            }

            if (string.IsNullOrWhiteSpace(normalizedNeedle) || normalizedNeedle == "all" || normalizedNeedle == "everything")
                return targets;

            SpeechIntentTrackable[] trackables =
                FindObjectsByType<SpeechIntentTrackable>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var seen = new HashSet<int>();
            foreach (SpeechIntentTrackable trackable in trackables)
            {
                if (trackable == null || trackable.gameObject == null)
                    continue;

                GameObject candidate = trackable.gameObject;
                if (IsProtectedDeleteTarget(candidate))
                    continue;

                if (!MatchesDeleteName(candidate, trackable, normalizedNeedle))
                    continue;

                int id = candidate.GetInstanceID();
                if (seen.Add(id))
                    targets.Add(candidate);
            }

            if (!all && targets.Count > 1)
            {
                string label = string.IsNullOrWhiteSpace(requestedName) ? "object" : requestedName;
                command.spoken_response = $"Which {label}?";
                command.should_execute = false;
                targets.Clear();
            }

            return targets;
        }

        private static bool HasDeleteTargetConstraints(VoiceIntentCommand command)
        {
            if (command == null)
                return false;

            return !string.IsNullOrWhiteSpace(command.target_name) ||
                   !string.IsNullOrWhiteSpace(command.object_name) ||
                   !string.IsNullOrWhiteSpace(command.target_entity) ||
                   !string.IsNullOrWhiteSpace(command.target_material_prompt);
        }

        private bool TryResolveIndicatedDeleteTarget(VoiceIntentCommand command, SpatialSnapshot spatial, out GameObject target)
        {
            target = null;
            SceneEntityResolver resolver = targetTransformController != null
                ? targetTransformController.entityResolver
                : null;

            if (resolver != null)
            {
                target = resolver.ResolveTarget(TargetReferenceMode.PointedObject, "", spatial, command.target_hand);
                if (target != null)
                    return true;
            }

            if (TryResolvePointedObjectFromSnapshot(spatial, command.target_hand, out target))
                return true;

            return TryResolveGazeObject(spatial, out target);
        }

        private static bool TryResolvePointedObjectFromSnapshot(SpatialSnapshot spatial, HandSelection handSelection, out GameObject target)
        {
            target = null;
            if (spatial == null)
                return false;

            if (TryGetPreferredHandHit(spatial, handSelection, out HandRaySnapshot hand))
            {
                string hitPath = hand.has_actionable_hit ? hand.actionable_hit_object_path : hand.hit_object_path;
                string hitName = hand.has_actionable_hit ? hand.actionable_hit_object_name : hand.hit_object_name;

                target = FindByHierarchyPath(hitPath);
                if (target == null && !string.IsNullOrWhiteSpace(hitName))
                    target = FindByNameInActiveScene(hitName);

                SpeechIntentTrackable trackable = target != null ? target.GetComponentInParent<SpeechIntentTrackable>() : null;
                target = trackable != null ? trackable.gameObject : target;
                return target != null && !TargetSelectionUtility.IsNonActionableSupportTarget(target);
            }

            return false;
        }

        private bool TryResolveGazeObject(SpatialSnapshot spatial, out GameObject target)
        {
            target = null;
            if (spatial == null || spatial.head_forward.sqrMagnitude <= 0.0001f)
                return false;

            Ray ray = new Ray(spatial.head_position, spatial.head_forward.normalized);
            if (!Physics.Raycast(ray, out RaycastHit hit, deleteGazeMaxDistance, deleteGazeRaycastMask, deleteGazeTriggerInteraction))
                return false;

            target = hit.collider != null ? hit.collider.gameObject : null;
            SpeechIntentTrackable trackable = target != null ? target.GetComponentInParent<SpeechIntentTrackable>() : null;
            target = trackable != null ? trackable.gameObject : target;
            return target != null;
        }

        private static bool TryGetPreferredHandHit(SpatialSnapshot spatial, HandSelection handSelection, out HandRaySnapshot hand)
        {
            hand = null;
            if (spatial == null)
                return false;

            if (handSelection == HandSelection.Left && spatial.left_hand != null && spatial.left_hand.has_hit)
            {
                hand = spatial.left_hand;
                return true;
            }

            if (handSelection == HandSelection.Right && spatial.right_hand != null && spatial.right_hand.has_hit)
            {
                hand = spatial.right_hand;
                return true;
            }

            if (spatial.right_hand != null && spatial.right_hand.has_hit && spatial.right_hand.is_pointing)
            {
                hand = spatial.right_hand;
                return true;
            }

            if (spatial.left_hand != null && spatial.left_hand.has_hit && spatial.left_hand.is_pointing)
            {
                hand = spatial.left_hand;
                return true;
            }

            if (spatial.right_hand != null && spatial.right_hand.has_hit)
            {
                hand = spatial.right_hand;
                return true;
            }

            if (spatial.left_hand != null && spatial.left_hand.has_hit)
            {
                hand = spatial.left_hand;
                return true;
            }

            return false;
        }

        private GameObject ResolveRememberedDeleteTarget(VoiceIntentCommand command)
        {
            if (interactionMemory == null || command == null)
                return null;

            switch (command.target_reference)
            {
                case TargetReferenceMode.LastCreatedObject:
                    return interactionMemory.lastCreatedObject;
                case TargetReferenceMode.LastInteractedTarget:
                    return interactionMemory.lastInteractedTarget;
                case TargetReferenceMode.LastCreatedOrInteracted:
                    return interactionMemory.GetLastCreatedOrInteracted();
                case TargetReferenceMode.CurrentSelection:
                    return interactionMemory.currentSelection;
                default:
                    return null;
            }
        }

        private static bool MatchesDeleteName(GameObject candidate, SpeechIntentTrackable trackable, string normalizedNeedle)
        {
            if (MatchesDeleteName(candidate != null ? candidate.name : null, normalizedNeedle) ||
                MatchesDeleteName(trackable != null ? trackable.canonicalName : null, normalizedNeedle))
            {
                return true;
            }

            if (trackable?.aliases != null)
            {
                foreach (string alias in trackable.aliases)
                    if (MatchesDeleteName(alias, normalizedNeedle))
                        return true;
            }

            return false;
        }

        private static GameObject FindByHierarchyPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            string[] segments = path.Split('/');
            if (segments.Length == 0)
                return null;

            Scene activeScene = SceneManager.GetActiveScene();
            GameObject[] roots = activeScene.GetRootGameObjects();
            foreach (GameObject root in roots)
            {
                if (root == null || !string.Equals(root.name, segments[0], StringComparison.OrdinalIgnoreCase))
                    continue;

                Transform current = root.transform;
                for (int i = 1; i < segments.Length && current != null; i++)
                    current = FindDirectChild(current, segments[i]);

                if (current != null)
                    return current.gameObject;
            }

            return null;
        }

        private static Transform FindDirectChild(Transform parent, string childName)
        {
            if (parent == null || string.IsNullOrWhiteSpace(childName))
                return null;

            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                if (child != null && string.Equals(child.name, childName, StringComparison.OrdinalIgnoreCase))
                    return child;
            }

            return null;
        }

        private static GameObject FindByNameInActiveScene(string objectName)
        {
            if (string.IsNullOrWhiteSpace(objectName))
                return null;

            Scene activeScene = SceneManager.GetActiveScene();
            GameObject[] roots = activeScene.GetRootGameObjects();
            foreach (GameObject root in roots)
            {
                if (root == null)
                    continue;

                Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
                foreach (Transform transform in transforms)
                {
                    if (transform != null && string.Equals(transform.name, objectName, StringComparison.OrdinalIgnoreCase))
                        return transform.gameObject;
                }
            }

            return null;
        }

        private static bool MatchesDeleteName(string candidate, string normalizedNeedle)
        {
            string normalizedCandidate = NormalizeDeleteName(candidate);
            if (string.IsNullOrWhiteSpace(normalizedCandidate) || string.IsNullOrWhiteSpace(normalizedNeedle))
                return false;

            return normalizedCandidate == normalizedNeedle ||
                   normalizedCandidate.Contains(normalizedNeedle, StringComparison.Ordinal) ||
                   normalizedNeedle.Contains(normalizedCandidate, StringComparison.Ordinal);
        }

        private static bool IsProtectedDeleteTarget(GameObject target)
        {
            if (target == null)
                return true;

            Transform current = target.transform;
            while (current != null)
            {
                string name = current.name;
                if (string.Equals(name, "Me", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, "Main Camera", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, "UI", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, "Systems", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, "Arch", StringComparison.OrdinalIgnoreCase) ||
                    name.IndexOf("LCARS", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("Tts", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("VoiceActivation", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }

                current = current.parent;
            }

            return false;
        }

        private static bool IsAudioDeleteCommand(VoiceIntentCommand command)
        {
            if (command == null || command.target_reference == TargetReferenceMode.PointedObject)
                return false;

            string target = FirstNonEmpty(command?.target_name, command?.object_name, command?.sound_prompt);
            string normalized = NormalizeDeleteName(target);
            return normalized == "audio" ||
                   normalized == "sound" ||
                   normalized == "audios" ||
                   normalized == "sounds";
        }

        private static bool IsAllToken(string value)
        {
            string normalized = NormalizeDeleteName(value);
            return normalized == "all" ||
                   normalized == "everything";
        }

        private static bool IsClarificationResponse(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            string trimmed = value.Trim();
            return trimmed.StartsWith("Which ", StringComparison.OrdinalIgnoreCase) ||
                   trimmed.StartsWith("What ", StringComparison.OrdinalIgnoreCase) ||
                   trimmed.EndsWith("?", StringComparison.Ordinal);
        }

        private static string NormalizeDeleteName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            string normalized = value.Trim().ToLowerInvariant();
            string[] prefixes = { "all of the ", "all the ", "all ", "every ", "the " };
            foreach (string prefix in prefixes)
            {
                if (normalized.StartsWith(prefix, StringComparison.Ordinal))
                {
                    normalized = normalized.Substring(prefix.Length).Trim();
                    break;
                }
            }

            normalized = normalized.Replace("_", " ").Replace("-", " ");
            char[] chars = normalized.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (!char.IsLetterOrDigit(chars[i]))
                    chars[i] = ' ';
            }

            string[] parts = new string(chars).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Length > 3 && parts[i].EndsWith("s", StringComparison.Ordinal))
                    parts[i] = parts[i].Substring(0, parts[i].Length - 1);
            }

            return string.Join(" ", parts);
        }

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (string value in values)
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            return string.Empty;
        }

        private static string NormalizeGenerationTarget(VoiceIntentCommand command)
        {
            string target = FirstNonEmpty(command?.target_entity, command?.target_name, command?.object_name).Trim().ToLowerInvariant();
            if (target.Contains("object", StringComparison.Ordinal) ||
                target.Contains("model", StringComparison.Ordinal) ||
                target.Contains("3d", StringComparison.Ordinal))
                return "object";
            if (target.Contains("world", StringComparison.Ordinal) ||
                target.Contains("worldlabs", StringComparison.Ordinal) ||
                target.Contains("world labs", StringComparison.Ordinal))
                return "world";
            return "all";
        }

        private void HandleLoadSplat(VoiceIntentCommand command)
        {
            if (string.IsNullOrWhiteSpace(command.content_path))
            {
                Debug.LogWarning("[WorldActionDispatcher] HandleLoadSplat: content_path is empty.");
                return;
            }

            // A model file is not a splat: it imports through the object pipeline so it is wrapped,
            // placed, saved and grabbable. Both arrive here as LoadSplat because the parser only cares
            // that the user named a file.
            string extension = (System.IO.Path.GetExtension(command.content_path) ?? string.Empty).ToLowerInvariant();
            if (extension == ".glb" || extension == ".gltf" || extension == ".obj" ||
                extension == ".fbx" || extension == ".usdz")
            {
                LoadSideloadedModel(command.content_path, command);
                return;
            }

            if (splatLoader == null)
            {
                Debug.LogWarning("[WorldActionDispatcher] HandleLoadSplat: splatLoader not assigned.");
                return;
            }
            Debug.Log($"[WorldActionDispatcher] Loading splat from '{command.content_path}'.");
            splatLoader.LoadAsync(command.content_path);
        }

        /// <summary>Imports a sideloaded model file as an interactable object.</summary>
        void LoadSideloadedModel(string path, VoiceIntentCommand command)
        {
            if (objectGenerationService == null)
                objectGenerationService = ObjectGenerationService.GetOrCreate();

            if (objectGenerationService == null)
            {
                Debug.LogWarning("[WorldActionDispatcher] HandleLoadSplat: no object generation service for a model file.");
                ArchStatusBus.Warning("Model import is not available.", "OBJECT");
                return;
            }

            Debug.Log($"[WorldActionDispatcher] Loading sideloaded model '{path}'.");
            ArchStatusBus.Info($"Loading {path}.", "OBJECT");
            objectGenerationService.ImportModelFile(path, command);
        }

        private void HandleSetGenerationModel(VoiceIntentCommand command)
        {
            if (string.IsNullOrWhiteSpace(command.generation_model))
            {
                Debug.LogWarning("[WorldActionDispatcher] SetGenerationModel: generation_model is empty.");
                return;
            }

            MarbleModel model = command.generation_model.ToLower() switch
            {
                "draft"                       => MarbleModel.Draft,
                "fast" or "low"               => MarbleModel.Fast,
                "standard" or "normal"        => MarbleModel.Standard,
                "high" or "best" or "premium" => MarbleModel.High,
                _                             => MarbleModel.Standard
            };

            coordinator?.SetGenerationModel(model);
            worldBrowser?.SetGenerationModel(model);
            ArchStatusBus.Post($"Generation model set to {ModelLabel(model)}.", ArchStatusLevel.Info, "MODEL");
            Debug.Log($"[WorldActionDispatcher] SetGenerationModel → {model}");
        }

        void HandleBrowserGenerationModelChanged(MarbleModel model)
        {
            if (_syncingGenerationModel)
                return;

            _syncingGenerationModel = true;
            coordinator?.SetGenerationModel(model);
            _syncingGenerationModel = false;
            ArchStatusBus.Post($"Generation model set to {ModelLabel(model)}.", ArchStatusLevel.Info, "MODEL");
        }

        void HandleCoordinatorGenerationModelChanged(MarbleModel model)
        {
            if (_syncingGenerationModel)
                return;

            _syncingGenerationModel = true;
            worldBrowser?.SetGenerationModel(model);
            _syncingGenerationModel = false;
        }

        static string ModelLabel(MarbleModel model) => model switch
        {
            MarbleModel.Draft    => "Draft",
            MarbleModel.Fast     => "Fast",
            MarbleModel.Standard => "Standard",
            MarbleModel.High     => "High",
            _                    => model.ToString()
        };

        private void HandleLoadPanorama(VoiceIntentCommand command)
        {
            if (panoLoader == null)
            {
                Debug.LogWarning("[WorldActionDispatcher] HandleLoadPanorama: panoLoader not assigned.");
                return;
            }
            if (string.IsNullOrWhiteSpace(command.content_path))
            {
                Debug.LogWarning("[WorldActionDispatcher] HandleLoadPanorama: content_path is empty.");
                return;
            }
            Debug.Log($"[WorldActionDispatcher] Loading panorama from '{command.content_path}'.");
            panoLoader.LoadAsync(command.content_path);
        }

        string _pendingSaveAsName;
        WorldConfig _pendingSaveAsExisting;

        /// <summary>
        /// Saves the current room, whether it holds a generated/loaded world or only the built-in
        /// static room with edits. A save spoken with no world loaded materialises the static draft
        /// rather than doing nothing, and an explicit name that already exists always asks first.
        /// </summary>
        private void HandleSaveWorldConfig(VoiceIntentCommand command)
        {
            if (worldConfigAutoSave == null)
            {
                command.spoken_response = "Saving is not configured.";
                return;
            }

            WorldConfig active = worldConfigAutoSave.ActiveConfig;
            if (active == null)
            {
                // The app starts with no world by design; the static room is still a place the user can
                // fill with objects and sound, so give it a draft and save that.
                active = worldConfigAutoSave.EnsureStaticDraft();
            }

            bool isDraft = string.IsNullOrEmpty(active.config_id);
            string requested = (command.config_name ?? string.Empty).Trim();

            if (!string.IsNullOrWhiteSpace(requested))
            {
                WorldConfig existing = FindConfigByDisplayName(requested);
                if (existing != null && !ReferenceEquals(existing, active))
                {
                    _pendingSaveAsName = requested;
                    _pendingSaveAsExisting = existing;
                    Debug.LogWarning($"[WorldActionDispatcher] Save As '{requested}' collides with an existing world; asking before overwriting.");
                    command.spoken_response = $"{requested} already exists. Say overwrite to replace it, or cancel.";
                    ArchStatusBus.Warning($"{requested} already exists. Overwrite or cancel.", "SAVE");
                    return;
                }
            }

            SaveActiveConfig(active, requested, isDraft, command);
        }

        WorldConfig FindConfigByDisplayName(string name)
        {
            var configs = worldConfigStore?.ListConfigs();
            if (configs == null || string.IsNullOrWhiteSpace(name))
                return null;

            foreach (WorldConfig config in configs)
            {
                if (config != null && string.Equals((config.display_name ?? string.Empty).Trim(), name.Trim(),
                        StringComparison.OrdinalIgnoreCase))
                    return config;
            }

            return null;
        }

        void SaveActiveConfig(WorldConfig active, string requestedName, bool isDraft, VoiceIntentCommand command)
        {
            if (isDraft)
            {
                WorldConfig created = MaterialiseDraftConfig(active, requestedName);
                command.spoken_response = $"Saved as {created.display_name}.";
                ArchStatusBus.Info($"Saved as {created.display_name}.", "SAVE");
                TryAutoCaptureThumbnail(created);
                return;
            }

            if (!string.IsNullOrWhiteSpace(requestedName) &&
                !string.Equals(requestedName.Trim(), (active.display_name ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase))
            {
                WorldConfig fork = worldConfigStore.ForkConfig(active, requestedName.Trim());
                if (fork != null)
                {
                    worldConfigAutoSave.ActiveConfig = fork;
                    Debug.Log($"[WorldActionDispatcher] Saved As '{requestedName}'.");
                    command.spoken_response = $"Saved as {fork.display_name}.";
                    ArchStatusBus.Info($"Saved as {fork.display_name}.", "SAVE");
                    TryAutoCaptureThumbnail(fork);
                }
                return;
            }

            worldConfigStore.SaveConfig(active);
            Debug.Log($"[WorldActionDispatcher] Saved current config '{active.display_name}'.");
            command.spoken_response = $"Saved {active.display_name}.";
            ArchStatusBus.Info($"Saved {active.display_name}.", "SAVE");
            TryAutoCaptureThumbnail(active);
        }

        /// <summary>
        /// A saved world with no thumbnail shows as a blank card, so capture one automatically on save.
        /// The capture service renders the camera, writes thumbnail.png into the config folder and
        /// re-saves; it no-ops while another capture is in progress, and worlds that already have a
        /// thumbnail are left alone so a deliberate reshoot is not overwritten on every save.
        /// </summary>
        void TryAutoCaptureThumbnail(WorldConfig config)
        {
            if (worldViewCaptureService == null || config?.world_source == null)
                return;

            if (!string.IsNullOrEmpty(config.world_source.cached_thumbnail))
                return;

            if (worldViewCaptureService.CaptureThumbnail())
                Debug.Log($"[WorldActionDispatcher] Queued automatic thumbnail capture for '{config.display_name}'.");
        }

        /// <summary>Moves the draft's captured contents onto the freshly created config before writing.</summary>
        static void CopyCapturedState(WorldConfig from, WorldConfig to)
        {
            if (from == null || to == null) return;

            to.world_transform = from.world_transform;
            to.lighting = from.lighting;
            to.objects = from.objects ?? new System.Collections.Generic.List<SavedObject>();
            to.spawn_points = from.spawn_points ?? new System.Collections.Generic.List<SpawnPointData>();
            to.prompts = from.prompts ?? new System.Collections.Generic.List<PromptEntry>();
            to.generation_model = from.generation_model;
            to.created_at = from.created_at ?? to.created_at;
        }

        /// <summary>
        /// Answers a pending "overwrite or cancel?" raised by a colliding save-as. Returns true when the
        /// utterance was consumed; unrelated speech drops the question and continues as a new command.
        /// </summary>
        bool TryResolvePendingSaveAs(VoiceIntentCommand command)
        {
            string text = (command.transcript ?? string.Empty).Trim().ToLowerInvariant();
            bool overwrite = text.Contains("overwrite") || text.Contains("replace") ||
                             text.Contains("yes") || text.Contains("go ahead") || text.Contains("do it");
            bool cancel = text.Contains("cancel") || text.Contains("never mind") ||
                          text.Contains("no thanks") || text == "no" || text.Contains("don't");

            string name = _pendingSaveAsName;
            WorldConfig existing = _pendingSaveAsExisting;

            if (!overwrite && !cancel)
            {
                Debug.Log($"[WorldActionDispatcher] Pending save-as '{name}' dropped by unrelated utterance '{command.transcript}'.");
                _pendingSaveAsName = null;
                _pendingSaveAsExisting = null;
                return false;
            }

            _pendingSaveAsName = null;
            _pendingSaveAsExisting = null;

            if (cancel)
            {
                command.spoken_response = "Okay, nothing was overwritten.";
                ArchStatusBus.Info("Save cancelled.", "SAVE");
                return true;
            }

            if (existing == null || worldConfigStore == null)
            {
                command.spoken_response = "I lost track of that world. Say save this world again.";
                return true;
            }

            WorldConfig active = worldConfigAutoSave != null ? worldConfigAutoSave.ActiveConfig : null;
            if (active != null && !ReferenceEquals(active, existing))
                CopyCapturedState(active, existing);

            existing.display_name = name;
            worldConfigStore.SaveConfig(existing);
            if (worldConfigAutoSave != null) worldConfigAutoSave.ActiveConfig = existing;

            Debug.Log($"[WorldActionDispatcher] Overwrote existing world '{name}'.");
            command.spoken_response = $"Overwrote {name}.";
            ArchStatusBus.Info($"Overwrote {name}.", "SAVE");
            TryAutoCaptureThumbnail(existing);
            return true;
        }

        private void HandleLoadWorldConfig(VoiceIntentCommand command)
        {
            if (!string.IsNullOrWhiteSpace(command.config_name))
            {
                if (worldConfigStore == null || worldConfigRestorer == null)
                {
                    Debug.LogWarning("[WorldActionDispatcher] LoadWorldConfig: worldConfigStore or worldConfigRestorer not assigned.");
                    command.spoken_response = "Saved world loading is not configured.";
                    return;
                }

                // The startup scan usually finishes long before the user speaks, so answer from the
                // in-memory list: a hit restores immediately and a miss can ask "shall I create it?"
                // in this same turn. Only an empty list needs the asynchronous path below.
                var knownConfigs = worldConfigStore.ListConfigs();
                if (knownConfigs != null && knownConfigs.Count > 0)
                {
                    WorldConfig match = worldConfigStore.FindBestDisplayNameMatch(command.config_name);
                    if (match != null)
                    {
                        Debug.Log($"[WorldActionDispatcher] Loading saved world '{match.display_name}' from local storage before any World Labs fallback.");
                        ArchStatusBus.Info($"Loading saved world {match.display_name}.", "LOAD");
                        if (string.Equals(match.world_source?.type, "static", StringComparison.OrdinalIgnoreCase))
                            FindFirstObjectByType<WorldLabsWorldManager>(FindObjectsInactive.Include)?.RestoreDefaultWorld();
                        _ = worldConfigRestorer.RestoreAsync(match);
                        return;
                    }

                    OfferToCreateMissingWorld(command, command.config_name, knownConfigs);
                    return;
                }

                StartCoroutine(LoadSavedWorldByNameCoroutine(command.config_name));
            }
            else
            {
                // Open My Worlds panel
                uiPanels?.Show("my worlds");
            }
        }

        /// <summary>
        /// A saved-world load missed. Turn the request into a confirmation instead of failing:
        /// nothing is generated unless the user answers yes, and the prompt travels with the pending
        /// command so the confirmation executes without re-parsing the transcript.
        /// </summary>
        void OfferToCreateMissingWorld(VoiceIntentCommand command, string requestedName, System.Collections.Generic.IReadOnlyList<WorldConfig> available)
        {
            var names = new System.Collections.Generic.List<string>();
            if (available != null)
            {
                foreach (var config in available)
                {
                    if (config != null && !string.IsNullOrWhiteSpace(config.display_name))
                        names.Add(config.display_name);
                }
            }

            Debug.Log($"[WorldActionDispatcher] No saved world matching '{requestedName}'. " +
                      $"Available local worlds=[{string.Join(", ", names)}]. Offering to create it; " +
                      "nothing is generated unless the user confirms.");

            // The recogniser often keeps a full stop ("purple elephant."), which reads badly in the
            // spoken question and in the generation prompt.
            string requested = (requestedName ?? string.Empty).Trim().TrimEnd('.', '!', '?', ',', ';', ':');

            command.intent = VoiceIntentType.GenerateWorld;
            command.should_execute = false;
            command.world_prompt = requested;
            command.spoken_response = $"No saved world called {requested}. Shall I create it?";
        }

        System.Collections.IEnumerator LoadSavedWorldByNameCoroutine(string requestedName)
        {
            // Wait for the local world.json scan if startup has not completed. This is
            // intentionally local-only: a failed match must not become a new generation.
            System.Threading.Tasks.Task scanTask = worldConfigStore.ScanAndMigrateAsync();
            while (!scanTask.IsCompleted)
                yield return null;

            if (scanTask.IsFaulted)
            {
                Debug.LogWarning($"[WorldActionDispatcher] Could not scan saved worlds before loading '{requestedName}': {scanTask.Exception?.GetBaseException().Message}");
                ArchStatusBus.Warning("Could not scan saved worlds.", "LOAD");
                yield break;
            }

            WorldConfig match = worldConfigStore.FindBestDisplayNameMatch(requestedName);
            if (match == null)
            {
                string available = string.Join(", ", System.Linq.Enumerable.Select(
                    worldConfigStore.ListConfigs(),
                    config => string.IsNullOrWhiteSpace(config?.display_name) ? "<unnamed>" : config.display_name));
                Debug.LogWarning($"[WorldActionDispatcher] LoadWorldConfig: no saved config matching '{requestedName}'. Available local worlds=[{available}]. No world generation was started.");
                ArchStatusBus.Warning($"No saved world named {requestedName}.", "LOAD");
                yield break;
            }

            Debug.Log($"[WorldActionDispatcher] Loading saved world '{match.display_name}' from local storage before any World Labs fallback.");
            ArchStatusBus.Info($"Loading saved world {match.display_name}.", "LOAD");

            // A static config is the built-in room, so any loaded splat world has to go — but the
            // unload must happen BEFORE the restore starts. Doing it inside the restore invalidated the
            // restore's own transition and cancelled it silently.
            if (string.Equals(match.world_source?.type, "static", StringComparison.OrdinalIgnoreCase))
                FindFirstObjectByType<WorldLabsWorldManager>(FindObjectsInactive.Include)?.RestoreDefaultWorld();

            _ = worldConfigRestorer.RestoreAsync(match);
        }

        private void HandleSetPositionSnap(VoiceIntentCommand command)
        {
            EnsureWorldManipulationController();
            if (worldManipulationController == null)
            {
                command.spoken_response = "World manipulation is not available.";
                return;
            }

            float meters = Mathf.Max(0f, command.position_snap_meters);
            worldManipulationController.SetPositionSnap(meters);
            command.spoken_response = meters <= 0f
                ? "Grid snap off."
                : $"Grid snap set to {FormatSnapDistance(meters)}.";
            ArchStatusBus.Info(command.spoken_response, "SNAP");
        }

        private void HandleSetAngleSnap(VoiceIntentCommand command)
        {
            EnsureWorldManipulationController();
            if (worldManipulationController == null)
            {
                command.spoken_response = "World manipulation is not available.";
                return;
            }

            float degrees = Mathf.Max(0f, command.angle_snap_degrees);
            worldManipulationController.SetAngleSnap(degrees);
            command.spoken_response = degrees <= 0f
                ? "Angle snap off."
                : $"Angle snap set to {degrees:g} degrees.";
            ArchStatusBus.Info(command.spoken_response, "SNAP");
        }

        /// <summary>
        /// Applies master volume changes to the whole experience.
        ///
        /// Everything the app plays routes through <see cref="AudioListener.volume"/>: generated world
        /// ambience, audio sourced from providers, and the TTS voice itself. Per-source
        /// <c>ControlAudioSource</c> needs an existing audio source and so cannot express "turn it down",
        /// which is what people actually say.
        /// </summary>
        private void HandleSetWorldVolume(VoiceIntentCommand command)
        {
            float before = AudioListener.volume;
            string action = (command.audio_control ?? string.Empty).Trim().ToLowerInvariant();

            switch (action)
            {
                case "mute":
                    // Remember the level so "unmute" restores it rather than jumping to full volume.
                    if (before > 0.001f) _volumeBeforeMute = before;
                    AudioListener.volume = 0f;
                    break;

                case "unmute":
                    AudioListener.volume = _volumeBeforeMute > 0.001f ? _volumeBeforeMute : 1f;
                    break;

                case "louder":
                case "softer":
                {
                    float delta = Mathf.Approximately(command.audio_volume_delta, 0f)
                        ? (action == "louder" ? 0.1f : -0.1f)
                        : command.audio_volume_delta;
                    AudioListener.volume = Mathf.Clamp01(before + delta);
                    break;
                }

                default:
                    if (command.audio_muted)
                    {
                        if (before > 0.001f) _volumeBeforeMute = before;
                        AudioListener.volume = 0f;
                    }
                    else
                    {
                        AudioListener.volume = Mathf.Clamp01(command.audio_volume);
                    }
                    break;
            }

            // Deliberately NO bookkeeping here. The level to restore is captured by the mute itself; updating
            // it on every non-zero change meant a "louder" after a mute overwrote the remembered level, so
            // unmute restored the intermediate value instead of the one the user muted from.

            Debug.Log($"[Audio] master volume {before:0.00} -> {AudioListener.volume:0.00} (action='{action}')");
            ArchStatusBus.Info($"Master volume {AudioListener.volume:0.00}", "AUDIO");
        }

        float _volumeBeforeMute = 1f;

        private void HandleSetSnapEnabled(VoiceIntentCommand command)
        {
            EnsureWorldManipulationController();
            if (worldManipulationController == null)
            {
                command.spoken_response = "Transform snapping is not available.";
                return;
            }

            worldManipulationController.SetSnapEnabled(command.snap_enabled);
            command.spoken_response = command.snap_enabled ? "Snap on." : "Snap off.";
            ArchStatusBus.Info(command.spoken_response, "SNAP");
        }

        private void HandleGrabWorld(VoiceIntentCommand command)
        {
            EnsureWorldManipulationController();
            if (worldManipulationController == null)
            {
                command.spoken_response = "World manipulation is not available.";
                return;
            }

            bool release = string.Equals(command.world_manipulation_action, "release", StringComparison.OrdinalIgnoreCase);
            if (release)
            {
                worldManipulationController.ReleaseWorld();
                command.spoken_response = "World released.";
                return;
            }

            if (!worldManipulationController.GrabWorld())
                command.spoken_response = "No world is loaded to grab.";
            else
                command.spoken_response = "World grabbed.";
        }

        void EnsureWorldManipulationController()
        {
            if (worldManipulationController == null)
                worldManipulationController = GetComponent<WorldManipulationController>()
                                              ?? FindFirstObjectByType<WorldManipulationController>(FindObjectsInactive.Include);
        }

        static string FormatSnapDistance(float meters)
        {
            if (meters < 0.01f)
                return $"{meters * 1000f:g} millimeters";
            if (meters < 1f)
                return $"{meters * 100f:g} centimeters";
            return $"{meters:g} meters";
        }

        private void HandleCreateAudioSource(VoiceIntentCommand command, SpatialSnapshot spatial)
        {
            if (audioWorldActionController == null)
            {
                audioWorldActionController = GetComponent<AudioWorldActionController>()
                                             ?? FindFirstObjectByType<AudioWorldActionController>();
            }

            if (audioWorldActionController == null)
            {
                Debug.LogWarning("[WorldActionDispatcher] CreateAudioSource: audioWorldActionController not assigned.");
                return;
            }

            audioWorldActionController.CreateAudioSource(
                command,
                spatial,
                created =>
                {
                    if (created != null)
                    {
                        interactionMemory?.RegisterCreatedObject(created);
                        OnObjectMutated?.Invoke(command, created);
                    }
                });
        }

        private void HandleControlAudioSource(VoiceIntentCommand command)
        {
            if (audioWorldActionController == null)
            {
                audioWorldActionController = GetComponent<AudioWorldActionController>()
                                             ?? FindFirstObjectByType<AudioWorldActionController>();
            }

            if (audioWorldActionController == null)
            {
                Debug.LogWarning("[WorldActionDispatcher] ControlAudioSource: audioWorldActionController not assigned.");
                return;
            }

            audioWorldActionController.ApplyAudioControl(
                command,
                changed =>
                {
                    if (changed != null)
                    {
                        interactionMemory?.RegisterModifiedTarget(changed);
                        OnObjectMutated?.Invoke(command, changed);
                    }
                });
        }
    }

    internal static class VoiceIntentCommandExtensions
    {
        public static string rawSummary(this VoiceIntentCommand command)
        {
            return command == null
                ? "<null>"
                : $"{command.intent} | execute={command.should_execute} | transcript={command.transcript}";
        }
    }
}
