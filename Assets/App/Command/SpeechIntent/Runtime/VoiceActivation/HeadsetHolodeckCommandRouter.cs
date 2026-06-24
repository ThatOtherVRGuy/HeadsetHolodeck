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

            string normalizedCommandText = NormalizePostWakeAsrCommand(commandText);
            if (!string.Equals(normalizedCommandText, commandText, System.StringComparison.Ordinal))
            {
                Debug.Log(
                    $"[HeadsetHolodeckCommandRouter] Normalized post-wake ASR command from '{commandText}' to '{normalizedCommandText}'.",
                    this);
                commandText = normalizedCommandText;
            }

            Debug.Log($"[HeadsetHolodeckCommandRouter] Forwarding to VoiceCommandRouter.SubmitTypedCommand: '{commandText}'", this);
            voiceCommandRouter.SubmitTypedCommand(commandText);
        }

        public void HandleVoiceCommandAudio(byte[] wavBytes, string fallbackCommandText)
        {
            fallbackCommandText = NormalizePostWakeAsrCommand(fallbackCommandText);
            Debug.Log(
                $"[HeadsetHolodeckCommandRouter] HandleVoiceCommandAudio bytes={wavBytes?.Length ?? 0}, fallback='{fallbackCommandText}'",
                this);

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

            if (wavBytes == null || wavBytes.Length == 0)
            {
                Debug.LogWarning("[HeadsetHolodeckCommandRouter] No audio bytes for command; falling back to text.", this);
                HandleVoiceCommand(fallbackCommandText);
                return;
            }

            Debug.Log("[HeadsetHolodeckCommandRouter] Forwarding to VoiceCommandRouter.SubmitAudioCommand.", this);
            voiceCommandRouter.SubmitAudioCommand(wavBytes, fallbackCommandText);
        }

        public static string NormalizePostWakeAsrCommandForTests(string commandText)
        {
            return NormalizePostWakeAsrCommand(commandText);
        }

        static string NormalizePostWakeAsrCommand(string commandText)
        {
            string text = (commandText ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(text))
                return "";

            text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");
            if (LooksLikeAllCapsAsr(text))
                text = text.ToLowerInvariant();

            text = RewriteCommandPrefix(text, @"^(?<please>please\s+)?createa\b", "${please}create a");
            text = RewriteCommandPrefix(text, @"^(?<please>please\s+)?(?:read|reed)\s+an\b", "${please}create an");
            text = RewriteCommandPrefix(text, @"^(?<please>please\s+)?(?:read|reed)\s+a\b", "${please}create a");
            text = RewriteCommandPrefix(text, @"^(?<please>please\s+)?(?:he|we|you)\s+(?:ate|eight)\s+an\b", "${please}create an");
            text = RewriteCommandPrefix(text, @"^(?<please>please\s+)?(?:he|we|you)\s+(?:ate|eight)\s+a\b", "${please}create a");
            text = RewriteCommandPrefix(text, @"^(?<please>please\s+)?(?:made)\s+an\b", "${please}make an");
            text = RewriteCommandPrefix(text, @"^(?<please>please\s+)?(?:made)\s+a\b", "${please}make a");

            return text.Trim();
        }

        static string RewriteCommandPrefix(string text, string pattern, string replacement)
        {
            return System.Text.RegularExpressions.Regex.Replace(
                text,
                pattern,
                replacement,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        static bool LooksLikeAllCapsAsr(string text)
        {
            bool hasLetter = false;
            foreach (char c in text)
            {
                if (!char.IsLetter(c))
                    continue;

                hasLetter = true;
                if (char.IsLower(c))
                    return false;
            }

            return hasLetter;
        }
    }
}
