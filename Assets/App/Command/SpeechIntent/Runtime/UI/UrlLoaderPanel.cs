// Assets/App/Command/SpeechIntent/Runtime/UI/UrlLoaderPanel.cs

using System.Collections;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SpeechIntent
{
    /// <summary>
    /// URL entry panel: TMP_InputField (supports Quest virtual keyboard) for typing a URL,
    /// a Load button, and a scrollable history list populated by instantiating
    /// <see cref="urlHistoryItemPrefab"/>. Tapping a history row fills the input field.
    /// </summary>
    public class UrlLoaderPanel : MonoBehaviour
    {
        [Header("Loaders")]
        public LocalRemoteSplatLoader splatLoader;
        public LocalRemotePanoLoader  panoLoader;

        [Header("UI References")]
        [Tooltip("TMP_InputField where the user types or pastes the URL.")]
        public TMP_InputField    urlInputField;
        [Tooltip("The Content RectTransform inside the history ScrollView's Viewport.")]
        public RectTransform     historyListContent;
        [Tooltip("Prefab instantiated for each history entry. Must have a UrlHistoryItemUI component.")]
        public UrlHistoryItemUI  urlHistoryItemPrefab;
        [Tooltip("Small status line shown below the input row.")]
        public TMP_Text          statusLabel;

        Coroutine _refreshCoroutine;

        void OnEnable() => RefreshHistory();

        void OnDisable()
        {
            if (_refreshCoroutine != null)
            {
                StopCoroutine(_refreshCoroutine);
                _refreshCoroutine = null;
            }
        }

        // ── Public API ────────────────────────────────────────────────────────────

        public void OnLoadButtonClicked()
        {
            string url = urlInputField != null ? urlInputField.text.Trim() : "";
            if (string.IsNullOrWhiteSpace(url))
            {
                SetStatus("Enter a URL or file path first.");
                return;
            }
            ExecuteLoad(url);
        }

        public void RefreshHistory()
        {
            if (!isActiveAndEnabled) return;
            if (_refreshCoroutine != null)
                StopCoroutine(_refreshCoroutine);
            _refreshCoroutine = StartCoroutine(RefreshHistoryCoroutine());
        }

        public void Refresh() => RefreshHistory();

        IEnumerator RefreshHistoryCoroutine()
        {
            if (historyListContent == null) yield break;

            for (int i = historyListContent.childCount - 1; i >= 0; i--)
                Destroy(historyListContent.GetChild(i).gameObject);

            yield return null;

            List<string> urls = UrlHistoryStore.Load();

            if (urls.Count == 0)
            {
                AddPlaceholderRow("No recent URLs.");
                _refreshCoroutine = null;
                yield break;
            }

            if (urlHistoryItemPrefab == null)
            {
                Debug.LogError("[UrlLoaderPanel] urlHistoryItemPrefab is not assigned.");
                _refreshCoroutine = null;
                yield break;
            }

            foreach (string url in urls)
            {
                string captured = url;
                var item = Instantiate(urlHistoryItemPrefab, historyListContent);
                item.SetData(captured, () =>
                {
                    if (urlInputField != null)
                        urlInputField.text = captured;
                });
                yield return null;
            }

            _refreshCoroutine = null;
        }

        public void ClearHistory()
        {
            UrlHistoryStore.Clear();
            RefreshHistory();
        }

        // ── Private ───────────────────────────────────────────────────────────────

        void ExecuteLoad(string url)
        {
            string ext   = Path.GetExtension(url).ToLowerInvariant();
            bool isSplat = ext == ".spz" || ext == ".ply";
            bool isPano  = ext == ".jpg" || ext == ".jpeg" || ext == ".png";

            if (!isSplat && !isPano)
            {
                SetStatus($"Unsupported format '{ext}'. Use .spz, .ply, .jpg, .jpeg, or .png.");
                return;
            }

            UrlHistoryStore.Save(url);
            RefreshHistory();
            SetStatus($"Loading {(isSplat ? "splat" : "panorama")}…");

            if (isSplat) splatLoader?.LoadAsync(url);
            else         panoLoader?.LoadAsync(url);
        }

        /// <summary>Shown when history is empty; built inline since it's a one-off non-interactive row.</summary>
        void AddPlaceholderRow(string message)
        {
            var go = new GameObject("Placeholder", typeof(RectTransform));
            go.transform.SetParent(historyListContent, false);

            var le = go.AddComponent<LayoutElement>();
            le.minHeight = 46;

            var hg = go.AddComponent<HorizontalLayoutGroup>();
            hg.padding                = new RectOffset(12, 12, 0, 0);
            hg.childAlignment         = TextAnchor.MiddleLeft;
            hg.childForceExpandWidth  = true;
            hg.childForceExpandHeight = true;

            var textGO = new GameObject("Text", typeof(RectTransform));
            textGO.transform.SetParent(go.transform, false);
            var tmp       = textGO.AddComponent<TextMeshProUGUI>();
            tmp.text      = message;
            tmp.fontSize  = 14;
            tmp.color     = new Color(0.5f, 0.5f, 0.5f);
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            tmp.fontStyle = FontStyles.Italic;
        }

        void SetStatus(string msg)
        {
            if (statusLabel == null) return;
            bool show = !string.IsNullOrEmpty(msg);
            statusLabel.gameObject.SetActive(show);
            if (show) statusLabel.text = msg;
        }
    }
}
