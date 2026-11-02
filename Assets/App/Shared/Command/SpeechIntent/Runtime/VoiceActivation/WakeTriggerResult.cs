using System;

namespace SpeechIntent.VoiceActivation
{
    [Serializable]
    public sealed class WakeTriggerResult
    {
        public WakeTriggerResult(
            string wakeWord,
            string transcript,
            string commandText,
            float confidence,
            bool hasInlineCommand)
        {
            WakeWord = wakeWord ?? string.Empty;
            Transcript = transcript ?? string.Empty;
            CommandText = commandText ?? string.Empty;
            Confidence = confidence;
            HasInlineCommand = hasInlineCommand;
        }

        public string WakeWord { get; }
        public string Transcript { get; }
        public string CommandText { get; }
        public float Confidence { get; }
        public bool HasInlineCommand { get; }
    }
}
