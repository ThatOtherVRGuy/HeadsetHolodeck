using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Holodeck.Save;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using GaussianSplatting.Runtime;
using WorldLabs.Runtime;

namespace SpeechIntent
{
    public class ArchWorldInfoPanel : MonoBehaviour
    {
        const string DefaultWorldId = "__default__";
        const string StaticWorldAttribution = "ATTRIBUTION\nmodel by Set Blueprint Archive";

        [Header("Dependencies")]
        public WorldConfigStore worldConfigStore;
        public WorldConfigAutoSave worldConfigAutoSave;
        public WorldLabsWorldManager worldManager;

        [Header("Labels")]
        public TMP_Text worldNameLabel;
        public TMP_Text datesLabel;
        public TMP_Text sourceLabel;
        public TMP_Text sizeLabel;
        public TMP_Text costLabel;
        public TMP_Text attributionLabel;
        public TMP_Text statusLabel;

        [Header("Behavior")]
        public bool refreshAutomatically = true;
        public float refreshIntervalSeconds = 1f;
        public float statusMessageHoldSeconds = 12f;

        float _nextRefreshTime;
        float _statusMessageUntil;
        string _heldStatusMessage;
        bool _isLoadingWorld;
        string _activeWorldId;
        string _sizeConfigKey;
        bool _sizeRefreshRunning;
        long _configFolderBytes;
        long _cachedBytes;

        void OnEnable()
        {
            ResolveDependencies();
            SubscribeWorldEvents();
            ArchStatusBus.StatusPosted += HandleStatusPosted;
            Refresh();
            _nextRefreshTime = Time.unscaledTime + refreshIntervalSeconds;
        }

        void OnDisable()
        {
            UnsubscribeWorldEvents();
            ArchStatusBus.StatusPosted -= HandleStatusPosted;
        }

        void Update()
        {
            if (!refreshAutomatically || Time.unscaledTime < _nextRefreshTime) return;
            _nextRefreshTime = Time.unscaledTime + Mathf.Max(0.25f, refreshIntervalSeconds);
            Refresh();
        }

        public void Refresh()
        {
            WorldConfig config = worldConfigAutoSave != null ? worldConfigAutoSave.ActiveConfig : null;
            if (_isLoadingWorld)
            {
                ShowWorldLoading(_activeWorldId);
                return;
            }

            if (config == null || IsDefaultWorld(config.world_source?.world_id))
            {
                ResetToNoWorldLoaded();
                return;
            }

            _activeWorldId = config.world_source?.world_id;
            string configFolder = worldConfigStore != null && !string.IsNullOrWhiteSpace(config.config_id)
                ? Path.Combine(worldConfigStore.WorldsRootPath, config.config_id)
                : "";

            EnsureSizeRefresh(config, configFolder);

            SetText(worldNameLabel, string.IsNullOrWhiteSpace(config.display_name) ? "UNTITLED WORLD" : config.display_name);
            SetText(datesLabel, $"CREATED {FormatDate(config.created_at)}   MODIFIED {FormatDate(config.modified_at)}");
            SetText(sourceLabel, BuildSourceText(config));
            SetText(sizeLabel, _sizeRefreshRunning
                ? "SIZE CALCULATING..."
                : $"CONFIG {FormatBytes(_configFolderBytes)}   CACHED {FormatBytes(_cachedBytes)}   TOTAL {FormatBytes(_configFolderBytes + _cachedBytes)}");
            SetText(costLabel, "API COST NOT RECORDED");
            SetText(attributionLabel, BuildSavedAttributionText(config));
            SetStatusText($"OBJECTS {config.objects?.Count ?? 0}   PROMPTS {config.prompts?.Count ?? 0}");
        }

        public void ResetToNoWorldLoaded()
        {
            _isLoadingWorld = false;
            _activeWorldId = null;
            _sizeConfigKey = null;
            _sizeRefreshRunning = false;
            _configFolderBytes = 0L;
            _cachedBytes = 0L;
            SetText(worldNameLabel, "NO WORLD LOADED");
            SetText(datesLabel, "CREATED --   MODIFIED --");
            SetText(sourceLabel, "SOURCE STATIC WORLD");
            SetText(sizeLabel, "CONFIG 0 B   CACHED 0 B   TOTAL 0 B");
            SetText(costLabel, "API COST 0");
            SetText(attributionLabel, StaticWorldAttribution);
            SetStatusText("NO WORLD LOADED");
        }

        void ShowWorldLoading(string worldId)
        {
            _isLoadingWorld = true;
            _activeWorldId = worldId;
            _sizeConfigKey = null;
            _sizeRefreshRunning = false;
            _configFolderBytes = 0L;
            _cachedBytes = 0L;
            SetText(worldNameLabel, "LOADING WORLD");
            SetText(datesLabel, "CREATED --   MODIFIED --");
            SetText(sourceLabel, string.IsNullOrWhiteSpace(worldId) ? "SOURCE --" : $"SOURCE ID {worldId}");
            SetText(sizeLabel, "CONFIG 0 B   CACHED 0 B   TOTAL 0 B");
            SetText(costLabel, "API COST 0");
            SetText(attributionLabel, StaticWorldAttribution);
            SetStatusText("LOADING");
        }

        void HandleStatusPosted(ArchStatusMessage status)
        {
            if (status.level != ArchStatusLevel.Error && status.level != ArchStatusLevel.Warning)
                return;

            string prefix = status.level == ArchStatusLevel.Error ? "ERROR" : "WARNING";
            _heldStatusMessage = $"{prefix}: {status.message}";
            _statusMessageUntil = Time.unscaledTime + Mathf.Max(1f, statusMessageHoldSeconds);
            SetText(statusLabel, _heldStatusMessage);
        }

        void SetStatusText(string value)
        {
            if (!string.IsNullOrWhiteSpace(_heldStatusMessage) && Time.unscaledTime < _statusMessageUntil)
            {
                SetText(statusLabel, _heldStatusMessage);
                return;
            }

            _heldStatusMessage = null;
            SetText(statusLabel, value);
        }

        void EnsureSizeRefresh(WorldConfig config, string configFolder)
        {
            string key = $"{config.config_id}|{config.modified_at}|{config.world_source?.cached_splat}|{config.world_source?.cached_pano}|{config.world_source?.cached_thumbnail}";
            if (string.Equals(_sizeConfigKey, key, StringComparison.Ordinal) || _sizeRefreshRunning)
                return;

            _sizeConfigKey = key;
            _sizeRefreshRunning = true;
            _configFolderBytes = 0L;
            _cachedBytes = 0L;
            _ = RefreshSizeAsync(config, configFolder, key);
        }

        async Task RefreshSizeAsync(WorldConfig config, string configFolder, string key)
        {
            try
            {
                SizeSnapshot size = await Task.Run(() =>
                {
                    long configBytes = Directory.Exists(configFolder) ? GetDirectorySize(configFolder) : 0L;
                    long cachedBytes = GetReferencedCachedBytes(config, configFolder);
                    return new SizeSnapshot(configBytes, cachedBytes);
                });

                if (!string.Equals(_sizeConfigKey, key, StringComparison.Ordinal))
                    return;

                _configFolderBytes = size.configFolderBytes;
                _cachedBytes = size.cachedBytes;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ArchWorldInfoPanel] Could not calculate world size: " + ex.Message);
            }
            finally
            {
                if (string.Equals(_sizeConfigKey, key, StringComparison.Ordinal))
                    _sizeRefreshRunning = false;
            }
        }

        void HandleWorldLoadStarted(string worldId)
        {
            if (IsDefaultWorld(worldId))
            {
                ResetToNoWorldLoaded();
                return;
            }

            ShowWorldLoading(worldId);
        }

        void HandleWorldLoaded(string worldId, GaussianSplatRenderer renderer)
        {
            if (IsDefaultWorld(worldId))
            {
                ResetToNoWorldLoaded();
                return;
            }

            _isLoadingWorld = false;
            _activeWorldId = worldId;
            Refresh();
        }

        void HandleWorldUnloaded(string worldId)
        {
            if (string.IsNullOrWhiteSpace(_activeWorldId) || string.Equals(_activeWorldId, worldId, StringComparison.Ordinal))
                ResetToNoWorldLoaded();
        }

        void ResolveDependencies()
        {
            if (worldManager == null) worldManager = FindFirstObjectByType<WorldLabsWorldManager>();
            if (worldConfigAutoSave == null) worldConfigAutoSave = FindFirstObjectByType<WorldConfigAutoSave>();
            if (worldConfigStore == null) worldConfigStore = FindFirstObjectByType<WorldConfigStore>();
        }

        void SubscribeWorldEvents()
        {
            if (worldManager == null) return;
            worldManager.OnWorldLoadStarted += HandleWorldLoadStarted;
            worldManager.OnWorldLoaded += HandleWorldLoaded;
            worldManager.OnWorldUnloaded += HandleWorldUnloaded;
        }

        void UnsubscribeWorldEvents()
        {
            if (worldManager == null) return;
            worldManager.OnWorldLoadStarted -= HandleWorldLoadStarted;
            worldManager.OnWorldLoaded -= HandleWorldLoaded;
            worldManager.OnWorldUnloaded -= HandleWorldUnloaded;
        }

        string BuildSourceText(WorldConfig config)
        {
            WorldSourceData source = config.world_source;
            if (source == null) return "SOURCE --";

            var sb = new StringBuilder();
            sb.Append("SOURCE ");
            sb.Append(string.IsNullOrWhiteSpace(source.type) ? "unknown" : source.type);
            if (!string.IsNullOrWhiteSpace(source.world_id))
                sb.Append("   ID ").Append(source.world_id);
            if (!string.IsNullOrWhiteSpace(config.generation_model))
                sb.Append("   CREATE MODEL ").Append(config.generation_model);
            return sb.ToString();
        }

        string BuildSavedAttributionText(WorldConfig config)
        {
            var entries = new List<string>();
            if (config.objects != null)
            {
                foreach (SavedObject savedObject in config.objects)
                {
                    if (savedObject?.components == null) continue;
                    foreach (SavedComponent component in savedObject.components)
                    {
                        if (!string.Equals(component?.type, "AudioAttributionMetadata", StringComparison.OrdinalIgnoreCase))
                            continue;
                        entries.Add(FormatAttribution(component.data));
                    }
                }
            }

            if (entries.Count == 0)
                return "ATTRIBUTION\nNO AUDIO ATTRIBUTIONS RECORDED";

            return "ATTRIBUTION\n" + string.Join("\n", entries);
        }

        string BuildLiveAttributionText()
        {
            AudioAttributionMetadata[] live = FindObjectsByType<AudioAttributionMetadata>(FindObjectsSortMode.None);
            if (live.Length == 0)
                return "ATTRIBUTION\nNO AUDIO ATTRIBUTIONS RECORDED";

            var lines = new List<string>();
            foreach (AudioAttributionMetadata meta in live)
            {
                if (meta == null) continue;
                string title = Empty(meta.title, "Untitled");
                string creator = Empty(meta.creator, "unknown");
                string provider = Empty(meta.provider, "provider");
                string license = Empty(meta.license, "license unknown");
                lines.Add($"{title} / {creator} / {provider} / {license}");
            }

            return "ATTRIBUTION\n" + string.Join("\n", lines);
        }

        string FormatAttribution(JObject data)
        {
            if (data == null) return "Unknown audio attribution";
            string title = Empty(data["title"]?.Value<string>(), "Untitled");
            string creator = Empty(data["creator"]?.Value<string>(), "unknown");
            string provider = Empty(data["provider"]?.Value<string>(), "provider");
            string license = Empty(data["license"]?.Value<string>(), "license unknown");
            string landing = data["landing_url"]?.Value<string>();

            return string.IsNullOrWhiteSpace(landing)
                ? $"{title} / {creator} / {provider} / {license}"
                : $"{title} / {creator} / {provider} / {license}\n{landing}";
        }

        long GetReferencedCachedBytes(WorldConfig config, string configFolder)
        {
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddResolvedPath(paths, configFolder, config.world_source?.cached_splat);
            AddResolvedPath(paths, configFolder, config.world_source?.cached_pano);
            AddResolvedPath(paths, configFolder, config.world_source?.cached_thumbnail);

            if (config.objects != null)
            {
                foreach (SavedObject savedObject in config.objects)
                {
                    if (savedObject?.components == null) continue;
                    foreach (SavedComponent component in savedObject.components)
                    {
                        if (!string.Equals(component?.type, "AudioAttributionMetadata", StringComparison.OrdinalIgnoreCase))
                            continue;

                        string absolute = component.data?["cached_absolute_path"]?.Value<string>();
                        if (!string.IsNullOrWhiteSpace(absolute)) paths.Add(absolute);
                    }
                }
            }

            long total = 0L;
            foreach (string path in paths)
                if (File.Exists(path)) total += new FileInfo(path).Length;
            return total;
        }

        static void AddResolvedPath(HashSet<string> paths, string configFolder, string maybeRelative)
        {
            if (string.IsNullOrWhiteSpace(maybeRelative)) return;
            string path = Path.IsPathRooted(maybeRelative) || string.IsNullOrWhiteSpace(configFolder)
                ? maybeRelative
                : Path.GetFullPath(Path.Combine(configFolder, maybeRelative));
            paths.Add(path);
        }

        static long GetDirectorySize(string dir)
        {
            long total = 0L;
            try
            {
                foreach (string file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                    total += new FileInfo(file).Length;
            }
            catch
            {
                return total;
            }
            return total;
        }

        static string FormatBytes(long bytes)
        {
            if (bytes < 1024L) return bytes + " B";
            if (bytes < 1024L * 1024L) return (bytes / 1024f).ToString("0.0") + " KB";
            if (bytes < 1024L * 1024L * 1024L) return (bytes / 1048576f).ToString("0.0") + " MB";
            return (bytes / 1073741824f).ToString("0.00") + " GB";
        }

        static string FormatDate(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "--";
            return DateTime.TryParse(raw, out DateTime dt)
                ? dt.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
                : raw;
        }

        static void SetText(TMP_Text label, string value)
        {
            if (label != null) label.text = value ?? "";
        }

        static bool IsDefaultWorld(string worldId) =>
            string.Equals(worldId, DefaultWorldId, StringComparison.Ordinal);

        static string Empty(string value, string fallback) =>
            string.IsNullOrWhiteSpace(value) ? fallback : value;

        readonly struct SizeSnapshot
        {
            public SizeSnapshot(long configFolderBytes, long cachedBytes)
            {
                this.configFolderBytes = configFolderBytes;
                this.cachedBytes = cachedBytes;
            }

            public readonly long configFolderBytes;
            public readonly long cachedBytes;
        }
    }
}
