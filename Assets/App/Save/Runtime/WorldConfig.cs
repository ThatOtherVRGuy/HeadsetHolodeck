// Assets/App/Save/Runtime/WorldConfig.cs
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

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
        public List<PromptEntry> prompts = new List<PromptEntry>();
        public List<SavedObject> objects = new List<SavedObject>();
        public LightingData lighting;  // null if lighting not set
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
