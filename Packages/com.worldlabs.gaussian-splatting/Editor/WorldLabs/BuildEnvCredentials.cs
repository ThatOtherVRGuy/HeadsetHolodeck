// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace WorldLabs.Editor
{
    /// <summary>
    /// How much of the project-root .env a build is allowed to carry.
    ///
    /// <see cref="Private"/> is a build for the developer's own machine and may embed every entry.
    /// <see cref="Distribution"/> is a build handed to anyone else and may embed only the
    /// non-secret configuration allowlist; credentials and unrelated entries are dropped.
    /// <see cref="Device"/> is a build for a machine that keeps its own .env where the app can read
    /// it (<c>Application.persistentDataPath/.env</c>, e.g. the headset's app files directory), so
    /// the artifact embeds nothing at all.
    /// <see cref="Unspecified"/> means nobody has declared intent yet, so the build refuses to
    /// embed credentials rather than guessing.
    /// </summary>
    public enum BuildCredentialMode
    {
        Unspecified = 0,
        Private = 1,
        Distribution = 2,
        Device = 3,
    }

    /// <summary>Raised when a build cannot continue because credential intent is missing or invalid.</summary>
    public sealed class BuildCredentialModeException : Exception
    {
        public BuildCredentialModeException(string message) : base(message) { }
    }

    /// <summary>What <see cref="BuildEnvCredentials.Embed"/> actually did, for logging and fixtures.</summary>
    public sealed class BuildEnvEmbedResult
    {
        public bool WroteEmbeddedFile;
        public bool RemovedStaleEmbeddedFile;
        public readonly List<string> IncludedKeys = new List<string>();
        public readonly List<string> ExcludedKeys = new List<string>();
        public string Reason = string.Empty;
    }

    /// <summary>
    /// The single decision point for which .env entries a build may embed. Kept free of Unity
    /// build callbacks so fixtures can drive every branch against throwaway directories.
    ///
    /// Two rules decide what a distribution build may carry:
    ///   1. a key whose name looks like a credential ("..._API_KEY", "..._TOKEN", "..._SECRET") is
    ///      never embedded, even if someone adds it to the allowlist by mistake;
    ///   2. anything else must be named in <see cref="DistributionSafeKeys"/>, so unrelated entries
    ///      the developer happens to keep in .env stay out of the artifact.
    /// </summary>
    public static class BuildEnvCredentials
    {
        /// <summary>Environment variable that declares build intent for any build path.</summary>
        public const string ModeEnvironmentVariable = "HOLODECK_CREDENTIAL_MODE";

        /// <summary>Unity command-line argument that declares build intent (wins over the environment).</summary>
        public const string ModeArgument = "-credentialMode";

        public const string PrivateValue = "private";
        public const string DistributionValue = "distribution";
        public const string DeviceValue = "device";

        /// <summary>
        /// Non-secret configuration a distribution build may embed. Every entry here is a base URL
        /// or endpoint override; no entry may carry a credential.
        /// </summary>
        public static readonly string[] DistributionSafeKeys =
        {
            "THREEDAISTUDIO_BASE_URL",
            "HITEM_BASE_URL",
            "FREESOUND_SEARCH_URL",
            "XENO_CANTO_SEARCH_URL",
            "OPENVERSE_AUDIO_SEARCH_URL",
        };

        // Deliberately broad: a false positive only costs one excluded entry in a shared build.
        static readonly string[] CredentialMarkers =
        {
            "KEY", "TOKEN", "SECRET", "PASSWORD", "PASSWD", "CREDENTIAL", "AUTH",
        };

        /// <summary>True when the key name looks like it carries a credential.</summary>
        public static bool IsCredentialShaped(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return false;

            string upper = key.Trim().ToUpperInvariant();
            foreach (string marker in CredentialMarkers)
            {
                if (upper.Contains(marker)) return true;
            }
            return false;
        }

        /// <summary>True when a distribution build may embed this entry.</summary>
        public static bool IsDistributionSafe(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return false;
            if (IsCredentialShaped(key)) return false;

            string trimmed = key.Trim();
            foreach (string allowed in DistributionSafeKeys)
            {
                if (string.Equals(trimmed, allowed, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        // ── Mode resolution ───────────────────────────────────────────────────

        /// <summary>Parses one declaration. Null/empty means "not declared".</summary>
        public static BuildCredentialMode ParseMode(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return BuildCredentialMode.Unspecified;

            string value = raw.Trim().ToLowerInvariant();
            switch (value)
            {
                case PrivateValue: return BuildCredentialMode.Private;
                case DistributionValue: return BuildCredentialMode.Distribution;
                case DeviceValue: return BuildCredentialMode.Device;
                default:
                    throw new BuildCredentialModeException(
                        $"Unknown build credential mode '{raw}'. Use '{PrivateValue}', '{DistributionValue}' or '{DeviceValue}' " +
                        $"(command line {ModeArgument}, environment {ModeEnvironmentVariable}, or " +
                        "menu 'Headset Holodeck > Build Credentials').");
            }
        }

        /// <summary>
        /// Command line wins over environment wins over the saved per-machine choice. When nothing
        /// declares intent the result is <see cref="BuildCredentialMode.Unspecified"/>.
        /// </summary>
        public static BuildCredentialMode Resolve(string commandLineValue, string environmentValue, string savedValue)
        {
            if (!string.IsNullOrWhiteSpace(commandLineValue)) return ParseMode(commandLineValue);
            if (!string.IsNullOrWhiteSpace(environmentValue)) return ParseMode(environmentValue);
            if (!string.IsNullOrWhiteSpace(savedValue)) return ParseMode(savedValue);
            return BuildCredentialMode.Unspecified;
        }

        /// <summary>Resolves intent from the current build process.</summary>
        public static BuildCredentialMode ResolveFromProcess(string savedValue)
        {
            return Resolve(ReadCommandLineValue(Environment.GetCommandLineArgs()),
                           Environment.GetEnvironmentVariable(ModeEnvironmentVariable),
                           savedValue);
        }

        /// <summary>Reads <c>-credentialMode value</c> from a command line, or null.</summary>
        public static string ReadCommandLineValue(string[] arguments)
        {
            if (arguments == null) return null;
            for (int i = 0; i < arguments.Length - 1; i++)
            {
                if (string.Equals(arguments[i], ModeArgument, StringComparison.Ordinal)) return arguments[i + 1];
            }
            return null;
        }

        // ── .env parsing and filtering ────────────────────────────────────────

        /// <summary>One parsed .env entry, keeping the original line so private builds can copy verbatim.</summary>
        public sealed class Entry
        {
            public string Key;
            public string Value;
            public string Line;
        }

        /// <summary>Parses .env content with the same rules EnvLoader uses at runtime.</summary>
        public static List<Entry> Parse(string content)
        {
            var entries = new List<Entry>();
            if (string.IsNullOrEmpty(content)) return entries;

            foreach (string raw in content.Split('\n'))
            {
                string line = raw.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;

                int eq = line.IndexOf('=');
                if (eq <= 0) continue;

                string key = line.Substring(0, eq).Trim();
                string value = line.Substring(eq + 1).Trim();
                if (value.Length >= 2 &&
                    ((value[0] == '"' && value[value.Length - 1] == '"') ||
                     (value[0] == '\'' && value[value.Length - 1] == '\'')))
                {
                    value = value.Substring(1, value.Length - 2);
                }

                entries.Add(new Entry { Key = key, Value = value, Line = line });
            }
            return entries;
        }

        /// <summary>True when any entry carries a non-empty credential.</summary>
        public static bool HasCredentials(string content)
        {
            foreach (Entry entry in Parse(content))
            {
                if (IsCredentialShaped(entry.Key) && !string.IsNullOrEmpty(entry.Value)) return true;
            }
            return false;
        }

        /// <summary>Builds the distribution-safe document from .env content.</summary>
        public static string FilterForDistribution(string content, out List<string> includedKeys, out List<string> excludedKeys)
        {
            includedKeys = new List<string>();
            excludedKeys = new List<string>();

            var builder = new StringBuilder();
            builder.Append("# Generated by BuildEnvCopier for a distribution build.\n");
            builder.Append("# Only non-secret configuration is embedded; credentials are never included.\n");

            foreach (Entry entry in Parse(content))
            {
                if (IsDistributionSafe(entry.Key))
                {
                    builder.Append(entry.Line).Append('\n');
                    includedKeys.Add(entry.Key);
                }
                else
                {
                    excludedKeys.Add(entry.Key);
                }
            }
            return builder.ToString();
        }

        // ── Embedding ─────────────────────────────────────────────────────────

        /// <summary>File name used for the embedded copy; kept here so fixtures can assert on it.</summary>
        public const string EmbeddedFileName = "worldlabs_env.txt";

        /// <summary>
        /// Prepares (or removes) the file a build embeds. Never writes a partial document: the
        /// content lands on a hidden temporary path and is renamed into place.
        ///
        /// Throws <see cref="BuildCredentialModeException"/> when intent is
        /// <see cref="BuildCredentialMode.Unspecified"/> and the .env holds credentials, so a build
        /// can never embed them just because nobody chose.
        /// </summary>
        public static BuildEnvEmbedResult Embed(string envPath, string embeddedPath, BuildCredentialMode mode)
        {
            if (string.IsNullOrWhiteSpace(embeddedPath)) throw new ArgumentException("Missing embedded path.", nameof(embeddedPath));

            var result = new BuildEnvEmbedResult();
            bool envExists = !string.IsNullOrWhiteSpace(envPath) && File.Exists(envPath);
            string content = envExists ? File.ReadAllText(envPath) : string.Empty;

            switch (mode)
            {
                case BuildCredentialMode.Private:
                    if (!envExists)
                    {
                        result.Reason = "no .env at " + envPath;
                        result.RemovedStaleEmbeddedFile = RemoveEmbedded(embeddedPath);
                        return result;
                    }

                    WriteAtomically(embeddedPath, content);
                    result.WroteEmbeddedFile = true;
                    foreach (Entry entry in Parse(content)) result.IncludedKeys.Add(entry.Key);
                    result.Reason = "private build: every .env entry embedded";
                    return result;

                case BuildCredentialMode.Distribution:
                    // Drop anything a previous (possibly crashed) private build left behind first.
                    result.RemovedStaleEmbeddedFile = RemoveEmbedded(embeddedPath);
                    if (!envExists)
                    {
                        result.Reason = "distribution build: no .env to filter";
                        return result;
                    }

                    string filtered = FilterForDistribution(content, out List<string> included, out List<string> excluded);
                    result.IncludedKeys.AddRange(included);
                    result.ExcludedKeys.AddRange(excluded);
                    if (included.Count == 0)
                    {
                        result.Reason = "distribution build: nothing embeddable, no credentials included";
                        return result;
                    }

                    WriteAtomically(embeddedPath, filtered);
                    result.WroteEmbeddedFile = true;
                    result.Reason = $"distribution build: {included.Count} safe entr{(included.Count == 1 ? "y" : "ies")} embedded, {excluded.Count} excluded";
                    return result;

                case BuildCredentialMode.Device:
                    // The target keeps its own .env, so the artifact must carry nothing — not even
                    // the non-secret configuration. Remove any stale copy an earlier build left.
                    result.RemovedStaleEmbeddedFile = RemoveEmbedded(embeddedPath);
                    if (envExists)
                    {
                        foreach (Entry entry in Parse(content)) result.ExcludedKeys.Add(entry.Key);
                    }
                    result.Reason = "device build: nothing embedded; the app reads .env from its own " +
                                    "persistent data path on the target device";
                    return result;

                default:
                    if (!envExists || !HasCredentials(content))
                    {
                        result.RemovedStaleEmbeddedFile = RemoveEmbedded(embeddedPath);
                        result.Reason = envExists
                            ? "no build credential mode set and no credentials to embed"
                            : "no build credential mode set and no .env present";
                        return result;
                    }

                    throw new BuildCredentialModeException(
                        "Build credential intent is not set and this project's .env contains credentials, so the " +
                        "build was stopped instead of guessing. Choose one:\n" +
                        $"  • private build (embeds your .env, do not share the artifact): {ModeArgument} {PrivateValue} " +
                        $"or set {ModeEnvironmentVariable}={PrivateValue}, or menu 'Headset Holodeck > Build Credentials > Private'.\n" +
                        $"  • distribution build (embeds only non-secret configuration): {ModeArgument} {DistributionValue} " +
                        $"or set {ModeEnvironmentVariable}={DistributionValue}, or menu 'Headset Holodeck > Build Credentials > Distribution'.\n" +
                        $"  • device build (embeds nothing; the app reads .env from its own persistent data path): " +
                        $"{ModeArgument} {DeviceValue} or set {ModeEnvironmentVariable}={DeviceValue}, " +
                        "or menu 'Headset Holodeck > Build Credentials > Device'.\n" +
                        "No credentials were embedded and no build output was produced.");
            }
        }

        /// <summary>Removes the embedded copy and its Unity metadata. Returns true when something existed.</summary>
        public static bool RemoveEmbedded(string embeddedPath)
        {
            bool changed = false;
            try
            {
                if (File.Exists(embeddedPath))
                {
                    File.Delete(embeddedPath);
                    changed = true;
                }

                string meta = embeddedPath + ".meta";
                if (File.Exists(meta))
                {
                    File.Delete(meta);
                    changed = true;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[BuildEnvCopier] Could not remove embedded .env copy '{embeddedPath}': {ex.Message}");
            }
            return changed;
        }

        static void WriteAtomically(string path, string content)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            // Leading dot keeps Unity from importing the temporary as an asset and minting a .meta.
            string temporary = Path.Combine(directory ?? ".", "." + Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.WriteAllText(temporary, content, new UTF8Encoding(false));
                MoveIntoPlace(temporary, path);
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    try { File.Delete(temporary); } catch (Exception) { }
                }
            }
        }

        static void MoveIntoPlace(string temporary, string path)
        {
            if (!File.Exists(path))
            {
                File.Move(temporary, path);
                return;
            }

            try
            {
                File.Replace(temporary, path, null);
            }
            catch (Exception error) when (error is PlatformNotSupportedException ||
                                          error is NotSupportedException ||
                                          error is IOException)
            {
                // File.Replace is not implemented on every Mono/Unix profile Unity ships; the
                // document is regenerated by the next build either way, and the temporary is
                // removed by the caller's finally block if this throws too.
                File.Delete(path);
                File.Move(temporary, path);
            }
        }
    }
}
