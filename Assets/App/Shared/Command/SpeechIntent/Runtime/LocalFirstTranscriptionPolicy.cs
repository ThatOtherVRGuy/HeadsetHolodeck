using System;
using System.Collections.Generic;

namespace SpeechIntent
{
    /// <summary>What to do with a captured utterance, given whatever the local recogniser produced.</summary>
    public enum LocalTranscriptDecision
    {
        /// <summary>The local grammar understood it: act now, no network, no cost, no latency.</summary>
        AcceptLocal,

        /// <summary>Send the audio to the cloud transcriber.</summary>
        UseCloud
    }

    /// <summary>
    /// Decides whether a captured command can be handled locally or must go to the cloud transcriber.
    ///
    /// Local-first matters here because the device is often offline, the network is slow, and every cloud
    /// call costs money and a second of latency. The local recogniser already produces a transcript (that is
    /// how the wake word is detected), and the router already receives it as <c>fallbackText</c> — it was
    /// simply ignored whenever the cloud path was configured.
    ///
    /// The hard part is that sherpa-onnx exposes no confidence score through this Unity wrapper, so there is
    /// no probability to threshold. This policy uses what does exist: whether the reserved local grammar
    /// accepts the text, and whether the text is one of the junk transcriptions actually observed in the
    /// field (recorded in docs/xr/pico/WAKE-WORD-EVIDENCE.md). Free-form speech still goes to the cloud,
    /// because Whisper is simply better at it.
    /// </summary>
    public static class LocalFirstTranscriptionPolicy
    {
        /// <summary>
        /// Transcripts the local recogniser has actually produced from noise, captured from hardware and
        /// emulator logs. Accepting these as commands is what made the device feel broken; sending them to
        /// the cloud instead gives the user one more chance to be understood.
        /// </summary>
        static readonly HashSet<string> KnownJunk = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "well",
            "no",
            "what",
            "i",
            "there",
            "the",
            "he said",
            "i asked",
            "ode",
            "dudle",
            "louise",
            "neighbor",
            "webs something",
            "through a light shade",
            "mister joneson cut two word"
        };

        /// <summary>Shortest local transcript treated as worth acting on when the cloud is unavailable.</summary>
        public const int MinimumUsableLength = 3;

        /// <summary>
        /// Chooses the path for one utterance.
        /// </summary>
        /// <param name="localText">What the on-device recogniser produced, or empty when it produced nothing.</param>
        /// <param name="reason">Short, log-safe explanation of the decision.</param>
        public static LocalTranscriptDecision Decide(string localText, out string reason)
        {
            localText = Normalize(localText);

            if (localText.Length == 0)
            {
                reason = "local produced nothing";
                return LocalTranscriptDecision.UseCloud;
            }

            if (TryAcceptLocally(localText, out string grammarReason))
            {
                reason = grammarReason;
                return LocalTranscriptDecision.AcceptLocal;
            }

            if (IsKnownJunk(localText))
            {
                reason = "known junk transcription";
                return LocalTranscriptDecision.UseCloud;
            }

            reason = "free-form speech; cloud is more accurate";
            return LocalTranscriptDecision.UseCloud;
        }

        /// <summary>
        /// Whether the local transcript is worth acting on when the cloud path has already failed.
        ///
        /// This is the difference between "I heard you but could not reach the transcriber" (use the local
        /// text, the user gets a result) and "I heard nothing useful" (apologise honestly).
        /// </summary>
        public static bool IsUsableLocalFallback(string localText)
        {
            localText = Normalize(localText);
            if (localText.Length < MinimumUsableLength) return false;
            return !IsKnownJunk(localText);
        }

        /// <summary>True when the reserved local grammar understands the text, i.e. it never needed the cloud.</summary>
        public static bool TryAcceptLocally(string localText, out string reason)
        {
            reason = string.Empty;
            localText = Normalize(localText);
            if (localText.Length == 0) return false;

            if (!OpenAiSpeechIntentService.TryBuildLocalPreflightCommand(localText, out VoiceIntentCommand command))
                return false;

            if (command == null || command.intent == VoiceIntentType.Unknown)
                return false;

            reason = $"local grammar understood {command.intent}";
            return true;
        }

        public static bool IsKnownJunk(string localText)
        {
            return KnownJunk.Contains(Normalize(localText));
        }

        /// <summary>
        /// Trims and strips trailing punctuation. Speech recognisers emit "Load Elvin City." with a full stop,
        /// which would otherwise defeat both the junk list and the grammar's prefix matching.
        /// </summary>
        static string Normalize(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            return text.Trim().TrimEnd('.', '!', '?', ',', ';', ':').Trim();
        }
    }
}
