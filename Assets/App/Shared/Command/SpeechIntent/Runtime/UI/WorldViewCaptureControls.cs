using Holodeck.Save;
using UnityEngine;

namespace SpeechIntent
{
    public sealed class WorldViewCaptureControls : MonoBehaviour
    {
        public WorldViewCaptureService captureService;

        void Awake()
        {
            ResolveService();
        }

        public void CaptureThumbnail()
        {
            ResolveService();
            if (captureService == null)
            {
                ArchStatusBus.Warning("World view capture service not assigned.", "CAPTURE");
                return;
            }

            captureService.CaptureThumbnail();
        }

        public void CapturePanorama()
        {
            ResolveService();
            if (captureService == null)
            {
                ArchStatusBus.Warning("World view capture service not assigned.", "CAPTURE");
                return;
            }

            captureService.CapturePanorama();
        }

        void ResolveService()
        {
            if (captureService == null)
                captureService = FindFirstObjectByType<WorldViewCaptureService>(FindObjectsInactive.Include);
        }
    }
}
