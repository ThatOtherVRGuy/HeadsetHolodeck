using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SpeechIntent
{
    public static class RuntimeDotEnv
    {
        const string FileName = ".env";

        static readonly Dictionary<string, string> Values = new Dictionary<string, string>(StringComparer.Ordinal);
        static bool _loaded;
        static string _loadedPath;

        public static string ExpectedPersistentPath => Path.Combine(Application.persistentDataPath, FileName);

        public static string LoadedPath
        {
            get
            {
                EnsureLoaded();
                return _loadedPath ?? string.Empty;
            }
        }

        public static string Get(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return string.Empty;

            EnsureLoaded();
            return Values.TryGetValue(key.Trim(), out string value) ? value : string.Empty;
        }

        public static string GetEnvironmentOrDotEnv(string key)
        {
            string value = Environment.GetEnvironmentVariable(key);
            return string.IsNullOrWhiteSpace(value) ? Get(key) : value;
        }

        public static void EnsureLoaded()
        {
            if (_loaded)
                return;

            _loaded = true;
            foreach (string path in CandidatePaths())
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                    continue;

                int count = LoadFile(path);
                if (count > 0)
                {
                    _loadedPath = path;
                    Debug.Log($"[RuntimeDotEnv] Loaded {count} variable(s) from {path}");
                }
                return;
            }

            Debug.Log($"[RuntimeDotEnv] No .env file found. Runtime .env path is {ExpectedPersistentPath}");
        }

        static IEnumerable<string> CandidatePaths()
        {
            yield return ExpectedPersistentPath;

#if !UNITY_ANDROID || UNITY_EDITOR
            yield return Path.Combine(Application.streamingAssetsPath, FileName);
            yield return Path.GetFullPath(Path.Combine(Application.dataPath, "..", FileName));
#endif
        }

        static int LoadFile(string path)
        {
            int loaded = 0;
            foreach (string raw in File.ReadAllLines(path))
            {
                if (!TryParseLine(raw, out string key, out string value))
                    continue;

                Values[key] = value;
                Environment.SetEnvironmentVariable(key, value);
                loaded++;
            }

            return loaded;
        }

        static bool TryParseLine(string raw, out string key, out string value)
        {
            key = string.Empty;
            value = string.Empty;

            string line = (raw ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#", StringComparison.Ordinal))
                return false;

            int eq = line.IndexOf('=');
            if (eq <= 0)
                return false;

            key = line.Substring(0, eq).Trim();
            value = line.Substring(eq + 1).Trim();
            if (string.IsNullOrWhiteSpace(key))
                return false;

            if (value.Length >= 2 &&
                ((value[0] == '"' && value[value.Length - 1] == '"') ||
                 (value[0] == '\'' && value[value.Length - 1] == '\'')))
            {
                value = value.Substring(1, value.Length - 2);
            }

            return true;
        }
    }
}
