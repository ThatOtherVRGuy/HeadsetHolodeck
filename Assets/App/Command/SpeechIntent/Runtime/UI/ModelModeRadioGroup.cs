using System;
using System.Collections;
using Holodeck.Direct;
using Holodeck.Save;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WorldLabs.API;
using WorldLabs.Runtime;

namespace SpeechIntent
{
    [DisallowMultipleComponent]
    public sealed class ModelModeRadioGroup : MonoBehaviour
    {
        [Header("Buttons")]
        public Button draftButton;
        public Button fastButton;
        public Button standardButton;
        public Button highButton;

        [Header("Optional Labels")]
        public Text legacyModeLabel;
        public TMP_Text tmpModeLabel;

        [Header("Dependencies")]
        public VoiceToWorldLabsPluginCoordinator coordinator;
        public WorldBrowserController worldBrowser;
        public WorldLabsWorldManager worldManager;
        public WorldConfigAutoSave worldConfigAutoSave;

        [Header("LCARS Colors")]
        public bool useSharedPalette = true;
        public LcarsPalette palette;
        public Color activeColor = new Color(0.25f, 0.34f, 1f, 1f);
        public Color draftInactiveColor = new Color(1f, 0.55f, 0.03f, 1f);
        public Color fastInactiveColor = new Color(1f, 0.55f, 0.03f, 1f);
        public Color standardInactiveColor = new Color(1f, 0.55f, 0.03f, 1f);
        public Color highInactiveColor = new Color(1f, 0.55f, 0.03f, 1f);
        public Color activeTextColor = Color.black;
        public Color inactiveTextColor = Color.black;

        [Header("Debug")]
        public bool logDebugMessages;

        MarbleModel _selectedModel = MarbleModel.Standard;
        string _lastAppliedConfigId;
        bool _syncing;

        void Awake()
        {
            ResolveDependencies();
            ResolveButtons();
        }

        void OnEnable()
        {
            ResolveDependencies();
            ResolveButtons();
            WireButtonListeners();
            Subscribe();

            MarbleModel initial = coordinator != null
                ? coordinator.CurrentGenerationModel
                : worldBrowser != null ? worldBrowser.SelectedGenerationModel : _selectedModel;
            ApplySelection(initial, syncControllers: true, postStatus: false);
            ApplyLoadedWorldModelIfAvailable();
        }

        void OnDisable()
        {
            UnwireButtonListeners();
            Unsubscribe();
        }

        void Update()
        {
            WorldConfig config = worldConfigAutoSave != null ? worldConfigAutoSave.ActiveConfig : null;
            string configId = config != null ? config.config_id : null;
            if (string.Equals(_lastAppliedConfigId, configId, StringComparison.Ordinal))
                return;

            _lastAppliedConfigId = configId;
            if (config == null)
                return;

            if (TryParseModel(config.generation_model, out MarbleModel model))
                ApplySelection(model, syncControllers: true, postStatus: false);
        }

        public void SelectDraft() => ApplySelection(MarbleModel.Draft, syncControllers: true, postStatus: true);
        public void SelectFast() => ApplySelection(MarbleModel.Fast, syncControllers: true, postStatus: true);
        public void SelectStandard() => ApplySelection(MarbleModel.Standard, syncControllers: true, postStatus: true);
        public void SelectHigh() => ApplySelection(MarbleModel.High, syncControllers: true, postStatus: true);

        public void Refresh()
        {
            ApplySelection(_selectedModel, syncControllers: false, postStatus: false);
        }

        void ApplySelection(MarbleModel model, bool syncControllers, bool postStatus)
        {
            _selectedModel = model;
            PaintButtons(model);
            UpdateLabel(model);

            if (!syncControllers || _syncing)
                return;

            _syncing = true;
            try
            {
                if (coordinator != null && coordinator.CurrentGenerationModel != model)
                    coordinator.SetGenerationModel(model);
                if (worldBrowser != null && worldBrowser.SelectedGenerationModel != model)
                    worldBrowser.SetGenerationModel(model);
            }
            finally
            {
                _syncing = false;
            }

            if (postStatus)
                ArchStatusBus.Post($"Generation model set to {ModelLabel(model)}.", ArchStatusLevel.Info, "MODEL");

            if (logDebugMessages)
                Debug.Log($"[ModelModeRadioGroup] Selected {model}.", this);
        }

        void PaintButtons(MarbleModel model)
        {
            LcarsPalette activePalette = ActivePalette;
            Color selectedColor = useSharedPalette ? activePalette.modelActive : activeColor;
            PaintButton(draftButton, model == MarbleModel.Draft, useSharedPalette ? activePalette.modelDraft : draftInactiveColor, selectedColor);
            PaintButton(fastButton, model == MarbleModel.Fast, useSharedPalette ? activePalette.modelFast : fastInactiveColor, selectedColor);
            PaintButton(standardButton, model == MarbleModel.Standard, useSharedPalette ? activePalette.modelStandard : standardInactiveColor, selectedColor);
            PaintButton(highButton, model == MarbleModel.High, useSharedPalette ? activePalette.modelHigh : highInactiveColor, selectedColor);
        }

        void PaintButton(Button button, bool active, Color inactiveColor, Color selectedColor)
        {
            if (button == null)
                return;

            Graphic target = button.targetGraphic != null ? button.targetGraphic : button.GetComponent<Graphic>();
            if (target != null)
                target.color = active ? selectedColor : inactiveColor;

            Color baseColor = active ? selectedColor : inactiveColor;
            ColorBlock colors = button.colors;
            colors.normalColor = baseColor;
            colors.highlightedColor = Color.Lerp(baseColor, Color.white, 0.18f);
            colors.pressedColor = Color.Lerp(baseColor, Color.black, 0.25f);
            colors.selectedColor = baseColor;
            colors.disabledColor = Color.Lerp(baseColor, Color.black, 0.45f);
            colors.colorMultiplier = 1f;
            button.colors = colors;

            Text legacyText = button.GetComponentInChildren<Text>(true);
            if (legacyText != null)
                legacyText.color = active ? activeTextColor : inactiveTextColor;

            TMP_Text tmpText = button.GetComponentInChildren<TMP_Text>(true);
            if (tmpText != null)
                tmpText.color = active ? activeTextColor : inactiveTextColor;
        }

        LcarsPalette ActivePalette => palette != null ? palette : LcarsPalette.Default;

        void UpdateLabel(MarbleModel model)
        {
            string text = $"Model: {ModelLabel(model)}";
            if (legacyModeLabel != null)
                legacyModeLabel.text = text;
            if (tmpModeLabel != null)
                tmpModeLabel.text = text;
        }

        void WireButtonListeners()
        {
            UnwireButtonListeners();
            if (draftButton != null) draftButton.onClick.AddListener(SelectDraft);
            if (fastButton != null) fastButton.onClick.AddListener(SelectFast);
            if (standardButton != null) standardButton.onClick.AddListener(SelectStandard);
            if (highButton != null) highButton.onClick.AddListener(SelectHigh);
        }

        void UnwireButtonListeners()
        {
            if (draftButton != null) draftButton.onClick.RemoveListener(SelectDraft);
            if (fastButton != null) fastButton.onClick.RemoveListener(SelectFast);
            if (standardButton != null) standardButton.onClick.RemoveListener(SelectStandard);
            if (highButton != null) highButton.onClick.RemoveListener(SelectHigh);
        }

        void Subscribe()
        {
            if (coordinator != null)
                coordinator.OnGenerationModelChanged += HandleGenerationModelChanged;
            if (worldBrowser != null)
                worldBrowser.OnGenerationModelChanged += HandleGenerationModelChanged;
            if (worldManager != null)
                worldManager.OnWorldLoaded += HandleWorldLoaded;
        }

        void Unsubscribe()
        {
            if (coordinator != null)
                coordinator.OnGenerationModelChanged -= HandleGenerationModelChanged;
            if (worldBrowser != null)
                worldBrowser.OnGenerationModelChanged -= HandleGenerationModelChanged;
            if (worldManager != null)
                worldManager.OnWorldLoaded -= HandleWorldLoaded;
        }

        void HandleGenerationModelChanged(MarbleModel model)
        {
            if (_syncing)
                return;

            ApplySelection(model, syncControllers: true, postStatus: false);
        }

        void HandleWorldLoaded(string worldId, GaussianSplatting.Runtime.GaussianSplatRenderer renderer)
        {
            if (!isActiveAndEnabled)
                return;

            StartCoroutine(ApplyLoadedWorldModelNextFrame());
        }

        IEnumerator ApplyLoadedWorldModelNextFrame()
        {
            yield return null;
            yield return null;
            ApplyLoadedWorldModelIfAvailable();
        }

        void ApplyLoadedWorldModelIfAvailable()
        {
            WorldConfig config = worldConfigAutoSave != null ? worldConfigAutoSave.ActiveConfig : null;
            if (config != null && TryParseModel(config.generation_model, out MarbleModel savedModel))
            {
                _lastAppliedConfigId = config.config_id;
                ApplySelection(savedModel, syncControllers: true, postStatus: false);
                return;
            }

            string worldModel = worldManager != null ? worldManager.LastLoadedWorld?.model : null;
            if (TryParseModel(worldModel, out MarbleModel loadedModel))
                ApplySelection(loadedModel, syncControllers: true, postStatus: false);
        }

        void ResolveDependencies()
        {
            if (coordinator == null)
                coordinator = FindFirstObjectByType<VoiceToWorldLabsPluginCoordinator>(FindObjectsInactive.Include);
            if (worldBrowser == null)
                worldBrowser = FindFirstObjectByType<WorldBrowserController>(FindObjectsInactive.Include);
            if (worldManager == null)
                worldManager = FindFirstObjectByType<WorldLabsWorldManager>(FindObjectsInactive.Include);
            if (worldConfigAutoSave == null)
                worldConfigAutoSave = FindFirstObjectByType<WorldConfigAutoSave>(FindObjectsInactive.Include);
        }

        void ResolveButtons()
        {
            draftButton ??= FindButton("draft");
            fastButton ??= FindButton("fast");
            standardButton ??= FindButton("standard");
            highButton ??= FindButton("high");
        }

        Button FindButton(string modelName)
        {
            Button[] buttons = GetComponentsInChildren<Button>(true);
            foreach (Button button in buttons)
            {
                if (button == null)
                    continue;

                if (Matches(button.name, modelName))
                    return button;

                Text legacyText = button.GetComponentInChildren<Text>(true);
                if (legacyText != null && Matches(legacyText.text, modelName))
                    return button;

                TMP_Text tmpText = button.GetComponentInChildren<TMP_Text>(true);
                if (tmpText != null && Matches(tmpText.text, modelName))
                    return button;
            }

            return null;
        }

        static bool TryParseModel(string raw, out MarbleModel model)
        {
            model = MarbleModel.Standard;
            if (string.IsNullOrWhiteSpace(raw))
                return false;

            string value = Normalize(raw);
            switch (value)
            {
                case "draft":
                case "marble10draft":
                    model = MarbleModel.Draft;
                    return true;
                case "fast":
                case "low":
                case "marble10":
                    model = MarbleModel.Fast;
                    return true;
                case "standard":
                case "normal":
                case "marble11":
                    model = MarbleModel.Standard;
                    return true;
                case "high":
                case "best":
                case "premium":
                case "marble11plus":
                    model = MarbleModel.High;
                    return true;
                default:
                    return Enum.TryParse(raw, ignoreCase: true, out model);
            }
        }

        static bool Matches(string raw, string modelName)
        {
            return string.Equals(Normalize(raw), Normalize(modelName), StringComparison.Ordinal);
        }

        static string Normalize(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return string.Empty;

            var buffer = new System.Text.StringBuilder(raw.Length);
            foreach (char c in raw)
            {
                if (char.IsLetterOrDigit(c))
                    buffer.Append(char.ToLowerInvariant(c));
            }

            return buffer.ToString();
        }

        static string ModelLabel(MarbleModel model) => model switch
        {
            MarbleModel.Draft => "Draft",
            MarbleModel.Fast => "Fast",
            MarbleModel.Standard => "Standard",
            MarbleModel.High => "High",
            _ => model.ToString()
        };
    }
}
