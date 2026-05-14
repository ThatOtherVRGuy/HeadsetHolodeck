using Holodeck.Save;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using SpeechIntent;
using WorldLabs.Runtime;

namespace Holodeck.Editor
{
    public static class ArchLcarsUiSetup
    {
        const float CanvasSize = 1000f;
        const float CanvasScale = 0.001f;
        const string RootName = "ArchLCARS";

        static readonly Color Black = new Color(0.005f, 0.005f, 0.006f, 0.96f);
        static readonly Color Orange = new Color(1.00f, 0.55f, 0.07f, 1f);
        static readonly Color Gold = new Color(1.00f, 0.78f, 0.08f, 1f);
        static readonly Color Amber = new Color(0.90f, 0.38f, 0.05f, 1f);
        static readonly Color Lavender = new Color(0.66f, 0.52f, 0.94f, 1f);
        static readonly Color Blue = new Color(0.48f, 0.55f, 1.00f, 1f);
        static readonly Color Pale = new Color(0.78f, 0.72f, 0.88f, 1f);
        static readonly Color Text = new Color(0.94f, 0.90f, 0.82f, 1f);

        [MenuItem("Holodeck/Setup LCARS Arch UI")]
        public static void Setup()
        {
            GameObject root = FindSceneObject(RootName);
            bool createdRoot = root == null;
            if (createdRoot)
                root = CreateDefaultRoot();

            Canvas left = EnsurePillarCanvas(root.transform, "LeftPillar_Operations", new Vector3(-0.65f, 1.55f, -0.04f));
            Canvas right = EnsurePillarCanvas(root.transform, "RightPillar_Info", new Vector3(0.65f, 1.55f, -0.04f));

            BuildOperationsCanvas(left);
            BuildInfoCanvas(right);
            BuildCrossbeam(root.transform);

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log(createdRoot
                ? "[ArchLcarsUiSetup] Created ArchLCARS and built LCARS arch UI."
                : "[ArchLcarsUiSetup] Found existing ArchLCARS in the scene and rebuilt LCARS arch UI in place.");
        }

        static GameObject CreateDefaultRoot()
        {
            Transform parent =
                GameObject.Find("Holodeck/Environment/Arch")?.transform
                ?? GameObject.Find("Environment/Arch")?.transform
                ?? FindSceneObject("Arch")?.transform
                ?? GameObject.Find("Holodeck/Environment/TNGHolodeck")?.transform
                ?? GameObject.Find("Environment/TNGHolodeck")?.transform
                ?? GameObject.Find("TNGHolodeck")?.transform
                ?? EnsurePath("UI").transform;

            GameObject root = EnsureChild(parent, RootName);
            Undo.RecordObject(root.transform, "Place LCARS UI Root");
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;
            return root;
        }

        static Canvas EnsurePillarCanvas(Transform parent, string name, Vector3 localPosition)
        {
            GameObject go = EnsureChild(parent, name);
            Undo.RecordObject(go.transform, "Place LCARS Pillar Canvas");
            go.transform.localPosition = localPosition;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one * CanvasScale;

            Canvas canvas = GetOrAdd<Canvas>(go);
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = Camera.main;

            CanvasScaler scaler = GetOrAdd<CanvasScaler>(go);
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;

            GetOrAdd<GraphicRaycaster>(go);

            RectTransform rt = GetOrAdd<RectTransform>(go);
            rt.sizeDelta = new Vector2(CanvasSize, CanvasSize);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);

            Image bg = GetOrAdd<Image>(go);
            bg.color = Black;
            return canvas;
        }

        static void BuildOperationsCanvas(Canvas canvas)
        {
            ClearGeneratedChildren(canvas.transform);

            RectTransform root = canvas.GetComponent<RectTransform>();
            MakeBand(root, "TopBlock", new Vector2(225, 880), new Vector2(420, 120), Orange);
            MakeBand(root, "TopHeader", new Vector2(650, 910), new Vector2(500, 48), Gold);
            MakeLabel(root, "Title", "HOLODECK OPS", 46, new Vector2(610, 910), new Vector2(480, 52), Gold, FontStyles.Bold);
            MakeBand(root, "LowerHook", new Vector2(235, 65), new Vector2(430, 78), Orange);
            MakeBand(root, "BottomRail", new Vector2(690, 55), new Vector2(480, 44), Gold);

            ArchOperationsPanel ops = GetOrAdd<ArchOperationsPanel>(canvas.gameObject);
            RectTransform host = MakePanelHost(root, "OperationsHost", new Vector2(548, 492), new Vector2(660, 640));

            GameObject worldLabs = FindSceneObject("WorldLabs_GUI");
            GameObject myWorlds = FindSceneObject("MyWorldsPanel");
            GameObject content = FindSceneObject("ContentLoadingCanvas");
            GameObject cameraCapture = FindSceneObject("HeadsetCameraPreviewPanel");
            GameObject imageSearch = FindSceneObject("ImageSearchPanel");

            ops.worldLabsPanel = EmbedExistingPanel(worldLabs, host, "WorldLabs_GUI");
            ops.myWorldsPanel = EmbedExistingPanel(myWorlds, host, "MyWorldsPanel");
            ops.contentLoadingPanel = EmbedExistingPanel(content, host, "ContentLoadingCanvas");
            ops.cameraCapturePanel = EmbedExistingPanel(cameraCapture, host, "HeadsetCameraPreviewPanel");
            ops.imageSearchPanel = EmbedExistingPanel(imageSearch, host, "ImageSearchPanel");
            ops.defaultPanel = ArchOperationsPanel.DefaultPanel.MyWorlds;

            MakeOpsButton(root, "WORLDLABS", new Vector2(160, 720), ops.ShowWorldLabs, Orange);
            MakeOpsButton(root, "MY WORLDS", new Vector2(160, 645), ops.ShowMyWorlds, Gold);
            MakeOpsButton(root, "FILES/URL", new Vector2(160, 570), ops.ShowContentLoading, Lavender);
            MakeOpsButton(root, "CAMERA", new Vector2(160, 495), ops.ShowCameraCapture, Blue);
            MakeOpsButton(root, "IMAGE", new Vector2(160, 420), ops.ShowImageSearch, Lavender);
            MakeOpsButton(root, "CREATE", new Vector2(160, 220), ops.ShowWorldLabs, Amber);
            MakeOpsButton(root, "LOAD", new Vector2(160, 145), ops.ShowMyWorlds, Blue);

            ops.ShowDefault();
            EditorUtility.SetDirty(ops);
        }

        static void BuildInfoCanvas(Canvas canvas)
        {
            ClearGeneratedChildren(canvas.transform);

            RectTransform root = canvas.GetComponent<RectTransform>();
            MakeBand(root, "TopBlock", new Vector2(220, 880), new Vector2(390, 120), Orange);
            MakeBand(root, "TopHeader", new Vector2(665, 910), new Vector2(465, 48), Gold);
            MakeLabel(root, "Title", "WORLD STATUS", 46, new Vector2(620, 910), new Vector2(500, 52), Gold, FontStyles.Bold);
            MakeBand(root, "RightSpine", new Vector2(890, 510), new Vector2(78, 500), Amber);
            MakeBand(root, "BottomRail", new Vector2(690, 55), new Vector2(480, 44), Gold);

            ArchWorldInfoPanel info = GetOrAdd<ArchWorldInfoPanel>(canvas.gameObject);
            info.worldConfigAutoSave = Object.FindFirstObjectByType<WorldConfigAutoSave>();
            info.worldConfigStore = Object.FindFirstObjectByType<WorldConfigStore>();
            info.worldManager = Object.FindFirstObjectByType<WorldLabsWorldManager>();

            info.worldNameLabel = MakeLabel(root, "WorldName", "NO WORLD LOADED", 38, new Vector2(475, 790), new Vector2(650, 64), Text, FontStyles.Bold);
            info.datesLabel = MakeDataRow(root, "Dates", "CREATED --   MODIFIED --", 720);
            info.sourceLabel = MakeDataRow(root, "Source", "SOURCE STATIC WORLD", 670);
            info.sizeLabel = MakeDataRow(root, "Disk", "CONFIG 0 B   CACHED 0 B   TOTAL 0 B", 620);
            info.costLabel = MakeDataRow(root, "Costs", "API COST 0", 570);
            info.statusLabel = MakeDataRow(root, "Status", "NO WORLD LOADED", 520);

            MakeBand(root, "AttributionHeader", new Vector2(350, 455), new Vector2(520, 34), Lavender);
            MakeLabel(root, "AttributionHeaderText", "ATTRIBUTION", 22, new Vector2(350, 455), new Vector2(500, 34), Black, FontStyles.Bold);
            info.attributionLabel = MakeLabel(root, "Attribution", "ATTRIBUTION\nmodel by Set Blueprint Archive", 17,
                new Vector2(455, 300), new Vector2(700, 270), Text, FontStyles.Normal);
            info.attributionLabel.alignment = TextAlignmentOptions.TopLeft;
            info.attributionLabel.enableWordWrapping = true;
            info.attributionLabel.overflowMode = TextOverflowModes.Ellipsis;

            info.Refresh();
            EditorUtility.SetDirty(info);
        }

        static void BuildCrossbeam(Transform parent)
        {
            GameObject go = EnsureChild(parent, "Crossbeam_Status");
            go.transform.localPosition = new Vector3(0f, 2.2f, -0.04f);
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one * CanvasScale;

            Canvas canvas = GetOrAdd<Canvas>(go);
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = Camera.main;
            GetOrAdd<CanvasScaler>(go).uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            GetOrAdd<GraphicRaycaster>(go);

            RectTransform rt = GetOrAdd<RectTransform>(go);
            rt.sizeDelta = new Vector2(1600, 210);
            GetOrAdd<Image>(go).color = Black;
            ClearGeneratedChildren(go.transform);

            RectTransform leftCap = MakeBand(rt, "LeftCap", new Vector2(190, 130), new Vector2(360, 80), Orange);
            MakeBand(rt, "CenterRail", new Vector2(810, 130), new Vector2(680, 44), Gold);
            MakeBand(rt, "RightCap", new Vector2(1410, 130), new Vector2(260, 80), Orange);
            MakeLabel(rt, "Title", "HOLODECK PROGRAMMING", 48, new Vector2(810, 130), new Vector2(720, 68), Gold, FontStyles.Bold);

            TMP_Text modeLabel = MakeLabel(leftCap, "ModeLabel", "READY", 28, Vector2.zero, leftCap.sizeDelta, Black, FontStyles.Bold);
            TMP_Text healthLabel = MakeLabel(rt, "Health", "00:00:00  RUN 0:00:00  WORLD --:--:--", 18, new Vector2(1320, 130), new Vector2(420, 44), Black, FontStyles.Bold);

            RectTransform ticker = MakeBand(rt, "TickerViewport", new Vector2(810, 62), new Vector2(1060, 44), new Color(0.01f, 0.01f, 0.012f, 1f));
            GetOrAdd<RectMask2D>(ticker.gameObject).padding = Vector4.zero;
            TMP_Text messageLabel = MakeTickerLabel(ticker, "Message", "Holodeck systems standing by.", Text);
            TMP_Text messageWrapLabel = MakeTickerLabel(ticker, "MessageWrap", "Holodeck systems standing by.", Text);
            messageWrapLabel.gameObject.SetActive(false);

            ArchCrossbeamStatusPanel status = GetOrAdd<ArchCrossbeamStatusPanel>(go);
            status.modeLabel = modeLabel;
            status.messageLabel = messageLabel;
            status.messageWrapLabel = messageWrapLabel;
            status.healthLabel = healthLabel;
            status.worldManager = Object.FindFirstObjectByType<WorldLabsWorldManager>(FindObjectsInactive.Include);
            status.scrollViewport = ticker;
            status.messageTransform = messageLabel.GetComponent<RectTransform>();
            status.messageWrapTransform = messageWrapLabel.GetComponent<RectTransform>();
            status.statusColorTarget = leftCap.GetComponent<Image>();
            status.flashTarget = leftCap.GetComponent<Image>();
            status.flashErrors = true;
            status.errorFlashesPerSecond = 2f;
            status.ApplyStatus(ArchStatusBus.LastMessage);
            EditorUtility.SetDirty(status);
        }

        static TMP_Text MakeDataRow(RectTransform parent, string name, string text, float y)
        {
            MakeBand(parent, name + "Key", new Vector2(145, y), new Vector2(210, 36), Orange);
            return MakeLabel(parent, name, text, 20, new Vector2(500, y), new Vector2(610, 36), Text, FontStyles.Normal);
        }

        static TMP_Text MakeTickerLabel(RectTransform parent, string name, string text, Color color)
        {
            GameObject go = EnsureChild(parent, name);
            RectTransform rt = GetOrAdd<RectTransform>(go);
            rt.anchorMin = new Vector2(0f, 0.5f);
            rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = parent.sizeDelta;

            TMP_Text tmp = GetOrAdd<TextMeshProUGUI>(go);
            tmp.text = text;
            tmp.fontSize = 22f;
            tmp.fontStyle = FontStyles.Normal;
            tmp.color = color;
            tmp.alignment = TextAlignmentOptions.Left;
            tmp.raycastTarget = false;
            tmp.enableWordWrapping = false;
            tmp.overflowMode = TextOverflowModes.Overflow;
            return tmp;
        }

        static void MakeOpsButton(RectTransform parent, string label, Vector2 pos, UnityAction action, Color color)
        {
            Button button = MakeButton(parent, label, label, pos, new Vector2(250, 50), color);
            button.onClick.RemoveAllListeners();
            UnityEventTools.AddPersistentListener(button.onClick, action);
            EditorUtility.SetDirty(button);
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
            MakeLabel(go.GetComponent<RectTransform>(), "Label", label, 22, Vector2.zero, size, Black, FontStyles.Bold);
            return button;
        }

        static RectTransform MakePanelHost(RectTransform parent, string name, Vector2 pos, Vector2 size)
        {
            RectTransform rt = MakeBand(parent, name, pos, size, new Color(0.02f, 0.02f, 0.025f, 1f));
            RectMask2D mask = GetOrAdd<RectMask2D>(rt.gameObject);
            mask.padding = Vector4.zero;
            return rt;
        }

        static GameObject EmbedExistingPanel(GameObject panel, RectTransform host, string label)
        {
            if (panel == null)
            {
                GameObject placeholder = EnsureChild(host, label + "_Missing");
                RectTransform placeholderRt = GetOrAdd<RectTransform>(placeholder);
                placeholderRt.anchorMin = Vector2.zero;
                placeholderRt.anchorMax = Vector2.one;
                placeholderRt.offsetMin = Vector2.zero;
                placeholderRt.offsetMax = Vector2.zero;
                GetOrAdd<Image>(placeholder).color = new Color(0.05f, 0.05f, 0.06f, 1f);
                MakeLabel(placeholderRt, "MissingLabel", label + " NOT FOUND", 24, Vector2.zero, host.sizeDelta, Text, FontStyles.Bold);
                return placeholder;
            }

            Undo.SetTransformParent(panel.transform, host, "Embed Existing Panel In LCARS Host");
            panel.transform.localPosition = Vector3.zero;
            panel.transform.localRotation = Quaternion.identity;
            panel.transform.localScale = Vector3.one;

            RectTransform rt = panel.GetComponent<RectTransform>();
            if (rt != null)
            {
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
                rt.pivot = new Vector2(0.5f, 0.5f);
            }

            return panel;
        }

        static RectTransform MakeBand(RectTransform parent, string name, Vector2 pos, Vector2 size, Color color)
        {
            GameObject go = EnsureChild(parent, name);
            RectTransform rt = GetOrAdd<RectTransform>(go);
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(pos.x - parent.sizeDelta.x * 0.5f, pos.y - parent.sizeDelta.y * 0.5f);
            rt.sizeDelta = size;

            Image image = GetOrAdd<Image>(go);
            image.color = color;
            return rt;
        }

        static TMP_Text MakeLabel(RectTransform parent, string name, string text, float size, Vector2 pos, Vector2 box, Color color, FontStyles style)
        {
            return MakeLabel(parent, name, text, size, pos, box, color, style, HorizontalAlignmentOptions.Center);
        }

        static TMP_Text MakeLabel(RectTransform parent, string name, string text, float size, Vector2 pos, Vector2 box, Color color, FontStyles style, HorizontalAlignmentOptions horizontal)
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
            tmp.alignment = horizontal switch
            {
                HorizontalAlignmentOptions.Left => TextAlignmentOptions.Left,
                HorizontalAlignmentOptions.Right => TextAlignmentOptions.Right,
                _ => TextAlignmentOptions.Center
            };
            tmp.raycastTarget = false;
            tmp.enableWordWrapping = false;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            return tmp;
        }

        static GameObject EnsurePath(string path)
        {
            string[] parts = path.Split('/');
            GameObject current = GameObject.Find(parts[0]);
            if (current == null)
            {
                current = new GameObject(parts[0]);
                Undo.RegisterCreatedObjectUndo(current, "Create " + parts[0]);
            }

            for (int i = 1; i < parts.Length; i++)
                current = EnsureChild(current.transform, parts[i]);
            return current;
        }

        static GameObject FindSceneObject(string name)
        {
            GameObject direct = GameObject.Find(name) ?? GameObject.Find("UI/" + name);
            if (direct != null) return direct;

            foreach (GameObject go in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (go == null || go.name != name) continue;
                if (!go.scene.IsValid()) continue;
                return go;
            }

            return null;
        }

        static GameObject EnsureChild(Transform parent, string name)
        {
            Transform existing = parent.Find(name);
            if (existing != null) return existing.gameObject;

            GameObject child = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(child, "Create " + name);
            child.transform.SetParent(parent, false);
            return child;
        }

        static void ClearGeneratedChildren(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                Transform child = parent.GetChild(i);
                if (child == null) continue;
                if (child.name.Contains("WorldLabs_GUI") ||
                    child.name.Contains("MyWorldsPanel") ||
                    child.name.Contains("ContentLoadingCanvas") ||
                    child.name.Contains("HeadsetCameraPreviewPanel"))
                    continue;
                Undo.DestroyObjectImmediate(child.gameObject);
            }
        }

        static T GetOrAdd<T>(GameObject go) where T : Component
        {
            T existing = go.GetComponent<T>();
            return existing != null ? existing : Undo.AddComponent<T>(go);
        }
    }
}
