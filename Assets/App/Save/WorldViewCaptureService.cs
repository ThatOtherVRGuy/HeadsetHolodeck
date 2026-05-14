using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Holodeck.Save
{
    public sealed class WorldViewCaptureService : MonoBehaviour
    {
        [Header("Dependencies")]
        public WorldConfigStore worldConfigStore;
        public WorldConfigAutoSave worldConfigAutoSave;
        public Camera captureCamera;

        [Header("Capture")]
        [Min(64)] public int thumbnailWidth = 1024;
        [Min(64)] public int thumbnailHeight = 576;
        [Min(128)] public int panoramaCubemapSize = 1024;
        [Min(256)] public int panoramaWidth = 2048;
        [Min(128)] public int panoramaHeight = 1024;
        [Min(0f)] public float countdownSeconds = 3f;

        [Header("Temporary Hide During Capture")]
        public bool hideArchObjectsDuringCapture = true;
        public GameObject[] additionalObjectsToHide;

        bool _captureInProgress;

        public bool CaptureThumbnail()
        {
            if (!TryGetCaptureContext(out _, out _))
                return false;

            if (!TryBeginCapture())
                return false;

            StartCoroutine(CaptureAfterCountdown(CaptureKind.Thumbnail));
            return true;
        }

        public bool CapturePanorama()
        {
            if (!TryGetCaptureContext(out _, out _))
                return false;

            if (!TryBeginCapture())
                return false;

            StartCoroutine(CaptureAfterCountdown(CaptureKind.Panorama));
            return true;
        }

        void OnDisable()
        {
            StopAllCoroutines();
            _captureInProgress = false;
        }

        IEnumerator CaptureAfterCountdown(CaptureKind kind)
        {
            int wholeSeconds = Mathf.CeilToInt(countdownSeconds);
            for (int remaining = wholeSeconds; remaining > 0; remaining--)
            {
                string label = kind == CaptureKind.Thumbnail ? "thumbnail" : "panorama";
                SpeechIntent.ArchStatusBus.Info($"Capturing {label} in {remaining}.", "CAPTURE");
                Debug.Log($"[WorldViewCaptureService] Capturing {label} in {remaining}.", this);
                yield return new WaitForSecondsRealtime(1f);
            }

            if (countdownSeconds > 0f && wholeSeconds == 0)
                yield return new WaitForSecondsRealtime(countdownSeconds);

            if (kind == CaptureKind.Thumbnail)
                CaptureThumbnailNow();
            else
                CapturePanoramaNow();
        }

        void CaptureThumbnailNow()
        {
            if (!TryGetCaptureContext(out WorldConfig config, out string configFolder))
            {
                _captureInProgress = false;
                return;
            }

            List<GameObject> hidden = HideCaptureObjects();
            try
            {
                Camera cam = ResolveCaptureCamera();
                if (cam == null)
                {
                    ReportWarning("No camera available for thumbnail capture.");
                    return;
                }

                Texture2D texture = RenderCameraToTexture(cam, thumbnailWidth, thumbnailHeight);
                if (texture == null)
                {
                    ReportWarning("Thumbnail capture failed.");
                    return;
                }

                try
                {
                    string relativePath = SavePng(configFolder, "thumbnail.png", texture);
                    EnsureWorldSource(config).cached_thumbnail = relativePath;
                    worldConfigStore.SaveConfig(config);
                    Debug.Log($"[WorldViewCaptureService] Captured thumbnail for '{config.display_name}' -> {relativePath}", this);
                    SpeechIntent.ArchStatusBus.Success("Captured world thumbnail.", "CAPTURE");
                    return;
                }
                finally
                {
                    Destroy(texture);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[WorldViewCaptureService] Thumbnail capture failed: {ex.Message}", this);
                SpeechIntent.ArchStatusBus.Warning("Thumbnail capture failed.", "CAPTURE");
            }
            finally
            {
                RestoreHiddenObjects(hidden);
                _captureInProgress = false;
            }
        }

        void CapturePanoramaNow()
        {
            if (!TryGetCaptureContext(out WorldConfig config, out string configFolder))
            {
                _captureInProgress = false;
                return;
            }

            List<GameObject> hidden = HideCaptureObjects();
            try
            {
                Camera cam = ResolveCaptureCamera();
                if (cam == null)
                {
                    ReportWarning("No camera available for panorama capture.");
                    return;
                }

                Texture2D texture = RenderCameraToEquirectangular(cam);
                if (texture == null)
                {
                    ReportWarning("Panorama capture failed.");
                    return;
                }

                try
                {
                    string relativePath = SavePng(configFolder, "panorama.png", texture);
                    EnsureWorldSource(config).cached_pano = relativePath;
                    if (string.IsNullOrEmpty(config.world_source.cached_thumbnail))
                        config.world_source.cached_thumbnail = relativePath;
                    worldConfigStore.SaveConfig(config);
                    Debug.Log($"[WorldViewCaptureService] Captured panorama for '{config.display_name}' -> {relativePath}", this);
                    SpeechIntent.ArchStatusBus.Success("Captured world panorama.", "CAPTURE");
                    return;
                }
                finally
                {
                    Destroy(texture);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[WorldViewCaptureService] Panorama capture failed: {ex.Message}", this);
                SpeechIntent.ArchStatusBus.Warning("Panorama capture failed.", "CAPTURE");
            }
            finally
            {
                RestoreHiddenObjects(hidden);
                _captureInProgress = false;
            }
        }

        enum CaptureKind
        {
            Thumbnail,
            Panorama
        }

        void Awake()
        {
            ResolveDependencies();
        }

        bool TryGetCaptureContext(out WorldConfig config, out string configFolder)
        {
            ResolveDependencies();
            config = worldConfigAutoSave != null ? worldConfigAutoSave.ActiveConfig : null;
            configFolder = null;

            if (worldConfigStore == null)
            {
                ReportWarning("World config store is not assigned.");
                return false;
            }

            if (config == null)
            {
                ReportWarning("No world is loaded.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(config.config_id))
            {
                ReportWarning("Loaded world has no config folder.");
                return false;
            }

            configFolder = worldConfigStore.GetConfigFolderPath(config);
            Directory.CreateDirectory(configFolder);
            return true;
        }

        bool TryBeginCapture()
        {
            if (_captureInProgress)
            {
                ReportWarning("World capture is already running.");
                return false;
            }

            _captureInProgress = true;
            return true;
        }

        void ResolveDependencies()
        {
            if (worldConfigStore == null)
                worldConfigStore = FindFirstObjectByType<WorldConfigStore>(FindObjectsInactive.Include);
            if (worldConfigAutoSave == null)
                worldConfigAutoSave = FindFirstObjectByType<WorldConfigAutoSave>(FindObjectsInactive.Include);
            if (captureCamera == null)
                captureCamera = Camera.main;
        }

        Camera ResolveCaptureCamera()
        {
            Camera main = Camera.main;
            if (main != null)
            {
                captureCamera = main;
                return main;
            }

            if (captureCamera != null)
                return captureCamera;

            Camera[] cameras = FindObjectsByType<Camera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            return cameras.Length > 0 ? cameras[0] : null;
        }

        static Texture2D RenderCameraToTexture(Camera source, int width, int height)
        {
            RenderTexture rt = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            RenderTexture previousActive = RenderTexture.active;
            RenderTexture previousTarget = source.targetTexture;
            try
            {
                source.targetTexture = rt;
                source.Render();
                RenderTexture.active = rt;
                Texture2D texture = new Texture2D(width, height, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                texture.Apply(false, false);
                return texture;
            }
            finally
            {
                source.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        Texture2D RenderCameraToEquirectangular(Camera source)
        {
            Camera cam = CreateCaptureCamera(source);
            RenderTexture previousActive = RenderTexture.active;
            Texture2D[] faces = null;
            try
            {
                faces = RenderSixFaces(cam, panoramaCubemapSize);
                return StitchFacesToEquirect(faces, panoramaWidth, panoramaHeight);
            }
            finally
            {
                RenderTexture.active = previousActive;
                if (faces != null)
                    foreach (Texture2D face in faces)
                        if (face != null)
                            Destroy(face);
                if (cam != null)
                    Destroy(cam.gameObject);
            }
        }

        static Camera CreateCaptureCamera(Camera source)
        {
            GameObject go = new GameObject("WorldViewCaptureCamera");
            go.hideFlags = HideFlags.HideAndDontSave;
            Camera cam = go.AddComponent<Camera>();
            cam.CopyFrom(source);
            cam.enabled = false;
            cam.stereoTargetEye = StereoTargetEyeMask.None;
            Transform sourceTransform = source.transform;
            Transform camTransform = cam.transform;
            camTransform.SetPositionAndRotation(sourceTransform.position, sourceTransform.rotation);
            camTransform.localScale = sourceTransform.lossyScale;
            return cam;
        }

        static Texture2D[] RenderSixFaces(Camera cam, int faceSize)
        {
            Texture2D[] faces = new Texture2D[6];
            float oldFieldOfView = cam.fieldOfView;
            float oldAspect = cam.aspect;
            Quaternion oldRotation = cam.transform.rotation;

            try
            {
                cam.fieldOfView = 90f;
                cam.aspect = 1f;

                faces[(int)CubemapFace.PositiveX] = RenderFace(cam, Vector3.right, Vector3.up, faceSize);
                faces[(int)CubemapFace.NegativeX] = RenderFace(cam, Vector3.left, Vector3.up, faceSize);
                faces[(int)CubemapFace.PositiveY] = RenderFace(cam, Vector3.up, Vector3.back, faceSize);
                faces[(int)CubemapFace.NegativeY] = RenderFace(cam, Vector3.down, Vector3.forward, faceSize);
                faces[(int)CubemapFace.PositiveZ] = RenderFace(cam, Vector3.forward, Vector3.up, faceSize);
                faces[(int)CubemapFace.NegativeZ] = RenderFace(cam, Vector3.back, Vector3.up, faceSize);
                return faces;
            }
            finally
            {
                cam.fieldOfView = oldFieldOfView;
                cam.aspect = oldAspect;
                cam.transform.rotation = oldRotation;
            }
        }

        static Texture2D RenderFace(Camera cam, Vector3 forward, Vector3 up, int faceSize)
        {
            cam.transform.rotation = Quaternion.LookRotation(forward, up);
            return RenderCameraToTexture(cam, faceSize, faceSize);
        }

        static Texture2D StitchFacesToEquirect(Texture2D[] faces, int width, int height)
        {
            Texture2D texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            int faceSize = faces != null && faces.Length > 0 && faces[0] != null ? faces[0].width : 0;
            if (faceSize <= 0)
                return texture;

            for (int y = 0; y < height; y++)
            {
                float v = (y + 0.5f) / height;
                float latitude = (0.5f - v) * Mathf.PI;
                float cosLatitude = Mathf.Cos(latitude);
                float sinLatitude = Mathf.Sin(latitude);

                for (int x = 0; x < width; x++)
                {
                    float u = (x + 0.5f) / width;
                    float longitude = (u - 0.5f) * Mathf.PI * 2f;
                    Vector3 direction = new Vector3(
                        Mathf.Sin(longitude) * cosLatitude,
                        sinLatitude,
                        Mathf.Cos(longitude) * cosLatitude);

                    texture.SetPixel(x, y, SampleFacesNearest(faces, faceSize, direction));
                }
            }

            texture.Apply(false, false);
            return texture;
        }

        static Color SampleFacesNearest(Texture2D[] faces, int faceSize, Vector3 direction)
        {
            float ax = Mathf.Abs(direction.x);
            float ay = Mathf.Abs(direction.y);
            float az = Mathf.Abs(direction.z);
            CubemapFace face;
            float sc;
            float tc;
            float ma;

            if (ax >= ay && ax >= az)
            {
                ma = ax;
                if (direction.x > 0f)
                {
                    face = CubemapFace.PositiveX;
                    sc = -direction.z;
                    tc = -direction.y;
                }
                else
                {
                    face = CubemapFace.NegativeX;
                    sc = direction.z;
                    tc = -direction.y;
                }
            }
            else if (ay >= ax && ay >= az)
            {
                ma = ay;
                if (direction.y > 0f)
                {
                    face = CubemapFace.PositiveY;
                    sc = direction.x;
                    tc = direction.z;
                }
                else
                {
                    face = CubemapFace.NegativeY;
                    sc = direction.x;
                    tc = -direction.z;
                }
            }
            else
            {
                ma = az;
                if (direction.z > 0f)
                {
                    face = CubemapFace.PositiveZ;
                    sc = direction.x;
                    tc = -direction.y;
                }
                else
                {
                    face = CubemapFace.NegativeZ;
                    sc = -direction.x;
                    tc = -direction.y;
                }
            }

            float u = Mathf.Clamp01((sc / ma + 1f) * 0.5f);
            float v = Mathf.Clamp01((tc / ma + 1f) * 0.5f);
            int px = Mathf.Clamp(Mathf.RoundToInt(u * (faceSize - 1)), 0, faceSize - 1);
            int py = Mathf.Clamp(Mathf.RoundToInt(v * (faceSize - 1)), 0, faceSize - 1);
            Texture2D faceTexture = faces != null ? faces[(int)face] : null;
            return faceTexture != null ? faceTexture.GetPixel(px, py) : Color.black;
        }

        static string SavePng(string configFolder, string fileName, Texture2D texture)
        {
            string absolutePath = Path.Combine(configFolder, fileName);
            byte[] bytes = texture.EncodeToPNG();
            File.WriteAllBytes(absolutePath, bytes);
            return fileName;
        }

        static WorldSourceData EnsureWorldSource(WorldConfig config)
        {
            if (config.world_source == null)
                config.world_source = new WorldSourceData { type = "local_splat" };
            return config.world_source;
        }

        List<GameObject> HideCaptureObjects()
        {
            var hidden = new List<GameObject>();
            if (hideArchObjectsDuringCapture)
            {
                AddIfActive(hidden, GameObject.Find("ArchLCARS"));
                AddIfActive(hidden, GameObject.Find("holoarch_v3_-_with_panels"));
            }

            if (additionalObjectsToHide != null)
            {
                foreach (GameObject go in additionalObjectsToHide)
                    AddIfActive(hidden, go);
            }

            foreach (GameObject go in hidden)
                go.SetActive(false);
            return hidden;
        }

        static void AddIfActive(List<GameObject> hidden, GameObject go)
        {
            if (go == null || !go.activeSelf || hidden.Contains(go))
                return;
            hidden.Add(go);
        }

        static void RestoreHiddenObjects(List<GameObject> hidden)
        {
            foreach (GameObject go in hidden)
                if (go != null)
                    go.SetActive(true);
        }

        void ReportWarning(string message)
        {
            Debug.LogWarning($"[WorldViewCaptureService] {message}", this);
            SpeechIntent.ArchStatusBus.Warning(message, "CAPTURE");
        }
    }
}
