using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using PonyuDev.SherpaOnnx.Asr.Offline;
using PonyuDev.SherpaOnnx.Asr.Offline.Engine;
using SpeechIntent.VoiceActivation;
using UnityEngine;
using UnityEngine.Networking;

namespace SpeechIntent
{
    /// <summary>
    /// Replays recorded WAV files through the same on-device recogniser the app uses at runtime.
    ///
    /// Why this exists: the emulator's microphone is silent, so the entire speech path — recognition,
    /// wake matching, local-vs-cloud policy, intent routing — was untestable without a headset and a person
    /// willing to speak into it. Recorded clips remove both constraints: the same audio can be replayed
    /// deterministically, before and after a model or parser change, which is the only way to tell whether
    /// something actually improved.
    ///
    /// Drop WAVs in <c>persistentDataPath/VoiceFixtures</c> (any sample rate; stereo is mixed to mono) and
    /// send <c>replay</c> through the debug command channel, or <c>replay &lt;file-or-folder&gt;</c>.
    /// </summary>
    public static class VoiceFixtureReplay
    {
        public const string FixtureFolderName = "VoiceFixtures";

        public static string DefaultFolder => Path.Combine(Application.persistentDataPath, FixtureFolderName);

        /// <summary>Runs every WAV in the fixture folder, or one file/folder when a path is supplied.</summary>
        public static IEnumerator Run(string spec, Action<string> report)
        {
            // AsrService is a plain object owned by the wake trigger, not a component, so the service is
            // reached through the trigger rather than searched for.
            VadAsrWakeTrigger trigger = UnityEngine.Object.FindFirstObjectByType<VadAsrWakeTrigger>(FindObjectsInactive.Include);
            AsrService asr = trigger != null ? trigger.AsrStream : null;
            if (asr == null)
            {
                report?.Invoke("[Fixture] the wake trigger has no recogniser yet (ASR not initialised); cannot replay.");
                yield break;
            }

            List<string> files = ResolveFiles(spec, report);
            if (files.Count == 0)
            {
                report?.Invoke($"[Fixture] no .wav files found in '{ResolvePath(spec)}'.");
                yield break;
            }

            report?.Invoke($"[Fixture] replaying {files.Count} clip(s) from '{ResolvePath(spec)}'.");

            int recognised = 0;
            foreach (string file in files)
            {
                AudioClip clip = null;
                yield return LoadClip(file, loaded => clip = loaded);
                if (clip == null)
                {
                    report?.Invoke($"[Fixture] FAILED to load '{Path.GetFileName(file)}'.");
                    continue;
                }

                float[] samples = ToMono(clip);
                int sampleRate = clip.frequency;
                float seconds = clip.length;
                UnityEngine.Object.Destroy(clip);

                AsrResult result = asr.Recognize(samples, sampleRate);
                string text = result != null ? (result.Text ?? string.Empty).Trim() : string.Empty;

                if (text.Length > 0) recognised++;

                LocalTranscriptDecision decision = LocalFirstTranscriptionPolicy.Decide(text, out string reason);
                SpeechIntent.OpenAiSpeechIntentService.TryBuildLocalPreflightCommand(text, out VoiceIntentCommand grammar);
                string intent = grammar != null ? grammar.intent.ToString() : "none";

                report?.Invoke(
                    $"[Fixture] {Path.GetFileName(file)} dur={seconds:0.00}s local='{text}' " +
                    $"decision={decision} reason='{reason}' grammar={intent}");
            }

            report?.Invoke($"[Fixture] done: {recognised}/{files.Count} clips produced text.");
        }

        static List<string> ResolveFiles(string spec, Action<string> report)
        {
            string path = ResolvePath(spec);
            var files = new List<string>();

            if (File.Exists(path)) { files.Add(path); return files; }
            if (!Directory.Exists(path))
            {
                report?.Invoke($"[Fixture] '{path}' does not exist.");
                return files;
            }

            string[] found = Directory.GetFiles(path, "*.wav", SearchOption.AllDirectories);
            Array.Sort(found, StringComparer.OrdinalIgnoreCase);
            files.AddRange(found);
            return files;
        }

        static string ResolvePath(string spec)
        {
            if (string.IsNullOrWhiteSpace(spec)) return DefaultFolder;
            spec = spec.Trim().Trim('"');
            return Path.IsPathRooted(spec) ? spec : Path.Combine(DefaultFolder, spec);
        }

        static IEnumerator LoadClip(string absolutePath, Action<AudioClip> onLoaded)
        {
            string uri = new Uri(absolutePath).AbsoluteUri;
            using UnityWebRequest request = UnityWebRequestMultimedia.GetAudioClip(uri, AudioType.WAV);
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[Fixture] load failed for '{absolutePath}': {request.error}");
                onLoaded?.Invoke(null);
                yield break;
            }

            onLoaded?.Invoke(DownloadHandlerAudioClip.GetContent(request));
        }

        /// <summary>Mixes to mono: the recogniser expects one channel, and a stereo clip would halve its
        /// effective sample rate and garble the audio.</summary>
        static float[] ToMono(AudioClip clip)
        {
            int channels = Mathf.Max(1, clip.channels);
            int frameCount = clip.samples;
            float[] interleaved = new float[frameCount * channels];
            clip.GetData(interleaved, 0);

            if (channels == 1) return interleaved;

            float[] mono = new float[frameCount];
            for (int frame = 0; frame < frameCount; frame++)
            {
                float sum = 0f;
                for (int channel = 0; channel < channels; channel++)
                    sum += interleaved[frame * channels + channel];
                mono[frame] = sum / channels;
            }

            return mono;
        }
    }
}
