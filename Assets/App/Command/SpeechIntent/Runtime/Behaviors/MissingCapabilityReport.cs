using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpeechIntent.Behaviors
{
    [Serializable]
    public class MissingCapabilityReport
    {
        public string status = "missing_capability";
        public string user_request = "";
        public string requested_behavior = "";
        public string[] available_behaviors = new string[0];
        public string[] needed_capabilities = new string[0];
        public string possible_approximation = "";

        public MissingCapabilityReport()
        {
        }

        public MissingCapabilityReport(
            string userRequest,
            string requestedBehavior,
            IEnumerable<string> availableBehaviors,
            IEnumerable<string> neededCapabilities,
            string possibleApproximation)
        {
            user_request = userRequest ?? "";
            requested_behavior = requestedBehavior ?? "";
            available_behaviors = ToArrayOrEmpty(availableBehaviors);
            needed_capabilities = ToArrayOrEmpty(neededCapabilities);
            possible_approximation = possibleApproximation ?? "";
        }

        public string ToUserMessage()
        {
            string behavior = string.IsNullOrWhiteSpace(requested_behavior)
                ? "that behavior"
                : requested_behavior;

            if (needed_capabilities != null && needed_capabilities.Length > 0)
                return "I cannot run " + behavior + " because this scene is missing: " + string.Join(", ", needed_capabilities) + ".";

            if (!string.IsNullOrWhiteSpace(possible_approximation))
                return "I cannot run " + behavior + ", but I can try: " + possible_approximation + ".";

            return "I cannot run that behavior with the current scene capabilities.";
        }

        public void Log()
        {
            Debug.LogWarning("[MissingCapabilityReport] " + ToJson());
        }

        public string ToJson()
        {
            return JsonUtility.ToJson(this);
        }

        static string[] ToArrayOrEmpty(IEnumerable<string> values)
        {
            if (values == null)
                return new string[0];

            return new List<string>(values).ToArray();
        }
    }
}
