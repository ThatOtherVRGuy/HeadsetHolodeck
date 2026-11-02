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
    ///   1. An explicit <c>filePath</c> passed by the caller.
    ///   2. The live .env beside the app's own data: <c>Application.persistentDataPath/.env</c>.
    ///      On a headset this is <c>/sdcard/Android/data/&lt;package&gt;/files/.env</c> and it is the
    ///      credential source a device build (<c>-credentialMode device</c>) relies on, because such
    ///      a build embeds nothing.
    ///   3. The project-root .env — Editor and desktop only, for convenience.
    ///   4. Resources TextAsset "WorldLabs/worldlabs_env" — a copy baked in by a private build.
    ///      Deliberately last so a baked copy can never shadow the device's own file.
    ///   5. System environment variables (per-key fallback, checked by <see cref="Get"/>).
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

            // ── 1. Explicit path wins ─────────────────────────────────────────────
            if (!string.IsNullOrEmpty(filePath))
            {
                TryLoadFromFile(filePath, announceMissing: true);
                _isLoaded = true;
                return;
            }

            // ── 2. The device's own .env (the only source available on Android) ───
            if (TryLoadFromFile(DeviceEnvPath)) { _isLoaded = true; return; }

            // ── 3. Project root, Editor/desktop only ──────────────────────────────
#if !UNITY_ANDROID || UNITY_EDITOR
            string projectRoot = Path.Combine(Directory.GetParent(Application.dataPath).FullName, ".env");
            if (!string.Equals(Path.GetFullPath(projectRoot), Path.GetFullPath(DeviceEnvPath), StringComparison.Ordinal) &&
                TryLoadFromFile(projectRoot))
            {
                _isLoaded = true;
                return;
            }
#endif

            // ── 4. A copy baked into the build by a private build (never shadows 2 or 3) ──
            var textAsset = Resources.Load<TextAsset>(ResourcesPath);
            if (textAsset != null)
            {
                ParseContent(textAsset.text);
                Debug.Log($"[EnvLoader] Loaded {_envVariables.Count} variables from the embedded Resources copy.");
                _isLoaded = true;
                return;
            }

            Debug.LogWarning($"[EnvLoader] No configuration found. Put a .env at '{DeviceEnvPath}' on the " +
                             "device (that is where provider keys are read from), or build with " +
                             $"-credentialMode private to embed one.");
            _isLoaded = true;
        }

        /// <summary>Where the app expects the device's own .env: its persistent data directory.</summary>
        public static string DeviceEnvPath => Path.Combine(Application.persistentDataPath, ".env");

        /// <summary>
        /// The order sources are consulted in, for logging and fixtures. Android has no project-root
        /// file, and the embedded copy is always last so a baked build cannot shadow the device .env.
        /// </summary>
        public static string[] DescribeSourceOrder(bool isAndroid)
        {
            return isAndroid
                ? new[] { "device .env (" + DeviceEnvPath + ")", "embedded resources", "process environment" }
                : new[] { "device .env (" + DeviceEnvPath + ")", "project-root .env", "embedded resources", "process environment" };
        }

        static bool TryLoadFromFile(string path, bool announceMissing = false)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                if (announceMissing)
                    Debug.LogWarning($"[EnvLoader] .env not found at '{path}'.");
                return false;
            }

            try
            {
                ParseContent(File.ReadAllText(path));
                Debug.Log($"[EnvLoader] Loaded {_envVariables.Count} variables from '{path}'.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EnvLoader] Failed to read '{path}': {ex.Message}");
            }
            return true;
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
