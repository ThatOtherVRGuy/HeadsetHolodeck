using SpeechIntent;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Holodeck.Editor
{
    public static class LcarsVirtualKeyboardSetup
    {
        static readonly Color Black = new Color(0.005f, 0.005f, 0.006f, 0.96f);
        static readonly Color Panel = new Color(0.015f, 0.015f, 0.018f, 0.98f);
        static readonly Color Orange = new Color(1.00f, 0.48f, 0.04f, 1f);
        static readonly Color Gold = new Color(1.00f, 0.76f, 0.07f, 1f);
        static readonly Color Blue = new Color(0.25f, 0.32f, 1.00f, 1f);
        static readonly Color Violet = new Color(0.50f, 0.27f, 0.88f, 1f);
        static readonly Color Text = new Color(0.94f, 0.90f, 0.82f, 1f);

        [MenuItem("Holodeck/Setup LCARS Virtual Keyboard")]
        public static void Setup()
        {
            Transform parent = FindKeyboardParent();
            if (parent == null)
            {
                Debug.LogWarning("[LcarsVirtualKeyboardSetup] ArchLCARS or a Canvas not found.");
                return;
            }

            GameObject keyboardObject = EnsureChild(parent, "LcarsVirtualKeyboard", out bool created);
            RectTransform root = GetOrAdd<RectTransform>(keyboardObject);
            if (created)
            {
                root.anchorMin = root.anchorMax = new Vector2(0.5f, 0f);
                root.pivot = new Vector2(0.5f, 0f);
                root.anchoredPosition = new Vector2(0f, 20f);
                root.sizeDelta = new Vector2(980f, 430f);
            }

            Image background = GetOrAdd<Image>(keyboardObject);
            background.color = Panel;
            EnsureRenderableCanvas(keyboardObject);
            CanvasGroup group = GetOrAdd<CanvasGroup>(keyboardObject);
            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;
            EnableMouseInputForEditorTesting();

            ClearChildren(keyboardObject.transform);

            LcarsVirtualKeyboard keyboard = GetOrAdd<LcarsVirtualKeyboard>(keyboardObject);
            keyboard.keyboardRoot = keyboardObject;
            keyboard.voiceCommandRouter = Object.FindFirstObjectByType<VoiceCommandRouter>(FindObjectsInactive.Include);
            keyboard.mirrorTargetTextInCommandField = true;
            keyboard.fadeDuration = 0.5f;
            keyboard.debugLogging = true;

            MakeBand(root, "Header", new Vector2(490, 405), new Vector2(900, 38), Gold);
            MakeLabel(root, "Title", "TEXT ENTRY", 24, new Vector2(490, 405), new Vector2(880, 38), Black, FontStyles.Bold);
            keyboard.targetLabel = MakeLabel(root, "TargetLabel", "TARGET COMMAND", 14, new Vector2(160, 365), new Vector2(260, 28), Text, FontStyles.Normal);
            TMP_InputField commandInput = MakeInput(root, "TypedCommandInput", "Type command or select a field...", new Vector2(490, 365), new Vector2(580, 36));
            keyboard.commandInputField = commandInput;
            Button send = MakeButton(root, "SendCommandButton", "SEND", new Vector2(815, 365), new Vector2(120, 36), Orange);
            UnityEventTools.AddPersistentListener(send.onClick, keyboard.SubmitCommand);

            RectTransform rows = MakeRows(root);
            MakeKeyRow(rows, keyboard, "DigitsRow", new[] { "1", "2", "3", "4", "5", "6", "7", "8", "9", "0", "-", ".", "_", "~" });
            MakeKeyRow(rows, keyboard, "LettersTopRow", Concat(Split("qwertyuiop"), new[] { ":", "/", "?", "#" }));
            MakeKeyRow(rows, keyboard, "LettersHomeRow", Concat(Split("asdfghjkl"), new[] { "[", "]", "@" }));
            MakeKeyRow(rows, keyboard, "LettersBottomRow", Concat(Split("zxcvbnm"), new[] { "!", "$", "&", "'", "(", ")", "*" }));
            MakeKeyRow(rows, keyboard, "UrlSymbolsRow", new[] { "+", ",", ";", "=", "%", ".com" });

            RectTransform controls = MakeControlRow(root);
            Button shift = MakeButton(controls, "ShiftButton", "SHIFT", Vector2.zero, new Vector2(110, 44), Violet);
            Button space = MakeButton(controls, "SpaceButton", "SPACE", Vector2.zero, new Vector2(310, 44), Gold);
            Button backspace = MakeButton(controls, "BackspaceButton", "BACK", Vector2.zero, new Vector2(110, 44), Blue);
            Button clear = MakeButton(controls, "ClearButton", "CLEAR", Vector2.zero, new Vector2(110, 44), Blue);
            Button enter = MakeButton(controls, "EnterButton", "ENTER", Vector2.zero, new Vector2(110, 44), Orange);
            Button hide = MakeButton(controls, "HideButton", "HIDE", Vector2.zero, new Vector2(110, 44), Violet);
            UnityEventTools.AddPersistentListener(shift.onClick, keyboard.ToggleShift);
            UnityEventTools.AddPersistentListener(space.onClick, keyboard.Space);
            UnityEventTools.AddPersistentListener(backspace.onClick, keyboard.Backspace);
            UnityEventTools.AddPersistentListener(clear.onClick, keyboard.Clear);
            UnityEventTools.AddPersistentListener(enter.onClick, keyboard.SubmitTarget);
            UnityEventTools.AddPersistentListener(hide.onClick, keyboard.Hide);

            EditorUtility.SetDirty(keyboard);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log("[LcarsVirtualKeyboardSetup] LCARS virtual keyboard is ready.");
        }

        static Transform FindKeyboardParent()
        {
            GameObject arch = GameObject.Find("ArchLCARS");
            if (arch != null)
                return arch.transform;

            Canvas canvas = Object.FindFirstObjectByType<Canvas>(FindObjectsInactive.Include);
            return canvas != null ? canvas.transform : null;
        }

        static void EnsureRenderableCanvas(GameObject keyboardObject)
        {
            Canvas canvas = GetOrAdd<Canvas>(keyboardObject);
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 100;
            if (canvas.worldCamera == null)
                canvas.worldCamera = Camera.main != null
                    ? Camera.main
                    : Object.FindFirstObjectByType<Camera>(FindObjectsInactive.Include);

            CanvasScaler scaler = GetOrAdd<CanvasScaler>(keyboardObject);
            scaler.dynamicPixelsPerUnit = 10f;
            scaler.referencePixelsPerUnit = 100f;

            GetOrAdd<GraphicRaycaster>(keyboardObject);
            GetOrAdd<UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster>(keyboardObject);
        }

        static void EnableMouseInputForEditorTesting()
        {
            EventSystem eventSystem = Object.FindFirstObjectByType<EventSystem>(FindObjectsInactive.Include);
            if (eventSystem == null)
                return;

            foreach (MonoBehaviour component in eventSystem.GetComponents<MonoBehaviour>())
            {
                if (component == null || component.GetType().Name != "XRUIInputModule")
                    continue;

                SerializedObject serialized = new SerializedObject(component);
                SerializedProperty mouseInput = serialized.FindProperty("m_EnableMouseInput");
                if (mouseInput == null)
                    return;

                mouseInput.boolValue = true;
                serialized.ApplyModifiedProperties();
                EditorUtility.SetDirty(component);
                return;
            }
        }

        static RectTransform MakeRows(RectTransform root)
        {
            GameObject go = EnsureChild(root, "KeyRows", out _);
            RectTransform rt = GetOrAdd<RectTransform>(go);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0, -35);
            rt.sizeDelta = new Vector2(900, 250);
            VerticalLayoutGroup layout = GetOrAdd<VerticalLayoutGroup>(go);
            layout.spacing = 6;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            return rt;
        }

        static RectTransform MakeControlRow(RectTransform root)
        {
            GameObject go = EnsureChild(root, "ControlRow", out _);
            RectTransform rt = GetOrAdd<RectTransform>(go);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0, 18);
            rt.sizeDelta = new Vector2(900, 52);
            HorizontalLayoutGroup layout = GetOrAdd<HorizontalLayoutGroup>(go);
            layout.spacing = 8;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            return rt;
        }

        static void MakeKeyRow(RectTransform parent, LcarsVirtualKeyboard keyboard, string name, string[] values)
        {
            GameObject row = EnsureChild(parent, name, out _);
            RectTransform rt = GetOrAdd<RectTransform>(row);
            rt.sizeDelta = new Vector2(900, 42);
            HorizontalLayoutGroup layout = GetOrAdd<HorizontalLayoutGroup>(row);
            layout.spacing = 5;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            foreach (string value in values)
                MakeKey(rt, keyboard, value);
        }

        static void MakeKey(RectTransform parent, LcarsVirtualKeyboard keyboard, string value)
        {
            float width = value == ".com" ? 78f : 48f;
            Button button = MakeButton(parent, "Key_" + SafeName(value), value, Vector2.zero, new Vector2(width, 38), KeyColor(value));
            LcarsVirtualKeyboardKey key = GetOrAdd<LcarsVirtualKeyboardKey>(button.gameObject);
            key.keyboard = keyboard;
            key.value = value;
            key.isLetter = value.Length == 1 && char.IsLetter(value[0]);
            key.label = button.GetComponentInChildren<TMP_Text>(true);
            if (key.isLetter)
                keyboard.RegisterLetterLabel(key.label);
            button.onClick.RemoveAllListeners();
            UnityEventTools.AddPersistentListener(button.onClick, key.Press);
        }

        static string[] Split(string value)
        {
            string[] result = new string[value.Length];
            for (int i = 0; i < value.Length; i++)
                result[i] = value[i].ToString();
            return result;
        }

        static string[] Concat(string[] first, string[] second)
        {
            string[] result = new string[first.Length + second.Length];
            first.CopyTo(result, 0);
            second.CopyTo(result, first.Length);
            return result;
        }

        static Color KeyColor(string value)
        {
            if (value == "/" || value == ":" || value == "?" || value == "#" || value == ".com")
                return Orange;
            if (value.Length == 1 && char.IsDigit(value[0]))
                return Gold;
            if (value.Length == 1 && char.IsLetter(value[0]))
                return Blue;
            return Violet;
        }

        static TMP_InputField MakeInput(RectTransform parent, string name, string placeholder, Vector2 pos, Vector2 size)
        {
            GameObject go = MakeBand(parent, name, pos, size, new Color(0.07f, 0.08f, 0.11f, 1f)).gameObject;
            TMP_InputField input = GetOrAdd<TMP_InputField>(go);
            TMP_Text text = MakeLabel(go.GetComponent<RectTransform>(), "Text", "", 16, Vector2.zero, size - new Vector2(16, 0), Text, FontStyles.Normal);
            text.alignment = TextAlignmentOptions.MidlineLeft;
            TMP_Text placeholderText = MakeLabel(go.GetComponent<RectTransform>(), "Placeholder", placeholder, 15, Vector2.zero, size - new Vector2(16, 0), new Color(0.55f, 0.58f, 0.65f, 1f), FontStyles.Italic);
            placeholderText.alignment = TextAlignmentOptions.MidlineLeft;
            input.textComponent = text;
            input.placeholder = placeholderText;
            input.targetGraphic = GetOrAdd<Image>(go);
            return input;
        }

        static RectTransform MakeBand(RectTransform parent, string name, Vector2 pos, Vector2 size, Color color)
        {
            GameObject go = EnsureChild(parent, name, out _);
            RectTransform rt = GetOrAdd<RectTransform>(go);
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos == Vector2.zero
                ? Vector2.zero
                : new Vector2(pos.x - parent.sizeDelta.x * 0.5f, pos.y - parent.sizeDelta.y * 0.5f);
            rt.sizeDelta = size;
            GetOrAdd<Image>(go).color = color;
            return rt;
        }

        static TMP_Text MakeLabel(RectTransform parent, string name, string text, float size, Vector2 pos, Vector2 box, Color color, FontStyles style)
        {
            GameObject go = EnsureChild(parent, name, out _);
            RectTransform rt = GetOrAdd<RectTransform>(go);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos == Vector2.zero
                ? Vector2.zero
                : new Vector2(pos.x - parent.sizeDelta.x * 0.5f, pos.y - parent.sizeDelta.y * 0.5f);
            rt.sizeDelta = box;
            TMP_Text tmp = GetOrAdd<TextMeshProUGUI>(go);
            tmp.text = text;
            tmp.fontSize = size;
            tmp.fontStyle = style;
            tmp.color = color;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.raycastTarget = false;
            tmp.enableWordWrapping = false;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            return tmp;
        }

        static Button MakeButton(RectTransform parent, string name, string label, Vector2 pos, Vector2 size, Color color)
        {
            GameObject go = MakeBand(parent, name, pos, size, color).gameObject;
            Button button = GetOrAdd<Button>(go);
            Image image = GetOrAdd<Image>(go);
            button.targetGraphic = image;
            TMP_Text text = MakeLabel(go.GetComponent<RectTransform>(), "Label", label, label.Length > 5 ? 15 : 18, Vector2.zero, size, Black, FontStyles.Bold);
            text.raycastTarget = false;
            ColorBlock colors = button.colors;
            colors.normalColor = color;
            colors.highlightedColor = Color.Lerp(color, Color.white, 0.22f);
            colors.pressedColor = Color.Lerp(color, Color.black, 0.30f);
            colors.selectedColor = Color.Lerp(color, Color.white, 0.12f);
            colors.colorMultiplier = 1f;
            button.colors = colors;
            LayoutElement layout = GetOrAdd<LayoutElement>(go);
            layout.preferredWidth = size.x;
            layout.preferredHeight = size.y;
            return button;
        }

        static GameObject EnsureChild(Transform parent, string name, out bool created)
        {
            Transform existing = parent.Find(name);
            if (existing != null)
            {
                created = false;
                return existing.gameObject;
            }

            GameObject child = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(child, "Create " + name);
            child.transform.SetParent(parent, false);
            created = true;
            return child;
        }

        static T GetOrAdd<T>(GameObject go) where T : Component
        {
            T existing = go.GetComponent<T>();
            return existing != null ? existing : Undo.AddComponent<T>(go);
        }

        static void ClearChildren(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
                Object.DestroyImmediate(parent.GetChild(i).gameObject);
        }

        static string SafeName(string value)
        {
            return value.Replace("/", "Slash")
                .Replace(":", "Colon")
                .Replace("?", "Question")
                .Replace("#", "Hash")
                .Replace("[", "LeftBracket")
                .Replace("]", "RightBracket")
                .Replace("@", "At")
                .Replace("!", "Bang")
                .Replace("$", "Dollar")
                .Replace("&", "Amp")
                .Replace("'", "Apostrophe")
                .Replace("(", "LeftParen")
                .Replace(")", "RightParen")
                .Replace("*", "Star")
                .Replace("+", "Plus")
                .Replace(",", "Comma")
                .Replace(";", "Semicolon")
                .Replace("=", "Equals")
                .Replace("%", "Percent")
                .Replace(".", "Dot")
                .Replace("~", "Tilde");
        }
    }
}
