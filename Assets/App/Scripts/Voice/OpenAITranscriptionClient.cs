using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using Holodeck.Direct;

namespace Holodeck.Voice
{
    public sealed class OpenAITranscriptionClient : MonoBehaviour
    {
        [SerializeField] private bool logDebugMessages = true;
        [SerializeField] private int timeoutSeconds = 120;

        [Serializable]
        private sealed class TranscriptionResponse
        {
            public string text;
        }

        public IEnumerator TranscribeWav(
            byte[] wavBytes,
            Action<string> onSuccess,
            Action<string> onError)
        {
            string key = HolodeckDirectSecrets.ResolveOpenAiApiKey();
            if (string.IsNullOrWhiteSpace(key) || key.IndexOf("REPLACE_WITH", StringComparison.Ordinal) >= 0)
            {
                onError?.Invoke("OpenAI API key is missing. Set OPENAI_API_KEY in .env or the runtime .env file.");
                yield break;
            }

            if (wavBytes == null || wavBytes.Length == 0)
            {
                onError?.Invoke("No WAV data was provided for transcription.");
                yield break;
            }

            string url = $"{HolodeckDirectSecrets.OpenAiBaseUrl}/audio/transcriptions";

            List<IMultipartFormSection> formSections = new List<IMultipartFormSection>
            {
                new MultipartFormFileSection("file", wavBytes, "command.wav", "audio/wav"),
                new MultipartFormDataSection("model", HolodeckDirectSecrets.OpenAiTranscriptionModel),
                new MultipartFormDataSection("response_format", "json"),
                new MultipartFormDataSection("language", "en"),
                new MultipartFormDataSection("temperature", "0")
            };

            using (UnityWebRequest request = UnityWebRequest.Post(url, formSections))
            {
                request.timeout = timeoutSeconds;
                request.SetRequestHeader("Authorization", $"Bearer {key}");
                request.SetRequestHeader("Accept", "application/json");

                if (logDebugMessages)
                {
                    Debug.Log("Sending audio to OpenAI transcription endpoint.", this);
                }

                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    string body = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;
                    onError?.Invoke(
                        $"OpenAI transcription failed. HTTP={(long)request.responseCode}, Error={request.error}, Body={body}");
                    yield break;
                }

                if (request.downloadHandler == null)
                {
                    onError?.Invoke("OpenAI transcription returned no download handler.");
                    yield break;
                }

                string json = request.downloadHandler.text;
                TranscriptionResponse response = JsonUtility.FromJson<TranscriptionResponse>(json);

                if (response == null || string.IsNullOrWhiteSpace(response.text))
                {
                    onError?.Invoke($"OpenAI transcription returned an empty response: {json}");
                    yield break;
                }

                if (logDebugMessages)
                {
                    Debug.Log($"Transcription complete: {response.text}", this);
                }

                onSuccess?.Invoke(response.text.Trim());
            }
        }
    }
}
