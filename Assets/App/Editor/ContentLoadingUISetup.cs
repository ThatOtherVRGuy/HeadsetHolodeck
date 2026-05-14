// Assets/App/Editor/ContentLoadingUISetup.cs
// Menu: Holodeck / Setup Content Loading UI

using SpeechIntent;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.UI;
using System.IO;

/// <summary>
/// Creates the world-space Content Loading UI canvas and wires all component references.
/// Run once per scene; re-running creates a second canvas (delete the old one first).
///
/// Canvas defaults: 600 × 750 px, world-space, GameObject scale (0.002, 0.002, 0.002)
///   → ~1.2 m × 1.5 m in world space. Scale the transform to taste.
/// </summary>
public static class ContentLoadingUISetup
{
    // ── Palette ───────────────────────────────────────────────────────────────────
    static readonly Color CanvasBg        = new Color(0.08f, 0.09f, 0.12f, 0.97f);
    static readonly Color TabBarBg        = new Color(0.05f, 0.06f, 0.09f, 1.00f);
    static readonly Color TabActive       = new Color(0.25f, 0.55f, 1.00f, 1.00f);
    static readonly Color TabInactive     = new Color(0.18f, 0.18f, 0.22f, 1.00f);
    static readonly Color PanelHeaderBg   = new Color(0.10f, 0.11f, 0.15f, 1.00f);
    static readonly Color DividerColor    = new Color(1.00f, 1.00f, 1.00f, 0.08f);
    static readonly Color InputFieldBg    = new Color(0.12f, 0.13f, 0.18f, 1.00f);
    static readonly Color LoadBtnColor    = new Color(0.25f, 0.55f, 1.00f, 1.00f);
    static readonly Color ClearBtnColor   = new Color(0.55f, 0.20f, 0.20f, 1.00f);
    static readonly Color RefreshBtnColor = new Color(0.20f, 0.45f, 0.20f, 1.00f);
    static readonly Color ScrollBg        = new Color(0.06f, 0.07f, 0.10f, 1.00f);
    static readonly Color StatusColor     = new Color(1.00f, 0.75f, 0.30f, 1.00f);
    static readonly Color LabelColor      = new Color(0.85f, 0.90f, 1.00f, 1.00f);
    static readonly Color SubLabelColor   = new Color(0.55f, 0.60f, 0.70f, 1.00f);

    const float CanvasW = 600f;
    const float CanvasH = 750f;

    // ─────────────────────────────────────────────────────────────────────────────

    [MenuItem("Holodeck/Setup Content Loading UI")]
    static void CreateContentLoadingUI()
    {
        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("Create Content Loading UI");

        // ── Find /UI parent ───────────────────────────────────────────────────────
        GameObject uiParent = GameObject.Find("UI");
        if (uiParent == null)
        {
            uiParent = new GameObject("UI");
            Undo.RegisterCreatedObjectUndo(uiParent, "Create UI root");
        }

        // ── Canvas root ───────────────────────────────────────────────────────────
        var canvasGO = CreateGO("ContentLoadingCanvas", uiParent.transform);

        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;

        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode            = CanvasScaler.ScaleMode.ConstantPixelSize;
        scaler.referencePixelsPerUnit = 1f;

        canvasGO.AddComponent<GraphicRaycaster>();

        var canvasRT = canvasGO.GetComponent<RectTransform>();
        canvasRT.sizeDelta            = new Vector2(CanvasW, CanvasH);
        canvasGO.transform.localScale    = new Vector3(0.002f, 0.002f, 0.002f);
        canvasGO.transform.localPosition = Vector3.zero;

        AddImage(canvasGO, CanvasBg);

        var uiCtrl = canvasGO.AddComponent<ContentLoadingUI>();

        // ── Root layout (fills canvas) ────────────────────────────────────────────
        var root   = CreateGO("Root", canvasGO.transform);
        var rootRT = root.GetComponent<RectTransform>();
        rootRT.anchorMin = Vector2.zero;
        rootRT.anchorMax = Vector2.one;
        rootRT.offsetMin = Vector2.zero;
        rootRT.offsetMax = Vector2.zero;

        var rootVLG = root.AddComponent<VerticalLayoutGroup>();
        rootVLG.padding                = new RectOffset(0, 0, 0, 0);
        rootVLG.spacing                = 0;
        rootVLG.childForceExpandWidth  = true;
        rootVLG.childForceExpandHeight = false;
        rootVLG.childAlignment         = TextAnchor.UpperCenter;

        // ── Tab bar ───────────────────────────────────────────────────────────────
        var (tabBar, localTab, urlsTab) = BuildTabBar(root.transform);
        SetLayoutHeight(tabBar, 50f);

        BuildDivider(root.transform);

        // ── Local Files panel ─────────────────────────────────────────────────────
        var (localPanel, fileBrowser, fileContent, fileStatus) = BuildLocalFilesPanel(root.transform);
        SetLayoutFlexible(localPanel, flexibleHeight: 1f);

        // ── URLs panel ────────────────────────────────────────────────────────────
        var (urlPanel, urlLoader, inputField, urlContent, urlStatus) = BuildUrlsPanel(root.transform);
        SetLayoutFlexible(urlPanel, flexibleHeight: 1f);
        urlPanel.SetActive(false);

        // ── Wire ContentLoadingUI ─────────────────────────────────────────────────
        Undo.RecordObject(uiCtrl, "Wire ContentLoadingUI");
        uiCtrl.localFilesPanel = localPanel;
        uiCtrl.urlsPanel       = urlPanel;
        uiCtrl.localFilesTab   = localTab;
        uiCtrl.urlsTab         = urlsTab;
        EditorUtility.SetDirty(uiCtrl);

        // Persistent (serialized) listeners for tab buttons
        UnityEventTools.AddVoidPersistentListener(localTab.onClick, uiCtrl.ShowLocalFiles);
        UnityEventTools.AddVoidPersistentListener(urlsTab.onClick,  uiCtrl.ShowUrls);

        // ── Wire loaders (auto-discover from scene) ───────────────────────────────
        var splatLoader  = Object.FindObjectOfType<LocalRemoteSplatLoader>();
        var panoLoader   = Object.FindObjectOfType<LocalRemotePanoLoader>();
        var configStore  = Object.FindObjectOfType<Holodeck.Save.WorldConfigStore>();

        // ── Create list-item prefabs ──────────────────────────────────────────────
        var (fileEntryPrefab, urlHistoryPrefab) = CreateListItemPrefabs();

        Undo.RecordObject(fileBrowser, "Wire LocalFileBrowserPanel");
        fileBrowser.splatLoader      = splatLoader;
        fileBrowser.panoLoader       = panoLoader;
        fileBrowser.worldConfigStore = configStore;
        fileBrowser.fileListContent  = fileContent;
        fileBrowser.statusLabel      = fileStatus;
        fileBrowser.fileEntryPrefab  = fileEntryPrefab;
        EditorUtility.SetDirty(fileBrowser);

        Undo.RecordObject(urlLoader, "Wire UrlLoaderPanel");
        urlLoader.splatLoader           = splatLoader;
        urlLoader.panoLoader            = panoLoader;
        urlLoader.historyListContent    = urlContent;
        urlLoader.statusLabel           = urlStatus;
        urlLoader.urlInputField         = inputField;
        urlLoader.urlHistoryItemPrefab  = urlHistoryPrefab;
        EditorUtility.SetDirty(urlLoader);

        // ── Register with UiPanelController if present ────────────────────────────
        var panelCtrl = Object.FindObjectOfType<UiPanelController>();
        if (panelCtrl != null)
        {
            Undo.RecordObject(panelCtrl, "Register ContentLoader panel");
            panelCtrl.panels.Add(new UiPanelController.PanelEntry
            {
                key  = "ContentLoader",
                root = canvasGO,
            });
            EditorUtility.SetDirty(panelCtrl);
        }

        // ── Finalize ──────────────────────────────────────────────────────────────
        Selection.activeGameObject = canvasGO;

        if (splatLoader == null)
            Debug.LogWarning("[ContentLoadingUISetup] LocalRemoteSplatLoader not found — assign manually.");
        if (panoLoader == null)
            Debug.LogWarning("[ContentLoadingUISetup] LocalRemotePanoLoader not found — assign manually.");

        Debug.Log("[ContentLoadingUISetup] Done.");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Panel builders
    // ═════════════════════════════════════════════════════════════════════════════

    static (GameObject tabBar, Button localTab, Button urlsTab) BuildTabBar(Transform parent)
    {
        var bar = CreateGO("TabBar", parent);
        AddImage(bar, TabBarBg);
        var hlg = bar.AddComponent<HorizontalLayoutGroup>();
        hlg.padding                = new RectOffset(4, 4, 4, 4);
        hlg.spacing                = 4;
        hlg.childForceExpandWidth  = true;
        hlg.childForceExpandHeight = true;

        var localBtn = BuildTabButton(bar.transform, "Local Files", TabActive);
        var urlBtn   = BuildTabButton(bar.transform, "Remote URLs", TabInactive);
        return (bar, localBtn, urlBtn);
    }

    static (GameObject panel, LocalFileBrowserPanel browser, RectTransform content, TMP_Text status)
        BuildLocalFilesPanel(Transform parent)
    {
        var panel = CreateGO("LocalFilesPanel", parent);
        AddImage(panel, CanvasBg);
        var vlg = panel.AddComponent<VerticalLayoutGroup>();
        vlg.padding                = new RectOffset(8, 8, 8, 8);
        vlg.spacing                = 6;
        vlg.childForceExpandWidth  = true;
        vlg.childForceExpandHeight = false;

        // Header row: label + refresh button
        var headerRow = CreateGO("Header", panel.transform);
        AddImage(headerRow, PanelHeaderBg);
        var hlg = headerRow.AddComponent<HorizontalLayoutGroup>();
        hlg.padding                = new RectOffset(12, 8, 0, 0);
        hlg.spacing                = 8;
        hlg.childAlignment         = TextAnchor.MiddleLeft;
        hlg.childForceExpandWidth  = false;
        hlg.childForceExpandHeight = true;
        SetLayoutHeight(headerRow, 44f);

        // Label child (flexible width)
        var labelGO = CreateGO("Label", headerRow.transform);
        labelGO.AddComponent<LayoutElement>().flexibleWidth = 1;
        AddFilledText(labelGO, "Local Files", 20, LabelColor, TextAlignmentOptions.MidlineLeft, bold: true);

        var refreshBtn = BuildSmallButton(headerRow.transform, "↺  Refresh", RefreshBtnColor, 110f);

        // Status label (hidden until needed)
        var statusGO = CreateGO("Status", panel.transform);
        SetLayoutHeight(statusGO, 32f);
        var statusTMP = AddFilledText(statusGO, "", 14, StatusColor, TextAlignmentOptions.MidlineLeft);
        statusGO.SetActive(false);

        // Scroll view for file list
        var (scrollGO, content) = BuildScrollView(panel.transform, "FileScroll");
        SetLayoutFlexible(scrollGO, flexibleHeight: 1f);

        var browser = panel.AddComponent<LocalFileBrowserPanel>();

        // Persistent listener on refresh button
        UnityEventTools.AddVoidPersistentListener(refreshBtn.onClick, browser.Refresh);

        return (panel, browser, content, statusTMP);
    }

    static (GameObject panel, UrlLoaderPanel loader, TMP_InputField inputField,
            RectTransform content, TMP_Text status)
        BuildUrlsPanel(Transform parent)
    {
        var panel = CreateGO("UrlsPanel", parent);
        AddImage(panel, CanvasBg);
        var vlg = panel.AddComponent<VerticalLayoutGroup>();
        vlg.padding                = new RectOffset(8, 8, 8, 8);
        vlg.spacing                = 6;
        vlg.childForceExpandWidth  = true;
        vlg.childForceExpandHeight = false;

        // Header row
        var headerRow = CreateGO("Header", panel.transform);
        AddImage(headerRow, PanelHeaderBg);
        var headerHLG = headerRow.AddComponent<HorizontalLayoutGroup>();
        headerHLG.padding                = new RectOffset(12, 8, 0, 0);
        headerHLG.childAlignment         = TextAnchor.MiddleLeft;
        headerHLG.childForceExpandWidth  = true;
        headerHLG.childForceExpandHeight = true;
        SetLayoutHeight(headerRow, 44f);

        // Label is a child of the header row (not the row itself)
        var headerLabelGO = CreateGO("Label", headerRow.transform);
        AddFilledText(headerLabelGO, "Remote URLs", 20, LabelColor, TextAlignmentOptions.MidlineLeft, bold: true);

        // Input row: text field + Load button
        var inputRow = CreateGO("InputRow", panel.transform);
        var inputHLG = inputRow.AddComponent<HorizontalLayoutGroup>();
        inputHLG.spacing               = 6;
        inputHLG.childAlignment        = TextAnchor.MiddleLeft;
        inputHLG.childForceExpandWidth  = false;
        inputHLG.childForceExpandHeight = true;
        SetLayoutHeight(inputRow, 50f);

        var inputField = BuildTMPInputField(inputRow.transform, "Enter URL or file path…");
        inputField.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;

        var loadBtn = BuildSmallButton(inputRow.transform, "Load", LoadBtnColor, 90f);

        // Status label
        var statusGO = CreateGO("Status", panel.transform);
        SetLayoutHeight(statusGO, 28f);
        var statusTMP = AddFilledText(statusGO, "", 13, StatusColor, TextAlignmentOptions.MidlineLeft);
        statusGO.SetActive(false);

        // History sub-header: "Recent" label + Clear button
        var histRow = CreateGO("HistoryHeader", panel.transform);
        var histHLG = histRow.AddComponent<HorizontalLayoutGroup>();
        histHLG.padding                = new RectOffset(4, 4, 0, 0);
        histHLG.spacing                = 8;
        histHLG.childAlignment         = TextAnchor.MiddleLeft;
        histHLG.childForceExpandWidth  = false;
        histHLG.childForceExpandHeight = true;
        SetLayoutHeight(histRow, 32f);

        var recentLabelGO = CreateGO("RecentLabel", histRow.transform);
        recentLabelGO.AddComponent<LayoutElement>().flexibleWidth = 1;
        AddFilledText(recentLabelGO, "Recent", 14, SubLabelColor, TextAlignmentOptions.MidlineLeft);

        var clearBtn = BuildSmallButton(histRow.transform, "Clear", ClearBtnColor, 70f);

        BuildDivider(panel.transform);

        // History scroll view
        var (scrollGO, content) = BuildScrollView(panel.transform, "HistoryScroll");
        SetLayoutFlexible(scrollGO, flexibleHeight: 1f);

        var loader = panel.AddComponent<UrlLoaderPanel>();

        // Persistent listeners
        UnityEventTools.AddVoidPersistentListener(loadBtn.onClick,  loader.OnLoadButtonClicked);
        UnityEventTools.AddVoidPersistentListener(clearBtn.onClick, loader.ClearHistory);

        return (panel, loader, inputField, content, statusTMP);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // UI primitives
    // ═════════════════════════════════════════════════════════════════════════════

    static (GameObject scrollRoot, RectTransform content) BuildScrollView(Transform parent, string name)
    {
        var scrollGO = CreateGO(name, parent);
        AddImage(scrollGO, ScrollBg);
        var sr = scrollGO.AddComponent<ScrollRect>();
        sr.horizontal        = false;
        sr.vertical          = true;
        sr.movementType      = ScrollRect.MovementType.Clamped;
        sr.scrollSensitivity = 30f;

        var viewportGO = CreateGO("Viewport", scrollGO.transform);
        var viewportRT = viewportGO.GetComponent<RectTransform>();
        viewportRT.anchorMin = Vector2.zero;
        viewportRT.anchorMax = Vector2.one;
        viewportRT.offsetMin = Vector2.zero;
        viewportRT.offsetMax = Vector2.zero;
        viewportGO.AddComponent<RectMask2D>();
        sr.viewport = viewportRT;

        var contentGO = CreateGO("Content", viewportGO.transform);
        var contentRT = contentGO.GetComponent<RectTransform>();
        contentRT.anchorMin = new Vector2(0f, 1f);
        contentRT.anchorMax = new Vector2(1f, 1f);
        contentRT.pivot     = new Vector2(0.5f, 1f);
        contentRT.offsetMin = Vector2.zero;
        contentRT.offsetMax = Vector2.zero;

        var vlg = contentGO.AddComponent<VerticalLayoutGroup>();
        vlg.padding                = new RectOffset(4, 4, 4, 4);
        vlg.spacing                = 3;
        vlg.childForceExpandWidth  = true;
        vlg.childForceExpandHeight = false;

        var csf = contentGO.AddComponent<ContentSizeFitter>();
        csf.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;
        csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        sr.content = contentRT;
        return (scrollGO, contentRT);
    }

    /// <summary>Button with Image background; text is a stretched child GO.</summary>
    static Button BuildTabButton(Transform parent, string label, Color bgColor)
    {
        var go  = CreateGO(label, parent);
        var img = go.AddComponent<Image>();
        img.color = bgColor;

        var btn  = go.AddComponent<Button>();
        btn.targetGraphic = img;
        var cols = btn.colors;
        cols.normalColor      = bgColor;
        cols.highlightedColor = Color.Lerp(bgColor, Color.white, 0.15f);
        cols.pressedColor     = Color.Lerp(bgColor, Color.white, 0.30f);
        btn.colors = cols;

        // Text as a child so it doesn't disturb the button's own RectTransform
        var textGO = CreateGO("Text", go.transform);
        StretchRT(textGO.GetComponent<RectTransform>());
        ConfigureTMP(textGO.AddComponent<TextMeshProUGUI>(), label, 17, Color.white,
            TextAlignmentOptions.Center, bold: true);

        return btn;
    }

    /// <summary>Small button with fixed width; text is a stretched child GO.</summary>
    static Button BuildSmallButton(Transform parent, string label, Color bgColor, float width)
    {
        var go  = CreateGO(label, parent);
        var img = go.AddComponent<Image>();
        img.color = bgColor;

        var le = go.AddComponent<LayoutElement>();
        le.minWidth       = width;
        le.preferredWidth = width;

        var btn  = go.AddComponent<Button>();
        btn.targetGraphic = img;
        var cols = btn.colors;
        cols.normalColor      = bgColor;
        cols.highlightedColor = Color.Lerp(bgColor, Color.white, 0.20f);
        cols.pressedColor     = Color.Lerp(bgColor, Color.black, 0.20f);
        btn.colors = cols;

        var textGO = CreateGO("Text", go.transform);
        StretchRT(textGO.GetComponent<RectTransform>());
        ConfigureTMP(textGO.AddComponent<TextMeshProUGUI>(), label, 15, Color.white,
            TextAlignmentOptions.Center, bold: true);

        return btn;
    }

    static TMP_InputField BuildTMPInputField(Transform parent, string placeholder)
    {
        var root = CreateGO("InputField", parent);
        AddImage(root, InputFieldBg);

        var field = root.AddComponent<TMP_InputField>();

        var areaGO = CreateGO("Text Area", root.transform);
        var areaRT = areaGO.GetComponent<RectTransform>();
        areaRT.anchorMin = Vector2.zero;
        areaRT.anchorMax = Vector2.one;
        areaRT.offsetMin = new Vector2(10f, 6f);
        areaRT.offsetMax = new Vector2(-10f, -6f);
        areaGO.AddComponent<RectMask2D>();

        var phGO = CreateGO("Placeholder", areaGO.transform);
        StretchRT(phGO.GetComponent<RectTransform>());
        var ph = phGO.AddComponent<TextMeshProUGUI>();
        ph.text      = placeholder;
        ph.fontSize  = 16;
        ph.fontStyle = FontStyles.Italic;
        ph.color     = new Color(0.45f, 0.50f, 0.60f);

        var textGO = CreateGO("Text", areaGO.transform);
        StretchRT(textGO.GetComponent<RectTransform>());
        var textTMP = textGO.AddComponent<TextMeshProUGUI>();
        textTMP.fontSize = 16;
        textTMP.color    = Color.white;

        field.textViewport  = areaRT;
        field.textComponent = textTMP;
        field.placeholder   = ph;
        field.caretColor    = new Color(0.5f, 0.8f, 1f);
        field.caretWidth    = 2;

        return field;
    }

    static void BuildDivider(Transform parent)
    {
        var div = CreateGO("Divider", parent);
        AddImage(div, DividerColor);
        SetLayoutHeight(div, 1f);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Micro helpers
    // ═════════════════════════════════════════════════════════════════════════════

    /// <summary>Creates a GO with RectTransform, parents it, registers with Undo.</summary>
    static GameObject CreateGO(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        Undo.RegisterCreatedObjectUndo(go, "Create Content Loading UI");
        return go;
    }

    static Image AddImage(GameObject go, Color color)
    {
        var img = go.AddComponent<Image>();
        img.color = color;
        return img;
    }

    /// <summary>
    /// Adds a TMP child that stretches to fill <paramref name="go"/>.
    /// Use for label GOs where the text should fill the available rect.
    /// </summary>
    static TMP_Text AddFilledText(GameObject go, string text, float size, Color color,
        TextAlignmentOptions align, bool bold = false)
    {
        // Text on a stretched child so layout components on `go` are undisturbed
        var textGO = new GameObject("Text", typeof(RectTransform));
        textGO.transform.SetParent(go.transform, false);
        Undo.RegisterCreatedObjectUndo(textGO, "Create Content Loading UI");
        StretchRT(textGO.GetComponent<RectTransform>());

        var tmp = textGO.AddComponent<TextMeshProUGUI>();
        ConfigureTMP(tmp, text, size, color, align, bold);
        return tmp;
    }

    static void ConfigureTMP(TextMeshProUGUI tmp, string text, float size, Color color,
        TextAlignmentOptions align, bool bold = false)
    {
        tmp.text         = text;
        tmp.fontSize     = size;
        tmp.color        = color;
        tmp.alignment    = align;
        tmp.overflowMode = TextOverflowModes.Ellipsis;
        if (bold) tmp.fontStyle = FontStyles.Bold;
    }

    static void SetLayoutHeight(GameObject go, float height)
    {
        var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
        le.minHeight       = height;
        le.preferredHeight = height;
    }

    static void SetLayoutFlexible(GameObject go, float flexibleHeight = -1f, float flexibleWidth = -1f)
    {
        var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
        if (flexibleHeight >= 0f) le.flexibleHeight = flexibleHeight;
        if (flexibleWidth  >= 0f) le.flexibleWidth  = flexibleWidth;
    }

    static void StretchRT(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Prefab creation
    // ═════════════════════════════════════════════════════════════════════════════

    const string PrefabFolder = "Assets/App/Command/SpeechIntent/Runtime/UI/Prefabs";

    static readonly Color EntryNormal    = new Color(1f, 1f, 1f, 0.06f);
    static readonly Color EntryHighlight = new Color(0.7f, 0.85f, 1f, 0.25f);
    static readonly Color EntryPressed   = new Color(0.4f, 0.65f, 1f, 0.45f);

    static (FileEntryItemUI fileEntry, UrlHistoryItemUI urlHistory) CreateListItemPrefabs()
    {
        EnsurePrefabFolder();
        var fileEntry  = CreateFileEntryPrefab();
        var urlHistory = CreateUrlHistoryPrefab();
        AssetDatabase.SaveAssets();
        return (fileEntry, urlHistory);
    }

    static void EnsurePrefabFolder()
    {
        string parent = "Assets/App/Command/SpeechIntent/Runtime/UI";
        if (!AssetDatabase.IsValidFolder(PrefabFolder))
            AssetDatabase.CreateFolder(parent, "Prefabs");
    }

    /// <summary>
    /// FileEntryItem prefab:
    ///   Root (Image bg, Button, LayoutElement, HorizontalLayoutGroup)
    ///     FileName (TMP_Text, flexibleWidth)
    ///     Badge (Image pill, HorizontalLayoutGroup)
    ///       BadgeLabel (TMP_Text)
    /// </summary>
    static FileEntryItemUI CreateFileEntryPrefab()
    {
        string path = PrefabFolder + "/FileEntryItem.prefab";

        // Root
        var root    = new GameObject("FileEntryItem", typeof(RectTransform));
        var rootImg = root.AddComponent<Image>();
        rootImg.color = EntryNormal;

        var le = root.AddComponent<LayoutElement>();
        le.minHeight       = 52;
        le.preferredHeight = 52;
        le.flexibleWidth   = 1;

        var btn  = root.AddComponent<Button>();
        btn.targetGraphic = rootImg;
        var cols = btn.colors;
        cols.normalColor      = EntryNormal;
        cols.highlightedColor = EntryHighlight;
        cols.pressedColor     = EntryPressed;
        cols.selectedColor    = EntryHighlight;
        btn.colors = cols;

        var hlg = root.AddComponent<HorizontalLayoutGroup>();
        hlg.padding                = new RectOffset(12, 12, 0, 0);
        hlg.spacing                = 8;
        hlg.childAlignment         = TextAnchor.MiddleLeft;
        hlg.childForceExpandHeight = true;
        hlg.childForceExpandWidth  = false;

        // File name label
        var nameGO  = new GameObject("FileName", typeof(RectTransform));
        nameGO.transform.SetParent(root.transform, false);
        nameGO.AddComponent<LayoutElement>().flexibleWidth = 1;
        var nameTMP = nameGO.AddComponent<TextMeshProUGUI>();
        nameTMP.fontSize    = 16;
        nameTMP.color       = Color.white;
        nameTMP.alignment   = TextAlignmentOptions.MidlineLeft;
        nameTMP.overflowMode = TextOverflowModes.Ellipsis;

        // Badge pill
        var badgeGO  = new GameObject("Badge", typeof(RectTransform));
        badgeGO.transform.SetParent(root.transform, false);
        var badgeImg = badgeGO.AddComponent<Image>();
        badgeImg.color = new Color(0.5f, 0.85f, 1f, 0.25f);
        var badgeLE  = badgeGO.AddComponent<LayoutElement>();
        badgeLE.minWidth        = 52;
        badgeLE.preferredWidth  = 52;
        badgeLE.minHeight       = 28;
        badgeLE.preferredHeight = 28;
        var badgeHLG = badgeGO.AddComponent<HorizontalLayoutGroup>();
        badgeHLG.childAlignment         = TextAnchor.MiddleCenter;
        badgeHLG.childForceExpandWidth  = true;
        badgeHLG.childForceExpandHeight = true;

        var badgeLabelGO = new GameObject("BadgeLabel", typeof(RectTransform));
        badgeLabelGO.transform.SetParent(badgeGO.transform, false);
        var badgeTMP = badgeLabelGO.AddComponent<TextMeshProUGUI>();
        badgeTMP.fontSize  = 13;
        badgeTMP.color     = new Color(0.5f, 0.85f, 1f);
        badgeTMP.alignment = TextAlignmentOptions.Center;

        // Wire FileEntryItemUI
        var ui = root.AddComponent<FileEntryItemUI>();
        ui.button        = btn;
        ui.fileNameLabel = nameTMP;
        ui.badgeLabel    = badgeTMP;
        ui.badgeImage    = badgeImg;

        var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        return prefab.GetComponent<FileEntryItemUI>();
    }

    /// <summary>
    /// UrlHistoryItem prefab:
    ///   Root (Image bg, Button, LayoutElement, HorizontalLayoutGroup)
    ///     UrlText (TMP_Text, ellipsis overflow)
    /// </summary>
    static UrlHistoryItemUI CreateUrlHistoryPrefab()
    {
        string path = PrefabFolder + "/UrlHistoryItem.prefab";

        var root    = new GameObject("UrlHistoryItem", typeof(RectTransform));
        var rootImg = root.AddComponent<Image>();
        rootImg.color = new Color(1f, 1f, 1f, 0.05f);

        var le = root.AddComponent<LayoutElement>();
        le.minHeight       = 46;
        le.preferredHeight = 46;
        le.flexibleWidth   = 1;

        var btn  = root.AddComponent<Button>();
        btn.targetGraphic = rootImg;
        var cols = btn.colors;
        cols.normalColor      = new Color(1f, 1f, 1f, 0.05f);
        cols.highlightedColor = new Color(0.7f, 0.9f, 1f, 0.22f);
        cols.pressedColor     = new Color(0.4f, 0.7f, 1f, 0.40f);
        cols.selectedColor    = new Color(0.7f, 0.9f, 1f, 0.22f);
        btn.colors = cols;

        var hlg = root.AddComponent<HorizontalLayoutGroup>();
        hlg.padding                = new RectOffset(12, 12, 0, 0);
        hlg.childAlignment         = TextAnchor.MiddleLeft;
        hlg.childForceExpandWidth  = true;
        hlg.childForceExpandHeight = true;

        var textGO = new GameObject("UrlText", typeof(RectTransform));
        textGO.transform.SetParent(root.transform, false);
        var tmp = textGO.AddComponent<TextMeshProUGUI>();
        tmp.fontSize     = 14;
        tmp.color        = new Color(0.75f, 0.92f, 1f);
        tmp.alignment    = TextAlignmentOptions.MidlineLeft;
        tmp.overflowMode = TextOverflowModes.Ellipsis;

        var ui = root.AddComponent<UrlHistoryItemUI>();
        ui.button   = btn;
        ui.urlLabel = tmp;

        var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        return prefab.GetComponent<UrlHistoryItemUI>();
    }
}
