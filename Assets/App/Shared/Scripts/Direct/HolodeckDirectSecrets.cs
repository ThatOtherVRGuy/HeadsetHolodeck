namespace Holodeck.Direct
{
    public static class HolodeckDirectSecrets
    {
        public const string OpenAiApiKey = "";

        public const string OpenAiBaseUrl = "https://api.openai.com/v1";
        public const string OpenAiTranscriptionModel = "gpt-4o-transcribe";

        public static string ResolveOpenAiApiKey()
        {
            if (!string.IsNullOrWhiteSpace(OpenAiApiKey))
            {
                return OpenAiApiKey;
            }

            return global::SpeechIntent.RuntimeDotEnv.GetEnvironmentOrDotEnv("OPENAI_API_KEY");
        }
    }
}
