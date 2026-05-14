using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace SpeechIntent.Audio
{
    public sealed class OpenverseProvider : MonoBehaviour, ISoundProvider
    {
        [Header("Search")]
        [Tooltip("Search endpoint. If OPENVERSE_AUDIO_SEARCH_URL is set in the environment, it overrides this value.")]
        [SerializeField] private string searchUrl = "https://api.openverse.org/v1/audio/";
        [SerializeField] private string licenseFilter = "cc0,by,pdm";
        [SerializeField] private int timeoutSeconds = 30;
        [SerializeField] private bool verboseLogging;

        public string ProviderName => "openverse";
        public bool IsConfigured => true;
        public bool CanHandle(SoundQuery query) => true;

        public async Task<List<SoundSearchResult>> SearchAsync(SoundQuery query, int limit)
        {
            var results = new List<SoundSearchResult>();
            string searchText = query?.BestSearchText ?? string.Empty;
            if (string.IsNullOrWhiteSpace(searchText)) return results;

            limit = Mathf.Clamp(limit, 1, 50);
            var parameters = new List<string>
            {
                "q=" + UnityWebRequest.EscapeURL(searchText),
                "page_size=" + limit,
                "filter_dead=true"
            };

            if (!string.IsNullOrWhiteSpace(licenseFilter))
                parameters.Add("license=" + UnityWebRequest.EscapeURL(licenseFilter));

            string url = ResolveSearchUrl() + "?" + string.Join("&", parameters);
            if (verboseLogging) Debug.Log("[OpenverseProvider] " + url);

            using UnityWebRequest request = UnityWebRequest.Get(url);
            request.timeout = timeoutSeconds;
            UnityWebRequestAsyncOperation op = request.SendWebRequest();
            while (!op.isDone) await Task.Yield();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning(
                    $"[OpenverseProvider] Search failed. HTTP={(long)request.responseCode}, Error={request.error}, Body={request.downloadHandler?.text}");
                return results;
            }

            try
            {
                JObject root = JObject.Parse(request.downloadHandler.text);
                JArray items = root["results"] as JArray;
                if (items == null) return results;

                foreach (JToken item in items)
                {
                    string fileUrl = item.Value<string>("url") ?? "";
                    var result = new SoundSearchResult
                    {
                        id = item.Value<string>("id") ?? "",
                        title = item.Value<string>("title") ?? "",
                        provider = item.Value<string>("source") ?? ProviderName,
                        creator = item.Value<string>("creator") ?? "",
                        license = item.Value<string>("license") ?? "",
                        licenseUrl = item.Value<string>("license_url") ?? "",
                        landingUrl = item.Value<string>("foreign_landing_url") ?? "",
                        audioUrl = fileUrl,
                        previewUrl = fileUrl,
                        durationSeconds = item.Value<float?>("duration") ?? 0f,
                        fileExtension = SoundProviderUtil.GuessExtension(fileUrl, ".mp3")
                    };

                    JArray tags = item["tags"] as JArray;
                    if (tags != null)
                    {
                        foreach (JToken tag in tags)
                        {
                            string name = tag.Type == JTokenType.Object
                                ? tag.Value<string>("name")
                                : tag.Value<string>();
                            if (!string.IsNullOrWhiteSpace(name)) result.tags.Add(name);
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(result.BestAudioUrl)) results.Add(result);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[OpenverseProvider] JSON parse failed: " + ex.Message);
            }

            return results;
        }

        public Task<byte[]> DownloadAudioBytesAsync(SoundSearchResult result) =>
            SoundProviderUtil.DownloadBytesAsync(result?.BestAudioUrl, timeoutSeconds);

        private string ResolveSearchUrl()
        {
            string env = SoundProviderUtil.Env("OPENVERSE_AUDIO_SEARCH_URL").Trim();
            if (!string.IsNullOrWhiteSpace(env)) return env.Trim();
            return string.IsNullOrWhiteSpace(searchUrl)
                ? "https://api.openverse.org/v1/audio/"
                : searchUrl.Trim();
        }
    }
}
