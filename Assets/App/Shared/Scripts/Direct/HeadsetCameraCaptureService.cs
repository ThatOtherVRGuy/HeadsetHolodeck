using System;
using System.Collections;
using SpeechIntent;
using UnityEngine;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

namespace Holodeck.Direct
{
    public sealed class HeadsetCameraCaptureService : MonoBehaviour
    {
        const string HeadsetCameraPermission = "horizonos.permission.HEADSET_CAMERA";

        [Header("Camera")]
        [SerializeField] string preferredDeviceName = "";
        [SerializeField] int requestedWidth = 1280;
        [SerializeField] int requestedHeight = 960;
        [SerializeField] int requestedFps = 30;
        [SerializeField] float cameraStartTimeoutSeconds = 3f;

        [Header("Runtime")]
        [SerializeField] Texture2D lastCapturedTexture;
        [SerializeField] string lastCaptureSource = "";

        WebCamTexture _webcam;
        Coroutine _captureCoroutine;
        Coroutine _previewCoroutine;

        public Texture2D LastCapturedTexture => lastCapturedTexture;
        public string LastCaptureSource => lastCaptureSource;
        public bool HasCapture => lastCapturedTexture != null;
        public Texture CurrentPreviewTexture => _webcam;
        public bool IsPreviewing => _webcam != null && _webcam.isPlaying;
        public event Action<Texture2D> CaptureChanged;
        public event Action<string> CaptureFailed;
        public event Action<Texture, bool> PreviewChanged;

        public void Capture()
        {
            Capture(null);
        }

        public void Capture(Action<Texture2D, string> onComplete)
        {
            if (_captureCoroutine != null)
                StopCoroutine(_captureCoroutine);
            _captureCoroutine = StartCoroutine(CaptureCoroutine(onComplete));
        }

        public void BeginPreview()
        {
            if (_previewCoroutine != null)
                StopCoroutine(_previewCoroutine);
            _previewCoroutine = StartCoroutine(BeginPreviewCoroutine());
        }

        public void ConfirmPreviewCapture()
        {
            if (!IsPreviewing)
            {
                ReportFailure("Camera preview is not active. Say capture image first.", null);
                return;
            }

            CaptureCurrentFrame();
            StopPreview();
        }

        public void CancelPreview()
        {
            StopPreview();
            ArchStatusBus.Info("Camera preview cancelled.", "CAPTURE");
        }

        public void ClearCapture()
        {
            if (lastCapturedTexture != null)
                Destroy(lastCapturedTexture);
            lastCapturedTexture = null;
            lastCaptureSource = "";
            CaptureChanged?.Invoke(null);
        }

        public void SetExternalCapture(Texture2D texture, string source)
        {
            if (texture == null)
            {
                ClearCapture();
                return;
            }

            Texture2D ownedTexture = CloneTexture(texture);
            ownedTexture.name = texture.name;

            if (lastCapturedTexture != null)
                Destroy(lastCapturedTexture);

            lastCapturedTexture = ownedTexture;
            lastCaptureSource = string.IsNullOrWhiteSpace(source) ? "External image" : source;
            CaptureChanged?.Invoke(lastCapturedTexture);
            ArchStatusBus.Success($"Selected image prompt: {lastCaptureSource}", "IMAGE");
        }

        static Texture2D CloneTexture(Texture2D source)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture rt = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(source, rt);
            RenderTexture.active = rt;
            Texture2D clone = new Texture2D(source.width, source.height, TextureFormat.RGB24, false);
            clone.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
            clone.Apply(false, false);
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
            return clone;
        }

        IEnumerator BeginPreviewCoroutine()
        {
            yield return EnsurePermissionCoroutine();

            string error = EnsurePermission();
            if (!string.IsNullOrEmpty(error))
            {
                ReportFailure(error, null);
                yield break;
            }

            error = EnsureWebCam();
            if (!string.IsNullOrEmpty(error))
            {
                ReportFailure(error, null);
                yield break;
            }

            if (!_webcam.isPlaying)
                _webcam.Play();

            PreviewChanged?.Invoke(_webcam, true);
            ArchStatusBus.Info("Camera preview starting.", "CAPTURE");

            float deadline = Time.realtimeSinceStartup + cameraStartTimeoutSeconds;
            while (Time.realtimeSinceStartup < deadline &&
                   (_webcam.width <= 16 || _webcam.height <= 16 || !_webcam.didUpdateThisFrame))
            {
                yield return null;
            }

            if (_webcam.width <= 16 || _webcam.height <= 16)
            {
                ReportFailure("Headset camera did not produce a preview image.", null);
                yield break;
            }

            PreviewChanged?.Invoke(_webcam, true);
            ArchStatusBus.Info("Camera preview active. Say OK or shoot to capture.", "CAPTURE");
            _previewCoroutine = null;
        }

        IEnumerator CaptureCoroutine(Action<Texture2D, string> onComplete)
        {
            yield return EnsurePermissionCoroutine();

            string error = EnsurePermission();
            if (!string.IsNullOrEmpty(error))
            {
                ReportFailure(error, onComplete);
                yield break;
            }

            error = EnsureWebCam();
            if (!string.IsNullOrEmpty(error))
            {
                ReportFailure(error, onComplete);
                yield break;
            }

            if (!_webcam.isPlaying)
                _webcam.Play();

            float deadline = Time.realtimeSinceStartup + cameraStartTimeoutSeconds;
            while (Time.realtimeSinceStartup < deadline &&
                   (_webcam.width <= 16 || _webcam.height <= 16 || !_webcam.didUpdateThisFrame))
            {
                yield return null;
            }

            if (_webcam.width <= 16 || _webcam.height <= 16)
            {
                ReportFailure("Headset camera did not produce an image.", onComplete);
                yield break;
            }

            Texture2D texture = CaptureCurrentFrame();
            onComplete?.Invoke(texture, null);
            _captureCoroutine = null;
        }

        /// <summary>
        /// Clockwise angle needed to make the camera image upright, straight from the platform.
        ///
        /// The PICO camera sensor delivers a landscape frame while the headset is used upright, so the
        /// preview and the captured image both arrive rotated 90 degrees. The captured image matters more
        /// than the preview: it becomes the world prompt, and a sideways prompt produces a sideways world.
        /// </summary>
        public int PreviewRotationAngle
        {
            get
            {
                if (_webcam == null) return 0;
                try { return _webcam.videoRotationAngle; }
                catch (Exception) { return 0; }
            }
        }

        public bool PreviewIsVerticallyMirrored => _webcam != null && _webcam.videoVerticallyMirrored;

        static Texture2D RotateTexture(Texture2D source, int degrees)
        {
            degrees = ((degrees % 360) + 360) % 360;
            if (degrees == 0) return source;

            int width = source.width;
            int height = source.height;
            bool quarterTurn = degrees == 90 || degrees == 270;
            int newWidth = quarterTurn ? height : width;
            int newHeight = quarterTurn ? width : height;

            Color32[] sourcePixels = source.GetPixels32();
            Color32[] targetPixels = new Color32[sourcePixels.Length];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int nx, ny;
                    switch (degrees)
                    {
                        case 90: nx = height - 1 - y; ny = x; break;
                        case 180: nx = width - 1 - x; ny = height - 1 - y; break;
                        case 270: nx = y; ny = width - 1 - x; break;
                        default: nx = x; ny = y; break;
                    }
                    targetPixels[ny * newWidth + nx] = sourcePixels[y * width + x];
                }
            }

            Texture2D result = new Texture2D(newWidth, newHeight, TextureFormat.RGB24, false);
            result.SetPixels32(targetPixels);
            result.Apply(false, false);
            return result;
        }

        Texture2D CaptureCurrentFrame()
        {
            Texture2D texture = new Texture2D(_webcam.width, _webcam.height, TextureFormat.RGB24, false);
            texture.SetPixels32(_webcam.GetPixels32());
            texture.Apply(false, false);

            int angle = PreviewRotationAngle;
            if (angle != 0)
            {
                Texture2D rotated = RotateTexture(texture, angle);
                Destroy(texture);
                texture = rotated;
                ArchStatusBus.Info($"Rotated camera capture by {angle} degrees to upright.", "CAPTURE");
            }

            texture.name = $"HeadsetCameraCapture_{DateTime.UtcNow:yyyyMMdd_HHmmss}";

            if (lastCapturedTexture != null)
                Destroy(lastCapturedTexture);
            lastCapturedTexture = texture;
            lastCaptureSource = string.IsNullOrEmpty(_webcam.deviceName) ? "Headset camera" : _webcam.deviceName;

            CaptureChanged?.Invoke(lastCapturedTexture);
            ArchStatusBus.Success("Captured headset camera image.", "CAPTURE");
            return lastCapturedTexture;
        }

        string EnsurePermission()
        {
#if (UNITY_EDITOR || UNITY_STANDALONE_OSX) && !UNITY_ANDROID
            if (!Application.HasUserAuthorization(UserAuthorization.WebCam))
                return "Camera permission requested. Please allow access, then capture again.";
#endif

#if UNITY_ANDROID && !UNITY_EDITOR
            if (Permission.HasUserAuthorizedPermission(HeadsetCameraPermission) ||
                Permission.HasUserAuthorizedPermission(Permission.Camera))
            {
                return null;
            }

            Permission.RequestUserPermission(HeadsetCameraPermission);
            if (!Permission.HasUserAuthorizedPermission(HeadsetCameraPermission))
                Permission.RequestUserPermission(Permission.Camera);

            return "Camera permission requested. Please allow access, then capture again.";
#else
            return null;
#endif
        }

        IEnumerator EnsurePermissionCoroutine()
        {
#if (UNITY_EDITOR || UNITY_STANDALONE_OSX) && !UNITY_ANDROID
            if (!Application.HasUserAuthorization(UserAuthorization.WebCam))
                yield return Application.RequestUserAuthorization(UserAuthorization.WebCam);
#else
            yield break;
#endif
        }

        string EnsureWebCam()
        {
            if (_webcam != null)
                return null;

            WebCamDevice[] devices = WebCamTexture.devices;
            if (devices == null || devices.Length == 0)
                return "No headset camera devices are available.";

            string deviceName = SelectDevice(devices);
            if (string.IsNullOrEmpty(deviceName))
                return "No usable headset camera device was found.";

            _webcam = new WebCamTexture(deviceName, requestedWidth, requestedHeight, requestedFps);
            return null;
        }

        string SelectDevice(WebCamDevice[] devices)
        {
            if (!string.IsNullOrWhiteSpace(preferredDeviceName))
            {
                foreach (WebCamDevice device in devices)
                    if (string.Equals(device.name, preferredDeviceName, StringComparison.OrdinalIgnoreCase))
                        return device.name;
            }

            foreach (WebCamDevice device in devices)
            {
                string name = device.name ?? "";
                if (name.IndexOf("passthrough", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("headset", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("quest", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return device.name;
                }
            }

            foreach (WebCamDevice device in devices)
                if (!device.isFrontFacing)
                    return device.name;

            return devices[0].name;
        }

        void ReportFailure(string message, Action<Texture2D, string> onComplete)
        {
            Debug.LogWarning($"[HeadsetCameraCaptureService] {message}", this);
            ArchStatusBus.Warning(message, "CAPTURE");
            CaptureFailed?.Invoke(message);
            onComplete?.Invoke(null, message);
            PreviewChanged?.Invoke(null, false);
            _captureCoroutine = null;
            _previewCoroutine = null;
        }

        void StopPreview()
        {
            if (_previewCoroutine != null)
            {
                StopCoroutine(_previewCoroutine);
                _previewCoroutine = null;
            }

            if (_webcam != null && _webcam.isPlaying)
                _webcam.Stop();
            PreviewChanged?.Invoke(null, false);
        }

        void OnDestroy()
        {
            StopPreview();
            ClearCapture();
        }
    }
}
