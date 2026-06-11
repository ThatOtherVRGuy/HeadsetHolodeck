using System;
using System.Collections;
using UnityEngine;

namespace Holodeck.Direct
{
    public sealed class ObjectThumbnailCaptureService : MonoBehaviour
    {
        [Header("Capture")]
        [Min(64)] public int thumbnailSize = 256;
        [Range(1, 31)] public int captureLayer = 31;
        public Color backgroundColor = new Color(0f, 0f, 0f, 0f);
        [Range(15f, 80f)] public float cameraFieldOfView = 32f;
        [Min(1f)] public float framingPadding = 1.25f;
        public Vector3 viewDirection = new Vector3(0.7f, 0.35f, -1f);

        [Header("Lighting")]
        public Color keyLightColor = Color.white;
        public float keyLightIntensity = 2.4f;
        public Color fillLightColor = new Color(0.75f, 0.82f, 1f, 1f);
        public float fillLightIntensity = 0.7f;
        public Color ambientColor = new Color(0.35f, 0.35f, 0.38f, 1f);

        Camera _camera;
        Light _keyLight;
        Light _fillLight;
        Transform _rig;

        public IEnumerator CapturePrimaryThumbnail(
            CachedObjectStore store,
            CachedObjectRecord record,
            GameObject source,
            Action<bool, string> onComplete)
        {
            if (store == null || record == null || source == null)
            {
                onComplete?.Invoke(false, "Thumbnail capture missing store, record, or object.");
                yield break;
            }

            EnsureRig();

            GameObject clone = null;
            RenderTexture renderTexture = null;
            Texture2D texture = null;
            Color previousAmbient = RenderSettings.ambientLight;
            try
            {
                clone = Instantiate(source, _rig);
                clone.name = "ThumbnailCapture_" + source.name;
                clone.transform.localPosition = Vector3.zero;
                clone.transform.localRotation = Quaternion.identity;
                clone.transform.localScale = source.transform.lossyScale;
                SetLayerRecursively(clone, captureLayer);
                DisableRuntimeBehaviours(clone);

                if (!TryGetRendererBounds(clone, out Bounds bounds))
                {
                    onComplete?.Invoke(false, "Thumbnail capture found no renderers.");
                    yield break;
                }

                ConfigureCamera(bounds);
                ConfigureLights(bounds);

                RenderSettings.ambientLight = ambientColor;
                int size = Mathf.Max(64, thumbnailSize);
                renderTexture = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32)
                {
                    antiAliasing = 4
                };
                _camera.targetTexture = renderTexture;
                _camera.Render();

                RenderTexture previousActive = RenderTexture.active;
                RenderTexture.active = renderTexture;
                texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
                texture.ReadPixels(new Rect(0, 0, size, size), 0, 0);
                texture.Apply(false, false);
                RenderTexture.active = previousActive;

                byte[] pngBytes = texture.EncodeToPNG();
                string relativePath = store.SaveThumbnailFrame(record, pngBytes, 0, ".png");
                onComplete?.Invoke(true, relativePath);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ObjectThumbnailCaptureService] Thumbnail capture failed: {ex.Message}", this);
                onComplete?.Invoke(false, ex.Message);
            }
            finally
            {
                RenderSettings.ambientLight = previousAmbient;
                if (_camera != null)
                    _camera.targetTexture = null;
                if (renderTexture != null)
                    renderTexture.Release();
                if (texture != null)
                    Destroy(texture);
                if (clone != null)
                    Destroy(clone);
            }
        }

        void EnsureRig()
        {
            if (_rig != null && _camera != null && _keyLight != null && _fillLight != null)
                return;

            GameObject rigGo = new GameObject("ObjectThumbnailCaptureRig");
            rigGo.hideFlags = HideFlags.HideAndDontSave;
            rigGo.transform.SetParent(transform, false);
            _rig = rigGo.transform;

            GameObject cameraGo = new GameObject("ThumbnailCamera");
            cameraGo.hideFlags = HideFlags.HideAndDontSave;
            cameraGo.transform.SetParent(_rig, false);
            _camera = cameraGo.AddComponent<Camera>();
            _camera.enabled = false;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = backgroundColor;
            _camera.cullingMask = 1 << captureLayer;
            _camera.nearClipPlane = 0.01f;
            _camera.farClipPlane = 100f;
            _camera.fieldOfView = cameraFieldOfView;

            _keyLight = CreateLight("ThumbnailKeyLight", keyLightColor, keyLightIntensity);
            _fillLight = CreateLight("ThumbnailFillLight", fillLightColor, fillLightIntensity);
        }

        Light CreateLight(string name, Color color, float intensity)
        {
            GameObject go = new GameObject(name);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.transform.SetParent(_rig, false);
            Light light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = color;
            light.intensity = intensity;
            return light;
        }

        void ConfigureCamera(Bounds bounds)
        {
            Vector3 direction = viewDirection.sqrMagnitude > 0.001f
                ? viewDirection.normalized
                : new Vector3(0.7f, 0.35f, -1f).normalized;
            float radius = Mathf.Max(bounds.extents.magnitude, 0.05f);
            float fovRad = Mathf.Deg2Rad * Mathf.Max(1f, cameraFieldOfView);
            float distance = radius * framingPadding / Mathf.Tan(fovRad * 0.5f);

            _camera.transform.position = bounds.center - direction * distance;
            _camera.transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
            _camera.nearClipPlane = Mathf.Max(0.01f, distance - radius * 3f);
            _camera.farClipPlane = Mathf.Max(_camera.nearClipPlane + 1f, distance + radius * 3f);
            _camera.fieldOfView = cameraFieldOfView;
            _camera.backgroundColor = backgroundColor;
            _camera.cullingMask = 1 << captureLayer;
        }

        void ConfigureLights(Bounds bounds)
        {
            _keyLight.color = keyLightColor;
            _keyLight.intensity = keyLightIntensity;
            _keyLight.transform.rotation = Quaternion.LookRotation((bounds.center - new Vector3(-1.5f, 2f, -2f)).normalized, Vector3.up);

            _fillLight.color = fillLightColor;
            _fillLight.intensity = fillLightIntensity;
            _fillLight.transform.rotation = Quaternion.LookRotation((bounds.center - new Vector3(2f, 1f, 1.5f)).normalized, Vector3.up);
        }

        static bool TryGetRendererBounds(GameObject root, out Bounds bounds)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            bounds = default;
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

            return hasBounds;
        }

        static void SetLayerRecursively(GameObject root, int layer)
        {
            if (root == null)
                return;

            root.layer = layer;
            foreach (Transform child in root.transform)
                SetLayerRecursively(child.gameObject, layer);
        }

        static void DisableRuntimeBehaviours(GameObject root)
        {
            MonoBehaviour[] behaviours = root.GetComponentsInChildren<MonoBehaviour>(true);
            foreach (MonoBehaviour behaviour in behaviours)
                behaviour.enabled = false;
        }
    }
}
