using GaussianSplatting.Runtime;
using UnityEngine;
using WorldLabs.Runtime;

namespace Holodeck.State
{
    public sealed class HolodeckAudioFeedback : MonoBehaviour
    {
        [Header("Dependencies")]
        [SerializeField] private HolodeckStateMachine stateMachine;
        [SerializeField] private AudioSource audioSource;

        [Header("World Dependencies")]
        [SerializeField] private WorldLabsWorldManager worldManager;

        [Header("Clips")]
        [SerializeField] private AudioClip listeningClip;
        [SerializeField] private AudioClip heardClip;

        [Header("World Clips")]
        [SerializeField] private AudioClip panoramaLoadedClip;
        [SerializeField] private AudioClip splatLoadedClip;
        [SerializeField] private AudioClip failedToLoadClip;
        [SerializeField] private AudioClip splatDisabledClip;

        // Sentinel worldId emitted by WorldLabsWorldManager.RestoreDefaultWorld().
        private const string DefaultWorldId = "__default__";

        private void Awake()
        {
            if (stateMachine == null)
                Debug.LogError($"{nameof(HolodeckAudioFeedback)} is missing a HolodeckStateMachine.", this);

            if (audioSource == null)
                Debug.LogError($"{nameof(HolodeckAudioFeedback)} is missing an AudioSource.", this);
        }

        private void OnEnable()
        {
            if (stateMachine != null)
                stateMachine.StateChanged += HandleStateChanged;

            if (worldManager != null)
                worldManager.OnWorldLoaded += HandleWorldLoaded;
        }

        private void OnDisable()
        {
            if (stateMachine != null)
                stateMachine.StateChanged -= HandleStateChanged;

            if (worldManager != null)
                worldManager.OnWorldLoaded -= HandleWorldLoaded;
        }

        private void HandleStateChanged(HolodeckState previous, HolodeckState next)
        {
            switch (next)
            {
                case HolodeckState.ListeningForCommand:
                    if (listeningClip != null)
                        audioSource.PlayOneShot(listeningClip);
                    break;

                case HolodeckState.Interpreting:
                    if (heardClip != null)
                        audioSource.PlayOneShot(heardClip);
                    break;

                case HolodeckState.Ready:
                    if (splatLoadedClip != null)
                        audioSource.PlayOneShot(splatLoadedClip);
                    break;

                case HolodeckState.Error:
                    if (failedToLoadClip != null)
                        audioSource.PlayOneShot(failedToLoadClip);
                    break;
            }
        }

        private void HandleWorldLoaded(string worldId, GaussianSplatRenderer renderer)
        {
            if (worldId == DefaultWorldId && splatDisabledClip != null)
                audioSource.PlayOneShot(splatDisabledClip);
        }

        public void PlayListeningClip()       { if (listeningClip      != null) audioSource.PlayOneShot(listeningClip);      }
        public void PlayHeardClip()           { if (heardClip           != null) audioSource.PlayOneShot(heardClip);           }
        public void PlayPanoramaLoadedClip()  { if (panoramaLoadedClip  != null) audioSource.PlayOneShot(panoramaLoadedClip);  }
        public void PlaySplatLoadedClip()     { if (splatLoadedClip     != null) audioSource.PlayOneShot(splatLoadedClip);     }
        public void PlayFailedToLoadClip()    { if (failedToLoadClip    != null) audioSource.PlayOneShot(failedToLoadClip);    }
        public void PlaySplatDisabledClip()   { if (splatDisabledClip   != null) audioSource.PlayOneShot(splatDisabledClip);   }
    }
}
