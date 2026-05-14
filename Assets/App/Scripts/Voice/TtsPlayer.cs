using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using PonyuDev.SherpaOnnx.Tts;

public class TtsPlayer : MonoBehaviour
{
    [SerializeField] private TtsOrchestrator _orchestrator;
    public string textToSpeak = "Welcome";
    public float delayBeforeSpeaking = 1f;
    public bool speakOnStart = false;
    public bool preloadAfterStartup = false;
    public float startupInitializationDelay = 3f;
    [Tooltip("Extra suppression after TTS playback ends. ASR often returns the TTS transcript after the AudioSource has stopped.")]
    public float voiceActivationSuppressionTailSeconds = 2f;
    Task _speakTask = Task.CompletedTask;
    Task _initializeTask;
    static float _suppressVoiceActivationUntil;
    static readonly List<TtsPlayer> ActivePlayers = new List<TtsPlayer>();

    public static bool IsVoiceActivationSuppressed =>
        Time.realtimeSinceStartup < _suppressVoiceActivationUntil || IsAnyTtsAudioSourcePlaying();

    public static void SuppressVoiceActivationFor(float seconds)
    {
        if (seconds <= 0f)
            return;

        _suppressVoiceActivationUntil = Mathf.Max(
            _suppressVoiceActivationUntil,
            Time.realtimeSinceStartup + seconds);
    }

    private void OnEnable()
    {
        if (!ActivePlayers.Contains(this))
            ActivePlayers.Add(this);
    }

    private void OnDisable()
    {
        ActivePlayers.Remove(this);
    }

    private IEnumerator Start()
    {
        if (_orchestrator == null)
            yield break;

        if (preloadAfterStartup)
        {
            if (startupInitializationDelay > 0f)
                yield return new WaitForSeconds(startupInitializationDelay);
            _ = EnsureInitializedAsync();
        }

        if (!speakOnStart)
            yield break;

        yield return new WaitForSeconds(delayBeforeSpeaking);
        Task initTask = EnsureInitializedAsync();
        while (!initTask.IsCompleted)
            yield return null;

        if (initTask.IsFaulted)
        {
            Debug.LogWarning("[TtsPlayer] TTS initialization failed: " + initTask.Exception?.GetBaseException().Message, this);
            yield break;
        }

        SpeakAsync();
    }

    public void Say(string text)
    {
        StartCoroutine(JustSayIt(text));
    }

    private IEnumerator JustSayIt(string text)
    {
        textToSpeak = text;
        yield return new WaitForSeconds(delayBeforeSpeaking);
        Task initTask = EnsureInitializedAsync();
        while (!initTask.IsCompleted)
            yield return null;

        if (initTask.IsFaulted)
        {
            Debug.LogWarning("[TtsPlayer] TTS initialization failed: " + initTask.Exception?.GetBaseException().Message, this);
            yield break;
        }

        SpeakAsync();
    }

    private void SpeakAsync()
    {
        if (textToSpeak == null || _orchestrator?.Service == null || !_orchestrator.Service.IsReady)
            return;
        if (!_speakTask.IsCompleted)
            return;

        // GenerateAndPlay: generates speech and plays it using pooled
        // AudioClip + AudioSource from the cache. Objects are returned
        // to the pool automatically when playback finishes.
        StartCoroutine(SpeakAndTrackCoroutine(textToSpeak));
    }

    private IEnumerator SpeakAndTrackCoroutine(string text)
    {
        SuppressVoiceActivationFor(0.5f);
        _speakTask = _orchestrator.GenerateAndPlayAsync(text);
        while (!_speakTask.IsCompleted)
        {
            SuppressVoiceActivationFor(0.5f);
            yield return null;
        }

        if (_speakTask.IsFaulted)
        {
            Debug.LogWarning("[TtsPlayer] TTS playback failed: " + _speakTask.Exception?.GetBaseException().Message, this);
            yield break;
        }

        bool sawPlayback = false;
        while (IsTtsAudioSourcePlaying())
        {
            sawPlayback = true;
            SuppressVoiceActivationFor(0.25f);
            yield return null;
        }

        if (sawPlayback)
        {
            SuppressVoiceActivationFor(Mathf.Max(0f, voiceActivationSuppressionTailSeconds));
            Debug.Log($"[TtsPlayer] TTS playback ended; voice activation suppression tail applied ({voiceActivationSuppressionTailSeconds:0.00}s).", this);
        }
        else
        {
            SuppressVoiceActivationFor(Mathf.Max(0f, voiceActivationSuppressionTailSeconds));
            Debug.Log($"[TtsPlayer] TTS playback source was not observed; applying suppression tail anyway ({voiceActivationSuppressionTailSeconds:0.00}s).", this);
        }
    }

    public static bool IsAnyTtsAudioSourcePlaying()
    {
        for (int i = ActivePlayers.Count - 1; i >= 0; i--)
        {
            TtsPlayer player = ActivePlayers[i];
            if (player == null)
            {
                ActivePlayers.RemoveAt(i);
                continue;
            }

            if (player.IsTtsAudioSourcePlaying())
                return true;
        }

        AudioSource[] sources = FindObjectsByType<AudioSource>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (AudioSource source in sources)
        {
            if (source == null || !source.isPlaying)
                continue;

            if (IsKnownTtsAudioSource(source))
                return true;
        }

        return false;
    }

    private bool IsTtsAudioSourcePlaying()
    {
        if (_orchestrator == null)
            return false;

        AudioSource[] sources = _orchestrator.GetComponentsInChildren<AudioSource>(true);
        foreach (AudioSource source in sources)
        {
            if (source != null && source.isPlaying)
                return true;
        }

        return false;
    }

    private static bool IsKnownTtsAudioSource(AudioSource source)
    {
        if (source == null)
            return false;

        if (source.GetComponentInParent<TtsOrchestrator>() != null)
            return true;

        string objectName = source.gameObject.name;
        return objectName.StartsWith("TtsAudioSource_", System.StringComparison.OrdinalIgnoreCase) ||
               objectName.IndexOf("Tts", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private Task EnsureInitializedAsync()
    {
        if (_orchestrator?.Service == null || _orchestrator.Service.IsReady)
            return Task.CompletedTask;

        if (_initializeTask == null || _initializeTask.IsFaulted || _initializeTask.IsCanceled)
            _initializeTask = EnsureInitializedInternalAsync();

        return _initializeTask;
    }

    private async Task EnsureInitializedInternalAsync()
    {
        if (_orchestrator == null)
            return;

        if (!_orchestrator.IsInitialized)
        {
            Debug.Log("[TtsPlayer] Waiting for TtsOrchestrator async initialization.", this);

            TaskCompletionSource<bool> initialized = new TaskCompletionSource<bool>();
            void HandleInitialized() => initialized.TrySetResult(true);

            _orchestrator.Initialized += HandleInitialized;
            try
            {
                if (!_orchestrator.IsInitialized)
                    await initialized.Task;
            }
            finally
            {
                _orchestrator.Initialized -= HandleInitialized;
            }
        }

        if (_orchestrator.Service == null || _orchestrator.Service.IsReady)
            return;

        Debug.Log("[TtsPlayer] Initializing TTS service through async StreamingAssets path.", this);
        await _orchestrator.Service.InitializeAsync();
    }
}
