using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace SpeechIntent.VoiceActivation
{
    /// <summary>
    /// Placeholder for a future native sherpa-onnx keyword spotting bridge.
    /// It exposes the same app-level wake contract as VadAsrWakeTrigger so the
    /// controller and command pipeline do not change when true KWS lands.
    /// </summary>
    public sealed class KwsWakeTrigger : MonoBehaviour, IWakeTrigger
    {
        public VoiceActivationConfig config;
        [SerializeField] private bool isRunning;
        [SerializeField] private string lastStatus = "";

        public event Action<WakeTriggerResult> WakeDetected;
        public event Action<string> StatusChanged;

        public bool IsRunning => isRunning;

        public Task StartAsync(CancellationToken cancellationToken)
        {
            isRunning = true;
            SetStatus("KWS wake trigger placeholder running. Add native keyword spotting bridge here.");
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            isRunning = false;
            SetStatus("KWS wake trigger stopped.");
            return Task.CompletedTask;
        }

        [ContextMenu("Simulate Wake")]
        public void SimulateWake()
        {
            if (!isRunning)
                return;

            string wakeWord = config != null && config.wakeWords != null && config.wakeWords.Count > 0
                ? config.wakeWords[0]
                : "computer";
            WakeDetected?.Invoke(new WakeTriggerResult(wakeWord, wakeWord, "", 1f, false));
        }

        void OnDisable()
        {
            _ = StopAsync();
        }

        void SetStatus(string status)
        {
            lastStatus = status ?? string.Empty;
            if (config == null || config.debugLogging)
                Debug.Log("[KwsWakeTrigger] " + lastStatus, this);
            StatusChanged?.Invoke(lastStatus);
        }
    }
}
