using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Holodeck.Editor
{
    /// <summary>
    /// Reads the .env file at the project root on every domain reload and injects
    /// each KEY=VALUE pair into the current process's environment variables.
    ///
    /// This makes secrets available via System.Environment.GetEnvironmentVariable()
    /// to any Editor or Play-mode code without ever committing them to source control.
    ///
    /// .env format: one KEY=VALUE per line; lines starting with # are comments;
    /// blank lines are ignored; values are not unquoted (keep them bare).
    /// </summary>
    [InitializeOnLoad]
    internal static class DotEnvLoader
    {
        static DotEnvLoader()
        {
            // Project root is one level above the Assets folder.
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string envPath     = Path.Combine(projectRoot, ".env");

            if (!File.Exists(envPath))
                return;

            int loaded = 0;
            foreach (string raw in File.ReadAllLines(envPath))
            {
                string line = raw.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("#"))
                    continue;

                int eq = line.IndexOf('=');
                if (eq <= 0)
                    continue;

                string key   = line.Substring(0, eq).Trim();
                string value = line.Substring(eq + 1).Trim();

                Environment.SetEnvironmentVariable(key, value);
                loaded++;
            }

            if (loaded > 0)
                Debug.Log($"[DotEnvLoader] Loaded {loaded} variable(s) from .env");
        }
    }
}
