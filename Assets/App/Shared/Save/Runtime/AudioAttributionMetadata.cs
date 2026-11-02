using System;
using System.Collections.Generic;
using UnityEngine;

namespace Holodeck.Save
{
    [Serializable]
    public class AudioAttributionMetadata : MonoBehaviour
    {
        public string provider = "";
        public string providerSoundId = "";
        public string title = "";
        public string creator = "";
        public string license = "";
        public string licenseUrl = "";
        public string landingUrl = "";
        public string audioUrl = "";
        public string previewUrl = "";
        public string prompt = "";
        public string category = "";
        public float durationSeconds;
        public long downloadedBytes;
        public string cachedFileName = "";
        public string cachedAbsolutePath = "";
        public List<string> tags = new List<string>();
        public string capturedAtUtc = "";
    }
}
