// Assets/App/Save/Runtime/WorldConfig.cs
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Holodeck.Save
{
    public class WorldConfig
    {
        public int schema_version = 1;
        public string config_id;
        public string display_name;
        public string created_at;   // ISO 8601 UTC, e.g. "2026-04-15T10:30:00Z"
        public string modified_at;  // ISO 8601 UTC
        public WorldSourceData world_source;
        public string generation_model;
        public WorldTransformData world_transform;
        public List<PromptEntry> prompts = new List<PromptEntry>();
        public List<SpawnPointData> spawn_points = new List<SpawnPointData>();
        public List<SavedObject> objects = new List<SavedObject>();
        public LightingData lighting;  // null if lighting not set
    }

    public class WorldTransformData
    {
        public JsonVector3 position;
        public JsonQuaternion rotation = Quaternion.identity;
        public JsonVector3 scale = Vector3.one;

        public static WorldTransformData FromTransform(Transform transform)
        {
            if (transform == null)
                return null;

            return new WorldTransformData
            {
                position = transform.localPosition,
                rotation = transform.localRotation,
                scale = transform.localScale
            };
        }

        public void ApplyTo(Transform transform)
        {
            if (transform == null)
                return;

            transform.localPosition = position;
            transform.localRotation = rotation;
            transform.localScale = scale;
        }
    }

    public class SpawnPointData
    {
        public string id;
        public string name;
        public string source;       // "estimated" | "manual"
        public string method;       // estimator method or "manual"
        public string created_at;   // ISO 8601 UTC
        public JsonVector3 position;
        public JsonQuaternion rotation = Quaternion.identity;
        public JsonVector3 look_at;
        public float confidence;
    }

    public struct JsonVector3
    {
        public float x;
        public float y;
        public float z;

        public JsonVector3(float x, float y, float z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public static implicit operator Vector3(JsonVector3 value) =>
            new Vector3(value.x, value.y, value.z);

        public static implicit operator JsonVector3(Vector3 value) =>
            new JsonVector3(value.x, value.y, value.z);
    }

    public struct JsonQuaternion
    {
        public float x;
        public float y;
        public float z;
        public float w;

        public JsonQuaternion(float x, float y, float z, float w)
        {
            this.x = x;
            this.y = y;
            this.z = z;
            this.w = w;
        }

        public static implicit operator Quaternion(JsonQuaternion value) =>
            new Quaternion(value.x, value.y, value.z, value.w);

        public static implicit operator JsonQuaternion(Quaternion value) =>
            new JsonQuaternion(value.x, value.y, value.z, value.w);
    }

    public class WorldSourceData
    {
        public string type;          // "worldlabs" | "local_splat" | "local_pano" | "url"
        public string world_id;      // WorldLabs world_id; null for local/url
        public string display_name;  // human-readable world name
        public string url;           // for type="url"
        public string cached_splat;  // relative path from config folder (e.g. "../CachedWorlds/x.spz"); null if not cached
        public string cached_pano;
        public string cached_thumbnail;
    }

    public class PromptEntry
    {
        public string timestamp;    // ISO 8601 UTC
        public string type;         // "world_creation" | "voice_command"
        public string intent;       // VoiceIntentType name string
        public string transcript;
    }

    public class SavedObject
    {
        public string instance_id;
        public string prefab_name;   // null for programmatically created objects (e.g. audio sources)
        public string display_name;
        public List<SavedComponent> components = new List<SavedComponent>();
    }

    public class SavedComponent
    {
        public string type;
        public JObject data;         // free-form per component type — Newtonsoft handles JObject natively
    }

    public class LightingData
    {
        public string preset;
        public float sun_azimuth;
        public float sun_elevation;
    }
}
