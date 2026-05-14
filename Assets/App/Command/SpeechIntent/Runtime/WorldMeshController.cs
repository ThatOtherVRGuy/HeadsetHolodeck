// Assets/App/Command/SpeechIntent/Runtime/WorldMeshController.cs

using System;
using System.Threading.Tasks;
using GLTFast;
using GaussianSplatting.Runtime;
using UnityEngine;
using WorldLabs.API;
using WorldLabs.Runtime;

namespace SpeechIntent
{
    /// <summary>
    /// Automatically downloads the GLB collision mesh when a WorldLabs world loads.
    /// Builds MeshCollider components (always active) and toggleable MeshRenderers
    /// for mesh-only view mode. Aligns to the same transform as the splat renderer.
    /// </summary>
    public class WorldMeshController : MonoBehaviour
    {
        [Header("Dependencies")]
        public WorldLabsWorldManager worldManager;

        // ── State ─────────────────────────────────────────────────────────────
        GltfImport  _gltf;
        GameObject  _meshRoot;
        bool        _isLoading;

        public bool HasMesh => _meshRoot != null && !_isLoading;

        // ── Lifecycle ─────────────────────────────────────────────────────────

        void OnEnable()
        {
            if (worldManager == null)
            {
                Debug.LogWarning("[WorldMeshController] worldManager is not assigned — mesh events will not fire.");
                return;
            }
            worldManager.OnWorldLoaded   += OnWorldLoaded;
            worldManager.OnWorldUnloaded += OnWorldUnloaded;
        }

        void OnDisable()
        {
            if (worldManager != null)
            {
                worldManager.OnWorldLoaded   -= OnWorldLoaded;
                worldManager.OnWorldUnloaded -= OnWorldUnloaded;
            }
        }

        void OnDestroy()
        {
            DestroyMesh();
        }

        // ── Event handlers ────────────────────────────────────────────────────

        private async void OnWorldLoaded(string worldId, GaussianSplatRenderer renderer)
        {
            try
            {
                // Skip the default placeholder world
                if (worldId == "__default__") return;

                World world = worldManager.LastLoadedWorld;
                string meshUrl = world?.assets?.mesh?.collider_mesh_url;

                if (string.IsNullOrEmpty(meshUrl))
                {
                    Debug.Log($"[WorldMeshController] No collider_mesh_url for '{worldId}' — skipping mesh download.");
                    return;
                }

                await LoadMeshAsync(worldId, meshUrl, renderer);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        private void OnWorldUnloaded(string worldId)
        {
            DestroyMesh();
        }

        // ── Mesh loading ──────────────────────────────────────────────────────

        private async Task LoadMeshAsync(string worldId, string url, GaussianSplatRenderer renderer)
        {
            if (_isLoading)
            {
                Debug.LogWarning($"[WorldMeshController] Already loading a mesh, skipping '{worldId}'.");
                return;
            }

            DestroyMesh();
            _isLoading = true;

            Debug.Log($"[WorldMeshController] Downloading collider mesh for '{worldId}'…");

            try
            {
                byte[] bytes = await WorldLabsClientExtensions.DownloadBinaryAsync(url);
                Debug.Log($"[WorldMeshController] GLB download complete: {bytes.Length} bytes.");

                // Create root aligned to the splat renderer's parent transform
                _meshRoot = new GameObject("MeshRoot");
                _meshRoot.transform.SetParent(transform, false);

                // Copy splat root transform so mesh and splat align.
                // Note: lossyScale (world scale) is assigned as localScale. This is correct
                // only when WorldMeshController's own transform has identity scale.
                if (renderer != null)
                {
                    Transform splatParent = renderer.transform.parent != null
                        ? renderer.transform.parent
                        : renderer.transform;
                    _meshRoot.transform.SetPositionAndRotation(
                        splatParent.position, splatParent.rotation);
                    _meshRoot.transform.localScale = splatParent.lossyScale;
                }
                else
                {
                    Debug.LogWarning($"[WorldMeshController] renderer is null for '{worldId}' — mesh will use identity transform.");
                }

                // Import GLB
                _gltf = new GltfImport();
                bool ok = await _gltf.LoadGltfBinary(bytes);
                if (!ok)
                {
                    Debug.LogError($"[WorldMeshController] GLTFast failed to parse GLB for '{worldId}'.");
                    DestroyMesh();
                    return;
                }

                await _gltf.InstantiateSceneAsync(_meshRoot.transform);
                Debug.Log($"[WorldMeshController] GLB instantiated under MeshRoot.");

                // Add MeshColliders; hide renderers (visual is off by default)
                foreach (var mf in _meshRoot.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.sharedMesh == null) continue;
                    var mc = mf.gameObject.AddComponent<MeshCollider>();
                    mc.sharedMesh = mf.sharedMesh;
                    mc.convex     = false;
                }

                foreach (var mr in _meshRoot.GetComponentsInChildren<MeshRenderer>(true))
                    mr.enabled = false;

                Debug.Log($"[WorldMeshController] Mesh ready for '{worldId}' — colliders active, visual hidden.");
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                DestroyMesh();
            }
            finally
            {
                _isLoading = false;
            }
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Enables all MeshRenderers in the mesh root so the geometry is visible.
        /// Colliders remain active regardless of this call.
        /// </summary>
        public void ShowVisual()
        {
            if (_meshRoot == null) return;
            foreach (var mr in _meshRoot.GetComponentsInChildren<MeshRenderer>(true))
                mr.enabled = true;
        }

        /// <summary>
        /// Disables all MeshRenderers in the mesh root. Colliders remain active.
        /// </summary>
        public void HideVisual()
        {
            if (_meshRoot == null) return;
            foreach (var mr in _meshRoot.GetComponentsInChildren<MeshRenderer>(true))
                mr.enabled = false;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        void DestroyMesh()
        {
            if (_meshRoot != null)
            {
                Destroy(_meshRoot);
                _meshRoot = null;
            }
            _gltf?.Dispose();
            _gltf = null;
        }
    }
}
