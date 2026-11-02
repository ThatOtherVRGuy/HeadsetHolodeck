using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Holodeck.State;
using SpeechIntent;
using WorldLabs.API;
using WorldLabs.Runtime;
using WorldLabs.Runtime.Tools;

namespace Holodeck.Direct
{
    public sealed class VoiceToWorldLabsPluginCoordinator : MonoBehaviour
    {
        public static event Action<string> UserFacingStatus;

        [Header("Dependencies")]
        [SerializeField] private HolodeckStateMachine stateMachine;
        [SerializeField] private WorldLabsWorldManager worldManager;
        [SerializeField] private ThumbnailSkyboxController thumbnailSkybox;
        [SerializeField] private HeadsetCameraCaptureService headsetCameraCapture;

        [Header("Generation")]
        public bool panoramaOnly = false;
        [SerializeField] private MarbleModel generationModel = MarbleModel.Standard;
        [SerializeField] private float pollIntervalSeconds = 5f;
        [SerializeField] private float generationTimeoutSeconds = 600f;

        [Header("Long Running Jobs")]
        [Min(5f)] public float longWaitPromptSeconds = 180f;
        [Min(5f)] public float longWaitRepeatSeconds = 120f;
        [Min(10f)] public float hardTimeoutSeconds = 900f;

        [Header("Debug")]
        [SerializeField] private bool logDebugMessages = true;

        [Header("Floor Detection")]
        [SerializeField] private RuntimeSplatFloorLoader floorLoader;

        [Header("Runtime Debug")]
        [SerializeField, TextArea] private string lastPromptUsed = string.Empty;
        [SerializeField] private string lastWorldId = string.Empty;
        [SerializeField] private bool isBusy;

        private Coroutine _activeFlow;
        private WorldTransitionOperation _operation;
        private float _activeJobStartedAt;
        private float _nextLongWaitPromptAt;
        private string _activeWorldPrompt = string.Empty;

        public string LastPromptUsed => lastPromptUsed;
        public string LastWorldId => lastWorldId;
        public bool IsBusy => isBusy;
        public string BusyMessage => string.IsNullOrWhiteSpace(_activeWorldPrompt)
            ? "World generation is already running. Please wait."
            : $"World generation is already running for {_activeWorldPrompt}. Please wait.";
        public MarbleModel CurrentGenerationModel => generationModel;
        public string CurrentGenerationModelName => GenerationModelLabel(generationModel);
        public event Action<MarbleModel> OnGenerationModelChanged;

        public void SetGenerationModel(MarbleModel model)
        {
            if (generationModel == model)
                return;

            generationModel = model;
            OnGenerationModelChanged?.Invoke(model);
        }

        private void Awake()
        {
            if (stateMachine == null)
            {
                Debug.LogError($"{nameof(VoiceToWorldLabsPluginCoordinator)} is missing a HolodeckStateMachine.", this);
            }

            if (worldManager == null)
            {
                Debug.LogError($"{nameof(VoiceToWorldLabsPluginCoordinator)} is missing a WorldLabsWorldManager.", this);
            }
        }

        private void OnEnable()
        {
        }

        private void OnDisable()
        {
            StopLocalGeneration();
        }

        void StopLocalGeneration()
        {
            var operation = _operation;
            var flow = _activeFlow;
            // Detach before cancellation callbacks, which are allowed to start another request.
            _operation = null;
            _activeFlow = null;
            isBusy = false;
            ClearLongRunningWatch();
            if (flow != null) StopCoroutine(flow);
            if (operation != null && !operation.IsCompleted) worldManager?.CancelTransition(operation);
        }

        void StartGeneration(string prompt, Texture2D capture = null)
        {
            if (!isActiveAndEnabled || worldManager == null || stateMachine == null) return;
            StopLocalGeneration();
            if (_operation != null) return; // A cancellation listener has already started a newer request.
            if (stateMachine.CurrentState == HolodeckState.Error) stateMachine.ClearErrorAndReturnToIdle();
            var operation = worldManager.BeginTransition();
            if (!operation.IsCurrent) return;
            _operation = operation;
            ResetDebugFields();
            isBusy = true;
            BeginLongRunningWatch(prompt);
            if (_operation != operation || !operation.IsCurrent) return;
            Task task = RunGenerationAsync(prompt, capture, operation);
            if (_operation == operation && !task.IsCompleted) _activeFlow = StartCoroutine(WatchGeneration(task));
        }

        IEnumerator WatchGeneration(Task task)
        {
            while (!task.IsCompleted)
            {
                if (ShouldAbortForCancelOrTimeout("world generation or loading")) yield break;
                MaybePromptForLongWait();
                yield return null;
            }
            _activeFlow = null;
        }

        public void TriggerWorldGeneration(string prompt)
        {
            if (string.IsNullOrWhiteSpace(prompt)) return;
            if (!WorldLabsApiConfig.IsWorldLabsConfigured())
            {
                ArchStatusBus.Warning("WorldLabs API key missing. Set WORLDLABS_API_KEY in .env or on the service.", "WORLD");
                return;
            }

            StartGeneration(prompt);
        }

        public void TriggerWorldGenerationFromLastCapture(string prompt)
        {
            if (!WorldLabsApiConfig.IsWorldLabsConfigured())
            {
                ArchStatusBus.Warning("WorldLabs API key missing. Set WORLDLABS_API_KEY in .env or on the service.", "WORLD");
                return;
            }

            if (headsetCameraCapture == null)
                headsetCameraCapture = FindFirstObjectByType<HeadsetCameraCaptureService>();

            Texture2D capture = headsetCameraCapture != null ? headsetCameraCapture.LastCapturedTexture : null;
            if (capture == null)
            {
                string message = "No captured headset camera image is ready.";
                Debug.LogWarning($"[VoiceToWorldLabsPluginCoordinator] {message}", this);
                ArchStatusBus.Warning(message, "CAPTURE");
                return;
            }

            if (string.IsNullOrWhiteSpace(prompt))
                prompt = "Create a world inspired by this image.";

            StartGeneration(prompt, capture);
        }

        public bool CancelActiveGeneration(string reason = null)
        {
            if (!isBusy) return false;
            var cancelled = _operation;
            StopLocalGeneration();
            if (_operation == null && (worldManager == null || worldManager.CurrentTransition == cancelled))
            {
                stateMachine?.TryTransitionTo(HolodeckState.Idle);
                string message = string.IsNullOrWhiteSpace(reason)
                    ? "Stopped waiting for world generation. The remote job may continue." : reason;
                PublishWarning(message, speak: true);
            }
            return true;
        }

        public bool ContinueActiveGeneration()
        {
            if (!isBusy)
                return false;

            _nextLongWaitPromptAt = Time.realtimeSinceStartup + Mathf.Max(5f, longWaitRepeatSeconds);
            PublishInfo($"Okay, I will keep waiting for {FirstNonEmpty(_activeWorldPrompt, "the world")}.", speak: true);
            return true;
        }

        private async Task RunGenerationAsync(string prompt, Texture2D imagePrompt, WorldTransitionOperation operation)
        {
            string worldId = null;
            Texture2D preview = null;
            try
            {
                operation.ThrowIfStale();
                lastPromptUsed = prompt;
                if (!stateMachine.TryTransitionTo(HolodeckState.Generating))
                    throw new InvalidOperationException("Could not transition to Generating.");
                World world = await operation.AwaitAsync(GenerateWorldAsync(prompt, operation.Token, imagePrompt));
                if (world == null) throw new InvalidOperationException("World Labs returned no world.");
                worldId = world.world_id;
                lastWorldId = worldId;
                var assetClient = new WorldLabsClient();
                if (!string.IsNullOrEmpty(worldId) && string.IsNullOrEmpty(world.assets?.imagery?.pano_url))
                {
                    try
                    {
                        world = await operation.AwaitAsync(assetClient.GetWorldAsync(worldId, operation.Token)) ?? world;
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex) { Debug.LogWarning($"[VoiceToWorldLabsPluginCoordinator] World refresh failed: {ex.Message}"); }
                }
                operation.ThrowIfStale();
                try
                {
                    preview = await assetClient.DownloadPanoramaAsync(world, operation.Token);
                    operation.Track(preview);
                    operation.ThrowIfStale();
                    var skyboxOwner = thumbnailSkybox;
                    var ownedPreview = preview;
                    operation.Token.Register(() => { if (skyboxOwner != null) skyboxOwner.ClearStored(ownedPreview); });
                    thumbnailSkybox?.Show(preview);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { Debug.LogWarning($"[VoiceToWorldLabsPluginCoordinator] Panorama unavailable: {ex.Message}"); }

                operation.ThrowIfStale();
                if (!panoramaOnly)
                {
                    if (floorLoader != null)
                    {
                        worldManager.NotifyWorldLoadStarted(worldId, operation);
                        await worldManager.PrepareForWorldLoadAsync(operation);
                        operation.ThrowIfStale();
                        string url = ResolveSpzUrl(world);
                        if (string.IsNullOrEmpty(url)) throw new InvalidOperationException("No SPZ URL found in world assets.");
                        byte[] bytes = await WorldLabsClientExtensions.DownloadBinaryAsync(url, operation.Token);
                        operation.ThrowIfStale();
                        // Placement owns its renderer from creation, so cancellation during its
                        // final yield destroys content even if this owner has stopped waiting.
                        var placed = await floorLoader.LoadPlacedRuntimeWorldAsync(bytes,
                            worldId: worldId, worldName: world.display_name,
                            thumbnailUrl: world.assets?.thumbnail_url,
                            gameObjectName: $"World_{world.display_name ?? worldId}",
                            sourceKind: RuntimeSplatFloorLoader.SplatSourceKind.WorldLabs,
                            operation: operation);
                        operation.ThrowIfStale();
                        if (!worldManager.RegisterExternalWorld(worldId, placed.renderer, world, operation))
                            throw new OperationCanceledException(operation.Token);
                    }
                    else
                    {
                        var renderer = await worldManager.LoadWorldAsync(world, operation);
                        operation.ThrowIfStale();
                        if (renderer == null) throw new InvalidOperationException("World load failed.");
                    }
                }
                operation.ThrowIfStale();
                if (!stateMachine.TryTransitionTo(HolodeckState.Ready))
                    throw new InvalidOperationException("Could not transition to Ready.");
                operation.ThrowIfStale();
                if (!panoramaOnly) thumbnailSkybox?.StartFadeOut();
                worldManager.CompleteTransition(operation);
                if (logDebugMessages) Debug.Log($"World loaded successfully. WorldId={worldId}", this);
            }
            catch (OperationCanceledException)
            {
                if (thumbnailSkybox != null && preview != null) thumbnailSkybox.ClearStored(preview);
            }
            catch (Exception ex)
            {
                if (operation.IsCurrent)
                {
                    if (!string.IsNullOrEmpty(worldId)) worldManager.NotifyWorldLoadFailed(worldId, ex.Message, operation);
                    if (operation.IsCurrent) stateMachine?.SetError(ex.Message);
                    worldManager.CancelTransition(operation);
                    if (thumbnailSkybox != null && preview != null) thumbnailSkybox.ClearStored(preview);
                }
            }
            finally
            {
                if (_operation == operation)
                {
                    isBusy = false;
                    ClearLongRunningWatch();
                }
            }
        }

        private async Task<World> GenerateWorldAsync(string prompt, CancellationToken cancellationToken, Texture2D imagePrompt = null)
        {
            if (worldManager == null)
            {
                throw new InvalidOperationException("WorldLabsWorldManager is not assigned.");
            }

            cancellationToken.ThrowIfCancellationRequested();

            MarbleModel model = generationModel;

            WorldLabsClient client = new WorldLabsClient();
            GenerateWorldResponse generationResponse = imagePrompt != null
                ? await client.GenerateWorldFromTextureAsync(
                    texture: imagePrompt,
                    textPrompt: prompt,
                    isPano: false,
                    displayName: BuildDisplayName(prompt),
                    model: model)
                : await client.GenerateWorldFromTextAsync(
                    textPrompt: prompt,
                    displayName: BuildDisplayName(prompt),
                    model: model,
                    tags: null,
                    seed: null,
                    isPublic: false);

            if (generationResponse == null || string.IsNullOrWhiteSpace(generationResponse.operation_id))
            {
                throw new Exception(generationResponse == null
                    ? "World Labs returned a null generation response."
                    : "World Labs did not return a valid operation ID.");
            }

            cancellationToken.ThrowIfCancellationRequested();

            GetOperationResponse operation = await client.WaitForOperationAsync(
                generationResponse.operation_id,
                pollIntervalSeconds,
                generationTimeoutSeconds,
                progress =>
                {
                    // The API returns an empty error object on healthy polls, so treating any non-null
                    // error as a failure made a working multi-minute generation look broken.
                    bool hasError = progress?.error != null &&
                                    ((progress.error.code ?? 0) != 0 ||
                                     !string.IsNullOrWhiteSpace(progress.error.message));

                    if (hasError)
                    {
                        Debug.LogWarning($"[WorldLabs] Poll error: code={progress.error.code}, message='{progress.error.message}'");
                    }
                    else if (progress != null)
                    {
                        // metadata.progress is {status, description}, not a percentage.
                        string state = progress.metadata?.progress?.status;
                        string detail = progress.metadata?.progress?.description;
                        Debug.Log($"[WorldLabs] Poll: done={progress.done}" +
                                  (string.IsNullOrEmpty(state) ? string.Empty : $" status={state}") +
                                  (string.IsNullOrEmpty(detail) ? string.Empty : $" ({detail})"));
                    }
                }, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            if (operation == null)
            {
                throw new Exception("World Labs returned no operation result.");
            }

            if (operation.error != null && !string.IsNullOrWhiteSpace(operation.error.message))
            {
                throw new Exception(operation.error.message);
            }

            if (operation.response == null)
            {
                throw new Exception("World Labs operation completed without a world payload.");
            }

            return operation.response;
        }

        public static string GenerationModelLabel(MarbleModel model) => model switch
        {
            MarbleModel.Draft    => "Draft",
            MarbleModel.Fast     => "Fast",
            MarbleModel.Standard => "Standard",
            MarbleModel.High     => "High",
            _                    => model.ToString()
        };

        private static string BuildDisplayName(string prompt)
        {
            string cleaned = prompt.Replace("\n", " ").Trim();
            if (cleaned.Length > 48)
            {
                cleaned = cleaned.Substring(0, 48);
            }

            return string.IsNullOrWhiteSpace(cleaned) ? "Holodeck World" : cleaned;
        }

        private void ResetDebugFields()
        {
            lastPromptUsed = string.Empty;
            lastWorldId = string.Empty;
        }

        private void BeginLongRunningWatch(string prompt)
        {
            _activeWorldPrompt = string.IsNullOrWhiteSpace(prompt) ? "world" : prompt.Trim();
            _activeJobStartedAt = Time.realtimeSinceStartup;
            _nextLongWaitPromptAt = _activeJobStartedAt + Mathf.Max(5f, longWaitPromptSeconds);
            PublishInfo($"Started world generation for {FirstNonEmpty(_activeWorldPrompt, "world")} with WorldLabs.");
        }

        private void ClearLongRunningWatch()
        {
            _activeJobStartedAt = 0f;
            _nextLongWaitPromptAt = 0f;
            _activeWorldPrompt = string.Empty;
        }

        private bool ShouldAbortForCancelOrTimeout(string phase)
        {
            if (_operation == null || !_operation.IsCurrent)
            {
                isBusy = false;
                ClearLongRunningWatch();
                return true;
            }

            if (hardTimeoutSeconds > 0f && Time.realtimeSinceStartup - _activeJobStartedAt >= hardTimeoutSeconds)
            {
                CancelActiveGeneration($"World generation timed out during {phase}. Please try again later.");
                return true;
            }

            return false;
        }

        private void MaybePromptForLongWait()
        {
            if (_nextLongWaitPromptAt <= 0f || Time.realtimeSinceStartup < _nextLongWaitPromptAt)
                return;

            PublishWarning($"Still creating {FirstNonEmpty(_activeWorldPrompt, "the world")} with WorldLabs. Say continue waiting or cancel world generation.", speak: true);
            _nextLongWaitPromptAt = Time.realtimeSinceStartup + Mathf.Max(5f, longWaitRepeatSeconds);
        }

        private static void PublishInfo(string message, bool speak = false)
        {
            Debug.Log("[VoiceToWorldLabsPluginCoordinator] " + message);
            ArchStatusBus.Info(message, "WORLD");
            if (speak)
                UserFacingStatus?.Invoke(message);
        }

        private static void PublishWarning(string message, bool speak = false)
        {
            Debug.LogWarning("[VoiceToWorldLabsPluginCoordinator] " + message);
            ArchStatusBus.Warning(message, "WORLD");
            if (speak)
                UserFacingStatus?.Invoke(message);
        }

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (string value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }
            return "";
        }

        private string ResolveSpzUrl(World world)
        {
            string resKey = worldManager.preferredResolution switch
            {
                WorldLabsWorldManager.SplatResolution.FullRes => "full_res",
                WorldLabsWorldManager.SplatResolution._100k   => "100k",
                _                                              => "500k",
            };
            return world.assets?.splats?.GetUrl(resKey)
                ?? world.assets?.splats?.GetBestResolutionUrl();
        }
    }
}
