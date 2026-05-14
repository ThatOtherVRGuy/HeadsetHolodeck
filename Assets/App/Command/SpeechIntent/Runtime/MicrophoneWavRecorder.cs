using System;
using System.IO;
using UnityEngine;

namespace SpeechIntent
{
    public class MicrophoneWavRecorder : MonoBehaviour
    {
        [Header("Microphone")]
        public string selectedDevice = "";
        [Range(8000, 48000)] public int sampleRate = 16000;
        [Range(1, 30)] public int maxRecordSeconds = 10;

        public bool IsRecording => _clip != null && Microphone.IsRecording(_deviceToUse);

        private AudioClip _clip;
        private string _deviceToUse;

        public void BeginRecording()
        {
            if (IsRecording)
            {
                Debug.LogWarning("Recorder is already running.");
                return;
            }

            _deviceToUse = ResolveDevice();
            _clip = Microphone.Start(_deviceToUse, false, maxRecordSeconds, sampleRate);

            if (_clip == null)
            {
                Debug.LogError("Failed to start microphone recording.");
            }
        }

        public byte[] EndRecordingToWavBytes()
        {
            if (!IsRecording || _clip == null)
            {
                Debug.LogWarning("Recorder was not active.");
                return null;
            }

            int samplePosition = Microphone.GetPosition(_deviceToUse);
            Microphone.End(_deviceToUse);

            if (samplePosition <= 0)
            {
                _clip = null;
                Debug.LogWarning("No microphone samples were captured.");
                return null;
            }

            float[] samples = new float[samplePosition * _clip.channels];
            _clip.GetData(samples, 0);
            byte[] wavBytes = EncodeWav(samples, _clip.channels, _clip.frequency);

            _clip = null;
            return wavBytes;
        }

        private string ResolveDevice()
        {
            if (!string.IsNullOrWhiteSpace(selectedDevice))
            {
                return selectedDevice;
            }

            if (Microphone.devices == null || Microphone.devices.Length == 0)
            {
                return null;
            }

            return Microphone.devices[0];
        }

        private static byte[] EncodeWav(float[] samples, int channels, int sampleRate)
        {
            using MemoryStream stream = new MemoryStream();
            using BinaryWriter writer = new BinaryWriter(stream);

            int bytesPerSample = 2;
            int subChunk2Size = samples.Length * bytesPerSample;
            int chunkSize = 36 + subChunk2Size;
            int byteRate = sampleRate * channels * bytesPerSample;
            short blockAlign = (short)(channels * bytesPerSample);
            short bitsPerSample = 16;

            writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(chunkSize);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
            writer.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)channels);
            writer.Write(sampleRate);
            writer.Write(byteRate);
            writer.Write(blockAlign);
            writer.Write(bitsPerSample);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
            writer.Write(subChunk2Size);

            for (int i = 0; i < samples.Length; i++)
            {
                float clamped = Mathf.Clamp(samples[i], -1f, 1f);
                short intSample = (short)Mathf.RoundToInt(clamped * short.MaxValue);
                writer.Write(intSample);
            }

            writer.Flush();
            return stream.ToArray();
        }
    }
}
