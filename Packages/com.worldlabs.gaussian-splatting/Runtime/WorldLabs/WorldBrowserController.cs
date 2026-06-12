// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.Events;
using WorldLabs.API;

namespace WorldLabs.Runtime
{
    /// <summary>
    /// World-space UI that lists WorldLabs worlds as clickable panorama cards.
    /// Tap a card to load the world; tap again to unload.
    /// Auto-builds a World Space Canvas hierarchy on Awake if nothing is pre-wired.
    /// </summary>
    [RequireComponent(typeof(WorldLabsWorldManager))]
    public class WorldBrowserController : MonoBehaviour
    {
        // ── Inspector ─────────────────────────────────────────────────────────

        [Header("Manager (auto-resolved if null)")]
        public WorldLabsWorldManager worldManager;

        [Header("UI — leave null to auto-create")]
        public Canvas      browserCanvas;
        public ScrollRect  worldScrollRect;
        public RectTransform worldListContent;
        public Button      prevButton;
        public Button      nextButton;
        public Text        pageLabel;
        public Text    statusText;
        [Tooltip("Optional external button that unloads the current world and restores the default asset.")]
        public Button      unloadButton;

        [Header("World Creation")]
        [Tooltip("Model to use when generating new worlds from a text prompt.")]
        public MarbleModel selectedModel = MarbleModel.Standard;

        [Header("Create UI — leave null to auto-create")]
        [Tooltip("Root panel shown when in create mode (auto-created if null).")]
        public GameObject createPanel;
        [Tooltip("InputField for the generation prompt (auto-created if null).")]
        public InputField  promptInputField;
        [Tooltip("Button that triggers world generation (auto-created if null).")]
        public Button      createWorldButton;
        [Tooltip("Header button that toggles between browse and create modes (auto-created if null).")]
        public Button      createToggleButton;

        [Header("Delete UI — leave null to auto-create")]
        [Tooltip("Overlay panel shown when the user taps a card's delete button (auto-created if null).")]
        public GameObject confirmDeletePanel;
        [Tooltip("Label showing the world name inside the confirm panel (auto-created if null).")]
        public Text       confirmDeleteLabel;
        [Tooltip("Button that executes the permanent deletion (auto-created if null).")]
        public Button     confirmDeleteButton;
        [Tooltip("Button that dismisses the confirm panel without deleting (auto-created if null).")]
        public Button     cancelDeleteButton;

        [Header("Model Selector — leave null to auto-create")]
        [Tooltip("Row of four model-selection buttons (auto-created if null).")]
        public GameObject modelSelectorRow;
        [Tooltip("Label showing active model in the header (auto-created if null).")]
        public Text       currentModelLabel;

        [Header("Layout")]
        [Tooltip("Canvas size in pixels (world units = pixels × 0.005).")]
        public Vector2 canvasPixelSize = new Vector2(420, 600);
        [Tooltip("Columns in the world card grid.")]
        public int columns = 2;
        [Tooltip("Total card height in pixels (image + name bar).")]
        public float cardHeight = 182f;
        [Tooltip("Height of the panorama image portion of each card.")]
        public float cardImageHeight = 148f;

        [Header("Save System")]
        [Tooltip("Assign a WorldConfigStore here. Serialized as MonoBehaviour to avoid a package→Assembly-CSharp dependency.")]
        public MonoBehaviour worldBookmarkProvider;

        [Header("Panorama Sphere")]
        [Tooltip("Fired with the downloaded Texture2D when panorama is ready. Wire to ThumbnailSkyboxController.Show.")]
        [SerializeField] private UnityEvent<Texture2D> onPanoramaTextureReady;

        [Tooltip("Fired when the splat renderer is ready. Wire to ThumbnailSkyboxController.StartFadeOut.")]
        [SerializeField] private UnityEvent onSplatReady;

        [Tooltip("Fired when the panorama texture download fails (network error, missing URL, etc).")]
        [SerializeField] private UnityEvent onPanoramaDownloadFailed;

        [Tooltip("When enabled, the SPZ is never downloaded or rendered. Only the panorama sphere is shown.")]
        [SerializeField] private bool panoramaOnly = false;

        // ── C# events (code-only subscribers) ────────────────────────────────

        /// <summary>
        /// Fired with the raw panoramic image bytes immediately after download.
        /// Subscribe to cache the file to disk.
        /// </summary>
        public event Action<string, byte[]> OnPanoBytesDownloaded;

        /// <summary>
        /// Fired when a panorama-only world finishes displaying (no splat will follow).
        /// Used by the save system to trigger caching when OnWorldLoaded will never fire.
        /// </summary>
        public event Action<string> OnPanoWorldShown;
        public event Action<MarbleModel> OnGenerationModelChanged;

        // ── State ─────────────────────────────────────────────────────────────

        static readonly Color ModelActiveColor   = new Color(0.25f, 0.55f, 1.00f, 1f);
        static readonly Color ModelInactiveColor = new Color(0.18f, 0.20f, 0.28f, 1f);

        readonly List<WorldCardUI> _pool    = new();
        readonly Stack<string>     _history = new();   // page-token history for Back
        readonly Button[] _modelButtons = new Button[4];

        string _currentToken;
        string _nextToken;
        bool   _loading;

        [Header("Startup")]
        [Tooltip("If false, the WorldLabs list is loaded only when Refresh is called after the panel is explicitly shown.")]
        public bool refreshOnStart = false;
        [Tooltip("Frames to wait before the optional startup refresh.")]
        [Min(0)] public int startupRefreshDelayFrames = 20;

        // ── Create-world state ────────────────────────────────────────────────
        WorldLabsClient _wlClient;
        bool            _createPanelOpen;
        bool            _isGenerating;
        bool            _generationCancelled;

        // ── Delete state ──────────────────────────────────────────────────────
        World _pendingDeleteWorld;

        // ── Lifecycle ─────────────────────────────────────────────────────────

        void Awake()
        {
            if (worldManager == null)
                worldManager = GetComponent<WorldLabsWorldManager>();

            if (browserCanvas == null)
                BuildUI();
            else
                WirePrebuiltModelButtons();

            prevButton.onClick.AddListener(OnPrevPage);
            nextButton.onClick.AddListener(OnNextPage);

            if (unloadButton != null)
                unloadButton.onClick.AddListener(OnUnloadCurrentWorld);

            if (createToggleButton != null)
                createToggleButton.onClick.AddListener(ToggleCreateMode);
            if (createWorldButton != null)
                createWorldButton.onClick.AddListener(StartWorldCreation);

            if (confirmDeleteButton != null)
                confirmDeleteButton.onClick.AddListener(OnDeleteConfirmed);
            if (cancelDeleteButton != null)
                cancelDeleteButton.onClick.AddListener(OnDeleteCancelled);
        }

        void Start()
        {
            // If the user pre-wired a plain ScrollRect, swap it for ScrollbarOnlyScrollRect
            // so that dragging the content area no longer scrolls (only the scrollbar does).
            if (worldScrollRect != null && worldScrollRect.GetType() == typeof(ScrollRect))
                worldScrollRect = SwapToScrollbarOnly(worldScrollRect);

            EnsureContentLayout();
            _started = true;
            if (refreshOnStart)
                StartCoroutine(RefreshAfterStartupDelay());
        }

        System.Collections.IEnumerator RefreshAfterStartupDelay()
        {
            int frames = Mathf.Max(0, startupRefreshDelayFrames);
            for (int i = 0; i < frames; i++)
                yield return null;

            Refresh();
        }

        /// <summary>
        /// Replaces a plain ScrollRect with ScrollbarOnlyScrollRect, preserving all bindings.
        /// </summary>
        static ScrollbarOnlyScrollRect SwapToScrollbarOnly(ScrollRect old)
        {
            // Snapshot everything we need before destroying
            var go            = old.gameObject;
            var horizontal    = old.horizontal;
            var vertical      = old.vertical;
            var content       = old.content;
            var viewport      = old.viewport;
            var hBar          = old.horizontalScrollbar;
            var vBar          = old.verticalScrollbar;
            var hVis          = old.horizontalScrollbarVisibility;
            var vVis          = old.verticalScrollbarVisibility;
            var hSpacing      = old.horizontalScrollbarSpacing;
            var vSpacing      = old.verticalScrollbarSpacing;
            var movementType  = old.movementType;
            var elasticity    = old.elasticity;
            var inertia       = old.inertia;
            var decel         = old.decelerationRate;
            var sensitivity   = old.scrollSensitivity;

            UnityEngine.Object.DestroyImmediate(old);

            var neo = go.AddComponent<ScrollbarOnlyScrollRect>();
            neo.horizontal                  = horizontal;
            neo.vertical                    = vertical;
            neo.content                     = content;
            neo.viewport                    = viewport;
            neo.horizontalScrollbar         = hBar;
            neo.verticalScrollbar           = vBar;
            neo.horizontalScrollbarVisibility = hVis;
            neo.verticalScrollbarVisibility = vVis;
            neo.horizontalScrollbarSpacing  = hSpacing;
            neo.verticalScrollbarSpacing    = vSpacing;
            neo.movementType                = movementType;
            neo.elasticity                  = elasticity;
            neo.inertia                     = inertia;
            neo.decelerationRate            = decel;
            neo.scrollSensitivity           = sensitivity;
            return neo;
        }

        /// <summary>
        /// Adds GridLayoutGroup + ContentSizeFitter to worldListContent if they are missing.
        /// Called from Start() so RectTransform sizes are already calculated by Unity.
        /// Safe to call multiple times — skips if a layout group already exists.
        /// </summary>
        void EnsureContentLayout()
        {
            if (worldListContent == null) return;
            if (worldListContent.GetComponent<LayoutGroup>() != null) return;

            // Try to read the actual viewport width for accurate cell sizing.
            Canvas.ForceUpdateCanvases();
            float width = canvasPixelSize.x;
            if (worldScrollRect?.viewport != null && worldScrollRect.viewport.rect.width > 1f)
                width = worldScrollRect.viewport.rect.width;

            const float pad = 8f, gap = 6f;
            float cellW = Mathf.Max(50f, (width - pad * 2 - gap * (columns - 1)) / columns);

            // Content must be anchored to the top and grow downward.
            worldListContent.anchorMin = new Vector2(0f, 1f);
            worldListContent.anchorMax = new Vector2(1f, 1f);
            worldListContent.pivot     = new Vector2(0.5f, 1f);
            worldListContent.offsetMin = worldListContent.offsetMax = Vector2.zero;

            var glg = worldListContent.gameObject.AddComponent<GridLayoutGroup>();
            glg.constraint      = GridLayoutGroup.Constraint.FixedColumnCount;
            glg.constraintCount = columns;
            glg.cellSize        = new Vector2(cellW, cardHeight);
            glg.spacing         = new Vector2(gap, gap);
            glg.padding         = new RectOffset((int)pad, (int)pad, (int)pad, (int)pad);
            glg.startCorner     = GridLayoutGroup.Corner.UpperLeft;
            glg.startAxis       = GridLayoutGroup.Axis.Horizontal;
            glg.childAlignment  = TextAnchor.UpperLeft;

            if (worldListContent.GetComponent<ContentSizeFitter>() == null)
                worldListContent.gameObject.AddComponent<ContentSizeFitter>().verticalFit =
                    ContentSizeFitter.FitMode.PreferredSize;

            Debug.Log($"[WorldBrowserController] EnsureContentLayout: cellW={cellW:F0}, " +
                      $"cardH={cardHeight}, cols={columns}");
        }

        bool _started;

        void OnEnable()
        {
            worldManager.OnWorldLoaded       += OnWorldLoaded;
            worldManager.OnWorldLoadFailed   += OnWorldLoadFailed;
            worldManager.OnWorldLoadProgress += OnWorldProgress;
            worldManager.OnWorldUnloaded     += OnWorldUnloadedHandler;
            // Don't Refresh() yet on the very first enable — Start() handles it after
            // EnsureContentLayout() so cards always have a GridLayoutGroup to land in.
            if (_started) Refresh();
        }

        void OnDisable()
        {
            worldManager.OnWorldLoaded       -= OnWorldLoaded;
            worldManager.OnWorldLoadFailed   -= OnWorldLoadFailed;
            worldManager.OnWorldLoadProgress -= OnWorldProgress;
            worldManager.OnWorldUnloaded     -= OnWorldUnloadedHandler;
            _generationCancelled = true;   // abort any in-progress polling
        }

        // ── Public ────────────────────────────────────────────────────────────

        /// <summary>
        /// Unloads the current world and restores the default asset placeholder.
        /// Wired to <see cref="unloadButton"/> automatically; can also be called from code.
        /// </summary>
        public void OnUnloadCurrentWorld()
        {
            worldManager.RestoreDefaultWorld();
            RefreshAllCards();
            SetStatus("World unloaded");
        }

        public async void Refresh()
        {
            if (_loading) return;
            _loading = true;
            _history.Clear();
            _currentToken = null;
            SetStatus("Loading worlds…");
            prevButton.interactable = false;
            nextButton.interactable = false;

            try
            {
                var worlds = await worldManager.ListWorldsAsync(pageToken: null, pageSize: 20);
                _nextToken = worldManager.LastNextPageToken;
                PopulateGrid(worlds);
                SetStatus($"{worlds.Count} worlds");
            }
            catch (Exception ex) { SetStatus($"Error: {ex.Message}"); }
            finally { _loading = false; UpdatePagination(); }
        }

        // ── Pagination ────────────────────────────────────────────────────────

        async void OnNextPage()
        {
            if (_loading || string.IsNullOrEmpty(_nextToken)) return;
            _loading = true;
            prevButton.interactable = false;
            nextButton.interactable = false;
            SetStatus("Loading…");

            _history.Push(_currentToken);
            _currentToken = _nextToken;
            _nextToken = null;

            try
            {
                var worlds = await worldManager.ListWorldsAsync(pageToken: _currentToken, pageSize: 20);
                _nextToken = worldManager.LastNextPageToken;
                PopulateGrid(worlds);
                SetStatus($"{worlds.Count} worlds");
            }
            catch (Exception ex) { SetStatus($"Error: {ex.Message}"); }
            finally { _loading = false; UpdatePagination(); }
        }

        async void OnPrevPage()
        {
            if (_loading || _history.Count == 0) return;
            _loading = true;
            prevButton.interactable = false;
            nextButton.interactable = false;
            SetStatus("Loading…");

            _currentToken = _history.Pop();
            _nextToken = null;

            try
            {
                var worlds = await worldManager.ListWorldsAsync(pageToken: _currentToken, pageSize: 20);
                _nextToken = worldManager.LastNextPageToken;
                PopulateGrid(worlds);
                SetStatus($"{worlds.Count} worlds");
            }
            catch (Exception ex) { SetStatus($"Error: {ex.Message}"); }
            finally { _loading = false; UpdatePagination(); }
        }

        // ── Grid ──────────────────────────────────────────────────────────────

        void PopulateGrid(IList<World> worlds)
        {
            Debug.Log($"[WorldBrowserController] PopulateGrid: {worlds.Count} worlds, " +
                      $"content={worldListContent != null}");

            foreach (var c in _pool) c.gameObject.SetActive(false);

            for (int i = 0; i < worlds.Count; i++)
            {
                WorldCardUI card;
                if (i < _pool.Count)
                {
                    card = _pool[i];
                    card.gameObject.SetActive(true);
                }
                else
                {
                    card = WorldCardUI.Create(worldListContent, cardImageHeight);
                    _pool.Add(card);
                }
                card.Bind(
                    worlds[i],
                    worldManager,
                    tex => onPanoramaTextureReady?.Invoke(tex),
                    ()  => onSplatReady?.Invoke(),
                    ()  => onPanoramaDownloadFailed?.Invoke(),
                    panoramaOnly,
                    world => LastClickedWorld = world,
                    (id, bytes) => OnPanoBytesDownloaded?.Invoke(id, bytes),
                    id => OnPanoWorldShown?.Invoke(id));

                // Bookmark indicator — show if this world has a saved config
                if (worldBookmarkProvider != null && card != null)
                {
                    var provider = worldBookmarkProvider as IWorldBookmarkProvider;
                    bool hasSaved = provider?.HasConfigForWorldId(worlds[i].world_id) ?? false;
                    card.SetBookmarkVisible(hasSaved);
                }
            }

            Debug.Log($"[WorldBrowserController] Grid populated: {_pool.Count} cards created");

            // Force layout rebuild so ContentSizeFitter sets content height immediately.
            LayoutRebuilder.ForceRebuildLayoutImmediate(worldListContent);

            if (worldScrollRect != null)
                worldScrollRect.normalizedPosition = new Vector2(0, 1);
        }

        void UpdatePagination()
        {
            prevButton.interactable = _history.Count > 0;
            nextButton.interactable = !string.IsNullOrEmpty(_nextToken);
            if (pageLabel != null)
                pageLabel.text = $"Page {_history.Count + 1}";
        }

        // ── Manager events ────────────────────────────────────────────────────

        void OnWorldLoaded(string id, GaussianSplatting.Runtime.GaussianSplatRenderer _)
        {
            RefreshCard(id);
            SetStatus("World loaded");
        }

        void OnWorldLoadFailed(string id, string error)
        {
            RefreshCard(id);
            SetStatus($"Failed: {error}");
        }

        void OnWorldProgress(string id, float progress)
        {
            SetStatus($"Loading {progress * 100:0}%…");
            foreach (var c in _pool)
                if (c.isActiveAndEnabled && c.WorldId == id)
                { c.SetProgress(progress); return; }
        }

        void OnWorldUnloadedHandler(string id)
        {
            // Refresh every visible card so "loaded" highlight resets correctly.
            RefreshAllCards();
        }

        void RefreshCard(string id)
        {
            foreach (var c in _pool)
                if (c.isActiveAndEnabled && c.WorldId == id)
                { c.RefreshState(); return; }
        }

        void RefreshAllCards()
        {
            foreach (var c in _pool)
                if (c.isActiveAndEnabled) c.RefreshState();
        }

        void SetStatus(string msg)
        {
            if (statusText != null) statusText.text = msg;
        }

        bool CanCallWorldLabsApi(string operation)
        {
            _wlClient ??= new WorldLabsClient();
            if (_wlClient.IsConfigured)
                return true;

            const string message = "WorldLabs API key missing. Set WORLDLABS_API_KEY in the project-root .env file.";
            SetStatus(message);
            Debug.LogWarning($"[WorldBrowserController] {operation} skipped: {message}", this);
            return false;
        }

        // ── World creation ────────────────────────────────────────────────────

        /// <summary>
        /// Sets panorama-only mode and pushes the new value to all currently-pooled cards
        /// so that the next click on any card respects the updated setting.
        /// </summary>
        public World LastClickedWorld { get; private set; }

        public void SetPanoramaOnly(bool value)
        {
            panoramaOnly = value;
            foreach (WorldCardUI card in _pool)
                card.SetPanoramaOnly(value);
        }

        /// <summary>
        /// Sets the active generation model, refreshes model button highlights,
        /// and updates the header label. Called by UI taps and voice commands.
        /// </summary>
        public void SetGenerationModel(MarbleModel m)
        {
            bool changed = selectedModel != m;
            selectedModel = m;
            for (int i = 0; i < _modelButtons.Length; i++)
            {
                if (_modelButtons[i] == null) continue;
                var img = _modelButtons[i].GetComponent<Image>();
                if (img != null) img.color = i == (int)m ? ModelActiveColor : ModelInactiveColor;
            }
            if (currentModelLabel != null)
            {
                string label = m switch
                {
                    MarbleModel.Draft    => "Draft",
                    MarbleModel.Fast     => "Fast",
                    MarbleModel.Standard => "Standard",
                    MarbleModel.High     => "High",
                    _                    => m.ToString()
                };
                currentModelLabel.text = $"Model: {label}";
            }

            SetStatus($"Generation model: {GetGenerationModelLabel(m)}");
            if (changed)
                OnGenerationModelChanged?.Invoke(m);
        }

        public MarbleModel SelectedGenerationModel => selectedModel;

        public static string GetGenerationModelLabel(MarbleModel model) => model switch
        {
            MarbleModel.Draft    => "Draft",
            MarbleModel.Fast     => "Fast",
            MarbleModel.Standard => "Standard",
            MarbleModel.High     => "High",
            _                    => model.ToString()
        };

        /// Toggles between the browse list and the create-world panel.
        /// Safe to call from a UI Button onClick event.
        /// </summary>
        public void ToggleCreateMode()
        {
            _createPanelOpen = !_createPanelOpen;

            if (createPanel != null)
                createPanel.SetActive(_createPanelOpen);

            if (worldScrollRect != null)
                worldScrollRect.gameObject.SetActive(!_createPanelOpen);

            // Update the toggle button label
            if (createToggleButton != null)
            {
                var txt = createToggleButton.GetComponentInChildren<Text>();
                if (txt != null)
                    txt.text = _createPanelOpen ? "✕ Browse" : "➕ Create";
            }

            if (_createPanelOpen && !_isGenerating)
                SetStatus("Enter a prompt and press Generate.");
            else if (!_createPanelOpen && !_isGenerating)
                SetStatus(string.Empty);
        }

        /// <summary>
        /// Starts world generation from the text in <see cref="promptInputField"/>.
        /// Loads the default asset as a placeholder while the API generates the world,
        /// then loads the finished world automatically as the active world.
        /// </summary>
        async void StartWorldCreation()
        {
            if (_isGenerating) return;

            string prompt = promptInputField != null ? (promptInputField.text ?? string.Empty).Trim() : string.Empty;
            if (string.IsNullOrEmpty(prompt))
            {
                SetStatus("Please enter a prompt.");
                return;
            }

            if (!CanCallWorldLabsApi("World creation"))
                return;

            _isGenerating        = true;
            _generationCancelled = false;
            if (createWorldButton != null) createWorldButton.interactable = false;

            // Show the default asset as a placeholder while generation is in progress
            worldManager.RestoreDefaultWorld();
            SetStatus("Starting generation…");

            string modelStr = selectedModel switch
            {
                MarbleModel.Draft    => "marble-1.0-draft",
                MarbleModel.Fast     => "marble-1.0",
                MarbleModel.High     => "marble-1.1-plus",
                _                    => "marble-1.1",
            };

            try
            {
                var request = new WorldsGenerateRequest
                {
                    world_prompt = TextPrompt.Create(prompt),
                    model        = modelStr,
                    permission   = Permission.Private
                };

                GenerateWorldResponse genResponse = await _wlClient.GenerateWorldAsync(request);
                string opId = genResponse.operation_id;

                // Poll until the operation is done AND world assets are ready
                const float pollInterval = 5f;
                const float timeout      = 600f;
                float       elapsed      = 0f;
                World       readyWorld   = null;

                while (elapsed < timeout && !_generationCancelled)
                {
                    await Task.Delay(TimeSpan.FromSeconds(pollInterval));
                    elapsed += pollInterval;

                    if (_generationCancelled) break;

                    GetOperationResponse op = await _wlClient.GetOperationAsync(opId);

                    if (op.error != null && !string.IsNullOrEmpty(op.error.message))
                        throw new Exception(op.error.message);

                    if (op.done && IsWorldReady(op.response))
                    {
                        readyWorld = op.response;
                        break;
                    }

                    SetStatus(op.done
                        ? "Finalizing assets…"
                        : $"Generating {(op.metadata?.progress ?? 0f) * 100:0}%…");
                }

                if (_generationCancelled)
                {
                    SetStatus("Generation cancelled.");
                    return;
                }

                if (readyWorld == null)
                {
                    SetStatus("Timed out waiting for world assets.");
                    return;
                }

                // Return to browse mode before loading the world
                if (_createPanelOpen) ToggleCreateMode();
                SetStatus("Loading world…");

                await worldManager.LoadWorldAsync(readyWorld);
                SetStatus("World loaded!");

                // Refresh the browse list so the new world appears in the grid
                Refresh();
            }
            catch (Exception ex)
            {
                SetStatus($"Error: {ex.Message}");
                Debug.LogError($"[WorldBrowserController] World creation failed: {ex}");
            }
            finally
            {
                _isGenerating = false;
                if (createWorldButton != null) createWorldButton.interactable = true;
            }
        }

        // ── Delete ────────────────────────────────────────────────────────────

        /// <summary>
        /// Shows the confirm-delete panel for the given world.
        /// Called from each card's delete button via <see cref="WorldCardUI"/>.
        /// </summary>
        public void RequestDeleteWorld(World world)
        {
            if (world == null) return;
            _pendingDeleteWorld = world;

            if (confirmDeleteLabel != null)
            {
                string name = string.IsNullOrEmpty(world.display_name)
                    ? world.world_id : world.display_name;
                confirmDeleteLabel.text = $"\"{name}\"\n\nThis cannot be undone.";
            }

            if (confirmDeletePanel != null)
                confirmDeletePanel.SetActive(true);
        }

        /// <summary>
        /// Executes the deletion after the user confirmed. Wired to the confirm button.
        /// </summary>
        public async void OnDeleteConfirmed()
        {
            if (_pendingDeleteWorld == null) return;

            if (confirmDeletePanel != null)
                confirmDeletePanel.SetActive(false);

            string id   = _pendingDeleteWorld.world_id;
            string name = string.IsNullOrEmpty(_pendingDeleteWorld.display_name)
                ? id : _pendingDeleteWorld.display_name;

            if (!CanCallWorldLabsApi("Delete world"))
                return;

            _pendingDeleteWorld = null;

            // Unload first if the world is currently active
            if (worldManager.IsWorldLoaded(id))
                worldManager.UnloadWorld(id);

            SetStatus($"Deleting '{name}'…");

            try
            {
                var response = await _wlClient.DeleteWorldAsync(id);
                if (response.deleted)
                {
                    SetStatus($"'{name}' deleted.");
                    // Remove the card immediately — the list endpoint is eventually consistent
                    // and may still return the deleted world for a few seconds after DELETE succeeds.
                    RemoveCardById(id);
                    // Reconcile with the server in the background after a short delay.
                    _ = RefreshAfterDelay(2f);
                }
                else
                {
                    SetStatus("Delete returned an unexpected response.");
                }
            }
            catch (WorldLabsException ex)
            {
                SetStatus($"Delete failed ({ex.StatusCode}): {ex.Message}");
                Debug.LogError($"[WorldBrowserController] Delete world '{id}' failed: {ex}");
            }
        }

        /// <summary>
        /// Dismisses the confirm-delete panel without deleting. Wired to the cancel button.
        /// </summary>
        public void OnDeleteCancelled()
        {
            _pendingDeleteWorld = null;
            if (confirmDeletePanel != null)
                confirmDeletePanel.SetActive(false);
        }

        /// <summary>
        /// Immediately hides the card for <paramref name="worldId"/> without waiting for
        /// a server round-trip. Used after a successful delete to avoid showing a stale entry
        /// while the list endpoint catches up.
        /// </summary>
        void RemoveCardById(string worldId)
        {
            foreach (var card in _pool)
            {
                if (card.isActiveAndEnabled && card.WorldId == worldId)
                {
                    card.gameObject.SetActive(false);
                    return;
                }
            }
        }

        /// <summary>
        /// Waits <paramref name="seconds"/> then calls <see cref="Refresh"/> to reconcile
        /// the displayed list with the server after an eventually-consistent write.
        /// </summary>
        async System.Threading.Tasks.Task RefreshAfterDelay(float seconds)
        {
            await System.Threading.Tasks.Task.Delay(System.TimeSpan.FromSeconds(seconds));
            if (this != null) Refresh();
        }

        /// <summary>Returns true when the world has at least panorama imagery or splat assets.</summary>
        static bool IsWorldReady(World world)
        {
            if (world == null) return false;
            bool hasImagery = !string.IsNullOrEmpty(world.assets?.imagery?.pano_url);
            bool hasSplats  = world.assets?.splats?.spz_urls?.Count > 0;
            return hasImagery || hasSplats;
        }

        // ── Prefab button wiring ──────────────────────────────────────────────

        /// <summary>
        /// When the prefab ships with a pre-built ModelSelectorRow, BuildUI() is
        /// skipped. This method walks the row's children, wires onClick listeners,
        /// and populates _modelButtons[] so SetGenerationModel() can update highlights.
        /// </summary>
        void WirePrebuiltModelButtons()
        {
            if (modelSelectorRow == null) return;

            int count = Mathf.Min(modelSelectorRow.transform.childCount, _modelButtons.Length);
            for (int i = 0; i < count; i++)
            {
                int captured = i;
                Button btn = modelSelectorRow.transform.GetChild(i).GetComponent<Button>();
                if (btn == null) continue;
                _modelButtons[i] = btn;
                btn.onClick.AddListener(() => SetGenerationModel((MarbleModel)captured));
            }

            SetGenerationModel(selectedModel);
        }

        // ── UI auto-build ─────────────────────────────────────────────────────

        void BuildUI()
        {
            const float headerH = 40f;
            const float footerH = 46f;
            const float statusH = 20f;

            // ── World Space Canvas ────────────────────────────────────────────
            var canvasGo = new GameObject("WorldBrowserCanvas");
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvasGo.AddComponent<GraphicRaycaster>();
            browserCanvas = canvas;

            var canvasRt = canvasGo.GetComponent<RectTransform>();
            canvasRt.sizeDelta     = canvasPixelSize;
            canvasRt.localScale    = new Vector3(0.005f, 0.005f, 0.005f);
            canvasRt.localPosition = Vector3.zero;
            canvasRt.localRotation = Quaternion.identity;

            // ── Root panel ────────────────────────────────────────────────────
            var panelGo = Div("Panel", canvasGo.transform);
            Stretch(panelGo, Vector2.zero, Vector2.one);
            panelGo.AddComponent<Image>().color = new Color(0.10f, 0.10f, 0.14f, 0.97f);
            Transform panel = panelGo.transform;

            // ── Header ────────────────────────────────────────────────────────
            var headerGo = Div("Header", panel);
            AnchorEdge(headerGo, top: true, h: headerH);
            headerGo.AddComponent<Image>().color = new Color(0.11f, 0.11f, 0.17f, 1f);

            MakeLabel(headerGo.transform, "Title", "WorldLabs Worlds", 16,
                TextAnchor.MiddleLeft,
                new Vector2(0, 0), new Vector2(0.50f, 1),
                new Vector2(12, 0), new Vector2(0, 0));

            createToggleButton = MakeButton(headerGo.transform, "➕ Create",
                new Vector2(0.50f, 0), new Vector2(0.78f, 1),
                new Vector2(2, 4), new Vector2(-2, -4));

            pageLabel = MakeLabel(headerGo.transform, "PageLabel", "Page 1", 11,
                TextAnchor.MiddleRight,
                new Vector2(0.72f, 0), new Vector2(0.87f, 1),
                new Vector2(0, 0), new Vector2(0, 0));
            pageLabel.color = new Color(0.65f, 0.65f, 0.65f, 1f);

            currentModelLabel = MakeLabel(headerGo.transform, "ModelLabel", "Model: Standard", 11,
                TextAnchor.MiddleRight,
                new Vector2(0.87f, 0), new Vector2(1f, 1),
                new Vector2(0, 0), new Vector2(-6, 0));
            currentModelLabel.color = new Color(0.45f, 0.70f, 1.00f, 1f);
            currentModelLabel.raycastTarget = false;

            // ── Footer ────────────────────────────────────────────────────────
            var footerGo = Div("Footer", panel);
            AnchorEdge(footerGo, top: false, h: footerH);
            footerGo.AddComponent<Image>().color = new Color(0.09f, 0.09f, 0.14f, 1f);

            prevButton = MakeButton(footerGo.transform, "◀ Prev",
                new Vector2(0, 0), new Vector2(0.35f, 1), new Vector2(8, 6), new Vector2(-4, -6));
            nextButton = MakeButton(footerGo.transform, "Next ▶",
                new Vector2(0.65f, 0), new Vector2(1, 1), new Vector2(4, 6), new Vector2(-8, -6));

            // ── Status bar ────────────────────────────────────────────────────
            var statusGo = new GameObject("Status", typeof(RectTransform), typeof(Text));
            statusGo.transform.SetParent(panel, false);
            var statusRt = statusGo.GetComponent<RectTransform>();
            statusRt.anchorMin = new Vector2(0, 0);
            statusRt.anchorMax = new Vector2(1, 0);
            statusRt.offsetMin = new Vector2(10, footerH);
            statusRt.offsetMax = new Vector2(-10, footerH + statusH);
            statusText = statusGo.GetComponent<Text>();
            statusText.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            statusText.fontSize  = 11;
            statusText.color     = new Color(0.55f, 0.55f, 0.55f, 1f);
            statusText.alignment = TextAnchor.MiddleLeft;
            statusText.raycastTarget = false;

            // ── Scroll view ───────────────────────────────────────────────────
            var scrollGo = Div("ScrollView", panel);
            var scrollRt = scrollGo.GetComponent<RectTransform>();
            scrollRt.anchorMin = new Vector2(0, 0);
            scrollRt.anchorMax = new Vector2(1, 1);
            scrollRt.offsetMin = new Vector2(0, footerH + statusH);
            scrollRt.offsetMax = new Vector2(0, -headerH);
            scrollGo.AddComponent<Image>().color = new Color(0.08f, 0.08f, 0.12f, 1f);

            var scroll = scrollGo.AddComponent<ScrollbarOnlyScrollRect>();
            scroll.horizontal = false;
            worldScrollRect = scroll;

            // Viewport — RectMask2D uses scissor-rect clipping (no stencil buffer).
            // Mask+Color.clear can mask out all children on Android/XR GPUs.
            var vpGo = Div("Viewport", scrollGo.transform);
            Stretch(vpGo, Vector2.zero, Vector2.one);
            vpGo.AddComponent<RectMask2D>();
            scroll.viewport = vpGo.GetComponent<RectTransform>();

            // Content — GridLayoutGroup drives layout, ContentSizeFitter drives height
            var contentGo = Div("Content", vpGo.transform);
            var contentRt = contentGo.GetComponent<RectTransform>();
            contentRt.anchorMin = new Vector2(0, 1);
            contentRt.anchorMax = new Vector2(1, 1);
            contentRt.pivot     = new Vector2(0.5f, 1);
            contentRt.offsetMin = contentRt.offsetMax = Vector2.zero;

            const float pad = 8f, gap = 6f;
            float cellW = (canvasPixelSize.x - pad * 2 - gap * (columns - 1)) / columns;

            var glg = contentGo.AddComponent<GridLayoutGroup>();
            glg.constraint      = GridLayoutGroup.Constraint.FixedColumnCount;
            glg.constraintCount = columns;
            glg.cellSize        = new Vector2(cellW, cardHeight);
            glg.spacing         = new Vector2(gap, gap);
            glg.padding         = new RectOffset((int)pad, (int)pad, (int)pad, (int)pad);
            glg.startCorner     = GridLayoutGroup.Corner.UpperLeft;
            glg.startAxis       = GridLayoutGroup.Axis.Horizontal;
            glg.childAlignment  = TextAnchor.UpperLeft;

            contentGo.AddComponent<ContentSizeFitter>().verticalFit =
                ContentSizeFitter.FitMode.PreferredSize;

            worldListContent = contentRt;
            scroll.content   = contentRt;

            // ── Create panel (overlays scroll view; hidden by default) ─────────
            var createGo = Div("CreatePanel", panel);
            var createRt = createGo.GetComponent<RectTransform>();
            createRt.anchorMin = new Vector2(0, 0);
            createRt.anchorMax = new Vector2(1, 1);
            createRt.offsetMin = new Vector2(0, footerH + statusH);
            createRt.offsetMax = new Vector2(0, -headerH);
            createGo.AddComponent<Image>().color = new Color(0.10f, 0.10f, 0.14f, 0.97f);
            createPanel = createGo;
            createGo.SetActive(false);

            MakeLabel(createGo.transform, "CreateTitle", "Create New World", 16,
                TextAnchor.MiddleCenter,
                new Vector2(0, 0.85f), new Vector2(1, 1f),
                new Vector2(10, 0), new Vector2(-10, 0));

            // ── Model selector row ────────────────────────────────────────────────
            var selectorGo = Div("ModelSelectorRow", createGo.transform);
            var selectorRt = selectorGo.GetComponent<RectTransform>();
            selectorRt.anchorMin = new Vector2(0f, 0.84f);
            selectorRt.anchorMax = new Vector2(1f, 0.96f);
            selectorRt.offsetMin = new Vector2(14f, 0f);
            selectorRt.offsetMax = new Vector2(-14f, 0f);
            var hlg = selectorGo.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing                = 4f;
            hlg.childForceExpandWidth  = true;
            hlg.childForceExpandHeight = true;
            modelSelectorRow = selectorGo;

            string[] modelLabels = { "Draft", "Fast", "Standard", "High" };
            for (int i = 0; i < modelLabels.Length; i++)
            {
                int captured = i;
                var btnGo = new GameObject(modelLabels[i], typeof(RectTransform));
                btnGo.transform.SetParent(selectorGo.transform, false);
                btnGo.AddComponent<LayoutElement>();

                var img = btnGo.AddComponent<Image>();
                img.color = ModelInactiveColor; // SetGenerationModel below applies the active highlight

                var btn = btnGo.AddComponent<Button>();
                btn.targetGraphic = img;
                btn.transition    = Selectable.Transition.ColorTint;
                var colors = btn.colors;
                colors.highlightedColor = new Color(0.40f, 0.65f, 1.00f, 1f);
                colors.pressedColor     = new Color(0.15f, 0.38f, 0.80f, 1f);
                btn.colors    = colors;
                btn.navigation = new Navigation { mode = Navigation.Mode.None };
                btn.onClick.AddListener(() => SetGenerationModel((MarbleModel)captured));
                _modelButtons[i] = btn;

                var textGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
                textGo.transform.SetParent(btnGo.transform, false);
                var trt = textGo.GetComponent<RectTransform>();
                trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
                trt.offsetMin = trt.offsetMax = Vector2.zero;
                var txt = textGo.GetComponent<Text>();
                txt.font        = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                txt.fontSize    = 11;
                txt.color       = Color.white;
                txt.alignment   = TextAnchor.MiddleCenter;
                txt.text        = modelLabels[i];
                txt.raycastTarget = false;
            }
            SetGenerationModel(selectedModel);

            MakeLabel(createGo.transform, "PromptLabel", "Describe your world:", 13,
                TextAnchor.MiddleLeft,
                new Vector2(0, 0.58f), new Vector2(1, 0.70f),
                new Vector2(16, 0), new Vector2(-16, 0));

            promptInputField = MakeInputField(createGo.transform, "PromptInput",
                new Vector2(0, 0.26f), new Vector2(1, 0.58f),
                new Vector2(14, 0), new Vector2(-14, 0));

            createWorldButton = MakeButton(createGo.transform, "Generate World",
                new Vector2(0.08f, 0.10f), new Vector2(0.92f, 0.24f),
                new Vector2(0, 0), new Vector2(0, 0));

            // ── Confirm-delete panel (overlays scroll view; hidden by default) ──
            var confirmGo = Div("ConfirmDeletePanel", panel);
            var confirmRt = confirmGo.GetComponent<RectTransform>();
            confirmRt.anchorMin = new Vector2(0, 0);
            confirmRt.anchorMax = new Vector2(1, 1);
            confirmRt.offsetMin = new Vector2(0, footerH + statusH);
            confirmRt.offsetMax = new Vector2(0, -headerH);
            confirmGo.AddComponent<Image>().color = new Color(0.10f, 0.06f, 0.06f, 0.97f);
            confirmDeletePanel = confirmGo;
            confirmGo.SetActive(false);

            MakeLabel(confirmGo.transform, "ConfirmTitle", "Delete World?", 16,
                TextAnchor.MiddleCenter,
                new Vector2(0, 0.78f), new Vector2(1, 0.95f),
                new Vector2(10, 0), new Vector2(-10, 0));

            confirmDeleteLabel = MakeLabel(confirmGo.transform, "ConfirmBody", "", 13,
                TextAnchor.MiddleCenter,
                new Vector2(0, 0.48f), new Vector2(1, 0.78f),
                new Vector2(16, 0), new Vector2(-16, 0));
            confirmDeleteLabel.color = new Color(0.90f, 0.75f, 0.75f, 1f);

            confirmDeleteButton = MakeButtonColored(confirmGo.transform, "✕  Delete Forever",
                new Color(0.65f, 0.12f, 0.12f, 1f),
                new Vector2(0.08f, 0.28f), new Vector2(0.92f, 0.44f),
                Vector2.zero, Vector2.zero);

            cancelDeleteButton = MakeButton(confirmGo.transform, "← Keep It",
                new Vector2(0.20f, 0.10f), new Vector2(0.80f, 0.24f),
                Vector2.zero, Vector2.zero);

            confirmDeleteButton.onClick.AddListener(OnDeleteConfirmed);
            cancelDeleteButton.onClick.AddListener(OnDeleteCancelled);
        }

        // ── Small helpers ─────────────────────────────────────────────────────

        static GameObject Div(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        static void Stretch(GameObject go, Vector2 anchorMin, Vector2 anchorMax)
        {
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        /// <summary>Anchors a strip to the top or bottom edge of its parent.</summary>
        static void AnchorEdge(GameObject go, bool top, float h)
        {
            var rt = go.GetComponent<RectTransform>();
            if (top)
            {
                rt.anchorMin = new Vector2(0, 1); rt.anchorMax = new Vector2(1, 1);
                rt.offsetMin = new Vector2(0, -h); rt.offsetMax = Vector2.zero;
            }
            else
            {
                rt.anchorMin = new Vector2(0, 0); rt.anchorMax = new Vector2(1, 0);
                rt.offsetMin = Vector2.zero;       rt.offsetMax = new Vector2(0, h);
            }
        }

        static Text MakeLabel(Transform parent, string name, string text, int size,
            TextAnchor align,
            Vector2 anchorMin, Vector2 anchorMax,
            Vector2 offsetMin, Vector2 offsetMax)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
            rt.offsetMin = offsetMin; rt.offsetMax = offsetMax;
            var t = go.GetComponent<Text>();
            t.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.fontSize  = size;
            t.color     = Color.white;
            t.alignment = align;
            t.text      = text;
            t.raycastTarget = false;
            return t;
        }

        static Button MakeButton(Transform parent, string label,
            Vector2 anchorMin, Vector2 anchorMax,
            Vector2 offsetMin, Vector2 offsetMax)
        {
            var go = new GameObject(label, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
            rt.offsetMin = offsetMin; rt.offsetMax = offsetMax;

            var img = go.AddComponent<Image>();
            img.color = new Color(0.20f, 0.40f, 0.70f, 1f);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;

            var colors = btn.colors;
            colors.highlightedColor = new Color(0.35f, 0.55f, 0.85f, 1f);
            colors.pressedColor     = new Color(0.15f, 0.30f, 0.55f, 1f);
            btn.colors = colors;

            var textGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
            textGo.transform.SetParent(go.transform, false);
            var trt = textGo.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = trt.offsetMax = Vector2.zero;
            var t = textGo.GetComponent<Text>();
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.fontSize = 13; t.color = Color.white;
            t.alignment = TextAnchor.MiddleCenter; t.text = label;
            t.raycastTarget = false;
            return btn;
        }

        static Button MakeButtonColored(Transform parent, string label, Color bgColor,
            Vector2 anchorMin, Vector2 anchorMax,
            Vector2 offsetMin, Vector2 offsetMax)
        {
            var btn = MakeButton(parent, label, anchorMin, anchorMax, offsetMin, offsetMax);
            btn.GetComponent<Image>().color = bgColor;
            var colors = btn.colors;
            colors.highlightedColor = new Color(
                Mathf.Min(bgColor.r + 0.15f, 1f),
                Mathf.Min(bgColor.g + 0.10f, 1f),
                Mathf.Min(bgColor.b + 0.10f, 1f), 1f);
            colors.pressedColor = new Color(
                Mathf.Max(bgColor.r - 0.15f, 0f),
                Mathf.Max(bgColor.g - 0.05f, 0f),
                Mathf.Max(bgColor.b - 0.05f, 0f), 1f);
            btn.colors = colors;
            return btn;
        }

        /// <summary>Creates a multi-line InputField sized by anchor rect.</summary>
        static InputField MakeInputField(Transform parent, string name,
            Vector2 anchorMin, Vector2 anchorMax,
            Vector2 offsetMin, Vector2 offsetMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
            rt.offsetMin = offsetMin; rt.offsetMax = offsetMax;

            var bg = go.AddComponent<Image>();
            bg.color = new Color(0.14f, 0.16f, 0.22f, 1f);

            var field = go.AddComponent<InputField>();
            field.lineType              = InputField.LineType.MultiLineNewline;
            // GameActivity on Android (Meta Quest) throws
            // "Hiding input field is not supported when using Game Activity"
            // when this is true, which then causes a keyboard-visibility timeout.
            // Setting false keeps the InputField visible while the system keyboard
            // is open — correct for a world-space VR canvas.
            field.shouldHideMobileInput = false;

            // Placeholder text
            var phGo = new GameObject("Placeholder", typeof(RectTransform), typeof(Text));
            phGo.transform.SetParent(go.transform, false);
            var phRt = phGo.GetComponent<RectTransform>();
            phRt.anchorMin = Vector2.zero; phRt.anchorMax = Vector2.one;
            phRt.offsetMin = new Vector2(8, 4); phRt.offsetMax = new Vector2(-8, -4);
            var phTxt = phGo.GetComponent<Text>();
            phTxt.font          = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            phTxt.fontSize      = 13;
            phTxt.color         = new Color(0.50f, 0.50f, 0.55f, 0.80f);
            phTxt.fontStyle     = FontStyle.Italic;
            phTxt.text          = "e.g. A serene Japanese garden at dawn…";
            phTxt.raycastTarget = false;
            field.placeholder   = phTxt;

            // Input text
            var txtGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
            txtGo.transform.SetParent(go.transform, false);
            var txtRt = txtGo.GetComponent<RectTransform>();
            txtRt.anchorMin = Vector2.zero; txtRt.anchorMax = Vector2.one;
            txtRt.offsetMin = new Vector2(8, 4); txtRt.offsetMax = new Vector2(-8, -4);
            var inputTxt = txtGo.GetComponent<Text>();
            inputTxt.font          = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            inputTxt.fontSize      = 13;
            inputTxt.color         = Color.white;
            inputTxt.raycastTarget = false;
            field.textComponent    = inputTxt;

            return field;
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // WorldCardUI — one clickable card per world
    // Implements pointer interfaces directly (no Button component) so that XR
    // input modules (e.g. Oculus PointableCanvasModule) always fire events on
    // the same GameObject as the raycast-target Image.
    // ──────────────────────────────────────────────────────────────────────────

    public class WorldCardUI : MonoBehaviour,
        IPointerEnterHandler,
        IPointerExitHandler,
        IInitializePotentialDragHandler,
        IBeginDragHandler,
        IDragHandler,
        IEndDragHandler
    {
        // Wired by Create()
        public RawImage   panoramaImage;
        public Text       nameText;
        public GameObject loadingOverlay;
        public Text       loadingLabel;
        public Slider     progressSlider;
        public Button     deleteButton;    // top-right ✕ button — calls RequestDeleteWorld
        Image             _hoverOverlay;   // brightens on pointer-enter

        [Header("Save System")]
        public UnityEngine.UI.Image bookmarkIndicator;

        public void SetBookmarkVisible(bool visible)
        {
            if (bookmarkIndicator != null)
                bookmarkIndicator.gameObject.SetActive(visible);
        }

        World _world;
        WorldLabsWorldManager _manager;
        Action<Texture2D>      _onPanoramaReady;
        Action                 _onSplatReady;
        Action                 _onPanoramaFailed;
        Action<World>          _onClicked;
        Action<string, byte[]> _onPanoBytesDownloaded;
        Action<string>         _onPanoWorldShown;
        bool                   _panoramaOnly;

        public void SetPanoramaOnly(bool value) { _panoramaOnly = value; }

        public string WorldId => _world?.world_id;

        // ── IPointerEnterHandler / IPointerExitHandler ────────────────────────

        public void OnPointerEnter(PointerEventData _)
        {
            if (_hoverOverlay != null)
                _hoverOverlay.color = new Color(0.60f, 0.75f, 1.00f, 0.28f);
        }

        public void OnPointerExit(PointerEventData _)
        {
            if (_hoverOverlay != null)
                _hoverOverlay.color = new Color(1f, 1f, 1f, 0f);
        }

        // ── Drag interception ─────────────────────────────────────────────────
        // WorldCardUI is on the same GO as the Button, so when the event system
        // finds this as the drag handler it sets:
        //   pointerDrag  = card GO
        //   pointerPress = card GO  (Button is also on card GO)
        // Because pointerDrag == pointerPress, Unity does NOT clear eligibleForClick
        // even if the pointer drifts — so Button.onClick always fires on tap.
        // The no-op drag methods also prevent ScrollRect from ever receiving drags
        // from the card area, so dragging cards never scrolls the list.
        public void OnInitializePotentialDrag(PointerEventData e) { e.useDragThreshold = true; }
        public void OnBeginDrag(PointerEventData e) { }
        public void OnDrag(PointerEventData e) { }
        public void OnEndDrag(PointerEventData e) { }

        // ── Binding ───────────────────────────────────────────────────────────

        public void Bind(
            World world,
            WorldLabsWorldManager manager,
            Action<Texture2D> onPanoramaReady,
            Action onSplatReady,
            Action onPanoramaFailed,
            bool panoramaOnly,
            Action<World> onClicked = null,
            Action<string, byte[]> onPanoBytesDownloaded = null,
            Action<string> onPanoWorldShown = null)
        {
            _world                 = world;
            _manager               = manager;
            _onPanoramaReady       = onPanoramaReady;
            _onSplatReady          = onSplatReady;
            _onPanoramaFailed      = onPanoramaFailed;
            _onClicked             = onClicked;
            _onPanoBytesDownloaded = onPanoBytesDownloaded;
            _onPanoWorldShown      = onPanoWorldShown;
            _panoramaOnly          = panoramaOnly;

            Debug.Log($"[WorldCardUI] Bind '{world?.display_name}'");

            if (nameText != null)
                nameText.text = string.IsNullOrEmpty(world.display_name)
                    ? world.world_id : world.display_name;

            if (_hoverOverlay != null)
                _hoverOverlay.color = new Color(1f, 1f, 1f, 0f);

            // Wire the Button click — Button is on the same root GO.
            var btn = GetComponent<Button>();
            if (btn != null)
            {
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(HandleClick);
                btn.interactable = true;
            }

            // Wire the delete button — re-bound every Bind() since cards are pooled.
            if (deleteButton != null)
            {
                deleteButton.onClick.RemoveAllListeners();
                World capturedForDelete = world;
                deleteButton.onClick.AddListener(() =>
                    GetComponentInParent<WorldBrowserController>()
                        ?.RequestDeleteWorld(capturedForDelete));
            }

            RefreshState();
            DownloadPanorama();
        }

        // ── State ─────────────────────────────────────────────────────────────

        public void RefreshState()
        {
            if (_world == null) return;
            bool loaded  = _manager != null && _manager.IsWorldLoaded(_world.world_id);
            bool loading = _manager != null && _manager.IsWorldLoading(_world.world_id);

            if (loadingOverlay != null)
                loadingOverlay.SetActive(loading);

            if (progressSlider != null)
                progressSlider.gameObject.SetActive(loading);

            // Card border colour (the 2 px inset around the panorama) — green = loaded.
            var img = GetComponent<Image>();
            if (img != null)
                img.color = loaded
                    ? new Color(0.15f, 0.70f, 0.25f, 1f)   // bright green border
                    : new Color(0.22f, 0.24f, 0.32f, 1f);  // neutral dark-blue border

            if (nameText != null)
            {
                var nameBg = nameText.transform.parent?.GetComponent<Image>();
                if (nameBg != null)
                    nameBg.color = loaded
                        ? new Color(0.06f, 0.22f, 0.10f, 0.95f)
                        : new Color(0.08f, 0.08f, 0.14f, 0.95f);

                string displayName = string.IsNullOrEmpty(_world.display_name)
                    ? _world.world_id : _world.display_name;
                nameText.text = loaded ? $"✓  {displayName}" : displayName;
            }
        }

        public void SetProgress(float p)
        {
            if (progressSlider == null) return;
            progressSlider.gameObject.SetActive(true);
            progressSlider.value = p;
            if (loadingLabel != null)
                loadingLabel.text = $"Loading {p * 100:0}%";
        }

        // ── Click ─────────────────────────────────────────────────────────────

        async void HandleClick()
        {
            Debug.Log($"[WorldCardUI] Click: '{_world?.display_name}'");

            if (_world == null || _manager == null) return;
            if (_manager.IsWorldLoading(_world.world_id)) return;

            _onClicked?.Invoke(_world);

            // ── Unload path (unchanged) ───────────────────────────────────────────
            if (_manager.IsWorldLoaded(_world.world_id))
            {
                _manager.UnloadWorld(_world.world_id);
                RefreshState();
                return;
            }

            RefreshState();

            // Capture world reference before any await — card may be rebound during downloads.
            World worldAtClick = _world;

            // ── 1. Download panorama texture ──────────────────────────────────────
            string panoUrl  = _world?.assets?.imagery?.pano_url;
            string thumbUrl = _world?.assets?.thumbnail_url;

            if (!string.IsNullOrEmpty(panoUrl) || !string.IsNullOrEmpty(thumbUrl))
            {
                try
                {
                    // Use binary download so we can cache the raw bytes before decoding.
                    // Skip WebP URLs — Unity doesn't decode them natively.
                    bool primaryIsWebP = panoUrl != null && panoUrl.EndsWith(".webp", StringComparison.OrdinalIgnoreCase);
                    string urlToUse = primaryIsWebP && !string.IsNullOrEmpty(thumbUrl) ? thumbUrl : (panoUrl ?? thumbUrl);

                    byte[] imageBytes = await WorldLabsClientExtensions.DownloadBinaryAsync(urlToUse);

                    // Guard: if card was rebound to a different world while awaiting, discard.
                    if (_world != worldAtClick) return;

                    Texture2D tex = new Texture2D(2, 2);
                    if (imageBytes != null && tex.LoadImage(imageBytes))
                    {
                        _onPanoBytesDownloaded?.Invoke(_world.world_id, imageBytes);
                        _onPanoramaReady?.Invoke(tex);
                    }
                    else
                    {
                        UnityEngine.Object.Destroy(tex);
                        _onPanoramaFailed?.Invoke();
                    }
                }
                catch (Exception ex)
                {
                    if (_world != worldAtClick) return;
                    Debug.LogWarning($"[WorldCardUI] Panorama sphere download failed for " +
                                     $"'{_world?.display_name}': {ex.Message}");
                    _onPanoramaFailed?.Invoke();
                }
            }

            // ── 2. Panorama-only mode — stop here ─────────────────────────────────
            if (_panoramaOnly)
            {
                _onPanoWorldShown?.Invoke(worldAtClick.world_id);
                RefreshState();
                return;
            }

            // Guard: check again before starting the SPZ load (covers the case where no
            // pano URL existed so the block above was skipped entirely).
            if (_world != worldAtClick) return;

            // ── 3. Load splat ─────────────────────────────────────────────────────
            try
            {
                var loaded = await _manager.LoadWorldAsync(_world);
                if (_world != worldAtClick) return;
                if (loaded != null)
                    _onSplatReady?.Invoke();
                else
                    Debug.LogWarning($"[WorldCardUI] LoadWorldAsync returned null for '{_world?.display_name}'");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[WorldCardUI] Load failed '{_world?.display_name}': {ex.Message}");
            }

            if (_world != worldAtClick) return;
            RefreshState();
        }

        // ── Panorama download — async/await, no coroutine ─────────────────────

        async void DownloadPanorama()
        {
            if (panoramaImage == null) return;
            panoramaImage.texture = null;

            // Prefer the panorama URL; fall back to thumbnail (handles WebP skip)
            string panoUrl  = _world?.assets?.imagery?.pano_url;
            string thumbUrl = _world?.assets?.thumbnail_url;

            if (string.IsNullOrEmpty(panoUrl) && string.IsNullOrEmpty(thumbUrl))
            {
                Debug.Log($"[WorldCardUI] No image URL for '{_world?.display_name}'");
                return;
            }

            try
            {
                Texture2D tex = await WorldLabsClientExtensions
                    .DownloadTextureWithFallbackAsync(panoUrl, thumbUrl);

                // Guard: card might have been recycled by the time download completes
                if (panoramaImage != null && tex != null)
                {
                    panoramaImage.color = Color.white;
                    panoramaImage.texture = tex;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[WorldCardUI] Panorama download failed for " +
                                 $"'{_world?.display_name}': {ex.Message}");
            }
        }

        // ── Factory ───────────────────────────────────────────────────────────

        /// <summary>Creates a card sized by the GridLayoutGroup cell; do not set sizeDelta.</summary>
        public static WorldCardUI Create(Transform parent, float imageHeight)
        {
            const float nameBarH = 34f;

            var go = new GameObject("WorldCard", typeof(RectTransform));
            go.transform.SetParent(parent, false);

            // Root Image — the SOLE raycast target for the entire card.
            var cardImg = go.AddComponent<Image>();
            cardImg.color         = Color.white;
            cardImg.raycastTarget = true;

            // Button on the same GO as the raycast-target Image.
            // Transition.None — cardImg is hidden behind children, tinting is invisible.
            // Hover visual is handled separately by WorldCardUI + _hoverOverlay.
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = cardImg;
            btn.interactable  = true;
            btn.transition    = Selectable.Transition.None;
            btn.navigation    = new Navigation { mode = Navigation.Mode.None };

            var card = go.AddComponent<WorldCardUI>();

            // ── Panorama (inset 2 px so card border colour shows) ─────────────
            var panoGo = new GameObject("Panorama", typeof(RectTransform), typeof(RawImage));
            panoGo.transform.SetParent(go.transform, false);
            var panoRt = panoGo.GetComponent<RectTransform>();
            panoRt.anchorMin = new Vector2(0f, 0f);
            panoRt.anchorMax = new Vector2(1f, 1f);
            panoRt.offsetMin = new Vector2(2f, nameBarH + 2f);
            panoRt.offsetMax = new Vector2(-2f, -2f);
            card.panoramaImage = panoGo.GetComponent<RawImage>();
            card.panoramaImage.color         = new Color(0.18f, 0.22f, 0.32f, 1f);
            card.panoramaImage.uvRect        = new Rect(0, 0, 1, 1);
            card.panoramaImage.raycastTarget = false;  // must not block root

            // ── Name bar (bottom 34 px) ───────────────────────────────────────
            var nameBgGo = new GameObject("NameBar", typeof(RectTransform), typeof(Image));
            nameBgGo.transform.SetParent(go.transform, false);
            var nameBgRt = nameBgGo.GetComponent<RectTransform>();
            nameBgRt.anchorMin = new Vector2(0f, 0f);
            nameBgRt.anchorMax = new Vector2(1f, 0f);
            nameBgRt.offsetMin = Vector2.zero;
            nameBgRt.offsetMax = new Vector2(0f, nameBarH);
            nameBgGo.GetComponent<Image>().color         = new Color(0.08f, 0.08f, 0.14f, 0.95f);
            nameBgGo.GetComponent<Image>().raycastTarget = false;

            var nameGo = new GameObject("Name", typeof(RectTransform), typeof(Text));
            nameGo.transform.SetParent(nameBgGo.transform, false);
            var nameRt = nameGo.GetComponent<RectTransform>();
            nameRt.anchorMin = Vector2.zero; nameRt.anchorMax = Vector2.one;
            nameRt.offsetMin = new Vector2(6f, 2f); nameRt.offsetMax = new Vector2(-6f, -2f);
            card.nameText = nameGo.GetComponent<Text>();
            card.nameText.font          = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            card.nameText.fontSize      = 13;
            card.nameText.color         = Color.white;
            card.nameText.alignment     = TextAnchor.MiddleLeft;
            card.nameText.raycastTarget = false;

            // ── Loading overlay (hidden by default) ───────────────────────────
            var overlayGo = new GameObject("LoadingOverlay", typeof(RectTransform), typeof(Image));
            overlayGo.transform.SetParent(go.transform, false);
            var overlayRt = overlayGo.GetComponent<RectTransform>();
            overlayRt.anchorMin = new Vector2(0f, 0f);
            overlayRt.anchorMax = new Vector2(1f, 1f);
            overlayRt.offsetMin = new Vector2(0f, nameBarH);
            overlayRt.offsetMax = Vector2.zero;
            overlayGo.GetComponent<Image>().color         = new Color(0f, 0f, 0f, 0.60f);
            overlayGo.GetComponent<Image>().raycastTarget = false;
            card.loadingOverlay = overlayGo;

            var loadLabelGo = new GameObject("LoadingLabel", typeof(RectTransform), typeof(Text));
            loadLabelGo.transform.SetParent(overlayGo.transform, false);
            var llRt = loadLabelGo.GetComponent<RectTransform>();
            llRt.anchorMin = new Vector2(0.05f, 0.45f);
            llRt.anchorMax = new Vector2(0.95f, 0.70f);
            llRt.offsetMin = llRt.offsetMax = Vector2.zero;
            card.loadingLabel = loadLabelGo.GetComponent<Text>();
            card.loadingLabel.font          = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            card.loadingLabel.fontSize      = 13;
            card.loadingLabel.color         = Color.white;
            card.loadingLabel.alignment     = TextAnchor.MiddleCenter;
            card.loadingLabel.text          = "Loading…";
            card.loadingLabel.raycastTarget = false;

            // ── Progress slider (inside overlay) ─────────────────────────────
            var sliderGo = new GameObject("Progress", typeof(RectTransform));
            sliderGo.transform.SetParent(overlayGo.transform, false);
            var sliderRt = sliderGo.GetComponent<RectTransform>();
            sliderRt.anchorMin = new Vector2(0.05f, 0.32f);
            sliderRt.anchorMax = new Vector2(0.95f, 0.42f);
            sliderRt.offsetMin = sliderRt.offsetMax = Vector2.zero;

            var slider = sliderGo.AddComponent<Slider>();
            slider.minValue = 0; slider.maxValue = 1; slider.value = 0;
            slider.interactable = false;
            slider.transition   = Selectable.Transition.None;

            var sliderBg = new GameObject("BG", typeof(RectTransform), typeof(Image));
            sliderBg.transform.SetParent(sliderGo.transform, false);
            Stretch(sliderBg, Vector2.zero, Vector2.one);
            var sliderBgImg = sliderBg.GetComponent<Image>();
            sliderBgImg.color = new Color(0.2f, 0.2f, 0.2f, 1f);
            sliderBgImg.raycastTarget = false;

            var fillArea = new GameObject("FillArea", typeof(RectTransform));
            fillArea.transform.SetParent(sliderGo.transform, false);
            Stretch(fillArea, Vector2.zero, Vector2.one);

            var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fill.transform.SetParent(fillArea.transform, false);
            var fillRt = fill.GetComponent<RectTransform>();
            fillRt.anchorMin = Vector2.zero;
            fillRt.anchorMax = new Vector2(0f, 1f);
            fillRt.offsetMin = fillRt.offsetMax = Vector2.zero;
            var fillImg = fill.GetComponent<Image>();
            fillImg.color = new Color(0.25f, 0.65f, 1.0f, 1f);
            fillImg.raycastTarget = false;
            slider.fillRect = fillRt;

            card.progressSlider = slider;
            sliderGo.SetActive(false);
            overlayGo.SetActive(false);

            // ── Delete button (top-right corner, 24×24, red) ─────────────────────
            var delGo = new GameObject("DeleteButton", typeof(RectTransform));
            delGo.transform.SetParent(go.transform, false);
            var delRt = delGo.GetComponent<RectTransform>();
            delRt.anchorMin = new Vector2(1f, 1f);
            delRt.anchorMax = new Vector2(1f, 1f);
            delRt.pivot     = new Vector2(1f, 1f);
            delRt.offsetMin = new Vector2(-26f, -26f);
            delRt.offsetMax = new Vector2(-2f,  -2f);

            var delImg = delGo.AddComponent<Image>();
            delImg.color         = new Color(0.70f, 0.10f, 0.10f, 0.90f);
            delImg.raycastTarget = true;

            var delBtn = delGo.AddComponent<Button>();
            delBtn.targetGraphic = delImg;
            delBtn.transition    = Selectable.Transition.ColorTint;
            var delColors = delBtn.colors;
            delColors.highlightedColor = new Color(0.90f, 0.20f, 0.20f, 1f);
            delColors.pressedColor     = new Color(0.50f, 0.06f, 0.06f, 1f);
            delBtn.colors = delColors;
            delBtn.navigation = new Navigation { mode = Navigation.Mode.None };

            var delTxtGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
            delTxtGo.transform.SetParent(delGo.transform, false);
            var delTxtRt = delTxtGo.GetComponent<RectTransform>();
            delTxtRt.anchorMin = Vector2.zero; delTxtRt.anchorMax = Vector2.one;
            delTxtRt.offsetMin = delTxtRt.offsetMax = Vector2.zero;
            var delTxt = delTxtGo.GetComponent<Text>();
            delTxt.font          = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            delTxt.fontSize      = 14;
            delTxt.color         = Color.white;
            delTxt.alignment     = TextAnchor.MiddleCenter;
            delTxt.text          = "✕";
            delTxt.raycastTarget = false;

            card.deleteButton = delBtn;

            // ── Bookmark indicator (top-left, 24×24, gold) ───────────────────
            // Hidden by default; WorldConfigStore.HasConfigForWorldId controls visibility.
            var bmGo = new GameObject("BookmarkIndicator", typeof(RectTransform));
            bmGo.transform.SetParent(go.transform, false);
            var bmRt = bmGo.GetComponent<RectTransform>();
            bmRt.anchorMin = new Vector2(0f, 1f);
            bmRt.anchorMax = new Vector2(0f, 1f);
            bmRt.pivot     = new Vector2(0f, 1f);
            bmRt.offsetMin = new Vector2(2f, -26f);
            bmRt.offsetMax = new Vector2(26f, -2f);
            var bmImg = bmGo.AddComponent<Image>();
            bmImg.color         = new Color(0.95f, 0.75f, 0.10f, 1f);
            bmImg.raycastTarget = false;
            card.bookmarkIndicator = bmImg;
            bmGo.SetActive(false);

            // ── Hover overlay — last child, drawn on top, transparent by default ─
            // WorldCardUI.OnPointerEnter/Exit sets its colour directly.
            // raycastTarget = false so all hits land on cardImg (root).
            var hlGo = new GameObject("HoverOverlay", typeof(RectTransform), typeof(Image));
            hlGo.transform.SetParent(go.transform, false);
            var hlRt = hlGo.GetComponent<RectTransform>();
            hlRt.anchorMin = Vector2.zero; hlRt.anchorMax = Vector2.one;
            hlRt.offsetMin = hlRt.offsetMax = Vector2.zero;
            var hlImg = hlGo.GetComponent<Image>();
            hlImg.color         = new Color(1f, 1f, 1f, 0f);
            hlImg.raycastTarget = false;
            card._hoverOverlay  = hlImg;

            return card;
        }

        // reuse the Stretch helper without referencing the outer class
        static void Stretch(GameObject go, Vector2 anchorMin, Vector2 anchorMax)
        {
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // ScrollbarOnlyScrollRect
    // A ScrollRect that ignores pointer-drag so only the scrollbar scrolls.
    // Prevents hand-tremor in XR from triggering a scroll when clicking a card.
    // ──────────────────────────────────────────────────────────────────────────

    public class ScrollbarOnlyScrollRect : ScrollRect
    {
        public override void OnInitializePotentialDrag(PointerEventData e) { }
        public override void OnBeginDrag(PointerEventData e) { }
        public override void OnDrag(PointerEventData e) { }
        public override void OnEndDrag(PointerEventData e) { }
    }
}
