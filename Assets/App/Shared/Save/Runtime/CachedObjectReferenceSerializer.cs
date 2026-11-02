using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Scripting;

namespace Holodeck.Save
{
    [Preserve]
    public class CachedObjectReferenceSerializer : IComponentSerializer
    {
        public string TypeName => "CachedObjectReference";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void AutoRegister() => WorldConfigComponentRegistry.Register(new CachedObjectReferenceSerializer());

        public JObject Save(GameObject go)
        {
            CachedObjectReference reference = go.GetComponent<CachedObjectReference>();
            if (reference == null || string.IsNullOrWhiteSpace(reference.cachedObjectId))
                return null;

            return new JObject
            {
                ["cached_object_id"] = reference.cachedObjectId,
                ["cached_model_path"] = reference.cachedModelPath
            };
        }

        public void Restore(GameObject go, JObject data, RestorationContext ctx)
        {
            CachedObjectReference reference =
                go.GetComponent<CachedObjectReference>() ?? go.AddComponent<CachedObjectReference>();

            reference.cachedObjectId = data["cached_object_id"]?.Value<string>() ?? "";
            reference.cachedModelPath = data["cached_model_path"]?.Value<string>() ?? "";
        }
    }
}
