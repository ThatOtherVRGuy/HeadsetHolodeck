// Assets/App/Command/SpeechIntent/Runtime/LocalRemoteSplatLoader.cs

using System;
using System.IO;
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
            StartLoad(pathOrUrl, displayName, sourceKind, savedSpawnMetadata, null);
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
            SplatSpawnMetadata savedSpawnMetadata)
        {
            var completion = new TaskCompletionSource<bool>();
            StartLoad(pathOrUrl, displayName, sourceKind, savedSpawnMetadata, completion);
            return completion.Task;
        }

        void StartLoad(
            string pathOrUrl,
            string displayName,
            RuntimeSplatFloorLoader.SplatSourceKind sourceKind,
            SplatSpawnMetadata savedSpawnMetadata,
            TaskCompletionSource<bool> completion)
        {
            if (string.IsNullOrWhiteSpace(pathOrUrl))
            {
                Debug.LogWarning("[LocalRemoteSplatLoader] pathOrUrl is empty.");
                ArchStatusBus.Warning("No splat path or URL provided.", "LOAD");
                onLoadFailed?.Invoke("No path or URL provided.");
                completion?.TrySetResult(false);
                return;
            }
            StartCoroutine(LoadCoroutine(pathOrUrl, displayName, sourceKind, savedSpawnMetadata, completion));
        }

        System.Collections.IEnumerator LoadCoroutine(
            string pathOrUrl,
            string displayName,
            RuntimeSplatFloorLoader.SplatSourceKind sourceKind,
            SplatSpawnMetadata savedSpawnMetadata,
            TaskCompletionSource<bool> completion)
        {
            string resolved = ResolveLocalPath(pathOrUrl);
            string worldId  = "local_" + Path.GetFileNameWithoutExtension(resolved)
                                       + "_" + DateTime.UtcNow.Ticks;
            string worldName = !string.IsNullOrWhiteSpace(displayName)
                ? displayName
                : Path.GetFileNameWithoutExtension(resolved);

            onLoadStarted?.Invoke(worldId);
            ArchStatusBus.Info("Loading splat " + worldName + ".", "LOAD");

            bool isUrl = pathOrUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                      || pathOrUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

            // Notify before destroying the outgoing world. The static holodeck subscribes to
            // this event, so it remains visible during cached-file I/O, processing, and cleanup.
            Debug.Log($"[LocalRemoteSplatLoader] Load '{worldName}': notifying world load start before cleanup.");
            worldManager?.NotifyWorldLoadStarted(worldId);
            if (worldManager != null)
            {
                yield return worldManager.PrepareForWorldLoadCoroutine();
                Debug.Log($"[LocalRemoteSplatLoader] Load '{worldName}': old-world cleanup complete.");
            }

            byte[] fileBytes = null;
            string error = null;

            // ── Acquire bytes ────────────────────────────────────────────────
            if (isUrl)
            {
                UnityWebRequest req = UnityWebRequest.Get(pathOrUrl);
                try
                {
                    yield return req.SendWebRequest();

                    if (req.result != UnityWebRequest.Result.Success)
                        error = $"Download failed: {req.error}";
                    else
                        fileBytes = req.downloadHandler.data;
                }
                finally
                {
                    req.Dispose();
                }
            }
            else
            {
                if (!File.Exists(resolved))
                {
                    error = $"File not found: {resolved}";
                }
                else
                {
                    Task<byte[]> readTask = Task.Run(() => File.ReadAllBytes(resolved));
                    while (!readTask.IsCompleted) yield return null;
                    if (readTask.IsFaulted)
                        error = $"Read failed: {readTask.Exception?.GetBaseException().Message}";
                    else
                        fileBytes = readTask.Result;
                }
            }

            if (error != null)
            {
                ReportLoadFailure(worldId, error, "Could not load that splat file.");
                completion?.TrySetResult(false);
                yield break;
            }

            // ── Detect format ────────────────────────────────────────────────
            string ext = Path.GetExtension(isUrl ? pathOrUrl : resolved)
                             .ToLowerInvariant();

            if (ext != ".spz" && ext != ".ply")
            {
                string msg = $"Unsupported format '{ext}'. Expected .spz or .ply.";
                ReportLoadFailure(worldId, msg, "That file format is not supported.");
                completion?.TrySetResult(false);
                yield break;
            }

            if (floorLoader == null)
            {
                ReportLoadFailure(worldId, "RuntimeSplatFloorLoader not assigned.", "Splat loader is not configured.");
                completion?.TrySetResult(false);
                yield break;
            }

            // ── Load (format-specific) ───────────────────────────────────────
            Task<RuntimeSplatFloorLoader.LoadResult> loadTask;

            if (ext == ".spz")
            {
                loadTask = floorLoader.LoadPlacedRuntimeWorldAsync(
                    fileBytes,
                    worldId,
                    worldName,
                    gameObjectName: worldName,
                    sourceKind: sourceKind,
                    savedSpawnMetadata: savedSpawnMetadata);
            }
            else // .ply
            {
                // LoadPlacedRuntimeWorldFromSplatsAsync takes ownership of splats and disposes them.
                Task<NativeArray<InputSplatData>> parseTask = Task.Run(() =>
                {
                    RuntimePlyReader.ReadFromBytes(fileBytes, out NativeArray<InputSplatData> splats);
                    return splats;
                });
                while (!parseTask.IsCompleted) yield return null;
                if (parseTask.IsFaulted)
                {
                    string msg = $"PLY parse failed: {parseTask.Exception?.GetBaseException().Message}";
                    ReportLoadFailure(worldId, msg, "Could not load that PLY file.");
                    completion?.TrySetResult(false);
                    yield break;
                }
                if (!parseTask.Result.IsCreated)
                {
                    string msg = "PLY parser returned no splats.";
                    ReportLoadFailure(worldId, msg, "Could not load that PLY file.");
                    completion?.TrySetResult(false);
                    yield break;
                }
                loadTask = floorLoader.LoadPlacedRuntimeWorldFromSplatsAsync(
                    parseTask.Result,
                    worldId,
                    worldName,
                    gameObjectName: worldName,
                    sourceKind: sourceKind,
                    savedSpawnMetadata: savedSpawnMetadata);
            }

            while (!loadTask.IsCompleted) yield return null;

            if (loadTask.IsFaulted)
            {
                string msg = $"Load failed: {loadTask.Exception?.GetBaseException().Message}";
                ReportLoadFailure(worldId, msg, "Could not finish loading that splat.");
                completion?.TrySetResult(false);
                yield break;
            }

            RuntimeSplatFloorLoader.LoadResult result = loadTask.Result;
            Camera activeCamera = Camera.main;
            Debug.Log("[LocalRemoteSplatLoader] " + BuildRendererDiagnostics("before-registration", result.renderer, activeCamera));
            // Let URP run at least one frame with the new renderer enabled before the world
            // manager hides the static holodeck in response to RegisterExternalWorld.
            yield return null;
            Debug.Log("[LocalRemoteSplatLoader] " + BuildRendererDiagnostics("after-first-render-frame", result.renderer, activeCamera));
            // No World object available for local/remote file loads; LastLoadedWorld is not updated here.
            Debug.Log($"[LocalRemoteSplatLoader] Load '{worldName}': renderer ready, registering external world.");
            worldManager?.RegisterExternalWorld(worldId, result.renderer);

            Debug.Log($"[LocalRemoteSplatLoader] Loaded '{worldName}' ({ext}) as worldId='{worldId}'.");
            ArchStatusBus.Success("Loaded splat " + worldName + ".", "READY");
            completion?.TrySetResult(true);
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
            string safeDetail = string.IsNullOrWhiteSpace(detailedMessage)
                ? "Splat load failed."
                : detailedMessage.Trim();

            Debug.LogError($"[LocalRemoteSplatLoader] {safeDetail}");
            ArchStatusBus.Error(safeDetail, "LOAD");
            worldManager?.NotifyWorldLoadFailed(worldId, safeDetail);
            onLoadFailed?.Invoke(safeDetail);

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
            if (pathOrUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
             || pathOrUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return pathOrUrl;  // URL — no resolution
            if (Path.IsPathRooted(pathOrUrl))
                return pathOrUrl;
            return Path.Combine(localBasePath, pathOrUrl);
        }
    }
}
