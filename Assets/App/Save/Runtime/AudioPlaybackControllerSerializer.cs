using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Scripting;

namespace Holodeck.Save
{
    [Preserve]
    public class AudioPlaybackControllerSerializer : IComponentSerializer
    {
        public string TypeName => "AudioPlaybackController";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void AutoRegister() => WorldConfigComponentRegistry.Register(new AudioPlaybackControllerSerializer());

        public JObject Save(GameObject go)
        {
            AudioPlaybackController controller = go.GetComponent<AudioPlaybackController>();
            if (controller == null) return null;

            return new JObject
            {
                ["mode"] = controller.mode.ToString(),
                ["interval_seconds"] = controller.intervalSeconds,
                ["interval_variance_seconds"] = controller.intervalVarianceSeconds,
                ["play_on_enable"] = controller.playOnEnable
            };
        }

        public void Restore(GameObject go, JObject data, RestorationContext ctx)
        {
            AudioPlaybackController controller =
                go.GetComponent<AudioPlaybackController>() ?? go.AddComponent<AudioPlaybackController>();

            string mode = data["mode"]?.Value<string>();
            controller.mode = ParseMode(mode);
            controller.intervalSeconds = data["interval_seconds"]?.Value<float>() ?? 10f;
            controller.intervalVarianceSeconds = data["interval_variance_seconds"]?.Value<float>() ?? 0f;
            controller.playOnEnable = data["play_on_enable"]?.Value<bool>() ?? false;
        }

        private static AudioPlaybackMode ParseMode(string raw)
        {
            return raw switch
            {
                nameof(AudioPlaybackMode.Once) => AudioPlaybackMode.Once,
                nameof(AudioPlaybackMode.Interval) => AudioPlaybackMode.Interval,
                nameof(AudioPlaybackMode.RandomInterval) => AudioPlaybackMode.RandomInterval,
                _ => AudioPlaybackMode.Loop
            };
        }
    }
}
