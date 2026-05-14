using System;
using System.Collections;
using UnityEngine;

#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace Holodeck.Voice
{
    [Serializable]
    public sealed class VoiceCaptureResult
    {
        public VoiceCaptureResult(
            AudioClip clip,
            byte[] wavBytes,
            string deviceName,
            int sampleRate,
            int channels,
            float durationSeconds)
        {
            Clip = clip;
            WavBytes = wavBytes;
            DeviceName = deviceName;
            SampleRate = sampleRate;
            Channels = channels;
            DurationSeconds = durationSeconds;
        }

        public AudioClip Clip { get; }
        public byte[] WavBytes { get; }
        public string DeviceName { get; }
        public int SampleRate { get; }
        public int Channels { get; }
        public float DurationSeconds { get; }
    }

    public sealed class VoiceCaptureManager : MonoBehaviour
    {
        [Header("Recording Settings")]
        [SerializeField] private string preferredDeviceName = string.Empty;
        [SerializeField] private int maxRecordingSeconds = 15;
        [SerializeField] private int sampleRate = 16000;
        [SerializeField] private bool requestPermissionOnStart = true;
        [SerializeField] private bool logDebugMessages = true;

        [Header("Runtime Debug")]
        [SerializeField] private bool isCapturing;
        [SerializeField] private string activeDeviceDisplayName = string.Empty;
        [SerializeField, TextArea] private string lastFailureMessage = string.Empty;
        [SerializeField] private float lastCaptureDurationSeconds;

        private AudioClip _recordingClip;
        private string _apiDeviceName;
        private float _captureStartRealtime;
        private bool _permissionRequestInFlight;

        public bool IsCapturing => isCapturing;
        public string ActiveDeviceDisplayName => activeDeviceDisplayName;
        public string LastFailureMessage => lastFailureMessage;
        public float LastCaptureDurationSeconds => lastCaptureDurationSeconds;

        public event Action CaptureStarted;
        public event Action<VoiceCaptureResult> CaptureCompleted;
        public event Action<string> CaptureFailed;

        private void Start()
        {
            if (requestPermissionOnStart)
            {
                StartCoroutine(EnsureMicrophonePermissionCoroutine());
            }
        }

        public bool StartCapture()
        {
            if (isCapturing)
            {
                if (logDebugMessages)
                {
                    Debug.LogWarning("Voice capture is already in progress.", this);
                }

                return false;
            }

            if (_permissionRequestInFlight)
            {
                NotifyFailure("Microphone permission request is still pending.");
                return false;
            }

            if (!HasMicrophonePermission())
            {
                StartCoroutine(EnsureMicrophonePermissionCoroutine());
                NotifyFailure("Microphone permission is required before recording.");
                return false;
            }

            _apiDeviceName = ResolveDeviceNameForApi();
            activeDeviceDisplayName = string.IsNullOrWhiteSpace(_apiDeviceName) ? "<default>" : _apiDeviceName;

            _recordingClip = Microphone.Start(_apiDeviceName, false, maxRecordingSeconds, sampleRate);
            if (_recordingClip == null)
            {
                NotifyFailure("Microphone.Start returned null.");
                return false;
            }

            _captureStartRealtime = Time.realtimeSinceStartup;
            isCapturing = true;
            lastFailureMessage = string.Empty;
            lastCaptureDurationSeconds = 0f;

            if (logDebugMessages)
            {
                Debug.Log($"Voice capture started on device '{activeDeviceDisplayName}'.", this);
            }

            CaptureStarted?.Invoke();
            return true;
        }

        public VoiceCaptureResult StopCapture()
        {
            if (!isCapturing)
            {
                NotifyFailure("StopCapture was called but no recording is active.");
                return null;
            }

            int capturedFrames = Microphone.GetPosition(_apiDeviceName);

            if (Microphone.IsRecording(_apiDeviceName))
            {
                Microphone.End(_apiDeviceName);
            }

            isCapturing = false;

            if (_recordingClip == null)
            {
                NotifyFailure("Recording clip was unexpectedly null.");
                return null;
            }

            int fallbackFrames = Mathf.Clamp(
                Mathf.RoundToInt((Time.realtimeSinceStartup - _captureStartRealtime) * _recordingClip.frequency),
                0,
                _recordingClip.samples);

            if (capturedFrames <= 0)
            {
                capturedFrames = fallbackFrames;
            }

            capturedFrames = Mathf.Clamp(capturedFrames, 0, _recordingClip.samples);

            if (capturedFrames <= 0)
            {
                _recordingClip = null;
                NotifyFailure("No microphone samples were captured.");
                return null;
            }

            int channels = _recordingClip.channels;
            float[] sampleBuffer = new float[capturedFrames * channels];
            _recordingClip.GetData(sampleBuffer, 0);

            float rms = 0f;
            for (int i = 0; i < sampleBuffer.Length; i++)
            {
                rms += sampleBuffer[i] * sampleBuffer[i];
            }
            rms = Mathf.Sqrt(rms / sampleBuffer.Length);

            if (rms < 0.001f)
            {
                _recordingClip = null;
                NotifyFailure($"Captured audio is silent (RMS={rms:F6}). Check that the OS has granted microphone access to Unity.");
                return null;
            }

            AudioClip trimmedClip = AudioClip.Create(
                "HolodeckVoiceCommand",
                capturedFrames,
                channels,
                _recordingClip.frequency,
                false);

            trimmedClip.SetData(sampleBuffer, 0);

            byte[] wavBytes = WavUtility.FromAudioData(sampleBuffer, channels, _recordingClip.frequency);

            lastCaptureDurationSeconds = capturedFrames / (float)_recordingClip.frequency;

            VoiceCaptureResult result = new VoiceCaptureResult(
                trimmedClip,
                wavBytes,
                activeDeviceDisplayName,
                _recordingClip.frequency,
                channels,
                lastCaptureDurationSeconds);

            _recordingClip = null;

            if (logDebugMessages)
            {
                Debug.Log(
                    $"Voice capture completed. Duration={result.DurationSeconds:0.00}s, " +
                    $"SampleRate={result.SampleRate}, Channels={result.Channels}, Bytes={result.WavBytes.Length}",
                    this);
            }

            CaptureCompleted?.Invoke(result);
            return result;
        }

        private IEnumerator EnsureMicrophonePermissionCoroutine()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (Permission.HasUserAuthorizedPermission(Permission.Microphone))
            {
                yield break;
            }

            _permissionRequestInFlight = true;

            bool finished = false;
            bool granted = false;

            PermissionCallbacks callbacks = new PermissionCallbacks();
            callbacks.PermissionGranted += _ =>
            {
                granted = true;
                finished = true;
            };
            callbacks.PermissionDenied += _ =>
            {
                granted = false;
                finished = true;
            };
            callbacks.PermissionDeniedAndDontAskAgain += _ =>
            {
                granted = false;
                finished = true;
            };

            Permission.RequestUserPermission(Permission.Microphone, callbacks);

            float timeoutAt = Time.realtimeSinceStartup + 10f;
            while (!finished && Time.realtimeSinceStartup < timeoutAt)
            {
                yield return null;
            }

            _permissionRequestInFlight = false;

            if (!granted && !Permission.HasUserAuthorizedPermission(Permission.Microphone))
            {
                NotifyFailure("Microphone permission was denied.");
            }
#else
            yield break;
#endif
        }

        private bool HasMicrophonePermission()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return Permission.HasUserAuthorizedPermission(Permission.Microphone);
#else
            return true;
#endif
        }

        private string ResolveDeviceNameForApi()
        {
            if (!string.IsNullOrWhiteSpace(preferredDeviceName))
            {
                foreach (string device in Microphone.devices)
                {
                    if (string.Equals(device, preferredDeviceName, StringComparison.Ordinal))
                    {
                        return device;
                    }
                }
            }

            // Returning null uses the system's default microphone device.
            return null;
        }

        private void NotifyFailure(string message)
        {
            lastFailureMessage = message;
            if (logDebugMessages)
            {
                Debug.LogWarning(message, this);
            }

            CaptureFailed?.Invoke(message);
        }

        private static class WavUtility
        {
            public static byte[] FromAudioData(float[] samples, int channels, int sampleRate)
            {
                const short bitsPerSample = 16;
                int byteCount = samples.Length * 2;
                const int headerSize = 44;

                byte[] bytes = new byte[headerSize + byteCount];

                WriteString(bytes, 0, "RIFF");
                WriteInt(bytes, 4, 36 + byteCount);
                WriteString(bytes, 8, "WAVE");
                WriteString(bytes, 12, "fmt ");
                WriteInt(bytes, 16, 16);
                WriteShort(bytes, 20, 1);
                WriteShort(bytes, 22, (short)channels);
                WriteInt(bytes, 24, sampleRate);
                WriteInt(bytes, 28, sampleRate * channels * (bitsPerSample / 8));
                WriteShort(bytes, 32, (short)(channels * (bitsPerSample / 8)));
                WriteShort(bytes, 34, bitsPerSample);
                WriteString(bytes, 36, "data");
                WriteInt(bytes, 40, byteCount);

                int offset = 44;
                for (int i = 0; i < samples.Length; i++)
                {
                    short pcm = (short)Mathf.Clamp(samples[i] * short.MaxValue, short.MinValue, short.MaxValue);
                    bytes[offset++] = (byte)(pcm & 0xFF);
                    bytes[offset++] = (byte)((pcm >> 8) & 0xFF);
                }

                return bytes;
            }

            private static void WriteInt(byte[] bytes, int offset, int value)
            {
                bytes[offset + 0] = (byte)(value & 0xFF);
                bytes[offset + 1] = (byte)((value >> 8) & 0xFF);
                bytes[offset + 2] = (byte)((value >> 16) & 0xFF);
                bytes[offset + 3] = (byte)((value >> 24) & 0xFF);
            }

            private static void WriteShort(byte[] bytes, int offset, short value)
            {
                bytes[offset + 0] = (byte)(value & 0xFF);
                bytes[offset + 1] = (byte)((value >> 8) & 0xFF);
            }

            private static void WriteString(byte[] bytes, int offset, string value)
            {
                for (int i = 0; i < value.Length; i++)
                {
                    bytes[offset + i] = (byte)value[i];
                }
            }
        }
    }
}
