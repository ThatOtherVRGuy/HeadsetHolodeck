using TMPro;
using Holodeck.Direct;
using UnityEngine;
using UnityEngine.UI;

namespace SpeechIntent
{
    public class ArchOperationsPanel : MonoBehaviour
    {
        public enum DefaultPanel
        {
            MyWorlds,
            WorldLabs,
            ContentLoading,
            CameraCapture,
            ImageSearch,
            ObjectCatalog
        }

        public DefaultPanel defaultPanel = DefaultPanel.MyWorlds;
        public GameObject worldLabsPanel;
        public GameObject myWorldsPanel;
        public GameObject contentLoadingPanel;
        public GameObject cameraCapturePanel;
        public GameObject imageSearchPanel;
        public GameObject objectCatalogPanel;
        public PixabayImageSearchService pixabayImageSearchService;

        [Header("Startup Preload")]
        public bool preloadObjectCatalogOnStartup = true;
        [Min(0)] public int objectCatalogPreloadDelayFrames = 4;

        GameObject _activePanel;
        Coroutine _objectCatalogPreloadCoroutine;

        void OnEnable()
        {
            ResolveServices();
            if (worldLabsPanel != null || myWorldsPanel != null || contentLoadingPanel != null || cameraCapturePanel != null || imageSearchPanel != null || objectCatalogPanel != null)
                ShowDefault();
            if (preloadObjectCatalogOnStartup && objectCatalogPanel != null)
                _objectCatalogPreloadCoroutine = StartCoroutine(PreloadObjectCatalogAfterStartup());
        }

        void OnDisable()
        {
            if (_objectCatalogPreloadCoroutine != null)
            {
                StopCoroutine(_objectCatalogPreloadCoroutine);
                _objectCatalogPreloadCoroutine = null;
            }
        }

        public void ShowDefault()
        {
            switch (defaultPanel)
            {
                case DefaultPanel.WorldLabs:
                    ShowWorldLabs();
                    break;
                case DefaultPanel.ContentLoading:
                    ShowContentLoading();
                    break;
                case DefaultPanel.CameraCapture:
                    ShowCameraCapture();
                    break;
                case DefaultPanel.ImageSearch:
                    ShowImageSearch();
                    break;
                case DefaultPanel.ObjectCatalog:
                    ShowObjectCatalog();
                    break;
                default:
                    ShowMyWorlds();
                    break;
            }
        }

        public void ShowWorldLabs() => ShowOnly(worldLabsPanel);
        public void ShowMyWorlds() => ShowOnly(myWorldsPanel);
        public void ShowContentLoading() => ShowOnly(contentLoadingPanel);
        public void ShowCameraCapture() => ShowOnly(cameraCapturePanel);
        public void ShowImageSearch() => ShowOnly(imageSearchPanel);
        public void ShowObjectCatalog() => ShowOnly(objectCatalogPanel);

        System.Collections.IEnumerator PreloadObjectCatalogAfterStartup()
        {
            int frames = Mathf.Max(0, objectCatalogPreloadDelayFrames);
            for (int i = 0; i < frames; i++)
                yield return null;

            CachedObjectCatalogPanel catalog = objectCatalogPanel != null
                ? objectCatalogPanel.GetComponent<CachedObjectCatalogPanel>()
                : null;
            if (catalog != null && objectCatalogPanel != _activePanel)
                catalog.RefreshInBackground(this);

            _objectCatalogPreloadCoroutine = null;
        }

        void ShowOnly(GameObject target)
        {
            _activePanel = target;
            SetActive(worldLabsPanel, worldLabsPanel == target);
            SetActive(myWorldsPanel, myWorldsPanel == target);
            SetActive(contentLoadingPanel, contentLoadingPanel == target);
            SetActive(cameraCapturePanel, cameraCapturePanel == target);
            SetActive(imageSearchPanel, imageSearchPanel == target);
            SetActive(objectCatalogPanel, objectCatalogPanel == target);
            ApplyStyle();
        }

        static void SetActive(GameObject go, bool active)
        {
            if (go != null) go.SetActive(active);
        }

        void ApplyStyle()
        {
            LcarsPanelStyler.StylePanel(worldLabsPanel);
            LcarsPanelStyler.StylePanel(myWorldsPanel);
            LcarsPanelStyler.StylePanel(contentLoadingPanel);
            LcarsPanelStyler.StylePanel(cameraCapturePanel);
            LcarsPanelStyler.StylePanel(imageSearchPanel);
            LcarsPanelStyler.StylePanel(objectCatalogPanel);

            foreach (Button button in GetComponentsInChildren<Button>(true))
            {
                if (BelongsTo(button.gameObject, worldLabsPanel) ||
                    BelongsTo(button.gameObject, myWorldsPanel) ||
                    BelongsTo(button.gameObject, contentLoadingPanel) ||
                    BelongsTo(button.gameObject, cameraCapturePanel) ||
                    BelongsTo(button.gameObject, imageSearchPanel) ||
                    BelongsTo(button.gameObject, objectCatalogPanel))
                {
                    continue;
                }

                string label = ButtonLabel(button).ToLowerInvariant();
                bool disabled = ShouldDisableNavButton(label);
                button.interactable = !disabled;
                bool active =
                    (_activePanel == worldLabsPanel && (label.Contains("worldlabs") || label.Contains("create"))) ||
                    (_activePanel == myWorldsPanel && (label.Contains("my worlds") || label == "load")) ||
                    (_activePanel == contentLoadingPanel && label.Contains("files")) ||
                    (_activePanel == cameraCapturePanel && label.Contains("camera")) ||
                    (_activePanel == imageSearchPanel && label.Contains("image")) ||
                    (_activePanel == objectCatalogPanel && label.Contains("objects"));
                LcarsPanelStyler.StyleNavButton(button, active);
            }
        }

        void ResolveServices()
        {
            if (pixabayImageSearchService == null)
                pixabayImageSearchService = FindFirstObjectByType<PixabayImageSearchService>(FindObjectsInactive.Include);
        }

        bool ShouldDisableNavButton(string label)
        {
            if (label.Contains("image"))
            {
                ResolveServices();
                return pixabayImageSearchService == null || !pixabayImageSearchService.IsConfigured;
            }

            if (label.Contains("worldlabs") || label == "create")
                return !WorldLabsApiConfig.IsWorldLabsConfigured();

            return false;
        }

        static bool BelongsTo(GameObject child, GameObject parent)
        {
            if (child == null || parent == null)
                return false;
            return child.transform == parent.transform || child.transform.IsChildOf(parent.transform);
        }

        static string ButtonLabel(Button button)
        {
            TMP_Text tmp = button.GetComponentInChildren<TMP_Text>(true);
            if (tmp != null)
                return tmp.text ?? "";
            Text legacy = button.GetComponentInChildren<Text>(true);
            return legacy != null ? legacy.text ?? "" : "";
        }
    }
}
