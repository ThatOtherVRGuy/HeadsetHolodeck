using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Scripting;

namespace Holodeck.Save
{
    [Preserve]
    public class AudioAttributionMetadataSerializer : IComponentSerializer
    {
        public string TypeName => "AudioAttributionMetadata";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void AutoRegister() => WorldConfigComponentRegistry.Register(new AudioAttributionMetadataSerializer());

        public JObject Save(GameObject go)
        {
            AudioAttributionMetadata meta = go.GetComponent<AudioAttributionMetadata>();
            if (meta == null) return null;

            return new JObject
            {
                ["provider"] = meta.provider,
                ["provider_sound_id"] = meta.providerSoundId,
                ["title"] = meta.title,
                ["creator"] = meta.creator,
                ["license"] = meta.license,
                ["license_url"] = meta.licenseUrl,
                ["landing_url"] = meta.landingUrl,
                ["audio_url"] = meta.audioUrl,
                ["preview_url"] = meta.previewUrl,
                ["prompt"] = meta.prompt,
                ["category"] = meta.category,
                ["duration_seconds"] = meta.durationSeconds,
                ["downloaded_bytes"] = meta.downloadedBytes,
                ["cached_file_name"] = meta.cachedFileName,
                ["cached_absolute_path"] = meta.cachedAbsolutePath,
                ["tags"] = new JArray(meta.tags ?? new System.Collections.Generic.List<string>()),
                ["captured_at_utc"] = meta.capturedAtUtc
            };
        }

        public void Restore(GameObject go, JObject data, RestorationContext ctx)
        {
            AudioAttributionMetadata meta =
                go.GetComponent<AudioAttributionMetadata>() ?? go.AddComponent<AudioAttributionMetadata>();

            meta.provider = data["provider"]?.Value<string>() ?? "";
            meta.providerSoundId = data["provider_sound_id"]?.Value<string>() ?? "";
            meta.title = data["title"]?.Value<string>() ?? "";
            meta.creator = data["creator"]?.Value<string>() ?? "";
            meta.license = data["license"]?.Value<string>() ?? "";
            meta.licenseUrl = data["license_url"]?.Value<string>() ?? "";
            meta.landingUrl = data["landing_url"]?.Value<string>() ?? "";
            meta.audioUrl = data["audio_url"]?.Value<string>() ?? "";
            meta.previewUrl = data["preview_url"]?.Value<string>() ?? "";
            meta.prompt = data["prompt"]?.Value<string>() ?? "";
            meta.category = data["category"]?.Value<string>() ?? "";
            meta.durationSeconds = data["duration_seconds"]?.Value<float>() ?? 0f;
            meta.downloadedBytes = data["downloaded_bytes"]?.Value<long>() ?? 0L;
            meta.cachedFileName = data["cached_file_name"]?.Value<string>() ?? "";
            meta.cachedAbsolutePath = data["cached_absolute_path"]?.Value<string>() ?? "";
            meta.capturedAtUtc = data["captured_at_utc"]?.Value<string>() ?? "";
            meta.tags.Clear();

            if (data["tags"] is JArray tags)
            {
                foreach (JToken tag in tags)
                {
                    string value = tag.Value<string>();
                    if (!string.IsNullOrWhiteSpace(value))
                        meta.tags.Add(value);
                }
            }
        }
    }
}
