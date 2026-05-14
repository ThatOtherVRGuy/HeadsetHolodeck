// Assets/App/Command/SpeechIntent/Runtime/UI/MyWorldsPanel.cs
using System.Collections;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using Holodeck.Save;

namespace SpeechIntent
{
    /// <summary>
    /// Scrollable panel showing all WorldConfig folders.
    /// Follows the LocalFileBrowserPanel pattern.
    /// Register with UiPanelController under key "my worlds".
    /// </summary>
    public class MyWorldsPanel : MonoBehaviour
    {
        [Header("Dependencies")]
        public WorldConfigStore    worldConfigStore;
        public WorldConfigRestorer worldConfigRestorer;

        [Header("UI References")]
        public RectTransform              cardListContent;
        public WorldConfigCardUI          cardPrefab;
        public TMP_Text                   statusLabel;
        public UnityEngine.UI.InputField  saveAsInputField;
        public UnityEngine.UI.Button      saveAsConfirmButton;

        WorldConfig _pendingSaveAs;
        Coroutine _refreshCoroutine;
        readonly System.Collections.Generic.List<Texture2D> _thumbnails = new System.Collections.Generic.List<Texture2D>();

        void OnEnable()
        {
            if (worldConfigStore != null)
                worldConfigStore.OnConfigsChanged += Refresh;
            Refresh();
        }

        void OnDisable()
        {
            if (worldConfigStore != null)
                worldConfigStore.OnConfigsChanged -= Refresh;
            if (_refreshCoroutine != null)
            {
                StopCoroutine(_refreshCoroutine);
                _refreshCoroutine = null;
            }
        }

        public void Refresh()
        {
            if (!isActiveAndEnabled) return;
            if (_refreshCoroutine != null)
                StopCoroutine(_refreshCoroutine);
            _refreshCoroutine = StartCoroutine(RefreshCoroutine());
        }

        IEnumerator RefreshCoroutine()
        {
            if (cardListContent == null) yield break;

            foreach (Texture2D tex in _thumbnails)
                if (tex != null) Destroy(tex);
            _thumbnails.Clear();

            for (int i = cardListContent.childCount - 1; i >= 0; i--)
                Destroy(cardListContent.GetChild(i).gameObject);

            if (worldConfigStore == null)
            {
                ShowStatus("WorldConfigStore not assigned.");
                yield break;
            }

            var configs = worldConfigStore.ListConfigs();
            if (configs.Count == 0)
            {
                ShowStatus("No saved worlds found.");
                yield break;
            }

            ShowStatus(null);
            foreach (WorldConfig config in configs)
            {
                WorldConfig captured = config;

                WorldConfigCardUI card = Instantiate(cardPrefab, cardListContent);
                card.SetData(
                    displayName: config.display_name,
                    sourceType:  config.world_source?.type,
                    modifiedAt:  config.modified_at,
                    thumb:       null,
                    onLoad:   () => _ = worldConfigRestorer?.RestoreAsync(captured),
                    onSaveAs: () => BeginSaveAs(captured),
                    onDelete: () => ConfirmDelete(captured)
                );
                StartCoroutine(LoadThumbnailCoroutine(config, card));
                yield return null;
            }

            _refreshCoroutine = null;
        }

        void BeginSaveAs(WorldConfig config)
        {
            _pendingSaveAs = config;
            if (saveAsInputField != null)
            {
                saveAsInputField.text = config.display_name ?? "";
                saveAsInputField.gameObject.SetActive(true);
            }
            if (saveAsConfirmButton != null)
            {
                saveAsConfirmButton.gameObject.SetActive(true);
                saveAsConfirmButton.onClick.RemoveAllListeners();
                saveAsConfirmButton.onClick.AddListener(CommitSaveAs);
            }
        }

        void CommitSaveAs()
        {
            if (_pendingSaveAs == null || worldConfigStore == null) return;
            string newName = saveAsInputField != null ? saveAsInputField.text.Trim() : "";
            if (string.IsNullOrEmpty(newName)) return;

            worldConfigStore.ForkConfig(_pendingSaveAs, newName);
            _pendingSaveAs = null;
            if (saveAsInputField    != null) saveAsInputField.gameObject.SetActive(false);
            if (saveAsConfirmButton != null) saveAsConfirmButton.gameObject.SetActive(false);
        }

        void ConfirmDelete(WorldConfig config)
        {
            // Simple immediate delete — confirmation dialog can be added later if needed
            worldConfigStore?.DeleteConfig(config.config_id);
        }

        IEnumerator LoadThumbnailCoroutine(WorldConfig config, WorldConfigCardUI card)
        {
            string imagePath = !string.IsNullOrEmpty(config.world_source?.cached_thumbnail)
                ? config.world_source.cached_thumbnail
                : config.world_source?.cached_pano;
            if (string.IsNullOrEmpty(imagePath)) yield break;
            string abs = Path.GetFullPath(
                Path.Combine(worldConfigStore.WorldsRootPath, config.config_id, imagePath));
            if (!File.Exists(abs)) yield break;

            using UnityWebRequest req = UnityWebRequestTexture.GetTexture("file://" + abs);
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success)
                yield break;

            Texture2D tex = DownloadHandlerTexture.GetContent(req);
            if (tex == null || card == null) yield break;
            _thumbnails.Add(tex);
            card.SetThumbnail(tex);
        }

        void ShowStatus(string msg)
        {
            if (statusLabel == null) return;
            statusLabel.gameObject.SetActive(msg != null);
            if (msg != null) statusLabel.text = msg;
        }
    }
}
