using GaussianSplatting.Runtime;
using UnityEngine;
using WorldLabs.Runtime;

namespace SpeechIntent
{
    public class InteractionMemory : MonoBehaviour
    {
        [Header("World Manager")]
        [SerializeField] private WorldLabsWorldManager worldManager;

        [Header("Tracked references")]
        public GameObject currentWorldRoot;
        public GameObject lastCreatedObject;
        public GameObject lastModifiedTarget;
        public GameObject lastInteractedTarget;
        public GameObject currentSelection;

        [Header("Metadata")]
        [TextArea(2, 5)]
        public string currentWorldDescription = "Default world";

        public void RegisterCurrentWorld(GameObject worldRoot)
        {
            currentWorldRoot = worldRoot;
            if (worldRoot != null)
            {
                lastInteractedTarget = worldRoot;
                currentSelection = worldRoot;
            }
        }

        public void RegisterCurrentWorld(GameObject worldRoot, string description)
        {
            RegisterCurrentWorld(worldRoot);
            if (!string.IsNullOrWhiteSpace(description))
            {
                currentWorldDescription = description;
            }
        }

        public void RegisterCreatedObject(GameObject createdObject)
        {
            if (createdObject == null)
            {
                return;
            }

            lastCreatedObject = createdObject;
            lastModifiedTarget = createdObject;
            lastInteractedTarget = createdObject;
            currentSelection = createdObject;
        }

        public void RegisterModifiedTarget(GameObject target)
        {
            if (target == null)
            {
                return;
            }

            lastModifiedTarget = target;
            lastInteractedTarget = target;
            currentSelection = target;
        }

        public void RegisterInteraction(GameObject target)
        {
            if (target == null)
            {
                return;
            }

            lastInteractedTarget = target;
            currentSelection = target;
        }

        public void RegisterSelection(GameObject target)
        {
            currentSelection = target;
        }

        private void OnEnable()
        {
            if (worldManager != null)
            {
                worldManager.OnWorldLoaded   += HandleWorldLoaded;
                worldManager.OnWorldUnloaded += HandleWorldUnloaded;
            }
        }

        private void OnDisable()
        {
            if (worldManager != null)
            {
                worldManager.OnWorldLoaded   -= HandleWorldLoaded;
                worldManager.OnWorldUnloaded -= HandleWorldUnloaded;
            }
        }

        private void HandleWorldLoaded(string worldId, GaussianSplatRenderer renderer)
        {
            RegisterCurrentWorld(renderer != null ? renderer.gameObject : null);
        }

        private void HandleWorldUnloaded(string worldId)
        {
            if (currentWorldRoot != null)
                currentWorldRoot = null;
        }

        public GameObject GetLastCreatedOrInteracted()
        {
            CleanupDestroyedReferences();

            if (lastModifiedTarget != null)
            {
                return lastModifiedTarget;
            }

            if (lastInteractedTarget != null)
            {
                return lastInteractedTarget;
            }

            if (lastCreatedObject != null)
            {
                return lastCreatedObject;
            }

            if (currentSelection != null)
            {
                return currentSelection;
            }

            return currentWorldRoot;
        }

        private void Update()
        {
            CleanupDestroyedReferences();
        }

        private void CleanupDestroyedReferences()
        {
            if (currentWorldRoot == null)
            {
                currentWorldRoot = null;
            }

            if (lastCreatedObject == null)
            {
                lastCreatedObject = null;
            }

            if (lastModifiedTarget == null)
            {
                lastModifiedTarget = null;
            }

            if (lastInteractedTarget == null)
            {
                lastInteractedTarget = null;
            }

            if (currentSelection == null)
            {
                currentSelection = null;
            }
        }
    }
}
