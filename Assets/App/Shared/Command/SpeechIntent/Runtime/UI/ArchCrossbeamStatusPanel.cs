using TMPro;
using GaussianSplatting.Runtime;
using UnityEngine;
using UnityEngine.UI;
using WorldLabs.Runtime;

namespace SpeechIntent
{
    public class ArchCrossbeamStatusPanel : MonoBehaviour
    {
        [Header("Labels")]
        public TMP_Text modeLabel;
        public TMP_Text messageLabel;
        public TMP_Text messageWrapLabel;
        public TMP_Text healthLabel;
        public TMP_Text appTimeLabel;
        public TMP_Text worldTimeLabel;

        [Header("World Timing")]
        public WorldLabsWorldManager worldManager;
        public bool autoFindWorldManager = true;
        public string noWorldTimeText = "--:--:--";
        public string combinedTimeFormat = "{0}  RUN {1}  WORLD {2}";

        [Header("Scroll")]
        public RectTransform scrollViewport;
        public RectTransform messageTransform;
        public RectTransform messageWrapTransform;
        public float scrollSpeed = 90f;
        public float wrapGap = 80f;
        public float restartDelay = 1.2f;

        [Header("Colors")]
        public Graphic statusColorTarget;
        public Graphic flashTarget;
        public Color infoColor = new Color(1.00f, 0.78f, 0.08f, 1f);
        public Color successColor = new Color(0.48f, 0.55f, 1.00f, 1f);
        public Color warningColor = new Color(1.00f, 0.55f, 0.07f, 1f);
        public Color errorColor = new Color(0.90f, 0.20f, 0.10f, 1f);

        [Header("Alert Flash")]
        public bool flashWarnings = false;
        public bool flashErrors = true;
        public float warningFlashesPerSecond = 1f;
        public float errorFlashesPerSecond = 2f;

        float _scrollX;
        float _scrollWidth;
        float _delayRemaining;
        bool _isScrolling;
        bool _isFlashing;
        float _flashFrequency;
        Color _flashColor;
        float _worldStartRealtime;
        float _nextClockUpdateRealtime;
        bool _hasWorld;
        string _lastRealtimeText;
        string _lastAppTimeText;
        string _lastWorldTimeText;
        static float s_appStartRealtime;
        static bool s_appStartInitialized;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetAppTimer()
        {
            s_appStartRealtime = 0f;
            s_appStartInitialized = false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void StartAppTimer()
        {
            EnsureAppTimerStarted();
        }

        void OnEnable()
        {
            EnsureAppTimerStarted();
            ResolveWorldManager();
            SubscribeWorldManager();
            SyncWorldTimerFromManager();
            ArchStatusBus.StatusPosted += HandleStatusPosted;
            ApplyStatus(ArchStatusBus.LastMessage);
            UpdateTimeLabels(force: true);
        }

        void OnDisable()
        {
            ArchStatusBus.StatusPosted -= HandleStatusPosted;
            UnsubscribeWorldManager();
        }

        void Update()
        {
            if (Time.unscaledTime >= _nextClockUpdateRealtime)
                UpdateTimeLabels(force: false);

            UpdateFlash();

            if (_isScrolling && messageTransform != null && messageWrapTransform != null)
            {
                if (_delayRemaining > 0f)
                {
                    _delayRemaining -= Time.unscaledDeltaTime;
                    return;
                }

                _scrollX -= scrollSpeed * Time.unscaledDeltaTime;
                if (_scrollX <= -_scrollWidth - wrapGap)
                    _scrollX += _scrollWidth + wrapGap;

                messageTransform.anchoredPosition = new Vector2(_scrollX, messageTransform.anchoredPosition.y);
                messageWrapTransform.anchoredPosition = new Vector2(_scrollX + _scrollWidth + wrapGap, messageWrapTransform.anchoredPosition.y);
            }
        }

        void HandleStatusPosted(ArchStatusMessage status)
        {
            ApplyStatus(status);
        }

        public void ApplyStatus(ArchStatusMessage status)
        {
            if (modeLabel != null)
                modeLabel.text = status.mode.ToUpperInvariant();

            string message = string.IsNullOrWhiteSpace(status.message)
                ? "Holodeck systems standing by."
                : status.message;

            if (messageLabel != null)
                messageLabel.text = message;
            if (messageWrapLabel != null)
                messageWrapLabel.text = message;
            UpdateTimeLabels(force: true);

            Color color = ColorFor(status.level);
            if (statusColorTarget != null)
                statusColorTarget.color = color;
            if (flashTarget != null)
                flashTarget.color = color;
            if (modeLabel != null)
                modeLabel.color = Color.black;

            ConfigureFlash(status.level, color);
            RecalculateScroll();
        }

        public void RecalculateScroll()
        {
            if (scrollViewport == null || messageLabel == null || messageTransform == null || messageWrapTransform == null)
                return;

            messageLabel.ForceMeshUpdate();
            if (messageWrapLabel != null)
                messageWrapLabel.ForceMeshUpdate();

            float viewportWidth = scrollViewport.rect.width;
            _scrollWidth = Mathf.Max(messageLabel.preferredWidth + 8f, viewportWidth);
            _isScrolling = messageLabel.preferredWidth > viewportWidth;
            _scrollX = 0f;
            _delayRemaining = restartDelay;

            messageTransform.sizeDelta = new Vector2(_scrollWidth, messageTransform.sizeDelta.y);
            messageWrapTransform.sizeDelta = new Vector2(_scrollWidth, messageWrapTransform.sizeDelta.y);
            messageTransform.anchoredPosition = Vector2.zero;
            messageWrapTransform.anchoredPosition = new Vector2(_scrollWidth + wrapGap, 0f);

            if (messageWrapLabel != null)
                messageWrapLabel.gameObject.SetActive(_isScrolling);

            messageLabel.alignment = _isScrolling ? TextAlignmentOptions.Left : TextAlignmentOptions.Center;
        }

        Color ColorFor(ArchStatusLevel level)
        {
            return level switch
            {
                ArchStatusLevel.Success => successColor,
                ArchStatusLevel.Warning => warningColor,
                ArchStatusLevel.Error => errorColor,
                _ => infoColor
            };
        }

        void ConfigureFlash(ArchStatusLevel level, Color color)
        {
            _flashColor = color;
            _isFlashing =
                (level == ArchStatusLevel.Error && flashErrors) ||
                (level == ArchStatusLevel.Warning && flashWarnings);
            _flashFrequency = level == ArchStatusLevel.Error
                ? Mathf.Max(0.1f, errorFlashesPerSecond)
                : Mathf.Max(0.1f, warningFlashesPerSecond);

            SetFlashVisible(true);
        }

        void UpdateFlash()
        {
            if (!_isFlashing)
                return;

            float phase = Mathf.PingPong(Time.unscaledTime * _flashFrequency * 2f, 1f);
            SetFlashVisible(phase > 0.5f);
        }

        void SetFlashVisible(bool visible)
        {
            Color color = _flashColor;
            color.a = visible ? _flashColor.a : 0.08f;

            if (flashTarget != null)
                flashTarget.color = color;
            else if (statusColorTarget != null)
                statusColorTarget.color = color;

            if (modeLabel != null)
                modeLabel.enabled = visible || !_isFlashing;
        }

        void ResolveWorldManager()
        {
            if (worldManager == null && autoFindWorldManager)
                worldManager = FindFirstObjectByType<WorldLabsWorldManager>(FindObjectsInactive.Include);
        }

        void SubscribeWorldManager()
        {
            if (worldManager == null)
                return;

            worldManager.OnWorldLoaded += HandleWorldLoaded;
            worldManager.OnWorldUnloaded += HandleWorldUnloaded;
        }

        void UnsubscribeWorldManager()
        {
            if (worldManager == null)
                return;

            worldManager.OnWorldLoaded -= HandleWorldLoaded;
            worldManager.OnWorldUnloaded -= HandleWorldUnloaded;
        }

        void SyncWorldTimerFromManager()
        {
            _hasWorld = worldManager != null && worldManager.LoadedWorldIds != null && worldManager.LoadedWorldIds.Count > 0;
            if (_hasWorld && _worldStartRealtime <= 0f)
                _worldStartRealtime = Time.realtimeSinceStartup;
        }

        void HandleWorldLoaded(string worldId, GaussianSplatRenderer renderer)
        {
            _hasWorld = true;
            _worldStartRealtime = Time.realtimeSinceStartup;
            UpdateTimeLabels(force: true);
        }

        void HandleWorldUnloaded(string worldId)
        {
            _hasWorld = worldManager != null && worldManager.LoadedWorldIds != null && worldManager.LoadedWorldIds.Count > 0;
            if (!_hasWorld)
                _worldStartRealtime = 0f;
            UpdateTimeLabels(force: true);
        }

        void UpdateTimeLabels(bool force)
        {
            string realtime = System.DateTime.Now.ToString("HH:mm:ss");
            EnsureAppTimerStarted();
            string appTime = FormatDuration(Time.realtimeSinceStartup - s_appStartRealtime);
            string worldTime = _hasWorld && _worldStartRealtime > 0f
                ? FormatDuration(Time.realtimeSinceStartup - _worldStartRealtime)
                : noWorldTimeText;

            if (!force && realtime == _lastRealtimeText && appTime == _lastAppTimeText && worldTime == _lastWorldTimeText)
                return;

            _lastRealtimeText = realtime;
            _lastAppTimeText = appTime;
            _lastWorldTimeText = worldTime;
            _nextClockUpdateRealtime = Mathf.Floor(Time.unscaledTime) + 1f;

            if (healthLabel != null)
                healthLabel.text = string.Format(combinedTimeFormat, realtime, appTime, worldTime);
            if (appTimeLabel != null)
                appTimeLabel.text = appTime;
            if (worldTimeLabel != null)
                worldTimeLabel.text = worldTime;
        }

        static string FormatDuration(float seconds)
        {
            seconds = Mathf.Max(0f, seconds);
            int totalSeconds = Mathf.FloorToInt(seconds);
            int hours = totalSeconds / 3600;
            int minutes = totalSeconds / 60 % 60;
            int secs = totalSeconds % 60;
            return $"{hours}:{minutes:00}:{secs:00}";
        }

        static void EnsureAppTimerStarted()
        {
            if (s_appStartInitialized)
                return;

            s_appStartRealtime = Time.realtimeSinceStartup;
            s_appStartInitialized = true;
        }
    }
}
