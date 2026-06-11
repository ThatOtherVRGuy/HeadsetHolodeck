using Holodeck.Direct;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace SpeechIntent
{
    public sealed class CachedObjectCardUI : MonoBehaviour
    {
        public TMP_Text nameLabel;
        public TMP_Text providerLabel;
        public TMP_Text dateLabel;
        public RawImage thumbnail;
        public Button useButton;
        public Button deleteButton;
        public Button renameButton;

        CachedObjectRecord _record;

        public CachedObjectRecord Record => _record;

        public void SetData(
            CachedObjectRecord record,
            Texture thumbnailTexture,
            UnityAction onUse,
            UnityAction onDelete,
            UnityAction onRename)
        {
            _record = record;

            if (nameLabel != null)
                nameLabel.text = FirstNonEmpty(record?.canonical_name, "Cached Object");
            if (providerLabel != null)
                providerLabel.text = FirstNonEmpty(record?.provider, "Unknown Provider");
            if (dateLabel != null)
                dateLabel.text = FirstNonEmpty(record?.modified_at, record?.created_at);

            SetThumbnail(thumbnailTexture);
            WireButton(useButton, onUse);
            WireButton(deleteButton, onDelete);
            WireButton(renameButton, onRename);
        }

        public void SetThumbnail(Texture texture)
        {
            if (thumbnail == null)
                return;

            thumbnail.texture = texture;
            thumbnail.gameObject.SetActive(texture != null);
        }

        static void WireButton(Button button, UnityAction action)
        {
            if (button == null)
                return;

            button.onClick.RemoveAllListeners();
            if (action != null)
                button.onClick.AddListener(action);
        }

        static string FirstNonEmpty(params string[] values)
        {
            foreach (string value in values)
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            return "";
        }
    }
}
