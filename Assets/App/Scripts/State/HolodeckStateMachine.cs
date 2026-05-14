using System;
using UnityEngine;

namespace Holodeck.State
{
    public sealed class HolodeckStateMachine : MonoBehaviour
    {
        [SerializeField] private HolodeckState initialState = HolodeckState.Idle;
        [SerializeField] private bool logStateChanges = true;

        [Header("Runtime Debug")]
        [SerializeField] private HolodeckState currentState;
        [SerializeField, TextArea] private string lastErrorMessage = string.Empty;

        public HolodeckState CurrentState => currentState;
        public string LastErrorMessage => lastErrorMessage;

        public event Action<HolodeckState, HolodeckState> StateChanged;
        public event Action<string> ErrorRaised;

        private void Awake()
        {
            currentState = initialState;
            lastErrorMessage = string.Empty;
        }

        public bool CanTransitionTo(HolodeckState targetState)
        {
            if (targetState == currentState)
            {
                return true;
            }

            switch (currentState)
            {
                case HolodeckState.Idle:
                    return targetState == HolodeckState.ListeningForCommand
                        || targetState == HolodeckState.Generating
                        || targetState == HolodeckState.Ready
                        || targetState == HolodeckState.Error;

                case HolodeckState.ListeningForCommand:
                    return targetState == HolodeckState.Interpreting
                        || targetState == HolodeckState.Idle
                        || targetState == HolodeckState.Error;

                case HolodeckState.Interpreting:
                    return targetState == HolodeckState.Generating
                        || targetState == HolodeckState.Idle
                        || targetState == HolodeckState.Error;

                case HolodeckState.Generating:
                    return targetState == HolodeckState.Ready
                        || targetState == HolodeckState.Idle
                        || targetState == HolodeckState.Error;

                case HolodeckState.Ready:
                    return targetState == HolodeckState.ListeningForCommand
                        || targetState == HolodeckState.Generating
                        || targetState == HolodeckState.Idle
                        || targetState == HolodeckState.Error;

                case HolodeckState.Error:
                    return targetState == HolodeckState.Idle
                        || targetState == HolodeckState.ListeningForCommand;

                default:
                    return false;
            }
        }

        public bool TryTransitionTo(HolodeckState targetState)
        {
            if (!CanTransitionTo(targetState))
            {
                if (logStateChanges)
                {
                    Debug.LogWarning(
                        $"Rejected invalid state transition: {currentState} -> {targetState}",
                        this);
                }

                return false;
            }

            TransitionInternal(targetState, clearErrorOnNonErrorState: true);
            return true;
        }

        public void ForceState(HolodeckState targetState)
        {
            TransitionInternal(targetState, clearErrorOnNonErrorState: true);
        }

        public void SetError(string message)
        {
            lastErrorMessage = string.IsNullOrWhiteSpace(message) ? "Unknown error." : message;

            HolodeckState previous = currentState;
            currentState = HolodeckState.Error;

            if (logStateChanges)
            {
                Debug.LogError($"Holodeck error: {lastErrorMessage}", this);
            }

            StateChanged?.Invoke(previous, currentState);
            ErrorRaised?.Invoke(lastErrorMessage);
        }

        public void ClearErrorAndReturnToIdle()
        {
            lastErrorMessage = string.Empty;
            ForceState(HolodeckState.Idle);
        }

        private void TransitionInternal(HolodeckState targetState, bool clearErrorOnNonErrorState)
        {
            HolodeckState previous = currentState;
            currentState = targetState;

            if (clearErrorOnNonErrorState && targetState != HolodeckState.Error)
            {
                lastErrorMessage = string.Empty;
            }

            if (logStateChanges && previous != targetState)
            {
                Debug.Log($"Holodeck state: {previous} -> {targetState}", this);
            }

            StateChanged?.Invoke(previous, currentState);
        }
    }
}
