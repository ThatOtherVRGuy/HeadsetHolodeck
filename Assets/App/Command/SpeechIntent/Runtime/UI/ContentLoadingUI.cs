// Assets/App/Command/SpeechIntent/Runtime/UI/ContentLoadingUI.cs

using UnityEngine;
using UnityEngine.UI;

namespace SpeechIntent
{
    /// <summary>
    /// Tab controller for the Content Loading UI.
    /// Toggles between the Local Files panel and the Remote URLs panel.
    /// Wire the tab buttons' onClick to <see cref="ShowLocalFiles"/> and <see cref="ShowUrls"/>.
    /// </summary>
    public class ContentLoadingUI : MonoBehaviour
    {
        [Header("Panels")]
        public GameObject localFilesPanel;
        public GameObject urlsPanel;

        [Header("Tab Buttons")]
        public Button localFilesTab;
        public Button urlsTab;

        static readonly Color TabActive   = new Color(0.25f, 0.55f, 1.00f, 1f);
        static readonly Color TabInactive = new Color(0.18f, 0.18f, 0.22f, 1f);

        void Start() => ShowLocalFiles();

        public void Refresh()
        {
            if (localFilesPanel != null && localFilesPanel.activeInHierarchy)
                localFilesPanel.SendMessage("Refresh", SendMessageOptions.DontRequireReceiver);
            if (urlsPanel != null && urlsPanel.activeInHierarchy)
                urlsPanel.SendMessage("RefreshHistory", SendMessageOptions.DontRequireReceiver);
        }

        public void ShowLocalFiles()
        {
            SetPanelActive(localFilesPanel, true);
            SetPanelActive(urlsPanel, false);
            SetTabColor(localFilesTab, TabActive);
            SetTabColor(urlsTab, TabInactive);
        }

        public void ShowUrls()
        {
            SetPanelActive(localFilesPanel, false);
            SetPanelActive(urlsPanel, true);
            SetTabColor(localFilesTab, TabInactive);
            SetTabColor(urlsTab, TabActive);
        }

        static void SetPanelActive(GameObject go, bool active)
        {
            if (go != null) go.SetActive(active);
        }

        static void SetTabColor(Button btn, Color color)
        {
            if (btn == null) return;
            var img = btn.GetComponent<Image>();
            if (img != null) img.color = color;
        }
    }
}
