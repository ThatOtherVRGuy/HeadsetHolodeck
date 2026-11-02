using Holodeck.Save;
using SpeechIntent;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Holodeck.Editor
{
    public static class WorldViewCaptureUiSetup
    {
        [MenuItem("Holodeck/Setup World View Capture UI")]
        public static void Setup()
        {
            EnsureSetupSceneLoaded();

            WorldViewCaptureService service = EnsureCaptureService();
            EnsureCaptureControls(service);
            WireDispatcher(service);

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            Debug.Log("[WorldViewCaptureUiSetup] World thumbnail/panorama capture service and buttons are ready.");
        }

        static void EnsureSetupSceneLoaded()
        {
            if (GameObject.Find("CommandButtons") != null ||
                Object.FindFirstObjectByType<ArchOperationsPanel>(FindObjectsInactive.Include) != null)
            {
                return;
            }

            const string scenePath = "Assets/Scenes/Holodeck.unity";
            if (!System.IO.File.Exists(scenePath))
                return;

            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        }

        static WorldViewCaptureService EnsureCaptureService()
        {
            WorldViewCaptureService existing = Object.FindFirstObjectByType<WorldViewCaptureService>(FindObjectsInactive.Include);
            if (existing != null)
            {
                WireCaptureService(existing);
                return existing;
            }

            Transform systems =
                GameObject.Find("Holodeck/Systems")?.transform
                ?? GameObject.Find("Systems")?.transform
                ?? new GameObject("Systems").transform;

            GameObject go = new GameObject("WorldViewCaptureService");
            Undo.RegisterCreatedObjectUndo(go, "Create world view capture service");
            go.transform.SetParent(systems, false);

            WorldViewCaptureService service = Undo.AddComponent<WorldViewCaptureService>(go);
            WireCaptureService(service);
            return service;
        }

        static void WireCaptureService(WorldViewCaptureService service)
        {
            Undo.RecordObject(service, "Wire world view capture service");
            service.worldConfigStore = Object.FindFirstObjectByType<WorldConfigStore>(FindObjectsInactive.Include);
            service.worldConfigAutoSave = Object.FindFirstObjectByType<WorldConfigAutoSave>(FindObjectsInactive.Include);
            service.captureCamera = Camera.main;
            EditorUtility.SetDirty(service);
        }

        static WorldViewCaptureControls EnsureCaptureControls(WorldViewCaptureService service)
        {
            Transform commandButtons = FindCommandButtons();
            ArchOperationsPanel ops = Object.FindFirstObjectByType<ArchOperationsPanel>(FindObjectsInactive.Include);
            RectTransform host = FindOperationsHost(ops);
            if (commandButtons == null || ops == null || host == null)
            {
                Debug.LogWarning("[WorldViewCaptureUiSetup] CommandButtons or OperationsHost not found. Run Holodeck/Setup LCARS Arch UI first.");
                return null;
            }

            GameObject panel = EnsureChild(host, "WorldViewCapturePanel");
            ConfigureCapturePanel(panel);

            // Migrate the authored controls as a single unit. Persistent thumbnail and
            // panorama listeners therefore retain their targets through this refactor.
            Transform legacyControls = commandButtons.Find("WorldViewCaptureControls");
            GameObject root = legacyControls != null
                ? legacyControls.gameObject
                : EnsureChild(panel.transform, "WorldViewCaptureControls");
            Transform legacyOptions = root.transform.Find("OptionsButton");
            if (legacyOptions != null && legacyOptions.parent != commandButtons)
            {
                Transform existingOptions = commandButtons.Find("OptionsButton");
                if (existingOptions != null && existingOptions != legacyOptions)
                    Undo.DestroyObjectImmediate(legacyOptions.gameObject);
                else
                    legacyOptions.SetParent(commandButtons, false);
            }
            if (root.transform.parent != panel.transform)
                root.transform.SetParent(panel.transform, false);

            RectTransform rootRt = GetOrAdd<RectTransform>(root);
            rootRt.sizeDelta = new Vector2(248, 160);
            rootRt.anchorMin = new Vector2(0.5f, 0.5f);
            rootRt.anchorMax = new Vector2(0.5f, 0.5f);
            rootRt.anchoredPosition = new Vector2(0f, 70f);

            VerticalLayoutGroup layout = GetOrAdd<VerticalLayoutGroup>(root);
            layout.spacing = 8;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            WorldViewCaptureControls controls = GetOrAdd<WorldViewCaptureControls>(root);
            Undo.RecordObject(controls, "Wire world view capture controls");
            controls.captureService = service;
            EditorUtility.SetDirty(controls);

            Button thumbnail = FindButton(root.transform, "CaptureThumbnailButton", "THUMBNAIL")
                ?? MakeButton(root.transform, "CaptureThumbnailButton", "THUMBNAIL", LcarsPalette.Default.gold);
            Button panorama = FindButton(root.transform, "CapturePanoramaButton", "PANORAMA")
                ?? MakeButton(root.transform, "CapturePanoramaButton", "PANORAMA", LcarsPalette.Default.lightBlue);
            ConfigureButton(thumbnail, "THUMBNAIL", LcarsPalette.Default.gold);
            ConfigureButton(panorama, "PANORAMA", LcarsPalette.Default.lightBlue);
            RemoveDuplicateButton(root.transform, "THUMBNAIL", thumbnail);
            RemoveDuplicateButton(root.transform, "PANORAMA", panorama);

            Button capture = MakeButton(commandButtons, "CaptureWorldViewButton", "CAPTURE", LcarsPalette.Default.gold);
            Button options = MakeButton(commandButtons, "OptionsButton", "OPTIONS", LcarsPalette.Default.orange);

            ResetListeners(thumbnail);
            ResetListeners(panorama);
            ResetListeners(capture);
            ResetListeners(options);
            if (controls != null)
            {
                UnityEventTools.AddPersistentListener(thumbnail.onClick, controls.CaptureThumbnail);
                UnityEventTools.AddPersistentListener(panorama.onClick, controls.CapturePanorama);
            }

            Undo.RecordObject(ops, "Wire world view capture panel");
            ops.worldViewCapturePanel = panel;
            UnityEventTools.AddPersistentListener(capture.onClick, ops.ShowWorldViewCapture);
            UnityEventTools.AddPersistentListener(options.onClick, ops.ShowOptions);
            panel.SetActive(ops.defaultPanel == ArchOperationsPanel.DefaultPanel.Capture);
            EditorUtility.SetDirty(ops);

            EditorUtility.SetDirty(thumbnail);
            EditorUtility.SetDirty(panorama);
            EditorUtility.SetDirty(capture);
            EditorUtility.SetDirty(options);
            EditorUtility.SetDirty(panel);
            return controls;
        }

        static void ConfigureCapturePanel(GameObject panel)
        {
            RectTransform rt = GetOrAdd<RectTransform>(panel);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            Image background = GetOrAdd<Image>(panel);
            background.color = new Color(0.025f, 0.035f, 0.065f, 0.97f);

            TMP_Text title = GetOrAdd<TextMeshProUGUI>(EnsureChild(panel.transform, "Title"));
            RectTransform titleRt = title.GetComponent<RectTransform>();
            titleRt.anchorMin = new Vector2(0.5f, 0.5f);
            titleRt.anchorMax = new Vector2(0.5f, 0.5f);
            titleRt.sizeDelta = new Vector2(480f, 44f);
            titleRt.anchoredPosition = new Vector2(0f, 210f);
            title.text = "CAPTURE WORLD VIEW";
            title.fontSize = 26;
            title.fontStyle = FontStyles.Bold;
            title.color = LcarsPalette.Default.buttonText;
            title.alignment = TextAlignmentOptions.Center;
            title.raycastTarget = false;
        }

        static RectTransform FindOperationsHost(ArchOperationsPanel ops)
        {
            if (ops == null)
                return null;

            Transform direct = ops.transform.Find("OperationsHost");
            if (direct is RectTransform directRect)
                return directRect;

            foreach (RectTransform candidate in ops.GetComponentsInChildren<RectTransform>(true))
                if (candidate.name == "OperationsHost")
                    return candidate;
            return null;
        }

        static Button FindButton(Transform parent, params string[] names)
        {
            foreach (string name in names)
            {
                Transform child = parent.Find(name);
                if (child != null && child.TryGetComponent(out Button button))
                    return button;
            }
            return null;
        }

        static void RemoveDuplicateButton(Transform parent, string legacyName, Button retained)
        {
            Transform legacy = parent.Find(legacyName);
            if (legacy != null && legacy.gameObject != retained.gameObject)
                Undo.DestroyObjectImmediate(legacy.gameObject);
        }

        static void ResetListeners(Button button)
        {
            button.onClick.RemoveAllListeners();
            for (int i = button.onClick.GetPersistentEventCount() - 1; i >= 0; i--)
                UnityEventTools.RemovePersistentListener(button.onClick, i);
        }

        static void WireDispatcher(WorldViewCaptureService service)
        {
            WorldActionDispatcher dispatcher = Object.FindFirstObjectByType<WorldActionDispatcher>(FindObjectsInactive.Include);
            if (dispatcher == null)
                return;

            Undo.RecordObject(dispatcher, "Wire world view capture service");
            dispatcher.worldViewCaptureService = service;
            EditorUtility.SetDirty(dispatcher);
        }

        static Transform FindCommandButtons()
        {
            GameObject root = GameObject.Find("CommandButtons");
            if (root != null)
                return root.transform;

            ArchOperationsPanel ops = Object.FindFirstObjectByType<ArchOperationsPanel>(FindObjectsInactive.Include);
            if (ops == null)
                return null;

            foreach (Transform child in ops.GetComponentsInChildren<Transform>(true))
                if (child.name == "CommandButtons")
                    return child;

            return null;
        }

        static Button MakeButton(Transform parent, string name, string label, Color color)
        {
            GameObject go = EnsureChild(parent, name);
            Button button = GetOrAdd<Button>(go);
            ConfigureButton(button, label, color);
            return button;
        }

        static void ConfigureButton(Button button, string label, Color color)
        {
            GameObject go = button.gameObject;
            RectTransform rt = GetOrAdd<RectTransform>(go);
            rt.sizeDelta = new Vector2(248, 48);

            Image image = GetOrAdd<Image>(go);
            image.color = color;

            ColorBlock colors = button.colors;
            colors.normalColor = color;
            colors.highlightedColor = Color.Lerp(color, Color.white, 0.22f);
            colors.pressedColor = Color.Lerp(color, Color.black, 0.30f);
            colors.selectedColor = Color.Lerp(color, Color.white, 0.12f);
            colors.colorMultiplier = 1f;
            button.colors = colors;

            TMP_Text text = GetOrAdd<TextMeshProUGUI>(EnsureChild(go.transform, "Label"));
            RectTransform textRt = text.GetComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = Vector2.zero;
            textRt.offsetMax = Vector2.zero;
            text.text = label;
            text.fontSize = 20;
            text.fontStyle = FontStyles.Bold;
            text.color = LcarsPalette.Default.buttonText;
            text.alignment = TextAlignmentOptions.Center;
            text.enableWordWrapping = false;
            text.raycastTarget = false;
        }

        static GameObject EnsureChild(Transform parent, string name)
        {
            Transform existing = parent.Find(name);
            if (existing != null)
                return existing.gameObject;

            GameObject child = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(child, "Create " + name);
            child.transform.SetParent(parent, false);
            return child;
        }

        static T GetOrAdd<T>(GameObject go) where T : Component
        {
            T existing = go.GetComponent<T>();
            return existing != null ? existing : Undo.AddComponent<T>(go);
        }
    }
}
