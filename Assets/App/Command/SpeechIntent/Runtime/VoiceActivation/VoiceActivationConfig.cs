using System.Collections.Generic;
using UnityEngine;

namespace SpeechIntent.VoiceActivation
{
    [CreateAssetMenu(fileName = "VoiceActivationConfig", menuName = "Speech Intent/Voice Activation Config")]
    public sealed class VoiceActivationConfig : ScriptableObject
    {
        [Header("Wake")]
        public List<string> wakeWords = new List<string> { "computer" };
        public WakeWordMatchMode wakeWordMatchMode = WakeWordMatchMode.StartsWith;
        [Range(0f, 1f)] public float wakeConfidenceThreshold = 0f;
        public bool allowInlineCommands = true;

        [Header("Implementation")]
        public VoiceActivationMode activationMode = VoiceActivationMode.VadAsr;

        [Header("VAD / ASR")]
        [Range(0f, 1f)] public float vadSensitivity = 0.5f;
        [Tooltip("Maximum recognized wake phrase duration accepted before wake matching is ignored. 0 disables the duration check.")]
        public float maxWakePhraseDurationSeconds = 8f;

        [Header("Command Window")]
        public float commandListenTimeoutSeconds = 7f;
        public float cooldownSeconds = 0.75f;

        [Header("Diagnostics")]
        public bool debugLogging = true;

        public IReadOnlyList<string> WakeWords => wakeWords;

        public static VoiceActivationConfig CreateRuntimeDefault()
        {
            VoiceActivationConfig config = CreateInstance<VoiceActivationConfig>();
            config.wakeWords = new List<string> { "computer" };
            return config;
        }
    }
}
