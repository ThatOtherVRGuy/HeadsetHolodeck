// SPDX-License-Identifier: MIT

using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace WorldLabs.Editor
{
    /// <summary>
    /// The developer's standing choice of build credential intent, remembered per machine and per
    /// project in EditorPrefs. It is deliberately not stored in the repository: whether a build is
    /// private or shareable is a property of the person building, not of the checkout.
    /// </summary>
    public static class BuildCredentialSettings
    {
        const string PreferencePrefix = "HeadsetHolodeck.BuildCredentials.Mode";

        /// <summary>The saved declaration, or null when this machine has not chosen yet.</summary>
        public static string SavedValue
        {
            get
            {
                string value = EditorPrefs.GetString(PreferenceKey, string.Empty);
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }
        }

        public static BuildCredentialMode Mode
        {
            get
            {
                try
                {
                    return BuildEnvCredentials.ResolveFromProcess(SavedValue);
                }
                catch (BuildCredentialModeException)
                {
                    // A stale or hand-edited preference must not wedge every build; treat it as unset.
                    return BuildCredentialMode.Unspecified;
                }
            }
        }

        public static void Save(BuildCredentialMode mode)
        {
            if (mode == BuildCredentialMode.Unspecified)
            {
                Clear();
                return;
            }

            EditorPrefs.SetString(PreferenceKey, DescribeValue(mode));
        }

        public static void Clear()
        {
            EditorPrefs.DeleteKey(PreferenceKey);
        }

        /// <summary>Human-readable name of the mode in force, including where it came from.</summary>
        public static string Describe()
        {
            string commandLine = BuildEnvCredentials.ReadCommandLineValue(Environment.GetCommandLineArgs());
            if (!string.IsNullOrWhiteSpace(commandLine))
                return $"{DescribeValue(SafeParse(commandLine))} (from {BuildEnvCredentials.ModeArgument})";

            string environment = Environment.GetEnvironmentVariable(BuildEnvCredentials.ModeEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(environment))
                return $"{DescribeValue(SafeParse(environment))} (from {BuildEnvCredentials.ModeEnvironmentVariable})";

            string saved = SavedValue;
            if (!string.IsNullOrWhiteSpace(saved))
                return $"{DescribeValue(SafeParse(saved))} (saved on this machine)";

            return "Not chosen — a build stops rather than embed credentials, until you pick Private or Distribution.";
        }

        public static string DescribeValue(BuildCredentialMode mode)
        {
            switch (mode)
            {
                case BuildCredentialMode.Private: return "Private build (embeds .env; do not share the artifact)";
                case BuildCredentialMode.Distribution: return "Distribution build (non-secret configuration only)";
                case BuildCredentialMode.Device: return "Device build (embeds nothing; the app reads .env from the device)";
                default: return "Unspecified";
            }
        }

        /// <summary>Path of the transient embedded copy a build writes.</summary>
        public static string EmbeddedEnvPath =>
            Path.Combine("Assets", "Resources", "WorldLabs", BuildEnvCredentials.EmbeddedFileName);

        /// <summary>Project-root .env path.</summary>
        public static string ProjectEnvPath =>
            Path.Combine(Directory.GetParent(Application.dataPath).FullName, ".env");

        static BuildCredentialMode SafeParse(string raw)
        {
            try { return BuildEnvCredentials.ParseMode(raw); }
            catch (BuildCredentialModeException) { return BuildCredentialMode.Unspecified; }
        }

        static string PreferenceKey =>
            PreferencePrefix + "." + HashProjectPath(Directory.GetParent(Application.dataPath).FullName);

        static string HashProjectPath(string projectPath)
        {
            unchecked
            {
                uint hash = 2166136261u;
                foreach (char c in projectPath ?? string.Empty)
                {
                    hash = (hash ^ char.ToLowerInvariant(c)) * 16777619u;
                }
                return hash.ToString("x8");
            }
        }
    }
}
