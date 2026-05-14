using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace SpeechIntent.Audio
{
    public sealed class XenoCantoSoundProvider : MonoBehaviour, ISoundProvider
    {
        [Header("API")]
        [Tooltip("Optional. If empty, XENO_CANTO_API_KEY is read from the process environment.")]
        [SerializeField] private string apiKey = "";
        [Tooltip("Search endpoint. If XENO_CANTO_SEARCH_URL is set in the environment, it overrides this value.")]
        [SerializeField] private string searchUrl = "https://xeno-canto.org/api/3/recordings";

        [Header("Search")]
        [SerializeField] private int timeoutSeconds = 30;
        [SerializeField] private bool verboseLogging;

        public string ProviderName => "xeno-canto";
        public bool IsConfigured => !string.IsNullOrWhiteSpace(ResolveApiKey());

        public bool CanHandle(SoundQuery query)
        {
            if (!IsConfigured || query == null) return false;
            string text = (query.prompt + " " + query.category + " " + query.species).ToLowerInvariant();
            return !string.IsNullOrWhiteSpace(query.species)
                || text.Contains("bird")
                || text.Contains("birds")
                || text.Contains("birdsong")
                || text.Contains("wildlife");
        }

        public async Task<List<SoundSearchResult>> SearchAsync(SoundQuery query, int limit)
        {
            var results = new List<SoundSearchResult>();
            string key = ResolveApiKey();
            string searchText = query?.BestSearchText ?? string.Empty;
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(searchText))
                return results;

            limit = Mathf.Clamp(limit, 1, 50);
            List<string> candidates = BuildCandidateQueries(searchText);

            foreach (string candidate in candidates)
            {
                results = await SearchCandidateAsync(candidate, key, limit);
                if (results.Count > 0) return results;
            }

            return results;
        }

        public Task<byte[]> DownloadAudioBytesAsync(SoundSearchResult result) =>
            SoundProviderUtil.DownloadBytesAsync(result?.BestAudioUrl, timeoutSeconds);

        private async Task<List<SoundSearchResult>> SearchCandidateAsync(string query, string key, int limit)
        {
            var results = new List<SoundSearchResult>();
            string url =
                $"{ResolveSearchUrl()}?query={UnityWebRequest.EscapeURL(query)}&page=1&per_page={limit}&key={UnityWebRequest.EscapeURL(key)}";

            if (verboseLogging) Debug.Log("[XenoCantoSoundProvider] " + url);

            using UnityWebRequest request = UnityWebRequest.Get(url);
            request.timeout = timeoutSeconds;
            UnityWebRequestAsyncOperation op = request.SendWebRequest();
            while (!op.isDone) await Task.Yield();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning(
                    $"[XenoCantoSoundProvider] Search failed. HTTP={(long)request.responseCode}, Error={request.error}, Body={request.downloadHandler?.text}");
                return results;
            }

            try
            {
                JObject root = JObject.Parse(request.downloadHandler.text);
                JArray items = root["recordings"] as JArray;
                if (items == null) return results;

                foreach (JToken item in items)
                {
                    string fileUrl = item.Value<string>("file") ?? "";
                    if (fileUrl.StartsWith("//", StringComparison.Ordinal)) fileUrl = "https:" + fileUrl;

                    string genus = item.Value<string>("gen") ?? "";
                    string species = item.Value<string>("sp") ?? "";
                    string english = item.Value<string>("en") ?? "";
                    string title = (genus + " " + species).Trim();
                    if (string.IsNullOrWhiteSpace(title)) title = english;

                    var result = new SoundSearchResult
                    {
                        id = item.Value<string>("id") ?? "",
                        title = title,
                        provider = ProviderName,
                        creator = item.Value<string>("rec") ?? "",
                        license = item.Value<string>("lic") ?? "",
                        landingUrl = item.Value<string>("url") ?? "",
                        audioUrl = fileUrl,
                        previewUrl = fileUrl,
                        fileExtension = SoundProviderUtil.GuessExtension(fileUrl, ".mp3"),
                        durationSeconds = ParseDurationSeconds(item.Value<string>("length"))
                    };

                    result.tags.Add(item.Value<string>("type") ?? "bird");
                    if (!string.IsNullOrWhiteSpace(english)) result.tags.Add(english);
                    if (!string.IsNullOrWhiteSpace(result.BestAudioUrl)) results.Add(result);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[XenoCantoSoundProvider] JSON parse failed: " + ex.Message);
            }

            return results;
        }

        private string ResolveApiKey()
        {
            if (!string.IsNullOrWhiteSpace(apiKey)) return apiKey.Trim();
            return SoundProviderUtil.Env("XENO_CANTO_API_KEY").Trim();
        }

        private string ResolveSearchUrl()
        {
            string env = SoundProviderUtil.Env("XENO_CANTO_SEARCH_URL").Trim();
            if (!string.IsNullOrWhiteSpace(env)) return env.Trim();
            return string.IsNullOrWhiteSpace(searchUrl)
                ? "https://xeno-canto.org/api/3/recordings"
                : searchUrl.Trim();
        }

        private static List<string> BuildCandidateQueries(string searchText)
        {
            var results = new List<string>();
            string cleaned = SoundProviderUtil.SafeTrim(searchText);
            if (string.IsNullOrWhiteSpace(cleaned)) return results;

            string[] tokens = cleaned.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length >= 2)
                results.Add($"gen:{tokens[0]} sp:{tokens[1]}");

            results.Add($"en:\"{cleaned.Replace("\"", "\\\"")}\"");
            results.Add(cleaned);
            return results;
        }

        private static float ParseDurationSeconds(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return 0f;
            raw = raw.Trim();
            if (TimeSpan.TryParseExact(
                    raw,
                    new[] { @"m\:ss", @"mm\:ss", @"h\:mm\:ss" },
                    CultureInfo.InvariantCulture,
                    out TimeSpan ts))
                return (float)ts.TotalSeconds;
            return float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float seconds)
                ? seconds
                : 0f;
        }
    }
}
