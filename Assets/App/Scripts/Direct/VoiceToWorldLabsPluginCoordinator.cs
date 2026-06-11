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
        private CancellationTokenSource _generationCts;
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
            if (_activeFlow != null)
            {
                StopCoroutine(_activeFlow);
                _activeFlow = null;
            }

            _generationCts?.Cancel();
            _generationCts?.Dispose();
            _generationCts = null;

            isBusy = false;
            ClearLongRunningWatch();
        }

        public void TriggerWorldGeneration(string prompt)
        {
            if (isBusy)
            {
                PublishWarning(BusyMessage, speak: true);
                return;
            }
            if (string.IsNullOrWhiteSpace(prompt)) return;
            if (!WorldLabsApiConfig.IsWorldLabsConfigured())
            {
                ArchStatusBus.Warning("WorldLabs API key missing. Set WORLDLABS_API_KEY in .env or on the service.", "WORLD");
                return;
            }

            if (stateMachine.CurrentState == HolodeckState.Error)
                stateMachine.ClearErrorAndReturnToIdle();

            ResetDebugFields();

            _generationCts?.Cancel();
            _generationCts?.Dispose();
            _generationCts = new CancellationTokenSource();

            if (_activeFlow != null)
                StopCoroutine(_activeFlow);

            isBusy = true;
            BeginLongRunningWatch(prompt);
            _activeFlow = StartCoroutine(RunGenerationFlow(prompt));
        }

        public void TriggerWorldGenerationFromLastCapture(string prompt)
        {
            if (isBusy)
            {
                PublishWarning(BusyMessage, speak: true);
                return;
            }
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

            if (stateMachine.CurrentState == HolodeckState.Error)
                stateMachine.ClearErrorAndReturnToIdle();

            ResetDebugFields();

            _generationCts?.Cancel();
            _generationCts?.Dispose();
            _generationCts = new CancellationTokenSource();

            if (_activeFlow != null)
                StopCoroutine(_activeFlow);

            isBusy = true;
            BeginLongRunningWatch(prompt);
            _activeFlow = StartCoroutine(RunGenerationFlow(prompt, capture));
        }

        public bool CancelActiveGeneration(string reason = null)
        {
            if (!isBusy)
                return false;

            if (_activeFlow != null)
            {
                StopCoroutine(_activeFlow);
                _activeFlow = null;
            }

            _generationCts?.Cancel();
            _generationCts?.Dispose();
            _generationCts = null;

            isBusy = false;
            string message = string.IsNullOrWhiteSpace(reason) ? "Cancelled world generation." : reason;
            PublishWarning(message, speak: true);
            stateMachine?.TryTransitionTo(HolodeckState.Idle);
            ClearLongRunningWatch();
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

        private IEnumerator RunGenerationFlow(string prompt, Texture2D imagePrompt = null)
        {
            lastPromptUsed = prompt;

            if (!stateMachine.TryTransitionTo(HolodeckState.Generating))
            {
                isBusy = false;
                stateMachine.SetError("Could not transition to Generating.");
                yield break;
            }

            Task<World> generationTask = GenerateWorldAsync(lastPromptUsed, _generationCts.Token, imagePrompt);
            while (!generationTask.IsCompleted)
            {
                if (ShouldAbortForCancelOrTimeout("world generation"))
                    yield break;
                MaybePromptForLongWait();
                yield return null;
            }

            if (_generationCts == null || _generationCts.IsCancellationRequested)
            {
                isBusy = false;
                ClearLongRunningWatch();
                yield break;
            }

            if (generationTask.IsFaulted)
            {
                isBusy = false;
                Exception baseException = generationTask.Exception?.GetBaseException();
                Debug.LogError($"[VoiceToWorldLabsPluginCoordinator] Generation failed.\n{generationTask.Exception}", this);
                stateMachine.SetError(baseException != null ? baseException.Message : "Unknown generation error.");
                ClearLongRunningWatch();
                yield break;
            }

            World world = generationTask.Result;
            lastWorldId = world != null ? world.world_id : string.Empty;

            // Re-fetch world if pano_url is missing from generation response.
            WorldLabsClient assetClient = new WorldLabsClient();
            if (!string.IsNullOrEmpty(lastWorldId) && string.IsNullOrEmpty(world?.assets?.imagery?.pano_url))
            {
                Debug.Log($"[VoiceToWorldLabsPluginCoordinator] pano_url missing from generation response — re-fetching world '{lastWorldId}'.", this);
                Task<World> refetchTask = assetClient.GetWorldAsync(lastWorldId);
                while (!refetchTask.IsCompleted)
                {
                    if (ShouldAbortForCancelOrTimeout("world refresh"))
                        yield break;
                    MaybePromptForLongWait();
                    yield return null;
                }
                if (!refetchTask.IsFaulted && !refetchTask.IsCanceled && refetchTask.Result != null)
                {
                    world = refetchTask.Result;
                    Debug.Log($"[VoiceToWorldLabsPluginCoordinator] Re-fetched world. pano_url='{world?.assets?.imagery?.pano_url}'", this);
                }
                else
                {
                    Debug.LogWarning($"[VoiceToWorldLabsPluginCoordinator] Re-fetch failed ({refetchTask.Exception?.GetBaseException().Message}); will attempt panorama download with original world.", this);
                }
            }

            // Download panorama and show as skybox preview while the splat loads.
            Debug.Log($"[VoiceToWorldLabsPluginCoordinator] Starting panorama download. pano_url='{world?.assets?.imagery?.pano_url}'", this);
            Task<Texture2D> thumbnailTask = assetClient.DownloadPanoramaAsync(world);
            while (!thumbnailTask.IsCompleted)
            {
                if (ShouldAbortForCancelOrTimeout("panorama download"))
                    yield break;
                MaybePromptForLongWait();
                yield return null;
            }

            if (_generationCts == null || _generationCts.IsCancellationRequested)
            {
                if (!thumbnailTask.IsFaulted && !thumbnailTask.IsCanceled && thumbnailTask.Result != null)
                    Destroy(thumbnailTask.Result);
                isBusy = false;
                ClearLongRunningWatch();
                yield break;
            }

            if (!thumbnailTask.IsFaulted && !thumbnailTask.IsCanceled && thumbnailTask.Result != null)
            {
                Debug.Log($"[VoiceToWorldLabsPluginCoordinator] Panorama downloaded ({thumbnailTask.Result.width}x{thumbnailTask.Result.height}). thumbnailSkybox={(thumbnailSkybox != null ? "assigned" : "NULL — not wired in Inspector")}", this);
                thumbnailSkybox?.Show(thumbnailTask.Result);
            }
            else
            {
                string reason = thumbnailTask.IsFaulted
                    ? thumbnailTask.Exception?.GetBaseException().Message
                    : thumbnailTask.IsCanceled ? "cancelled" : "null texture";
                Debug.LogWarning($"[VoiceToWorldLabsPluginCoordinator] Panorama download failed ({reason}); skipping skybox preview.", this);
            }

            if (panoramaOnly)
            {
                stateMachine.TryTransitionTo(HolodeckState.Ready);
                _generationCts?.Dispose();
                _generationCts = null;
                isBusy = false;
                _activeFlow = null;
                ClearLongRunningWatch();
                yield break;
            }

            if (floorLoader != null)
            {
                // ── Floor-loader path ─────────────────────────────────────────────
                Debug.Log($"[VoiceToWorldLabsPluginCoordinator] Floor-loader path: worldId={lastWorldId}", this);
                string spzUrl = ResolveSpzUrl(world);
                if (string.IsNullOrEmpty(spzUrl))
                {
                    isBusy = false;
                    stateMachine.SetError("No SPZ URL found in world assets.");
                    ClearLongRunningWatch();
                    yield break;
                }

                Debug.Log($"[VoiceToWorldLabsPluginCoordinator] SPZ URL resolved: {spzUrl}", this);

                yield return worldManager.PrepareForWorldLoadCoroutine();
                worldManager.NotifyWorldLoadStarted(lastWorldId);

                Debug.Log("[VoiceToWorldLabsPluginCoordinator] SPZ download starting…", this);
                Task<byte[]> spzTask = WorldLabsClientExtensions.DownloadBinaryAsync(spzUrl);
                while (!spzTask.IsCompleted)
                {
                    if (ShouldAbortForCancelOrTimeout("splat download"))
                        yield break;
                    MaybePromptForLongWait();
                    yield return null;
                }

                if (spzTask.IsFaulted)
                {
                    isBusy = false;
                    Debug.LogError($"[VoiceToWorldLabsPluginCoordinator] SPZ download failed: {spzTask.Exception?.GetBaseException().Message}", this);
                    worldManager.NotifyWorldLoadFailed(lastWorldId, spzTask.Exception?.GetBaseException().Message ?? "SPZ download failed.");
                    stateMachine.SetError(spzTask.Exception?.GetBaseException().Message ?? "SPZ download failed.");
                    ClearLongRunningWatch();
                    yield break;
                }

                byte[] spzBytes = spzTask.Result;
                Debug.Log($"[VoiceToWorldLabsPluginCoordinator] SPZ download complete: {spzBytes.Length} bytes", this);

                if (_generationCts == null || _generationCts.IsCancellationRequested)
                {
                    Debug.Log("[VoiceToWorldLabsPluginCoordinator] Cancelled after SPZ download — aborting floor load.", this);
                    isBusy = false;
                    ClearLongRunningWatch();
                    yield break;
                }

                Task<RuntimeSplatFloorLoader.LoadResult> placeTask = floorLoader.LoadPlacedRuntimeWorldAsync(
                    spzBytes,
                    worldId:        lastWorldId,
                    worldName:      world.display_name,
                    thumbnailUrl:   world.assets?.thumbnail_url,
                    gameObjectName: $"World_{world.display_name ?? lastWorldId}",
                    sourceKind:     RuntimeSplatFloorLoader.SplatSourceKind.WorldLabs);

                while (!placeTask.IsCompleted)
                {
                    if (ShouldAbortForCancelOrTimeout("world placement"))
                        yield break;
                    MaybePromptForLongWait();
                    yield return null;
                }

                if (placeTask.IsFaulted)
                {
                    isBusy = false;
                    Debug.LogError($"[VoiceToWorldLabsPluginCoordinator] Floor load failed: {placeTask.Exception?.GetBaseException().Message}", this);
                    worldManager.NotifyWorldLoadFailed(lastWorldId, placeTask.Exception?.GetBaseException().Message ?? "Floor load failed.");
                    stateMachine.SetError(placeTask.Exception?.GetBaseException().Message ?? "Floor load failed.");
                    ClearLongRunningWatch();
                    yield break;
                }

                RuntimeSplatFloorLoader.LoadResult placed = placeTask.Result;
                Debug.Log($"[VoiceToWorldLabsPluginCoordinator] Floor load complete: renderer={placed.renderer != null}", this);

                if (placed.floorEstimate != null)
                {
                    SplatFloorEstimate est = placed.floorEstimate;
                    Debug.Log($"[VoiceToWorldLabsPluginCoordinator] Floor analysis: success={est.success} floorY={est.estimatedFloorY:F3} message='{est.message}'", this);
                }

                worldManager.RegisterExternalWorld(lastWorldId, placed.renderer, world);
                Debug.Log("[VoiceToWorldLabsPluginCoordinator] RegisterExternalWorld called — world live.", this);
            }
            else
            {
                // ── Standard WorldLabsWorldManager load ───────────────────────────
                Task loadTask = worldManager.LoadWorldAsync(world);
                while (!loadTask.IsCompleted)
                {
                    if (ShouldAbortForCancelOrTimeout("world load"))
                        yield break;
                    MaybePromptForLongWait();
                    yield return null;
                }

                if (loadTask.IsFaulted)
                {
                    isBusy = false;
                    Exception baseException = loadTask.Exception?.GetBaseException();
                    stateMachine.SetError(baseException != null ? baseException.Message : "World load failed.");
                    ClearLongRunningWatch();
                    yield break;
                }
            }

            if (!stateMachine.TryTransitionTo(HolodeckState.Ready))
            {
                isBusy = false;
                stateMachine.SetError("Could not transition to Ready.");
                ClearLongRunningWatch();
                yield break;
            }

            if (logDebugMessages)
                Debug.Log($"World loaded successfully. WorldId={lastWorldId}", this);

            thumbnailSkybox?.StartFadeOut();
            _generationCts?.Dispose();
            _generationCts = null;
            isBusy = false;
            _activeFlow = null;
            ClearLongRunningWatch();
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
                    if (progress?.error != null)
                        Debug.LogWarning($"[WorldLabs] Poll error: code={progress.error.code}, message='{progress.error.message}'");
                    else if (progress != null)
                        Debug.Log($"[WorldLabs] Poll: done={progress.done}");
                });

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
            if (_generationCts == null || _generationCts.IsCancellationRequested)
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
