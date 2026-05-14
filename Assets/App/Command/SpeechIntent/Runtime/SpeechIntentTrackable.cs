using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpeechIntent
{
    public class SpeechIntentTrackable : MonoBehaviour
    {
        public string canonicalName = "";
        public List<string> aliases = new List<string>();
        /// <summary>Stable ID assigned by WorldConfigAutoSave at placement time. Persisted in world.json.</summary>
        public string configInstanceId = "";

        public string EffectiveName => string.IsNullOrWhiteSpace(canonicalName) ? gameObject.name : canonicalName;

        public bool Matches(string candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return false;
            }

            if (string.Equals(EffectiveName, candidate, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            for (int i = 0; i < aliases.Count; i++)
            {
                if (string.Equals(aliases[i], candidate, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
