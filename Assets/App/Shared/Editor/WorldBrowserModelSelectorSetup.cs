using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using WorldLabs.Runtime;

namespace Holodeck.Editor
{
    /// <summary>
    /// Adds the model-selector row and model label to the WorldLabs_GUI prefab.
    ///
    /// The prefab uses a pre-built static hierarchy so BuildUI() is skipped at runtime.
    /// This script adds the missing UI elements directly to the prefab asset:
    ///   - "ModelSelectorRow": HLG with Draft / Fast / Standard / High buttons inside Create Panel
    ///   - "ModelLabel": small "Model: Standard" text at the top of Create Panel
    ///
    /// Also shifts the existing "World Prompt" input and "Create World" button down 30px
    /// to make room.
    ///
    /// Re-running is safe — existing ModelSelectorRow / ModelLabel are replaced.
    ///
    /// Menu: Holodeck > Setup Model Selector UI
    /// </summary>
    public static class WorldBrowserModelSelectorSetup
    {
        private const string PrefabPath =
            "Assets/Samples/WorldLabs Gaussian Splatting/1.1.0/Sensai Sample/Prefabs/WorldLabs_GUI.prefab";

        [MenuItem("Holodeck/Setup Model Selector UI")]
        public static void SetupModelSelectorUI()
        {
            // ── Load prefab for editing ───────────────────────────────────────
            GameObject prefabRoot = PrefabUtility.LoadPrefabContents(PrefabPath);
            if (prefabRoot == null)
            {
                Debug.LogError($"[WorldBrowserModelSelectorSetup] Could not load prefab at {PrefabPath}");
                return;
            }

            try
            {
                WorldBrowserController browser = prefabRoot.GetComponent<WorldBrowserController>();
                if (browser == null)
                {
                    Debug.LogError("[WorldBrowserModelSelectorSetup] WorldBrowserController not found on prefab root.");
                    return;
                }

                // ── Find Create Panel ─────────────────────────────────────────
                Transform createPanel = prefabRoot.transform.Find("Create Panel");
                if (createPanel == null)
                {
                    Debug.LogError("[WorldBrowserModelSelectorSetup] 'Create Panel' not found.");
                    return;
                }

                // ── Shift existing children down to make room ─────────────────
                // World Prompt input: currently at (0, 0) → move to (0, -30)
                Transform worldPrompt = createPanel.Find("World Prompt");
                if (worldPrompt != null)
                {
                    var rt = worldPrompt.GetComponent<RectTransform>();
                    rt.anchoredPosition = new Vector2(0f, -30f);
                }
                else
                    Debug.LogWarning("[WorldBrowserModelSelectorSetup] 'World Prompt' not found in Create Panel.");

                // Create World button: currently at (225, -175) → move to (225, -210)
                Transform createWorldBtn = createPanel.Find("Create World");
                if (createWorldBtn != null)
                {
                    var rt = createWorldBtn.GetComponent<RectTransform>();
                    rt.anchoredPosition = new Vector2(225f, -210f);
                }
                else
                    Debug.LogWarning("[WorldBrowserModelSelectorSetup] 'Create World' not found in Create Panel.");

                // ── Remove stale ModelSelectorRow / ModelLabel if re-running ──
                DestroyChildIfExists(createPanel, "ModelSelectorRow");
                DestroyChildIfExists(createPanel, "ModelLabel");

                // ── Add ModelLabel (small header above selector row) ──────────
                var labelGo = new GameObject("ModelLabel", typeof(RectTransform), typeof(Text));
                labelGo.transform.SetParent(createPanel, false);
                var labelRt = labelGo.GetComponent<RectTransform>();
                labelRt.anchorMin        = new Vector2(0.5f, 0.5f);
                labelRt.anchorMax        = new Vector2(0.5f, 0.5f);
                labelRt.anchoredPosition = new Vector2(0f, 148f);
                labelRt.sizeDelta        = new Vector2(540f, 22f);
                var labelText = labelGo.GetComponent<Text>();
                labelText.font          = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                labelText.fontSize      = 11;
                labelText.color         = new Color(0.45f, 0.70f, 1.00f, 1f);
                labelText.alignment     = TextAnchor.MiddleRight;
                labelText.text          = "Model: Standard";
                labelText.raycastTarget = false;
                browser.currentModelLabel = labelText;

                // ── Add ModelSelectorRow ──────────────────────────────────────
                var rowGo = new GameObject("ModelSelectorRow", typeof(RectTransform));
                rowGo.transform.SetParent(createPanel, false);
                var rowRt = rowGo.GetComponent<RectTransform>();
                rowRt.anchorMin        = new Vector2(0.5f, 0.5f);
                rowRt.anchorMax        = new Vector2(0.5f, 0.5f);
                rowRt.anchoredPosition = new Vector2(0f, 115f);
                rowRt.sizeDelta        = new Vector2(540f, 42f);

                var hlg = rowGo.AddComponent<HorizontalLayoutGroup>();
                hlg.spacing                = 6f;
                hlg.childForceExpandWidth  = true;
                hlg.childForceExpandHeight = true;
                hlg.padding                = new RectOffset(0, 0, 0, 0);

                browser.modelSelectorRow = rowGo;

                // ── Build the four model buttons ──────────────────────────────
                string[] labels = { "Draft", "Fast", "Standard", "High" };

                for (int i = 0; i < labels.Length; i++)
                {
                    var btnGo = new GameObject(labels[i], typeof(RectTransform));
                    btnGo.transform.SetParent(rowGo.transform, false);
                    btnGo.AddComponent<LayoutElement>();

                    var img = btnGo.AddComponent<Image>();
                    // Standard gets active color; others get inactive — SetGenerationModel
                    // will re-sync at runtime, this just sets a sensible initial state.
                    img.color = i == 2   // Standard == index 2
                        ? new Color(0.25f, 0.55f, 1.00f, 1f)
                        : new Color(0.18f, 0.20f, 0.28f, 1f);

                    var btn = btnGo.AddComponent<Button>();
                    btn.targetGraphic = img;
                    btn.transition    = Selectable.Transition.ColorTint;
                    var colors = btn.colors;
                    colors.highlightedColor = new Color(0.40f, 0.65f, 1.00f, 1f);
                    colors.pressedColor     = new Color(0.15f, 0.38f, 0.80f, 1f);
                    btn.colors     = colors;
                    btn.navigation = new Navigation { mode = Navigation.Mode.None };

                    var textGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
                    textGo.transform.SetParent(btnGo.transform, false);
                    var trt = textGo.GetComponent<RectTransform>();
                    trt.anchorMin = Vector2.zero;
                    trt.anchorMax = Vector2.one;
                    trt.offsetMin = trt.offsetMax = Vector2.zero;
                    var txt = textGo.GetComponent<Text>();
                    txt.font          = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                    txt.fontSize      = 11;
                    txt.color         = Color.white;
                    txt.alignment     = TextAnchor.MiddleCenter;
                    txt.text          = labels[i];
                    txt.raycastTarget = false;

                    // onClick is wired at runtime by WorldBrowserController.Awake() — we can't
                    // capture MarbleModel enum values as persistent listeners from an Editor script,
                    // so the buttons use a runtime AddListener instead (already done in Awake guard).
                    // NOTE: Add the runtime wiring in Awake if not already present — see below.
                }

                // ── Save prefab ───────────────────────────────────────────────
                PrefabUtility.SaveAsPrefabAsset(prefabRoot, PrefabPath);
                Debug.Log("[WorldBrowserModelSelectorSetup] Done. ModelSelectorRow and ModelLabel added to WorldLabs_GUI prefab.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }

        private static void DestroyChildIfExists(Transform parent, string childName)
        {
            Transform existing = parent.Find(childName);
            if (existing != null)
                Object.DestroyImmediate(existing.gameObject);
        }
    }
}
