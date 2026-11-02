using System;
using System.Threading;
using System.Threading.Tasks;

namespace SpeechIntent.VoiceActivation
{
    public interface IVoiceCommandRecognizer
    {
        event Action<string> StatusChanged;

        bool IsListeningForCommand { get; }

        Task<VoiceCommandRecognitionResult> ListenForCommandAsync(
            float timeoutSeconds,
            CancellationToken cancellationToken);
    }
}
