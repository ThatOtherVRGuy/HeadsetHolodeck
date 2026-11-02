using System;
using System.Collections;
using Newtonsoft.Json;
using SpeechIntent;
using UnityEngine;
using UnityEngine.Networking;

namespace Holodeck.Direct
{
    public sealed class ThreeDAIStudioCreditService : MonoBehaviour
    {
        const string DefaultBaseUrl = "https://api.3daistudio.com";

        [Header("Credentials")]
        [SerializeField] string apiKey = "";
        [SerializeField] string baseUrl = DefaultBaseUrl;

        [Header("Behavior")]
        [SerializeField] int requestTimeoutSeconds = 20;
        [SerializeField] bool debugLogging = true;

        public bool IsConfigured => !string.IsNullOrWhiteSpace(ResolveApiKey());
        public bool HasLastKnownBalance { get; private set; }
        public decimal LastKnownBalance { get; private set; }
        public string LastFailureMessage { get; private set; } = "";

        public static ThreeDAIStudioCreditService GetOrCreate()
        {
            ThreeDAIStudioCreditService existing = FindFirstObjectByType<ThreeDAIStudioCreditService>(FindObjectsInactive.Include);
            if (existing != null)
                return existing;

            Transform systems =
                GameObject.Find("Holodeck/Systems")?.transform
                ?? GameObject.Find("Systems")?.transform
                ?? new GameObject("Systems").transform;

            GameObject go = new GameObject("ThreeDAIStudioCreditService");
            go.transform.SetParent(systems, false);
            return go.AddComponent<ThreeDAIStudioCreditService>();
        }

        public IEnumerator RefreshBalance(Action<bool, decimal, string> onComplete)
        {
            string key = ResolveApiKey();
            if (string.IsNullOrWhiteSpace(key))
            {
                string missingKeyMessage = "3dAIStudio API key missing. Set THREEDAISTUDIO_API_KEY.";
                LastFailureMessage = missingKeyMessage;
                onComplete?.Invoke(false, 0m, missingKeyMessage);
                yield break;
            }

            string url = $"{ResolveBaseUrl()}/account/user/wallet/";
            using UnityWebRequest request = UnityWebRequest.Get(url);
            request.timeout = requestTimeoutSeconds;
            request.SetRequestHeader("Authorization", $"Bearer {key}");
            request.SetRequestHeader("Accept", "application/json");

            if (debugLogging)
                Debug.Log("[ThreeDAIStudioCreditService] Checking credit balance.", this);

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                string error = BuildHttpError("3dAIStudio balance check failed", request);
                LastFailureMessage = error;
                ArchStatusBus.Warning(error, "OBJECT");
                onComplete?.Invoke(false, 0m, error);
                yield break;
            }

            WalletResponse response = ParseJson<WalletResponse>(request.downloadHandler.text);
            if (response == null || string.IsNullOrWhiteSpace(response.balance) ||
                !decimal.TryParse(response.balance, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out decimal balance))
            {
                string error = "3dAIStudio balance response was not usable.";
                LastFailureMessage = error;
                ArchStatusBus.Warning(error, "OBJECT");
                onComplete?.Invoke(false, 0m, error);
                yield break;
            }

            HasLastKnownBalance = true;
            LastKnownBalance = balance;
            LastFailureMessage = "";
            string balanceMessage = $"3dAIStudio balance: {FormatCredits(balance)} credits.";
            Debug.Log("[ThreeDAIStudioCreditService] " + balanceMessage, this);
            ArchStatusBus.Info(balanceMessage, "OBJECT");
            onComplete?.Invoke(true, balance, null);
        }

        string ResolveApiKey()
        {
            if (!string.IsNullOrWhiteSpace(apiKey))
                return apiKey.Trim();
            return RuntimeDotEnv.GetEnvironmentOrDotEnv("THREEDAISTUDIO_API_KEY");
        }

        string ResolveBaseUrl()
        {
            string configured = RuntimeDotEnv.GetEnvironmentOrDotEnv("THREEDAISTUDIO_BASE_URL");
            if (string.IsNullOrWhiteSpace(configured))
                configured = baseUrl;
            configured = string.IsNullOrWhiteSpace(configured) ? DefaultBaseUrl : configured.Trim();
            return configured.TrimEnd('/');
        }

        static string BuildHttpError(string prefix, UnityWebRequest request)
        {
            string status = request.responseCode > 0 ? $"HTTP {request.responseCode}" : request.error;
            string body = request.downloadHandler != null ? ExtractMessage(request.downloadHandler.text) : "";
            return string.IsNullOrWhiteSpace(body)
                ? $"{prefix}: {status}."
                : $"{prefix}: {status}. {body}.";
        }

        static T ParseJson<T>(string json) where T : class
        {
            try
            {
                return JsonConvert.DeserializeObject<T>(json);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ThreeDAIStudioCreditService] JSON parse failed: {ex.Message}");
                return null;
            }
        }

        static string ExtractMessage(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return "empty response";
            ErrorResponse response = ParseJson<ErrorResponse>(json);
            if (!string.IsNullOrWhiteSpace(response?.error))
                return response.error;
            if (!string.IsNullOrWhiteSpace(response?.message))
                return response.message;
            if (!string.IsNullOrWhiteSpace(response?.detail))
                return response.detail;
            return json.Length > 240 ? json.Substring(0, 240) : json;
        }

        public static string FormatCredits(decimal credits)
        {
            return credits == decimal.Truncate(credits)
                ? credits.ToString("0", System.Globalization.CultureInfo.InvariantCulture)
                : credits.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        }

        [Serializable]
        sealed class WalletResponse
        {
            public string balance;
        }

        [Serializable]
        sealed class ErrorResponse
        {
            public string error;
            public string message;
            public string detail;
        }
    }
}
