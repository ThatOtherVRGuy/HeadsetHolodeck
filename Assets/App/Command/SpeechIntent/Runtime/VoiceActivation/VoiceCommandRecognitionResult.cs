using System;

namespace SpeechIntent.VoiceActivation
{
    [Serializable]
    public sealed class VoiceCommandRecognitionResult
    {
        public VoiceCommandRecognitionResult(
            bool success,
            string transcript,
            string error = "")
        {
            Success = success;
            Transcript = transcript ?? string.Empty;
            Error = error ?? string.Empty;
        }

        public bool Success { get; }
        public string Transcript { get; }
        public string Error { get; }
    }
}
