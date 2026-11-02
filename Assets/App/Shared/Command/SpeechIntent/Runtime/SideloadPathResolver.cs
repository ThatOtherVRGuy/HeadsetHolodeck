using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace SpeechIntent
{
    /// <summary>
    /// Turns a spoken file reference into a real path inside the sideload folder.
    ///
    /// Sideloaded content lives in <c>Application.persistentDataPath/WorldContent</c>, and people do not
    /// speak paths: they say "castle dot glb", or "models slash castle dot glb", and expect the app to find
    /// the file. This resolves those forms, searches subfolders when the name is not at the top level, and
    /// leaves URLs and rooted paths alone.
    /// </summary>
    public static class SideloadPathResolver
    {
        /// <summary>Folder scanned for sideloaded worlds, panoramas and models.</summary>
        public static string Root => Path.Combine(Application.persistentDataPath, "WorldContent");

        static readonly string[] Extensions =
        {
            ".spz", ".ply", ".glb", ".gltf", ".obj", ".fbx", ".usdz", ".jpg", ".jpeg", ".png"
        };

        /// <summary>True when the text names a media file we know how to load.</summary>
        public static bool HasKnownExtension(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            string lower = text.Trim().ToLowerInvariant();
            return Extensions.Any(ext => lower.EndsWith(ext, StringComparison.Ordinal));
        }

        /// <summary>
        /// Rewrites spoken separators into real ones: "castle dot glb" -> "castle.glb",
        /// "models slash castle dot glb" -> "models/castle.glb". Case is preserved so a case-sensitive
        /// file system still matches; comparisons elsewhere are case-insensitive.
        /// </summary>
        public static string NormalizeSpokenPath(string spoken)
        {
            if (string.IsNullOrWhiteSpace(spoken))
                return string.Empty;

            string text = spoken.Trim();

            // Longest phrases first so "back slash" wins over "slash".
            var replacements = new (string Word, string Symbol)[]
            {
                ("back slash", "/"),
                ("backslash", "/"),
                ("underscore", "_"),
                ("under score", "_"),
                ("hyphen", "-"),
                ("dash", "-"),
                ("period", "."),
                ("dot", "."),
                ("slash", "/"),
            };

            foreach ((string word, string symbol) in replacements)
            {
                text = Regex.Replace(text, @"\b" + Regex.Escape(word) + @"\b", symbol,
                    RegexOptions.IgnoreCase);
            }

            // Drop whitespace around separators so "castle . glb" still reads as "castle.glb", then
            // collapse the runs of space that spoken filenames accumulate.
            text = Regex.Replace(text, @"\s*([./\\_\-])\s*", "$1");
            text = Regex.Replace(text, @"\s{2,}", " ").Trim();

            return text;
        }

        /// <summary>
        /// Resolves a spoken or written reference to an absolute path. Returns false only when the
        /// reference is a local file that cannot be found anywhere under the sideload folder.
        /// </summary>
        public static bool TryResolve(string spokenOrPath, out string absolutePath, out string note)
        {
            absolutePath = null;
            note = string.Empty;

            string candidate = NormalizeSpokenPath(spokenOrPath);
            if (string.IsNullOrEmpty(candidate))
                return false;

            if (candidate.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                candidate.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                absolutePath = candidate;
                note = "url";
                return true;
            }

            if (candidate.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
                candidate = candidate.Substring("file://".Length);

            if (Path.IsPathRooted(candidate))
            {
                absolutePath = candidate;
                note = File.Exists(candidate) ? "absolute path" : "absolute path (missing)";
                return File.Exists(candidate);
            }

            string normalised = candidate.Replace('\\', '/');
            string direct = Path.Combine(Root, normalised.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(direct))
            {
                absolutePath = direct;
                note = "sideload folder";
                return true;
            }

            // Not at the top level: search the sideload folder by file name. This is what makes
            // "castle dot glb" work when the file sits in WorldContent/models.
            string fileName = Path.GetFileName(normalised);
            if (string.IsNullOrEmpty(fileName) || !Directory.Exists(Root))
                return false;

            List<string> matches;
            try
            {
                matches = Directory.GetFiles(Root, "*", SearchOption.AllDirectories)
                    .Where(f => string.Equals(Path.GetFileName(f), fileName, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(f => f.Length)
                    .ToList();
            }
            catch (Exception ex)
            {
                note = "search failed: " + ex.Message;
                return false;
            }

            if (matches.Count == 0)
                return false;

            absolutePath = matches[0];
            note = matches.Count == 1
                ? "found in a subfolder"
                : $"ambiguous ({matches.Count} matches); using {absolutePath.Substring(Root.Length).TrimStart('/', '\\')}";
            return true;
        }

        /// <summary>Human-readable description of the sideload folder, for spoken errors.</summary>
        public static string DescribeRoot() => Root;
    }
}
