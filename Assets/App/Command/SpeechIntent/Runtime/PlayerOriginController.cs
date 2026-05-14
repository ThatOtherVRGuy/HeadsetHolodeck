using GaussianSplatting.Runtime;
using Holodeck.Direct;
using UnityEngine;
using WorldLabs.Runtime;

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
            ResetToOrigin();
        }
    }
}
