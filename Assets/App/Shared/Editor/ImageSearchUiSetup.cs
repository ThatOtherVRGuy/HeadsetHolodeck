using Holodeck.Direct;
using SpeechIntent;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Holodeck.Editor
{
    public static class ImageSearchUiSetup
    {
        static readonly Color Black = new Color(0.005f, 0.005f, 0.006f, 0.96f);
        static readonly Color Orange = new Color(1.00f, 0.55f, 0.07f, 1f);
        static readonly Color Gold = new Color(1.00f, 0.78f, 0.08f, 1f);
        static readonly Color Blue = new Color(0.48f, 0.55f, 1.00f, 1f);
        static readonly Color Lavender = new Color(0.66f, 0.52f, 0.94f, 1f);
        static readonly Color Text = new Color(0.94f, 0.90f, 0.82f, 1f);

        [MenuItem("Holodeck/Setup Pixabay Image Search UI")]
        public static void Setup()
        {
            PixabayImageSearchService pixabay = EnsurePixabayService();
            HeadsetCameraCaptureService capture = Object.FindFirstObjectByType<HeadsetCameraCaptureService>();
            ArchOperationsPanel ops = Object.FindFirstObjectByType<ArchOperationsPanel>(FindObjectsInactive.Include);
            if (ops == null)
            {
                Debug.LogWarning("[ImageSearchUiSetup] ArchOperationsPanel not found. Run Holodeck > Setup LCARS Arch UI first.");
                return;
            }

            RectTransform host = FindOperationsHost(ops.transform);
            if (host == null)
            {
                Debug.LogWarning("[ImageSearchUiSetup] OperationsHost not found under ArchOperationsPanel.");
                return;
            }

            GameObject panel = EnsureImageSearchPanel(host, pixabay, capture);
            Undo.RecordObject(ops, "Wire image search panel");
            ops.imageSearchPanel = panel;
            ops.pixabayImageSearchService = pixabay;
            EditorUtility.SetDirty(ops);

            EnsureImageButton(ops);

            WorldActionDispatcher dispatcher = Object.FindFirstObjectByType<WorldActionDispatcher>();
            if (dispatcher != null)
            {
                Undo.RecordObject(dispatcher, "Wire image search panel");
                dispatcher.imageSearchPanel = panel.GetComponent<ImageSearchPanel>();
                EditorUtility.SetDirty(dispatcher);
            }

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log("[ImageSearchUiSetup] Pixabay image search panel is ready.");
        }

        static PixabayImageSearchService EnsurePixabayService()
        {
            PixabayImageSearchService existing = Object.FindFirstObjectByType<PixabayImageSearchService>();
            if (existing != null)
                return existing;

            Transform systems =
                GameObject.Find("Holodeck/Systems")?.transform
                ?? GameObject.Find("Systems")?.transform
                ?? new GameObject("Systems").transform;

            GameObject go = new GameObject("PixabayImageSearchService");
            Undo.RegisterCreatedObjectUndo(go, "Create Pixabay image search service");
            go.transform.SetParent(systems, false);
            return Undo.AddComponent<PixabayImageSearchService>(go);
        }

        static GameObject EnsureImageSearchPanel(RectTransform host, PixabayImageSearchService pixabay, HeadsetCameraCaptureService capture)
        {
            GameObject panel = EnsureChild(host, "ImageSearchPanel");
            RectTransform rt = GetOrAdd<RectTransform>(panel);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
            GetOrAdd<Image>(panel).color = new Color(0.015f, 0.015f, 0.018f, 1f);
            ClearChildren(panel.transform);

            ImageSearchPanel imagePanel = GetOrAdd<ImageSearchPanel>(panel);
            imagePanel.pixabayService = pixabay;
            imagePanel.captureService = capture;
            imagePanel.worldCoordinator = Object.FindFirstObjectByType<VoiceToWorldLabsPluginCoordinator>();

            MakeBand(rt, "Header", new Vector2(330, 595), new Vector2(560, 44), Gold);
            MakeLabel(rt, "Title", "IMAGE SEARCH", 28, new Vector2(330, 595), new Vector2(540, 44), Black, FontStyles.Bold);

            TMP_InputField input = MakeInput(rt, "SearchInput", "Search Pixabay...", new Vector2(250, 535), new Vector2(380, 42));
            Button search = MakeButton(rt, "SearchButton", "SEARCH", new Vector2(520, 535), new Vector2(130, 42), Orange);

            RawImage preview = GetOrAdd<RawImage>(EnsureChild(panel.transform, "PreviewImage"));
            RectTransform previewRt = preview.GetComponent<RectTransform>();
            previewRt.anchorMin = previewRt.anchorMax = new Vector2(0.5f, 0.5f);
            previewRt.pivot = new Vector2(0.5f, 0.5f);
            previewRt.anchoredPosition = new Vector2(0, 25);
            previewRt.sizeDelta = new Vector2(560, 350);
            preview.color = new Color(0f, 0f, 0f, 0.65f);
            AspectRatioFitter aspect = GetOrAdd<AspectRatioFitter>(preview.gameObject);
            aspect.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            aspect.aspectRatio = 16f / 9f;

            TMP_Text status = MakeLabel(rt, "Status", "NO IMAGE SEARCH", 16, new Vector2(330, 150), new Vector2(560, 30), Text, FontStyles.Normal);
            TMP_Text attribution = MakeLabel(rt, "Attribution", "NO IMAGE SELECTED", 14, new Vector2(330, 115), new Vector2(560, 42), Text, FontStyles.Normal);
            attribution.enableWordWrapping = true;

            Button previous = MakeButton(rt, "PreviousButton", "PREV", new Vector2(90, 45), new Vector2(100, 48), Blue);
            Button next = MakeButton(rt, "NextButton", "NEXT", new Vector2(205, 45), new Vector2(100, 48), Blue);
            Button use = MakeButton(rt, "UseImageButton", "USE", new Vector2(320, 45), new Vector2(100, 48), Gold);
            Button createWorld = MakeButton(rt, "CreateWorldButton", "WORLD", new Vector2(445, 45), new Vector2(120, 48), Orange);
            Button createObject = MakeButton(rt, "CreateObjectButton", "OBJ", new Vector2(570, 45), new Vector2(100, 48), Lavender);

            search.onClick.RemoveAllListeners();
            previous.onClick.RemoveAllListeners();
            next.onClick.RemoveAllListeners();
            use.onClick.RemoveAllListeners();
            createWorld.onClick.RemoveAllListeners();
            createObject.onClick.RemoveAllListeners();
            UnityEventTools.AddPersistentListener(search.onClick, imagePanel.SearchFromInput);
            UnityEventTools.AddPersistentListener(previous.onClick, imagePanel.Previous);
            UnityEventTools.AddPersistentListener(next.onClick, imagePanel.Next);
            UnityEventTools.AddPersistentListener(use.onClick, imagePanel.UseSelectedImage);
            UnityEventTools.AddPersistentListener(createWorld.onClick, imagePanel.CreateWorld);
            UnityEventTools.AddPersistentListener(createObject.onClick, imagePanel.CreateObject);

            imagePanel.searchInput = input;
            imagePanel.previewImage = preview;
            imagePanel.previewAspect = aspect;
            imagePanel.statusLabel = status;
            imagePanel.attributionLabel = attribution;
            imagePanel.searchButton = search;
            imagePanel.previousButton = previous;
            imagePanel.nextButton = next;
            imagePanel.useImageButton = use;
            imagePanel.createWorldButton = createWorld;
            imagePanel.createObjectButton = createObject;
            EditorUtility.SetDirty(imagePanel);
            return panel;
        }

        static void EnsureImageButton(ArchOperationsPanel ops)
        {
            Button existing = FindButtonByNameOrLabel(ops.transform, "IMAGE");
            if (existing != null)
            {
                existing.onClick.RemoveAllListeners();
                UnityEventTools.AddPersistentListener(existing.onClick, ops.ShowImageSearch);
                EditorUtility.SetDirty(existing);
                return;
            }

            RectTransform parent = FindCommandButtonParent(ops.transform) ?? ops.GetComponent<RectTransform>();
            if (parent == null)
                return;

            Button button = MakeButton(parent, "IMAGE", "IMAGE", Vector2.zero, new Vector2(250, 50), Lavender);
            LayoutElement layout = GetOrAdd<LayoutElement>(button.gameObject);
            layout.preferredWidth = 250;
            layout.preferredHeight = 50;
            layout.minHeight = 42;
            PlaceAfterExistingNavButton(button.transform, parent);
            button.onClick.RemoveAllListeners();
            UnityEventTools.AddPersistentListener(button.onClick, ops.ShowImageSearch);
            EditorUtility.SetDirty(button);
        }

        static RectTransform FindOperationsHost(Transform root)
        {
            foreach (RectTransform child in root.GetComponentsInChildren<RectTransform>(true))
                if (child.name == "OperationsHost")
                    return child;
            return null;
        }

        static RectTransform FindCommandButtonParent(Transform root)
        {
            Button worldLabs = FindButtonByNameOrLabel(root, "WORLDLABS");
            if (worldLabs != null && worldLabs.transform.parent is RectTransform worldLabsParent)
                return worldLabsParent;

            Button camera = FindButtonByNameOrLabel(root, "CAMERA");
            if (camera != null && camera.transform.parent is RectTransform cameraParent)
                return cameraParent;

            foreach (VerticalLayoutGroup layout in root.GetComponentsInChildren<VerticalLayoutGroup>(true))
            {
                string name = layout.name.ToLowerInvariant();
                if (name.Contains("button") || name.Contains("command") || name.Contains("nav"))
                    return layout.GetComponent<RectTransform>();
            }

            return null;
        }

        static void PlaceAfterExistingNavButton(Transform button, RectTransform parent)
        {
            string[] anchors = { "CAMERA", "FILES/URL", "MY WORLDS", "WORLDLABS" };
            foreach (string anchor in anchors)
            {
                Button existing = FindButtonByNameOrLabel(parent, anchor);
                if (existing == null || existing.transform.parent != parent)
                    continue;

                button.SetSiblingIndex(existing.transform.GetSiblingIndex() + 1);
                return;
            }
        }

        static Button FindButtonByNameOrLabel(Transform root, string value)
        {
            foreach (Button button in root.GetComponentsInChildren<Button>(true))
            {
                if (button.name == value)
                    return button;
                TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
                if (label != null && string.Equals(label.text, value, System.StringComparison.OrdinalIgnoreCase))
                    return button;
            }
            return null;
        }

        static TMP_InputField MakeInput(RectTransform parent, string name, string placeholder, Vector2 pos, Vector2 size)
        {
            GameObject go = MakeBand(parent, name, pos, size, new Color(0.08f, 0.09f, 0.11f, 1f)).gameObject;
            TMP_InputField input = GetOrAdd<TMP_InputField>(go);

            TMP_Text text = MakeLabel(go.GetComponent<RectTransform>(), "Text", "", 18, Vector2.zero, size - new Vector2(16, 0), Text, FontStyles.Normal);
            text.alignment = TextAlignmentOptions.MidlineLeft;
            TMP_Text placeholderText = MakeLabel(go.GetComponent<RectTransform>(), "Placeholder", placeholder, 16, Vector2.zero, size - new Vector2(16, 0), new Color(0.55f, 0.58f, 0.65f, 1f), FontStyles.Italic);
            placeholderText.alignment = TextAlignmentOptions.MidlineLeft;

            input.textComponent = text;
            input.placeholder = placeholderText;
            input.targetGraphic = GetOrAdd<Image>(go);
            return input;
        }

        static RectTransform MakeBand(RectTransform parent, string name, Vector2 pos, Vector2 size, Color color)
        {
            GameObject go = EnsureChild(parent, name);
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
            GameObject go = EnsureChild(parent, name);
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
            ColorBlock colors = button.colors;
            colors.normalColor = color;
            colors.highlightedColor = Color.Lerp(color, Color.white, 0.25f);
            colors.pressedColor = Color.Lerp(color, Color.black, 0.25f);
            button.colors = colors;
            MakeLabel(go.GetComponent<RectTransform>(), "Label", label, label.Length > 8 ? 18 : 22, Vector2.zero, size, Black, FontStyles.Bold);
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
            return existing != null ? existing : go.AddComponent<T>();
        }

        static void ClearChildren(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
                Object.DestroyImmediate(parent.GetChild(i).gameObject);
        }
    }
}
