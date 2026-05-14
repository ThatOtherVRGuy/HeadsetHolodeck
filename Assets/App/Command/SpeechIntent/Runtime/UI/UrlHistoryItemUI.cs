// Assets/App/Command/SpeechIntent/Runtime/UI/UrlHistoryItemUI.cs

using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace SpeechIntent
{
    /// <summary>
    /// Component on the UrlHistoryItem prefab. Exposes the sub-components that
    /// <see cref="UrlLoaderPanel"/> needs to configure each history row.
    /// </summary>
    public class UrlHistoryItemUI : MonoBehaviour
    {
        public TMP_Text urlLabel;
        public Button   button;

        /// <summary>Configure this item and wire its click action.</summary>
        public void SetData(string url, UnityAction onClick)
        {
            if (urlLabel != null) urlLabel.text = url;
            if (button   != null)
            {
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(onClick);
            }
        }
    }
}
