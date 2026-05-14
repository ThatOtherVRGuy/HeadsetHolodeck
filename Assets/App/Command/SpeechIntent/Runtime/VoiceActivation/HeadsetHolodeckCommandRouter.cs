using UnityEngine;

namespace SpeechIntent.VoiceActivation
{
    /// <summary>
    /// App-level command bridge for hands-free voice activation.
    /// Today it forwards text into the existing SpeechIntent VoiceCommandRouter.
    /// Replace this body later if Headset Holodeck gains a different command entry point.
    /// </summary>
    public sealed class HeadsetHolodeckCommandRouter : MonoBehaviour
    {
        public VoiceCommandRouter voiceCommandRouter;

        public void HandleVoiceCommand(string commandText)
        {
            commandText = (commandText ?? string.Empty).Trim();
            Debug.Log($"[HeadsetHolodeckCommandRouter] HandleVoiceCommand '{commandText}'", this);
            if (string.IsNullOrWhiteSpace(commandText))
            {
                Debug.LogWarning("[HeadsetHolodeckCommandRouter] Ignoring empty voice command.", this);
                return;
            }

            if (voiceCommandRouter == null)
            {
                Debug.Log("[HeadsetHolodeckCommandRouter] VoiceCommandRouter reference missing; searching scene.", this);
                voiceCommandRouter = FindFirstObjectByType<VoiceCommandRouter>(FindObjectsInactive.Include);
            }

            if (voiceCommandRouter == null)
            {
                Debug.LogError("[HeadsetHolodeckCommandRouter] VoiceCommandRouter was not found.", this);
                return;
            }

            Debug.Log($"[HeadsetHolodeckCommandRouter] Forwarding to VoiceCommandRouter.SubmitTypedCommand: '{commandText}'", this);
            voiceCommandRouter.SubmitTypedCommand(commandText);
        }
    }
}
