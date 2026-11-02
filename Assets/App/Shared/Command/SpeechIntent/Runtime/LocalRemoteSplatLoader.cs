// Assets/App/Shared/Command/SpeechIntent/Runtime/LocalRemoteSplatLoader.cs

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GaussianSplatting.Runtime;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Networking;
using WorldLabs.Runtime;
using WorldLabs.Runtime.Tools;

namespace SpeechIntent
{
    /// <summary>
    /// Loads a Gaussian Splat file (.spz or .ply) from a local path or HTTP/HTTPS URL,
    /// applies floor placement, and registers it with <see cref="WorldLabsWorldManager"/>.
    /// </summary>
    public class LocalRemoteSplatLoader : MonoBehaviour
    {
        [Header("References")]
        public WorldLabsWorldManager   worldManager;
        public RuntimeSplatFloorLoader floorLoader;

        [Header("Local Storage")]
        [Tooltip("Base directory for local files. Partial filenames are resolved against this path.")]
        public string localBasePath = "";

        [Header("Events")]
        public StringEvent onLoadStarted;
        public StringEvent onLoadFailed;

        [Header("User Feedback")]
        [Tooltip("Optional TTS player used to speak concise load failure messages.")]
        public TtsPlayer voiceFeedback;

        void Awake()
        {
            if (string.IsNullOrWhiteSpace(localBasePath))
                localBasePath = Path.Combine(Application.persistentDataPath, "WorldContent");
        }

        /// <summary>
        /// Load an SPZ or PLY splat from a local path or URL.
        /// </summary>
        public void LoadAsync(string pathOrUrl, string displayName = null)
        {
            LoadAsync(pathOrUrl, displayName, RuntimeSplatFloorLoader.SplatSourceKind.LooseSplat);
        }

        /// <summary>
        /// Load an SPZ or PLY splat from a local path or URL with an explicit source orientation.
        /// </summary>
        public void LoadAsync(
            string pathOrUrl,
            string displayName,
            RuntimeSplatFloorLoader.SplatSourceKind sourceKind)
        {
            LoadAsync(pathOrUrl, displayName, sourceKind, null);
        }

        public void LoadAsync(
            string pathOrUrl,
            string displayName,
            RuntimeSplatFloorLoader.SplatSourceKind sourceKind,
            SplatSpawnMetadata savedSpawnMetadata)
        {
            _ = StartLoad(pathOrUrl, displayName, sourceKind, savedSpawnMetadata);
        }

        /// <summary>
        /// Starts a local or remote splat load and completes when that exact request has either
        /// registered its renderer or failed. This avoids relying on global world-manager events
        /// when a caller needs to await a particular cached-file load.
        /// </summary>
        public Task<bool> LoadAndWaitAsync(
            string pathOrUrl,
            string displayName,
            RuntimeSplatFloorLoader.SplatSourceKind sourceKind,
            SplatSpawnMetadata savedSpawnMetadata,
            Action<GameObject> beforeRendererCreated = null,
            WorldTransitionOperation operation = null)
        {
            return StartLoad(pathOrUrl, displayName, sourceKind, savedSpawnMetadata,
                beforeRendererCreated, operation);
        }

        sealed class LoadRequest : IDisposable
        {
            public readonly WorldTransitionOperation operation;
            public readonly bool ownsOperation;
            public readonly CancellationTokenSource cancellation;
            public readonly TaskCompletionSource<bool> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            readonly CancellationTokenRegistration registration;

            public LoadRequest(WorldTransitionOperation operation, bool ownsOperation)
            {
                this.operation = operation;
                this.ownsOperation = ownsOperation;
                cancellation = CancellationTokenSource.CreateLinkedTokenSource(operation.Token);
                // Settle callers promptly even if a parser or other underlying task ignores cancellation.
                // Unity cleanup remains on the captured Unity context in RunLoadAsync.
                registration = cancellation.Token.Register(() => completion.TrySetResult(false));
            }

            public void Dispose()
            {
                registration.Dispose();
                cancellation.Dispose();
            }
        }

        LoadRequest activeLoad;
        WorldTransitionOperation standaloneOperation;
        int requestVersion;

        void OnDisable() => CancelLoad();
        void OnDestroy() => CancelLoad();

        /// <summary>Stops local waiting and cleans up this loader's exact operation.</summary>
        public void CancelLoad()
        {
            requestVersion++;
            CancelPendingLoad();
        }

        void CancelPendingLoad()
        {
            LoadRequest request = activeLoad;
            WorldTransitionOperation standalone = standaloneOperation;
            // Detach both owners before callbacks can start another load.
            activeLoad = null;
            standaloneOperation = null;
            if (request != null)
            {
                if (worldManager != null)
                    worldManager.CancelTransition(request.operation);
                else
                    request.operation.Cancel();
            }
            standalone?.Cancel();
        }

        Task<bool> StartLoad(
            string pathOrUrl,
            string displayName,
            RuntimeSplatFloorLoader.SplatSourceKind sourceKind,
            SplatSpawnMetadata savedSpawnMetadata,
            Action<GameObject> beforeRendererCreated = null,
            WorldTransitionOperation operation = null)
        {
            if (!isActiveAndEnabled)
                return Task.FromResult(false);
            if (string.IsNullOrWhiteSpace(pathOrUrl))
            {
                Debug.LogWarning("[LocalRemoteSplatLoader] pathOrUrl is empty.");
                ArchStatusBus.Warning("No splat path or URL provided.", "LOAD");
                onLoadFailed?.Invoke("No path or URL provided.");
                return Task.FromResult(false);
            }

            // Manager-backed loads share one latest-request-wins policy with generation/restoration.
            // Cancel only our exact previous operation, never a newer request started elsewhere.
            int version = ++requestVersion;
            CancelPendingLoad();
            if (version != requestVersion || !isActiveAndEnabled)
                return Task.FromResult(false);
            bool ownsOperation = operation == null;
            if (operation == null)
            {
                operation = worldManager != null
                    ? worldManager.BeginTransition()
                    : (standaloneOperation = WorldTransitionOperation.CreateStandalone());
            }
            if (version != requestVersion || !operation.IsCurrent || !isActiveAndEnabled)
                return Task.FromResult(false);
            var request = new LoadRequest(operation, ownsOperation);
            activeLoad = request;
            _ = RunLoadAsync(pathOrUrl, displayName, sourceKind, savedSpawnMetadata,
                beforeRendererCreated, request);
            return request.completion.Task;
        }

        void ThrowIfStale(LoadRequest request)
        {
            request.cancellation.Token.ThrowIfCancellationRequested();
            request.operation.ThrowIfStale();
            if (this == null || !isActiveAndEnabled || !ReferenceEquals(activeLoad, request))
                throw new OperationCanceledException();
        }

        async Task RunLoadAsync(
            string pathOrUrl,
            string displayName,
            RuntimeSplatFloorLoader.SplatSourceKind sourceKind,
            SplatSpawnMetadata savedSpawnMetadata,
            Action<GameObject> beforeRendererCreated,
            LoadRequest request)
        {
            string worldId = null;
            bool loaded = false;
            RuntimeSplatFloorLoader.LoadResult result = null;
            try
            {
                ThrowIfStale(request);
                string resolved = ResolveLocalPath(pathOrUrl);
                worldId = "local_" + Path.GetFileNameWithoutExtension(resolved) + "_" + DateTime.UtcNow.Ticks;
                string worldName = !string.IsNullOrWhiteSpace(displayName)
                    ? displayName : Path.GetFileNameWithoutExtension(resolved);
                bool isUrl = pathOrUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                          || pathOrUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
                string extension = Path.GetExtension(isUrl ? new Uri(pathOrUrl).AbsolutePath : resolved).ToLowerInvariant();
                if (extension != ".spz" && extension != ".ply")
                    throw new NotSupportedException($"Unsupported format '{extension}'. Expected .spz or .ply.");
                if (floorLoader == null)
                    throw new InvalidOperationException("RuntimeSplatFloorLoader not assigned.");

                onLoadStarted?.Invoke(worldId);
                ThrowIfStale(request);
                ArchStatusBus.Info("Loading splat " + worldName + ".", "LOAD");
                // Keep the static holodeck visible through I/O, processing and cleanup.
                worldManager?.NotifyWorldLoadStarted(worldId, request.operation);
                ThrowIfStale(request);
                if (worldManager != null)
                    await worldManager.PrepareForWorldLoadAsync(request.operation);
                ThrowIfStale(request);

                byte[] bytes = await AwaitOrCancelAsync(
                    ReadBytesAsync(resolved, isUrl, request.cancellation.Token), request.cancellation.Token);
                ThrowIfStale(request);
                result = await AwaitOrCancelAsync(
                    PlaceWorldAsync(bytes, extension, worldId, worldName, sourceKind,
                        savedSpawnMetadata, beforeRendererCreated, request.operation, request.cancellation.Token),
                    request.cancellation.Token, DestroyLateResult);
                if (result?.gameObject != null)
                    request.operation.Track(result.gameObject);
                ThrowIfStale(request);
                if (result?.renderer == null)
                    throw new InvalidOperationException("Splat placement returned no renderer.");

                Camera activeCamera = Camera.main;
                Debug.Log("[LocalRemoteSplatLoader] " + BuildRendererDiagnostics("before-registration", result.renderer, activeCamera));
                // Let URP render a frame before the manager hides the static holodeck.
                await Task.Yield();
                ThrowIfStale(request);
                Debug.Log("[LocalRemoteSplatLoader] " + BuildRendererDiagnostics("after-first-render-frame", result.renderer, activeCamera));
                if (worldManager != null && !worldManager.RegisterExternalWorld(worldId, result.renderer, null, request.operation))
                    throw new OperationCanceledException();
                ThrowIfStale(request);
                // Caller-owned operations continue through saved objects/audio and ActiveConfig commit.
                if (request.ownsOperation)
                    worldManager?.CompleteTransition(request.operation);
                ThrowIfStale(request);
                Debug.Log($"[LocalRemoteSplatLoader] Loaded '{worldName}' ({extension}) as worldId='{worldId}'.");
                ArchStatusBus.Success("Loaded splat " + worldName + ".", "READY");
                ThrowIfStale(request);
                loaded = true;
                request.completion.TrySetResult(true);
            }
            catch (OperationCanceledException)
            {
                request.completion.TrySetResult(false);
            }
            catch (Exception exception)
            {
                if (this != null && isActiveAndEnabled && ReferenceEquals(activeLoad, request) && request.operation.IsCurrent)
                {
                    try { ReportLoadFailureCore(worldId, "Load failed: " + exception.GetBaseException().Message,
                        "Could not finish loading that splat.", request.operation); }
                    catch (Exception feedbackError) { Debug.LogException(feedbackError); }
                }
                request.completion.TrySetResult(false);
            }
            finally
            {
                try
                {
                    if (!loaded)
                    {
                        DestroyLateResult(result);
                        // A restore may try another source after a failed cache attempt.
                        // Its caller owns the eventual cancellation or commit decision.
                        if (request.ownsOperation)
                        {
                            if (worldManager != null)
                                worldManager.CancelTransition(request.operation);
                            else
                                request.operation.Cancel();
                        }
                    }
                }
                catch (Exception cleanupError) { Debug.LogException(cleanupError); }
                finally
                {
                    if (ReferenceEquals(activeLoad, request))
                        activeLoad = null;
                    request.completion.TrySetResult(false);
                    request.Dispose();
                }
            }
        }

        protected virtual async Task<byte[]> ReadBytesAsync(string resolvedPath, bool isUrl, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (!isUrl)
                return await Task.Run(() => File.ReadAllBytes(resolvedPath));

            using (UnityWebRequest request = UnityWebRequest.Get(resolvedPath))
            {
                UnityWebRequestAsyncOperation download = request.SendWebRequest();
                try
                {
                    while (!download.isDone)
                    {
                        token.ThrowIfCancellationRequested();
                        await Task.Yield();
                    }
                    token.ThrowIfCancellationRequested();
                    if (request.result != UnityWebRequest.Result.Success)
                        throw new IOException("Download failed: " + request.error);
                    return request.downloadHandler.data;
                }
                finally
                {
                    if (!download.isDone)
                        request.Abort();
                }
            }
        }

        protected virtual async Task<RuntimeSplatFloorLoader.LoadResult> PlaceWorldAsync(
            byte[] bytes, string extension, string worldId, string worldName,
            RuntimeSplatFloorLoader.SplatSourceKind sourceKind, SplatSpawnMetadata metadata,
            Action<GameObject> beforeRendererCreated, WorldTransitionOperation operation,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (extension == ".spz")
                return await floorLoader.LoadPlacedRuntimeWorldAsync(bytes, worldId, worldName,
                    gameObjectName: worldName, sourceKind: sourceKind, savedSpawnMetadata: metadata,
                    beforeRendererCreated: beforeRendererCreated, cancellationToken: token, operation: operation);

            Task<NativeArray<InputSplatData>> parseTask = Task.Run(() =>
            {
                RuntimePlyReader.ReadFromBytes(bytes, out NativeArray<InputSplatData> splats);
                return splats;
            });
            NativeArray<InputSplatData> parsed = await AwaitOrCancelAsync(parseTask, token,
                splats => { if (splats.IsCreated) splats.Dispose(); });
            // The floor method takes ownership, including early cancellation/error paths.
            return await floorLoader.LoadPlacedRuntimeWorldFromSplatsAsync(parsed, worldId, worldName,
                gameObjectName: worldName, sourceKind: sourceKind, savedSpawnMetadata: metadata,
                beforeRendererCreated: beforeRendererCreated, cancellationToken: token, operation: operation);
        }

        static async Task<T> AwaitOrCancelAsync<T>(Task<T> task, CancellationToken token, Action<T> discard = null)
        {
            var canceled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (token.Register(() => canceled.TrySetResult(true)))
            {
                if (await Task.WhenAny(task, canceled.Task) != task)
                {
                    _ = ObserveLateResultAsync(task, discard);
                    throw new OperationCanceledException(token);
                }
            }
            // Return completed results to the caller even if cancellation raced the completion;
            // the next guard either tracks/destroys the renderer or disposes the parsed splats.
            return await task;
        }

        static async Task ObserveLateResultAsync<T>(Task<T> task, Action<T> discard)
        {
            try
            {
                T result = await task;
                discard?.Invoke(result);
            }
            catch (Exception) { /* Abandoned work is observed; it cannot publish another operation's error. */ }
        }

        static void DestroyLateResult(RuntimeSplatFloorLoader.LoadResult result)
        {
            if (result?.gameObject == null)
                return;
            result.gameObject.SetActive(false);
            if (Application.isPlaying) Destroy(result.gameObject);
            else DestroyImmediate(result.gameObject);
        }

        static string BuildRendererDiagnostics(string phase, GaussianSplatRenderer renderer, Camera camera)
        {
            if (renderer == null)
                return $"Splat diagnostics phase={phase}; renderer=<null>; camera={(camera != null ? camera.name : "<null>")}.";

            Transform splatTransform = renderer.transform;
            string cameraState = camera == null
                ? "camera=<null>"
                : $"camera={camera.name}; cameraPos={camera.transform.position}; cameraRot={camera.transform.rotation.eulerAngles}";
            return $"Splat diagnostics phase={phase}; renderer={renderer.name}; active={renderer.isActiveAndEnabled}; " +
                   $"runtimeData={renderer.HasValidRuntimeData}; renderSetup={renderer.HasValidRenderSetup}; splatCount={renderer.splatCount}; " +
                   $"splatPos={splatTransform.position}; splatRot={splatTransform.rotation.eulerAngles}; splatScale={splatTransform.lossyScale}; {cameraState}.";
        }

        void ReportLoadFailure(string worldId, string detailedMessage, string spokenMessage = null)
        {
            ReportLoadFailureCore(worldId, detailedMessage, spokenMessage, null);
        }

        void ReportLoadFailureCore(string worldId, string detailedMessage, string spokenMessage,
            WorldTransitionOperation operation)
        {
            if (operation != null && !operation.IsCurrent)
                return;
            string safeDetail = string.IsNullOrWhiteSpace(detailedMessage)
                ? "Splat load failed."
                : detailedMessage.Trim();

            Debug.LogError($"[LocalRemoteSplatLoader] {safeDetail}");
            ArchStatusBus.Error(safeDetail, "LOAD");
            worldManager?.NotifyWorldLoadFailed(worldId, safeDetail, operation);
            if (operation != null && !operation.IsCurrent)
                return;
            onLoadFailed?.Invoke(safeDetail);
            if (operation != null && !operation.IsCurrent)
                return;

            if (voiceFeedback != null)
            {
                string speech = string.IsNullOrWhiteSpace(spokenMessage)
                    ? "Splat load failed."
                    : spokenMessage.Trim();
                voiceFeedback.Say(speech);
            }
        }

        string ResolveLocalPath(string pathOrUrl)
        {
            // Sideloaded files are usually named, not pathed ("castle dot glb"), and may sit in a
            // subfolder. The resolver handles the URL/absolute/relative/search cases and reports which
            // one it used.
            if (SideloadPathResolver.TryResolve(pathOrUrl, out string resolved, out string note))
            {
                Debug.Log($"[LocalRemoteSplatLoader] Resolved '{pathOrUrl}' -> '{resolved}' ({note}).", this);
                return resolved;
            }

            if (pathOrUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
             || pathOrUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return pathOrUrl;  // URL — no resolution
            if (Path.IsPathRooted(pathOrUrl))
                return pathOrUrl;

            // Leave a missing relative path as-is so the loader reports the failure with its own message.
            return Path.Combine(localBasePath, pathOrUrl);
        }
    }
}
