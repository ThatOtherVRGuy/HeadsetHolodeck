using System;
using GaussianSplatting.Runtime;
using Holodeck.Direct;
using Holodeck.State;
using UnityEngine;
using WorldLabs.Runtime;

namespace SpeechIntent
{
    public enum ViewMode { None, Pano, Splat3D, Mesh }

    public class ViewModeController : MonoBehaviour
    {
        [Header("Scene References")]
        public ThumbnailSkyboxController         thumbnailSkybox;
        public InteractionMemory                 interactionMemory;
        public WorldLabsWorldManager             worldManager;
        public HolodeckStateMachine              stateMachine;
        public VoiceToWorldLabsPluginCoordinator coordinator;
        public WorldBrowserController            worldBrowser;
        public WorldMeshController               worldMeshController;
        public HolodeckModelController           holodeckModelController;

        [Header("Events")]
        public StringEvent onViewModeError;

        public ViewMode DesiredMode { get; private set; }

        private bool _isSplatReady;
        private string _loadedSplatWorldId;  // world_id of the splat currently held by worldManager
        private GaussianSplatRenderer _loadedSplatRenderer;
        private bool _isPanoReady;  // re-synced from thumbnailSkybox.IsReady at start of every TryApply()
        private bool _panoPreloadPending;  // true while LocalRemotePanoLoader.PreloadAsync is in flight

        private void Awake()
        {
            // Fallback: find WorldBrowserController anywhere in the scene if not wired in Inspector.
            if (worldBrowser == null)
            {
                GameObject go = GameObject.Find("UI/WorldLabs_GUI");
                if (go != null) worldBrowser = go.GetComponent<WorldBrowserController>();
            }

            Debug.Log($"[ViewModeController] Awake — thumbnailSkybox={thumbnailSkybox != null}, " +
                      $"interactionMemory={interactionMemory != null}, worldManager={worldManager != null}, " +
                      $"stateMachine={stateMachine != null}, coordinator={coordinator != null}, " +
                      $"worldBrowser={worldBrowser != null}");
        }

        private void OnEnable()
        {
            if (worldManager != null)
            {
                worldManager.OnWorldLoaded   += OnWorldLoaded;
                worldManager.OnWorldUnloaded += OnWorldUnloaded;
            }
            if (stateMachine != null)
                stateMachine.StateChanged += OnStateChanged;
            if (thumbnailSkybox != null)
                thumbnailSkybox.OnReady += TryApply;
        }

        private void OnDisable()
        {
            if (worldManager != null)
            {
                worldManager.OnWorldLoaded   -= OnWorldLoaded;
                worldManager.OnWorldUnloaded -= OnWorldUnloaded;
            }
            if (stateMachine != null)
                stateMachine.StateChanged -= OnStateChanged;
            if (thumbnailSkybox != null)
                thumbnailSkybox.OnReady -= TryApply;
        }

        /// <summary>Called by LocalRemotePanoLoader.PreloadCoroutine at start/end so we don't
        /// report "not available" while a background fetch is still in flight.</summary>
        public void SetPanoPreloadPending(bool pending) => _panoPreloadPending = pending;

        /// <summary>True when a panorama is loaded, stored, or being fetched.</summary>
        public bool IsPanoAvailable =>
            (thumbnailSkybox != null && (thumbnailSkybox.IsReady || thumbnailSkybox.HasStoredTexture))
            || _panoPreloadPending;

        /// <summary>True when a splat is loaded or a WorldLabs world is available to load.</summary>
        public bool IsSplatAvailable => _isSplatReady || worldBrowser?.LastClickedWorld != null;

        public void RequestPanoView()
        {
            bool panoReady = thumbnailSkybox != null &&
                             (thumbnailSkybox.IsReady || thumbnailSkybox.HasStoredTexture);
            if (!panoReady && !_panoPreloadPending)
            {
                Debug.LogWarning("[ViewModeController] RequestPanoView — no panorama available.");
                onViewModeError?.Invoke("Panorama is not available for this world.");
                return;
            }

            worldMeshController?.HideVisual();
            HideStaticHolodeckModel("RequestPanoView");
            Debug.Log($"[ViewModeController] RequestPanoView — coordinator={(coordinator != null ? "set" : "NULL")}, worldBrowser={(worldBrowser != null ? "set" : "NULL")}, " +
                      $"worldManager={(worldManager != null ? "set" : "NULL")}, worldParent={(worldManager?.worldParent != null ? worldManager.worldParent.gameObject.name : "NULL")}");
            if (coordinator != null) coordinator.panoramaOnly = true;
            worldBrowser?.SetPanoramaOnly(true);
            DesiredMode = ViewMode.Pano;
            TryApply();
        }

        public void RequestMeshView()
        {
            Debug.Log($"[ViewModeController] RequestMeshView — worldMeshController={(worldMeshController != null ? "set" : "NULL")}");

            if (worldMeshController == null || !worldMeshController.HasMesh)
            {
                onViewModeError?.Invoke("Mesh not available for this world.");
                Debug.LogWarning("[ViewModeController] RequestMeshView — no mesh loaded.");
                return;
            }

            DesiredMode = ViewMode.Mesh;
            HideCurrentWorldRoot();          // hide splat
            // Leave skybox running — pano remains visible behind the mesh
            if (thumbnailSkybox != null) thumbnailSkybox.SuppressNextFadeOut = true;
            TryApply();  // TryApply's Mesh branch calls ShowVisual()
        }

        public void RequestSplatView()
        {
            // A splat can be shown if one is already loaded, or if there's a WorldLabs world
            // the browser can load on demand. Local-only pano worlds have neither.
            bool canShow3D = _isSplatReady || worldBrowser?.LastClickedWorld != null;
            if (!canShow3D)
            {
                Debug.LogWarning("[ViewModeController] RequestSplatView — no 3D source available.");
                onViewModeError?.Invoke("3D is not available for this world.");
                return;
            }

            worldMeshController?.HideVisual();
            Debug.Log($"[ViewModeController] RequestSplatView — coordinator={(coordinator != null ? "set" : "NULL")}, worldBrowser={(worldBrowser != null ? "set" : "NULL")}");
            if (coordinator != null) coordinator.panoramaOnly = false;
            worldBrowser?.SetPanoramaOnly(false);
            DesiredMode = ViewMode.Splat3D;

            // If the loaded splat is stale (different world than currently selected), treat it as absent.
            // This happens when the user browses to a new world while in pano mode — the old splat is never
            // unloaded because panoramaOnly=true skips LoadWorldAsync, so OnWorldUnloaded never fires.
            string desiredWorldId = worldBrowser?.LastClickedWorld?.world_id;
            if (_isSplatReady &&
                !string.IsNullOrEmpty(desiredWorldId) &&
                _loadedSplatWorldId != desiredWorldId)
            {
                Debug.Log($"[ViewModeController] RequestSplatView — stale splat '{_loadedSplatWorldId}' != desired '{desiredWorldId}', invalidating.");
                _isSplatReady = false;
            }

            // If a panorama is already showing but no splat was loaded (panorama-only card click),
            // trigger the splat load now so OnWorldLoaded will fire and TryApply() can show it.
            bool panoIsShowing = thumbnailSkybox != null && thumbnailSkybox.IsReady;
            if (!_isSplatReady && panoIsShowing && worldBrowser?.LastClickedWorld != null && worldManager != null)
            {
                Debug.Log($"[ViewModeController] RequestSplatView — triggering deferred splat load for '{worldBrowser.LastClickedWorld.display_name}'");
                _ = worldManager.LoadWorldAsync(worldBrowser.LastClickedWorld);
            }

            TryApply();
        }

        // ── Event handlers ────────────────────────────────────────────────

        private void OnWorldLoaded(string worldId, GaussianSplatRenderer renderer)
        {
            Debug.Log($"[ViewModeController] OnWorldLoaded worldId={worldId}, renderer={(renderer != null ? renderer.gameObject.name : "NULL")} — setting _isSplatReady=true");
            _isSplatReady = true;
            _loadedSplatWorldId = worldId;
            _loadedSplatRenderer = renderer;
            TryApply();
        }

        private void OnWorldUnloaded(string worldId)
        {
            Debug.Log($"[ViewModeController] OnWorldUnloaded worldId={worldId} — setting _isSplatReady=false");
            _isSplatReady = false;
            _loadedSplatWorldId = null;
            _loadedSplatRenderer = null;
        }

        // IMPORTANT: Must remain synchronous. StateChanged fires inside TryTransitionTo,
        // before the coordinator calls thumbnailSkybox.StartFadeOut(). SuppressNextFadeOut
        // must be set here to reliably suppress that call.
        private void OnStateChanged(HolodeckState previousState, HolodeckState newState)
        {
            Debug.Log($"[ViewModeController] OnStateChanged {previousState} → {newState}");
            if (newState == HolodeckState.Ready || newState == HolodeckState.Error)
                TryApply();
        }

        // ── Core logic ────────────────────────────────────────────────────

        private void TryApply()
        {
            if (thumbnailSkybox == null)
            {
                Debug.LogWarning("[ViewModeController] TryApply: thumbnailSkybox is null, skipping.");
                return;
            }

            // Re-sync pano readiness every call — handles the case where a fade completed
            // and destroyed _thumbnailTexture after we last cached isPanoReady = true.
            _isPanoReady = thumbnailSkybox.IsReady;

            Debug.Log($"[ViewModeController] TryApply — DesiredMode={DesiredMode}, " +
                      $"_isSplatReady={_isSplatReady}, _isPanoReady={_isPanoReady}, " +
                      $"IsShowing={thumbnailSkybox.IsShowing}, " +
                      $"stateMachine.CurrentState={stateMachine?.CurrentState}, " +
                      $"currentWorldRoot={(interactionMemory?.currentWorldRoot != null ? interactionMemory.currentWorldRoot.name : "null")}");

            if (DesiredMode == ViewMode.Pano)
            {
                HideStaticHolodeckModel("Pano");

                if (_isPanoReady && !thumbnailSkybox.IsShowing)
                {
                    Debug.Log("[ViewModeController] TryApply: Pano path — showing stored pano, suppressing next fade, hiding splat.");
                    // Set SuppressNextFadeOut BEFORE ShowStored(), because ShowStored() → Show()
                    // fires OnReady → TryApply() re-entrantly. The IsShowing guard handles that
                    // re-entrant call, but we also want to suppress any upcoming StartFadeOut()
                    // from the coordinator (which fires after StateChanged returns).
                    thumbnailSkybox.SuppressNextFadeOut = true;
                    HideCurrentWorldRoot();
                    thumbnailSkybox.ShowStored();
                }
                else if (_isPanoReady && thumbnailSkybox.IsShowing)
                {
                    Debug.Log("[ViewModeController] TryApply: Pano path — already showing, nothing to do.");
                }
                else if (thumbnailSkybox.HasStoredTexture)
                {
                    // Texture was faded out (e.g., splat was shown) but not destroyed.
                    // Re-show it directly — no SuppressNextFadeOut needed because no
                    // coordinator StartFadeOut() is pending in this path.
                    Debug.Log("[ViewModeController] TryApply: Pano path — texture stored (was faded), re-showing.");
                    HideCurrentWorldRoot();
                    thumbnailSkybox.ShowStored();
                }
                else
                {
                    Debug.Log("[ViewModeController] TryApply: Pano path — pano not yet loaded, waiting for OnReady.");
                }
            }
            else if (DesiredMode == ViewMode.Splat3D)
            {
                if (_isSplatReady)
                {
                    Debug.Log("[ViewModeController] TryApply: Splat3D path — showing world root, fading out pano.");
                    ShowCurrentWorldRoot();
                    thumbnailSkybox.StartFadeOut();
                }
                else if (stateMachine != null && stateMachine.CurrentState == HolodeckState.Error)
                {
                    Debug.Log($"[ViewModeController] TryApply: Splat3D path — state=Error, _isPanoReady={_isPanoReady}.");
                    // Splat failed. Fall back to pano if available.
                    if (_isPanoReady)
                    {
                        DesiredMode = ViewMode.Pano;
                        // No SuppressNextFadeOut here — no StartFadeOut() is pending in this error path.
                        HideCurrentWorldRoot();
                        thumbnailSkybox.ShowStored();
                        onViewModeError?.Invoke("3D not available, falling back to panorama");
                    }
                    else
                    {
                        onViewModeError?.Invoke("3D not available");
                    }
                }
                else
                {
                    Debug.Log("[ViewModeController] TryApply: Splat3D path — splat not yet loaded, waiting for OnWorldLoaded.");
                }
            }
            else if (DesiredMode == ViewMode.Mesh)
            {
                if (worldMeshController != null && worldMeshController.HasMesh)
                {
                    Debug.Log("[ViewModeController] TryApply: Mesh path — showing mesh visual.");
                    HideCurrentWorldRoot();
                    worldMeshController.ShowVisual();
                }
                else
                {
                    Debug.Log("[ViewModeController] TryApply: Mesh path — mesh not ready, falling back to pano.");
                    if (_isPanoReady)
                    {
                        DesiredMode = ViewMode.Pano;
                        HideCurrentWorldRoot();
                        thumbnailSkybox.ShowStored();
                        onViewModeError?.Invoke("Mesh not available, showing panorama");
                    }
                }
            }
        }

        private void HideCurrentWorldRoot()
        {
            Debug.Log($"[ViewModeController] HideCurrentWorldRoot — " +
                      $"worldManager={worldManager != null}, " +
                      $"worldParent={(worldManager?.worldParent != null ? worldManager.worldParent.gameObject.name : "NULL")}, " +
                      $"worldParentActive={(worldManager?.worldParent != null ? worldManager.worldParent.gameObject.activeSelf.ToString() : "N/A")}, " +
                      $"interactionMemory={interactionMemory != null}, " +
                      $"currentWorldRoot={(interactionMemory?.currentWorldRoot != null ? interactionMemory.currentWorldRoot.name : "NULL")}");

            if (worldManager != null && worldManager.worldParent != null)
            {
                worldManager.worldParent.gameObject.SetActive(false);
                Debug.Log($"[ViewModeController] HideCurrentWorldRoot — set '{worldManager.worldParent.gameObject.name}' active=false. " +
                          $"activeSelf={worldManager.worldParent.gameObject.activeSelf}, activeInHierarchy={worldManager.worldParent.gameObject.activeInHierarchy}");
            }
            else if (interactionMemory != null && interactionMemory.currentWorldRoot != null)
            {
                interactionMemory.currentWorldRoot.SetActive(false);
                Debug.Log($"[ViewModeController] HideCurrentWorldRoot — fallback: set '{interactionMemory.currentWorldRoot.name}' active=false.");
            }
            else
            {
                Debug.LogWarning("[ViewModeController] HideCurrentWorldRoot — nothing to hide (worldManager, worldParent, and currentWorldRoot all null/missing).");
            }

            HideStaticHolodeckModel("HideCurrentWorldRoot");
        }

        private void ShowCurrentWorldRoot()
        {
            // Keep the static holodeck shell hidden while a generated splat is active.
            // The Arch UI lives outside TNGHolodeck and remains independently voice-toggleable.
            HideStaticHolodeckModel("Splat3D");

            // Re-enable the dynamic world parent so the loaded splat root can be shown.
            if (worldManager != null && worldManager.worldParent != null)
                worldManager.worldParent.gameObject.SetActive(true);

            // Hide all dynamic worlds so only the current one is shown.
            worldManager?.SetAllWorldsActive(false);

            GameObject worldRoot = _loadedSplatRenderer != null
                ? _loadedSplatRenderer.gameObject
                : interactionMemory != null ? interactionMemory.currentWorldRoot : null;

            if (worldRoot != null)
            {
                worldRoot.SetActive(true);
                Debug.Log($"[ViewModeController] ShowCurrentWorldRoot — set '{worldRoot.name}' active=true.");
            }
            else
            {
                Debug.LogWarning("[ViewModeController] ShowCurrentWorldRoot — no loaded renderer/currentWorldRoot available to activate.");
            }
        }

        private void HideStaticHolodeckModel(string reason)
        {
            GameObject holodeckModel = holodeckModelController?.HolodeckModel;
            if (holodeckModel == null || !holodeckModel.activeSelf)
                return;

            holodeckModel.SetActive(false);
            Debug.Log($"[ViewModeController] {reason} — set static holodeck model '{holodeckModel.name}' active=false.");
        }
    }
}
