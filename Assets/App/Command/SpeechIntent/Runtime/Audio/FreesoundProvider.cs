using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace SpeechIntent.Audio
{
    public sealed class FreesoundProvider : MonoBehaviour, ISoundProvider
    {
        [Header("API")]
        [Tooltip("Optional. If empty, FREESOUND_API_KEY is read from the process environment.")]
        [SerializeField] private string apiKey = "";
        [Tooltip("Search endpoint. If FREESOUND_SEARCH_URL is set in the environment, it overrides this value.")]
        [SerializeField] private string searchUrl = "https://freesound.org/apiv2/search/text/";

        [Header("Search")]
        [SerializeField] private bool preferPreviewMp3 = true;
        [SerializeField] private bool onlyCc0OrAttribution = true;
        [SerializeField] private int timeoutSeconds = 30;
        [SerializeField] private bool verboseLogging;

        public string ProviderName => "freesound";

        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(ResolveApiKey());

        public bool CanHandle(SoundQuery query) => IsConfigured;

        public async Task<List<SoundSearchResult>> SearchAsync(SoundQuery query, int limit)
        {
            var results = new List<SoundSearchResult>();
            string key = ResolveApiKey();
            string searchText = query?.BestSearchText ?? string.Empty;

            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(searchText))
                return results;

            limit = Mathf.Clamp(limit, 1, 50);
            var parameters = new List<string>
            {
                "query=" + UnityWebRequest.EscapeURL(searchText),
                "fields=id,name,username,license,duration,type,tags,url,previews",
                "sort=score",
                "page_size=" + limit,
                "token=" + UnityWebRequest.EscapeURL(key)
            };

            string filter = BuildFilter(query);
            if (!string.IsNullOrWhiteSpace(filter))
                parameters.Add("filter=" + UnityWebRequest.EscapeURL(filter));

            string url = ResolveSearchUrl() + "?" + string.Join("&", parameters);
            if (verboseLogging) Debug.Log("[FreesoundProvider] " + url);

            using UnityWebRequest request = UnityWebRequest.Get(url);
            request.timeout = timeoutSeconds;
            UnityWebRequestAsyncOperation op = request.SendWebRequest();
            while (!op.isDone) await Task.Yield();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning(
                    $"[FreesoundProvider] Search failed. HTTP={(long)request.responseCode}, Error={request.error}, Body={request.downloadHandler?.text}");
                return results;
            }

            try
            {
                JObject root = JObject.Parse(request.downloadHandler.text);
                JArray items = root["results"] as JArray;
                if (items == null) return results;

                foreach (JToken item in items)
                {
                    var result = new SoundSearchResult
                    {
                        id = item.Value<string>("id") ?? "",
                        title = item.Value<string>("name") ?? "",
                        provider = ProviderName,
                        creator = item.Value<string>("username") ?? "",
                        license = item.Value<string>("license") ?? "",
                        landingUrl = item.Value<string>("url") ?? "",
                        durationSeconds = item.Value<float?>("duration") ?? 0f,
                        fileExtension = "." + (item.Value<string>("type") ?? "mp3").TrimStart('.').ToLowerInvariant()
                    };

                    JToken previews = item["previews"];
                    result.previewUrl = preferPreviewMp3
                        ? previews?.Value<string>("preview-hq-mp3") ?? previews?.Value<string>("preview-lq-mp3") ?? ""
                        : previews?.Value<string>("preview-hq-ogg") ?? previews?.Value<string>("preview-lq-ogg") ?? "";

                    JArray tags = item["tags"] as JArray;
                    if (tags != null)
                        foreach (JToken tag in tags)
                            if (tag.Type == JTokenType.String) result.tags.Add(tag.Value<string>());

                    result.audioUrl = result.previewUrl;
                    result.fileExtension = SoundProviderUtil.GuessExtension(result.BestAudioUrl, ".mp3");
                    if (!string.IsNullOrWhiteSpace(result.BestAudioUrl)) results.Add(result);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[FreesoundProvider] JSON parse failed: " + ex.Message);
            }

            return results;
        }

        public Task<byte[]> DownloadAudioBytesAsync(SoundSearchResult result) =>
            SoundProviderUtil.DownloadBytesAsync(result?.BestAudioUrl, timeoutSeconds);

        private string ResolveApiKey()
        {
            if (!string.IsNullOrWhiteSpace(apiKey)) return apiKey.Trim();
            return SoundProviderUtil.Env("FREESOUND_API_KEY").Trim();
        }

        private string ResolveSearchUrl()
        {
            string env = SoundProviderUtil.Env("FREESOUND_SEARCH_URL").Trim();
            if (!string.IsNullOrWhiteSpace(env)) return env.TrimEnd('/');
            return string.IsNullOrWhiteSpace(searchUrl)
                ? "https://freesound.org/apiv2/search/text/"
                : searchUrl.Trim();
        }

        private string BuildFilter(SoundQuery query)
        {
            var sb = new StringBuilder();
            if (onlyCc0OrAttribution)
                sb.Append("(license:\"Creative Commons 0\" OR license:\"Attribution\")");

            float maxDuration = query != null && query.maxDurationSeconds > 0f
                ? query.maxDurationSeconds
                : 45f;

            if (sb.Length > 0) sb.Append(' ');
            sb.Append("duration:[0.2 TO ");
            sb.Append(maxDuration.ToString("0.###", CultureInfo.InvariantCulture));
            sb.Append(']');
            return sb.ToString();
        }
    }
}
