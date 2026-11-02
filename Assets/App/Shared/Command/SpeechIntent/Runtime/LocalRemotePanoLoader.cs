// Assets/App/Shared/Command/SpeechIntent/Runtime/LocalRemotePanoLoader.cs

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Holodeck.Direct;
using UnityEngine;
using UnityEngine.Networking;
using WorldLabs.Runtime;

namespace SpeechIntent
{
    /// <summary>
    /// Loads an equirectangular panoramic image (.jpg, .png) from a local file path
    /// or HTTP/HTTPS URL and displays it via <see cref="ThumbnailSkyboxController"/>.
    /// </summary>
    public class LocalRemotePanoLoader : MonoBehaviour
    {
        [Header("References")]
        public ThumbnailSkyboxController thumbnailSkybox;
        public ViewModeController        viewModeController;
        public WorldLabsWorldManager     worldManager;

        [Header("Local Storage")]
        [Tooltip("Base directory for local files. Partial filenames are resolved against this path.")]
        public string localBasePath = "";

        [Header("Events")]
        public StringEvent onLoadStarted;
        public StringEvent onLoadFailed;

        /// <summary>
        /// Fired with the raw image bytes whenever a remote URL download succeeds (Load or Preload).
        /// Used by WorldConfigAutoSave to cache the pano to disk.
        /// </summary>
        public event Action<byte[]> OnPanoBytesLoaded;

        void Awake()
        {
            if (string.IsNullOrWhiteSpace(localBasePath))
                localBasePath = Path.Combine(Application.persistentDataPath, "WorldContent");
            if (worldManager == null)
                worldManager = viewModeController != null ? viewModeController.worldManager : null;
        }

        CancellationTokenSource _requestCancellation;
        WorldTransitionOperation _operation;
        long _requestId;
        bool _ownsOperation;

        void OnDisable() => CancelLoad();
        void OnDestroy() => CancelLoad();

        public void CancelLoad()
        {
            ++_requestId;
            CancellationTokenSource cancellation = _requestCancellation;
            bool pending = cancellation != null;
            _requestCancellation = null;
            cancellation?.Cancel();
            cancellation?.Dispose();
            if (_operation != null && _operation.IsCurrent && (_ownsOperation || (pending && !_operation.IsCompleted)))
            {
                if (worldManager != null) worldManager.CancelTransition(_operation);
                else _operation.Cancel();
            }
            _operation = null;
            viewModeController?.SetPanoPreloadPending(false);
        }

        public void PreloadAsync(string pathOrUrl) => _ = PreloadAndWaitAsync(pathOrUrl);
        public void LoadAsync(string pathOrUrl) => _ = LoadAndWaitAsync(pathOrUrl);

        public Task<bool> PreloadAndWaitAsync(string pathOrUrl, WorldTransitionOperation operation = null) =>
            StartLoadAsync(pathOrUrl, false, operation);

        public Task<bool> LoadAndWaitAsync(string pathOrUrl, WorldTransitionOperation operation = null) =>
            StartLoadAsync(pathOrUrl, true, operation);

        async Task<bool> StartLoadAsync(string pathOrUrl, bool display, WorldTransitionOperation operation)
        {
            try { return await RunLoadAsync(pathOrUrl, display, operation); }
            catch (OperationCanceledException) { return false; }
            catch (Exception ex)
            {
                // Includes dependency/lifecycle failures before a request could acquire a token.
                if (this != null && isActiveAndEnabled)
                    Debug.LogWarning($"[LocalRemotePanoLoader] Could not start panorama load: {ex.Message}");
                return false;
            }
        }

        async Task<bool> RunLoadAsync(string pathOrUrl, bool display, WorldTransitionOperation operation)
        {
            if (!isActiveAndEnabled || string.IsNullOrWhiteSpace(pathOrUrl)) return false;
            long requestId = ++_requestId;
            CancellationTokenSource previous = _requestCancellation;
            _requestCancellation = null;
            previous?.Cancel();
            previous?.Dispose();
            if (requestId != _requestId) return false;
            bool ownsOperation = operation == null && (display || worldManager == null || worldManager.CurrentTransition == null);
            operation ??= display
                ? (worldManager != null ? worldManager.BeginTransition() : WorldTransitionOperation.CreateStandalone())
                : (worldManager != null ? worldManager.CurrentTransition : null) ?? WorldTransitionOperation.CreateStandalone();
            if (worldManager == null && _operation != null && !ReferenceEquals(_operation, operation)) _operation.Cancel();
            _operation = operation;
            _ownsOperation = ownsOperation;
            using var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(operation.Token);
            _requestCancellation = requestCancellation;
            CancellationToken token = requestCancellation.Token;
            Texture2D texture = null;
            bool stored = false;
            try
            {
                void CheckCurrent()
                {
                    token.ThrowIfCancellationRequested();
                    operation.ThrowIfStale();
                    if (this == null || !isActiveAndEnabled || requestId != _requestId)
                        throw new OperationCanceledException(token);
                }
                CheckCurrent();
                if (display)
                {
                    onLoadStarted?.Invoke(pathOrUrl);
                    CheckCurrent();
                    worldManager?.NotifyWorldLoadStarted(pathOrUrl, operation);
                    if (worldManager != null) await worldManager.PrepareForWorldLoadAsync(operation);
                }
                else viewModeController?.SetPanoPreloadPending(true);
                CheckCurrent();
                byte[] imageBytes = await AwaitBytesAsync(ReadImageBytesAsync(pathOrUrl, token), token);
                CheckCurrent();
                texture = new Texture2D(2, 2, TextureFormat.RGB24, false);
                if (!texture.LoadImage(imageBytes)) throw new InvalidOperationException("Failed to decode panorama image.");
                if (thumbnailSkybox == null) throw new InvalidOperationException("ThumbnailSkyboxController not assigned.");
                CheckCurrent();
                Texture2D ownedTexture = texture;
                ThumbnailSkyboxController owner = thumbnailSkybox;
                operation.Token.Register(() =>
                {
                    if (owner != null) owner.ClearStored(ownedTexture);
                });
                // Store sets the texture before invoking OnReady; the cancellation callback
                // therefore also clears a reentrant transition started by an OnReady listener.
                thumbnailSkybox.Store(texture);
                stored = true;
                CheckCurrent();
                if (display)
                {
                    if (viewModeController != null) viewModeController.RequestPanoView();
                    else thumbnailSkybox.ShowStored();
                }
                CheckCurrent();
                if (IsUrl(pathOrUrl)) OnPanoBytesLoaded?.Invoke(imageBytes);
                CheckCurrent();
                if (ownsOperation) worldManager?.CompleteTransition(operation);
                ArchStatusBus.Success(display ? "Panorama loaded." : "Panorama preloaded.", "PANO");
                return true;
            }
            catch (OperationCanceledException) { return false; }
            catch (Exception ex)
            {
                if (operation.IsCurrent && requestId == _requestId)
                {
                    Debug.LogWarning($"[LocalRemotePanoLoader] {ex.Message}");
                    onLoadFailed?.Invoke(ex.Message);
                    if (display) worldManager?.NotifyWorldLoadFailed(pathOrUrl, ex.Message, operation);
                    if (ownsOperation && display)
                    {
                        if (worldManager != null) worldManager.CancelTransition(operation);
                        else operation.Cancel();
                    }
                }
                return false;
            }
            finally
            {
                if (!stored && texture != null) WorldTransitionOperation.DestroyOwned(texture);
                if (requestId == _requestId)
                {
                    _requestCancellation = null;
                    viewModeController?.SetPanoPreloadPending(false);
                }
            }
        }

        static async Task<byte[]> AwaitBytesAsync(Task<byte[]> read, CancellationToken token)
        {
            var canceled = new TaskCompletionSource<bool>();
            using (token.Register(() => canceled.TrySetResult(true)))
            {
                if (await Task.WhenAny(read, canceled.Task) != read)
                {
                    _ = ObserveReadAsync(read);
                    token.ThrowIfCancellationRequested();
                }
            }
            token.ThrowIfCancellationRequested();
            return await read;
        }

        static async Task ObserveReadAsync(Task read)
        {
            try { await read; }
            catch (Exception) { /* A canceled reader cannot publish a late error. */ }
        }

        // Overridable only for deterministic offline fixtures; production uses the same
        // cancellation token for file reads and aborting an in-flight Unity request.
        protected virtual async Task<byte[]> ReadImageBytesAsync(string pathOrUrl, CancellationToken token)
        {
            if (!IsUrl(pathOrUrl))
            {
                string resolved = ResolveLocalPath(pathOrUrl);
                if (!File.Exists(resolved)) throw new FileNotFoundException("Panorama file not found.", resolved);
                return await File.ReadAllBytesAsync(resolved, token);
            }
            using UnityWebRequest request = UnityWebRequest.Get(pathOrUrl);
            UnityWebRequestAsyncOperation sent = request.SendWebRequest();
            while (!sent.isDone)
            {
                if (token.IsCancellationRequested)
                {
                    request.Abort();
                    token.ThrowIfCancellationRequested();
                }
                await Task.Yield();
            }
            token.ThrowIfCancellationRequested();
            if (request.result != UnityWebRequest.Result.Success)
                throw new IOException($"Panorama download failed: {request.error}");
            return request.downloadHandler.data;
        }

        static bool IsUrl(string path) => path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                                          path.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

        string ResolveLocalPath(string pathOrUrl)
        {
            // Same naming rules as the splat loader: a spoken file name, possibly with "dot"/"slash",
            // resolved inside the sideload folder and its subfolders.
            if (SideloadPathResolver.TryResolve(pathOrUrl, out string resolved, out string note))
            {
                Debug.Log($"[LocalRemotePanoLoader] Resolved '{pathOrUrl}' -> '{resolved}' ({note}).", this);
                return resolved;
            }

            if (Path.IsPathRooted(pathOrUrl))
                return pathOrUrl;
            return Path.Combine(localBasePath, pathOrUrl);
        }
    }
}
