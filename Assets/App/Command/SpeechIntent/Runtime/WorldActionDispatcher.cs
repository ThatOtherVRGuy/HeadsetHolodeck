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

            if (command.intent == VoiceIntentType.AskClarification || !command.should_execute)
            {
                Debug.Log($"Command not executed. Intent={command.intent}, Reason={command.reason}");
                return;
            }

            switch (command.intent)
            {
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

            worldViewCaptureService.CapturePanorama();
        }

        private void HandleSwitchToStaticWorld()
        {
            audioWorldActionController?.StopAndDestroyAllWorldAudio();
            if (worldConfigAutoSave != null)
                worldConfigAutoSave.ActiveConfig = null;

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

            if (objectGenerationService != null && objectGenerationService.GenerateFromText(objectName, pendingCommand, pendingSpatial))
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
                interactionMemory?.RegisterInteraction(target);
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
                target = FindByHierarchyPath(hand.hit_object_path);
                if (target == null && !string.IsNullOrWhiteSpace(hand.hit_object_name))
                    target = FindByNameInActiveScene(hand.hit_object_name);

                SpeechIntentTrackable trackable = target != null ? target.GetComponentInParent<SpeechIntentTrackable>() : null;
                target = trackable != null ? trackable.gameObject : target;
                return target != null;
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
            if (splatLoader == null)
            {
                Debug.LogWarning("[WorldActionDispatcher] HandleLoadSplat: splatLoader not assigned.");
                return;
            }
            if (string.IsNullOrWhiteSpace(command.content_path))
            {
                Debug.LogWarning("[WorldActionDispatcher] HandleLoadSplat: content_path is empty.");
                return;
            }
            Debug.Log($"[WorldActionDispatcher] Loading splat from '{command.content_path}'.");
            splatLoader.LoadAsync(command.content_path);
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

        private void HandleSaveWorldConfig(VoiceIntentCommand command)
        {
            if (worldConfigAutoSave == null || worldConfigAutoSave.ActiveConfig == null)
            {
                Debug.LogWarning("[WorldActionDispatcher] SaveWorldConfig: no active config.");
                return;
            }

            if (!string.IsNullOrWhiteSpace(command.config_name))
            {
                // Save As — fork the active config with the new name
                WorldConfig fork = worldConfigStore?.ForkConfig(worldConfigAutoSave.ActiveConfig, command.config_name);
                if (fork != null)
                {
                    worldConfigAutoSave.ActiveConfig = fork;
                    Debug.Log($"[WorldActionDispatcher] Saved As '{command.config_name}'.");
                }
            }
            else
            {
                // Save — overwrite current
                worldConfigStore?.SaveConfig(worldConfigAutoSave.ActiveConfig);
                Debug.Log("[WorldActionDispatcher] Saved current config.");
            }
        }

        private void HandleLoadWorldConfig(VoiceIntentCommand command)
        {
            if (!string.IsNullOrWhiteSpace(command.config_name))
            {
                // Load by name — fuzzy match on display_name
                if (worldConfigStore == null || worldConfigRestorer == null)
                {
                    Debug.LogWarning("[WorldActionDispatcher] LoadWorldConfig: worldConfigStore or worldConfigRestorer not assigned.");
                    return;
                }
                string lower = command.config_name.ToLowerInvariant();
                WorldConfig match = null;
                foreach (WorldConfig c in worldConfigStore.ListConfigs())
                {
                    if (c.display_name != null &&
                        c.display_name.ToLowerInvariant().Contains(lower))
                    {
                        match = c;
                        break;
                    }
                }
                if (match != null)
                    _ = worldConfigRestorer.RestoreAsync(match);
                else
                    Debug.LogWarning($"[WorldActionDispatcher] LoadWorldConfig: no config matching '{command.config_name}'.");
            }
            else
            {
                // Open My Worlds panel
                uiPanels?.Show("my worlds");
            }
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
                        interactionMemory?.RegisterInteraction(changed);
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
