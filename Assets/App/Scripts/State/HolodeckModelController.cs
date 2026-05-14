using GaussianSplatting.Runtime;
using UnityEngine;
using WorldLabs.Runtime;

namespace Holodeck.State
{
    public sealed class HolodeckModelController : MonoBehaviour
    {
        [Header("Dependencies")]
        [SerializeField] private WorldLabsWorldManager worldManager;
        [SerializeField] private AudioSource audioSource;

        [Header("Model")]
        [SerializeField] private GameObject holodeckModel;

        public GameObject HolodeckModel => holodeckModel;

        [Header("Clips")]
        [SerializeField] private AudioClip worldLoadingClip;

        // Sentinel worldId emitted by WorldLabsWorldManager for the built-in default asset.
        private const string DefaultWorldId = "__default__";

        private void Awake()
        {
            if (worldManager == null)
                Debug.LogError($"{nameof(HolodeckModelController)} is missing a WorldLabsWorldManager.", this);

            if (audioSource == null)
                Debug.LogError($"{nameof(HolodeckModelController)} is missing an AudioSource.", this);

            if (holodeckModel == null)
                Debug.LogError($"{nameof(HolodeckModelController)} is missing a holodeck model.", this);
        }

        private void OnEnable()
        {
            if (worldManager != null)
            {
                worldManager.OnWorldLoadStarted += HandleWorldLoadStarted;
                worldManager.OnWorldLoaded      += HandleWorldLoaded;
                worldManager.OnWorldLoadFailed  += HandleWorldLoadFailed;
            }
        }

        private void OnDisable()
        {
            if (worldManager != null)
            {
                worldManager.OnWorldLoadStarted -= HandleWorldLoadStarted;
                worldManager.OnWorldLoaded      -= HandleWorldLoaded;
                worldManager.OnWorldLoadFailed  -= HandleWorldLoadFailed;
            }
        }

        private void HandleWorldLoadStarted(string worldId)
        {
            if (worldId == DefaultWorldId) return;
            if (holodeckModel == null) return;

            holodeckModel.SetActive(true);

            if (audioSource != null && worldLoadingClip != null)
                audioSource.PlayOneShot(worldLoadingClip);
        }

        private void HandleWorldLoaded(string worldId, GaussianSplatRenderer renderer)
        {
            if (worldId == DefaultWorldId) return;
            if (holodeckModel == null) return;

            holodeckModel.SetActive(false);
        }

        private void HandleWorldLoadFailed(string worldId, string error)
        {
            if (holodeckModel == null) return;

            holodeckModel.SetActive(false);
        }
    }
}
