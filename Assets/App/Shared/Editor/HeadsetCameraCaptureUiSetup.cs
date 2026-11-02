using Holodeck.Direct;
using SpeechIntent;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Holodeck.Editor
{
    public static class HeadsetCameraCaptureUiSetup
    {
        static readonly Color Black = new Color(0.005f, 0.005f, 0.006f, 0.96f);
        static readonly Color Orange = new Color(1.00f, 0.55f, 0.07f, 1f);
        static readonly Color Gold = new Color(1.00f, 0.78f, 0.08f, 1f);
        static readonly Color Blue = new Color(0.48f, 0.55f, 1.00f, 1f);
        static readonly Color Text = new Color(0.94f, 0.90f, 0.82f, 1f);

        [MenuItem("Holodeck/Setup Headset Camera Capture UI")]
        public static void Setup()
        {
            HeadsetCameraCaptureService service = EnsureCaptureService();
            ArchOperationsPanel ops = Object.FindFirstObjectByType<ArchOperationsPanel>();
            if (ops == null)
            {
                Debug.LogWarning("[HeadsetCameraCaptureUiSetup] ArchOperationsPanel not found. Run Holodeck > Setup LCARS Arch UI first.");
                return;
            }

            RectTransform host = FindOperationsHost(ops.transform);
            if (host == null)
            {
                Debug.LogWarning("[HeadsetCameraCaptureUiSetup] OperationsHost not found under ArchOperationsPanel.");
                return;
            }

            GameObject panel = EnsurePreviewPanel(host, service);
            Undo.RecordObject(ops, "Wire headset camera preview panel");
            ops.cameraCapturePanel = panel;
            EditorUtility.SetDirty(ops);

            EnsureCameraButton(ops);

            WorldActionDispatcher dispatcher = Object.FindFirstObjectByType<WorldActionDispatcher>();
            if (dispatcher != null)
            {
                Undo.RecordObject(dispatcher, "Wire headset camera capture service");
                dispatcher.headsetCameraCapture = service;
                EditorUtility.SetDirty(dispatcher);
            }

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log("[HeadsetCameraCaptureUiSetup] Headset camera capture service and LCARS preview panel are ready.");
        }

        static HeadsetCameraCaptureService EnsureCaptureService()
        {
            HeadsetCameraCaptureService existing = Object.FindFirstObjectByType<HeadsetCameraCaptureService>();
            if (existing != null)
                return existing;

            Transform systems =
                GameObject.Find("Holodeck/Systems")?.transform
                ?? GameObject.Find("Systems")?.transform
                ?? new GameObject("Systems").transform;

            GameObject go = new GameObject("HeadsetCameraCaptureService");
            Undo.RegisterCreatedObjectUndo(go, "Create headset camera capture service");
            go.transform.SetParent(systems, false);
            return Undo.AddComponent<HeadsetCameraCaptureService>(go);
        }

        static RectTransform FindOperationsHost(Transform root)
        {
            Transform found = root.Find("OperationsHost");
            if (found != null)
                return found as RectTransform;

            foreach (RectTransform child in root.GetComponentsInChildren<RectTransform>(true))
                if (child.name == "OperationsHost")
                    return child;
            return null;
        }

        static GameObject EnsurePreviewPanel(RectTransform host, HeadsetCameraCaptureService service)
        {
            GameObject panel = EnsureChild(host, "HeadsetCameraPreviewPanel");
            RectTransform rt = GetOrAdd<RectTransform>(panel);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
            GetOrAdd<Image>(panel).color = new Color(0.015f, 0.015f, 0.018f, 1f);
            ClearChildren(panel.transform);

            HeadsetCameraPreviewPanel preview = GetOrAdd<HeadsetCameraPreviewPanel>(panel);
            preview.captureService = service;
            preview.worldCoordinator = Object.FindFirstObjectByType<VoiceToWorldLabsPluginCoordinator>();

            MakeBand(rt, "Header", new Vector2(330, 595), new Vector2(560, 44), Gold);
            MakeLabel(rt, "Title", "HEADSET CAMERA", 28, new Vector2(330, 595), new Vector2(540, 44), Black, FontStyles.Bold);

            RawImage image = GetOrAdd<RawImage>(EnsureChild(panel.transform, "PreviewImage"));
            RectTransform imageRt = image.GetComponent<RectTransform>();
            imageRt.anchorMin = imageRt.anchorMax = new Vector2(0.5f, 0.5f);
            imageRt.pivot = new Vector2(0.5f, 0.5f);
            imageRt.anchoredPosition = new Vector2(0, 55);
            imageRt.sizeDelta = new Vector2(560, 420);
            image.color = new Color(0f, 0f, 0f, 0.65f);
            preview.previewImage = image;
            AspectRatioFitter aspect = GetOrAdd<AspectRatioFitter>(image.gameObject);
            aspect.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            aspect.aspectRatio = 4f / 3f;
            preview.previewAspect = aspect;

            TMP_Text status = MakeLabel(rt, "Status", "NO CAPTURE", 20, new Vector2(330, 105), new Vector2(560, 34), Text, FontStyles.Normal);
            preview.statusLabel = status;

            Button createWorld = MakeButton(rt, "CreateWorldButton", "CREATE WORLD", new Vector2(95, 45), new Vector2(150, 48), Orange);
            Button createObject = MakeButton(rt, "CreateObjectButton", "CREATE OBJ", new Vector2(255, 45), new Vector2(145, 48), Gold);
            Button recapture = MakeButton(rt, "RecaptureButton", "RE-CAPTURE", new Vector2(405, 45), new Vector2(135, 48), Blue);
            Button confirm = MakeButton(rt, "ConfirmCaptureButton", "SHOOT", new Vector2(535, 45), new Vector2(105, 48), Orange);
            Button cancel = MakeButton(rt, "CancelPreviewButton", "CANCEL", new Vector2(625, 45), new Vector2(95, 48), Blue);
            createWorld.onClick.RemoveAllListeners();
            createObject.onClick.RemoveAllListeners();
            recapture.onClick.RemoveAllListeners();
            confirm.onClick.RemoveAllListeners();
            cancel.onClick.RemoveAllListeners();
            UnityEventTools.AddPersistentListener(createWorld.onClick, preview.CreateWorld);
            UnityEventTools.AddPersistentListener(createObject.onClick, preview.CreateObject);
            UnityEventTools.AddPersistentListener(recapture.onClick, preview.Recapture);
            UnityEventTools.AddPersistentListener(confirm.onClick, preview.ConfirmCapture);
            UnityEventTools.AddPersistentListener(cancel.onClick, preview.CancelPreview);
            preview.createWorldButton = createWorld;
            preview.createObjectButton = createObject;
            preview.recaptureButton = recapture;
            preview.confirmCaptureButton = confirm;
            preview.cancelPreviewButton = cancel;

            EditorUtility.SetDirty(preview);
            return panel;
        }

        static void EnsureCameraButton(ArchOperationsPanel ops)
        {
            RectTransform root = ops.GetComponent<RectTransform>();
            if (root == null)
                return;

            Button button = MakeButton(root, "CAMERA", "CAMERA", new Vector2(160, 495), new Vector2(250, 50), Blue);
            button.onClick.RemoveAllListeners();
            UnityEventTools.AddPersistentListener(button.onClick, ops.ShowCameraCapture);
            EditorUtility.SetDirty(button);
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
            return existing != null ? existing : Undo.AddComponent<T>(go);
        }

        static void ClearChildren(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
                Undo.DestroyObjectImmediate(parent.GetChild(i).gameObject);
        }
    }
}
