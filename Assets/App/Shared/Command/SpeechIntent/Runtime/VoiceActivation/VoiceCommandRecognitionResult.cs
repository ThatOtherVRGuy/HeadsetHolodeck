using System;

namespace SpeechIntent.VoiceActivation
{
    [Serializable]
    public sealed class VoiceCommandRecognitionResult
    {
        public VoiceCommandRecognitionResult(
            bool success,
            string transcript,
            string error = "",
            byte[] audioWavBytes = null)
        {
            Success = success;
            Transcript = transcript ?? string.Empty;
            Error = error ?? string.Empty;
            AudioWavBytes = audioWavBytes;
        }

        public bool Success { get; }
        public string Transcript { get; }
        public string Error { get; }
        public byte[] AudioWavBytes { get; }
        public bool HasAudio => AudioWavBytes != null && AudioWavBytes.Length > 0;
    }
}
