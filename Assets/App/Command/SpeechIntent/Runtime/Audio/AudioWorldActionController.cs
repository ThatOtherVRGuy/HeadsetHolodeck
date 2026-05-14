using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using GaussianSplatting.Runtime;
using Holodeck.Save;
using UnityEngine;
using UnityEngine.Networking;
using WorldLabs.API;
using WorldLabs.Runtime;

namespace SpeechIntent.Audio
{
    public sealed class AudioWorldActionController : MonoBehaviour
    {
        [Header("Providers")]
        public FreesoundProvider freesoundProvider;
        public OpenverseProvider openverseProvider;
        public XenoCantoSoundProvider xenoCantoProvider;

        [Header("Save / Cache")]
        public WorldConfigStore worldConfigStore;
        public WorldConfigAutoSave worldConfigAutoSave;
        public WorldLabsWorldManager worldManager;
        public InteractionMemory interactionMemory;
        [Tooltip("Used when no WorldConfigStore is assigned.")]
        public string fallbackCacheFolder = "";

        [Header("Automatic World Ambience")]
        public bool createAmbienceWhenWorldLoads = true;
        public int maxAutomaticWorldSounds = 3;
        public bool skipDefaultWorld = true;

        [Header("Placement")]
        public Transform defaultParent;
        public float defaultDistance = 2f;
        public float surfaceOffset = 0.03f;
        public float defaultSpatialBlend = 1f;
        public float minDistance = 1f;
        public float maxDistance = 30f;
        public float multiLayerSpreadRadius = 1.75f;

        [Header("Search")]
        public int resultLimit = 8;
        public float maxDurationSeconds = 45f;
        public bool playImmediately = true;
        public bool verboseLogging = true;

        public StringEvent onStatus;
        public StringEvent onError;

        private readonly HashSet<string> _worldsWithAmbience = new HashSet<string>();
        private readonly List<SpawnedAudio> _spawnedAudio = new List<SpawnedAudio>();
        private string _activeWorldId = "";
        private int _audioLifetimeVersion;
        private bool _isShuttingDown;

        private sealed class SpawnedAudio
        {
            public string worldId;
            public GameObject gameObject;
        }

        private void Awake()
        {
            if (string.IsNullOrWhiteSpace(fallbackCacheFolder))
                fallbackCacheFolder = Path.Combine(Application.persistentDataPath, "WorldContent");

            if (freesoundProvider == null)
                freesoundProvider = GetComponent<FreesoundProvider>();
            if (openverseProvider == null)
                openverseProvider = GetComponent<OpenverseProvider>();
            if (xenoCantoProvider == null)
                xenoCantoProvider = GetComponent<XenoCantoSoundProvider>();
            if (worldManager == null)
                worldManager = FindFirstObjectByType<WorldLabsWorldManager>();
            if (interactionMemory == null)
                interactionMemory = GetComponent<InteractionMemory>();
        }

        private void OnEnable()
        {
            if (worldManager != null)
            {
                worldManager.OnWorldLoadStarted += HandleWorldLoadStarted;
                worldManager.OnWorldLoaded += HandleWorldLoaded;
                worldManager.OnWorldUnloaded += HandleWorldUnloaded;
            }
        }

        private void OnDisable()
        {
            if (worldManager != null)
            {
                worldManager.OnWorldLoadStarted -= HandleWorldLoadStarted;
                worldManager.OnWorldLoaded -= HandleWorldLoaded;
                worldManager.OnWorldUnloaded -= HandleWorldUnloaded;
            }
        }

        private void OnApplicationQuit()
        {
            _isShuttingDown = true;
            StopAndDestroyAllWorldAudio();
        }

        private void OnDestroy()
        {
            _isShuttingDown = true;
            StopAndDestroyAllWorldAudio();
        }

        public void CreateAudioSource(
            VoiceIntentCommand command,
            SpatialSnapshot spatial,
            Action<GameObject> onCreated = null)
        {
            _ = CreateAudioSourceAsync(command, spatial, onCreated);
        }

        private async Task CreateAudioSourceAsync(
            VoiceIntentCommand command,
            SpatialSnapshot spatial,
            Action<GameObject> onCreated)
        {
            try
            {
                int lifetimeVersion = _audioLifetimeVersion;
                if (command == null)
                {
                    EmitError("No audio command was provided.");
                    return;
                }

                if (LooksLikeExistingAudioPlaybackRequest(command))
                {
                    GameObject existing = ResolveAudioTarget(command, allowUnnamedFallback: false);
                    if (existing == null)
                    {
                        string requested = FirstNonEmpty(command.target_name, command.sound_prompt, command.object_name, command.transcript);
                        EmitError($"Error: audio asset '{requested}' was not found.");
                        return;
                    }

                    AudioPlaybackController playback =
                        existing.GetComponent<AudioPlaybackController>() ?? existing.AddComponent<AudioPlaybackController>();
                    playback.PlayNow();
                    onCreated?.Invoke(existing);
                    EmitStatus($"Playing existing audio source '{existing.name}'.");
                    return;
                }

                List<SoundQuery> queries = BuildQueries(command);
                if (queries.Count == 0)
                {
                    EmitError("No sound prompt was provided.");
                    return;
                }

                for (int queryIndex = 0; queryIndex < queries.Count; queryIndex++)
                {
                    SoundQuery query = queries[queryIndex];
                    ISoundProvider provider = null;
                    SoundSearchResult result = null;
                    foreach (ISoundProvider candidate in BuildProviderOrder(query))
                    {
                        if (candidate == null)
                            continue;

                        if (!candidate.IsConfigured)
                        {
                            EmitProviderNotConfigured(candidate);
                            continue;
                        }

                        if (!candidate.CanHandle(query))
                            continue;

                        EmitStatus($"Searching {candidate.ProviderName} for '{query.BestSearchText}'...");
                        List<SoundSearchResult> results = await candidate.SearchAsync(query, resultLimit);
                        result = PickBestResult(results, query);
                        if (result != null)
                        {
                            provider = candidate;
                            break;
                        }
                    }

                    if (provider == null || result == null)
                    {
                        EmitError($"No audio result found for '{query.BestSearchText}'.");
                        continue;
                    }

                    if (lifetimeVersion != _audioLifetimeVersion)
                    {
                        EmitStatus("Audio request cancelled because the world changed.");
                        return;
                    }

                    EmitStatus($"Downloading '{result.title}' from {result.provider}...");
                    byte[] bytes = await provider.DownloadAudioBytesAsync(result);
                    if (bytes == null || bytes.Length == 0)
                    {
                        EmitError("Downloaded audio was empty.");
                        continue;
                    }

                    string extension = SoundProviderUtil.GuessExtension(result.BestAudioUrl, result.fileExtension);
                    string cacheDir = ResolveCacheDirectory();
                    Directory.CreateDirectory(cacheDir);

                    string fileName = BuildCacheFileName(result, extension);
                    string absolutePath = Path.Combine(cacheDir, fileName);
                    await Task.Run(() => File.WriteAllBytes(absolutePath, bytes));

                    AudioClip clip = await LoadClipFromFileAsync(absolutePath);
                    if (clip == null)
                    {
                        EmitError("Audio downloaded, but Unity could not decode the clip.");
                        continue;
                    }

                    if (lifetimeVersion != _audioLifetimeVersion)
                    {
                        Destroy(clip);
                        EmitStatus("Audio request cancelled because the world changed.");
                        return;
                    }

                    GameObject go = SpawnAudioObject(
                        command,
                        spatial,
                        query,
                        result,
                        clip,
                        absolutePath,
                        fileName,
                        queryIndex,
                        queries.Count);
                    RegisterSpawnedAudio(go);
                    go.GetComponent<AudioPlaybackController>()?.Restart(playImmediately);

                    onCreated?.Invoke(go);
                    EmitStatus($"Playing '{result.title}'.");
                }
            }
            catch (Exception ex)
            {
                EmitError(ex.Message);
            }
        }

        public bool ApplyAudioControl(VoiceIntentCommand command, Action<GameObject> onMutated = null)
        {
            if (IsAllTarget(command))
                return ApplyAudioControlToAll(command, onMutated);

            GameObject target = ResolveAudioTarget(command);
            if (target == null)
            {
                EmitError("No matching audio source was found.");
                return false;
            }

            string control = (command.audio_control ?? string.Empty).Trim().ToLowerInvariant();
            bool changed = ApplyAudioControlToTarget(target, command, control, ensurePlaybackController: true);

            if (changed)
            {
                onMutated?.Invoke(target);
                EmitStatus($"Updated audio source '{target.name}'.");
            }

            return changed;
        }

        public int DeleteAudioTargets(VoiceIntentCommand command)
        {
            if (IsAllTarget(command) ||
                (command != null &&
                 command.target_reference != TargetReferenceMode.PointedObject &&
                 IsAudioCategoryTarget(command)))
            {
                return StopAndDestroyAllWorldAudio();
            }

            GameObject target = ResolveAudioTarget(command);
            if (target == null || IsUxAudioObject(target))
            {
                EmitError("I could not find that audio source.");
                return 0;
            }

            DestroyWorldAudioTarget(target);
            EmitStatus($"Deleted audio source '{target.name}'.");
            return 1;
        }

        private bool ApplyAudioControlToAll(VoiceIntentCommand command, Action<GameObject> onMutated)
        {
            List<GameObject> targets = GetWorldAudioTargets();
            if (targets.Count == 0)
            {
                EmitError("No world audio sources were found.");
                return false;
            }

            string control = (command.audio_control ?? string.Empty).Trim().ToLowerInvariant();
            int changedCount = 0;
            foreach (GameObject target in targets)
            {
                if (ApplyAudioControlToTarget(target, command, control, ensurePlaybackController: false))
                {
                    changedCount++;
                    onMutated?.Invoke(target);
                }
            }

            if (changedCount > 0)
                EmitStatus($"Updated {changedCount} world audio source(s).");

            return changedCount > 0;
        }

        private bool ApplyAudioControlToTarget(
            GameObject target,
            VoiceIntentCommand command,
            string control,
            bool ensurePlaybackController)
        {
            if (target == null)
                return false;

            AudioSource source = target.GetComponent<AudioSource>();
            if (source == null)
                return false;

            AudioPlaybackController playback = ensurePlaybackController
                ? target.GetComponent<AudioPlaybackController>() ?? target.AddComponent<AudioPlaybackController>()
                : target.GetComponent<AudioPlaybackController>();

            bool changed = false;

            if (control == "stop")
            {
                if (playback != null)
                    playback.StopPlayback();
                else
                    source.Stop();
                return true;
            }

            if (control == "louder" || command.audio_volume_delta > 0f)
            {
                float delta = command.audio_volume_delta > 0f ? command.audio_volume_delta : 0.15f;
                source.volume = Mathf.Clamp01(source.volume + delta);
                changed = true;
            }
            else if (control == "softer" || control == "quieter" || command.audio_volume_delta < 0f)
            {
                float delta = command.audio_volume_delta < 0f ? -command.audio_volume_delta : 0.15f;
                source.volume = Mathf.Clamp01(source.volume - delta);
                changed = true;
            }

            if (control == "set_volume" && command.audio_volume >= 0f)
            {
                source.volume = Mathf.Clamp01(command.audio_volume);
                changed = true;
            }

            if (control == "mute" || command.audio_muted)
            {
                source.mute = true;
                changed = true;
            }
            else if (control == "unmute")
            {
                source.mute = false;
                changed = true;
            }

            if (control == "make_ambient")
            {
                source.spatialBlend = 0f;
                changed = true;
            }
            else if (control == "make_spatialized")
            {
                source.spatialBlend = 1f;
                changed = true;
            }
            else if (command.audio_spatial_blend >= 0f && control == "set_spatial_blend")
            {
                source.spatialBlend = Mathf.Clamp01(command.audio_spatial_blend);
                changed = true;
            }

            AudioPlaybackMode? requestedMode = TryParsePlaybackMode(command.audio_playback_mode);
            if (playback != null &&
                (requestedMode.HasValue ||
                 control == "set_interval" ||
                 control == "set_random_interval" ||
                 control == "play_once" ||
                 control == "loop"))
            {
                AudioPlaybackMode mode = requestedMode ?? playback.mode;
                if (control == "set_interval") mode = AudioPlaybackMode.Interval;
                if (control == "set_random_interval") mode = AudioPlaybackMode.RandomInterval;
                if (control == "play_once") mode = AudioPlaybackMode.Once;
                if (control == "loop") mode = AudioPlaybackMode.Loop;

                float interval = command.audio_interval_seconds > 0f
                    ? command.audio_interval_seconds
                    : playback.intervalSeconds;
                float variance = command.audio_interval_variance_seconds > 0f
                    ? command.audio_interval_variance_seconds
                    : playback.intervalVarianceSeconds;

                playback.Configure(mode, interval, variance, false);
                changed = true;
            }

            if (control == "play_now" || command.audio_play_now)
            {
                if (playback != null)
                    playback.PlayNow();
                else if (source.clip != null)
                    source.Play();
                changed = true;
            }

            return changed;
        }

        private List<SoundQuery> BuildQueries(VoiceIntentCommand command)
        {
            var prompts = new List<string>();
            if (command.sound_prompts != null)
            {
                foreach (string prompt in command.sound_prompts)
                    if (!string.IsNullOrWhiteSpace(prompt)) prompts.Add(prompt.Trim());
            }

            if (!string.IsNullOrWhiteSpace(command.sound_prompt))
                AddSplitPrompts(prompts, command.sound_prompt);

            if (prompts.Count == 0 && !string.IsNullOrWhiteSpace(command.transcript))
                prompts.Add(command.transcript.Trim());

            int max = command.sound_count > 0 ? Mathf.Min(command.sound_count, prompts.Count) : prompts.Count;
            if (max <= 0) max = prompts.Count;

            var queries = new List<SoundQuery>();
            for (int i = 0; i < prompts.Count && i < max; i++)
            {
                queries.Add(new SoundQuery
                {
                    prompt = prompts[i],
                    category = command.sound_category ?? string.Empty,
                    species = command.sound_species ?? string.Empty,
                    provider = command.sound_provider ?? string.Empty,
                    maxDurationSeconds = command.sound_max_duration_seconds > 0f
                        ? command.sound_max_duration_seconds
                        : maxDurationSeconds,
                    preferLoop = ResolvePlaybackMode(command, prompts[i], command.sound_category) == AudioPlaybackMode.Loop
                });
            }

            return queries;
        }

        private IEnumerable<ISoundProvider> BuildProviderOrder(SoundQuery query)
        {
            SoundProviderPreference pref = ParseProvider(query.provider);

            if (pref == SoundProviderPreference.Freesound)
            {
                yield return freesoundProvider;
                yield return openverseProvider;
                yield break;
            }

            if (pref == SoundProviderPreference.Openverse)
            {
                yield return openverseProvider;
                yield return freesoundProvider;
                yield break;
            }

            if (pref == SoundProviderPreference.XenoCanto)
            {
                yield return xenoCantoProvider;
                yield return freesoundProvider;
                yield return openverseProvider;
                yield break;
            }

            yield return xenoCantoProvider;
            yield return freesoundProvider;
            yield return openverseProvider;
        }

        private static SoundProviderPreference ParseProvider(string raw)
        {
            string value = (raw ?? string.Empty).Trim().ToLowerInvariant();
            return value switch
            {
                "freesound" => SoundProviderPreference.Freesound,
                "openverse" => SoundProviderPreference.Openverse,
                "xeno-canto" or "xenocanto" or "bird" or "birds" => SoundProviderPreference.XenoCanto,
                _ => SoundProviderPreference.Auto
            };
        }

        private void EmitProviderNotConfigured(ISoundProvider provider)
        {
            if (provider == null) return;

            string message = provider.ProviderName switch
            {
                "freesound" =>
                    "Freesound provider is not configured. Set FREESOUND_API_KEY in the environment or assign the API key on FreesoundProvider.",
                "xeno-canto" =>
                    "xeno-canto provider is not configured. Set XENO_CANTO_API_KEY in the environment or assign the API key on XenoCantoSoundProvider.",
                _ =>
                    $"{provider.ProviderName} provider is not configured."
            };

            Debug.LogWarning("[AudioWorldActionController] " + message, this);
            global::SpeechIntent.ArchStatusBus.Warning(message, "AUDIO");
            onError?.Invoke(message);
        }

        private static SoundSearchResult PickBestResult(List<SoundSearchResult> results, SoundQuery query)
        {
            if (results == null || results.Count == 0) return null;
            float maxDuration = query != null && query.maxDurationSeconds > 0f
                ? query.maxDurationSeconds
                : 45f;

            SoundSearchResult best = null;
            float bestScore = float.MinValue;
            foreach (SoundSearchResult result in results)
            {
                if (result == null || string.IsNullOrWhiteSpace(result.BestAudioUrl)) continue;
                if (result.durationSeconds > 0f && result.durationSeconds > maxDuration) continue;

                float score = ScoreSearchResult(result, query);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = result;
                }
            }

            if (best != null)
                return best;

            foreach (SoundSearchResult result in results)
                if (result != null && !string.IsNullOrWhiteSpace(result.BestAudioUrl))
                    return result;

            return null;
        }

        private static float ScoreSearchResult(SoundSearchResult result, SoundQuery query)
        {
            string queryText = NormalizeSearchText(
                FirstNonEmpty(query?.BestSearchText, query?.prompt, query?.category, query?.species));
            string title = NormalizeSearchText(result.title);
            string tagText = NormalizeSearchText(string.Join(" ", result.tags ?? new List<string>()));
            string searchable = title + " " + tagText;

            float score = 0f;
            foreach (string term in BuildSearchTerms(queryText))
            {
                if (term.Length <= 2) continue;
                if (title.Contains(term, StringComparison.OrdinalIgnoreCase)) score += 5f;
                if (tagText.Contains(term, StringComparison.OrdinalIgnoreCase)) score += 2f;
            }

            if (query != null && query.preferLoop)
            {
                if (result.durationSeconds >= 8f) score += 1f;
                if (result.durationSeconds >= 20f) score += 1f;
            }

            if (IsNaturalAmbienceQuery(queryText) &&
                ContainsAny(searchable, "electric", "electronic", "synth", "synthesizer", "drone", "machine", "motor", "industrial", "space", "sci fi", "scifi"))
            {
                score -= 12f;
            }

            if (ContainsAny(queryText, "ocean", "sea", "beach", "shore", "coast", "wave", "waves", "surf") &&
                ContainsAny(searchable, "ocean", "sea", "beach", "shore", "coast", "wave", "waves", "surf"))
            {
                score += 8f;
            }

            if (ContainsAny(queryText, "forest", "woods", "jungle", "tree", "trees", "redwood") &&
                ContainsAny(searchable, "forest", "woods", "jungle", "tree", "trees", "bird", "birds", "leaves", "wind"))
            {
                score += 8f;
            }

            if (ContainsAny(queryText, "roman", "colosseum", "coliseum", "colusium", "arena", "gladiator") &&
                ContainsAny(searchable, "crowd", "cheer", "cheering", "arena", "people", "applause", "sword", "metal", "armor"))
            {
                score += 8f;
            }

            return score;
        }

        private static IEnumerable<string> BuildSearchTerms(string queryText)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddSearchTerm(seen, queryText);
            foreach (string token in queryText.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (IsSearchStopword(token)) continue;
                AddSearchTerm(seen, token);
            }

            if (ContainsAny(queryText, "ocean", "sea", "beach", "shore", "coast", "wave", "waves", "surf"))
                AddSearchTerms(seen, "ocean", "sea", "beach", "shore", "coast", "wave", "waves", "surf", "water");

            if (ContainsAny(queryText, "forest", "woods", "jungle", "tree", "trees", "redwood"))
                AddSearchTerms(seen, "forest", "woods", "jungle", "tree", "trees", "birds", "bird", "wind", "leaves");

            if (ContainsAny(queryText, "river", "stream", "creek", "waterfall", "rapids"))
                AddSearchTerms(seen, "river", "stream", "creek", "waterfall", "rapids", "water");

            if (ContainsAny(queryText, "rain", "storm", "thunder"))
                AddSearchTerms(seen, "rain", "storm", "thunder", "weather");

            if (ContainsAny(queryText, "roman", "colosseum", "coliseum", "colusium", "arena", "gladiator"))
                AddSearchTerms(seen, "crowd", "cheer", "cheering", "arena", "people", "applause", "sword", "metal", "armor");

            return seen;
        }

        private static void AddSearchTerms(HashSet<string> terms, params string[] values)
        {
            foreach (string value in values)
                AddSearchTerm(terms, value);
        }

        private static void AddSearchTerm(HashSet<string> terms, string value)
        {
            value = NormalizeSearchText(value);
            if (!string.IsNullOrWhiteSpace(value) && !IsSearchStopword(value))
                terms.Add(value);
        }

        private static bool IsSearchStopword(string token)
        {
            token = NormalizeSearchText(token);
            return string.IsNullOrWhiteSpace(token) ||
                   token == "sound" ||
                   token == "sounds" ||
                   token == "audio" ||
                   token == "ambient" ||
                   token == "ambience" ||
                   token == "ambiance" ||
                   token == "background" ||
                   token == "soundscape" ||
                   token == "of" ||
                   token == "the" ||
                   token == "a" ||
                   token == "an";
        }

        private static string NormalizeSearchText(string value)
        {
            value = (value ?? string.Empty).ToLowerInvariant();
            char[] chars = value.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (!char.IsLetterOrDigit(chars[i]))
                    chars[i] = ' ';
            }
            return string.Join(" ", new string(chars).Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }

        private static bool IsNaturalAmbienceQuery(string queryText)
        {
            return ContainsAny(queryText,
                "ocean", "sea", "beach", "shore", "coast", "wave", "waves", "surf",
                "forest", "woods", "jungle", "tree", "trees", "redwood",
                "river", "stream", "creek", "waterfall", "rapids",
                "rain", "storm", "thunder", "wind",
                "roman", "colosseum", "coliseum", "colusium", "arena");
        }

        private string ResolveCacheDirectory()
        {
            if (worldConfigStore != null)
                return worldConfigStore.CachedWorldsPath;
            return fallbackCacheFolder;
        }

        private string BuildCacheFileName(SoundSearchResult result, string extension)
        {
            string stem = SanitizeFileStem(result.title);
            if (string.IsNullOrWhiteSpace(stem)) stem = "Sound";
            string id = SanitizeFileStem(result.id);
            if (string.IsNullOrWhiteSpace(id)) id = DateTime.UtcNow.ToString("yyyyMMddTHHmmss");

            if (string.IsNullOrWhiteSpace(extension)) extension = ".mp3";
            if (!extension.StartsWith(".", StringComparison.Ordinal)) extension = "." + extension;
            return $"{stem}_{result.provider}_{id}{extension}";
        }

        private async Task<AudioClip> LoadClipFromFileAsync(string absolutePath)
        {
            string url = "file://" + absolutePath;
            using UnityWebRequest request =
                UnityWebRequestMultimedia.GetAudioClip(url, SoundProviderUtil.GuessAudioType(absolutePath));
            UnityWebRequestAsyncOperation op = request.SendWebRequest();
            while (!op.isDone) await Task.Yield();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[AudioWorldActionController] Clip decode failed: {request.error}");
                return null;
            }

            return DownloadHandlerAudioClip.GetContent(request);
        }

        private GameObject SpawnAudioObject(
            VoiceIntentCommand command,
            SpatialSnapshot spatial,
            SoundQuery query,
            SoundSearchResult result,
            AudioClip clip,
            string absolutePath,
            string fileName,
            int layerIndex,
            int layerCount)
        {
            ResolvePose(command, spatial, out Vector3 position, out Quaternion rotation);
            position += ComputeLayerOffset(layerIndex, layerCount);

            var go = new GameObject("Audio_" + SanitizeFileStem(result.title));
            if (defaultParent != null)
                go.transform.SetParent(defaultParent, true);
            go.transform.SetPositionAndRotation(position, rotation);

            AudioSource source = go.AddComponent<AudioSource>();
            source.volume = Mathf.Clamp01(command.audio_volume <= 0f ? 1f : command.audio_volume);
            source.spatialBlend = Mathf.Clamp01(command.audio_spatial_blend < 0f ? defaultSpatialBlend : command.audio_spatial_blend);
            source.minDistance = minDistance;
            source.maxDistance = maxDistance;
            source.playOnAwake = false;
            source.mute = command.audio_muted;

            AudioPlaybackController playback = go.AddComponent<AudioPlaybackController>();
            playback.playOnEnable = false;
            source.clip = clip;
            playback.Configure(
                ResolvePlaybackMode(command, query.prompt, query.category),
                command.audio_interval_seconds,
                command.audio_interval_variance_seconds,
                false);

            var holder = go.AddComponent<AudioClipPathHolder>();
            holder.absolutePath = absolutePath;
            holder.clipPath = worldConfigStore != null
                ? "../CachedWorlds/" + fileName
                : absolutePath;

            AudioAttributionMetadata attribution = go.AddComponent<AudioAttributionMetadata>();
            attribution.provider = result.provider ?? string.Empty;
            attribution.providerSoundId = result.id ?? string.Empty;
            attribution.title = result.title ?? string.Empty;
            attribution.creator = result.creator ?? string.Empty;
            attribution.license = result.license ?? string.Empty;
            attribution.licenseUrl = result.licenseUrl ?? string.Empty;
            attribution.landingUrl = result.landingUrl ?? string.Empty;
            attribution.audioUrl = result.audioUrl ?? string.Empty;
            attribution.previewUrl = result.previewUrl ?? string.Empty;
            attribution.prompt = query.prompt ?? string.Empty;
            attribution.category = query.category ?? string.Empty;
            attribution.durationSeconds = result.durationSeconds;
            attribution.downloadedBytes = clip != null ? new FileInfo(absolutePath).Length : 0L;
            attribution.cachedFileName = fileName ?? string.Empty;
            attribution.cachedAbsolutePath = absolutePath ?? string.Empty;
            attribution.capturedAtUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
            attribution.tags.Clear();
            if (result.tags != null)
                attribution.tags.AddRange(result.tags);

            SpeechIntentTrackable trackable = go.AddComponent<SpeechIntentTrackable>();
            trackable.canonicalName = string.IsNullOrWhiteSpace(query.prompt)
                ? result.title
                : query.prompt;

            return go;
        }

        private Vector3 ComputeLayerOffset(int layerIndex, int layerCount)
        {
            if (layerCount <= 1 || multiLayerSpreadRadius <= 0f)
                return Vector3.zero;

            float angle = (Mathf.PI * 2f * layerIndex) / layerCount;
            return new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * multiLayerSpreadRadius;
        }

        private void HandleWorldLoaded(string worldId, GaussianSplatRenderer renderer)
        {
            _activeWorldId = worldId ?? string.Empty;
            if (!createAmbienceWhenWorldLoads) return;
            if (skipDefaultWorld && worldId == "__default__") return;
            if (string.IsNullOrWhiteSpace(worldId)) return;
            if (!_worldsWithAmbience.Add(worldId)) return;

            World world = worldManager != null ? worldManager.LastLoadedWorld : null;
            string worldText = world != null && !string.IsNullOrWhiteSpace(world.display_name)
                ? world.display_name
                : worldId;

            global::SpeechIntent.ArchStatusBus.Info("Instantiating ambience for " + worldText + ".", "AUDIO");
            VoiceIntentCommand command = BuildAutomaticWorldAmbienceCommand(worldText);
            CreateAudioSource(command, null, created =>
            {
                if (created != null)
                    worldConfigAutoSave?.RegisterObjectMutation(command, created);
            });
        }

        private VoiceIntentCommand BuildAutomaticWorldAmbienceCommand(string worldText)
        {
            List<string> prompts = InferWorldSoundPrompts(worldText);
            if (prompts.Count > maxAutomaticWorldSounds)
                prompts.RemoveRange(maxAutomaticWorldSounds, prompts.Count - maxAutomaticWorldSounds);

            return new VoiceIntentCommand
            {
                transcript = "automatic ambience for " + worldText,
                intent = VoiceIntentType.CreateAudioSource,
                should_execute = true,
                confidence = 1f,
                sound_prompts = prompts,
                sound_count = prompts.Count,
                sound_category = "ambience",
                sound_provider = "auto",
                audio_loop = true,
                audio_volume = 0.55f,
                audio_playback_mode = "auto",
                audio_spatial_blend = 1f,
                sound_max_duration_seconds = maxDurationSeconds,
                spatial_reference = SpatialReferenceMode.HeadForward
            };
        }

        private GameObject ResolveAudioTarget(VoiceIntentCommand command)
        {
            return ResolveAudioTarget(command, allowUnnamedFallback: true);
        }

        private static bool IsAllTarget(VoiceIntentCommand command)
        {
            if (command == null)
                return false;

            if (command.target_reference == TargetReferenceMode.All)
                return true;

            string target = FirstNonEmpty(command.target_name, command.sound_prompt, command.object_name);
            return IsAllToken(target);
        }

        private static bool IsAudioCategoryTarget(VoiceIntentCommand command)
        {
            string target = FirstNonEmpty(command?.target_name, command?.sound_prompt, command?.object_name);
            string normalized = NormalizeAudioName(target);
            return normalized == "audio" ||
                   normalized == "audios" ||
                   normalized == "sound" ||
                   normalized == "sounds";
        }

        private List<GameObject> GetWorldAudioTargets()
        {
            var targets = new List<GameObject>();
            var seen = new HashSet<int>();

            for (int i = _spawnedAudio.Count - 1; i >= 0; i--)
            {
                SpawnedAudio item = _spawnedAudio[i];
                if (item == null || item.gameObject == null)
                {
                    _spawnedAudio.RemoveAt(i);
                    continue;
                }

                AddWorldAudioTarget(item.gameObject, targets, seen);
            }

            AudioPlaybackController[] playbackControllers =
                FindObjectsByType<AudioPlaybackController>(FindObjectsSortMode.None);
            foreach (AudioPlaybackController playback in playbackControllers)
            {
                if (playback == null)
                    continue;

                AddWorldAudioTarget(playback.gameObject, targets, seen);
            }

            return targets;
        }

        private static void AddWorldAudioTarget(
            GameObject target,
            List<GameObject> targets,
            HashSet<int> seen)
        {
            if (target == null || target.GetComponent<AudioSource>() == null)
                return;

            if (IsUxAudioObject(target))
                return;

            int id = target.GetInstanceID();
            if (!seen.Add(id))
                return;

            targets.Add(target);
        }

        private GameObject ResolveAudioTarget(VoiceIntentCommand command, bool allowUnnamedFallback)
        {
            string targetName = FirstNonEmpty(
                command?.target_name,
                command?.sound_prompt,
                command?.object_name);
            if (!allowUnnamedFallback && string.IsNullOrWhiteSpace(targetName))
                targetName = command?.transcript;

            if (command != null &&
                (command.target_reference == TargetReferenceMode.LastCreatedObject ||
                 command.target_reference == TargetReferenceMode.LastCreatedOrInteracted ||
                 command.target_reference == TargetReferenceMode.CurrentSelection) &&
                interactionMemory != null)
            {
                GameObject remembered = interactionMemory.GetLastCreatedOrInteracted();
                if (remembered != null &&
                    remembered.GetComponent<AudioSource>() != null &&
                    !IsUxAudioObject(remembered))
                    return remembered;
            }

            AudioSource[] sources = FindObjectsByType<AudioSource>(FindObjectsSortMode.None);
            if (!string.IsNullOrWhiteSpace(targetName))
            {
                string needle = NormalizeAudioName(targetName);
                foreach (AudioSource source in sources)
                {
                    if (source == null || IsUxAudioObject(source.gameObject)) continue;
                    SpeechIntentTrackable trackable = source.GetComponent<SpeechIntentTrackable>();
                    AudioAttributionMetadata attribution = source.GetComponent<AudioAttributionMetadata>();

                    if (MatchesAudioName(source.gameObject.name, needle) ||
                        MatchesAudioName(trackable?.canonicalName, needle) ||
                        MatchesAudioName(attribution?.prompt, needle) ||
                        MatchesAudioName(attribution?.title, needle))
                    {
                        return source.gameObject;
                    }

                    if (trackable?.aliases != null)
                    {
                        foreach (string alias in trackable.aliases)
                            if (MatchesAudioName(alias, needle))
                                return source.gameObject;
                    }
                }

                return null;
            }

            if (interactionMemory != null)
            {
                GameObject remembered = interactionMemory.GetLastCreatedOrInteracted();
                if (remembered != null &&
                    remembered.GetComponent<AudioSource>() != null &&
                    !IsUxAudioObject(remembered))
                    return remembered;
            }

            if (!allowUnnamedFallback)
                return null;

            foreach (AudioSource source in sources)
            {
                if (source != null && !IsUxAudioObject(source.gameObject))
                    return source.gameObject;
            }

            return null;
        }

        private static bool IsUxAudioObject(GameObject target)
        {
            if (target == null)
                return true;

            if (target.GetComponentInParent<global::TtsPlayer>() != null)
                return true;

            Transform current = target.transform;
            while (current != null)
            {
                string name = current.name;
                if (name.IndexOf("Tts", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("VoiceActivation", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("Voice Activation", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("AudioCue", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("Audio Cue", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }

                current = current.parent;
            }

            return false;
        }

        private static bool LooksLikeExistingAudioPlaybackRequest(VoiceIntentCommand command)
        {
            if (command == null)
                return false;

            string control = (command.audio_control ?? string.Empty).Trim().ToLowerInvariant();
            if (control == "play_now" || command.audio_play_now)
                return true;

            string transcript = (command.transcript ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(transcript))
                return false;

            if (transcript.StartsWith("please ", StringComparison.Ordinal))
                transcript = transcript.Substring("please ".Length);

            bool asksToPlay = transcript.StartsWith("play ", StringComparison.Ordinal) ||
                              transcript.StartsWith("start ", StringComparison.Ordinal) ||
                              transcript.StartsWith("trigger ", StringComparison.Ordinal);
            if (!asksToPlay)
                return false;

            return !ContainsAny(transcript,
                "add ", "create ", "download ", "find ", "get ", "generate ",
                "new ", "source", "ambience", "ambiance", "background");
        }

        private static bool MatchesAudioName(string candidate, string normalizedNeedle)
        {
            string normalizedCandidate = NormalizeAudioName(candidate);
            if (string.IsNullOrWhiteSpace(normalizedCandidate) || string.IsNullOrWhiteSpace(normalizedNeedle))
                return false;

            return normalizedCandidate == normalizedNeedle ||
                   normalizedCandidate.Contains(normalizedNeedle) ||
                   normalizedNeedle.Contains(normalizedCandidate);
        }

        private static string NormalizeAudioName(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return string.Empty;

            string value = raw.Trim().ToLowerInvariant();
            value = value.Replace("audio_", " ");
            value = value.Replace("sounds", "sound");
            value = value.Replace("audio", " ");
            value = value.Replace("clip", " ");
            value = value.Replace("source", " ");

            char[] chars = value.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (!char.IsLetterOrDigit(chars[i]))
                    chars[i] = ' ';
            }

            string[] parts = new string(chars).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Length > 3 && parts[i].EndsWith("s", StringComparison.Ordinal))
                    parts[i] = parts[i].Substring(0, parts[i].Length - 1);
            }
            return string.Join(" ", parts);
        }

        private static bool IsAllToken(string value)
        {
            string normalized = NormalizeAudioName(value);
            return normalized == "all" ||
                   normalized == "all sound" ||
                   normalized == "every sound" ||
                   normalized == "everything";
        }

        public int StopAndDestroyAllWorldAudio()
        {
            _audioLifetimeVersion++;

            int destroyed = 0;
            List<GameObject> targets = GetWorldAudioTargets();
            foreach (GameObject target in targets)
            {
                if (DestroyWorldAudioTarget(target))
                    destroyed++;
            }

            if (destroyed > 0 && !_isShuttingDown)
                EmitStatus($"Stopped and removed {destroyed} world audio source(s).");

            return destroyed;
        }

        private void HandleWorldLoadStarted(string worldId)
        {
            if (!string.IsNullOrWhiteSpace(worldId) && worldId != _activeWorldId)
                StopAndDestroyAllWorldAudio();
        }

        private void HandleWorldUnloaded(string worldId)
        {
            StopAndDestroyAudioForWorld(worldId);
            _worldsWithAmbience.Remove(worldId);
            if (worldId == _activeWorldId)
                _activeWorldId = "";
        }

        private void RegisterSpawnedAudio(GameObject go)
        {
            if (go == null) return;
            _spawnedAudio.Add(new SpawnedAudio
            {
                worldId = _activeWorldId,
                gameObject = go
            });
        }

        private int StopAndDestroyAudioForWorld(string worldId)
        {
            _audioLifetimeVersion++;

            int destroyed = 0;
            for (int i = _spawnedAudio.Count - 1; i >= 0; i--)
            {
                SpawnedAudio item = _spawnedAudio[i];
                if (item == null || item.gameObject == null)
                {
                    _spawnedAudio.RemoveAt(i);
                    continue;
                }

                bool destroy =
                    string.IsNullOrWhiteSpace(worldId) ||
                    string.Equals(item.worldId, worldId, StringComparison.Ordinal);
                if (!destroy)
                    continue;

                AudioSource source = item.gameObject.GetComponent<AudioSource>();
                if (source != null)
                    source.Stop();

                Destroy(item.gameObject);
                _spawnedAudio.RemoveAt(i);
                destroyed++;
            }

            if (destroyed > 0 && !_isShuttingDown)
                EmitStatus($"Stopped and removed {destroyed} world audio source(s).");

            return destroyed;
        }

        private bool DestroyWorldAudioTarget(GameObject target)
        {
            if (target == null || IsUxAudioObject(target))
                return false;

            AudioSource source = target.GetComponent<AudioSource>();
            if (source != null)
                source.Stop();

            for (int i = _spawnedAudio.Count - 1; i >= 0; i--)
            {
                SpawnedAudio item = _spawnedAudio[i];
                if (item == null || item.gameObject == null || item.gameObject == target)
                    _spawnedAudio.RemoveAt(i);
            }

            Destroy(target);
            return true;
        }

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (string value in values)
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            return string.Empty;
        }

        private static AudioPlaybackMode ResolvePlaybackMode(VoiceIntentCommand command, string prompt, string category)
        {
            AudioPlaybackMode? explicitMode = TryParsePlaybackMode(command?.audio_playback_mode);
            if (explicitMode.HasValue)
                return explicitMode.Value;

            if (command != null && command.audio_interval_seconds > 0f)
                return command.audio_interval_variance_seconds > 0f
                    ? AudioPlaybackMode.RandomInterval
                    : AudioPlaybackMode.Interval;

            string text = ((prompt ?? string.Empty) + " " + (category ?? string.Empty)).ToLowerInvariant();

            if (ContainsAny(text, "ocean", "wave", "rain", "river", "stream", "creek", "rapids", "wind", "crowd", "traffic", "hum", "ambience", "ambient", "soundscape", "waterfall"))
                return AudioPlaybackMode.Loop;

            if (ContainsAny(text, "bird", "birds", "hawk", "owl", "crow", "raven", "frog", "cricket", "insect", "thunder", "bell", "chime"))
                return AudioPlaybackMode.RandomInterval;

            if (ContainsAny(text, "door", "knock", "explosion", "crash", "impact", "footstep", "beep", "button"))
                return AudioPlaybackMode.Once;

            return command != null && command.audio_loop ? AudioPlaybackMode.Loop : AudioPlaybackMode.Once;
        }

        private static AudioPlaybackMode? TryParsePlaybackMode(string raw)
        {
            string value = (raw ?? string.Empty).Trim().ToLowerInvariant();
            return value switch
            {
                "loop" or "looping" => AudioPlaybackMode.Loop,
                "once" or "one_shot" or "oneshot" or "play_once" => AudioPlaybackMode.Once,
                "interval" or "fixed_interval" => AudioPlaybackMode.Interval,
                "random_interval" or "random" or "random_intervals" => AudioPlaybackMode.RandomInterval,
                _ => null
            };
        }

        private static List<string> InferWorldSoundPrompts(string worldText)
        {
            string text = (worldText ?? string.Empty).ToLowerInvariant();
            var prompts = new List<string>();

            if (ContainsAny(text, "forest", "woods", "jungle", "trees", "redwood"))
            {
                prompts.Add("forest birds ambience");
                prompts.Add("wind in trees ambience");
            }

            if (ContainsAny(text, "river", "stream", "creek", "waterfall", "rapids"))
                prompts.Add(ContainsAny(text, "rapids", "waterfall") ? "river rapids water" : "gentle river water");

            if (ContainsAny(text, "ocean", "sea", "beach", "shore", "coast"))
                prompts.Add("ocean waves ambience");

            if (ContainsAny(text, "city", "street", "market", "cyberpunk"))
                prompts.Add("distant city ambience");

            if (ContainsAny(text, "cave", "cavern", "underground"))
                prompts.Add("cave ambience dripping water");

            if (ContainsAny(text, "storm", "rain", "thunder"))
                prompts.Add(ContainsAny(text, "thunder") ? "distant thunder rain" : "rain ambience");

            if (ContainsAny(text, "roman", "colosseum", "coliseum", "colusium", "arena", "gladiator"))
            {
                prompts.Add("ancient arena crowd cheering");
                prompts.Add("distant crowd talking");
                prompts.Add("sword metal armor clash");
            }

            if (ContainsAny(text, "castle", "medieval", "knight", "fortress"))
            {
                prompts.Add("medieval courtyard crowd");
                prompts.Add("metal armor footsteps");
            }

            if (ContainsAny(text, "temple", "cathedral", "church", "monastery"))
                prompts.Add("large stone hall reverberation");

            if (ContainsAny(text, "market", "bazaar"))
                prompts.Add("busy market crowd ambience");

            if (prompts.Count == 0)
                prompts.Add("ambient soundscape " + worldText);

            return prompts;
        }

        private static bool ContainsAny(string text, params string[] terms)
        {
            foreach (string term in terms)
                if (text.Contains(term, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        private static void AddSplitPrompts(List<string> prompts, string raw)
        {
            string[] parts = raw.Split(new[] { ';', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string part in parts)
            {
                string trimmed = part.Trim();
                if (trimmed.Length > 0) prompts.Add(trimmed);
            }
        }

        private void ResolvePose(
            VoiceIntentCommand command,
            SpatialSnapshot spatial,
            out Vector3 position,
            out Quaternion rotation)
        {
            position = transform.position + transform.forward * defaultDistance;
            rotation = Quaternion.identity;

            if (spatial == null)
                return;

            if (command.spatial_reference == SpatialReferenceMode.BodyAnchor &&
                BodyAnchorResolver.TryResolve(spatial, command.body_anchor, command.target_hand, out position, out rotation))
            {
                return;
            }

            if (command.spatial_reference == SpatialReferenceMode.PointingHit &&
                TryGetPreferredHand(command.target_hand, spatial, out HandRaySnapshot hitHand) &&
                hitHand.has_hit)
            {
                position = hitHand.hit_point + hitHand.hit_normal * surfaceOffset;
                rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(spatial.head_forward, hitHand.hit_normal), hitHand.hit_normal);
                return;
            }

            if (command.spatial_reference == SpatialReferenceMode.HandMidpoint && spatial.has_hand_midpoint)
            {
                position = spatial.hand_midpoint;
                return;
            }

            if (TryGetPreferredHand(command.target_hand, spatial, out HandRaySnapshot hand))
            {
                position = hand.origin + hand.direction.normalized * defaultDistance;
                return;
            }

            if (spatial.head_forward.sqrMagnitude > 0.0001f)
                position = spatial.head_position + spatial.head_forward.normalized * defaultDistance;
        }

        private static bool TryGetPreferredHand(
            HandSelection selection,
            SpatialSnapshot spatial,
            out HandRaySnapshot hand)
        {
            hand = null;
            if (spatial == null) return false;

            if (selection == HandSelection.Left && spatial.left_hand != null && spatial.left_hand.is_available)
            {
                hand = spatial.left_hand;
                return true;
            }

            if (selection == HandSelection.Right && spatial.right_hand != null && spatial.right_hand.is_available)
            {
                hand = spatial.right_hand;
                return true;
            }

            if (spatial.right_hand != null && spatial.right_hand.is_available && spatial.right_hand.is_pointing)
            {
                hand = spatial.right_hand;
                return true;
            }

            if (spatial.left_hand != null && spatial.left_hand.is_available && spatial.left_hand.is_pointing)
            {
                hand = spatial.left_hand;
                return true;
            }

            if (spatial.right_hand != null && spatial.right_hand.is_available)
            {
                hand = spatial.right_hand;
                return true;
            }

            if (spatial.left_hand != null && spatial.left_hand.is_available)
            {
                hand = spatial.left_hand;
                return true;
            }

            return false;
        }

        private static string SanitizeFileStem(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

            char[] chars = raw.Trim().ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (!char.IsLetterOrDigit(chars[i])) chars[i] = '_';
            }

            string stem = new string(chars).Trim('_');
            while (stem.Contains("__", StringComparison.Ordinal))
                stem = stem.Replace("__", "_");
            return stem.Length > 36 ? stem.Substring(0, 36) : stem;
        }

        private void EmitStatus(string message)
        {
            if (verboseLogging) Debug.Log("[AudioWorldActionController] " + message, this);
            global::SpeechIntent.ArchStatusBus.Info(message, "AUDIO");
            onStatus?.Invoke(message);
        }

        private void EmitError(string message)
        {
            Debug.LogWarning("[AudioWorldActionController] " + message, this);
            global::SpeechIntent.ArchStatusBus.Warning(message, "AUDIO");
            onError?.Invoke(message);
        }
    }
}
