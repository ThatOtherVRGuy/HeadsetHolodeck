using System;
using System.Threading.Tasks;
using GaussianSplatting.Runtime;
using UnityEngine;
using WorldLabs.API;
using WorldLabs.Runtime;

namespace Holodeck.Diagnostics
{
    public class WorldLabsNamedWorldDiagnosticLoader : MonoBehaviour
    {
        [Header("World")]
        [SerializeField] private string worldDisplayName = "Christmas at the North Pole";
        [SerializeField] private bool loadOnStart;
        [SerializeField, Min(1)] private int pageSize = 50;
        [SerializeField, Min(1)] private int maxPages = 6;

        [Header("Scene References")]
        [SerializeField] private WorldLabsWorldManager worldManager;

        [Header("Logging")]
        [SerializeField] private bool verboseLogging = true;

        private bool _isLoading;

        public string WorldDisplayName
        {
            get => worldDisplayName;
            set => worldDisplayName = value;
        }

        public bool LoadOnStart
        {
            get => loadOnStart;
            set => loadOnStart = value;
        }

        private async void Start()
        {
            if (loadOnStart)
                await LoadConfiguredWorldAsync();
        }

        [ContextMenu("Load Configured World")]
        public async void LoadConfiguredWorldFromContextMenu()
        {
            await LoadConfiguredWorldAsync();
        }

        public async Task<GaussianSplatRenderer> LoadConfiguredWorldAsync()
        {
            if (_isLoading)
            {
                Debug.LogWarning("[WorldLabsNamedWorldDiagnosticLoader] A diagnostic world load is already running.", this);
                return null;
            }

            if (worldManager == null)
                worldManager = FindFirstObjectByType<WorldLabsWorldManager>();

            if (worldManager == null)
            {
                Debug.LogError("[WorldLabsNamedWorldDiagnosticLoader] No WorldLabsWorldManager found in scene.", this);
                return null;
            }

            if (string.IsNullOrWhiteSpace(worldDisplayName))
            {
                Debug.LogError("[WorldLabsNamedWorldDiagnosticLoader] worldDisplayName is empty.", this);
                return null;
            }

            _isLoading = true;
            try
            {
                Debug.Log($"[WorldLabsNamedWorldDiagnosticLoader] Searching for WorldLabs world '{worldDisplayName}'.", this);
                World world = await FindWorldByDisplayNameAsync(worldDisplayName);
                if (world == null)
                {
                    Debug.LogError($"[WorldLabsNamedWorldDiagnosticLoader] Could not find a WorldLabs world matching '{worldDisplayName}'.", this);
                    return null;
                }

                Debug.Log($"[WorldLabsNamedWorldDiagnosticLoader] Loading '{world.display_name}' ({world.world_id}).", this);
                GaussianSplatRenderer renderer = await worldManager.LoadWorldAsync(world);

                if (renderer == null)
                {
                    Debug.LogError($"[WorldLabsNamedWorldDiagnosticLoader] LoadWorldAsync returned null for '{world.display_name}'.", this);
                    return null;
                }

                LogRendererState("Loaded", renderer);
                return renderer;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[WorldLabsNamedWorldDiagnosticLoader] Diagnostic load failed: {ex}", this);
                return null;
            }
            finally
            {
                _isLoading = false;
            }
        }

        private async Task<World> FindWorldByDisplayNameAsync(string displayName)
        {
            string lower = displayName.Trim().ToLowerInvariant();
            string pageToken = null;

            for (int page = 0; page < maxPages; page++)
            {
                var worlds = await worldManager.ListWorldsAsync(pageToken: pageToken, pageSize: pageSize);
                if (verboseLogging)
                    Debug.Log($"[WorldLabsNamedWorldDiagnosticLoader] Page {page + 1}: {worlds?.Count ?? 0} worlds.", this);

                if (worlds != null)
                {
                    foreach (World world in worlds)
                    {
                        string name = world?.display_name ?? string.Empty;
                        if (name.Equals(displayName, StringComparison.OrdinalIgnoreCase) ||
                            name.ToLowerInvariant().Contains(lower))
                        {
                            return world;
                        }
                    }
                }

                pageToken = worldManager.LastNextPageToken;
                if (string.IsNullOrEmpty(pageToken))
                    break;
            }

            return null;
        }

        private void LogRendererState(string prefix, GaussianSplatRenderer renderer)
        {
            if (renderer == null)
            {
                Debug.LogWarning($"[WorldLabsNamedWorldDiagnosticLoader] {prefix}: renderer=NULL", this);
                return;
            }

            GameObject go = renderer.gameObject;
            Transform parent = worldManager != null ? worldManager.worldParent : null;
            Debug.Log(
                $"[WorldLabsNamedWorldDiagnosticLoader] {prefix}: renderer='{go.name}', " +
                $"activeSelf={go.activeSelf}, activeInHierarchy={go.activeInHierarchy}, " +
                $"parent={(parent != null ? parent.name : "NULL")}, " +
                $"parentActive={(parent != null ? parent.gameObject.activeInHierarchy.ToString() : "N/A")}, " +
                $"loadedIds=[{string.Join(", ", worldManager.LoadedWorldIds)}]",
                this);
        }
    }
}
