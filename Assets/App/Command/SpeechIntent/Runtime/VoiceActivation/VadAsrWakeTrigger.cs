using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using PonyuDev.SherpaOnnx.Asr.Offline;
using PonyuDev.SherpaOnnx.Asr.Offline.Engine;
using PonyuDev.SherpaOnnx.Common.Audio;
using PonyuDev.SherpaOnnx.Common.Audio.Config;
using PonyuDev.SherpaOnnx.Common.Platform;
using PonyuDev.SherpaOnnx.Vad;
using PonyuDev.SherpaOnnx.Vad.Config;
using PonyuDev.SherpaOnnx.Vad.Data;
using PonyuDev.SherpaOnnx.Vad.Engine;
using UnityEngine;

namespace SpeechIntent.VoiceActivation
{
    /// <summary>
    /// Current wake trigger implementation: microphone -> VAD -> offline ASR -> wake-word text match.
    /// Ponyu/Sherpa-specific types are intentionally contained in this adapter.
    /// </summary>
    public sealed class VadAsrWakeTrigger : MonoBehaviour, IWakeTrigger, IVoiceCommandRecognizer
    {
        [Header("Config")]
        public VoiceActivationConfig config;

        [Header("Microphone")]
        public string microphoneDeviceName = "";
        public bool requestMicrophonePermission = true;
        public bool requestDesktopMicrophoneAuthorization = true;
        [Min(0.5f)] public float microphoneStartTimeoutSeconds = 5f;
        public bool useEditorMicrophoneSampleRate = true;
        [Min(8000)] public int editorMicrophoneSampleRate = 48000;

        [Header("VAD Preflight")]
        [Tooltip("Quest and Editor builds need an ONNX VAD model. Leave this on unless you are intentionally targeting a native accelerator model.")]
        public bool requireOnnxVadModel = true;

        [Header("TTS Echo Rejection")]
        [Tooltip("Ignore ASR phrases while the local TTS voice is playing so the app does not wake itself.")]
        public bool suppressRecognitionWhileTtsSpeaking = true;

        [Header("Runtime")]
        [SerializeField] private bool isRunning;
        [SerializeField] private bool isListeningForCommand;
        [SerializeField] private string lastTranscript = "";
        [SerializeField] private string lastStatus = "";

        public event Action<WakeTriggerResult> WakeDetected;
        public event Action<string> StatusChanged;

        public bool IsRunning => isRunning;
        public bool IsListeningForCommand => isListeningForCommand;

        readonly ConcurrentQueue<RecognizedPhrase> _recognizedPhrases = new ConcurrentQueue<RecognizedPhrase>();

        CancellationTokenSource _runCts;
        MicrophoneSource _microphone;
        VadService _vad;
        AsrService _asr;
        float[] _vadWindow;
        int _vadWindowPosition;
        int _vadSampleRate = 16000;
        int _microphoneSampleRate = 16000;
        TaskCompletionSource<VoiceCommandRecognitionResult> _pendingCommand;
        CancellationTokenRegistration _pendingCommandCancellation;

        async void OnDisable()
        {
            await StopAsync();
        }

        async void OnDestroy()
        {
            await StopAsync();
        }

        void Update()
        {
            while (_recognizedPhrases.TryDequeue(out RecognizedPhrase phrase))
                HandleRecognizedPhrase(phrase);
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            if (isRunning)
            {
                Log("StartAsync ignored because wake trigger is already running.");
                return;
            }

            VoiceActivationConfig activeConfig = GetConfig();
            _runCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            CancellationToken ct = _runCts.Token;

            try
            {
                SetStatus(
                    $"Initializing voice activation. mode={activeConfig.activationMode}, " +
                    $"wakeWords=[{string.Join(", ", activeConfig.wakeWords)}], " +
                    $"match={activeConfig.wakeWordMatchMode}, inline={activeConfig.allowInlineCommands}");

                _vad = new VadService();
                _asr = new AsrService();

                Log("Checking Sherpa VAD settings before native initialization...");
                await ValidateVadSettingsAsync(ct);

                Log("Initializing Sherpa VAD service...");
                await _vad.InitializeAsync(ct: ct);
                ApplyVadSensitivity(activeConfig);
                Log(
                    $"VAD ready={_vad.IsReady}, profile={_vad.ActiveProfile?.profileName ?? "none"}, " +
                    $"sampleRate={_vad.ActiveProfile?.sampleRate ?? 0}, windowSize={_vad.WindowSize}, " +
                    $"threshold={_vad.ActiveProfile?.threshold ?? 0f:0.000}");

                Log("Initializing Sherpa ASR service...");
                await _asr.InitializeAsync(ct: ct);
                Log($"ASR ready={_asr.IsReady}, profile={_asr.ActiveProfile?.profileName ?? "none"}");

                if (!_vad.IsReady)
                    throw new InvalidOperationException("Sherpa VAD is not ready. Check VAD model installation and SHERPA_ONNX define.");
                if (!_asr.IsReady)
                    throw new InvalidOperationException("Sherpa ASR is not ready. Check ASR model installation and SHERPA_ONNX define.");

                _vadSampleRate = _vad.ActiveProfile != null ? _vad.ActiveProfile.sampleRate : 16000;
                _microphoneSampleRate = GetMicrophoneCaptureSampleRate(_vadSampleRate);

                MicrophoneSettingsData micSettings = new MicrophoneSettingsData
                {
                    sampleRate = _microphoneSampleRate,
                    clipLengthSec = 10,
                    micStartTimeoutSec = microphoneStartTimeoutSeconds
                };

                LogAvailableMicrophones();
                await RequestDesktopMicrophoneAuthorizationAsync(ct);
                _microphone = new MicrophoneSource(
                    micSettings,
                    string.IsNullOrWhiteSpace(microphoneDeviceName) ? null : microphoneDeviceName,
                    requestMicrophonePermission);

                _microphone.SamplesAvailable += HandleMicrophoneSamples;
                _microphone.SilenceDetected += HandleMicrophoneSilenceDetected;

                Log(
                    $"Starting microphone. requestedDevice='{(string.IsNullOrWhiteSpace(microphoneDeviceName) ? "<default>" : microphoneDeviceName)}', " +
                    $"captureRate={micSettings.sampleRate}, vadRate={_vadSampleRate}, " +
                    $"timeout={micSettings.micStartTimeoutSec:0.0}s, " +
                    $"requestPermission={requestMicrophonePermission}");
                bool started = await _microphone.StartRecordingAsync(ct);
                if (!started)
                {
                    throw new InvalidOperationException(
                        "Microphone failed to start. Check Quest microphone permission, global microphone privacy settings, " +
                        "and microphone device selection. If this happens on Quest after permission is granted, Unity's " +
                        "Microphone API may not be advancing and we should switch this path to native Android AudioRecord.");
                }
                Log(
                    $"Microphone started. actualDevice='{_microphone.DeviceName ?? "<default>"}', " +
                    $"sampleRate={_microphone.SampleRate}");

                _vadWindow = null;
                _vadWindowPosition = 0;
                isRunning = true;
                SetStatus("Listening for wake word.");
            }
            catch (OperationCanceledException)
            {
                SetStatus("Voice activation start canceled.");
                await StopAsync();
            }
            catch (Exception ex)
            {
                Debug.LogError("[VadAsrWakeTrigger] Start failed: " + ex.Message, this);
                SetStatus("Voice activation error: " + ex.Message);
                await StopAsync();
            }
        }

        public Task StopAsync()
        {
            Log($"StopAsync called. isRunning={isRunning}, hasMic={_microphone != null}, hasPendingCommand={_pendingCommand != null}");
            _runCts?.Cancel();
            _runCts?.Dispose();
            _runCts = null;

            CompletePendingCommand(new VoiceCommandRecognitionResult(false, "", "Voice activation stopped."));

            if (_microphone != null)
            {
                _microphone.SamplesAvailable -= HandleMicrophoneSamples;
                _microphone.SilenceDetected -= HandleMicrophoneSilenceDetected;
                _microphone.Dispose();
                _microphone = null;
            }

            _vad?.Dispose();
            _vad = null;

            _asr?.Dispose();
            _asr = null;

            _vadWindow = null;
            _vadWindowPosition = 0;
            _vadSampleRate = 16000;
            _microphoneSampleRate = 16000;
            isRunning = false;
            isListeningForCommand = false;
            SetStatus("Voice activation stopped.");
            return Task.CompletedTask;
        }

        public async Task<VoiceCommandRecognitionResult> ListenForCommandAsync(
            float timeoutSeconds,
            CancellationToken cancellationToken)
        {
            if (!isRunning)
            {
                Log("ListenForCommandAsync rejected because wake trigger is not running.");
                return new VoiceCommandRecognitionResult(false, "", "Wake trigger is not running.");
            }

            if (_pendingCommand != null)
            {
                Log("ListenForCommandAsync rejected because a command listen is already pending.");
                return new VoiceCommandRecognitionResult(false, "", "Already listening for a command.");
            }

            isListeningForCommand = true;
            SetStatus($"Listening for command. timeout={timeoutSeconds:0.00}s");

            _pendingCommand = new TaskCompletionSource<VoiceCommandRecognitionResult>();
            using CancellationTokenSource timeoutCts = new CancellationTokenSource(
                TimeSpan.FromSeconds(Mathf.Max(0.1f, timeoutSeconds)));
            using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeoutCts.Token);

            _pendingCommandCancellation = linkedCts.Token.Register(() =>
            {
                string error = timeoutCts.IsCancellationRequested
                    ? "No command heard before timeout."
                    : "Command listening canceled.";
                CompletePendingCommand(new VoiceCommandRecognitionResult(false, "", error));
            });

            VoiceCommandRecognitionResult result = await _pendingCommand.Task;
            isListeningForCommand = false;
            Log(
                $"Command listen completed. success={result.Success}, " +
                $"transcript='{result.Transcript}', error='{result.Error}'");
            return result;
        }

        void HandleMicrophoneSamples(float[] samples)
        {
            if (!isRunning || _vad == null || !_vad.IsReady || samples == null || samples.Length == 0)
                return;

            if (_microphoneSampleRate != _vadSampleRate)
                samples = ResampleLinear(samples, _microphoneSampleRate, _vadSampleRate);

            int windowSize = _vad.WindowSize;
            if (windowSize <= 0)
                return;

            if (_vadWindow == null || _vadWindow.Length != windowSize)
            {
                _vadWindow = new float[windowSize];
                _vadWindowPosition = 0;
            }

            for (int i = 0; i < samples.Length; i++)
            {
                _vadWindow[_vadWindowPosition++] = samples[i];
                if (_vadWindowPosition < windowSize)
                    continue;

                _vad.AcceptWaveform(_vadWindow);
                RecognizeDrainedSegments();
                _vadWindowPosition = 0;
            }
        }

        void RecognizeDrainedSegments()
        {
            if (_vad == null || _asr == null || !_asr.IsReady)
                return;

            foreach (VadSegment segment in _vad.DrainSegments())
            {
                Log(
                    $"VAD segment drained. duration={segment.Duration:0.00}s, " +
                    $"samples={segment.Samples?.Length ?? 0}");
                _ = RecognizeSegmentAsync(segment);
            }
        }

        async Task RecognizeSegmentAsync(VadSegment segment)
        {
            if (segment == null || segment.Samples == null || segment.Samples.Length == 0 || _asr == null)
                return;

            try
            {
                int sampleRate = _vad != null && _vad.ActiveProfile != null
                    ? _vad.ActiveProfile.sampleRate
                    : 16000;
                Log(
                    $"ASR recognizing segment. duration={segment.Duration:0.00}s, " +
                    $"sampleRate={sampleRate}, samples={segment.Samples.Length}");
                AsrResult result = await _asr.RecognizeAsync(segment.Samples, sampleRate);
                Log($"ASR result valid={result != null && result.IsValid}, text='{result?.Text ?? ""}'");
                if (result != null && result.IsValid)
                    _recognizedPhrases.Enqueue(new RecognizedPhrase(result.Text, segment.Duration));
            }
            catch (Exception ex)
            {
                SetStatus("ASR error: " + ex.Message);
            }
        }

        void HandleRecognizedPhrase(RecognizedPhrase phrase)
        {
            string transcript = (phrase.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(transcript))
                return;

            lastTranscript = transcript;
            Log($"ASR phrase dequeued. listeningForCommand={isListeningForCommand}, transcript='{transcript}'");

            if (suppressRecognitionWhileTtsSpeaking && global::TtsPlayer.IsVoiceActivationSuppressed)
            {
                Log($"Ignoring ASR phrase while TTS suppression is active: '{transcript}'");
                return;
            }

            if (isListeningForCommand && _pendingCommand != null)
            {
                Log("Treating recognized phrase as post-wake command.");
                CompletePendingCommand(new VoiceCommandRecognitionResult(true, transcript));
                return;
            }

            VoiceActivationConfig activeConfig = GetConfig();
            if (activeConfig.maxWakePhraseDurationSeconds > 0f &&
                phrase.DurationSeconds > activeConfig.maxWakePhraseDurationSeconds)
            {
                Log($"Ignoring long wake phrase ({phrase.DurationSeconds:0.00}s): {transcript}");
                return;
            }

            if (!TryMatchWakeWord(transcript, activeConfig, out string wakeWord, out string commandText))
            {
                Log($"Wake word not matched. transcript='{transcript}'");
                return;
            }

            bool hasInlineCommand = activeConfig.allowInlineCommands &&
                                    !string.IsNullOrWhiteSpace(commandText);
            Log(
                $"Wake word matched. wakeWord='{wakeWord}', inline={hasInlineCommand}, " +
                $"command='{(hasInlineCommand ? commandText : "")}'");
            WakeDetected?.Invoke(new WakeTriggerResult(
                wakeWord,
                transcript,
                hasInlineCommand ? commandText : "",
                1f,
                hasInlineCommand));
        }

        void CompletePendingCommand(VoiceCommandRecognitionResult result)
        {
            if (_pendingCommand == null)
                return;

            Log(
                $"Completing pending command. success={result.Success}, " +
                $"transcript='{result.Transcript}', error='{result.Error}'");
            TaskCompletionSource<VoiceCommandRecognitionResult> pending = _pendingCommand;
            _pendingCommand = null;
            _pendingCommandCancellation.Dispose();
            pending.TrySetResult(result);
        }

        void HandleMicrophoneSilenceDetected(string diagnosis)
        {
            SetStatus("Microphone silence detected: " + diagnosis);
        }

        void ApplyVadSensitivity(VoiceActivationConfig activeConfig)
        {
            if (_vad?.ActiveProfile == null)
                return;

            float threshold = Mathf.Clamp01(1f - activeConfig.vadSensitivity);
            _vad.ActiveProfile.threshold = Mathf.Clamp(threshold, 0.05f, 0.95f);
            _vad.LoadProfile(_vad.ActiveProfile);
        }

        void LogAvailableMicrophones()
        {
            string[] devices = Microphone.devices;
            if (devices == null || devices.Length == 0)
            {
                Log("Microphone.devices is empty.");
                return;
            }

            Log($"Microphone.devices=[{string.Join(", ", devices)}]");
        }

        int GetMicrophoneCaptureSampleRate(int vadSampleRate)
        {
#if UNITY_EDITOR
            if (useEditorMicrophoneSampleRate && editorMicrophoneSampleRate > 0)
                return editorMicrophoneSampleRate;
#endif
            return vadSampleRate;
        }

        static float[] ResampleLinear(float[] input, int inputRate, int outputRate)
        {
            if (input == null || input.Length == 0 || inputRate <= 0 || outputRate <= 0 || inputRate == outputRate)
                return input;

            int outputLength = Mathf.Max(1, Mathf.RoundToInt(input.Length * (outputRate / (float)inputRate)));
            float[] output = new float[outputLength];
            float step = (input.Length - 1) / Mathf.Max(1f, outputLength - 1f);

            for (int i = 0; i < outputLength; i++)
            {
                float sourcePosition = i * step;
                int sourceIndex = Mathf.FloorToInt(sourcePosition);
                int nextIndex = Mathf.Min(sourceIndex + 1, input.Length - 1);
                float t = sourcePosition - sourceIndex;
                output[i] = Mathf.Lerp(input[sourceIndex], input[nextIndex], t);
            }

            return output;
        }

        async UniTask RequestDesktopMicrophoneAuthorizationAsync(CancellationToken ct)
        {
            if (!requestMicrophonePermission || !requestDesktopMicrophoneAuthorization)
                return;

#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
            if (Application.HasUserAuthorization(UserAuthorization.Microphone))
            {
                Log("macOS microphone authorization is already granted.");
                return;
            }

            Log("Requesting macOS microphone authorization...");
            AsyncOperation authorization = Application.RequestUserAuthorization(UserAuthorization.Microphone);
            while (!authorization.isDone)
            {
                ct.ThrowIfCancellationRequested();
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }

            if (!Application.HasUserAuthorization(UserAuthorization.Microphone))
                throw new InvalidOperationException("macOS microphone authorization was not granted for Unity.");

            Log("macOS microphone authorization granted.");
#endif
        }

        async UniTask ValidateVadSettingsAsync(CancellationToken ct)
        {
            VadSettingsData settings = await VadSettingsLoader.LoadAsync(ct: ct);
            VadProfile activeProfile = VadSettingsLoader.GetActiveProfile(settings);
            if (activeProfile == null)
            {
                throw new InvalidOperationException(
                    "No active Sherpa VAD profile was found. Open Project Settings > Sherpa-ONNX > VAD and install/select an ONNX Silero VAD profile.");
            }

            string modelName = activeProfile.model ?? string.Empty;
            string extension = Path.GetExtension(modelName);
            Log(
                $"Active VAD profile: profile='{activeProfile.profileName}', model='{modelName}', " +
                $"extension='{extension}', provider='{activeProfile.provider}', sampleRate={activeProfile.sampleRate}");

            if (string.IsNullOrWhiteSpace(modelName))
            {
                throw new InvalidOperationException(
                    $"Active Sherpa VAD profile '{activeProfile.profileName}' has no model file assigned.");
            }

            if (requireOnnxVadModel && !string.Equals(extension, ".onnx", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Active Sherpa VAD profile '{activeProfile.profileName}' uses '{modelName}'. " +
                    "Headset Holodeck voice activation expects an ONNX VAD model for Mac Editor and Quest. " +
                    "Install/select silero_vad.onnx in Project Settings > Sherpa-ONNX > VAD.");
            }

            string resolvedModelPath = Path.Combine(
                StreamingAssetsCopier.GetResolvedStreamingAssetsPath(),
                "SherpaOnnx",
                "vad-models",
                activeProfile.profileName ?? string.Empty,
                modelName);

            if (!File.Exists(resolvedModelPath))
            {
                throw new InvalidOperationException(
                    $"Sherpa VAD model file was not found at '{resolvedModelPath}'. " +
                    "Install the ONNX VAD model and make sure it is included in StreamingAssets.");
            }
        }

        static bool TryMatchWakeWord(
            string transcript,
            VoiceActivationConfig activeConfig,
            out string wakeWord,
            out string commandText)
        {
            wakeWord = "";
            commandText = "";

            if (activeConfig == null || activeConfig.WakeWords == null)
                return false;

            foreach (string candidate in activeConfig.WakeWords)
            {
                string word = (candidate ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(word))
                    continue;

                if (!Matches(transcript, word, activeConfig.wakeWordMatchMode, out int start, out int length))
                    continue;

                wakeWord = word;
                commandText = StripWakeWord(transcript, start, length);
                return true;
            }

            return false;
        }

        static bool Matches(
            string transcript,
            string wakeWord,
            WakeWordMatchMode mode,
            out int start,
            out int length)
        {
            start = -1;
            length = 0;

            string pattern = @"\b" + Regex.Escape(wakeWord) + @"\b";
            RegexOptions options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
            Match match = Regex.Match(transcript, pattern, options);
            if (!match.Success)
                return false;

            bool allowed = mode switch
            {
                WakeWordMatchMode.Exact => IsOnlyWakeWord(transcript, match.Index, match.Length),
                WakeWordMatchMode.StartsWith => IsAtPhraseStart(transcript, match.Index),
                WakeWordMatchMode.Contains => true,
                _ => false
            };

            if (!allowed)
                return false;

            start = match.Index;
            length = match.Length;
            return true;
        }

        static bool IsAtPhraseStart(string transcript, int index)
        {
            for (int i = 0; i < index; i++)
            {
                if (!char.IsWhiteSpace(transcript[i]) && !char.IsPunctuation(transcript[i]))
                    return false;
            }

            return true;
        }

        static bool IsOnlyWakeWord(string transcript, int start, int length)
        {
            string before = transcript.Substring(0, start).Trim();
            string after = transcript.Substring(start + length).Trim();
            return string.IsNullOrEmpty(TrimCommandSeparators(before)) &&
                   string.IsNullOrEmpty(TrimCommandSeparators(after));
        }

        static string StripWakeWord(string transcript, int start, int length)
        {
            string after = transcript.Substring(start + length);
            return TrimCommandSeparators(after);
        }

        static string TrimCommandSeparators(string text)
        {
            return (text ?? string.Empty).Trim(' ', '\t', '\r', '\n', ',', '.', ':', ';', '-');
        }

        VoiceActivationConfig GetConfig()
        {
            if (config != null)
                return config;

            config = VoiceActivationConfig.CreateRuntimeDefault();
            return config;
        }

        void SetStatus(string status)
        {
            lastStatus = status ?? string.Empty;
            if (GetConfig().debugLogging)
                Debug.Log("[VadAsrWakeTrigger] " + lastStatus, this);
            StatusChanged?.Invoke(lastStatus);
        }

        void Log(string message)
        {
            if (GetConfig().debugLogging)
                Debug.Log("[VadAsrWakeTrigger] " + message, this);
        }

        readonly struct RecognizedPhrase
        {
            public RecognizedPhrase(string text, float durationSeconds)
            {
                Text = text;
                DurationSeconds = durationSeconds;
            }

            public string Text { get; }
            public float DurationSeconds { get; }
        }
    }
}
