// Assets/App/Command/SpeechIntent/Runtime/UI/WorldConfigCardUI.cs
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace SpeechIntent
{
    /// <summary>Card component for a single WorldConfig entry in MyWorldsPanel.</summary>
    public class WorldConfigCardUI : MonoBehaviour
    {
        public TMP_Text nameLabel;
        public TMP_Text sourceLabel;   // e.g. "WorldLabs", "Local Splat"
        public TMP_Text dateLabel;
        public RawImage thumbnail;     // pano preview if available
        public Button   loadButton;
        public Button   saveAsButton;
        public Button   deleteButton;

        public void SetData(
            string displayName,
            string sourceType,
            string modifiedAt,
            Texture2D thumb,
            UnityAction onLoad,
            UnityAction onSaveAs,
            UnityAction onDelete)
        {
            if (nameLabel   != null) nameLabel.text   = displayName ?? "";
            if (sourceLabel != null) sourceLabel.text = FormatSourceType(sourceType);
            if (dateLabel   != null) dateLabel.text   = modifiedAt  ?? "";
            if (thumbnail   != null)
            {
                SetThumbnail(thumb);
            }

            WireButton(loadButton,   onLoad);
            WireButton(saveAsButton, onSaveAs);
            WireButton(deleteButton, onDelete);
        }

        public void SetThumbnail(Texture texture)
        {
            if (thumbnail == null) return;
            thumbnail.texture = texture;
            thumbnail.gameObject.SetActive(texture != null);
        }

        static void WireButton(Button btn, UnityAction action)
        {
            if (btn == null) return;
            btn.onClick.RemoveAllListeners();
            if (action != null) btn.onClick.AddListener(action);
        }

        static string FormatSourceType(string type) => type switch
        {
            "worldlabs"   => "WorldLabs",
            "local_splat" => "Local Splat",
            "local_pano"  => "Local Pano",
            "url"         => "URL",
            _             => type ?? ""
        };
    }
}
