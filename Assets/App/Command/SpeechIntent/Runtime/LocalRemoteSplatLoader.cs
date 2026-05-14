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
            if (string.IsNullOrWhiteSpace(pathOrUrl))
            {
                Debug.LogWarning("[LocalRemoteSplatLoader] pathOrUrl is empty.");
                ArchStatusBus.Warning("No splat path or URL provided.", "LOAD");
                onLoadFailed?.Invoke("No path or URL provided.");
                return;
            }
            StartCoroutine(LoadCoroutine(pathOrUrl, displayName));
        }

        System.Collections.IEnumerator LoadCoroutine(string pathOrUrl, string displayName)
        {
            string resolved = ResolveLocalPath(pathOrUrl);
            string worldId  = "local_" + Path.GetFileNameWithoutExtension(resolved)
                                       + "_" + DateTime.UtcNow.Ticks;
            string worldName = !string.IsNullOrWhiteSpace(displayName)
                ? displayName
                : Path.GetFileNameWithoutExtension(resolved);

            onLoadStarted?.Invoke(worldId);
            ArchStatusBus.Info("Loading splat " + worldName + ".", "LOAD");

            if (worldManager != null)
                yield return worldManager.PrepareForWorldLoadCoroutine();

            bool isUrl = pathOrUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                      || pathOrUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

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
                Debug.LogError($"[LocalRemoteSplatLoader] {error}");
                ArchStatusBus.Error(error, "LOAD");
                onLoadFailed?.Invoke(error);
                yield break;
            }

            // ── Detect format ────────────────────────────────────────────────
            string ext = Path.GetExtension(isUrl ? pathOrUrl : resolved)
                             .ToLowerInvariant();

            if (ext != ".spz" && ext != ".ply")
            {
                string msg = $"Unsupported format '{ext}'. Expected .spz or .ply.";
                Debug.LogError($"[LocalRemoteSplatLoader] {msg}");
                ArchStatusBus.Error(msg, "LOAD");
                onLoadFailed?.Invoke(msg);
                yield break;
            }

            if (floorLoader == null)
            {
                ArchStatusBus.Error("RuntimeSplatFloorLoader not assigned.", "LOAD");
                onLoadFailed?.Invoke("RuntimeSplatFloorLoader not assigned.");
                yield break;
            }

            // ── Load (format-specific) ───────────────────────────────────────
            worldManager?.NotifyWorldLoadStarted(worldId);

            Task<RuntimeSplatFloorLoader.LoadResult> loadTask;

            if (ext == ".spz")
            {
                loadTask = floorLoader.LoadPlacedRuntimeWorldAsync(
                    fileBytes, worldId, worldName, gameObjectName: worldName);
            }
            else // .ply
            {
                // LoadPlacedRuntimeWorldFromSplatsAsync takes ownership of splats and disposes them.
                Task<RuntimeSplatFloorLoader.LoadResult> plyTask = null;
                Task parseTask = Task.Run(() =>
                {
                    RuntimePlyReader.ReadFromBytes(fileBytes, out NativeArray<InputSplatData> splats);
                    plyTask = floorLoader.LoadPlacedRuntimeWorldFromSplatsAsync(
                        splats, worldId, worldName, gameObjectName: worldName);
                });
                while (!parseTask.IsCompleted) yield return null;
                if (parseTask.IsFaulted)
                {
                    string msg = $"PLY parse failed: {parseTask.Exception?.GetBaseException().Message}";
                    Debug.LogError($"[LocalRemoteSplatLoader] {msg}");
                    ArchStatusBus.Error(msg, "LOAD");
                    worldManager?.NotifyWorldLoadFailed(worldId, msg);
                    onLoadFailed?.Invoke(msg);
                    yield break;
                }
                if (plyTask == null)
                {
                    string msg = "PLY loader returned null task.";
                    Debug.LogError($"[LocalRemoteSplatLoader] {msg}");
                    ArchStatusBus.Error(msg, "LOAD");
                    worldManager?.NotifyWorldLoadFailed(worldId, msg);
                    onLoadFailed?.Invoke(msg);
                    yield break;
                }
                loadTask = plyTask;
            }

            while (!loadTask.IsCompleted) yield return null;

            if (loadTask.IsFaulted)
            {
                string msg = $"Load failed: {loadTask.Exception?.GetBaseException().Message}";
                Debug.LogError($"[LocalRemoteSplatLoader] {msg}");
                ArchStatusBus.Error(msg, "LOAD");
                worldManager?.NotifyWorldLoadFailed(worldId, msg);
                onLoadFailed?.Invoke(msg);
                yield break;
            }

            RuntimeSplatFloorLoader.LoadResult result = loadTask.Result;
            // No World object available for local/remote file loads; LastLoadedWorld is not updated here.
            worldManager?.RegisterExternalWorld(worldId, result.renderer);

            Debug.Log($"[LocalRemoteSplatLoader] Loaded '{worldName}' ({ext}) as worldId='{worldId}'.");
            ArchStatusBus.Success("Loaded splat " + worldName + ".", "READY");
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
