// Assets/App/Save/Runtime/TransformSerializer.cs
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Scripting;

namespace Holodeck.Save
{
    [Preserve]
    public class TransformSerializer : IComponentSerializer
    {
        public string TypeName => "Transform";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void AutoRegister() => WorldConfigComponentRegistry.Register(new TransformSerializer());

        public JObject Save(GameObject go)
        {
            Transform t = go.transform;
            return new JObject
            {
                ["position"] = Vec3ToJObject(t.localPosition),
                ["rotation"] = QuatToJObject(t.localRotation),
                ["scale"]    = Vec3ToJObject(t.localScale)
            };
        }

        public void Restore(GameObject go, JObject data, RestorationContext ctx)
        {
            Transform t = go.transform;
            if (data["position"] is JObject pos) t.localPosition = JObjectToVec3(pos);
            if (data["rotation"] is JObject rot) t.localRotation = JObjectToQuat(rot);
            if (data["scale"]    is JObject scl) t.localScale    = JObjectToVec3(scl);
        }

        static JObject Vec3ToJObject(Vector3 v) =>
            new JObject { ["x"] = v.x, ["y"] = v.y, ["z"] = v.z };

        static JObject QuatToJObject(Quaternion q) =>
            new JObject { ["x"] = q.x, ["y"] = q.y, ["z"] = q.z, ["w"] = q.w };

        static Vector3 JObjectToVec3(JObject j) =>
            new Vector3(j["x"].Value<float>(), j["y"].Value<float>(), j["z"].Value<float>());

        static Quaternion JObjectToQuat(JObject j) =>
            new Quaternion(j["x"].Value<float>(), j["y"].Value<float>(),
                           j["z"].Value<float>(), j["w"].Value<float>());
    }
}
