using Holodeck.Direct;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SpeechIntent
{
    public sealed class HeadsetCameraPreviewPanel : MonoBehaviour
    {
        public HeadsetCameraCaptureService captureService;
        public VoiceToWorldLabsPluginCoordinator worldCoordinator;
        public ObjectGenerationService objectGenerationService;
        public RawImage previewImage;
        public AspectRatioFitter previewAspect;
        public TMP_Text statusLabel;
        public Button createWorldButton;
        public Button createObjectButton;
        public Button recaptureButton;
        public Button confirmCaptureButton;
        public Button cancelPreviewButton;
        public RectTransform hudRoot;
        public Color hudMarkerColor = new Color(1f, 0.78f, 0.08f, 0.95f);

        void Awake()
        {
            if (captureService == null)
                captureService = FindFirstObjectByType<HeadsetCameraCaptureService>();
            if (worldCoordinator == null)
                worldCoordinator = FindFirstObjectByType<VoiceToWorldLabsPluginCoordinator>();

            if (createWorldButton != null)
                createWorldButton.onClick.AddListener(CreateWorld);
            if (createObjectButton != null)
                createObjectButton.onClick.AddListener(CreateObject);
            if (recaptureButton != null)
                recaptureButton.onClick.AddListener(Recapture);
            if (confirmCaptureButton != null)
                confirmCaptureButton.onClick.AddListener(ConfirmCapture);
            if (cancelPreviewButton != null)
                cancelPreviewButton.onClick.AddListener(CancelPreview);
            if (previewImage != null && previewAspect == null)
                previewAspect = previewImage.GetComponent<AspectRatioFitter>();
            EnsureHudMarkers();
            SetHudVisible(false);
        }

        void OnEnable()
        {
            if (captureService != null)
            {
                captureService.CaptureChanged += HandleCaptureChanged;
                captureService.CaptureFailed += HandleCaptureFailed;
                captureService.PreviewChanged += HandlePreviewChanged;
                HandleCaptureChanged(captureService.LastCapturedTexture);
            }
            else
            {
                SetStatus("HEADSET CAMERA SERVICE NOT FOUND");
            }
            RefreshButtonState();
        }

        void OnDisable()
        {
            if (captureService != null)
            {
                captureService.CaptureChanged -= HandleCaptureChanged;
                captureService.CaptureFailed -= HandleCaptureFailed;
                captureService.PreviewChanged -= HandlePreviewChanged;
            }
        }

        public void Recapture()
        {
            if (captureService == null)
            {
                SetStatus("HEADSET CAMERA SERVICE NOT FOUND");
                return;
            }

            SetStatus("CAMERA PREVIEW STARTING...");
            captureService.BeginPreview();
        }

        public void ConfirmCapture()
        {
            if (captureService == null)
            {
                SetStatus("HEADSET CAMERA SERVICE NOT FOUND");
                return;
            }

            SetStatus("CAPTURING FRAME...");
            captureService.ConfirmPreviewCapture();
        }

        public void CancelPreview()
        {
            if (captureService == null)
                return;

            captureService.CancelPreview();
            SetHudVisible(false);
            HandleCaptureChanged(captureService.LastCapturedTexture);
        }

        public void CreateWorld()
        {
            if (!WorldLabsApiConfig.IsWorldLabsConfigured())
            {
                SetStatus("WORLDLABS API KEY MISSING");
                ArchStatusBus.Warning("WorldLabs API key missing. Set WORLDLABS_API_KEY in .env or on the service.", "WORLD");
                RefreshButtonState();
                return;
            }

            if (worldCoordinator == null)
                worldCoordinator = FindFirstObjectByType<VoiceToWorldLabsPluginCoordinator>();

            if (worldCoordinator == null)
            {
                SetStatus("WORLD BUILDER NOT FOUND");
                ArchStatusBus.Warning("World builder not found.", "CAPTURE");
                return;
            }

            SetStatus("SENDING IMAGE TO WORLD BUILDER...");
            worldCoordinator.TriggerWorldGenerationFromLastCapture("Create a world inspired by this image.");
        }

        public void CreateObject()
        {
            if (!ObjectGenerationApiConfig.IsAnyProviderConfigured())
            {
                SetStatus("OBJECT API KEY MISSING");
                ArchStatusBus.Warning("Object generator API key missing. Set THREEDAISTUDIO_API_KEY, or HITEM_ACCESS_KEY and HITEM_SECRET_KEY.", "OBJECT");
                RefreshButtonState();
                return;
            }

            if (objectGenerationService == null)
                objectGenerationService = ObjectGenerationService.GetOrCreate();

            SetStatus("CREATING OBJECT...");
            if (!objectGenerationService.GenerateFromLastCapture("Create a 3D object inspired by this image."))
                SetStatus(string.IsNullOrWhiteSpace(objectGenerationService.LastFailureMessage)
                    ? "OBJECT CREATION COULD NOT START"
                    : objectGenerationService.LastFailureMessage);
        }

        void HandleCaptureChanged(Texture2D texture)
        {
            if (previewImage != null)
            {
                previewImage.texture = texture;
                previewImage.color = texture != null ? Color.white : new Color(0f, 0f, 0f, 0.65f);
            }
            if (previewAspect != null && texture != null && texture.height > 0)
                previewAspect.aspectRatio = (float)texture.width / texture.height;
            SetHudVisible(false);

            SetStatus(texture != null
                ? $"CAPTURE READY {texture.width}x{texture.height}"
                : "NO CAPTURE");
            RefreshButtonState();
        }

        void HandlePreviewChanged(Texture texture, bool active)
        {
            if (previewImage != null)
            {
                previewImage.texture = texture;
                previewImage.color = active && texture != null ? Color.white : new Color(0f, 0f, 0f, 0.65f);
            }

            if (previewAspect != null && active && texture != null && texture.height > 0)
                previewAspect.aspectRatio = (float)texture.width / texture.height;

            SetHudVisible(active);
            if (active)
                SetStatus("AIM CAMERA - SAY OK, SHOOT, OR CAPTURE");
            else if (captureService != null)
                HandleCaptureChanged(captureService.LastCapturedTexture);
        }

        void HandleCaptureFailed(string message)
        {
            SetStatus(message);
            RefreshButtonState();
        }

        void RefreshButtonState()
        {
            bool hasCapture = captureService != null && captureService.LastCapturedTexture != null;
            bool objectCreatorConfigured = ObjectGenerationApiConfig.IsAnyProviderConfigured();
            bool worldLabsConfigured = WorldLabsApiConfig.IsWorldLabsConfigured();
            SetInteractable(createObjectButton, hasCapture && objectCreatorConfigured);
            SetInteractable(createWorldButton, hasCapture && worldLabsConfigured);
            LcarsPanelStyler.StylePanel(gameObject);
        }

        static void SetInteractable(Button button, bool interactable)
        {
            if (button == null)
                return;

            button.interactable = interactable;
            if (!interactable)
                LcarsPanelStyler.StyleDisabledButton(button);
        }

        void SetStatus(string message)
        {
            if (statusLabel != null)
                statusLabel.text = message ?? "";
        }

        void EnsureHudMarkers()
        {
            if (previewImage == null)
                return;

            if (hudRoot == null)
            {
                Transform found = previewImage.transform.Find("CaptureHud");
                GameObject hud = found != null ? found.gameObject : new GameObject("CaptureHud", typeof(RectTransform));
                hud.transform.SetParent(previewImage.transform, false);
                hudRoot = hud.GetComponent<RectTransform>();
            }

            hudRoot.anchorMin = Vector2.zero;
            hudRoot.anchorMax = Vector2.one;
            hudRoot.offsetMin = Vector2.zero;
            hudRoot.offsetMax = Vector2.zero;
            hudRoot.pivot = new Vector2(0.5f, 0.5f);

            MakeHudLine("TopLeftH", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -18f), new Vector2(92f, 5f), new Vector2(0f, 0.5f));
            MakeHudLine("TopLeftV", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, 0f), new Vector2(5f, 92f), new Vector2(0.5f, 1f));
            MakeHudLine("TopRightH", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(0f, -18f), new Vector2(92f, 5f), new Vector2(1f, 0.5f));
            MakeHudLine("TopRightV", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-18f, 0f), new Vector2(5f, 92f), new Vector2(0.5f, 1f));
            MakeHudLine("BottomLeftH", new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 18f), new Vector2(92f, 5f), new Vector2(0f, 0.5f));
            MakeHudLine("BottomLeftV", new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(18f, 0f), new Vector2(5f, 92f), new Vector2(0.5f, 0f));
            MakeHudLine("BottomRightH", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(0f, 18f), new Vector2(92f, 5f), new Vector2(1f, 0.5f));
            MakeHudLine("BottomRightV", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-18f, 0f), new Vector2(5f, 92f), new Vector2(0.5f, 0f));
        }

        void MakeHudLine(string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPosition, Vector2 size, Vector2 pivot)
        {
            Transform found = hudRoot.Find(name);
            GameObject go = found != null ? found.gameObject : new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(hudRoot, false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.anchoredPosition = anchoredPosition;
            rt.sizeDelta = size;

            Image image = go.GetComponent<Image>();
            image.color = hudMarkerColor;
            image.raycastTarget = false;
        }

        void SetHudVisible(bool visible)
        {
            if (hudRoot != null)
                hudRoot.gameObject.SetActive(visible);
        }
    }
}
