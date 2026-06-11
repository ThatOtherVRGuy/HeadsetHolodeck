using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SpeechIntent;
using UnityEngine;
using UnityEngine.Networking;

namespace Holodeck.Direct
{
    public sealed class ThreeDAIStudioObjectGenerationProvider : MonoBehaviour, IObjectGenerationProvider
    {
        const string DefaultBaseUrl = "https://api.3daistudio.com";

        [Header("Credentials")]
        [SerializeField] string apiKey = "";
        [SerializeField] string baseUrl = DefaultBaseUrl;

        [Header("Tripo Image To 3D")]
        [SerializeField] bool forceTexturedOutput = true;
        [SerializeField] bool texture = true;
        [SerializeField] bool pbr = true;
        [SerializeField] string textureQuality = "standard";
        [SerializeField] string geometryQuality = "standard";
        [SerializeField] int jpgQuality = 92;
        [SerializeField] int requestTimeoutSeconds = 60;
        [SerializeField] float pollIntervalSeconds = 5f;
        [SerializeField] float maxWaitSeconds = 600f;
        [SerializeField] int finishedMissingUrlRetryCount = 6;
        [SerializeField] bool debugLogging = true;
        [SerializeField] bool writeRawResponseDiagnostics = true;

        public string ProviderName => "3dAIStudio Tripo";
        public bool IsConfigured => !string.IsNullOrWhiteSpace(ResolveApiKey());

        public bool SupportsCapability(ObjectGenerationCapability capability)
        {
            return capability == ObjectGenerationCapability.ImageTo3D ||
                   capability == ObjectGenerationCapability.TextTo3D;
        }

        public ObjectGenerationCreditEstimate EstimateCredits(ObjectGenerationCapability capability)
        {
            int credits = capability == ObjectGenerationCapability.TextTo3D ? 20 : 40;
            if (texture)
                credits += string.Equals(textureQuality, "detailed", StringComparison.OrdinalIgnoreCase) ? 40 : 20;
            if (string.Equals(geometryQuality, "detailed", StringComparison.OrdinalIgnoreCase))
                credits += 40;

            return new ObjectGenerationCreditEstimate
            {
                known = true,
                requiredCredits = credits,
                description = capability == ObjectGenerationCapability.TextTo3D
                    ? "3dAIStudio Tripo text-to-3D"
                    : "3dAIStudio Tripo image-to-3D"
            };
        }

        public IEnumerator GenerateFromImage(ObjectGenerationRequest request, Action<ObjectGenerationResult> onComplete)
        {
            if (request == null || request.image == null)
            {
                Complete(onComplete, ObjectGenerationResult.Failed(ProviderName, "No image is available for object generation."));
                yield break;
            }

            string key = ResolveApiKey();
            if (string.IsNullOrWhiteSpace(key))
            {
                Complete(onComplete, ObjectGenerationResult.Failed(ProviderName, "3dAIStudio API key missing. Set THREEDAISTUDIO_API_KEY."));
                yield break;
            }

            string dataUri;
            try
            {
                dataUri = EncodeImageDataUri(request.image);
            }
            catch (Exception ex)
            {
                Complete(onComplete, ObjectGenerationResult.Failed(ProviderName, $"Could not encode image for 3dAIStudio: {ex.Message}"));
                yield break;
            }

            string taskId = "";
            string submitError = null;
            yield return SubmitTask(key, dataUri, (id, error) =>
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
            yield return PollTask(key, taskId, (url, cover, error) =>
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
            string prompt = request != null ? request.prompt : "";
            if (string.IsNullOrWhiteSpace(prompt))
            {
                Complete(onComplete, ObjectGenerationResult.Failed(ProviderName, "No text prompt is available for object generation."));
                yield break;
            }

            string key = ResolveApiKey();
            if (string.IsNullOrWhiteSpace(key))
            {
                Complete(onComplete, ObjectGenerationResult.Failed(ProviderName, "3dAIStudio API key missing. Set THREEDAISTUDIO_API_KEY."));
                yield break;
            }

            string taskId = "";
            string submitError = null;
            yield return SubmitTextTask(key, prompt, (id, error) =>
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
            yield return PollTask(key, taskId, (url, cover, error) =>
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

        IEnumerator SubmitTask(string key, string imageDataUri, Action<string, string> onComplete)
        {
            string url = $"{ResolveBaseUrl()}/v1/3d-models/tripo/image-to-3d/";
            ThreeDAIStudioImageTo3DRequest body = new ThreeDAIStudioImageTo3DRequest
            {
                image = imageDataUri,
                texture = ShouldRequestTexture(),
                pbr = ShouldRequestPbr(),
                texture_quality = string.IsNullOrWhiteSpace(textureQuality) ? "standard" : textureQuality.Trim(),
                geometry_quality = string.IsNullOrWhiteSpace(geometryQuality) ? "standard" : geometryQuality.Trim()
            };

            using UnityWebRequest request = BuildJsonPost(url, JsonConvert.SerializeObject(body), key);
            ArchStatusBus.Info("Submitting object image to 3dAIStudio Tripo.", "OBJECT");
            if (debugLogging)
                Debug.Log($"[ThreeDAIStudioObjectGenerationProvider] Submitting Tripo image-to-3D task. texture={body.texture}, pbr={body.pbr}", this);

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                onComplete?.Invoke(null, BuildHttpError("3dAIStudio task submission failed", request));
                yield break;
            }

            ThreeDAIStudioSubmitResponse response = ParseJson<ThreeDAIStudioSubmitResponse>(request.downloadHandler.text);
            string taskId = FirstNonEmpty(response?.task_id, response?.id, response?.data?.task_id, response?.data?.id);
            if (string.IsNullOrWhiteSpace(taskId))
            {
                onComplete?.Invoke(null, $"3dAIStudio task submission returned no task id: {ExtractMessage(request.downloadHandler.text)}");
                yield break;
            }

            ArchStatusBus.Info($"3dAIStudio object task submitted: {taskId}.", "OBJECT");
            Debug.Log($"[ThreeDAIStudioObjectGenerationProvider] Image-to-3D task submitted. taskId={taskId}", this);
            onComplete?.Invoke(taskId, null);
        }

        IEnumerator SubmitTextTask(string key, string prompt, Action<string, string> onComplete)
        {
            string url = $"{ResolveBaseUrl()}/v1/3d-models/tripo/text-to-3d/";
            ThreeDAIStudioTextTo3DRequest body = new ThreeDAIStudioTextTo3DRequest
            {
                prompt = prompt.Trim(),
                texture = ShouldRequestTexture(),
                pbr = ShouldRequestPbr(),
                texture_quality = string.IsNullOrWhiteSpace(textureQuality) ? "standard" : textureQuality.Trim(),
                geometry_quality = string.IsNullOrWhiteSpace(geometryQuality) ? "standard" : geometryQuality.Trim()
            };

            using UnityWebRequest request = BuildJsonPost(url, JsonConvert.SerializeObject(body), key);
            ArchStatusBus.Info("Submitting object prompt to 3dAIStudio Tripo.", "OBJECT");
            if (debugLogging)
                Debug.Log($"[ThreeDAIStudioObjectGenerationProvider] Submitting Tripo text-to-3D task. prompt='{prompt}' texture={body.texture}, pbr={body.pbr}", this);

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                onComplete?.Invoke(null, BuildHttpError("3dAIStudio text task submission failed", request));
                yield break;
            }

            ThreeDAIStudioSubmitResponse response = ParseJson<ThreeDAIStudioSubmitResponse>(request.downloadHandler.text);
            string taskId = FirstNonEmpty(response?.task_id, response?.id, response?.data?.task_id, response?.data?.id);
            if (string.IsNullOrWhiteSpace(taskId))
            {
                onComplete?.Invoke(null, $"3dAIStudio text task submission returned no task id: {ExtractMessage(request.downloadHandler.text)}");
                yield break;
            }

            ArchStatusBus.Info($"3dAIStudio object task submitted: {taskId}.", "OBJECT");
            Debug.Log($"[ThreeDAIStudioObjectGenerationProvider] Text-to-3D task submitted. taskId={taskId} prompt='{prompt}'", this);
            onComplete?.Invoke(taskId, null);
        }

        IEnumerator PollTask(string key, string taskId, Action<string, string, string> onComplete)
        {
            float deadline = Time.realtimeSinceStartup + maxWaitSeconds;
            int missingUrlAfterFinishedCount = 0;
            while (Time.realtimeSinceStartup <= deadline)
            {
                string url = $"{ResolveBaseUrl()}/v1/generation-request/{UnityWebRequest.EscapeURL(taskId)}/status/";
                using UnityWebRequest request = UnityWebRequest.Get(url);
                request.timeout = requestTimeoutSeconds;
                ApplyAuthHeaders(request, key);

                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    onComplete?.Invoke(null, null, BuildHttpError("3dAIStudio task status failed", request));
                    yield break;
                }

                string responseJson = request.downloadHandler.text;
                ThreeDAIStudioStatusResponse response = ParseJson<ThreeDAIStudioStatusResponse>(responseJson);
                string status = FirstNonEmpty(response?.status, response?.state, response?.data?.status, response?.data?.state);
                bool hasProgress = TryFindProgressPercent(responseJson, out float progressPercent);
                string progressLog = hasProgress ? $" progress={progressPercent:0.#}%" : "";
                Debug.Log($"[ThreeDAIStudioObjectGenerationProvider] Task {taskId} status={status}{progressLog}", this);

                if (IsSuccessStatus(status))
                {
                    string modelUrl = FindModelUrl(response, responseJson);
                    string coverUrl = FindCoverUrl(response, responseJson);
                    if (string.IsNullOrWhiteSpace(modelUrl))
                    {
                        string failureReason = FindFailureReason(responseJson);
                        if (string.IsNullOrWhiteSpace(failureReason) &&
                            missingUrlAfterFinishedCount < Mathf.Max(0, finishedMissingUrlRetryCount) &&
                            Time.realtimeSinceStartup + Mathf.Max(1f, pollIntervalSeconds) <= deadline)
                        {
                            missingUrlAfterFinishedCount++;
                            Debug.LogWarning($"[ThreeDAIStudioObjectGenerationProvider] Task {taskId} is {status}, but model URL is not available yet. Retrying {missingUrlAfterFinishedCount}/{Mathf.Max(0, finishedMissingUrlRetryCount)}.", this);
                            ArchStatusBus.Info(BuildProgressStatusMessage("finished; waiting for model asset URL", hasProgress, progressPercent), "OBJECT");
                            yield return new WaitForSecondsRealtime(Mathf.Max(1f, pollIntervalSeconds));
                            continue;
                        }

                        string candidates = DescribeUrlCandidates(responseJson);
                        string diagnosticPath = WriteMissingModelUrlDiagnostic(taskId, status, candidates, responseJson);
                        string diagnosticNote = string.IsNullOrWhiteSpace(diagnosticPath)
                            ? ""
                            : $" Raw response saved to: {diagnosticPath}";
                        Debug.LogWarning($"[ThreeDAIStudioObjectGenerationProvider] Finished task did not expose a model URL. URL candidates: {candidates}.{diagnosticNote} Raw response: {responseJson}", this);
                        string failureNote = string.IsNullOrWhiteSpace(failureReason) ? "" : $" Provider failure reason: {failureReason}.";
                        onComplete?.Invoke(null, null, "3dAIStudio task finished, but I could not find the generated model URL in the response." + failureNote + diagnosticNote);
                        yield break;
                    }

                    onComplete?.Invoke(modelUrl, coverUrl, null);
                    yield break;
                }

                if (IsFailureStatus(status))
                {
                    string error = FirstNonEmpty(response?.error, response?.message, response?.data?.error, response?.data?.message);
                    onComplete?.Invoke(null, null, $"3dAIStudio generation failed: {error}");
                    yield break;
                }

                ArchStatusBus.Info(BuildProgressStatusMessage(status, hasProgress, progressPercent), "OBJECT");
                yield return new WaitForSecondsRealtime(Mathf.Max(1f, pollIntervalSeconds));
            }

            onComplete?.Invoke(null, null, "3dAIStudio generation timed out.");
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

        UnityWebRequest BuildJsonPost(string url, string json, string key)
        {
            UnityWebRequest request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
            request.timeout = requestTimeoutSeconds;
            request.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(json));
            request.downloadHandler = new DownloadHandlerBuffer();
            ApplyAuthHeaders(request, key);
            request.SetRequestHeader("Content-Type", "application/json");
            return request;
        }

        void ApplyAuthHeaders(UnityWebRequest request, string key)
        {
            request.SetRequestHeader("Authorization", $"Bearer {key}");
            request.SetRequestHeader("Accept", "application/json");
        }

        string EncodeImageDataUri(Texture2D texture)
        {
            byte[] bytes = EncodeImage(texture);
            if (bytes == null || bytes.Length == 0)
                throw new InvalidOperationException("Encoded image was empty.");
            return $"data:image/jpeg;base64,{Convert.ToBase64String(bytes)}";
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

        string ResolveApiKey()
        {
            if (!string.IsNullOrWhiteSpace(apiKey))
                return apiKey.Trim();
            return RuntimeDotEnv.GetEnvironmentOrDotEnv("THREEDAISTUDIO_API_KEY");
        }

        string ResolveBaseUrl()
        {
            string configured = RuntimeDotEnv.GetEnvironmentOrDotEnv("THREEDAISTUDIO_BASE_URL");
            if (string.IsNullOrWhiteSpace(configured))
                configured = baseUrl;
            configured = string.IsNullOrWhiteSpace(configured) ? DefaultBaseUrl : configured.Trim();
            return configured.TrimEnd('/');
        }

        static bool IsSuccessStatus(string status)
        {
            return string.Equals(status, "finished", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(status, "complete", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(status, "success", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(status, "succeeded", StringComparison.OrdinalIgnoreCase);
        }

        static bool IsFailureStatus(string status)
        {
            return string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(status, "failure", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(status, "error", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(status, "cancelled", StringComparison.OrdinalIgnoreCase);
        }

        static string FindModelUrl(ThreeDAIStudioStatusResponse response, string json)
        {
            return FirstNonEmpty(
                response?.model_url,
                response?.glb_url,
                response?.download_url,
                response?.asset_url,
                response?.data?.model_url,
                response?.data?.glb_url,
                response?.data?.download_url,
                response?.data?.asset_url,
                FindAsset(response?.results, modelOnly: true),
                FindAsset(response?.data?.results, modelOnly: true),
                FindAsset(response?.result?.results, modelOnly: true),
                FindAsset(response?.data?.result?.results, modelOnly: true),
                FindAsset(response?.assets, modelOnly: true),
                FindAsset(response?.data?.assets, modelOnly: true),
                FindAsset(response?.result?.assets, modelOnly: true),
                FindAsset(response?.data?.result?.assets, modelOnly: true),
                FindUrlInJson(json, modelOnly: true));
        }

        static string FindCoverUrl(ThreeDAIStudioStatusResponse response, string json)
        {
            return FirstNonEmpty(
                response?.cover_url,
                response?.thumbnail_url,
                response?.preview_url,
                response?.data?.cover_url,
                response?.data?.thumbnail_url,
                response?.data?.preview_url,
                FindAsset(response?.results, modelOnly: false),
                FindAsset(response?.data?.results, modelOnly: false),
                FindAsset(response?.assets, modelOnly: false),
                FindAsset(response?.data?.assets, modelOnly: false),
                FindUrlInJson(json, modelOnly: false));
        }

        static string FindAsset(ThreeDAIStudioAsset[] assets, bool modelOnly)
        {
            if (assets == null)
                return "";

            foreach (ThreeDAIStudioAsset asset in assets)
            {
                string url = FirstNonEmpty(asset?.url, asset?.download_url, asset?.asset_url, asset?.asset);
                if (string.IsNullOrWhiteSpace(url))
                    continue;

                string type = FirstNonEmpty(asset?.type, asset?.asset_type, asset?.format);
                if (!modelOnly)
                    return url;

                if (url.EndsWith(".glb", StringComparison.OrdinalIgnoreCase) ||
                    type.IndexOf("model", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    type.IndexOf("glb", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return url;
                }
            }

            return "";
        }

        static string FindUrlInJson(string json, bool modelOnly)
        {
            if (string.IsNullOrWhiteSpace(json))
                return "";

            try
            {
                JToken token = JToken.Parse(json);
                List<UrlCandidate> candidates = new List<UrlCandidate>();
                CollectUrlCandidates(token, "", "", candidates);
                UrlCandidate best = SelectBestCandidate(candidates, modelOnly);
                return best.url ?? "";
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ThreeDAIStudioObjectGenerationProvider] Raw status URL scan failed: {ex.Message}");
                return "";
            }
        }

        static void CollectUrlCandidates(JToken token, string path, string context, List<UrlCandidate> candidates)
        {
            if (token == null)
                return;

            if (token.Type == JTokenType.Property)
            {
                JProperty property = (JProperty)token;
                string nextPath = string.IsNullOrWhiteSpace(path) ? property.Name : $"{path}.{property.Name}";
                string nextContext = FirstNonEmpty(context, property.Name);
                CollectUrlCandidates(property.Value, nextPath, nextContext, candidates);
                return;
            }

            if (token.Type == JTokenType.String)
            {
                string value = token.Value<string>();
                if (LooksLikeUrl(value))
                {
                    string urlContext = BuildContext(token, path, context);
                    candidates.Add(new UrlCandidate
                    {
                        url = value,
                        path = path,
                        context = urlContext,
                        score = ScoreUrlCandidate(value, path, urlContext)
                    });
                }

                return;
            }

            int index = 0;
            foreach (JToken child in token.Children())
            {
                string childPath = token.Type == JTokenType.Array ? $"{path}[{index}]" : path;
                CollectUrlCandidates(child, childPath, context, candidates);
                index++;
            }
        }

        static string FindDirectUrlInObject(JObject obj, bool modelOnly)
        {
            string[] urlKeys =
            {
                "model_url", "glb_url", "download_url", "asset_url", "asset", "url",
                "file_url", "file", "href", "model", "glb", "gltf", "result", "output",
                "preview_url", "thumbnail_url", "cover_url"
            };

            string type = FirstNonEmpty(
                StringProperty(obj, "asset_type"),
                StringProperty(obj, "type"),
                StringProperty(obj, "format"),
                StringProperty(obj, "mime_type"),
                StringProperty(obj, "content_type"));

            foreach (string key in urlKeys)
            {
                string url = StringProperty(obj, key);
                if (string.IsNullOrWhiteSpace(url))
                    continue;

                if (!LooksLikeUrl(url))
                    continue;

                if (!modelOnly)
                    return url;

                if (LooksLikeModelUrlOrType(url, type, key))
                    return url;
            }

            return "";
        }

        static UrlCandidate SelectBestCandidate(List<UrlCandidate> candidates, bool modelOnly)
        {
            if (candidates == null || candidates.Count == 0)
                return default;

            UrlCandidate best = default;
            int bestScore = int.MinValue;
            foreach (UrlCandidate candidate in candidates)
            {
                int score = candidate.score;
                if (!modelOnly && IsLikelyPreview(candidate))
                    score += 25;

                if (modelOnly && score <= 0)
                    score = 1; // Last resort: a signed model URL may not contain .glb.

                if (score > bestScore)
                {
                    best = candidate;
                    bestScore = score;
                }
            }

            return best;
        }

        static int ScoreUrlCandidate(string url, string path, string context)
        {
            int score = 0;
            string combined = $"{url} {path} {context}";
            if (url.IndexOf(".glb", StringComparison.OrdinalIgnoreCase) >= 0)
                score += 100;
            if (url.IndexOf(".gltf", StringComparison.OrdinalIgnoreCase) >= 0)
                score += 95;
            if (combined.IndexOf("3d_model", StringComparison.OrdinalIgnoreCase) >= 0 ||
                combined.IndexOf("3D_MODEL", StringComparison.OrdinalIgnoreCase) >= 0)
                score += 90;
            if (combined.IndexOf("model", StringComparison.OrdinalIgnoreCase) >= 0)
                score += 70;
            if (combined.IndexOf("asset", StringComparison.OrdinalIgnoreCase) >= 0)
                score += 35;
            if (combined.IndexOf("download", StringComparison.OrdinalIgnoreCase) >= 0)
                score += 30;
            if (combined.IndexOf("result", StringComparison.OrdinalIgnoreCase) >= 0 ||
                combined.IndexOf("output", StringComparison.OrdinalIgnoreCase) >= 0)
                score += 20;
            if (combined.IndexOf("thumbnail", StringComparison.OrdinalIgnoreCase) >= 0 ||
                combined.IndexOf("preview", StringComparison.OrdinalIgnoreCase) >= 0 ||
                combined.IndexOf("cover", StringComparison.OrdinalIgnoreCase) >= 0 ||
                url.IndexOf(".png", StringComparison.OrdinalIgnoreCase) >= 0 ||
                url.IndexOf(".jpg", StringComparison.OrdinalIgnoreCase) >= 0 ||
                url.IndexOf(".jpeg", StringComparison.OrdinalIgnoreCase) >= 0)
                score -= 50;
            return score;
        }

        static bool IsLikelyPreview(UrlCandidate candidate)
        {
            string combined = $"{candidate.url} {candidate.path} {candidate.context}";
            return combined.IndexOf("thumbnail", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   combined.IndexOf("preview", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   combined.IndexOf("cover", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   candidate.url.IndexOf(".png", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   candidate.url.IndexOf(".jpg", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   candidate.url.IndexOf(".jpeg", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static string BuildContext(JToken token, string path, string context)
        {
            JObject obj = token.Parent?.Parent as JObject;
            if (obj == null)
                return FirstNonEmpty(context, path);

            return FirstNonEmpty(context,
                StringProperty(obj, "asset_type"),
                StringProperty(obj, "type"),
                StringProperty(obj, "format"),
                StringProperty(obj, "mime_type"),
                StringProperty(obj, "content_type"),
                path);
        }

        static string DescribeUrlCandidates(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return "none";

            try
            {
                List<UrlCandidate> candidates = new List<UrlCandidate>();
                CollectUrlCandidates(JToken.Parse(json), "", "", candidates);
                if (candidates.Count == 0)
                    return "none";

                List<string> parts = new List<string>();
                foreach (UrlCandidate candidate in candidates)
                {
                    string shortened = candidate.url.Length > 120 ? candidate.url.Substring(0, 120) + "..." : candidate.url;
                    parts.Add($"score={candidate.score}, path={candidate.path}, context={candidate.context}, url={shortened}");
                }

                return string.Join(" | ", parts);
            }
            catch (Exception ex)
            {
                return "scan failed: " + ex.Message;
            }
        }

        static string FindFailureReason(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return "";

            try
            {
                JToken token = JToken.Parse(json);
                return FirstNonEmpty(
                    token.SelectToken("failure_reason")?.Value<string>(),
                    token.SelectToken("error")?.Value<string>(),
                    token.SelectToken("message")?.Value<string>(),
                    token.SelectToken("data.failure_reason")?.Value<string>(),
                    token.SelectToken("data.error")?.Value<string>(),
                    token.SelectToken("data.message")?.Value<string>(),
                    token.SelectToken("result.failure_reason")?.Value<string>(),
                    token.SelectToken("result.error")?.Value<string>(),
                    token.SelectToken("result.message")?.Value<string>());
            }
            catch
            {
                return "";
            }
        }

        static string BuildProgressStatusMessage(string status, bool hasProgress, float progressPercent)
        {
            string cleanStatus = string.IsNullOrWhiteSpace(status) ? "working" : status.Trim();
            return hasProgress
                ? $"3dAIStudio object generation: {cleanStatus} {progressPercent:0.#}%."
                : $"3dAIStudio object generation: {cleanStatus}.";
        }

        static bool TryFindProgressPercent(string json, out float percent)
        {
            percent = 0f;
            if (string.IsNullOrWhiteSpace(json))
                return false;

            try
            {
                return TryFindProgressPercent(JToken.Parse(json), out percent);
            }
            catch
            {
                return false;
            }
        }

        static bool TryFindProgressPercent(JToken token, out float percent)
        {
            percent = 0f;
            if (token == null)
                return false;

            if (token.Type == JTokenType.Property)
            {
                JProperty property = (JProperty)token;
                if (IsProgressKey(property.Name) && TryReadPercent(property.Value, out percent))
                    return true;

                return TryFindProgressPercent(property.Value, out percent);
            }

            foreach (JToken child in token.Children())
            {
                if (TryFindProgressPercent(child, out percent))
                    return true;
            }

            return false;
        }

        static bool IsProgressKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return false;

            string normalized = key.Trim().Replace("-", "_").ToLowerInvariant();
            return normalized == "progress" ||
                   normalized == "percent" ||
                   normalized == "percentage" ||
                   normalized == "percent_complete" ||
                   normalized == "progress_percent" ||
                   normalized == "progress_percentage";
        }

        static bool TryReadPercent(JToken token, out float percent)
        {
            percent = 0f;
            if (token == null)
                return false;

            double value;
            if (token.Type == JTokenType.Integer || token.Type == JTokenType.Float)
            {
                value = token.Value<double>();
            }
            else if (token.Type == JTokenType.String)
            {
                string text = token.Value<string>()?.Trim().TrimEnd('%');
                if (!double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value))
                    return false;
            }
            else
            {
                return false;
            }

            if (value > 0d && value <= 1d)
                value *= 100d;

            percent = Mathf.Clamp((float)value, 0f, 100f);
            return true;
        }

        bool ShouldRequestTexture()
        {
            return forceTexturedOutput || texture;
        }

        bool ShouldRequestPbr()
        {
            return forceTexturedOutput || pbr;
        }

        string WriteMissingModelUrlDiagnostic(string taskId, string status, string candidates, string rawResponse)
        {
            if (!writeRawResponseDiagnostics)
                return "";

            try
            {
                string directory = Path.Combine(Application.persistentDataPath, "Diagnostics", "ObjectGeneration");
                Directory.CreateDirectory(directory);

                string safeTaskId = SanitizeFileName(string.IsNullOrWhiteSpace(taskId) ? "unknown-task" : taskId);
                string path = Path.Combine(directory, $"3daistudio_missing_model_url_{safeTaskId}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json");
                JObject diagnostic = new JObject
                {
                    ["provider"] = ProviderName,
                    ["task_id"] = taskId ?? "",
                    ["status"] = status ?? "",
                    ["url_candidates"] = candidates ?? "",
                    ["created_at_utc"] = DateTime.UtcNow.ToString("O")
                };

                try
                {
                    diagnostic["raw_response"] = string.IsNullOrWhiteSpace(rawResponse)
                        ? JValue.CreateNull()
                        : JToken.Parse(rawResponse);
                }
                catch
                {
                    diagnostic["raw_response_text"] = rawResponse ?? "";
                }

                File.WriteAllText(path, diagnostic.ToString(Formatting.Indented));
                return path;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ThreeDAIStudioObjectGenerationProvider] Could not write raw response diagnostic: {ex.Message}", this);
                return "";
            }
        }

        static string SanitizeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "value";

            foreach (char invalid in Path.GetInvalidFileNameChars())
                value = value.Replace(invalid, '_');
            return value.Trim();
        }

        static bool LooksLikeModelUrlOrType(string url, string type, string key)
        {
            return url.IndexOf(".glb", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   url.IndexOf(".gltf", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   key.IndexOf("model", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   key.IndexOf("glb", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   type.IndexOf("model", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   type.IndexOf("glb", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   type.IndexOf("gltf", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static bool LooksLikeUrl(string value)
        {
            return value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                   value.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        }

        static string StringProperty(JObject obj, string key)
        {
            JToken token = obj.GetValue(key, StringComparison.OrdinalIgnoreCase);
            return token != null && token.Type == JTokenType.String ? token.Value<string>() : "";
        }

        struct UrlCandidate
        {
            public string url;
            public string path;
            public string context;
            public int score;
        }

        static T ParseJson<T>(string json) where T : class
        {
            try
            {
                return JsonConvert.DeserializeObject<T>(json);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ThreeDAIStudioObjectGenerationProvider] JSON parse failed: {ex.Message}");
                return null;
            }
        }

        static string ExtractMessage(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return "empty response";
            ThreeDAIStudioBaseResponse response = ParseJson<ThreeDAIStudioBaseResponse>(json);
            string message = FirstNonEmpty(response?.error, response?.message, response?.detail);
            if (!string.IsNullOrWhiteSpace(message))
                return message;
            return json.Length > 240 ? json.Substring(0, 240) : json;
        }

        static string FirstNonEmpty(params string[] values)
        {
            if (values == null)
                return "";
            foreach (string value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }
            return "";
        }

        static void Complete(Action<ObjectGenerationResult> onComplete, ObjectGenerationResult result)
        {
            onComplete?.Invoke(result);
        }

        [Serializable]
        sealed class ThreeDAIStudioImageTo3DRequest
        {
            public string image;
            public bool texture;
            public bool pbr;
            public string texture_quality;
            public string geometry_quality;
        }

        [Serializable]
        sealed class ThreeDAIStudioTextTo3DRequest
        {
            public string prompt;
            public bool texture;
            public bool pbr;
            public string texture_quality;
            public string geometry_quality;
        }

        [Serializable]
        class ThreeDAIStudioBaseResponse
        {
            public string error;
            public string message;
            public string detail;
        }

        [Serializable]
        sealed class ThreeDAIStudioSubmitResponse : ThreeDAIStudioBaseResponse
        {
            public string task_id;
            public string id;
            public ThreeDAIStudioSubmitData data;
        }

        [Serializable]
        sealed class ThreeDAIStudioSubmitData
        {
            public string task_id;
            public string id;
        }

        [Serializable]
        sealed class ThreeDAIStudioStatusResponse : ThreeDAIStudioBaseResponse
        {
            public string status;
            public string state;
            public string model_url;
            public string glb_url;
            public string download_url;
            public string asset_url;
            public string cover_url;
            public string thumbnail_url;
            public string preview_url;
            public ThreeDAIStudioAsset[] assets;
            public ThreeDAIStudioAsset[] results;
            public ThreeDAIStudioStatusData data;
            public ThreeDAIStudioStatusData result;
        }

        [Serializable]
        sealed class ThreeDAIStudioStatusData : ThreeDAIStudioBaseResponse
        {
            public string status;
            public string state;
            public string model_url;
            public string glb_url;
            public string download_url;
            public string asset_url;
            public string cover_url;
            public string thumbnail_url;
            public string preview_url;
            public ThreeDAIStudioAsset[] assets;
            public ThreeDAIStudioAsset[] results;
            public ThreeDAIStudioStatusData result;
        }

        [Serializable]
        sealed class ThreeDAIStudioAsset
        {
            public string type;
            public string asset_type;
            public string format;
            public string asset;
            public string url;
            public string download_url;
            public string asset_url;
        }
    }
}
