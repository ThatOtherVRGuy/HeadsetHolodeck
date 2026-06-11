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
    public static class CachedObjectCatalogUiSetup
    {
        const string ScenePath = "Assets/Scenes/Holodeck.unity";
        const string CardPrefabPath = "Assets/App/Command/SpeechIntent/Runtime/UI/Prefabs/CachedObjectCardPrefab.prefab";
        static readonly Color Black = new Color(0.005f, 0.005f, 0.006f, 0.96f);
        static readonly Color Panel = new Color(0.015f, 0.015f, 0.018f, 1f);
        static readonly Color Orange = new Color(1.00f, 0.55f, 0.07f, 1f);
        static readonly Color Gold = new Color(1.00f, 0.78f, 0.08f, 1f);
        static readonly Color Blue = new Color(0.48f, 0.55f, 1.00f, 1f);
        static readonly Color Red = new Color(0.92f, 0.12f, 0.02f, 1f);
        static readonly Color Text = new Color(0.94f, 0.90f, 0.82f, 1f);

        [MenuItem("Holodeck/Setup Cached Object Catalog UI")]
        public static void Setup()
        {
            OpenHolodeckSceneIfNeeded();

            ArchOperationsPanel ops = Object.FindFirstObjectByType<ArchOperationsPanel>(FindObjectsInactive.Include);
            if (ops == null)
            {
                Debug.LogWarning("[CachedObjectCatalogUiSetup] ArchOperationsPanel not found. Run Holodeck > Setup LCARS Arch UI first.");
                return;
            }

            RectTransform host = FindOperationsHost(ops.transform);
            if (host == null)
            {
                Debug.LogWarning("[CachedObjectCatalogUiSetup] OperationsHost not found under ArchOperationsPanel.");
                return;
            }

            CachedObjectStore store = CachedObjectStore.GetOrCreate();
            CachedObjectChoiceController choice = EnsureChoiceController();
            WorldActionDispatcher dispatcher = Object.FindFirstObjectByType<WorldActionDispatcher>(FindObjectsInactive.Include);

            GameObject catalogPanel = EnsureCatalogPanel(host, store, choice, dispatcher);
            CachedObjectChoicePanel choicePanel = EnsureChoicePanel(host);

            Undo.RecordObject(ops, "Wire cached object catalog panel");
            ops.objectCatalogPanel = catalogPanel;
            EditorUtility.SetDirty(ops);

            EnsureObjectsButton(ops);

            if (dispatcher != null)
            {
                Undo.RecordObject(dispatcher, "Wire cached object UI");
                dispatcher.cachedObjectStore = store;
                dispatcher.cachedObjectChoiceController = choice;
                dispatcher.cachedObjectChoicePanel = choicePanel;
                EditorUtility.SetDirty(dispatcher);
            }

            VoiceCommandRouter router = Object.FindFirstObjectByType<VoiceCommandRouter>(FindObjectsInactive.Include);
            if (router != null)
            {
                Undo.RecordObject(router, "Wire cached object choice controller");
                router.cachedObjectChoiceController = choice;
                EditorUtility.SetDirty(router);
            }

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            Debug.Log("[CachedObjectCatalogUiSetup] Cached object catalog UI is ready.");
        }

        [MenuItem("Holodeck/Setup Cached Object Card Prefab")]
        public static void SetupCardPrefab()
        {
            OpenHolodeckSceneIfNeeded();

            CachedObjectCardUI prefab = EnsureCardPrefabAsset();
            if (prefab == null)
            {
                Debug.LogError($"[CachedObjectCatalogUiSetup] Could not create cached object card prefab at {CardPrefabPath}.");
                return;
            }

            foreach (CachedObjectCatalogPanel panel in Object.FindObjectsByType<CachedObjectCatalogPanel>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                Undo.RecordObject(panel, "Wire cached object card prefab");
                panel.cardPrefab = prefab;
                EditorUtility.SetDirty(panel);
            }

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            Debug.Log($"[CachedObjectCatalogUiSetup] Cached object card prefab is ready at {CardPrefabPath}.");
        }

        static void OpenHolodeckSceneIfNeeded()
        {
            Scene active = SceneManager.GetActiveScene();
            if (active.IsValid() && active.path == ScenePath)
                return;

            if (!System.IO.File.Exists(ScenePath))
            {
                Debug.LogWarning($"[CachedObjectCatalogUiSetup] Scene not found at {ScenePath}.");
                return;
            }

            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        static GameObject EnsureCatalogPanel(RectTransform host, CachedObjectStore store, CachedObjectChoiceController choice, WorldActionDispatcher dispatcher)
        {
            GameObject panel = EnsureChild(host, "CachedObjectCatalogPanel");
            RectTransform rt = GetOrAdd<RectTransform>(panel);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
            GetOrAdd<Image>(panel).color = Panel;
            ClearChildren(panel.transform);

            CachedObjectCatalogPanel catalog = GetOrAdd<CachedObjectCatalogPanel>(panel);
            catalog.cachedObjectStore = store;
            catalog.cachedObjectChoiceController = choice;
            catalog.dispatcher = dispatcher;

            MakeBand(rt, "Header", new Vector2(330, 595), new Vector2(560, 44), Gold);
            MakeLabel(rt, "Title", "OBJECT CATALOG", 28, new Vector2(330, 595), new Vector2(540, 44), Black, FontStyles.Bold);

            Button refresh = MakeButton(rt, "RefreshButton", "REFRESH", new Vector2(112, 535), new Vector2(145, 42), Blue);
            TMP_Text status = MakeLabel(rt, "Status", "NO CACHED OBJECTS", 16, new Vector2(360, 535), new Vector2(390, 42), Text, FontStyles.Normal);
            status.alignment = TextAlignmentOptions.MidlineLeft;

            ScrollRect scroll = MakeScrollRect(rt, "ObjectScroll", new Vector2(330, 280), new Vector2(570, 455), out RectTransform content);
            CachedObjectCardUI cardPrefab = MakeCardPrefab(panel.transform);

            refresh.onClick.RemoveAllListeners();
            UnityEventTools.AddPersistentListener(refresh.onClick, catalog.Refresh);

            catalog.statusLabel = status;
            catalog.cardListContent = content;
            catalog.cardPrefab = cardPrefab;
            catalog.defaultUseDistanceMeters = 2f;
            catalog.cardsPerFrame = 2;

            EditorUtility.SetDirty(catalog);
            EditorUtility.SetDirty(scroll);
            panel.SetActive(false);
            return panel;
        }

        static CachedObjectChoicePanel EnsureChoicePanel(RectTransform host)
        {
            GameObject panel = EnsureChild(host, "CachedObjectChoicePanel");
            RectTransform rt = GetOrAdd<RectTransform>(panel);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0, -15);
            rt.sizeDelta = new Vector2(520, 250);
            GetOrAdd<Image>(panel).color = new Color(0.01f, 0.01f, 0.012f, 0.98f);
            ClearChildren(panel.transform);

            CachedObjectChoicePanel choicePanel = GetOrAdd<CachedObjectChoicePanel>(panel);
            TMP_Text title = MakeLabel(rt, "Title", "SAVED OBJECT", 26, new Vector2(260, 215), new Vector2(470, 36), Gold, FontStyles.Bold);
            TMP_Text detail = MakeLabel(rt, "Detail", "", 18, new Vector2(260, 145), new Vector2(470, 82), Text, FontStyles.Normal);
            detail.enableWordWrapping = true;
            Button use = MakeButton(rt, "UseSavedButton", "USE SAVED", new Vector2(108, 42), new Vector2(150, 46), Gold);
            Button createNew = MakeButton(rt, "CreateNewButton", "CREATE NEW", new Vector2(270, 42), new Vector2(160, 46), Orange);
            Button cancel = MakeButton(rt, "CancelButton", "CANCEL", new Vector2(425, 42), new Vector2(120, 46), Red);

            choicePanel.root = panel;
            choicePanel.titleLabel = title as TextMeshProUGUI;
            choicePanel.detailLabel = detail as TextMeshProUGUI;
            choicePanel.useSavedButton = use;
            choicePanel.createNewButton = createNew;
            choicePanel.cancelButton = cancel;
            panel.SetActive(false);
            EditorUtility.SetDirty(choicePanel);
            return choicePanel;
        }

        static CachedObjectCardUI MakeCardPrefab(Transform parent)
        {
            return EnsureCardPrefabAsset();
        }

        static CachedObjectCardUI EnsureCardPrefabAsset()
        {
            CachedObjectCardUI existingPrefab = AssetDatabase.LoadAssetAtPath<CachedObjectCardUI>(CardPrefabPath);
            if (existingPrefab != null)
                return existingPrefab;

            GameObject card = new GameObject("CachedObjectCardPrefab", typeof(RectTransform), typeof(Image));
            BuildCard(card);
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(CardPrefabPath));
            GameObject prefabAsset = PrefabUtility.SaveAsPrefabAsset(card, CardPrefabPath);
            Object.DestroyImmediate(card);
            AssetDatabase.SaveAssets();
            return prefabAsset != null
                ? prefabAsset.GetComponent<CachedObjectCardUI>()
                : AssetDatabase.LoadAssetAtPath<CachedObjectCardUI>(CardPrefabPath);
        }

        static void BuildCard(GameObject card)
        {
            RectTransform rt = GetOrAdd<RectTransform>(card);
            rt.sizeDelta = new Vector2(530, 104);
            GetOrAdd<Image>(card).color = new Color(0.04f, 0.045f, 0.06f, 1f);
            ClearChildren(card.transform);

            CachedObjectCardUI ui = GetOrAdd<CachedObjectCardUI>(card);
            RawImage thumb = GetOrAdd<RawImage>(EnsureChild(card.transform, "Thumbnail"));
            RectTransform thumbRt = thumb.GetComponent<RectTransform>();
            thumbRt.anchorMin = thumbRt.anchorMax = new Vector2(0, 0.5f);
            thumbRt.pivot = new Vector2(0, 0.5f);
            thumbRt.anchoredPosition = new Vector2(10, 0);
            thumbRt.sizeDelta = new Vector2(86, 86);

            ui.nameLabel = MakeLabel(rt, "Name", "Cached Object", 20, new Vector2(285, 78), new Vector2(330, 24), Text, FontStyles.Bold);
            ui.providerLabel = MakeLabel(rt, "Provider", "Provider", 14, new Vector2(285, 53), new Vector2(330, 20), Text, FontStyles.Normal);
            ui.dateLabel = MakeLabel(rt, "Date", "", 12, new Vector2(285, 31), new Vector2(330, 18), Text, FontStyles.Normal);
            ui.nameLabel.alignment = TextAlignmentOptions.MidlineLeft;
            ui.providerLabel.alignment = TextAlignmentOptions.MidlineLeft;
            ui.dateLabel.alignment = TextAlignmentOptions.MidlineLeft;
            ui.thumbnail = thumb;
            ui.useButton = MakeButton(rt, "UseButton", "USE", new Vector2(426, 74), new Vector2(82, 30), Blue);
            ui.deleteButton = MakeButton(rt, "DeleteButton", "DEL", new Vector2(426, 36), new Vector2(82, 30), Red);
            ui.renameButton = null;
            card.SetActive(false);
        }

        static ScrollRect MakeScrollRect(RectTransform parent, string name, Vector2 pos, Vector2 size, out RectTransform content)
        {
            GameObject viewport = MakeBand(parent, name, pos, size, new Color(0.02f, 0.022f, 0.027f, 1f)).gameObject;
            RectTransform viewportRt = viewport.GetComponent<RectTransform>();
            Mask mask = GetOrAdd<Mask>(viewport);
            mask.showMaskGraphic = true;

            GameObject contentGo = EnsureChild(viewport.transform, "Content");
            content = GetOrAdd<RectTransform>(contentGo);
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(0, 455);
            VerticalLayoutGroup layout = GetOrAdd<VerticalLayoutGroup>(contentGo);
            layout.spacing = 8;
            layout.padding = new RectOffset(10, 10, 10, 10);
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            ContentSizeFitter fitter = GetOrAdd<ContentSizeFitter>(contentGo);
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            ScrollRect scroll = GetOrAdd<ScrollRect>(viewport);
            scroll.viewport = viewportRt;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            return scroll;
        }

        static CachedObjectChoiceController EnsureChoiceController()
        {
            CachedObjectChoiceController existing = Object.FindFirstObjectByType<CachedObjectChoiceController>(FindObjectsInactive.Include);
            if (existing != null)
                return existing;

            Transform systems =
                GameObject.Find("Holodeck/Systems")?.transform
                ?? GameObject.Find("Systems")?.transform
                ?? new GameObject("Systems").transform;

            GameObject go = new GameObject("CachedObjectChoiceController");
            Undo.RegisterCreatedObjectUndo(go, "Create cached object choice controller");
            go.transform.SetParent(systems, false);
            return Undo.AddComponent<CachedObjectChoiceController>(go);
        }

        static void EnsureObjectsButton(ArchOperationsPanel ops)
        {
            Button existing = FindButtonByNameOrLabel(ops.transform, "OBJECTS");
            if (existing != null)
            {
                existing.onClick.RemoveAllListeners();
                UnityEventTools.AddPersistentListener(existing.onClick, ops.ShowObjectCatalog);
                EditorUtility.SetDirty(existing);
                return;
            }

            RectTransform parent = FindCommandButtonParent(ops.transform) ?? ops.GetComponent<RectTransform>();
            if (parent == null)
                return;

            Button button = MakeButton(parent, "OBJECTS", "OBJECTS", Vector2.zero, new Vector2(250, 50), Blue);
            LayoutElement layout = GetOrAdd<LayoutElement>(button.gameObject);
            layout.preferredWidth = 250;
            layout.preferredHeight = 50;
            layout.minHeight = 42;
            PlaceAfterExistingNavButton(button.transform, parent);
            button.onClick.RemoveAllListeners();
            UnityEventTools.AddPersistentListener(button.onClick, ops.ShowObjectCatalog);
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
            Button image = FindButtonByNameOrLabel(root, "IMAGE");
            if (image != null && image.transform.parent is RectTransform imageParent)
                return imageParent;

            Button camera = FindButtonByNameOrLabel(root, "CAMERA");
            if (camera != null && camera.transform.parent is RectTransform cameraParent)
                return cameraParent;

            foreach (VerticalLayoutGroup layout in root.GetComponentsInChildren<VerticalLayoutGroup>(true))
            {
                string lower = layout.name.ToLowerInvariant();
                if (lower.Contains("button") || lower.Contains("command") || lower.Contains("nav"))
                    return layout.GetComponent<RectTransform>();
            }

            return null;
        }

        static void PlaceAfterExistingNavButton(Transform button, RectTransform parent)
        {
            string[] anchors = { "IMAGE", "CAMERA", "FILES/URL", "MY WORLDS", "WORLDLABS" };
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
            MakeLabel(go.GetComponent<RectTransform>(), "Label", label, label.Length > 8 ? 16 : 20, Vector2.zero, size, Black, FontStyles.Bold);
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
