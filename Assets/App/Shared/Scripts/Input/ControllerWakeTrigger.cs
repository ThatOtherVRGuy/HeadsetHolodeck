using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Holodeck.Input
{
    public sealed class ControllerWakeTrigger : MonoBehaviour, IWakeTrigger
    {
        [SerializeField] private InputActionReference wakeAction;
        [SerializeField] private bool enableOnEnable = true;
        [SerializeField] private bool disableSharedActionOnDisable;
        [SerializeField] private bool logDebugMessages = true;

        public event Action WakeTriggered;

        public bool IsTriggerEnabled { get; private set; }

        private void OnEnable()
        {
            if (enableOnEnable)
            {
                EnableTrigger();
            }
        }

        private void OnDisable()
        {
            if (enableOnEnable)
            {
                DisableTrigger();
            }
        }

        public void EnableTrigger()
        {
            if (IsTriggerEnabled)
            {
                return;
            }

            if (wakeAction == null || wakeAction.action == null)
            {
                Debug.LogError($"{nameof(ControllerWakeTrigger)} requires a valid InputActionReference.", this);
                return;
            }

            wakeAction.action.performed += OnWakeActionPerformed;
            wakeAction.action.Enable();
            IsTriggerEnabled = true;

            if (logDebugMessages)
            {
                Debug.Log($"{nameof(ControllerWakeTrigger)} enabled using action '{wakeAction.action.name}'.", this);
            }
        }

        public void DisableTrigger()
        {
            if (!IsTriggerEnabled)
            {
                return;
            }

            if (wakeAction != null && wakeAction.action != null)
            {
                wakeAction.action.performed -= OnWakeActionPerformed;
                if (disableSharedActionOnDisable)
                {
                    wakeAction.action.Disable();
                }
            }

            IsTriggerEnabled = false;

            if (logDebugMessages)
            {
                Debug.Log($"{nameof(ControllerWakeTrigger)} disabled.", this);
            }
        }

        private void OnWakeActionPerformed(InputAction.CallbackContext context)
        {
            if (!context.performed)
            {
                return;
            }

            if (logDebugMessages)
            {
                Debug.Log($"{nameof(ControllerWakeTrigger)} fired.", this);
            }

            WakeTriggered?.Invoke();
        }
    }
}
