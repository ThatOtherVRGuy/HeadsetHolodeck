using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace WorldLabs.API
{
    /// <summary>
    /// Utility class to load environment variables from a .env file.
    ///
    /// Resolution order:
    ///   1. Resources TextAsset "WorldLabs/worldlabs_env" — embedded by the build
    ///      preprocessor; works on ALL platforms including Android/Pico.
    ///   2. System.IO file path — project root in the Editor, custom path if provided.
    ///   3. System environment variables (fallback).
    /// </summary>
    public static class EnvLoader
    {
        // Resource path written by BuildEnvCopier before each build.
        internal const string ResourcesPath = "WorldLabs/worldlabs_env";

        private static Dictionary<string, string> _envVariables;
        private static bool _isLoaded = false;

        /// <summary>
        /// Loads environment variables.
        /// </summary>
        /// <param name="filePath">
        /// Optional explicit file path. When null the standard resolution order is used.
        /// </param>
        public static void Load(string filePath = null)
        {
            _envVariables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // ── 1. Embedded Resources TextAsset (works on Android / all platforms) ──
            if (string.IsNullOrEmpty(filePath))
            {
                var textAsset = Resources.Load<TextAsset>(ResourcesPath);
                if (textAsset != null)
                {
                    ParseContent(textAsset.text);
                    Debug.Log($"[EnvLoader] Loaded {_envVariables.Count} variables from embedded Resources.");
                    _isLoaded = true;
                    return;
                }
            }

            // ── 2. File system ────────────────────────────────────────────────────
            if (string.IsNullOrEmpty(filePath))
            {
                // In the Editor, read straight from the project root for convenience.
                string projectRoot = Directory.GetParent(Application.dataPath).FullName;
                filePath = Path.Combine(projectRoot, ".env");
            }

            if (!File.Exists(filePath))
            {
                Debug.LogWarning($"[EnvLoader] .env not found at '{filePath}' and no embedded resource present.");
                _isLoaded = true;
                return;
            }

            try
            {
                ParseContent(File.ReadAllText(filePath));
                Debug.Log($"[EnvLoader] Loaded {_envVariables.Count} variables from '{filePath}'.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EnvLoader] Failed to read '{filePath}': {ex.Message}");
            }

            _isLoaded = true;
        }

        /// <summary>
        /// Gets an environment variable. Checks .env first, then system environment.
        /// </summary>
        public static string Get(string key, string defaultValue = null)
        {
            if (!_isLoaded) Load();

            if (_envVariables != null && _envVariables.TryGetValue(key, out string value))
                return value;

            string sysValue = Environment.GetEnvironmentVariable(key);
            return !string.IsNullOrEmpty(sysValue) ? sysValue : defaultValue;
        }

        /// <summary>Returns true if the key exists in .env or system environment.</summary>
        public static bool HasKey(string key)
        {
            if (!_isLoaded) Load();

            if (_envVariables != null && _envVariables.ContainsKey(key))
                return true;

            return !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key));
        }

        /// <summary>Discards cached values and reloads.</summary>
        public static void Reload()
        {
            _isLoaded = false;
            _envVariables = null;
            Load();
        }

        // ── Internal ──────────────────────────────────────────────────────────

        static void ParseContent(string content)
        {
            if (string.IsNullOrEmpty(content)) return;

            foreach (string raw in content.Split('\n'))
            {
                string line = raw.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;

                int eq = line.IndexOf('=');
                if (eq <= 0) continue;

                string key   = line.Substring(0, eq).Trim();
                string value = line.Substring(eq + 1).Trim();

                // Strip surrounding quotes
                if (value.Length >= 2 &&
                    ((value[0] == '"' && value[value.Length - 1] == '"') ||
                     (value[0] == '\'' && value[value.Length - 1] == '\'')))
                {
                    value = value.Substring(1, value.Length - 2);
                }

                _envVariables[key] = value;
            }
        }
    }
}
