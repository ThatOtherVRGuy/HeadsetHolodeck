// Assets/App/Command/SpeechIntent/Runtime/UI/UrlHistoryStore.cs

using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpeechIntent
{
    /// <summary>
    /// Persists a recently-used URL list in PlayerPrefs.
    /// Thread-safe reads; writes must be on the main thread (PlayerPrefs requirement).
    /// </summary>
    public static class UrlHistoryStore
    {
        const string PrefsKey  = "ContentLoader_UrlHistory";
        const int    MaxUrls   = 20;

        /// <summary>Returns the saved URL list, most-recent first.</summary>
        public static List<string> Load()
        {
            string json = PlayerPrefs.GetString(PrefsKey, null);
            if (string.IsNullOrEmpty(json))
                return new List<string>();
            try
            {
                var wrapper = JsonUtility.FromJson<Wrapper>(json);
                return wrapper?.urls ?? new List<string>();
            }
            catch
            {
                return new List<string>();
            }
        }

        /// <summary>Prepend <paramref name="url"/> to history, deduplicate, trim to <see cref="MaxUrls"/>.</summary>
        public static void Save(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            var list = Load();
            list.Remove(url);
            list.Insert(0, url);
            if (list.Count > MaxUrls)
                list.RemoveRange(MaxUrls, list.Count - MaxUrls);
            PlayerPrefs.SetString(PrefsKey, JsonUtility.ToJson(new Wrapper { urls = list }));
            PlayerPrefs.Save();
        }

        public static void Clear()
        {
            PlayerPrefs.DeleteKey(PrefsKey);
            PlayerPrefs.Save();
        }

        [Serializable]
        class Wrapper
        {
            public List<string> urls = new List<string>();
        }
    }
}
