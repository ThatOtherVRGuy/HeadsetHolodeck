// Assets/App/Command/SpeechIntent/Runtime/LocalRemotePanoLoader.cs

using System;
using System.IO;
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

        /// <summary>
        /// Load a panoramic image from a local path or URL, store it in the skybox without
        /// displaying it. The texture becomes available for <see cref="ViewModeController.RequestPanoView"/>.
        /// </summary>
        public void PreloadAsync(string pathOrUrl)
        {
            if (string.IsNullOrWhiteSpace(pathOrUrl))
            {
                Debug.LogWarning("[LocalRemotePanoLoader] PreloadAsync: pathOrUrl is empty.");
                ArchStatusBus.Warning("No panorama path or URL provided.", "PANO");
                return;
            }
            StartCoroutine(PreloadCoroutine(pathOrUrl));
        }

        System.Collections.IEnumerator PreloadCoroutine(string pathOrUrl)
        {
            viewModeController?.SetPanoPreloadPending(true);
            ArchStatusBus.Info("Preloading panorama.", "PANO");

            bool isUrl = pathOrUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                      || pathOrUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

            byte[] imageBytes = null;
            string error = null;

            if (isUrl)
            {
                UnityWebRequest req = UnityWebRequest.Get(pathOrUrl);
                try
                {
                    yield return req.SendWebRequest();
                    if (req.result != UnityWebRequest.Result.Success)
                        error = $"Download failed: {req.error}";
                    else
                        imageBytes = req.downloadHandler.data;
                }
                finally { req.Dispose(); }
            }
            else
            {
                string resolved = ResolveLocalPath(pathOrUrl);
                if (!File.Exists(resolved))
                {
                    error = $"File not found: {resolved}";
                }
                else
                {
                    System.Threading.Tasks.Task<byte[]> readTask = System.Threading.Tasks.Task.Run(() => File.ReadAllBytes(resolved));
                    while (!readTask.IsCompleted) yield return null;
                    if (readTask.IsFaulted)
                        error = $"Read failed: {readTask.Exception?.GetBaseException().Message}";
                    else
                        imageBytes = readTask.Result;
                }
            }

            if (error != null)
            {
                Debug.LogWarning($"[LocalRemotePanoLoader] PreloadAsync failed: {error}");
                ArchStatusBus.Warning(error, "PANO");
                viewModeController?.SetPanoPreloadPending(false);
                yield break;
            }

            Texture2D tex = new Texture2D(2, 2, TextureFormat.RGB24, false);
            if (!tex.LoadImage(imageBytes))
            {
                Destroy(tex);
                Debug.LogWarning($"[LocalRemotePanoLoader] PreloadAsync: failed to decode image from '{pathOrUrl}'.");
                ArchStatusBus.Warning("Failed to decode panorama image.", "PANO");
                yield break;
            }

            if (thumbnailSkybox == null)
            {
                Destroy(tex);
                Debug.LogError("[LocalRemotePanoLoader] PreloadAsync: thumbnailSkybox is not assigned.");
                ArchStatusBus.Error("ThumbnailSkyboxController not assigned.", "PANO");
                yield break;
            }

            viewModeController?.SetPanoPreloadPending(false);
            thumbnailSkybox.Store(tex);
            if (isUrl) OnPanoBytesLoaded?.Invoke(imageBytes);
            Debug.Log($"[LocalRemotePanoLoader] Panorama preloaded (not yet shown) from '{pathOrUrl}'.");
            ArchStatusBus.Success("Panorama preloaded.", "PANO");
        }

        /// <summary>
        /// Load a panoramic image from a local path or URL and display it.
        /// </summary>
        public void LoadAsync(string pathOrUrl)
        {
            if (string.IsNullOrWhiteSpace(pathOrUrl))
            {
                Debug.LogWarning("[LocalRemotePanoLoader] pathOrUrl is empty.");
                ArchStatusBus.Warning("No panorama path or URL provided.", "PANO");
                onLoadFailed?.Invoke("No path or URL provided.");
                return;
            }
            StartCoroutine(LoadCoroutine(pathOrUrl));
        }

        System.Collections.IEnumerator LoadCoroutine(string pathOrUrl)
        {
            onLoadStarted?.Invoke(pathOrUrl);
            ArchStatusBus.Info("Loading panorama.", "PANO");

            if (worldManager != null)
                yield return worldManager.PrepareForWorldLoadCoroutine();

            bool isUrl = pathOrUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                      || pathOrUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

            byte[] imageBytes = null;
            string error = null;

            if (isUrl)
            {
                UnityWebRequest req = UnityWebRequest.Get(pathOrUrl);
                try
                {
                    yield return req.SendWebRequest();

                    if (req.result != UnityWebRequest.Result.Success)
                        error = $"Download failed: {req.error}";
                    else
                        imageBytes = req.downloadHandler.data;
                }
                finally
                {
                    req.Dispose();
                }
            }
            else
            {
                string resolved = ResolveLocalPath(pathOrUrl);
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
                        imageBytes = readTask.Result;
                }
            }

            if (error != null)
            {
                Debug.LogError($"[LocalRemotePanoLoader] {error}");
                ArchStatusBus.Error(error, "PANO");
                onLoadFailed?.Invoke(error);
                yield break;
            }

            Texture2D tex = new Texture2D(2, 2, TextureFormat.RGB24, false);
            if (!tex.LoadImage(imageBytes))
            {
                Destroy(tex);
                string msg = $"Failed to decode image from '{pathOrUrl}'.";
                Debug.LogError($"[LocalRemotePanoLoader] {msg}");
                ArchStatusBus.Error(msg, "PANO");
                onLoadFailed?.Invoke(msg);
                yield break;
            }

            if (thumbnailSkybox == null)
            {
                Destroy(tex);
                Debug.LogError("[LocalRemotePanoLoader] thumbnailSkybox is not assigned.");
                ArchStatusBus.Error("ThumbnailSkyboxController not assigned.", "PANO");
                onLoadFailed?.Invoke("ThumbnailSkyboxController not assigned.");
                yield break;
            }

            // Store the texture and let RequestPanoView() display it. Using Store() instead of Show()
            // here ensures DesiredMode is set to Pano before TryApply() fires — if we called Show()
            // directly it would fire OnReady with the old DesiredMode, which could trigger StartFadeOut()
            // (e.g. if the previous mode was Splat3D) before RequestPanoView() sets the correct mode.
            if (isUrl) OnPanoBytesLoaded?.Invoke(imageBytes);
            thumbnailSkybox.Store(tex);
            if (viewModeController != null)
                viewModeController.RequestPanoView();
            else
                thumbnailSkybox.ShowStored();

            Debug.Log($"[LocalRemotePanoLoader] Panorama loaded from '{pathOrUrl}'.");
            ArchStatusBus.Success("Panorama loaded.", "READY");
        }

        string ResolveLocalPath(string pathOrUrl)
        {
            if (Path.IsPathRooted(pathOrUrl))
                return pathOrUrl;
            return Path.Combine(localBasePath, pathOrUrl);
        }
    }
}
