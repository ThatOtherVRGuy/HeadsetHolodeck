using System;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;

namespace SpeechIntent.VoiceActivation
{
    public sealed class HeadsetHolodeckVoiceController : MonoBehaviour
    {
        [Header("Config")]
        public VoiceActivationConfig config;
        public bool startOnEnable = true;
        [Tooltip("Quest can emit short pause/focus transitions during startup. Voice activation stops only if pause persists past this delay.")]
        [Min(0f)] public float pauseStopDelaySeconds = 2f;

        [Header("Implementations")]
        [Tooltip("Assign a MonoBehaviour that implements IWakeTrigger. If empty, the controller selects VadAsrWakeTrigger or KwsWakeTrigger from config.")]
        public MonoBehaviour wakeTriggerBehaviour;
        [Tooltip("Assign a MonoBehaviour that implements IVoiceCommandRecognizer. If empty, the wake trigger is used when it also implements that interface.")]
        public MonoBehaviour commandRecognizerBehaviour;

        [Header("Command Pipeline")]
        public HeadsetHolodeckCommandRouter commandRouter;

        [Header("Status UI")]
        public TextMeshProUGUI statusText;

        [Header("Audio Cues")]
        public AudioSource audioSource;
        public AudioClip wakeDetectedClip;
        public AudioClip listeningClip;
        public AudioClip commandAcceptedClip;
        public AudioClip errorClip;

        [Header("Runtime")]
        [SerializeField] private VoiceListeningState state = VoiceListeningState.Disabled;
        [SerializeField] private string lastCommandText = "";
        [SerializeField] private string lastStatus = "";

        IWakeTrigger _wakeTrigger;
        IVoiceCommandRecognizer _commandRecognizer;
        CancellationTokenSource _lifetimeCts;
        CancellationTokenSource _pauseCts;
        bool _wasRunningBeforePause;
        bool _isStarting;

        public VoiceListeningState State => state;

        async void OnEnable()
        {
            Log($"OnEnable. startOnEnable={startOnEnable}, state={state}");
            if (startOnEnable)
                await StartVoiceActivationAsync();
        }

        async void OnDisable()
        {
            Log("OnDisable.");
            await StopVoiceActivationAsync();
        }

        async void OnDestroy()
        {
            Log("OnDestroy.");
            await StopVoiceActivationAsync();
        }

        async void OnApplicationPause(bool pause)
        {
            Log($"OnApplicationPause({pause}). state={state}");
            if (pause)
            {
                _wasRunningBeforePause = state != VoiceListeningState.Disabled || _isStarting;
                SchedulePauseStop();
                return;
            }

            CancelPauseStop();
            if (_wasRunningBeforePause && isActiveAndEnabled)
            {
                _wasRunningBeforePause = false;
                await StartVoiceActivationAsync();
            }
        }

        [ContextMenu("Start Voice Activation")]
        public async void StartVoiceActivationFromInspector()
        {
            await StartVoiceActivationAsync();
        }

        [ContextMenu("Stop Voice Activation")]
        public async void StopVoiceActivationFromInspector()
        {
            await StopVoiceActivationAsync();
        }

        public async Task StartVoiceActivationAsync()
        {
            if (state != VoiceListeningState.Disabled && state != VoiceListeningState.Error)
            {
                Log($"StartVoiceActivationAsync ignored because state={state}.");
                return;
            }

            _isStarting = true;
            VoiceActivationConfig activeConfig = GetConfig();
            Log(
                $"StartVoiceActivationAsync. mode={activeConfig.activationMode}, " +
                $"wakeWords=[{string.Join(", ", activeConfig.wakeWords)}]");
            ResolveReferences(activeConfig);
            Log(
                $"Resolved references. wakeTrigger={_wakeTrigger?.GetType().Name ?? "null"}, " +
                $"commandRecognizer={_commandRecognizer?.GetType().Name ?? "null"}, " +
                $"commandRouter={(commandRouter != null ? commandRouter.name : "null")}");

            if (_wakeTrigger == null)
            {
                SetError("No wake trigger assigned or found.");
                return;
            }

            if (commandRouter == null)
                commandRouter = FindFirstObjectByType<HeadsetHolodeckCommandRouter>(FindObjectsInactive.Include);

            if (commandRouter == null)
            {
                SetError("No HeadsetHolodeckCommandRouter assigned or found.");
                return;
            }

            _lifetimeCts = new CancellationTokenSource();
            _wakeTrigger.WakeDetected += HandleWakeDetected;
            _wakeTrigger.StatusChanged += HandleSubsystemStatusChanged;
            if (_commandRecognizer != null && !ReferenceEquals(_commandRecognizer, _wakeTrigger))
                _commandRecognizer.StatusChanged += HandleSubsystemStatusChanged;

            try
            {
                SetState(VoiceListeningState.ListeningForWake, "Starting wake listener...");
                await _wakeTrigger.StartAsync(_lifetimeCts.Token);
                if (_lifetimeCts == null || _lifetimeCts.IsCancellationRequested)
                    return;
                if (!_wakeTrigger.IsRunning)
                {
                    SetError("Wake listener stopped during startup.");
                    return;
                }
                SetState(VoiceListeningState.ListeningForWake, "Listening for \"" + FirstWakeWord(activeConfig) + "\".");
            }
            catch (Exception ex)
            {
                SetError("Voice activation failed: " + ex.Message);
                await StopVoiceActivationAsync();
            }
            finally
            {
                _isStarting = false;
            }
        }

        public async Task StopVoiceActivationAsync()
        {
            CancelPauseStop();
            Log(
                $"StopVoiceActivationAsync. state={state}, " +
                $"wakeTrigger={_wakeTrigger?.GetType().Name ?? "null"}");
            _lifetimeCts?.Cancel();

            if (_wakeTrigger != null)
            {
                _wakeTrigger.WakeDetected -= HandleWakeDetected;
                _wakeTrigger.StatusChanged -= HandleSubsystemStatusChanged;
                await _wakeTrigger.StopAsync();
            }

            if (_commandRecognizer != null && !ReferenceEquals(_commandRecognizer, _wakeTrigger))
                _commandRecognizer.StatusChanged -= HandleSubsystemStatusChanged;

            _lifetimeCts?.Dispose();
            _lifetimeCts = null;
            _isStarting = false;
            SetState(VoiceListeningState.Disabled, "Voice activation disabled.");
        }

        async void HandleWakeDetected(WakeTriggerResult result)
        {
            Log(
                $"WakeDetected event. state={state}, wake='{result?.WakeWord ?? ""}', " +
                $"transcript='{result?.Transcript ?? ""}', inline={result?.HasInlineCommand ?? false}, " +
                $"command='{result?.CommandText ?? ""}'");
            if (state != VoiceListeningState.ListeningForWake)
            {
                Log("Ignoring wake while state is " + state + ".");
                return;
            }

            SetState(VoiceListeningState.WakeDetected, "Wake detected.");
            PlayCue(wakeDetectedClip);

            string inlineCommand = result != null && result.HasInlineCommand
                ? result.CommandText
                : "";

            if (!string.IsNullOrWhiteSpace(inlineCommand))
            {
                Log("Inline command present; routing immediately.");
                await ProcessCommandAsync(inlineCommand);
                return;
            }

            if (_commandRecognizer == null)
            {
                SetError("Wake detected, but no command recognizer is assigned.");
                await EnterCooldownAsync();
                return;
            }

            SetState(VoiceListeningState.ListeningForCommand, "Listening...");
            PlayCue(listeningClip);

            VoiceCommandRecognitionResult commandResult = await _commandRecognizer.ListenForCommandAsync(
                GetConfig().commandListenTimeoutSeconds,
                _lifetimeCts != null ? _lifetimeCts.Token : CancellationToken.None);
            Log(
                $"Command recognizer returned. success={commandResult?.Success ?? false}, " +
                $"transcript='{commandResult?.Transcript ?? ""}', error='{commandResult?.Error ?? ""}'");

            if (commandResult == null || !commandResult.Success || string.IsNullOrWhiteSpace(commandResult.Transcript))
            {
                SetError(commandResult != null ? commandResult.Error : "No command recognized.");
                PlayCue(errorClip);
                await EnterCooldownAsync();
                return;
            }

            await ProcessCommandAsync(commandResult.Transcript);
        }

        async Task ProcessCommandAsync(string commandText)
        {
            commandText = (commandText ?? string.Empty).Trim();
            Log($"ProcessCommandAsync('{commandText}')");
            if (string.IsNullOrWhiteSpace(commandText))
            {
                SetError("Empty command after wake word.");
                await EnterCooldownAsync();
                return;
            }

            SetState(VoiceListeningState.ProcessingCommand, "Processing: " + commandText);
            lastCommandText = commandText;
            PlayCue(commandAcceptedClip);
            Log("Forwarding command to HeadsetHolodeckCommandRouter.");
            commandRouter.HandleVoiceCommand(commandText);
            await EnterCooldownAsync();
        }

        async Task EnterCooldownAsync()
        {
            SetState(VoiceListeningState.Cooldown, "Voice cooldown.");

            float cooldown = Mathf.Max(0f, GetConfig().cooldownSeconds);
            Log($"Entering cooldown for {cooldown:0.00}s.");
            if (cooldown > 0f)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(cooldown), _lifetimeCts != null ? _lifetimeCts.Token : CancellationToken.None);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }

            if (_lifetimeCts == null || _lifetimeCts.IsCancellationRequested)
                return;

            SetState(VoiceListeningState.ListeningForWake, "Listening for \"" + FirstWakeWord(GetConfig()) + "\".");
        }

        void ResolveReferences(VoiceActivationConfig activeConfig)
        {
            Log(
                $"ResolveReferences. assignedWake={(wakeTriggerBehaviour != null ? wakeTriggerBehaviour.name : "null")}, " +
                $"assignedRecognizer={(commandRecognizerBehaviour != null ? commandRecognizerBehaviour.name : "null")}, " +
                $"mode={activeConfig.activationMode}");
            _wakeTrigger = wakeTriggerBehaviour as IWakeTrigger;
            _commandRecognizer = commandRecognizerBehaviour as IVoiceCommandRecognizer;

            if (_wakeTrigger == null)
            {
                if (activeConfig.activationMode == VoiceActivationMode.Kws)
                    _wakeTrigger = FindFirstObjectByType<KwsWakeTrigger>(FindObjectsInactive.Include);
                else
                    _wakeTrigger = FindFirstObjectByType<VadAsrWakeTrigger>(FindObjectsInactive.Include);
            }

            if (_commandRecognizer == null && _wakeTrigger is IVoiceCommandRecognizer recognizer)
                _commandRecognizer = recognizer;

            if (_commandRecognizer == null)
                _commandRecognizer = FindFirstObjectByType<VadAsrWakeTrigger>(FindObjectsInactive.Include);
        }

        void HandleSubsystemStatusChanged(string status)
        {
            if (string.IsNullOrWhiteSpace(status))
                return;

            lastStatus = status;
            if (statusText != null)
                statusText.text = status;
            Log(status);
        }

        void SetState(VoiceListeningState nextState, string status)
        {
            state = nextState;
            lastStatus = status ?? string.Empty;
            if (statusText != null)
                statusText.text = lastStatus;
            Log(state + ": " + lastStatus);
        }

        void SetError(string error)
        {
            SetState(VoiceListeningState.Error, error);
            PlayCue(errorClip);
        }

        void SchedulePauseStop()
        {
            CancelPauseStop();
            _pauseCts = new CancellationTokenSource();
            _ = StopAfterPauseDelayAsync(_pauseCts.Token);
        }

        void CancelPauseStop()
        {
            if (_pauseCts == null)
                return;

            _pauseCts.Cancel();
            _pauseCts.Dispose();
            _pauseCts = null;
        }

        async Task StopAfterPauseDelayAsync(CancellationToken cancellationToken)
        {
            float delay = Mathf.Max(0f, pauseStopDelaySeconds);
            Log($"Pause stop scheduled in {delay:0.00}s.");
            try
            {
                if (delay > 0f)
                    await Task.Delay(TimeSpan.FromSeconds(delay), cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException)
            {
                Log("Pause stop canceled.");
                return;
            }

            Log("Pause persisted; stopping voice activation.");
            await StopVoiceActivationAsync();
        }

        void PlayCue(AudioClip clip)
        {
            if (audioSource == null || clip == null)
                return;

            audioSource.PlayOneShot(clip);
        }

        VoiceActivationConfig GetConfig()
        {
            if (config != null)
                return config;

            config = VoiceActivationConfig.CreateRuntimeDefault();
            return config;
        }

        static string FirstWakeWord(VoiceActivationConfig activeConfig)
        {
            if (activeConfig != null &&
                activeConfig.wakeWords != null &&
                activeConfig.wakeWords.Count > 0 &&
                !string.IsNullOrWhiteSpace(activeConfig.wakeWords[0]))
                return activeConfig.wakeWords[0];

            return "Computer";
        }

        void Log(string message)
        {
            if (GetConfig().debugLogging)
                Debug.Log("[HeadsetHolodeckVoiceController] " + message, this);
        }
    }
}
