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
            if (commandButtons == null)
            {
                Debug.LogWarning("[WorldViewCaptureUiSetup] CommandButtons not found. Add UI buttons manually and call WorldViewCaptureService.CaptureThumbnail/CapturePanorama.");
                return null;
            }

            GameObject root = EnsureChild(commandButtons, "WorldViewCaptureControls");
            RectTransform rootRt = GetOrAdd<RectTransform>(root);
            rootRt.sizeDelta = new Vector2(248, 104);

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

            Button thumbnail = MakeButton(root.transform, "CaptureThumbnailButton", "THUMBNAIL", LcarsPalette.Default.gold);
            Button panorama = MakeButton(root.transform, "CapturePanoramaButton", "PANORAMA", LcarsPalette.Default.lightBlue);

            thumbnail.onClick.RemoveAllListeners();
            panorama.onClick.RemoveAllListeners();
            if (controls != null)
            {
                UnityEventTools.AddPersistentListener(thumbnail.onClick, controls.CaptureThumbnail);
                UnityEventTools.AddPersistentListener(panorama.onClick, controls.CapturePanorama);
            }

            EditorUtility.SetDirty(thumbnail);
            EditorUtility.SetDirty(panorama);
            return controls;
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
            RectTransform rt = GetOrAdd<RectTransform>(go);
            rt.sizeDelta = new Vector2(248, 48);

            Image image = GetOrAdd<Image>(go);
            image.color = color;

            Button button = GetOrAdd<Button>(go);
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
            return button;
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
