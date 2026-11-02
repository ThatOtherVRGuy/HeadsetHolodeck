using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SpeechIntent
{
    public sealed class LcarsVirtualKeyboard : MonoBehaviour
    {
        public GameObject keyboardRoot;
        public TMP_Text targetLabel;
        public TMP_InputField commandInputField;
        public VoiceCommandRouter voiceCommandRouter;
        public bool autoShowForSelectedInput = true;
        public bool mirrorTargetTextInCommandField = true;
        public float fadeDuration = 0.5f;
        public bool hideOnSubmit;
        public bool debugLogging;

        readonly List<TMP_Text> _letterLabels = new List<TMP_Text>();
        TMP_InputField _tmpTarget;
        InputField _legacyTarget;
        bool _shift;
        float _nextFocusedFieldCheckTime;
        bool _visible;
        Coroutine _visibilityRoutine;

        void Awake()
        {
            if (keyboardRoot == null)
                keyboardRoot = gameObject;
            if (voiceCommandRouter == null)
                voiceCommandRouter = FindFirstObjectByType<VoiceCommandRouter>(FindObjectsInactive.Include);

            EnsureRaycastSetup();
            bool visibleAtStart = IsVisibleByCanvasGroup();
            ApplyVisibilityImmediate(visibleAtStart);
        }

        void OnEnable()
        {
            if (keyboardRoot == null)
                keyboardRoot = gameObject;
            EnsureRaycastSetup();
        }

        void Update()
        {
            if (!autoShowForSelectedInput || EventSystem.current == null)
                return;

            GameObject selected = EventSystem.current.currentSelectedGameObject;
            if (selected == null)
            {
                CheckFocusedFieldOnInterval();
                return;
            }

            if (keyboardRoot != null && selected.transform.IsChildOf(keyboardRoot.transform))
                return;

            TMP_InputField tmp = selected.GetComponent<TMP_InputField>();
            if (tmp != null)
            {
                SetTarget(tmp);
                return;
            }

            InputField legacy = selected.GetComponent<InputField>();
            if (legacy != null)
            {
                SetTarget(legacy);
                return;
            }

            CheckFocusedFieldOnInterval();
        }

        public void SetTarget(TMP_InputField input)
        {
            _tmpTarget = input;
            _legacyTarget = null;
            Log($"Target TMP input set: {InputName(input)}");
            RefreshTargetLabel();
            RefreshCommandInputMirror();
            SetVisible(true);
        }

        public void SetTarget(InputField input)
        {
            _legacyTarget = input;
            _tmpTarget = null;
            Log($"Target legacy input set: {InputName(input)}");
            RefreshTargetLabel();
            RefreshCommandInputMirror();
            SetVisible(true);
        }

        public void FocusCommandInput()
        {
            if (commandInputField == null)
                return;

            commandInputField.Select();
            commandInputField.ActivateInputField();
            SetTarget(commandInputField);
        }

        public void ShowCommandInput()
        {
            FocusCommandInput();
        }

        public void SubmitCommand()
        {
            if (commandInputField == null)
                return;

            string text = commandInputField.text;
            if (voiceCommandRouter == null)
                voiceCommandRouter = FindFirstObjectByType<VoiceCommandRouter>(FindObjectsInactive.Include);

            if (voiceCommandRouter != null)
                voiceCommandRouter.SubmitTypedCommand(text);
            else
                ArchStatusBus.Warning("Typed command router not found.", "TYPE");
        }

        public void InputKey(string value)
        {
            if (string.IsNullOrEmpty(value))
                return;

            Log($"InputKey '{value}' shift={_shift} target={CurrentTargetName()}");
            if (value.Length == 1 && char.IsLetter(value[0]))
                value = _shift ? value.ToUpperInvariant() : value.ToLowerInvariant();

            InsertText(value);
            if (_shift && value.Length == 1 && char.IsLetter(value[0]))
                SetShift(false);
        }

        public void Space() => InsertText(" ");
        public void Slash() => InsertText("/");
        public void DotCom() => InsertText(".com");

        public void Backspace()
        {
            Log($"Backspace target={CurrentTargetName()}");
            if (_tmpTarget != null)
            {
                int pos = Mathf.Clamp(_tmpTarget.stringPosition, 0, _tmpTarget.text.Length);
                if (pos <= 0)
                    return;

                _tmpTarget.text = _tmpTarget.text.Remove(pos - 1, 1);
                _tmpTarget.stringPosition = pos - 1;
                _tmpTarget.caretPosition = pos - 1;
                _tmpTarget.ForceLabelUpdate();
                RefreshCommandInputMirror();
                return;
            }

            if (_legacyTarget != null)
            {
                int pos = Mathf.Clamp(_legacyTarget.caretPosition, 0, _legacyTarget.text.Length);
                if (pos <= 0)
                    return;

                _legacyTarget.text = _legacyTarget.text.Remove(pos - 1, 1);
                _legacyTarget.caretPosition = pos - 1;
                RefreshCommandInputMirror();
            }
        }

        public void Clear()
        {
            Log($"Clear target={CurrentTargetName()}");
            if (_tmpTarget != null)
                _tmpTarget.text = "";
            if (_legacyTarget != null)
                _legacyTarget.text = "";
            RefreshCommandInputMirror();
        }

        public void SubmitTarget()
        {
            Log($"SubmitTarget target={CurrentTargetName()}");
            if (_tmpTarget != null)
            {
                _tmpTarget.onSubmit?.Invoke(_tmpTarget.text);
                if (_tmpTarget == commandInputField)
                    SubmitCommand();
            }
            else if (_legacyTarget != null)
            {
                _legacyTarget.onSubmit?.Invoke(_legacyTarget.text);
            }

            if (hideOnSubmit)
                SetVisible(false);
        }

        public void ToggleShift() => SetShift(!_shift);

        public void Hide() => SetVisible(false);

        public void Show()
        {
            Log("Show requested.");
            if (_tmpTarget == null && _legacyTarget == null)
                FocusCommandInput();
            SetVisible(true);
        }

        public void RegisterLetterLabel(TMP_Text label)
        {
            if (label != null && !_letterLabels.Contains(label))
                _letterLabels.Add(label);
        }

        void InsertText(string value)
        {
            if (_tmpTarget == null && _legacyTarget == null)
            {
                Log($"No target for '{value}'. Focusing command input.");
                FocusCommandInput();
            }

            if (_tmpTarget != null)
            {
                int pos = Mathf.Clamp(_tmpTarget.stringPosition, 0, _tmpTarget.text.Length);
                Log($"Insert '{value}' into TMP {InputName(_tmpTarget)} at {pos}. Before='{_tmpTarget.text}'");
                _tmpTarget.text = _tmpTarget.text.Insert(pos, value);
                _tmpTarget.stringPosition = pos + value.Length;
                _tmpTarget.caretPosition = pos + value.Length;
                _tmpTarget.ForceLabelUpdate();
                Log($"TMP after='{_tmpTarget.text}' pos={_tmpTarget.stringPosition}");
                RefreshCommandInputMirror();
                return;
            }

            if (_legacyTarget != null)
            {
                int pos = Mathf.Clamp(_legacyTarget.caretPosition, 0, _legacyTarget.text.Length);
                Log($"Insert '{value}' into legacy {InputName(_legacyTarget)} at {pos}. Before='{_legacyTarget.text}'");
                _legacyTarget.text = _legacyTarget.text.Insert(pos, value);
                _legacyTarget.caretPosition = pos + value.Length;
                Log($"Legacy after='{_legacyTarget.text}' pos={_legacyTarget.caretPosition}");
                RefreshCommandInputMirror();
                return;
            }

            Log($"Insert '{value}' failed because no target is available.");
        }

        void SetTargetFromFocusedField()
        {
            foreach (TMP_InputField input in FindObjectsByType<TMP_InputField>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (input == null || !input.isFocused)
                    continue;
                if (keyboardRoot != null && input.transform.IsChildOf(keyboardRoot.transform))
                    continue;

                SetTarget(input);
                return;
            }

            foreach (InputField input in FindObjectsByType<InputField>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (input == null || !input.isFocused)
                    continue;
                if (keyboardRoot != null && input.transform.IsChildOf(keyboardRoot.transform))
                    continue;

                SetTarget(input);
                return;
            }
        }

        void CheckFocusedFieldOnInterval()
        {
            if (Time.unscaledTime < _nextFocusedFieldCheckTime)
                return;

            _nextFocusedFieldCheckTime = Time.unscaledTime + 0.2f;
            SetTargetFromFocusedField();
        }

        void SetShift(bool active)
        {
            _shift = active;
            foreach (TMP_Text label in _letterLabels)
            {
                if (label == null || string.IsNullOrEmpty(label.text))
                    continue;
                label.text = _shift ? label.text.ToUpperInvariant() : label.text.ToLowerInvariant();
            }
        }

        void SetVisible(bool visible)
        {
            if (keyboardRoot == null)
                return;

            Log($"SetVisible({visible})");
            EnsureRaycastSetup();
            if (_visible == visible)
            {
                if (visible)
                    SetRenderSurfaceEnabled(true);
                return;
            }

            _visible = visible;

            if (_visibilityRoutine != null)
                StopCoroutine(_visibilityRoutine);

            if (keyboardRoot != gameObject && visible && !keyboardRoot.activeSelf)
                keyboardRoot.SetActive(visible);

            if (!isActiveAndEnabled || fadeDuration <= 0f)
            {
                ApplyVisibilityImmediate(visible);
                return;
            }

            _visibilityRoutine = StartCoroutine(FadeVisibility(visible));
        }

        IEnumerator FadeVisibility(bool visible)
        {
            CanvasGroup group = GetOrCreateCanvasGroup();
            float startAlpha = group.alpha;
            float endAlpha = visible ? 1f : 0f;

            SetRenderSurfaceEnabled(true);
            group.interactable = visible;
            group.blocksRaycasts = visible;

            float elapsed = 0f;
            while (elapsed < fadeDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                group.alpha = Mathf.Lerp(startAlpha, endAlpha, Mathf.Clamp01(elapsed / fadeDuration));
                yield return null;
            }

            group.alpha = endAlpha;
            group.interactable = visible;
            group.blocksRaycasts = visible;

            if (!visible)
            {
                SetRenderSurfaceEnabled(false);
                if (keyboardRoot != gameObject)
                    keyboardRoot.SetActive(false);
            }

            _visibilityRoutine = null;
        }

        void ApplyVisibilityImmediate(bool visible)
        {
            _visible = visible;
            CanvasGroup group = GetOrCreateCanvasGroup();
            group.alpha = visible ? 1f : 0f;
            group.interactable = visible;
            group.blocksRaycasts = visible;
            SetRenderSurfaceEnabled(visible);
            if (keyboardRoot != gameObject)
                keyboardRoot.SetActive(visible);
        }

        CanvasGroup GetOrCreateCanvasGroup()
        {
            CanvasGroup group = keyboardRoot.GetComponent<CanvasGroup>();
            return group != null ? group : keyboardRoot.AddComponent<CanvasGroup>();
        }

        void SetRenderSurfaceEnabled(bool enabled)
        {
            Canvas canvas = keyboardRoot.GetComponent<Canvas>();
            if (canvas != null)
                canvas.enabled = enabled;

            GraphicRaycaster graphicRaycaster = keyboardRoot.GetComponent<GraphicRaycaster>();
            if (graphicRaycaster != null)
                graphicRaycaster.enabled = enabled;

            const string typeName = "UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster, Unity.XR.Interaction.Toolkit";
            System.Type raycasterType = System.Type.GetType(typeName);
            if (raycasterType != null && keyboardRoot.GetComponent(raycasterType) is Behaviour trackedRaycaster)
                trackedRaycaster.enabled = enabled;

            if (debugLogging)
                Log($"Render surface enabled={enabled} visible={_visible}");
        }

        bool IsVisibleByCanvasGroup()
        {
            if (keyboardRoot == null)
                return false;

            CanvasGroup group = keyboardRoot.GetComponent<CanvasGroup>();
            return group != null && group.alpha > 0.01f;
        }

        void EnsureRaycastSetup()
        {
            if (keyboardRoot == null)
                return;

            Canvas canvas = keyboardRoot.GetComponent<Canvas>();
            if (canvas != null && canvas.renderMode == RenderMode.WorldSpace && canvas.worldCamera == null)
            {
                canvas.worldCamera = Camera.main != null
                    ? Camera.main
                    : FindFirstObjectByType<Camera>(FindObjectsInactive.Exclude);
                Log($"Assigned world-space canvas event camera: {(canvas.worldCamera != null ? canvas.worldCamera.name : "none")}");
            }

            if (keyboardRoot.GetComponent<GraphicRaycaster>() == null)
                keyboardRoot.AddComponent<GraphicRaycaster>();

            EnsureTrackedDeviceRaycaster();
            EnsureEventSystemMouseInput();
        }

        void EnsureTrackedDeviceRaycaster()
        {
            const string typeName = "UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster, Unity.XR.Interaction.Toolkit";
            System.Type raycasterType = System.Type.GetType(typeName);
            if (raycasterType == null || keyboardRoot.GetComponent(raycasterType) != null)
                return;

            keyboardRoot.AddComponent(raycasterType);
        }

        void EnsureEventSystemMouseInput()
        {
            if (EventSystem.current == null || EventSystem.current.currentInputModule == null)
                return;

            BaseInputModule module = EventSystem.current.currentInputModule;
            System.Type type = module.GetType();
            if (type.Name != "XRUIInputModule")
                return;

            if (SetBoolMember(type, module, "enableMouseInput", true) ||
                SetBoolMember(type, module, "m_EnableMouseInput", true))
                Log("Enabled mouse input on XR UI Input Module for editor/UI testing.");
        }

        static bool SetBoolMember(System.Type type, object target, string name, bool value)
        {
            PropertyInfo property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property != null && property.PropertyType == typeof(bool) && property.CanWrite)
            {
                if ((bool)property.GetValue(target) == value)
                    return false;
                property.SetValue(target, value);
                return true;
            }

            FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null && field.FieldType == typeof(bool))
            {
                if ((bool)field.GetValue(target) == value)
                    return false;
                field.SetValue(target, value);
                return true;
            }

            return false;
        }

        void RefreshTargetLabel()
        {
            if (targetLabel == null)
                return;

            if (_tmpTarget != null)
                targetLabel.text = "TARGET " + _tmpTarget.gameObject.name.ToUpperInvariant();
            else if (_legacyTarget != null)
                targetLabel.text = "TARGET " + _legacyTarget.gameObject.name.ToUpperInvariant();
            else
                targetLabel.text = "TARGET COMMAND";
        }

        void RefreshCommandInputMirror()
        {
            if (!mirrorTargetTextInCommandField || commandInputField == null)
                return;

            if (_tmpTarget == null && _legacyTarget == null)
                return;

            if (_tmpTarget == commandInputField)
                return;

            string text = _tmpTarget != null ? _tmpTarget.text : _legacyTarget.text;
            if (commandInputField.text == text)
                return;

            commandInputField.SetTextWithoutNotify(text);
            commandInputField.ForceLabelUpdate();
            Log($"Mirrored target text to command field: '{text}'");
        }

        string CurrentTargetName()
        {
            if (_tmpTarget != null)
                return "TMP " + InputName(_tmpTarget);
            if (_legacyTarget != null)
                return "Legacy " + InputName(_legacyTarget);
            return "none";
        }

        static string InputName(Component input)
        {
            return input != null ? input.gameObject.name : "null";
        }

        void Log(string message)
        {
            if (debugLogging)
                Debug.Log($"[LcarsVirtualKeyboard] {message}", this);
        }
    }
}
