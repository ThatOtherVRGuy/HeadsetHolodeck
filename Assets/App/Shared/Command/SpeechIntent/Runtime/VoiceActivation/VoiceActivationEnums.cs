namespace SpeechIntent.VoiceActivation
{
    public enum VoiceActivationMode
    {
        VadAsr,
        Kws
    }

    public enum WakeWordMatchMode
    {
        StartsWith,
        Contains,
        Exact
    }

    public enum VoiceListeningState
    {
        Disabled,
        ListeningForWake,
        WakeDetected,
        ListeningForCommand,
        ProcessingCommand,
        Cooldown,
        Error
    }
}
