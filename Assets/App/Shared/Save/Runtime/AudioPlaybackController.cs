using System;
using UnityEngine;

namespace Holodeck.Save
{
    public enum AudioPlaybackMode
    {
        Loop = 0,
        Once = 1,
        Interval = 2,
        RandomInterval = 3
    }

    /// <summary>
    /// Reusable playback behavior for a GameObject with an AudioSource.
    /// Add this to a prefab to make it loop, play once, or fire at fixed/random intervals.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class AudioPlaybackController : MonoBehaviour
    {
        private const float ClipDurationSafetyMultiplier = 1.1f;
        private const float DefaultRandomIntervalSeconds = 10f;
        private const float DefaultRandomVarianceSeconds = 2f;

        public AudioPlaybackMode mode = AudioPlaybackMode.Loop;
        public float intervalSeconds = 10f;
        public float intervalVarianceSeconds = 0f;
        public bool playOnEnable = false;

        private AudioSource _source;
        private float _nextPlayTime = -1f;

        public AudioSource Source => _source != null ? _source : (_source = GetComponent<AudioSource>());

        private void Awake()
        {
            _source = GetComponent<AudioSource>();
            ApplySourceLoopFlag();
        }

        private void OnEnable()
        {
            if (playOnEnable)
                Restart();
        }

        private void OnDisable()
        {
            _nextPlayTime = -1f;
        }

        private void Update()
        {
            if (mode != AudioPlaybackMode.Interval && mode != AudioPlaybackMode.RandomInterval)
                return;

            if (Source.clip == null || Time.time < _nextPlayTime)
                return;

            Source.Play();
            ScheduleNext();
        }

        public void Configure(AudioPlaybackMode newMode, float interval, float variance, bool playNow)
        {
            mode = newMode;
            intervalSeconds = Mathf.Max(0f, interval);
            intervalVarianceSeconds = Mathf.Max(0f, variance);
            NormalizeTimingForClip();
            Restart(playNow);
        }

        public void Restart(bool playNow = true)
        {
            ApplySourceLoopFlag();

            if (Source.clip == null)
            {
                _nextPlayTime = Time.time + ResolveInterval();
                return;
            }

            if (mode == AudioPlaybackMode.Loop)
            {
                if (playNow && !Source.isPlaying)
                    Source.Play();
                return;
            }

            if (mode == AudioPlaybackMode.Once)
            {
                if (playNow)
                    Source.Play();
                _nextPlayTime = -1f;
                return;
            }

            if (playNow)
                Source.Play();
            ScheduleNext();
        }

        public void PlayNow()
        {
            if (Source.clip != null)
                Source.Play();

            if (mode == AudioPlaybackMode.Interval || mode == AudioPlaybackMode.RandomInterval)
                ScheduleNext();
        }

        public void StopPlayback()
        {
            Source.Stop();
            _nextPlayTime = float.PositiveInfinity;
        }

        private void ApplySourceLoopFlag()
        {
            Source.loop = mode == AudioPlaybackMode.Loop;
        }

        private void ScheduleNext()
        {
            NormalizeTimingForClip();
            _nextPlayTime = Time.time + ResolveInterval();
        }

        private float ResolveInterval()
        {
            float baseInterval = intervalSeconds > 0f ? intervalSeconds : DefaultRandomIntervalSeconds;
            if (mode != AudioPlaybackMode.RandomInterval || intervalVarianceSeconds <= 0f)
                return Mathf.Max(GetSafeClipInterval(), baseInterval);

            float min = Mathf.Max(GetSafeClipInterval(), baseInterval - intervalVarianceSeconds);
            float max = Math.Max(min, baseInterval + intervalVarianceSeconds);
            return UnityEngine.Random.Range(min, max);
        }

        private void NormalizeTimingForClip()
        {
            if (mode != AudioPlaybackMode.Interval && mode != AudioPlaybackMode.RandomInterval)
                return;

            float safeInterval = GetSafeClipInterval();
            if (safeInterval <= 0f)
                return;

            if (mode == AudioPlaybackMode.Interval)
            {
                intervalSeconds = Mathf.Max(intervalSeconds, safeInterval);
                return;
            }

            if (intervalSeconds <= 0f)
                intervalSeconds = Mathf.Max(DefaultRandomIntervalSeconds, safeInterval * 2f);

            intervalVarianceSeconds = Mathf.Max(intervalVarianceSeconds, DefaultRandomVarianceSeconds, safeInterval);

            if (intervalSeconds - intervalVarianceSeconds < safeInterval)
                intervalSeconds = safeInterval + intervalVarianceSeconds;
        }

        private float GetSafeClipInterval()
        {
            AudioClip clip = Source.clip;
            return clip != null && clip.length > 0f
                ? clip.length * ClipDurationSafetyMultiplier
                : 0f;
        }
    }
}
