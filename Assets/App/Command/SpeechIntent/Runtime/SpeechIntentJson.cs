using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace SpeechIntent
{
    public static class SpeechIntentJson
    {
        private static JsonSerializerSettings _settings;

        public static JsonSerializerSettings Settings
        {
            get
            {
                if (_settings == null)
                {
                    _settings = new JsonSerializerSettings();
                    _settings.Converters.Add(new StringEnumConverter());
                    _settings.Converters.Add(new Vector3JsonConverter());
                }
                return _settings;
            }
        }

        public static T Deserialize<T>(string json)
        {
            return JsonConvert.DeserializeObject<T>(json, Settings);
        }

        public static string Serialize(object value, Formatting formatting = Formatting.None)
        {
            return JsonConvert.SerializeObject(value, formatting, Settings);
        }
    }

    /// <summary>
    /// Serializes Vector3 as {x, y, z} only, avoiding the circular reference
    /// caused by Vector3.normalized returning another Vector3.
    /// </summary>
    internal sealed class Vector3JsonConverter : JsonConverter<Vector3>
    {
        public override void WriteJson(JsonWriter writer, Vector3 value, JsonSerializer serializer)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("x"); writer.WriteValue(value.x);
            writer.WritePropertyName("y"); writer.WriteValue(value.y);
            writer.WritePropertyName("z"); writer.WriteValue(value.z);
            writer.WriteEndObject();
        }

        public override Vector3 ReadJson(JsonReader reader, Type objectType, Vector3 existingValue,
                                         bool hasExistingValue, JsonSerializer serializer)
        {
            JObject obj = JObject.Load(reader);
            return new Vector3(obj.Value<float>("x"), obj.Value<float>("y"), obj.Value<float>("z"));
        }
    }
}
