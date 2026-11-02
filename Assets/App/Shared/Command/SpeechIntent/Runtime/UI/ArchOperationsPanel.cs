using TMPro;
using Holodeck.Direct;
using SpeechIntent.HandInput;
using UnityEngine;
using UnityEngine.UI;
using WorldLabs.Runtime;

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
            ObjectCatalog,
            Options,
            Capture
        }

        public DefaultPanel defaultPanel = DefaultPanel.MyWorlds;
        public GameObject worldLabsPanel;
        public GameObject myWorldsPanel;
        public GameObject contentLoadingPanel;
        public GameObject cameraCapturePanel;
        public GameObject imageSearchPanel;
        public GameObject objectCatalogPanel;
        [Header("World View Capture")]
        [Tooltip("Dedicated panel for capturing the current world's thumbnail or panorama.")]
        public GameObject worldViewCapturePanel;
        [Header("Options")]
        [Tooltip("Dedicated panel for world snap, direct world grab, and dominant-hand controls.")]
        public GameObject optionsPanel;
        public PixabayImageSearchService pixabayImageSearchService;

        [Header("Diagnostics")]
        [Tooltip("Shows dev-only OpenAI error simulation buttons on the operations panel.")]
        public bool showOpenAiDiagnosticButtons = true;
        public VoiceCommandRouter voiceCommandRouter;
        public WorldManipulationController worldManipulationController;

        [Header("Startup Preload")]
        public bool preloadObjectCatalogOnStartup = true;
        [Min(0)] public int objectCatalogPreloadDelayFrames = 4;

        GameObject _activePanel;
        Coroutine _objectCatalogPreloadCoroutine;

        void OnEnable()
        {
            ResolveServices();
            EnsureOpenAiDiagnosticButtons();
            ResolveOptionsPanel();
            ResolveWorldViewCapturePanel();
            if (worldLabsPanel != null || myWorldsPanel != null || contentLoadingPanel != null || cameraCapturePanel != null || imageSearchPanel != null || objectCatalogPanel != null || optionsPanel != null || worldViewCapturePanel != null)
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
                case DefaultPanel.Options:
                    ShowOptions();
                    break;
                case DefaultPanel.Capture:
                    ShowWorldViewCapture();
                    break;
                default:
                    ShowMyWorlds();
                    break;
            }
        }

        public void ShowWorldLabs()
        {
            // The WorldLabs grid is revealed under the same XR pointer that pressed
            // this navigation button. Briefly ignore card clicks so that press cannot
            // be handed off to a newly visible world card.
            WorldBrowserController browser = worldLabsPanel?.GetComponentInChildren<WorldBrowserController>(true);
            Debug.Log($"[WorldLabsInputTrace] ShowWorldLabs invoked at frame {Time.frameCount}; " +
                      $"panelWasActive={worldLabsPanel != null && worldLabsPanel.activeInHierarchy}; " +
                      $"browserFound={browser != null}.", this);
            browser?.BlockWorldCardClicksFor(0.35f);
            ShowOnly(worldLabsPanel);
            Debug.Log($"[WorldLabsInputTrace] ShowWorldLabs completed at frame {Time.frameCount}; " +
                      $"panelIsActive={worldLabsPanel != null && worldLabsPanel.activeInHierarchy}; " +
                      $"browserCanvasActive={browser?.browserCanvas != null && browser.browserCanvas.gameObject.activeInHierarchy}.", this);
        }
        public void ShowMyWorlds() => ShowOnly(myWorldsPanel);
        public void ShowContentLoading() => ShowOnly(contentLoadingPanel);
        public void ShowCameraCapture() => ShowOnly(cameraCapturePanel);
        public void ShowImageSearch() => ShowOnly(imageSearchPanel);
        public void ShowObjectCatalog() => ShowOnly(objectCatalogPanel);
        public void ShowWorldViewCapture()
        {
            ResolveWorldViewCapturePanel();
            ShowOnly(worldViewCapturePanel);
        }
        public void ShowOptions()
        {
            ResolveOptionsPanel();
            ShowOnly(optionsPanel);
        }

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
            SetActive(worldViewCapturePanel, worldViewCapturePanel == target);
            SetActive(optionsPanel, optionsPanel == target);
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
            LcarsPanelStyler.StylePanel(worldViewCapturePanel);
            LcarsPanelStyler.StylePanel(optionsPanel);

            foreach (Button button in GetComponentsInChildren<Button>(true))
            {
                if (BelongsTo(button.gameObject, worldLabsPanel) ||
                    BelongsTo(button.gameObject, myWorldsPanel) ||
                    BelongsTo(button.gameObject, contentLoadingPanel) ||
                    BelongsTo(button.gameObject, cameraCapturePanel) ||
                    BelongsTo(button.gameObject, imageSearchPanel) ||
                    BelongsTo(button.gameObject, objectCatalogPanel) ||
                    BelongsTo(button.gameObject, worldViewCapturePanel) ||
                    BelongsTo(button.gameObject, optionsPanel))
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
                    (_activePanel == objectCatalogPanel && label.Contains("objects")) ||
                    (_activePanel == worldViewCapturePanel && label.Contains("capture")) ||
                    (_activePanel == optionsPanel && label.Contains("options"));
                LcarsPanelStyler.StyleNavButton(button, active);
            }
        }

        void ResolveServices()
        {
            if (pixabayImageSearchService == null)
                pixabayImageSearchService = FindFirstObjectByType<PixabayImageSearchService>(FindObjectsInactive.Include);
            if (voiceCommandRouter == null)
                voiceCommandRouter = FindFirstObjectByType<VoiceCommandRouter>(FindObjectsInactive.Include);
            if (worldManipulationController == null)
                worldManipulationController = FindFirstObjectByType<WorldManipulationController>(FindObjectsInactive.Include);
            if (worldManipulationController == null)
            {
                WorldActionDispatcher dispatcher = FindFirstObjectByType<WorldActionDispatcher>(FindObjectsInactive.Include);
                if (dispatcher != null)
                    worldManipulationController = dispatcher.GetComponent<WorldManipulationController>()
                                                  ?? dispatcher.gameObject.AddComponent<WorldManipulationController>();
            }
        }

        void EnsureOpenAiDiagnosticButtons()
        {
            ResolveServices();

            bool shouldShow = showOpenAiDiagnosticButtons && voiceCommandRouter != null;
            Button quota = EnsureDiagnosticButton(
                "OpenAiQuotaDiagnosticButton",
                "OPENAI QUOTA",
                new Vector2(160, 350),
                new Color(0.90f, 0.38f, 0.05f, 1f));
            Button rate = EnsureDiagnosticButton(
                "OpenAiRateLimitDiagnosticButton",
                "OPENAI RATE",
                new Vector2(160, 305),
                new Color(0.66f, 0.52f, 0.94f, 1f));

            ConfigureDiagnosticButton(quota, shouldShow, () => voiceCommandRouter?.SimulateOpenAiQuotaErrorForDebug());
            ConfigureDiagnosticButton(rate, shouldShow, () => voiceCommandRouter?.SimulateOpenAiRateLimitForDebug());
        }

        /// <summary>
        /// Editor-initialization entry point. This deliberately builds the Options
        /// hierarchy only when invoked by Holodeck/Setup Options Panel, never at runtime.
        /// </summary>
        public void BuildOptionsPanelLayout()
        {
            ResolveServices();
            if (worldManipulationController == null)
                return;

            RectTransform host = FindOperationsHost();
            if (host == null)
                return;

            if (optionsPanel == null)
            {
                Transform existing = host.Find("OptionsPanel");
                optionsPanel = existing != null ? existing.gameObject : new GameObject("OptionsPanel", typeof(RectTransform), typeof(Image));
                if (existing == null)
                    optionsPanel.transform.SetParent(host, false);
            }

            RectTransform panelRect = optionsPanel.GetComponent<RectTransform>();
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;
            Image background = optionsPanel.GetComponent<Image>();
            background.color = new Color(0.025f, 0.035f, 0.065f, 0.97f);

            MoveLegacyWorldManipulationControls();
            EnsureSectionLabel("WorldManipulationTitle", "OPTIONS: SNAP / WORLD", new Vector2(0, 260));
            ConfigureWorldButton("WorldSnapOn", "SNAP ON", new Vector2(-120, 210), new Color(0.25f, 0.75f, 0.50f, 1f), () => SetSnapEnabled(true), new Vector2(150, 42));
            ConfigureWorldButton("WorldSnapOff", "SNAP OFF", new Vector2(120, 210), new Color(0.90f, 0.38f, 0.05f, 1f), () => SetSnapEnabled(false), new Vector2(150, 42));
            EnsureSectionLabel("WorldGridLabel", GridLabel(), new Vector2(-205, 145));
            ConfigureWorldButton("WorldGridOff", "OFF", new Vector2(-65, 145), new Color(0.64f, 0.52f, 0.94f, 1f), () => SetGrid(0f));
            ConfigureWorldButton("WorldGrid1Cm", "1 CM", new Vector2(45, 145), new Color(0.94f, 0.69f, 0.29f, 1f), () => SetGrid(0.01f));
            ConfigureWorldButton("WorldGrid10Cm", "10 CM", new Vector2(155, 145), new Color(0.94f, 0.69f, 0.29f, 1f), () => SetGrid(0.1f));

            EnsureSectionLabel("WorldAngleLabel", AngleLabel(), new Vector2(-205, 85));
            ConfigureWorldButton("WorldAngleOff", "OFF", new Vector2(-65, 85), new Color(0.64f, 0.52f, 0.94f, 1f), () => SetAngle(0f));
            ConfigureWorldButton("WorldAngle15", "15 DEG", new Vector2(45, 85), new Color(0.95f, 0.48f, 0.17f, 1f), () => SetAngle(15f));
            ConfigureWorldButton("WorldAngle30", "30 DEG", new Vector2(155, 85), new Color(0.95f, 0.48f, 0.17f, 1f), () => SetAngle(30f));

            ConfigureWorldButton("WorldGrab", "GRAB WORLD", new Vector2(-120, 15), new Color(0.25f, 0.75f, 0.50f, 1f), GrabWorld, new Vector2(150, 42));
            ConfigureWorldButton("WorldRelease", "RELEASE", new Vector2(120, 15), new Color(0.90f, 0.38f, 0.05f, 1f), ReleaseWorld, new Vector2(150, 42));
            ConfigureWorldButton("WorldDominantLeft", "DOM LEFT", new Vector2(-120, -55), new Color(0.40f, 0.70f, 0.92f, 1f), () => SetDominantHand(HandedInputSide.Left), new Vector2(150, 42));
            ConfigureWorldButton("WorldDominantRight", "DOM RIGHT", new Vector2(120, -55), new Color(0.40f, 0.70f, 0.92f, 1f), () => SetDominantHand(HandedInputSide.Right), new Vector2(150, 42));
        }

        RectTransform FindOperationsHost()
        {
            Transform direct = transform.Find("OperationsHost");
            if (direct is RectTransform directRect)
                return directRect;
            foreach (RectTransform candidate in GetComponentsInChildren<RectTransform>(true))
                if (candidate.name == "OperationsHost")
                    return candidate;
            return transform as RectTransform;
        }

        void ResolveOptionsPanel()
        {
            if (optionsPanel != null)
                return;

            RectTransform host = FindOperationsHost();
            Transform existing = host != null ? host.Find("OptionsPanel") : null;
            if (existing != null)
                optionsPanel = existing.gameObject;
        }

        void ResolveWorldViewCapturePanel()
        {
            if (worldViewCapturePanel != null)
                return;

            RectTransform host = FindOperationsHost();
            Transform existing = host != null ? host.Find("WorldViewCapturePanel") : null;
            if (existing != null)
                worldViewCapturePanel = existing.gameObject;
        }

        void MoveLegacyWorldManipulationControls()
        {
            string[] names = { "WorldManipulationTitle", "WorldSnapOn", "WorldSnapOff", "WorldGridLabel", "WorldGridOff", "WorldGrid1Cm", "WorldGrid10Cm", "WorldAngleLabel", "WorldAngleOff", "WorldAngle15", "WorldAngle30", "WorldGrab", "WorldRelease", "WorldDominantLeft", "WorldDominantRight" };
            foreach (string name in names)
            {
                Transform legacy = transform.Find(name);
                if (legacy != null && legacy.parent != optionsPanel.transform)
                    legacy.SetParent(optionsPanel.transform, false);
            }
        }

        Transform OptionsControlsParent => optionsPanel != null ? optionsPanel.transform : transform;

        void SetGrid(float meters)
        {
            worldManipulationController?.SetPositionSnap(meters);
            UpdateWorldManipulationLabels();
            ArchStatusBus.Info(meters <= 0f ? "Grid snap off." : $"Grid snap set to {meters * 100f:g} centimeters.", "SNAP");
        }

        void SetSnapEnabled(bool enabled)
        {
            worldManipulationController?.SetSnapEnabled(enabled);
            UpdateWorldManipulationLabels();
            ArchStatusBus.Info(enabled ? "Snap on." : "Snap off.", "SNAP");
        }

        void SetAngle(float degrees)
        {
            worldManipulationController?.SetAngleSnap(degrees);
            UpdateWorldManipulationLabels();
            ArchStatusBus.Info(degrees <= 0f ? "Angle snap off." : $"Angle snap set to {degrees:g} degrees.", "SNAP");
        }

        void GrabWorld()
        {
            worldManipulationController?.GrabWorld();
        }

        void ReleaseWorld()
        {
            worldManipulationController?.ReleaseWorld();
        }

        void SetDominantHand(HandedInputSide side)
        {
            worldManipulationController?.SetDominantHand(side);
            ArchStatusBus.Info($"Dominant hand set to {side.ToString().ToLowerInvariant()}.", "WORLD");
        }

        void UpdateWorldManipulationLabels()
        {
            SetLabelText("WorldGridLabel", GridLabel());
            SetLabelText("WorldAngleLabel", AngleLabel());
        }

        string GridLabel()
        {
            if (worldManipulationController != null && !worldManipulationController.SnapEnabled)
                return "GRID: SNAP OFF";
            float meters = worldManipulationController != null ? worldManipulationController.ConfiguredPositionSnapMeters : 0f;
            return meters <= 0f ? "GRID: OFF" : meters < 1f ? $"GRID: {meters * 100f:g} CM" : $"GRID: {meters:g} M";
        }

        string AngleLabel()
        {
            if (worldManipulationController != null && !worldManipulationController.SnapEnabled)
                return "ANGLE: SNAP OFF";
            float degrees = worldManipulationController != null ? worldManipulationController.ConfiguredAngleSnapDegrees : 0f;
            return degrees <= 0f ? "ANGLE: OFF" : $"ANGLE: {degrees:g} DEG";
        }

        void EnsureSectionLabel(string name, string text, Vector2 position)
        {
            Transform existing = OptionsControlsParent.Find(name);
            GameObject go = existing != null ? existing.gameObject : new GameObject(name, typeof(RectTransform));
            if (existing == null)
                go.transform.SetParent(OptionsControlsParent, false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = position;
            rt.sizeDelta = new Vector2(210, 34);
            TextMeshProUGUI tmp = go.GetComponent<TextMeshProUGUI>() ?? go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = 17f;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = new Color(0.98f, 0.77f, 0.31f, 1f);
            tmp.raycastTarget = false;
        }

        void SetLabelText(string name, string value)
        {
            Transform found = OptionsControlsParent.Find(name);
            TMP_Text label = found != null ? found.GetComponent<TMP_Text>() : null;
            if (label != null)
                label.text = value;
        }

        void ConfigureWorldButton(string name, string label, Vector2 position, Color color, UnityEngine.Events.UnityAction action, Vector2? size = null)
        {
            Button button = EnsureDiagnosticButton(name, label, position, color);
            RectTransform rt = button.GetComponent<RectTransform>();
            rt.sizeDelta = size ?? new Vector2(86, 36);
            button.gameObject.SetActive(true);
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(action);
        }

        Button EnsureDiagnosticButton(string name, string label, Vector2 position, Color color)
        {
            Transform existing = OptionsControlsParent.Find(name);
            GameObject go = existing != null ? existing.gameObject : new GameObject(name, typeof(RectTransform));
            if (existing == null)
                go.transform.SetParent(OptionsControlsParent, false);

            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = position;
            rt.sizeDelta = new Vector2(250, 36);

            Image image = go.GetComponent<Image>();
            if (image == null)
                image = go.AddComponent<Image>();
            image.color = color;

            Button button = go.GetComponent<Button>();
            if (button == null)
                button = go.AddComponent<Button>();

            Transform labelTransform = go.transform.Find("Label");
            GameObject labelObject = labelTransform != null ? labelTransform.gameObject : new GameObject("Label", typeof(RectTransform));
            if (labelTransform == null)
                labelObject.transform.SetParent(go.transform, false);

            RectTransform labelRt = labelObject.GetComponent<RectTransform>();
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = Vector2.zero;
            labelRt.offsetMax = Vector2.zero;

            TextMeshProUGUI tmp = labelObject.GetComponent<TextMeshProUGUI>();
            if (tmp == null)
                tmp = labelObject.AddComponent<TextMeshProUGUI>();
            tmp.text = label;
            tmp.fontSize = 18f;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.black;
            tmp.raycastTarget = false;

            return button;
        }

        static void ConfigureDiagnosticButton(Button button, bool visible, UnityEngine.Events.UnityAction action)
        {
            if (button == null)
                return;

            button.gameObject.SetActive(visible);
            button.onClick.RemoveAllListeners();
            if (visible && action != null)
                button.onClick.AddListener(action);
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
