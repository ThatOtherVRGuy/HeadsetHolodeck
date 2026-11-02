using System;
using System.Collections.Generic;

namespace Holodeck.Direct
{
    [Serializable]
    public class CachedObjectRecord
    {
        public int schema_version = 1;
        public string object_id = "";
        public string canonical_name = "";
        public List<string> aliases = new List<string>();
        public List<string> tags = new List<string>();
        public string provider = "";
        public string source_prompt = "";
        public string task_id = "";
        public string model_url = "";
        public string created_at = "";
        public string modified_at = "";
        public long file_size_bytes;
        public string model_path = "model.glb";
        public string thumbnail_path = "";
        public List<string> thumbnail_frames = new List<string>();
        public CachedObjectThumbnailAnimation thumbnail_animation = new CachedObjectThumbnailAnimation();
    }

    [Serializable]
    public class CachedObjectThumbnailAnimation
    {
        public string mode = "still";
        public float fps = 2f;
    }
}
