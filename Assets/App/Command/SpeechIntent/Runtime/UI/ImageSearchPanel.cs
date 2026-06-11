using Holodeck.Direct;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SpeechIntent
{
    public sealed class ImageSearchPanel : MonoBehaviour
    {
        public PixabayImageSearchService pixabayService;
        public HeadsetCameraCaptureService captureService;
        public VoiceToWorldLabsPluginCoordinator worldCoordinator;
        public ObjectGenerationService objectGenerationService;

        [Header("UI")]
        public TMP_InputField searchInput;
        public RawImage previewImage;
        public AspectRatioFitter previewAspect;
        public TMP_Text statusLabel;
        public TMP_Text attributionLabel;
        public Button searchButton;
        public Button previousButton;
        public Button nextButton;
        public Button useImageButton;
        public Button createWorldButton;
        public Button createObjectButton;

        void Awake()
        {
            ResolveServices();

            if (searchButton != null)
                searchButton.onClick.AddListener(SearchFromInput);
            if (previousButton != null)
                previousButton.onClick.AddListener(Previous);
            if (nextButton != null)
                nextButton.onClick.AddListener(Next);
            if (useImageButton != null)
                useImageButton.onClick.AddListener(UseSelectedImage);
            if (createWorldButton != null)
                createWorldButton.onClick.AddListener(CreateWorld);
            if (createObjectButton != null)
                createObjectButton.onClick.AddListener(CreateObject);
            if (searchInput != null)
                searchInput.onSubmit.AddListener(Search);
            if (previewImage != null && previewAspect == null)
                previewAspect = previewImage.GetComponent<AspectRatioFitter>();
        }

        void OnEnable()
        {
            ResolveServices();
            Subscribe();
            RefreshSelection();
            RefreshButtonState();
        }

        void OnDisable()
        {
            Unsubscribe();
        }

        public void SearchFromInput()
        {
            Search(searchInput != null ? searchInput.text : "");
        }

        public void Search(string query)
        {
            ResolveServices();
            if (pixabayService == null)
            {
                SetStatus("PIXABAY SERVICE NOT FOUND");
                RefreshButtonState();
                return;
            }

            if (!pixabayService.IsConfigured)
            {
                SetStatus("PIXABAY API KEY MISSING");
                ArchStatusBus.Warning("Pixabay API key missing. Set PIXABAY_API_KEY in .env or on the service.", "IMAGE");
                RefreshButtonState();
                return;
            }

            if (searchInput != null)
                searchInput.text = query ?? "";
            SetStatus("SEARCHING PIXABAY...");
            pixabayService.Search(query);
        }

        public void Previous()
        {
            pixabayService?.SelectPrevious();
        }

        public void Next()
        {
            pixabayService?.SelectNext();
        }

        public void UseSelectedImage()
        {
            ResolveServices();
            if (pixabayService == null || pixabayService.SelectedTexture == null)
            {
                SetStatus("NO IMAGE SELECTED");
                ArchStatusBus.Warning("No Pixabay image is selected.", "IMAGE");
                return;
            }

            if (captureService == null)
            {
                SetStatus("IMAGE PROMPT TARGET NOT FOUND");
                ArchStatusBus.Warning("Image prompt target not found.", "IMAGE");
                return;
            }

            PixabayImageResult result = pixabayService.SelectedResult;
            string source = result != null ? result.AttributionText : "Pixabay image";
            captureService.SetExternalCapture(pixabayService.SelectedTexture, source);
            SetStatus("IMAGE READY AS PROMPT");
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

            UseSelectedImage();
            ResolveServices();
            if (worldCoordinator == null)
            {
                SetStatus("WORLD BUILDER NOT FOUND");
                ArchStatusBus.Warning("World builder not found.", "IMAGE");
                return;
            }

            string query = pixabayService != null ? pixabayService.LastQuery : "";
            string prompt = string.IsNullOrWhiteSpace(query)
                ? "Create a world inspired by this image."
                : $"Create a world inspired by this image of {query}.";
            worldCoordinator.TriggerWorldGenerationFromLastCapture(prompt);
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

            UseSelectedImage();
            if (objectGenerationService == null)
                objectGenerationService = ObjectGenerationService.GetOrCreate();

            string query = pixabayService != null ? pixabayService.LastQuery : "";
            string prompt = string.IsNullOrWhiteSpace(query)
                ? "Create a 3D object inspired by this image."
                : $"Create a 3D object inspired by this image of {query}.";

            SetStatus("CREATING OBJECT...");
            if (!objectGenerationService.GenerateFromLastCapture(prompt))
                SetStatus(string.IsNullOrWhiteSpace(objectGenerationService.LastFailureMessage)
                    ? "OBJECT CREATION COULD NOT START"
                    : objectGenerationService.LastFailureMessage);
        }

        void ResolveServices()
        {
            if (pixabayService == null)
                pixabayService = FindFirstObjectByType<PixabayImageSearchService>();
            if (captureService == null)
                captureService = FindFirstObjectByType<HeadsetCameraCaptureService>();
            if (worldCoordinator == null)
                worldCoordinator = FindFirstObjectByType<VoiceToWorldLabsPluginCoordinator>();
        }

        void Subscribe()
        {
            if (pixabayService == null)
                return;

            pixabayService.SelectionChanged += HandleSelectionChanged;
            pixabayService.StatusChanged += SetStatus;
            pixabayService.SearchFailed += SetStatus;
        }

        void Unsubscribe()
        {
            if (pixabayService == null)
                return;

            pixabayService.SelectionChanged -= HandleSelectionChanged;
            pixabayService.StatusChanged -= SetStatus;
            pixabayService.SearchFailed -= SetStatus;
        }

        void RefreshSelection()
        {
            if (pixabayService != null)
                HandleSelectionChanged(pixabayService.SelectedResult, pixabayService.SelectedTexture);
            else
                SetStatus("PIXABAY SERVICE NOT FOUND");
            RefreshButtonState();
        }

        void HandleSelectionChanged(PixabayImageResult result, Texture2D texture)
        {
            if (previewImage != null)
            {
                previewImage.texture = texture;
                previewImage.color = texture != null ? Color.white : new Color(0f, 0f, 0f, 0.65f);
            }

            if (previewAspect != null && texture != null && texture.height > 0)
                previewAspect.aspectRatio = (float)texture.width / texture.height;

            if (attributionLabel != null)
            {
                attributionLabel.text = result != null
                    ? $"{result.AttributionText}\n{result.tags}"
                    : "NO IMAGE SELECTED";
            }

            RefreshButtonState();
        }

        void RefreshButtonState()
        {
            bool configured = pixabayService != null && pixabayService.IsConfigured;
            bool hasResults = pixabayService != null && pixabayService.Results.Count > 0;
            bool hasSelection = pixabayService != null && pixabayService.SelectedTexture != null;
            bool objectCreatorConfigured = ObjectGenerationApiConfig.IsAnyProviderConfigured();
            bool worldLabsConfigured = WorldLabsApiConfig.IsWorldLabsConfigured();

            SetInteractable(searchButton, configured);
            SetInteractable(previousButton, configured && hasResults);
            SetInteractable(nextButton, configured && hasResults);
            SetInteractable(useImageButton, configured && hasSelection);
            SetInteractable(createWorldButton, configured && hasSelection && worldLabsConfigured);
            SetInteractable(createObjectButton, configured && hasSelection && objectCreatorConfigured);
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
    }
}
