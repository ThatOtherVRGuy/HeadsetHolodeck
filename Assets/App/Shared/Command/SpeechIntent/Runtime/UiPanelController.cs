using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpeechIntent
{
    public class UiPanelController : MonoBehaviour
    {
        [Serializable]
        public class PanelEntry
        {
            public string key;
            public GameObject root;
        }

        public List<PanelEntry> panels = new List<PanelEntry>();

        [Header("Startup")]
        [Tooltip("Keeps registered panels inactive during boot, then shows the default panel after a short delay.")]
        public bool deferStartupPanels = true;
        [Tooltip("Panel key to show after the startup delay. Leave empty to keep all panels hidden.")]
        public string defaultPanelKey = "my_worlds";
        [Tooltip("Frames to wait before showing the default panel. This lets XR/world startup finish first.")]
        [Min(0)] public int startupDelayFrames = 20;
        [Tooltip("Panel keys that should stay active while child panels are deferred. Use this for the Arch/container itself.")]
        public List<string> alwaysVisibleKeys = new List<string> { "arch_menu" };
        [Tooltip("When showing a panel, hide the other registered panels first.")]
        public bool showPanelsExclusively = true;
        [Tooltip("Calls Refresh on the shown panel one frame after it is enabled, when supported.")]
        public bool refreshShownPanel = true;

        void Awake()
        {
            if (!deferStartupPanels)
                return;

            EnsureAlwaysVisiblePanelsActive();
            HideAll();
            if (!string.IsNullOrWhiteSpace(defaultPanelKey))
                StartCoroutine(ShowDefaultAfterStartupDelay());
        }

        System.Collections.IEnumerator ShowDefaultAfterStartupDelay()
        {
            int frames = Mathf.Max(0, startupDelayFrames);
            for (int i = 0; i < frames; i++)
                yield return null;

            Show(defaultPanelKey);
        }

        public void Show(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                Debug.LogWarning("UI show requested with an empty key.");
                return;
            }

            string normalizedKey = NormalizeKey(key);
            if (IsHideRequest(key, out string hideKey))
            {
                Hide(hideKey);
                return;
            }

            bool found = false;
            if (showPanelsExclusively)
                SetOtherPanelsInactive(normalizedKey);

            foreach (PanelEntry panel in panels)
            {
                if (panel?.root == null)
                {
                    continue;
                }

                bool shouldShow = string.Equals(NormalizeKey(panel.key), normalizedKey, StringComparison.OrdinalIgnoreCase);
                if (shouldShow)
                {
                    Debug.Log($"Showing UI panel with key '{normalizedKey}'.");
                    panel.root.SetActive(true);
                    if (refreshShownPanel)
                        StartCoroutine(RefreshPanelNextFrame(panel.root));
                    found = true;
                }
            }

            if (!found)
            {
                Debug.LogWarning($"No UI panel matched key '{normalizedKey}'.");
            }
        }

        System.Collections.IEnumerator RefreshPanelNextFrame(GameObject panelRoot)
        {
            yield return null;
            if (panelRoot != null && panelRoot.activeInHierarchy)
                panelRoot.SendMessage("Refresh", SendMessageOptions.DontRequireReceiver);
        }

        public void Hide(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                Debug.LogWarning("UI hide requested with an empty key.");
                return;
            }

            string normalizedKey = NormalizeKey(key);
            bool found = false;
            foreach (PanelEntry panel in panels)
            {
                if (panel?.root == null)
                {
                    continue;
                }

                bool shouldHide = string.Equals(NormalizeKey(panel.key), normalizedKey, StringComparison.OrdinalIgnoreCase);
                if (shouldHide)
                {
                    Debug.Log($"Hiding UI panel with key '{normalizedKey}'.");
                    panel.root.SetActive(false);
                    found = true;
                }
            }

            if (!found)
            {
                Debug.LogWarning($"No UI panel matched key '{normalizedKey}'.");
            }
        }

        public void HideAll()
        {
            foreach (PanelEntry panel in panels)
            {
                if (panel?.root != null)
                {
                    if (panel.root == gameObject || IsAlwaysVisible(panel.key))
                    {
                        continue;
                    }

                    panel.root.SetActive(false);
                }
            }
        }

        void SetOtherPanelsInactive(string normalizedKeyToKeep)
        {
            foreach (PanelEntry panel in panels)
            {
                if (panel?.root == null || panel.root == gameObject || IsAlwaysVisible(panel.key))
                    continue;

                bool keep = string.Equals(NormalizeKey(panel.key), normalizedKeyToKeep, StringComparison.OrdinalIgnoreCase);
                if (!keep && panel.root.activeSelf)
                    panel.root.SetActive(false);
            }
        }

        void EnsureAlwaysVisiblePanelsActive()
        {
            foreach (PanelEntry panel in panels)
            {
                if (panel?.root != null && IsAlwaysVisible(panel.key))
                    panel.root.SetActive(true);
            }
        }

        bool IsAlwaysVisible(string key)
        {
            string normalized = NormalizeKey(key);
            if ((alwaysVisibleKeys == null || alwaysVisibleKeys.Count == 0) && normalized == "arch_menu")
                return true;

            foreach (string alwaysVisibleKey in alwaysVisibleKeys)
            {
                if (string.Equals(NormalizeKey(alwaysVisibleKey), normalized, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        static bool IsHideRequest(string key, out string targetKey)
        {
            string value = (key ?? string.Empty).Trim();
            if (value.StartsWith("hide_", StringComparison.OrdinalIgnoreCase))
            {
                targetKey = value.Substring("hide_".Length);
                return true;
            }

            if (value.StartsWith("hide:", StringComparison.OrdinalIgnoreCase))
            {
                targetKey = value.Substring("hide:".Length);
                return true;
            }

            targetKey = value;
            return false;
        }

        static string NormalizeKey(string key)
        {
            string value = (key ?? string.Empty).Trim().ToLowerInvariant();
            value = value.Replace('-', '_').Replace(' ', '_');

            return value switch
            {
                "arch" or "menu" or "exit" or "arch_menu" => "arch_menu",
                "my_worlds" or "worlds" => "my_worlds",
                _ => value
            };
        }
    }
}
