// Assets/App/Save/Runtime/AudioSourceSerializer.cs
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Scripting;

namespace Holodeck.Save
{
    /// <summary>
    /// Saves AudioSource settings and clip path. Restore sets all properties
    /// and records the absolute path in AudioClipPathHolder; actual clip loading
    /// is deferred to WorldConfigRestorer.LoadAudioClipsAsync() (needs coroutine).
    /// </summary>
    [Preserve]
    public class AudioSourceSerializer : IComponentSerializer
    {
        public string TypeName => "AudioSource";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void AutoRegister() => WorldConfigComponentRegistry.Register(new AudioSourceSerializer());

        public JObject Save(GameObject go)
        {
            AudioSource src = go.GetComponent<AudioSource>();
            if (src == null) return null;

            string clipPath = "";
            AudioClipPathHolder holder = go.GetComponent<AudioClipPathHolder>();
            if (holder != null) clipPath = holder.clipPath ?? "";

            return new JObject
            {
                ["clip_path"]     = clipPath,
                ["volume"]        = src.volume,
                ["mute"]          = src.mute,
                ["loop"]          = src.loop,
                ["spatial_blend"] = src.spatialBlend,
                ["min_distance"]  = src.minDistance,
                ["max_distance"]  = src.maxDistance
            };
        }

        public void Restore(GameObject go, JObject data, RestorationContext ctx)
        {
            AudioSource src = go.GetComponent<AudioSource>() ?? go.AddComponent<AudioSource>();
            src.volume       = data["volume"]?.Value<float>()        ?? 1f;
            src.mute         = data["mute"]?.Value<bool>()           ?? false;
            src.loop         = data["loop"]?.Value<bool>()           ?? false;
            src.spatialBlend = data["spatial_blend"]?.Value<float>() ?? 0f;
            src.minDistance  = data["min_distance"]?.Value<float>()  ?? 1f;
            src.maxDistance  = data["max_distance"]?.Value<float>()  ?? 500f;
            src.playOnAwake  = false;  // WorldConfigRestorer will Play() after clip loads

            string relative = data["clip_path"]?.Value<string>();
            if (string.IsNullOrEmpty(relative)) return;

            string absolute = Path.GetFullPath(Path.Combine(ctx.ConfigFolderPath, relative));
            AudioClipPathHolder holder = go.GetComponent<AudioClipPathHolder>()
                                      ?? go.AddComponent<AudioClipPathHolder>();
            holder.clipPath    = relative;
            holder.absolutePath = absolute;
        }
    }
}
