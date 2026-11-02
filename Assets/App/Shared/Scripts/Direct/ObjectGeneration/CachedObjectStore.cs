using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using UnityEngine;

namespace Holodeck.Direct
{
    public class CachedObjectStore : MonoBehaviour
    {
        const string CachedObjectsFolderName = "CachedObjects";
        const string MetadataFileName = "object.json";
        const string DefaultModelFileName = "model.glb";

        string _testRootOverride;

        public string RootPath => _testRootOverride ?? Path.Combine(Application.persistentDataPath, "Worlds");
        public string CachedObjectsPath => Path.Combine(RootPath, CachedObjectsFolderName);

        public static CachedObjectStore GetOrCreate()
        {
            CachedObjectStore existing = FindFirstObjectByType<CachedObjectStore>(FindObjectsInactive.Include);
            if (existing != null) return existing;

            var go = new GameObject("CachedObjectStore");
            return go.AddComponent<CachedObjectStore>();
        }

        public static CachedObjectStore CreateForTesting(string rootPath)
        {
            var go = new GameObject("CachedObjectStore_Test");
            var store = go.AddComponent<CachedObjectStore>();
            store._testRootOverride = rootPath;
            return store;
        }

        public CachedObjectRecord SaveGeneratedObject(
            string canonicalName,
            string prompt,
            string providerName,
            string taskId,
            string modelUrl,
            byte[] modelBytes)
        {
            if (modelBytes == null) throw new ArgumentNullException(nameof(modelBytes));

            Directory.CreateDirectory(CachedObjectsPath);

            string now = DateTime.UtcNow.ToString("yyyy-MM-ddTHHmmssZ");
            string objectId = MakeObjectId(canonicalName);
            string objectDir = Path.Combine(CachedObjectsPath, objectId);
            while (Directory.Exists(objectDir))
            {
                objectId = MakeObjectId(canonicalName);
                objectDir = Path.Combine(CachedObjectsPath, objectId);
            }

            Directory.CreateDirectory(objectDir);

            string modelPath = Path.Combine(objectDir, DefaultModelFileName);
            File.WriteAllBytes(modelPath, modelBytes);

            var record = new CachedObjectRecord
            {
                object_id = objectId,
                canonical_name = canonicalName ?? "",
                provider = providerName ?? "",
                source_prompt = prompt ?? "",
                task_id = taskId ?? "",
                model_url = modelUrl ?? "",
                created_at = now,
                modified_at = now,
                file_size_bytes = modelBytes.LongLength,
                model_path = DefaultModelFileName
            };

            WriteRecord(record);
            return record;
        }

        public void Save(CachedObjectRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            if (!IsSafeObjectId(record.object_id))
                record.object_id = MakeObjectId(record.canonical_name);

            string now = DateTime.UtcNow.ToString("yyyy-MM-ddTHHmmssZ");
            if (string.IsNullOrWhiteSpace(record.created_at)) record.created_at = now;
            record.modified_at = now;

            WriteRecord(record);
        }

        public CachedObjectRecord Load(string objectId)
        {
            if (!IsSafeObjectId(objectId)) return null;

            string objectDir = GetObjectDirectory(objectId);
            if (objectDir == null) return null;

            string path = Path.Combine(objectDir, MetadataFileName);
            if (!File.Exists(path)) return null;

            return JsonConvert.DeserializeObject<CachedObjectRecord>(File.ReadAllText(path));
        }

        public CachedObjectRecord FindByName(string requestedName)
        {
            List<CachedObjectRecord> matches = FindAllByName(requestedName);
            return matches.Count > 0 ? matches[0] : null;
        }

        public List<CachedObjectRecord> ListAll()
        {
            var records = new List<CachedObjectRecord>();
            foreach (string jsonPath in EnumerateRecordPaths())
            {
                CachedObjectRecord record = ReadRecord(jsonPath);
                if (record != null)
                    records.Add(record);
            }

            records.Sort((a, b) => string.Compare(b?.modified_at, a?.modified_at, StringComparison.Ordinal));
            return records;
        }

        public bool Delete(string objectId)
        {
            string objectDir = GetObjectDirectory(objectId);
            if (objectDir == null || !Directory.Exists(objectDir))
                return false;

            Directory.Delete(objectDir, recursive: true);
            return true;
        }

        public List<CachedObjectRecord> FindAllByName(string requestedName)
        {
            string normalizedRequest = NormalizeLookupName(requestedName);
            var matches = new List<CachedObjectRecord>();
            if (string.IsNullOrEmpty(normalizedRequest)) return matches;

            foreach (string jsonPath in EnumerateRecordPaths())
            {
                CachedObjectRecord record = ReadRecord(jsonPath);
                if (record == null) continue;
                if (MatchesLookup(record, normalizedRequest))
                    matches.Add(record);
            }

            return matches;
        }

        IEnumerable<string> EnumerateRecordPaths()
        {
            if (!Directory.Exists(CachedObjectsPath))
                yield break;

            string[] jsonPaths;
            try
            {
                jsonPaths = Directory.GetFiles(CachedObjectsPath, MetadataFileName, SearchOption.AllDirectories);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[CachedObjectStore] Could not enumerate cached objects: {ex.Message}");
                yield break;
            }

            foreach (string jsonPath in jsonPaths)
                yield return jsonPath;
        }

        CachedObjectRecord ReadRecord(string jsonPath)
        {
            try
            {
                return JsonConvert.DeserializeObject<CachedObjectRecord>(File.ReadAllText(jsonPath));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[CachedObjectStore] Could not parse {jsonPath}: {ex.Message}");
                return null;
            }
        }

        static bool MatchesLookup(CachedObjectRecord record, string normalizedRequest)
        {
            if (record == null) return false;
            if (NormalizeLookupName(record.canonical_name) == normalizedRequest) return true;

            if (MatchesAny(record.aliases, normalizedRequest)) return true;
            if (MatchesAny(record.tags, normalizedRequest)) return true;

            return false;
        }

        static bool MatchesAny(List<string> values, string normalizedRequest)
        {
            if (values == null) return false;
            foreach (string value in values)
            {
                if (NormalizeLookupName(value) == normalizedRequest)
                    return true;
            }

            return false;
        }

        public string GetModelAbsolutePath(CachedObjectRecord record)
        {
            if (record == null || !IsSafeObjectId(record.object_id)) return null;
            string modelPath = string.IsNullOrWhiteSpace(record.model_path) ? DefaultModelFileName : record.model_path;
            if (!IsSafeRelativePath(modelPath)) return null;

            string objectDir = GetObjectDirectory(record.object_id);
            if (objectDir == null) return null;

            string candidate = Path.GetFullPath(Path.Combine(objectDir, modelPath));
            return IsPathUnder(CachedObjectsPath, candidate) ? candidate : null;
        }

        public string GetThumbnailAbsolutePath(CachedObjectRecord record)
        {
            if (record == null || !IsSafeObjectId(record.object_id)) return null;
            if (string.IsNullOrWhiteSpace(record.thumbnail_path)) return null;
            if (!IsSafeRelativePath(record.thumbnail_path)) return null;

            string objectDir = GetObjectDirectory(record.object_id);
            if (objectDir == null) return null;

            string candidate = Path.GetFullPath(Path.Combine(objectDir, record.thumbnail_path));
            return IsPathUnder(objectDir, candidate) ? candidate : null;
        }

        public string SaveThumbnailFrame(CachedObjectRecord record, byte[] imageBytes, int frameIndex = 0, string extension = ".png")
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            if (imageBytes == null || imageBytes.Length == 0) throw new ArgumentException("Thumbnail image bytes are empty.", nameof(imageBytes));
            if (!IsSafeObjectId(record.object_id))
                record.object_id = MakeObjectId(record.canonical_name);

            string objectDir = GetObjectDirectory(record.object_id);
            if (objectDir == null)
                throw new InvalidOperationException("[CachedObjectStore] Could not create a safe cached object path.");

            Directory.CreateDirectory(objectDir);

            string cleanExtension = string.IsNullOrWhiteSpace(extension) ? ".png" : extension.Trim();
            if (!cleanExtension.StartsWith(".", StringComparison.Ordinal))
                cleanExtension = "." + cleanExtension;
            cleanExtension = Regex.Replace(cleanExtension.ToLowerInvariant(), @"[^.a-z0-9]", "");
            if (string.IsNullOrWhiteSpace(cleanExtension) || cleanExtension == ".")
                cleanExtension = ".png";

            string relativePath = $"thumb_{Mathf.Max(0, frameIndex):000}{cleanExtension}";
            string absolutePath = Path.Combine(objectDir, relativePath);
            File.WriteAllBytes(absolutePath, imageBytes);

            record.thumbnail_path = relativePath;
            record.thumbnail_frames ??= new List<string>();
            while (record.thumbnail_frames.Count <= frameIndex)
                record.thumbnail_frames.Add("");
            record.thumbnail_frames[frameIndex] = relativePath;
            record.thumbnail_animation ??= new CachedObjectThumbnailAnimation();
            record.thumbnail_animation.mode = record.thumbnail_frames.Count > 1 ? "ping_pong" : "still";
            if (record.thumbnail_animation.fps <= 0f)
                record.thumbnail_animation.fps = 2f;

            Save(record);
            return relativePath;
        }

        void WriteRecord(CachedObjectRecord record)
        {
            if (!IsSafeObjectId(record.object_id))
                record.object_id = MakeObjectId(record.canonical_name);

            string objectDir = GetObjectDirectory(record.object_id);
            if (objectDir == null)
                throw new InvalidOperationException("[CachedObjectStore] Could not create a safe cached object path.");

            Directory.CreateDirectory(objectDir);

            string path = Path.Combine(objectDir, MetadataFileName);
            File.WriteAllText(path, JsonConvert.SerializeObject(record, Formatting.Indented));
        }

        static string MakeObjectId(string canonicalName)
        {
            string source = string.IsNullOrWhiteSpace(canonicalName) ? "object" : canonicalName;
            string sanitized = Regex.Replace(source.Trim().ToLowerInvariant(), @"[^a-z0-9]+", "_").Trim('_');
            if (string.IsNullOrWhiteSpace(sanitized)) sanitized = "object";
            if (sanitized.Length > 40) sanitized = sanitized.Substring(0, 40).Trim('_');
            return $"{sanitized}_{DateTime.UtcNow:yyyy-MM-ddTHHmmssfff}Z_{Guid.NewGuid().ToString("N").Substring(0, 6)}";
        }

        string GetObjectDirectory(string objectId)
        {
            if (!IsSafeObjectId(objectId)) return null;

            string candidate = Path.GetFullPath(Path.Combine(CachedObjectsPath, objectId));
            return IsPathUnder(CachedObjectsPath, candidate) ? candidate : null;
        }

        static bool IsSafeObjectId(string objectId)
        {
            return !string.IsNullOrWhiteSpace(objectId)
                && Regex.IsMatch(objectId, @"^[A-Za-z0-9_.-]+$");
        }

        static bool IsSafeRelativePath(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath)) return false;
            if (Path.IsPathRooted(relativePath)) return false;

            string[] parts = relativePath.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string part in parts)
            {
                if (part == "..") return false;
            }

            return true;
        }

        static bool IsPathUnder(string rootPath, string candidatePath)
        {
            string root = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string candidate = Path.GetFullPath(candidatePath);
            return candidate.StartsWith(root, StringComparison.Ordinal);
        }

        static string NormalizeLookupName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";

            string normalized = Regex.Replace(name.Trim().ToLowerInvariant(), @"\s+", " ");
            normalized = Regex.Replace(normalized, @"^(a|an|the)\s+", "");
            return normalized;
        }
    }
}
