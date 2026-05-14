// Assets/App/Editor/WorldConfigUiSetup.cs
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using SpeechIntent;

namespace Holodeck.Editor
{
    /// <summary>
    /// Creates the WorldConfigCardUI prefab and the My Worlds scene panel,
    /// then wires them into UiPanelController and WorldConfigSceneSetup dependencies.
    ///
    /// Run this AFTER Holodeck > Setup World Config.
    ///
    /// Menu: Holodeck > Setup World Config UI
    /// </summary>
    public static class WorldConfigUiSetup
    {
        const string CardPrefabPath = "Assets/App/Prefabs/WorldConfigCard.prefab";

        // Panel pixel dimensions (world-space canvas, scale 0.005 → 2.1 × 2.75 m)
        const float PanelW    = 420f;
        const float PanelH    = 550f;
        const float HeaderH   = 46f;
        const float SaveAsH   = 40f;
        const float Scale     = 0.005f;

        [MenuItem("Holodeck/Setup World Config UI")]
        public static void SetupWorldConfigUI()
        {
            // 1. Create / refresh the WorldConfigCardUI prefab
            WorldConfigCardUI cardPrefab = EnsureCardPrefab();

            // 2. Create / refresh the My Worlds panel in the scene
            MyWorldsPanel panel = EnsureMyWorldsPanel();

            if (panel != null)
            {
                // Wire card prefab onto panel
                Undo.RecordObject(panel, "Wire MyWorldsPanel.cardPrefab");
                panel.cardPrefab = cardPrefab;

                // 3. Register with UiPanelController
                WireMyWorldsPanel(panel);

                // 4. Refresh WorldConfigSceneSetup references if already run
                RefreshSaveSystemReferences(panel);
            }

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log("[WorldConfigUiSetup] Done.\n" +
                      "The WorldConfigCardUI prefab is at: " + CardPrefabPath + "\n" +
                      "My Worlds panel was added to the scene under UI/MyWorldsPanel.\n" +
                      "Run Holodeck > Setup World Config to refresh all save-system wiring.");
        }

        // ── Step 1: WorldConfigCardUI prefab ─────────────────────────────────

        static WorldConfigCardUI EnsureCardPrefab()
        {
            // Return existing prefab if already saved (LoadAssetAtPath<T> requires T=GameObject for prefabs)
            var existingGo = AssetDatabase.LoadAssetAtPath<GameObject>(CardPrefabPath);
            if (existingGo != null)
            {
                WorldConfigCardUI existing = existingGo.GetComponent<WorldConfigCardUI>();
                if (existing != null) return existing;
            }

            // Build card hierarchy in memory
            GameObject root = BuildCardHierarchy(out WorldConfigCardUI cardComponent);

            // Save as prefab
            bool success;
            GameObject prefabAsset =
                PrefabUtility.SaveAsPrefabAsset(root, CardPrefabPath, out success);
            Object.DestroyImmediate(root);

            if (!success || prefabAsset == null)
            {
                Debug.LogError("[WorldConfigUiSetup] Failed to save WorldConfigCard prefab to " + CardPrefabPath);
                return null;
            }

            WorldConfigCardUI result = prefabAsset.GetComponent<WorldConfigCardUI>();
            Debug.Log("[WorldConfigUiSetup] Created WorldConfigCard prefab at " + CardPrefabPath);
            return result;
        }

        static GameObject BuildCardHierarchy(out WorldConfigCardUI card)
        {
            // ── Root ──────────────────────────────────────────────────────────
            var root = new GameObject("WorldConfigCard");
            var rootRt = root.AddComponent<RectTransform>();
            rootRt.sizeDelta = new Vector2(PanelW - 20f, 200f);

            var rootImg = root.AddComponent<Image>();
            rootImg.color = new Color(0.15f, 0.17f, 0.22f, 1f);

            var vlg = root.AddComponent<VerticalLayoutGroup>();
            vlg.padding        = new RectOffset(0, 0, 0, 0);
            vlg.spacing        = 0f;
            vlg.childAlignment = TextAnchor.UpperLeft;
            vlg.childControlWidth  = true;
            vlg.childControlHeight = false;
            vlg.childForceExpandWidth  = true;
            vlg.childForceExpandHeight = false;

            var csf = root.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            card = root.AddComponent<WorldConfigCardUI>();

            // ── Thumbnail ─────────────────────────────────────────────────────
            var thumbGo = new GameObject("Thumbnail", typeof(RectTransform));
            thumbGo.transform.SetParent(root.transform, false);
            var thumbEl = thumbGo.AddComponent<LayoutElement>();
            thumbEl.preferredHeight = 140f;
            card.thumbnail = thumbGo.AddComponent<RawImage>();
            card.thumbnail.color = new Color(0.10f, 0.13f, 0.20f, 1f);
            card.thumbnail.uvRect = new Rect(0, 0, 1, 1);
            card.thumbnail.raycastTarget = false;
            thumbGo.SetActive(false); // hidden until a thumb is assigned

            // ── Info area ─────────────────────────────────────────────────────
            var infoGo = new GameObject("Info", typeof(RectTransform));
            infoGo.transform.SetParent(root.transform, false);
            var infoEl = infoGo.AddComponent<LayoutElement>();
            infoEl.preferredHeight = 56f;
            var infoVlg = infoGo.AddComponent<VerticalLayoutGroup>();
            infoVlg.padding        = new RectOffset(10, 10, 6, 4);
            infoVlg.spacing        = 2f;
            infoVlg.childAlignment = TextAnchor.UpperLeft;
            infoVlg.childControlHeight = false;
            infoVlg.childControlWidth  = true;
            infoVlg.childForceExpandWidth  = true;
            infoVlg.childForceExpandHeight = false;
            infoGo.AddComponent<Image>().color = new Color(0.09f, 0.10f, 0.14f, 1f);

            card.nameLabel = MakeTmpLabel(infoGo.transform, "NameLabel", "World Name", 15, Color.white, 360f, FontStyles.Bold);

            // ── Source + Date row ─────────────────────────────────────────────
            var sdRowGo = new GameObject("SourceDateRow", typeof(RectTransform));
            sdRowGo.transform.SetParent(infoGo.transform, false);
            var sdEl = sdRowGo.AddComponent<LayoutElement>();
            sdEl.preferredHeight = 18f;
            var sdHlg = sdRowGo.AddComponent<HorizontalLayoutGroup>();
            sdHlg.childAlignment       = TextAnchor.MiddleLeft;
            sdHlg.childControlHeight   = true;
            sdHlg.childControlWidth    = false;
            sdHlg.childForceExpandWidth = false;
            sdHlg.spacing              = 0f;

            card.sourceLabel = MakeTmpLabel(sdRowGo.transform, "SourceLabel", "WorldLabs", 11,
                new Color(0.55f, 0.70f, 1.00f, 1f), 110f, FontStyles.Normal);
            card.dateLabel = MakeTmpLabel(sdRowGo.transform, "DateLabel", "2026-01-01", 11,
                new Color(0.55f, 0.55f, 0.65f, 1f), 250f, FontStyles.Normal,
                HorizontalAlignmentOptions.Right);

            // ── Button row ────────────────────────────────────────────────────
            var btnRowGo = new GameObject("ButtonRow", typeof(RectTransform));
            btnRowGo.transform.SetParent(root.transform, false);
            var btnEl = btnRowGo.AddComponent<LayoutElement>();
            btnEl.preferredHeight = 36f;
            var btnHlg = btnRowGo.AddComponent<HorizontalLayoutGroup>();
            btnHlg.padding             = new RectOffset(6, 6, 4, 4);
            btnHlg.spacing             = 6f;
            btnHlg.childAlignment      = TextAnchor.MiddleLeft;
            btnHlg.childControlHeight  = true;
            btnHlg.childControlWidth   = true;
            btnHlg.childForceExpandWidth  = true;
            btnHlg.childForceExpandHeight = true;
            btnRowGo.AddComponent<Image>().color = new Color(0.09f, 0.10f, 0.14f, 1f);

            card.loadButton   = MakeCardButton(btnRowGo.transform, "LoadButton",   "Load",    new Color(0.20f, 0.55f, 0.20f, 1f));
            card.saveAsButton = MakeCardButton(btnRowGo.transform, "SaveAsButton", "Save As", new Color(0.18f, 0.36f, 0.65f, 1f));
            card.deleteButton = MakeCardButton(btnRowGo.transform, "DeleteButton", "Delete",  new Color(0.55f, 0.12f, 0.12f, 1f));

            return root;
        }

        static TextMeshProUGUI MakeTmpLabel(
            Transform parent, string name, string text, int fontSize,
            Color color, float preferredWidth, FontStyles style,
            HorizontalAlignmentOptions align = HorizontalAlignmentOptions.Left)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var el = go.AddComponent<LayoutElement>();
            el.preferredWidth  = preferredWidth;
            el.preferredHeight = fontSize + 4f;
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text                  = text;
            tmp.fontSize              = fontSize;
            tmp.color                 = color;
            tmp.fontStyle             = style;
            tmp.horizontalAlignment   = align;
            tmp.verticalAlignment     = VerticalAlignmentOptions.Middle;
            tmp.raycastTarget         = false;
            tmp.overflowMode          = TextOverflowModes.Ellipsis;
            return tmp;
        }

        static Button MakeCardButton(Transform parent, string name, string label, Color bg)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var img = go.AddComponent<Image>();
            img.color = bg;
            img.raycastTarget = true;

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.transition    = Selectable.Transition.ColorTint;
            btn.navigation    = new Navigation { mode = Navigation.Mode.None };
            var colors = btn.colors;
            colors.highlightedColor = new Color(
                Mathf.Min(1f, bg.r + 0.15f),
                Mathf.Min(1f, bg.g + 0.15f),
                Mathf.Min(1f, bg.b + 0.15f), 1f);
            colors.pressedColor = new Color(bg.r * 0.7f, bg.g * 0.7f, bg.b * 0.7f, 1f);
            btn.colors = colors;

            var txtGo = new GameObject("Text", typeof(RectTransform));
            txtGo.transform.SetParent(go.transform, false);
            var txtRt = txtGo.GetComponent<RectTransform>();
            txtRt.anchorMin = Vector2.zero; txtRt.anchorMax = Vector2.one;
            txtRt.offsetMin = txtRt.offsetMax = Vector2.zero;
            var tmp = txtGo.AddComponent<TextMeshProUGUI>();
            tmp.text                = label;
            tmp.fontSize            = 12;
            tmp.color               = Color.white;
            tmp.horizontalAlignment = HorizontalAlignmentOptions.Center;
            tmp.verticalAlignment   = VerticalAlignmentOptions.Middle;
            tmp.raycastTarget       = false;

            return btn;
        }

        // ── Step 2: My Worlds scene panel ─────────────────────────────────────

        static MyWorldsPanel EnsureMyWorldsPanel()
        {
            // Find the UI root (same parent as WorldLabs_GUI)
            GameObject uiRoot = EnsureRootObject("UI");

            // Reuse if already present (Transform.Find searches direct children correctly)
            Transform existingT = uiRoot.transform.Find("MyWorldsPanel");
            if (existingT != null)
            {
                MyWorldsPanel existing = existingT.GetComponent<MyWorldsPanel>();
                if (existing != null) return existing;
            }

            // Build or reuse panel root under UI
            GameObject panelRoot = EnsureChildObject(uiRoot, "MyWorldsPanel");

            // ── World-space Canvas ────────────────────────────────────────────
            GameObject canvasGo = EnsureChildObject(panelRoot, "MyWorldsCanvas");
            Canvas canvas = GetOrAdd<Canvas>(canvasGo);
            canvas.renderMode  = RenderMode.WorldSpace;
            GetOrAdd<GraphicRaycaster>(canvasGo);

            var canvasRt = canvasGo.GetComponent<RectTransform>();
            canvasRt.sizeDelta     = new Vector2(PanelW, PanelH);
            canvasRt.localScale    = new Vector3(Scale, Scale, Scale);
            canvasRt.localPosition = Vector3.zero;
            canvasRt.localRotation = Quaternion.identity;

            // Position slightly to the right so it doesn't overlap WorldBrowserController
            panelRoot.transform.localPosition = new Vector3(1.3f, 0f, 1.5f);

            // ── Root background ───────────────────────────────────────────────
            GameObject bg = EnsureChildObject(canvasGo, "Background");
            Stretch(bg, Vector2.zero, Vector2.one, 0f, 0f);
            GetOrAdd<Image>(bg).color = new Color(0.10f, 0.10f, 0.14f, 0.97f);

            // ── Header ────────────────────────────────────────────────────────
            GameObject header = EnsureChildObject(canvasGo, "Header");
            AnchorTop(header, HeaderH);
            GetOrAdd<Image>(header).color = new Color(0.11f, 0.11f, 0.18f, 1f);

            GameObject titleGo = EnsureChildObject(header, "Title");
            Stretch(titleGo, Vector2.zero, Vector2.one, 12f, -12f);
            var titleTmp = GetOrAdd<TextMeshProUGUI>(titleGo);
            titleTmp.text               = "My Worlds";
            titleTmp.fontSize           = 16;
            titleTmp.color              = Color.white;
            titleTmp.fontStyle          = FontStyles.Bold;
            titleTmp.horizontalAlignment = HorizontalAlignmentOptions.Left;
            titleTmp.verticalAlignment   = VerticalAlignmentOptions.Middle;
            titleTmp.raycastTarget      = false;

            // ── Scroll rect ───────────────────────────────────────────────────
            float scrollBottom = SaveAsH + 8f;
            GameObject scrollGo = EnsureChildObject(canvasGo, "CardListScrollRect");
            Stretch(scrollGo, Vector2.zero, Vector2.one, 0f, 0f);
            var scrollRt = scrollGo.GetComponent<RectTransform>();
            scrollRt.offsetMin = new Vector2(0f, scrollBottom);
            scrollRt.offsetMax = new Vector2(0f, -HeaderH);

            ScrollRect sr = GetOrAdd<ScrollRect>(scrollGo);
            sr.horizontal       = false;
            sr.vertical         = true;
            sr.scrollSensitivity = 30f;
            sr.movementType     = ScrollRect.MovementType.Elastic;
            sr.elasticity       = 0.1f;

            GameObject viewport = EnsureChildObject(scrollGo, "Viewport");
            Stretch(viewport, Vector2.zero, Vector2.one, 0f, 0f);
            GetOrAdd<Image>(viewport).color = new Color(0, 0, 0, 0.01f); // near-transparent for mask
            var mask = GetOrAdd<Mask>(viewport);
            mask.showMaskGraphic = false;
            sr.viewport = viewport.GetComponent<RectTransform>();

            GameObject content = EnsureChildObject(viewport, "Content");
            var contentRt = GetOrAdd<RectTransform>(content);
            contentRt.anchorMin = new Vector2(0f, 1f);
            contentRt.anchorMax = new Vector2(1f, 1f);
            contentRt.pivot     = new Vector2(0.5f, 1f);
            contentRt.offsetMin = contentRt.offsetMax = Vector2.zero;

            var contentVlg = GetOrAdd<VerticalLayoutGroup>(content);
            contentVlg.padding        = new RectOffset(10, 10, 8, 8);
            contentVlg.spacing        = 8f;
            contentVlg.childAlignment = TextAnchor.UpperCenter;
            contentVlg.childControlWidth  = true;
            contentVlg.childControlHeight = true;
            contentVlg.childForceExpandWidth  = true;
            contentVlg.childForceExpandHeight = false;

            var contentCsf = GetOrAdd<ContentSizeFitter>(content);
            contentCsf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            sr.content = contentRt;

            // ── Status label (shown when list is empty) ───────────────────────
            GameObject statusGo = EnsureChildObject(canvasGo, "StatusLabel");
            var statusRt = statusGo.GetComponent<RectTransform>();
            if (statusRt == null) statusRt = statusGo.AddComponent<RectTransform>();
            statusRt.anchorMin = new Vector2(0f, 0.3f);
            statusRt.anchorMax = new Vector2(1f, 0.7f);
            statusRt.offsetMin = new Vector2(20f, 0f);
            statusRt.offsetMax = new Vector2(-20f, 0f);
            var statusTmp = GetOrAdd<TextMeshProUGUI>(statusGo);
            statusTmp.text               = "No saved worlds found.";
            statusTmp.fontSize           = 14;
            statusTmp.color              = new Color(0.6f, 0.6f, 0.65f, 1f);
            statusTmp.horizontalAlignment = HorizontalAlignmentOptions.Center;
            statusTmp.verticalAlignment   = VerticalAlignmentOptions.Middle;
            statusTmp.raycastTarget      = false;
            statusGo.SetActive(false);

            // ── Save As area (shown when user taps Save As on a card) ─────────
            GameObject saveAsArea = EnsureChildObject(canvasGo, "SaveAsArea");
            AnchorBottom(saveAsArea, SaveAsH);
            GetOrAdd<Image>(saveAsArea).color = new Color(0.09f, 0.09f, 0.13f, 1f);

            var saveAsHlg = GetOrAdd<HorizontalLayoutGroup>(saveAsArea);
            saveAsHlg.padding             = new RectOffset(8, 8, 6, 6);
            saveAsHlg.spacing             = 8f;
            saveAsHlg.childControlHeight  = true;
            saveAsHlg.childControlWidth   = false;
            saveAsHlg.childForceExpandWidth  = false;
            saveAsHlg.childForceExpandHeight = true;

            GameObject inputGo = EnsureChildObject(saveAsArea, "SaveAsInput");
            var inputEl = GetOrAdd<LayoutElement>(inputGo);
            inputEl.preferredWidth  = PanelW - 100f - 8f * 3f;
            inputEl.flexibleWidth   = 1f;
            var inputField = GetOrAdd<InputField>(inputGo);
            if (inputField.targetGraphic == null)
            {
                var inputImg = GetOrAdd<Image>(inputGo);
                inputImg.color = new Color(0.20f, 0.21f, 0.28f, 1f);
                inputField.targetGraphic = inputImg;
            }
            // Create placeholder and text children only if they don't exist yet
            if (inputField.placeholder == null)
            {
                var placeholder = new GameObject("Placeholder", typeof(RectTransform));
                placeholder.transform.SetParent(inputGo.transform, false);
                Stretch(placeholder, Vector2.zero, Vector2.one, 4f, -4f);
                var phText = placeholder.AddComponent<Text>();
                phText.text      = "New name…";
                phText.fontSize  = 13;
                phText.color     = new Color(0.5f, 0.5f, 0.5f, 0.8f);
                phText.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                phText.alignment = TextAnchor.MiddleLeft;
                inputField.placeholder = phText;
            }
            if (inputField.textComponent == null)
            {
                var textGo = new GameObject("Text", typeof(RectTransform));
                textGo.transform.SetParent(inputGo.transform, false);
                Stretch(textGo, Vector2.zero, Vector2.one, 4f, -4f);
                var inputText = textGo.AddComponent<Text>();
                inputText.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                inputText.fontSize  = 13;
                inputText.color     = Color.white;
                inputText.alignment = TextAnchor.MiddleLeft;
                inputField.textComponent = inputText;
            }
            // Input starts hidden; MyWorldsPanel.BeginSaveAs activates it
            inputGo.SetActive(false);

            GameObject confirmGo = EnsureChildObject(saveAsArea, "SaveAsConfirm");
            var confirmEl = GetOrAdd<LayoutElement>(confirmGo);
            confirmEl.preferredWidth = 90f;
            var confirmImg = GetOrAdd<Image>(confirmGo);
            confirmImg.color = new Color(0.20f, 0.55f, 0.20f, 1f);
            var confirmBtn = GetOrAdd<Button>(confirmGo);
            confirmBtn.targetGraphic = confirmImg;
            confirmBtn.navigation    = new Navigation { mode = Navigation.Mode.None };
            confirmGo.SetActive(false); // hidden until BeginSaveAs activates it

            if (confirmGo.transform.Find("Text") == null)
            {
                var ctGo = new GameObject("Text", typeof(RectTransform));
                ctGo.transform.SetParent(confirmGo.transform, false);
                var ctRt = ctGo.GetComponent<RectTransform>();
                ctRt.anchorMin = Vector2.zero; ctRt.anchorMax = Vector2.one;
                ctRt.offsetMin = ctRt.offsetMax = Vector2.zero;
                var ctTmp = ctGo.AddComponent<TextMeshProUGUI>();
                ctTmp.text               = "Confirm";
                ctTmp.fontSize           = 13;
                ctTmp.color              = Color.white;
                ctTmp.horizontalAlignment = HorizontalAlignmentOptions.Center;
                ctTmp.verticalAlignment   = VerticalAlignmentOptions.Middle;
                ctTmp.raycastTarget      = false;
            }

            // ── Add MyWorldsPanel component and wire its fields ───────────────
            MyWorldsPanel mp = GetOrAdd<MyWorldsPanel>(panelRoot);
            Undo.RecordObject(mp, "Wire MyWorldsPanel fields");
            mp.cardListContent    = contentRt;
            mp.statusLabel        = statusTmp;
            mp.saveAsInputField   = inputField;
            mp.saveAsConfirmButton = confirmBtn;

            panelRoot.SetActive(false); // panel is hidden by default; shown by UiPanelController

            return mp;
        }

        // ── Step 3: Register with UiPanelController ───────────────────────────

        static void WireMyWorldsPanel(MyWorldsPanel panel)
        {
            UiPanelController uiPanels = Object.FindFirstObjectByType<UiPanelController>();
            if (uiPanels == null)
            {
                Debug.LogWarning("[WorldConfigUiSetup] UiPanelController not found in scene. " +
                                 "Run Holodeck > Setup SpeechIntent first.");
                return;
            }

            const string key = "my worlds";
            foreach (UiPanelController.PanelEntry e in uiPanels.panels)
                if (string.Equals(e?.key, key, System.StringComparison.OrdinalIgnoreCase)) return;

            Undo.RecordObject(uiPanels, "Register 'my worlds' panel");
            uiPanels.panels.Add(new UiPanelController.PanelEntry
            {
                key  = key,
                root = panel.gameObject
            });
        }

        // ── Step 4: Refresh save-system references ────────────────────────────

        static void RefreshSaveSystemReferences(MyWorldsPanel panel)
        {
            // Find WorldConfigStore and Restorer (likely on Systems/SpeechIntent)
            var store    = Object.FindFirstObjectByType<Holodeck.Save.WorldConfigStore>();
            var restorer = Object.FindFirstObjectByType<Holodeck.Save.WorldConfigRestorer>();

            if (store != null && panel.worldConfigStore == null)
            {
                Undo.RecordObject(panel, "Wire MyWorldsPanel.worldConfigStore");
                panel.worldConfigStore = store;
            }
            if (restorer != null && panel.worldConfigRestorer == null)
            {
                Undo.RecordObject(panel, "Wire MyWorldsPanel.worldConfigRestorer");
                panel.worldConfigRestorer = restorer;
            }
        }

        // ── Layout helpers ────────────────────────────────────────────────────

        static void Stretch(GameObject go, Vector2 anchorMin, Vector2 anchorMax,
            float leftPad = 0f, float rightPad = 0f)
        {
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = new Vector2(leftPad, 0f);
            rt.offsetMax = new Vector2(rightPad, 0f);
        }

        static void AnchorTop(GameObject go, float height)
        {
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot     = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(0f, -height);
            rt.offsetMax = Vector2.zero;
        }

        static void AnchorBottom(GameObject go, float height)
        {
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot     = new Vector2(0.5f, 0f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = new Vector2(0f, height);
        }

        // ── Scene helpers ─────────────────────────────────────────────────────

        static GameObject EnsureRootObject(string objName)
        {
            Scene active = SceneManager.GetActiveScene();
            foreach (GameObject root in active.GetRootGameObjects())
                if (root.name == objName) return root;

            GameObject created = new GameObject(objName);
            Undo.RegisterCreatedObjectUndo(created, $"Create {objName}");
            return created;
        }

        static GameObject EnsureChildObject(GameObject parent, string childName)
        {
            Transform t = parent.transform.Find(childName);
            if (t != null) return t.gameObject;

            // Create with RectTransform so UI layout helpers work immediately after parenting
            var child = new GameObject(childName, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(child, $"Create {childName}");
            child.transform.SetParent(parent.transform, false);
            return child;
        }

        static T GetOrAdd<T>(GameObject go) where T : Component
        {
            T c = go.GetComponent<T>();
            return c != null ? c : Undo.AddComponent<T>(go);
        }
    }
}
