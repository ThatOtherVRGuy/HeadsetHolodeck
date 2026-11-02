using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Holodeck.Save
{
    /// <summary>
    /// The single place that reads and writes a config folder's world.json.
    ///
    /// The JSON schema is unchanged. A save never truncates the committed file: the new document is
    /// written to a sibling temporary file, the previous valid document is preserved as
    /// <c>world.json.bak</c>, and only then is the temporary atomically renamed over the primary.
    /// Only committed world.json and world.json.bak are candidates for loading; an abandoned
    /// temporary file is never a commit. Readers therefore always see one complete generation, and
    /// an interrupted write cannot destroy the last valid save.
    /// </summary>
    internal static class WorldConfigFile
    {
        // Also covers metadata changes in the store. Stores are normally used on
        // Unity's main thread, but migrations and fixture writers run on workers.
        internal static readonly object Gate = new object();

        internal const string PrimaryFileName = "world.json";
        internal const string BackupSuffix = ".bak";
        internal const string TemporarySuffix = ".tmp";
        internal const string WriterLockFileName = ".world.json.lock";

        /// <summary>How long a writer waits for another process' in-flight save before giving up.</summary>
        const int WriterLockTimeoutMilliseconds = 5000;

        internal static string BackupPath(string path) => path + BackupSuffix;

        internal static string WriterLockPath(string directory) => Path.Combine(directory, WriterLockFileName);

        /// <summary>
        /// Serializes <paramref name="config"/> into <paramref name="path"/> without ever exposing a
        /// partially written document. Throws <see cref="UnauthorizedAccessException"/> when the
        /// destination is not writable for this install (the Android writable-copy repair relies on
        /// that type) and <see cref="IOException"/> for every other failure. In both cases the
        /// committed primary and the recovery backup are left as they were.
        /// </summary>
        /// <param name="checkpoint">
        /// Test-only fault injection hook: invoked with the current stage name and file path before
        /// and after each durable step so fixtures can simulate an interruption at an exact point.
        /// Production callers pass null.
        /// </param>
        internal static void Write(string path, WorldConfig config, Action<string, string> checkpoint = null)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));

            lock (Gate)
            {
                string directory = Path.GetDirectoryName(path);
                string temporary = path + "." + Guid.NewGuid().ToString("N") + TemporarySuffix;
                string backup = BackupPath(path);
                string backupTemporary = backup + "." + Guid.NewGuid().ToString("N") + TemporarySuffix;
                string stage = "serialize";
                try
                {
                    string json = JsonConvert.SerializeObject(config, Formatting.Indented);
                    // Never commit a document the loader would reject as a save.
                    Parse(json);

                    stage = "prepare directory";
                    Directory.CreateDirectory(directory);

                    stage = "acquire writer lock";
                    using (AcquireWriterLock(directory, path))
                    {
                        stage = "clear abandoned temporaries";
                        RemoveStaleTemporaries(directory, path, backup);

                        stage = "write temporary config";
                        WriteTemporary(temporary, json, checkpoint, "temp");

                        stage = "preserve valid primary";
                        string previous = ReadValidJson(path);
                        if (previous != null)
                        {
                            WriteTemporary(backupTemporary, previous, checkpoint, "backup");
                            AtomicReplace(backupTemporary, backup);
                            checkpoint?.Invoke("backup-committed", backupTemporary);
                        }
                        // A corrupt/missing primary must never overwrite a valid
                        // backup (including one just used by a recovery read).
                        stage = "commit primary";
                        checkpoint?.Invoke("before-primary-commit", temporary);
                        AtomicReplace(temporary, path);
                        checkpoint?.Invoke("primary-committed", path);
                    }
                }
                catch (UnauthorizedAccessException ex)
                {
                    // Preserve this type for the existing Android writable-copy repair.
                    throw new UnauthorizedAccessException(Failure(path, backup, stage, ex), ex);
                }
                catch (Exception ex)
                {
                    throw new IOException(Failure(path, backup, stage, ex), ex);
                }
                finally
                {
                    RemoveTemporary(temporary);
                    RemoveTemporary(backupTemporary);
                }
            }
        }

        static string Failure(string path, string backup, string stage, Exception cause) =>
            $"[WorldConfigStore] Save failed during {stage} for '{path}' ({cause.GetType().Name}: {cause.Message}). " +
            $"Existing committed data was not deleted; recovery backup: '{backup}'.";

        static void WriteTemporary(string path, string json, Action<string, string> checkpoint, string phase)
        {
            byte[] bytes = new UTF8Encoding(false, true).GetBytes(json);
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                checkpoint?.Invoke(phase + "-created", path);
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(flushToDisk: true);
            }
            checkpoint?.Invoke(phase + "-flushed", path);
        }

        /// <summary>
        /// Serializes writers from other processes (two app instances, or the editor and a build)
        /// that share one Worlds folder. The lock file is intentionally never deleted: removing it
        /// would let a new writer lock a different inode while the previous owner still holds the
        /// old one. Access failures (a read-only legacy folder) are not retried so the Android
        /// writable-copy repair still sees <see cref="UnauthorizedAccessException"/>.
        /// </summary>
        static FileStream AcquireWriterLock(string directory, string path)
        {
            string lockPath = WriterLockPath(directory);
            var clock = Stopwatch.StartNew();
            int attempt = 0;
            while (true)
            {
                try
                {
                    return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                }
                catch (IOException ex)
                {
                    if (clock.ElapsedMilliseconds >= WriterLockTimeoutMilliseconds)
                    {
                        throw new IOException(
                            $"[WorldConfigStore] Timed out after {WriterLockTimeoutMilliseconds} ms waiting for another writer to release '{lockPath}'. " +
                            $"No committed data for '{path}' was modified.",
                            ex);
                    }

                    Thread.Sleep(Math.Min(50, 5 + attempt++ * 5));
                }
            }
        }

        /// <summary>
        /// Clears temporaries abandoned by a killed process. They can never be a commit, so deleting
        /// them while holding the writer lock keeps the folder from growing without bound.
        /// </summary>
        static void RemoveStaleTemporaries(string directory, string path, string backup)
        {
            try
            {
                string primaryPrefix = Path.GetFileName(path) + ".";
                string backupPrefix = Path.GetFileName(backup) + ".";
                foreach (string candidate in Directory.GetFiles(directory))
                {
                    string name = Path.GetFileName(candidate);
                    if (!name.EndsWith(TemporarySuffix, StringComparison.Ordinal)) continue;
                    if (!name.StartsWith(primaryPrefix, StringComparison.Ordinal) &&
                        !name.StartsWith(backupPrefix, StringComparison.Ordinal)) continue;

                    RemoveTemporary(candidate);
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Debug.LogWarning($"[WorldConfigStore] Could not clear abandoned temporary files in '{directory}': {ex.Message}");
            }
        }

        /// <summary>
        /// Reads the committed save for <paramref name="path"/>. When the primary is missing,
        /// unreadable, or corrupt the previous committed generation in <c>world.json.bak</c> is used
        /// instead — that generation is one save behind by design, because the newest document only
        /// becomes the primary after it is complete. Returns null when neither file exists, and
        /// throws <see cref="IOException"/> (retaining both files) when a file exists but no valid
        /// save can be read from either candidate.
        /// </summary>
        internal static WorldConfig Read(string path)
        {
            lock (Gate)
            {
                Exception primaryError = null;
                try
                {
                    string json = ReadValidJson(path);
                    if (json != null) return Parse(json);
                    if (File.Exists(path)) primaryError = new InvalidDataException("Invalid config JSON.");
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    primaryError = ex;
                }

                string backup = BackupPath(path);
                Exception backupError = null;
                try
                {
                    string json = ReadValidJson(backup);
                    if (json != null)
                    {
                        Debug.LogWarning($"[WorldConfigStore] Primary save '{path}' is missing, unreadable, or corrupt; recovered the previous generation from '{backup}'. Both files are retained and the next successful save repairs the primary.");
                        return Parse(json);
                    }
                    if (File.Exists(backup)) backupError = new InvalidDataException("Invalid backup JSON.");
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    backupError = ex;
                }

                if (primaryError == null && backupError == null) return null;
                throw new IOException($"[WorldConfigStore] No valid save could be loaded from primary '{path}' or backup '{backup}'. Files were retained. Primary: {primaryError?.Message ?? "missing"} Backup: {backupError?.Message ?? "missing"}", primaryError ?? backupError);
            }
        }

        // Invalid JSON can be replaced, but I/O/access failures propagate during
        // writes: an unreadable primary might still be the only valid save.
        static string ReadValidJson(string path)
        {
            string json;
            try { json = File.ReadAllText(path); }
            catch (FileNotFoundException) { return null; }
            catch (DirectoryNotFoundException) { return null; }
            try { Parse(json); return json; }
            catch (JsonException) { return null; }
        }

        static WorldConfig Parse(string json)
        {
            JObject value = JObject.Parse(json);
            // Retain schema-v1 defaults/optional fields and legacy folder-ID repair,
            // but do not mistake JSON null, {} or an unrelated object for a save.
            if (value["display_name"]?.Type != JTokenType.String && value["world_source"]?.Type != JTokenType.Object)
                throw new JsonSerializationException("Expected a world config object.");
            return value.ToObject<WorldConfig>();
        }

        static void RemoveTemporary(string path)
        {
            try { File.Delete(path); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Debug.LogWarning($"[WorldConfigStore] Could not remove abandoned temporary file '{path}': {ex.Message}");
            }
        }

        static void AtomicReplace(string source, string destination)
        {
#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX || UNITY_EDITOR_LINUX || UNITY_STANDALONE_LINUX || UNITY_ANDROID
            // Adjacent files are on the same filesystem. POSIX rename replaces the
            // destination atomically, without a delete/move gap (also on Android).
            if (Rename(source, destination) != 0)
            {
                int error = Marshal.GetLastWin32Error();
                if (error == 1 || error == 13)
                    throw new UnauthorizedAccessException($"rename failed ({error}): '{destination}'");
                throw new IOException($"Atomic rename failed ({error}): '{source}' -> '{destination}'.");
            }
#else
            // Windows/.NET replacement, with a same-directory rename for creation.
            // Unsupported platforms fail closed; never fall back to delete + move.
            if (File.Exists(destination)) File.Replace(source, destination, null);
            else File.Move(source, destination);
#endif
        }

#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
        [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "rename", SetLastError = true)]
        static extern int Rename(string oldPath, string newPath);
#elif UNITY_EDITOR_LINUX || UNITY_STANDALONE_LINUX || UNITY_ANDROID
        [DllImport("libc", EntryPoint = "rename", SetLastError = true)]
        static extern int Rename(string oldPath, string newPath);
#endif
    }
}
