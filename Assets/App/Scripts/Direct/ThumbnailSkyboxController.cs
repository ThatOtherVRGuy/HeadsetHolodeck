using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;

namespace Holodeck.Direct
{
    public sealed class ThumbnailSkyboxController : MonoBehaviour
    {
        [Header("Thumbnail Skybox")]
        [SerializeField] private float fadeOutDuration = 1.5f;
        [SerializeField] private UnityEvent onThumbnailShown;

        [Header("Panorama Sphere")]
        [SerializeField] private Transform sphereOrigin;
        [SerializeField] private float expandDuration = 1.5f;
        [SerializeField] private float expandStartScale = 0.5f;
        [SerializeField] private float expandTargetScale = 500f;

        // Skybox fallback state
        private Material _skyboxMaterial;
        private Material _previousSkybox;
        private Coroutine _fadeCoroutine;
        private bool _isShowing;

        // Sphere state
        private GameObject _sphereGO;
        private Mesh _sphereMesh;
        private Material _sphereMaterial;
        private Coroutine _expandCoroutine;
        private bool _sphereMode;

        // Shared state (used by both sphere and skybox paths)
        private Texture2D _thumbnailTexture;

        // ── View mode integration ──────────────────────────────────────────
        public bool IsReady          { get; private set; }
        public bool IsShowing        => _isShowing;
        public bool HasStoredTexture => _thumbnailTexture != null;
        public bool SuppressNextFadeOut { get; set; }
        public event Action OnReady;

        private static readonly int ExposureId = Shader.PropertyToID("_Exposure");
        private static readonly int TexId      = Shader.PropertyToID("_Tex");
        private static readonly int ColorId    = Shader.PropertyToID("_Color");
        private static readonly int MainTexId  = Shader.PropertyToID("_MainTex");

        private Shader _spritesDefaultShader;

        private void Awake()
        {
            IsReady = false;
            _spritesDefaultShader = Shader.Find("Sprites/Default");
        }

        /// <summary>
        /// Displays the panorama on the inside of a sphere that expands from expandStartScale to
        /// expandTargetScale. Falls back to RenderSettings.skybox if Sprites/Default is missing.
        /// Takes ownership of tex — do not destroy it after calling this.
        /// </summary>
        public void Show(Texture2D tex)
        {
            // Stop any in-progress coroutines.
            if (_fadeCoroutine != null)
            {
                StopCoroutine(_fadeCoroutine);
                _fadeCoroutine = null;
            }
            if (_expandCoroutine != null)
            {
                StopCoroutine(_expandCoroutine);
                _expandCoroutine = null;
            }

            // Hide sphere before tearing down its material to avoid one-frame destroyed-texture display.
            if (_sphereGO != null)
                _sphereGO.SetActive(false);

            // Tear down previous materials and texture.
            if (_sphereMaterial != null)
            {
                Destroy(_sphereMaterial);
                _sphereMaterial = null;
            }
            if (_skyboxMaterial != null)
            {
                Destroy(_skyboxMaterial);
                _skyboxMaterial = null;
            }
            if (_thumbnailTexture != null)
            {
                Destroy(_thumbnailTexture);
                _thumbnailTexture = null;
            }

            if (_spritesDefaultShader != null)
            {
                // ── Sphere path ──────────────────────────────────────────────
                if (_sphereGO == null)
                {
                    _sphereGO = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    _sphereGO.name = "PanoramaSphere";

                    // Flip normals + reverse winding so inside faces are visible.
                    _sphereMesh = _sphereGO.GetComponent<MeshFilter>().mesh; // instance copy — stored for explicit cleanup
                    Vector3[] normals = _sphereMesh.normals;
                    for (int i = 0; i < normals.Length; i++)
                        normals[i] = -normals[i];
                    _sphereMesh.normals = normals;

                    int[] tris = _sphereMesh.triangles;
                    for (int i = 0; i < tris.Length; i += 3)
                    {
                        int tmp = tris[i];
                        tris[i] = tris[i + 2];
                        tris[i + 2] = tmp;
                    }
                    _sphereMesh.triangles = tris;

                    Destroy(_sphereGO.GetComponent<SphereCollider>());
                }

                // Sprites/Default uses Blend SrcAlpha OneMinusSrcAlpha — required for alpha fade via _Color.a.
                // If this shader is replaced, ensure the replacement uses the same blend mode.
                _sphereMaterial = new Material(_spritesDefaultShader);
                _sphereMaterial.SetTexture(MainTexId, tex);
                _sphereMaterial.SetColor(ColorId, Color.white);
                _sphereGO.GetComponent<MeshRenderer>().material = _sphereMaterial;
                _thumbnailTexture = tex;

                _sphereGO.transform.position = sphereOrigin != null ? sphereOrigin.position : Vector3.zero;
                _sphereGO.transform.localScale = Vector3.one * expandStartScale;
                _sphereGO.SetActive(true);

                _expandCoroutine = StartCoroutine(ExpandCoroutine());
                _sphereMode = true;
                _isShowing = true;
                IsReady = true;
                OnReady?.Invoke();

                Debug.Log($"[ThumbnailSkyboxController] Panorama sphere shown. Texture={tex.width}x{tex.height}", this);
                onThumbnailShown?.Invoke();
            }
            else
            {
                // ── Skybox fallback ──────────────────────────────────────────
                Debug.LogError("[ThumbnailSkyboxController] Sprites/Default shader not found — falling back to skybox.", this);
                _sphereMode = false;

                Shader panoramicShader = Shader.Find("Skybox/Panoramic");
                if (panoramicShader == null)
                {
                    Debug.LogError("[ThumbnailSkyboxController] Skybox/Panoramic shader not found. Add it to Graphics Settings > Always Included Shaders.", this);
                    return;
                }

                if (!_isShowing || _sphereMode)
                    _previousSkybox = RenderSettings.skybox;
                _thumbnailTexture = tex;

                _skyboxMaterial = new Material(panoramicShader);
                _skyboxMaterial.SetTexture(TexId, tex);
                _skyboxMaterial.SetFloat(ExposureId, 1.0f);

                RenderSettings.skybox = _skyboxMaterial;
                DynamicGI.UpdateEnvironment();

                _isShowing = true;
                IsReady = true;
                OnReady?.Invoke();
                Debug.Log($"[ThumbnailSkyboxController] Panorama skybox shown (fallback). Texture={tex.width}x{tex.height}", this);
                onThumbnailShown?.Invoke();
            }
        }

        /// <summary>
        /// Fades the panorama out (sphere alpha or skybox exposure) and restores state.
        /// No-op if Show() was never successfully called.
        /// </summary>
        public void StartFadeOut()
        {
            if (!_isShowing)
                return;

            if (SuppressNextFadeOut)
            {
                SuppressNextFadeOut = false;
                return;
            }

            // Stop expand before fade so they don't fight over localScale.
            if (_expandCoroutine != null)
            {
                StopCoroutine(_expandCoroutine);
                _expandCoroutine = null;
            }

            if (_fadeCoroutine != null)
                StopCoroutine(_fadeCoroutine);

            _fadeCoroutine = StartCoroutine(FadeOutCoroutine());
        }

        /// <summary>
        /// Stores the panorama texture without displaying it.
        /// The texture will be shown when <see cref="ShowStored"/> is called (e.g. on user pano request).
        /// Replaces any previously stored texture. Takes ownership of tex — do not destroy it after calling.
        /// </summary>
        public void Store(Texture2D tex)
        {
            if (tex == null) return;
            if (_thumbnailTexture != null)
                Destroy(_thumbnailTexture);
            _thumbnailTexture = tex;
            // IsReady stays false and IsShowing stays false — texture is held for future ShowStored().
            // Fire OnReady so ViewModeController.TryApply() is triggered: if DesiredMode is already Pano
            // (user said "pano" before preload finished), TryApply hits the HasStoredTexture branch and shows it.
            OnReady?.Invoke();
        }

        /// <summary>
        /// Re-displays the stored panorama texture.
        /// Saves the reference, nulls the field to prevent Show() from destroying it, then calls Show().
        /// No-op with a warning if no texture is stored.
        /// </summary>
        public void ShowStored()
        {
            if (_thumbnailTexture == null)
            {
                Debug.LogWarning("[ThumbnailSkyboxController] ShowStored: no stored texture.", this);
                return;
            }
            Texture2D stored = _thumbnailTexture;
            _thumbnailTexture = null;   // prevent Show() from destroying it before use
            Show(stored);
        }

        private IEnumerator ExpandCoroutine()
        {
            float elapsed = 0f;

            while (elapsed < expandDuration && _sphereGO != null)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / expandDuration);
                _sphereGO.transform.localScale = Vector3.one * Mathf.Lerp(expandStartScale, expandTargetScale, t);
                yield return null;
            }

            if (_sphereGO != null)
                _sphereGO.transform.localScale = Vector3.one * expandTargetScale;

            _expandCoroutine = null;
        }

        private IEnumerator FadeOutCoroutine()
        {
            IsReady = false;
            float elapsed = 0f;

            if (_sphereMode)
            {
                while (elapsed < fadeOutDuration && _sphereMaterial != null)
                {
                    elapsed += Time.deltaTime;
                    float t = Mathf.Clamp01(elapsed / fadeOutDuration);
                    Color c = _sphereMaterial.GetColor(ColorId);
                    c.a = Mathf.Lerp(1f, 0f, t);
                    _sphereMaterial.SetColor(ColorId, c);
                    yield return null;
                }

                if (_sphereGO != null)
                    _sphereGO.SetActive(false);

                if (_sphereMaterial != null)
                {
                    Destroy(_sphereMaterial);
                    _sphereMaterial = null;
                }
            }
            else
            {
                while (elapsed < fadeOutDuration && _skyboxMaterial != null)
                {
                    elapsed += Time.deltaTime;
                    float t = Mathf.Clamp01(elapsed / fadeOutDuration);
                    _skyboxMaterial.SetFloat(ExposureId, Mathf.Lerp(1f, 0f, t));
                    yield return null;
                }

                RenderSettings.skybox = _previousSkybox;
                DynamicGI.UpdateEnvironment();

                if (_skyboxMaterial != null)
                {
                    Destroy(_skyboxMaterial);
                    _skyboxMaterial = null;
                }
            }

            // Keep _thumbnailTexture alive so ShowStored() can re-show the pano if the
            // user switches back to pano mode after a splat was displayed. Show() and
            // OnDestroy() will destroy it when a new texture is loaded or the component
            // is torn down.

            _isShowing = false;
            _fadeCoroutine = null;
        }

        private void OnDestroy()
        {
            if (_expandCoroutine != null)
            {
                StopCoroutine(_expandCoroutine);
                _expandCoroutine = null;
            }

            if (_fadeCoroutine != null)
            {
                StopCoroutine(_fadeCoroutine);
                _fadeCoroutine = null;
            }

            if (_sphereMaterial != null)
            {
                Destroy(_sphereMaterial);
                _sphereMaterial = null;
            }

            if (_thumbnailTexture != null)
            {
                Destroy(_thumbnailTexture);
                _thumbnailTexture = null;
            }

            if (_sphereGO != null)
            {
                if (_sphereMesh != null)
                {
                    Destroy(_sphereMesh);
                    _sphereMesh = null;
                }
                Destroy(_sphereGO);
                _sphereGO = null;
            }

            // Only restore skybox if the skybox fallback path was active.
            if (!_sphereMode && _isShowing)
            {
                RenderSettings.skybox = _previousSkybox;
                DynamicGI.UpdateEnvironment();
            }

            if (_skyboxMaterial != null)
            {
                Destroy(_skyboxMaterial);
                _skyboxMaterial = null;
            }
        }
    }
}
