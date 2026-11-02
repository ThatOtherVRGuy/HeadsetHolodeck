using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;
using SpeechIntent;
using UnityEngine;
using UnityEngine.Networking;

namespace Holodeck.Direct
{
    public sealed class HitemObjectGenerationProvider : MonoBehaviour, IObjectGenerationProvider
    {
        const string DefaultBaseUrl = "https://api.hitem3d.ai";
        const int HitemFormatGlb = 2;

        [Header("Credentials")]
        [SerializeField] string accessKey = "";
        [SerializeField] string secretKey = "";
        [SerializeField] string baseUrl = DefaultBaseUrl;

        [Header("Generation")]
        [SerializeField] string model = "hitem3dv2.0";
        [SerializeField] string resolution = "512";
        [SerializeField] int requestType = 3;
        [SerializeField] int faceCount = 800000;
        [SerializeField] int pbr = 1;
        [SerializeField] int jpgQuality = 92;
        [SerializeField] int requestTimeoutSeconds = 60;
        [SerializeField] float pollIntervalSeconds = 5f;
        [SerializeField] float maxWaitSeconds = 600f;
        [SerializeField] bool debugLogging = true;

        string _accessToken = "";
        string _tokenType = "Bearer";

        public string ProviderName => "Hitem";

        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(ResolveAccessKey()) &&
            !string.IsNullOrWhiteSpace(ResolveSecretKey());

        public bool SupportsCapability(ObjectGenerationCapability capability)
        {
            return capability == ObjectGenerationCapability.ImageTo3D;
        }

        public ObjectGenerationCreditEstimate EstimateCredits(ObjectGenerationCapability capability)
        {
            return new ObjectGenerationCreditEstimate
            {
                known = false,
                requiredCredits = 0,
                description = "Hitem credit estimate is not available."
            };
        }

        public IEnumerator GenerateFromImage(ObjectGenerationRequest request, Action<ObjectGenerationResult> onComplete)
        {
            if (request == null || request.image == null)
            {
                Complete(onComplete, ObjectGenerationResult.Failed(ProviderName, "No image is available for object generation."));
                yield break;
            }

            if (!IsConfigured)
            {
                Complete(onComplete, ObjectGenerationResult.Failed(ProviderName, "Hitem credentials missing. Set HITEM_ACCESS_KEY and HITEM_SECRET_KEY."));
                yield break;
            }

            byte[] imageBytes;
            try
            {
                imageBytes = EncodeImage(request.image);
            }
            catch (Exception ex)
            {
                Complete(onComplete, ObjectGenerationResult.Failed(ProviderName, $"Could not encode image for Hitem: {ex.Message}"));
                yield break;
            }

            if (imageBytes == null || imageBytes.Length == 0)
            {
                Complete(onComplete, ObjectGenerationResult.Failed(ProviderName, "Encoded image was empty."));
                yield break;
            }

            string tokenError = null;
            yield return EnsureToken(error => tokenError = error);
            if (!string.IsNullOrWhiteSpace(tokenError))
            {
                Complete(onComplete, ObjectGenerationResult.Failed(ProviderName, tokenError));
                yield break;
            }

            string taskId = "";
            string submitError = null;
            yield return SubmitTask(request, imageBytes, (id, error) =>
            {
                taskId = id;
                submitError = error;
            });

            if (string.IsNullOrWhiteSpace(taskId))
            {
                Complete(onComplete, ObjectGenerationResult.Failed(ProviderName, submitError));
                yield break;
            }

            string modelUrl = "";
            string coverUrl = "";
            string queryError = null;
            yield return PollTask(taskId, (url, cover, error) =>
            {
                modelUrl = url;
                coverUrl = cover;
                queryError = error;
            });

            if (string.IsNullOrWhiteSpace(modelUrl))
            {
                Complete(onComplete, ObjectGenerationResult.Failed(ProviderName, queryError));
                yield break;
            }

            byte[] modelBytes = null;
            string downloadError = null;
            yield return DownloadModel(modelUrl, (bytes, error) =>
            {
                modelBytes = bytes;
                downloadError = error;
            });

            if (modelBytes == null || modelBytes.Length == 0)
            {
                Complete(onComplete, ObjectGenerationResult.Failed(ProviderName, downloadError));
                yield break;
            }

            Complete(onComplete, new ObjectGenerationResult
            {
                success = true,
                providerName = ProviderName,
                taskId = taskId,
                modelUrl = modelUrl,
                coverUrl = coverUrl,
                modelBytes = modelBytes
            });
        }

        public IEnumerator GenerateFromText(ObjectGenerationRequest request, Action<ObjectGenerationResult> onComplete)
        {
            Complete(onComplete, ObjectGenerationResult.Failed(ProviderName, "Hitem text-to-3D is not configured in this app. Use 3dAIStudio Tripo for text prompts."));
            yield break;
        }

        IEnumerator EnsureToken(Action<string> onComplete)
        {
            if (!string.IsNullOrWhiteSpace(_accessToken))
            {
                onComplete?.Invoke(null);
                yield break;
            }

            string credentials = $"{ResolveAccessKey()}:{ResolveSecretKey()}";
            string basic = Convert.ToBase64String(Encoding.UTF8.GetBytes(credentials));
            string url = $"{ResolveBaseUrl()}/open-api/v1/auth/token";

            using UnityWebRequest request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
            request.uploadHandler = new UploadHandlerRaw(Array.Empty<byte>());
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = requestTimeoutSeconds;
            request.SetRequestHeader("Authorization", $"Basic {basic}");
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Accept", "*/*");

            if (debugLogging)
                Debug.Log("[HitemObjectGenerationProvider] Requesting access token.", this);

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                onComplete?.Invoke(BuildHttpError("Hitem token request failed", request));
                yield break;
            }

            HitemTokenResponse response = ParseJson<HitemTokenResponse>(request.downloadHandler.text);
            if (response == null || response.code != 200 || response.data == null || string.IsNullOrWhiteSpace(response.data.accessToken))
            {
                onComplete?.Invoke($"Hitem token response was not usable: {ExtractMessage(request.downloadHandler.text)}");
                yield break;
            }

            _accessToken = response.data.accessToken;
            _tokenType = string.IsNullOrWhiteSpace(response.data.tokenType) ? "Bearer" : response.data.tokenType.Trim();
            onComplete?.Invoke(null);
        }

        IEnumerator SubmitTask(ObjectGenerationRequest requestData, byte[] imageBytes, Action<string, string> onComplete)
        {
            string url = $"{ResolveBaseUrl()}/open-api/v1/submit-task";
            string fileName = SanitizeFileName(requestData.fileName, "hitem_prompt.jpg");
            List<IMultipartFormSection> form = new List<IMultipartFormSection>
            {
                new MultipartFormFileSection("images", imageBytes, fileName, "image/jpeg"),
                new MultipartFormDataSection("request_type", Mathf.Clamp(requestType, 1, 3).ToString()),
                new MultipartFormDataSection("resolution", string.IsNullOrWhiteSpace(resolution) ? "512" : resolution.Trim()),
                new MultipartFormDataSection("face", Mathf.Clamp(faceCount, 100000, 2000000).ToString()),
                new MultipartFormDataSection("model", string.IsNullOrWhiteSpace(model) ? "hitem3dv2.0" : model.Trim()),
                new MultipartFormDataSection("format", HitemFormatGlb.ToString()),
                new MultipartFormDataSection("pbr", pbr == 0 ? "0" : "1")
            };

            using UnityWebRequest request = UnityWebRequest.Post(url, form);
            request.timeout = requestTimeoutSeconds;
            request.SetRequestHeader("Authorization", $"{_tokenType} {_accessToken}");
            request.SetRequestHeader("Accept", "*/*");

            ArchStatusBus.Info("Submitting object image to Hitem.", "OBJECT");
            if (debugLogging)
                Debug.Log($"[HitemObjectGenerationProvider] Submitting image-to-3D task. model={model}, resolution={resolution}, bytes={imageBytes.Length}", this);

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                onComplete?.Invoke(null, BuildHttpError("Hitem task submission failed", request));
                yield break;
            }

            HitemSubmitTaskResponse response = ParseJson<HitemSubmitTaskResponse>(request.downloadHandler.text);
            if (response == null || response.code != 200 || response.data == null || string.IsNullOrWhiteSpace(response.data.task_id))
            {
                onComplete?.Invoke(null, $"Hitem task submission was not accepted: {ExtractMessage(request.downloadHandler.text)}");
                yield break;
            }

            ArchStatusBus.Info($"Hitem object task submitted: {response.data.task_id}.", "OBJECT");
            Debug.Log($"[HitemObjectGenerationProvider] Task submitted. taskId={response.data.task_id}", this);
            onComplete?.Invoke(response.data.task_id, null);
        }

        IEnumerator PollTask(string taskId, Action<string, string, string> onComplete)
        {
            float deadline = Time.realtimeSinceStartup + maxWaitSeconds;
            while (Time.realtimeSinceStartup <= deadline)
            {
                string url = $"{ResolveBaseUrl()}/open-api/v1/query-task?task_id={UnityWebRequest.EscapeURL(taskId)}";
                using UnityWebRequest request = UnityWebRequest.Get(url);
                request.timeout = requestTimeoutSeconds;
                request.SetRequestHeader("Authorization", $"{_tokenType} {_accessToken}");
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("Accept", "*/*");

                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    onComplete?.Invoke(null, null, BuildHttpError("Hitem task query failed", request));
                    yield break;
                }

                HitemQueryTaskResponse response = ParseJson<HitemQueryTaskResponse>(request.downloadHandler.text);
                string state = response?.data?.state ?? "";
                Debug.Log($"[HitemObjectGenerationProvider] Task {taskId} state={state}", this);

                if (response == null || response.code != 200 || response.data == null)
                {
                    onComplete?.Invoke(null, null, $"Hitem task query returned an invalid response: {ExtractMessage(request.downloadHandler.text)}");
                    yield break;
                }

                if (string.Equals(state, "success", StringComparison.OrdinalIgnoreCase))
                {
                    onComplete?.Invoke(response.data.url, response.data.cover_url, null);
                    yield break;
                }

                if (string.Equals(state, "failed", StringComparison.OrdinalIgnoreCase))
                {
                    onComplete?.Invoke(null, null, $"Hitem generation failed: {response.msg}");
                    yield break;
                }

                ArchStatusBus.Info($"Hitem object generation: {state}.", "OBJECT");
                yield return new WaitForSecondsRealtime(Mathf.Max(1f, pollIntervalSeconds));
            }

            onComplete?.Invoke(null, null, "Hitem generation timed out.");
        }

        IEnumerator DownloadModel(string modelUrl, Action<byte[], string> onComplete)
        {
            ArchStatusBus.Info("Downloading generated object.", "OBJECT");
            using UnityWebRequest request = UnityWebRequest.Get(modelUrl);
            request.timeout = requestTimeoutSeconds;
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                onComplete?.Invoke(null, $"Generated object download failed: {request.error}");
                yield break;
            }

            onComplete?.Invoke(request.downloadHandler.data, null);
        }

        static string BuildHttpError(string prefix, UnityWebRequest request)
        {
            string status = request.responseCode > 0 ? $"HTTP {request.responseCode}" : request.error;
            string body = request.downloadHandler != null ? ExtractMessage(request.downloadHandler.text) : "";
            string retry = request.responseCode == 429 ? " Too many requests; wait a moment before trying again." : "";
            string billing = IsBillingOrCreditError(request.responseCode, body)
                ? " Check provider credits or billing before trying again."
                : "";
            return string.IsNullOrWhiteSpace(body)
                ? $"{prefix}: {status}.{retry}{billing}"
                : $"{prefix}: {status}. {body}.{retry}{billing}";
        }

        static bool IsBillingOrCreditError(long responseCode, string body)
        {
            if (responseCode == 402)
                return true;
            if (string.IsNullOrWhiteSpace(body))
                return false;
            return body.IndexOf("credit", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   body.IndexOf("credits", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   body.IndexOf("billing", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   body.IndexOf("payment", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   body.IndexOf("quota", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   body.IndexOf("insufficient", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        byte[] EncodeImage(Texture2D texture)
        {
            try
            {
                return ImageConversion.EncodeToJPG(texture, Mathf.Clamp(jpgQuality, 50, 100));
            }
            catch
            {
                Texture2D readable = MakeReadableCopy(texture);
                byte[] bytes = ImageConversion.EncodeToJPG(readable, Mathf.Clamp(jpgQuality, 50, 100));
                Destroy(readable);
                return bytes;
            }
        }

        static Texture2D MakeReadableCopy(Texture source)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture rt = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(source, rt);
            RenderTexture.active = rt;
            Texture2D copy = new Texture2D(source.width, source.height, TextureFormat.RGB24, false);
            copy.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
            copy.Apply(false, false);
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
            return copy;
        }

        string ResolveAccessKey()
        {
            if (!string.IsNullOrWhiteSpace(accessKey))
                return accessKey.Trim();
            return RuntimeDotEnv.GetEnvironmentOrDotEnv("HITEM_ACCESS_KEY");
        }

        string ResolveSecretKey()
        {
            if (!string.IsNullOrWhiteSpace(secretKey))
                return secretKey.Trim();
            return RuntimeDotEnv.GetEnvironmentOrDotEnv("HITEM_SECRET_KEY");
        }

        string ResolveBaseUrl()
        {
            string configured = RuntimeDotEnv.GetEnvironmentOrDotEnv("HITEM_BASE_URL");
            if (string.IsNullOrWhiteSpace(configured))
                configured = baseUrl;
            configured = string.IsNullOrWhiteSpace(configured) ? DefaultBaseUrl : configured.Trim();
            return configured.TrimEnd('/');
        }

        static T ParseJson<T>(string json) where T : class
        {
            try
            {
                return JsonConvert.DeserializeObject<T>(json);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[HitemObjectGenerationProvider] JSON parse failed: {ex.Message}");
                return null;
            }
        }

        static string ExtractMessage(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return "empty response";
            HitemBaseResponse response = ParseJson<HitemBaseResponse>(json);
            if (response != null && !string.IsNullOrWhiteSpace(response.msg))
                return response.msg;
            if (response != null && !string.IsNullOrWhiteSpace(response.message))
                return response.message;
            return json.Length > 240 ? json.Substring(0, 240) : json;
        }

        static void Complete(Action<ObjectGenerationResult> onComplete, ObjectGenerationResult result)
        {
            onComplete?.Invoke(result);
        }

        static string SanitizeFileName(string value, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value))
                return fallback;
            foreach (char c in System.IO.Path.GetInvalidFileNameChars())
                value = value.Replace(c, '_');
            if (!value.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) &&
                !value.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
            {
                value += ".jpg";
            }
            return value;
        }

        [Serializable]
        class HitemBaseResponse
        {
            public int code;
            public string msg;
            public string message;
        }

        [Serializable]
        sealed class HitemTokenResponse : HitemBaseResponse
        {
            public HitemTokenData data;
        }

        [Serializable]
        sealed class HitemTokenData
        {
            public string accessToken;
            public string tokenType;
        }

        [Serializable]
        sealed class HitemSubmitTaskResponse : HitemBaseResponse
        {
            public HitemSubmitTaskData data;
        }

        [Serializable]
        sealed class HitemSubmitTaskData
        {
            public string task_id;
        }

        [Serializable]
        sealed class HitemQueryTaskResponse : HitemBaseResponse
        {
            public HitemQueryTaskData data;
        }

        [Serializable]
        sealed class HitemQueryTaskData
        {
            public string task_id;
            public string state;
            public string id;
            public string url;
            public string cover_url;
        }
    }
}
