using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace SpeechIntent.Audio
{
    public enum SoundProviderPreference
    {
        Auto = 0,
        Freesound = 1,
        Openverse = 2,
        XenoCanto = 3
    }

    [Serializable]
    public sealed class SoundQuery
    {
        public string prompt = "";
        public string category = "";
        public string species = "";
        public string provider = "";
        public float maxDurationSeconds = 45f;
        public bool preferLoop = true;

        public string BestSearchText
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(species)) return species.Trim();
                if (!string.IsNullOrWhiteSpace(prompt)) return CleanSearchText(prompt);
                return CleanSearchText(category);
            }
        }

        static string CleanSearchText(string raw)
        {
            string value = (raw ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            value = Regex.Replace(value, @"^(add|create|get|find|download|play)\s+", "", RegexOptions.IgnoreCase);
            value = Regex.Replace(value, @"^(sound|sounds|audio)\s+(of|for)\s+", "", RegexOptions.IgnoreCase);
            value = Regex.Replace(value, @"\b(sound|sounds|audio|ambient|ambience|ambiance|background|soundscape)\b", " ", RegexOptions.IgnoreCase);
            value = Regex.Replace(value, @"\s+", " ").Trim();
            return string.IsNullOrWhiteSpace(value) ? raw.Trim() : value;
        }
    }

    [Serializable]
    public sealed class SoundSearchResult
    {
        public string id = "";
        public string title = "";
        public string provider = "";
        public string creator = "";
        public string license = "";
        public string licenseUrl = "";
        public string landingUrl = "";
        public string audioUrl = "";
        public string previewUrl = "";
        public string fileExtension = "";
        public float durationSeconds;
        public List<string> tags = new List<string>();

        public string BestAudioUrl =>
            !string.IsNullOrWhiteSpace(previewUrl) ? previewUrl : audioUrl;
    }
}
