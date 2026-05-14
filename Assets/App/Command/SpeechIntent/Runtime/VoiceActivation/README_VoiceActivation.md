# Headset Holodeck Voice Activation

State summary:

`Disabled -> ListeningForWake -> WakeDetected -> ProcessingCommand -> Cooldown -> ListeningForWake`

If the wake phrase has no inline command:

`WakeDetected -> ListeningForCommand -> ProcessingCommand -> Cooldown -> ListeningForWake`

Error cases enter `Error`, play the optional error cue, then usually pass through cooldown.

Inspector setup:

1. Create `VoiceActivationConfig` from `Assets > Create > Speech Intent > Voice Activation Config`.
2. Add `VadAsrWakeTrigger` to the `Systems/SpeechIntent` object or a sibling voice object.
3. Add `HeadsetHolodeckCommandRouter` and assign the existing `VoiceCommandRouter`.
4. Add `HeadsetHolodeckVoiceController`.
5. Assign the config to the controller and wake trigger.
6. Assign `Wake Trigger Behaviour` to the `VadAsrWakeTrigger` component.
7. Assign `Command Recognizer Behaviour` to the same `VadAsrWakeTrigger` for today's VAD/ASR flow.
8. Assign status text and optional audio cues if desired.

Quest / Android notes:

- The app must request and have `android.permission.RECORD_AUDIO`.
- Ponyu `MicrophoneSource` requests microphone permission when `requestMicrophonePermission` is enabled.
- If Quest returns silence through Unity's microphone API, Ponyu's `MicrophoneSource` includes an Android AudioRecord fallback path.

Sherpa notes:

- `VadAsrWakeTrigger` is the only class that imports Ponyu/Sherpa types.
- Today it uses `MicrophoneSource`, `VadService`, and offline `AsrService`.
- If Ponyu adds a Unity KWS wrapper, put that code in `KwsWakeTrigger` and keep `HeadsetHolodeckVoiceController` unchanged.
- If exact Ponyu API names change, update only `VadAsrWakeTrigger` or `KwsWakeTrigger`.
