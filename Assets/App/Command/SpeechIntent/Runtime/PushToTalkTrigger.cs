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
            router?.BeginRecording();
        }

        private void OnActionCanceled(InputAction.CallbackContext context)
        {
            if (logDebugMessages)
                Debug.Log($"[PushToTalkTrigger] Press released from {context.control?.path ?? "unknown control"}.", this);
            router?.EndRecordingAndProcess();
        }
    }
}
