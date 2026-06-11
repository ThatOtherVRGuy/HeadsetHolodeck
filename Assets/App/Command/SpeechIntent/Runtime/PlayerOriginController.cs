using GaussianSplatting.Runtime;
using Holodeck.Direct;
using Holodeck.Save;
using UnityEngine;
using WorldLabs.Runtime;
using WorldLabs.Runtime.Tools;

namespace SpeechIntent
{
    /// <summary>
    /// Teleports the player ('Me' / XR Origin) back to the configured reset anchor whenever a new
    /// splat or panorama becomes visible, and when "end program" is executed.
    ///
    /// Wire playerRoot to the XR Origin root (the object renamed to 'Me').
    /// Wire thumbnailSkybox and worldManager so the component can subscribe to their
    /// visibility events automatically.
    /// </summary>
    public class PlayerOriginController : MonoBehaviour
    {
        [Tooltip("The root transform of the XR Origin / player rig ('Me'). Teleported to origin on reset.")]
        public Transform playerRoot;

        [Tooltip("Optional reset target. If empty, a GameObject named 'Teleport Anchor' is found at runtime. Falls back to world origin.")]
        public Transform resetAnchor;

        [Tooltip("When enabled, also copies the reset anchor rotation onto playerRoot.")]
        public bool matchAnchorRotation;

        [Tooltip("ThumbnailSkyboxController — ResetToOrigin is called when a panorama becomes visible.")]
        public ThumbnailSkyboxController thumbnailSkybox;

        [Tooltip("WorldLabsWorldManager — ResetToOrigin is called when a splat finishes loading.")]
        public WorldLabsWorldManager worldManager;

        [Tooltip("WorldConfigAutoSave — used to save and cycle spawn points for the active world.")]
        public WorldConfigAutoSave worldConfigAutoSave;

        int _currentSpawnIndex = -1;

        private void OnEnable()
        {
            if (thumbnailSkybox != null)
                thumbnailSkybox.OnReady += ResetToOrigin;

            if (worldManager != null)
                worldManager.OnWorldLoaded += OnWorldLoaded;
        }

        private void OnDisable()
        {
            if (thumbnailSkybox != null)
                thumbnailSkybox.OnReady -= ResetToOrigin;

            if (worldManager != null)
                worldManager.OnWorldLoaded -= OnWorldLoaded;
        }

        /// <summary>
        /// Teleports the player root to the reset anchor position, or Vector3.zero if no anchor is available.
        /// Safe to call from UnityEvents.
        /// </summary>
        public void ResetToOrigin()
        {
            if (playerRoot == null)
            {
                Debug.LogWarning("[PlayerOriginController] playerRoot is not assigned.");
                return;
            }

            Transform anchor = ResolveResetAnchor();
            if (anchor != null)
            {
                Debug.Log($"[PlayerOriginController] Resetting player to anchor '{anchor.name}'.");
                playerRoot.position = anchor.position;
                if (matchAnchorRotation)
                    playerRoot.rotation = anchor.rotation;
                return;
            }

            Debug.Log("[PlayerOriginController] Resetting player to world origin.");
            playerRoot.position = Vector3.zero;
            if (matchAnchorRotation)
                playerRoot.rotation = Quaternion.identity;
        }

        private Transform ResolveResetAnchor()
        {
            if (resetAnchor != null)
                return resetAnchor;

            GameObject anchorGo = GameObject.Find("Teleport Anchor");
            if (anchorGo != null)
            {
                resetAnchor = anchorGo.transform;
                return resetAnchor;
            }

            return null;
        }

        private void OnWorldLoaded(string worldId, GaussianSplatRenderer renderer)
        {
            _currentSpawnIndex = 0;
            if (TryResetToSavedSpawnPoint())
                return;

            if (TryResetToSpawnPose(renderer))
                return;

            ResetToOrigin();
        }

        public bool SaveCurrentSpawnPoint()
        {
            if (playerRoot == null)
            {
                Debug.LogWarning("[PlayerOriginController] Cannot save spawn point: playerRoot is not assigned.");
                return false;
            }

            if (worldConfigAutoSave == null)
                worldConfigAutoSave = FindFirstObjectByType<WorldConfigAutoSave>(FindObjectsInactive.Include);

            bool saved = worldConfigAutoSave != null && worldConfigAutoSave.SaveManualSpawnPoint(playerRoot);
            if (saved)
            {
                _currentSpawnIndex = Mathf.Max(0, (worldConfigAutoSave.ActiveConfig?.spawn_points?.Count ?? 1) - 1);
                ArchStatusBus.Success("Saved spawn point.", "SPAWN");
            }
            else
            {
                ArchStatusBus.Warning("No active world to save a spawn point.", "SPAWN");
            }

            return saved;
        }

        public bool GoToNextSpawnPoint()
        {
            return ApplySpawnPointStep(1);
        }

        public bool GoToPreviousSpawnPoint()
        {
            return ApplySpawnPointStep(-1);
        }

        public bool RemoveCurrentSpawnPoint()
        {
            if (worldConfigAutoSave == null)
                worldConfigAutoSave = FindFirstObjectByType<WorldConfigAutoSave>(FindObjectsInactive.Include);

            WorldConfig config = worldConfigAutoSave != null ? worldConfigAutoSave.ActiveConfig : null;
            if (config?.spawn_points == null || config.spawn_points.Count == 0)
            {
                Debug.LogWarning("[PlayerOriginController] No saved spawn point to remove.");
                ArchStatusBus.Warning("No saved spawn point to remove.", "SPAWN");
                return false;
            }

            int index = Mathf.Clamp(_currentSpawnIndex, 0, config.spawn_points.Count - 1);
            string label = string.IsNullOrWhiteSpace(config.spawn_points[index]?.name)
                ? $"Spawn {index + 1}"
                : config.spawn_points[index].name;
            config.spawn_points.RemoveAt(index);
            if (worldConfigAutoSave.SaveActiveConfig())
            {
                _currentSpawnIndex = config.spawn_points.Count == 0
                    ? -1
                    : Mathf.Clamp(index, 0, config.spawn_points.Count - 1);
                Debug.Log($"[PlayerOriginController] Removed spawn point: {label}.");
                ArchStatusBus.Success("Removed spawn point " + label + ".", "SPAWN");
                return true;
            }

            Debug.LogWarning("[PlayerOriginController] Removed spawn point in memory, but could not save active config.");
            ArchStatusBus.Warning("Could not save spawn point changes.", "SPAWN");
            return false;
        }

        public bool RemoveAllSpawnPoints()
        {
            if (worldConfigAutoSave == null)
                worldConfigAutoSave = FindFirstObjectByType<WorldConfigAutoSave>(FindObjectsInactive.Include);

            WorldConfig config = worldConfigAutoSave != null ? worldConfigAutoSave.ActiveConfig : null;
            if (config?.spawn_points == null || config.spawn_points.Count == 0)
            {
                Debug.LogWarning("[PlayerOriginController] No saved spawn points to remove.");
                ArchStatusBus.Warning("No saved spawn points to remove.", "SPAWN");
                return false;
            }

            int removedCount = config.spawn_points.Count;
            config.spawn_points.Clear();
            if (worldConfigAutoSave.SaveActiveConfig())
            {
                _currentSpawnIndex = -1;
                Debug.Log($"[PlayerOriginController] Removed all spawn points ({removedCount}).");
                ArchStatusBus.Success($"Removed {removedCount} spawn points.", "SPAWN");
                return true;
            }

            Debug.LogWarning("[PlayerOriginController] Cleared spawn points in memory, but could not save active config.");
            ArchStatusBus.Warning("Could not save spawn point changes.", "SPAWN");
            return false;
        }

        public bool SuggestSpawnPoint()
        {
            if (playerRoot == null)
            {
                Debug.LogWarning("[PlayerOriginController] Cannot suggest spawn point: playerRoot is not assigned.");
                return false;
            }

            GameObject worldRoot = null;
            InteractionMemory memory = FindFirstObjectByType<InteractionMemory>(FindObjectsInactive.Include);
            if (memory != null)
                worldRoot = memory.currentWorldRoot;

            SplatSpawnPose spawnPose = worldRoot != null
                ? worldRoot.GetComponent<SplatSpawnPose>()
                : FindFirstObjectByType<SplatSpawnPose>(FindObjectsInactive.Include);
            if (spawnPose == null || !spawnPose.TryGetWorldPose(worldRoot != null ? worldRoot.transform : spawnPose.transform, out Vector3 position, out Quaternion rotation, out _))
            {
                Debug.LogWarning("[PlayerOriginController] No estimated spawn pose is available.");
                ArchStatusBus.Warning("No estimated spawn point is available.", "SPAWN");
                return false;
            }

            _currentSpawnIndex = -1;
            playerRoot.SetPositionAndRotation(position, rotation);
            Debug.Log($"[PlayerOriginController] Moved player to suggested spawn point ({spawnPose.method}, confidence={spawnPose.confidence:0.00}).");
            ArchStatusBus.Success("Moved to suggested spawn point.", "SPAWN");
            return true;
        }

        bool ApplySpawnPointStep(int step)
        {
            if (worldConfigAutoSave == null)
                worldConfigAutoSave = FindFirstObjectByType<WorldConfigAutoSave>(FindObjectsInactive.Include);

            WorldConfig config = worldConfigAutoSave != null ? worldConfigAutoSave.ActiveConfig : null;
            if (config?.spawn_points == null || config.spawn_points.Count == 0)
            {
                Debug.LogWarning("[PlayerOriginController] No saved spawn points for active world.");
                ArchStatusBus.Warning("No saved spawn points.", "SPAWN");
                return false;
            }

            if (playerRoot == null)
            {
                Debug.LogWarning("[PlayerOriginController] Cannot apply spawn point: playerRoot is not assigned.");
                return false;
            }

            int count = config.spawn_points.Count;
            if (_currentSpawnIndex < 0 || _currentSpawnIndex >= count)
                _currentSpawnIndex = 0;
            else
                _currentSpawnIndex = PositiveMod(_currentSpawnIndex + step, count);

            return ApplySpawnPoint(config.spawn_points[_currentSpawnIndex], _currentSpawnIndex, count);
        }

        bool TryResetToSavedSpawnPoint()
        {
            if (worldConfigAutoSave == null)
                worldConfigAutoSave = FindFirstObjectByType<WorldConfigAutoSave>(FindObjectsInactive.Include);

            WorldConfig config = worldConfigAutoSave != null ? worldConfigAutoSave.ActiveConfig : null;
            if (config?.spawn_points == null || config.spawn_points.Count == 0 || playerRoot == null)
                return false;

            _currentSpawnIndex = Mathf.Clamp(_currentSpawnIndex, 0, config.spawn_points.Count - 1);
            return ApplySpawnPoint(config.spawn_points[_currentSpawnIndex], _currentSpawnIndex, config.spawn_points.Count);
        }

        bool ApplySpawnPoint(SpawnPointData spawn, int index, int count)
        {
            if (spawn == null)
                return false;

            playerRoot.SetPositionAndRotation(spawn.position, spawn.rotation);
            string label = string.IsNullOrWhiteSpace(spawn.name) ? $"Spawn {index + 1}" : spawn.name;
            Debug.Log($"[PlayerOriginController] Moved player to spawn point {index + 1}/{count}: {label}.");
            ArchStatusBus.Success($"Spawn point {index + 1}/{count}: {label}.", "SPAWN");
            return true;
        }

        static int PositiveMod(int value, int modulus)
        {
            if (modulus <= 0)
                return 0;
            int result = value % modulus;
            return result < 0 ? result + modulus : result;
        }

        private bool TryResetToSpawnPose(GaussianSplatRenderer renderer)
        {
            if (playerRoot == null || renderer == null)
                return false;

            SplatSpawnPose spawnPose = renderer.GetComponent<SplatSpawnPose>();
            if (spawnPose == null || !spawnPose.hasPose)
                return false;

            Debug.Log($"[PlayerOriginController] Resetting player to splat spawn pose ({spawnPose.method}, confidence={spawnPose.confidence:0.00}).");
            playerRoot.SetPositionAndRotation(spawnPose.playerPosition, spawnPose.playerRotation);
            return true;
        }
    }
}
