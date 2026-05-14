// Assets/App/Command/SpeechIntent/Runtime/UI/FileEntryItemUI.cs

using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace SpeechIntent
{
    /// <summary>
    /// Component on the FileEntryItem prefab. Exposes the sub-components that
    /// <see cref="LocalFileBrowserPanel"/> needs to configure each list row.
    /// </summary>
    public class FileEntryItemUI : MonoBehaviour
    {
        public TMP_Text fileNameLabel;
        public TMP_Text badgeLabel;
        public Image    badgeImage;
        public Button   button;

        /// <summary>Configure this item and wire its click action.</summary>
        public void SetData(string fileName, string badge, Color badgeColor, UnityAction onClick)
        {
            if (fileNameLabel != null) fileNameLabel.text  = fileName;
            if (badgeLabel    != null) badgeLabel.text     = badge;
            if (badgeImage    != null) badgeImage.color    = new Color(badgeColor.r, badgeColor.g, badgeColor.b, 0.25f);
            if (badgeLabel    != null) badgeLabel.color    = badgeColor;
            if (button        != null)
            {
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(onClick);
            }
        }
    }
}
