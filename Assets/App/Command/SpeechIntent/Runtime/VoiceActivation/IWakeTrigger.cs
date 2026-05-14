using System;
using System.Threading;
using System.Threading.Tasks;

namespace SpeechIntent.VoiceActivation
{
    public interface IWakeTrigger
    {
        event Action<WakeTriggerResult> WakeDetected;
        event Action<string> StatusChanged;

        bool IsRunning { get; }

        Task StartAsync(CancellationToken cancellationToken);
        Task StopAsync();
    }
}
