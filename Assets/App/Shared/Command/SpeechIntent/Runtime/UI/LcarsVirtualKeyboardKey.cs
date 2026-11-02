using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SpeechIntent
{
    [RequireComponent(typeof(Button))]
    public sealed class LcarsVirtualKeyboardKey : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        public LcarsVirtualKeyboard keyboard;
        public string value = "";
        public bool isLetter;
        public TMP_Text label;
        public bool debugLogging;

        void Awake()
        {
            if (keyboard == null)
                keyboard = GetComponentInParent<LcarsVirtualKeyboard>(true);
            if (label == null)
                label = GetComponentInChildren<TMP_Text>(true);
            if (isLetter && keyboard != null)
                keyboard.RegisterLetterLabel(label);

            Button button = GetComponent<Button>();
            if (button.targetGraphic == null)
                button.targetGraphic = GetComponent<Image>();
            if (!HasPersistentPress(button))
            {
                button.onClick.RemoveListener(Press);
                button.onClick.AddListener(Press);
            }
        }

        public void Press()
        {
            if (keyboard == null)
                keyboard = GetComponentInParent<LcarsVirtualKeyboard>(true);
            if (debugLogging || (keyboard != null && keyboard.debugLogging))
                Debug.Log($"[LcarsVirtualKeyboardKey] Press '{value}' keyboard={(keyboard != null ? keyboard.name : "null")}", this);
            keyboard?.InputKey(value);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            LogPointer("Pointer enter", eventData);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            LogPointer("Pointer exit", eventData);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            LogPointer("Pointer click", eventData);
        }

        void LogPointer(string message, PointerEventData eventData)
        {
            if (!debugLogging && (keyboard == null || !keyboard.debugLogging))
                return;

            Debug.Log($"[LcarsVirtualKeyboardKey] {message} '{value}' pointer={eventData.pointerId}", this);
        }

        bool HasPersistentPress(Button button)
        {
            int count = button.onClick.GetPersistentEventCount();
            for (int i = 0; i < count; i++)
            {
                if (button.onClick.GetPersistentTarget(i) == this &&
                    button.onClick.GetPersistentMethodName(i) == nameof(Press))
                    return true;
            }

            return false;
        }
    }
}
