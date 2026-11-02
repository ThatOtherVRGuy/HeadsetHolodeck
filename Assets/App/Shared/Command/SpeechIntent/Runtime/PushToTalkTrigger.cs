using UnityEngine;
using UnityEngine.InputSystem;

namespace SpeechIntent
{
    /// <summary>
    /// Bridges the WakeCommand InputAction to VoiceCommandRouter push-to-talk.
    /// action.started  → router.BeginRecording()
    /// action.canceled → router.EndRecordingAndProcess()
    /// </summary>
    public class PushToTalkTrigger : MonoBehaviour
    {
        public InputActionReference pushToTalkAction;
        public VoiceCommandRouter   router;
        public bool logDebugMessages = true;

        [Tooltip("Ignore a press that arrives within this many seconds of the previous release. The " +
                 "Space key is bound both directly and through the emulated controller button, so one " +
                 "physical press can arrive twice and produce an empty second capture.")]
        public float duplicatePressWindowSeconds = 0.35f;

        bool _recording;
        float _lastReleaseTime = -999f;

        private void OnEnable()
        {
            if (pushToTalkAction == null || pushToTalkAction.action == null)
            {
                Debug.LogWarning("[PushToTalkTrigger] pushToTalkAction is not assigned.", this);
                return;
            }

            if (router == null)
            {
                Debug.LogWarning("[PushToTalkTrigger] router is not assigned.", this);
                return;
            }

            pushToTalkAction.action.started  += OnActionStarted;
            pushToTalkAction.action.canceled += OnActionCanceled;
            pushToTalkAction.action.Enable();

            if (logDebugMessages)
            {
                Debug.Log(
                    $"[PushToTalkTrigger] Enabled action '{pushToTalkAction.action.actionMap?.name}/{pushToTalkAction.action.name}' " +
                    $"with {pushToTalkAction.action.bindings.Count} bindings.",
                    this);
            }
        }

        private void OnDisable()
        {
            if (pushToTalkAction?.action == null) return;

            pushToTalkAction.action.started  -= OnActionStarted;
            pushToTalkAction.action.canceled -= OnActionCanceled;
            // Do NOT call action.Disable() — the action is shared and may be
            // used by other components (e.g. ControllerWakeTrigger).
        }

        private void OnActionStarted(InputAction.CallbackContext context)
        {
            if (logDebugMessages)
                Debug.Log($"[PushToTalkTrigger] Press started from {context.control?.path ?? "unknown control"}.", this);

            float sinceRelease = Time.unscaledTime - _lastReleaseTime;
            if (_recording || sinceRelease < duplicatePressWindowSeconds)
            {
                if (logDebugMessages)
                    Debug.Log($"[PushToTalkTrigger] Ignoring duplicate press ({sinceRelease:0.00}s after release, recording={_recording}).", this);
                return;
            }

            _recording = true;
            router?.BeginRecording();
        }

        private void OnActionCanceled(InputAction.CallbackContext context)
        {
            if (logDebugMessages)
                Debug.Log($"[PushToTalkTrigger] Press released from {context.control?.path ?? "unknown control"}.", this);

            // A release with no accepted press is the other half of a double-fire; ignoring it is what
            // stops the empty second capture and its "empty transcript" error.
            if (!_recording)
                return;

            _recording = false;
            _lastReleaseTime = Time.unscaledTime;
            router?.EndRecordingAndProcess();
        }
    }
}
