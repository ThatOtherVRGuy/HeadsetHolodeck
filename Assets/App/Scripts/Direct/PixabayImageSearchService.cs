using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json;
using SpeechIntent;
using UnityEngine;
using UnityEngine.Networking;

namespace Holodeck.Direct
{
    public sealed class PixabayImageSearchService : MonoBehaviour
    {
        const string PixabayApiUrl = "https://pixabay.com/api/";

        [Header("Search")]
        [SerializeField] string apiKey = "";
        [SerializeField] int perPage = 12;
        [SerializeField] int timeoutSeconds = 30;
        [SerializeField] string imageType = "photo";
        [SerializeField] string orientation = "horizontal";
        [SerializeField] bool safeSearch = true;

        [Header("Runtime")]
        [SerializeField] string lastQuery = "";
        [SerializeField] int selectedIndex;
        [SerializeField] Texture2D selectedTexture;

        readonly List<PixabayImageResult> _results = new();
        Coroutine _searchCoroutine;
        Coroutine _downloadCoroutine;

        public IReadOnlyList<PixabayImageResult> Results => _results;
        public PixabayImageResult SelectedResult => _results.Count > 0 && selectedIndex >= 0 && selectedIndex < _results.Count
            ? _results[selectedIndex]
            : null;
        public Texture2D SelectedTexture => selectedTexture;
        public string LastQuery => lastQuery;
        public int SelectedIndex => selectedIndex;
        public bool IsConfigured => !string.IsNullOrWhiteSpace(ResolveApiKey());

        public event Action<IReadOnlyList<PixabayImageResult>> ResultsChanged;
        public event Action<PixabayImageResult, Texture2D> SelectionChanged;
        public event Action<string> SearchFailed;
        public event Action<string> StatusChanged;

        public void Search(string query)
        {
            if (_searchCoroutine != null)
                StopCoroutine(_searchCoroutine);
            _searchCoroutine = StartCoroutine(SearchCoroutine(query));
        }

        public void SelectNext()
        {
            if (_results.Count == 0)
                return;
            SelectIndex((selectedIndex + 1) % _results.Count);
        }

        public void SelectPrevious()
        {
            if (_results.Count == 0)
                return;
            SelectIndex((selectedIndex - 1 + _results.Count) % _results.Count);
        }

        public void SelectIndex(int index)
        {
            if (_results.Count == 0)
                return;

            selectedIndex = Mathf.Clamp(index, 0, _results.Count - 1);
            if (_downloadCoroutine != null)
                StopCoroutine(_downloadCoroutine);
            _downloadCoroutine = StartCoroutine(DownloadSelectedCoroutine());
        }

        IEnumerator SearchCoroutine(string query)
        {
            query = (query ?? "").Trim();
            if (string.IsNullOrWhiteSpace(query))
            {
                Fail("Enter an image search query.");
                yield break;
            }

            string key = ResolveApiKey();
            if (string.IsNullOrWhiteSpace(key))
            {
                Fail("Pixabay API key missing. Set PIXABAY_API_KEY in .env or on the service.");
                yield break;
            }

            lastQuery = query;
            selectedIndex = 0;
            ClearSelectedTexture();
            StatusChanged?.Invoke($"Searching Pixabay for {query}...");

            string url =
                $"{PixabayApiUrl}?key={UnityWebRequest.EscapeURL(key)}" +
                $"&q={UnityWebRequest.EscapeURL(query)}" +
                $"&image_type={UnityWebRequest.EscapeURL(imageType)}" +
                $"&orientation={UnityWebRequest.EscapeURL(orientation)}" +
                $"&safesearch={(safeSearch ? "true" : "false")}" +
                $"&per_page={Mathf.Clamp(perPage, 3, 50)}";

            using UnityWebRequest request = UnityWebRequest.Get(url);
            request.timeout = timeoutSeconds;
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Fail($"Pixabay search failed: {request.error}");
                yield break;
            }

            PixabaySearchResponse response;
            try
            {
                response = JsonConvert.DeserializeObject<PixabaySearchResponse>(request.downloadHandler.text);
            }
            catch (Exception ex)
            {
                Fail($"Pixabay response parse failed: {ex.Message}");
                yield break;
            }

            _results.Clear();
            if (response?.hits != null)
                _results.AddRange(response.hits);

            ResultsChanged?.Invoke(_results);

            if (_results.Count == 0)
            {
                Fail($"No Pixabay images found for {query}.");
                yield break;
            }

            StatusChanged?.Invoke($"Found {_results.Count} Pixabay image(s).");
            SelectIndex(0);
            _searchCoroutine = null;
        }

        IEnumerator DownloadSelectedCoroutine()
        {
            PixabayImageResult result = SelectedResult;
            if (result == null)
                yield break;

            string imageUrl = !string.IsNullOrWhiteSpace(result.largeImageURL)
                ? result.largeImageURL
                : result.webformatURL;

            if (string.IsNullOrWhiteSpace(imageUrl))
            {
                Fail("Selected Pixabay result has no downloadable image URL.");
                yield break;
            }

            StatusChanged?.Invoke($"Loading image {selectedIndex + 1}/{_results.Count}...");
            using UnityWebRequest request = UnityWebRequestTexture.GetTexture(imageUrl, false);
            request.timeout = timeoutSeconds;
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Fail($"Pixabay image download failed: {request.error}");
                yield break;
            }

            ClearSelectedTexture();
            selectedTexture = DownloadHandlerTexture.GetContent(request);
            selectedTexture.name = $"Pixabay_{result.id}_{Sanitize(lastQuery)}";

            SelectionChanged?.Invoke(result, selectedTexture);
            StatusChanged?.Invoke($"Selected {selectedIndex + 1}/{_results.Count}: {result.AttributionText}");
            _downloadCoroutine = null;
        }

        string ResolveApiKey()
        {
            if (!string.IsNullOrWhiteSpace(apiKey))
                return apiKey.Trim();
            return RuntimeDotEnv.GetEnvironmentOrDotEnv("PIXABAY_API_KEY");
        }

        void Fail(string message)
        {
            Debug.LogWarning($"[PixabayImageSearchService] {message}", this);
            ArchStatusBus.Warning(message, "IMAGE");
            StatusChanged?.Invoke(message);
            SearchFailed?.Invoke(message);
            _searchCoroutine = null;
            _downloadCoroutine = null;
        }

        void ClearSelectedTexture()
        {
            if (selectedTexture != null)
                Destroy(selectedTexture);
            selectedTexture = null;
            SelectionChanged?.Invoke(SelectedResult, null);
        }

        static string Sanitize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "image";
            foreach (char c in System.IO.Path.GetInvalidFileNameChars())
                value = value.Replace(c, '_');
            return value.Replace(' ', '_');
        }

        void OnDestroy()
        {
            ClearSelectedTexture();
        }
    }

    [Serializable]
    public sealed class PixabaySearchResponse
    {
        public int total;
        public int totalHits;
        public List<PixabayImageResult> hits;
    }

    [Serializable]
    public sealed class PixabayImageResult
    {
        public int id;
        public string pageURL;
        public string type;
        public string tags;
        public string previewURL;
        public string webformatURL;
        public string largeImageURL;
        public string user;
        public string userImageURL;
        public int imageWidth;
        public int imageHeight;

        public string AttributionText => string.IsNullOrWhiteSpace(user)
            ? "Image from Pixabay"
            : $"Image by {user} on Pixabay";
    }
}
