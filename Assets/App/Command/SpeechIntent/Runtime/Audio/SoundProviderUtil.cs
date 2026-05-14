using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace SpeechIntent.Audio
{
    internal static class SoundProviderUtil
    {
        public static string Env(string key) =>
            global::SpeechIntent.RuntimeDotEnv.GetEnvironmentOrDotEnv(key);

        public static string SafeTrim(string value) =>
            string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

        public static string GuessExtension(string url, string fallback = ".mp3")
        {
            if (string.IsNullOrWhiteSpace(url)) return fallback;

            string noQuery = url;
            int queryIndex = noQuery.IndexOf('?');
            if (queryIndex >= 0) noQuery = noQuery.Substring(0, queryIndex);

            string ext = Path.GetExtension(noQuery);
            if (string.IsNullOrWhiteSpace(ext)) return fallback;
            ext = ext.ToLowerInvariant();
            return ext switch
            {
                ".wav" or ".ogg" or ".mp3" or ".aiff" or ".aif" => ext,
                _ => fallback
            };
        }

        public static AudioType GuessAudioType(string pathOrUrl)
        {
            string ext = GuessExtension(pathOrUrl, ".mp3");
            return ext switch
            {
                ".wav" => AudioType.WAV,
                ".ogg" => AudioType.OGGVORBIS,
                ".aiff" or ".aif" => AudioType.AIFF,
                ".mp3" => AudioType.MPEG,
                _ => AudioType.UNKNOWN
            };
        }

        public static async Task<byte[]> DownloadBytesAsync(string url, int timeoutSeconds = 30)
        {
            if (string.IsNullOrWhiteSpace(url))
                throw new ArgumentException("Audio URL is empty.", nameof(url));

            using UnityWebRequest request = UnityWebRequest.Get(url);
            request.timeout = timeoutSeconds;
            UnityWebRequestAsyncOperation op = request.SendWebRequest();
            while (!op.isDone) await Task.Yield();

            if (request.result != UnityWebRequest.Result.Success)
                throw new InvalidOperationException(
                    $"Download failed. HTTP={(long)request.responseCode}, Error={request.error}");

            return request.downloadHandler.data;
        }

        public static List<string> SplitTags(string raw)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(raw)) return result;
            string[] parts = raw.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string part in parts)
            {
                string tag = part.Trim();
                if (tag.Length > 0 && !result.Contains(tag)) result.Add(tag);
            }
            return result;
        }
    }
}
