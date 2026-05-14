// Assets/App/Command/SpeechIntent/Runtime/UI/LocalFileBrowserPanel.cs

using System.Collections;
using System.IO;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;

namespace SpeechIntent
{
    /// <summary>
    /// Scans <see cref="localBasePath"/> for supported content files and populates a
    /// scrollable list by instantiating <see cref="fileEntryPrefab"/> per file.
    /// Auto-refreshes when the panel is enabled.
    /// </summary>
    public class LocalFileBrowserPanel : MonoBehaviour
    {
        [Header("Loaders")]
        public LocalRemoteSplatLoader splatLoader;
        public LocalRemotePanoLoader  panoLoader;

        [Header("Local Storage")]
        [Tooltip("If assigned, scans WorldConfigStore.CachedWorldsPath. Overrides localBasePath.")]
        public Holodeck.Save.WorldConfigStore worldConfigStore;
        [Tooltip("Directory to scan. Defaults to Application.persistentDataPath/WorldContent at runtime. Ignored if worldConfigStore is assigned.")]
        public string localBasePath = "";

        [Header("UI References")]
        [Tooltip("The Content RectTransform inside the ScrollView's Viewport.")]
        public RectTransform   fileListContent;
        [Tooltip("Prefab instantiated for each file entry. Must have a FileEntryItemUI component.")]
        public FileEntryItemUI fileEntryPrefab;
        [Tooltip("Shown when the folder is empty or missing.")]
        public TMP_Text        statusLabel;

        static readonly string[] SplatExts      = { ".spz", ".ply" };
        static readonly string[] PanoExts       = { ".jpg", ".jpeg", ".png" };
        static readonly Color    SplatBadgeColor = new Color(0.5f, 0.85f, 1f);
        static readonly Color    PanoBadgeColor  = new Color(1f, 0.85f, 0.5f);
        Coroutine _refreshCoroutine;

        void Awake()
        {
            if (worldConfigStore != null)
                localBasePath = worldConfigStore.CachedWorldsPath;
            else if (string.IsNullOrWhiteSpace(localBasePath))
                localBasePath = Path.Combine(Application.persistentDataPath, "WorldContent");
        }

        void OnEnable() => Refresh();

        void OnDisable()
        {
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
            if (fileListContent == null) yield break;

            for (int i = fileListContent.childCount - 1; i >= 0; i--)
                Destroy(fileListContent.GetChild(i).gameObject);

            if (!Directory.Exists(localBasePath))
            {
                ShowStatus($"Folder not found:\n{localBasePath}");
                yield break;
            }

            if (fileEntryPrefab == null)
            {
                Debug.LogError("[LocalFileBrowserPanel] fileEntryPrefab is not assigned.");
                yield break;
            }

            Task<string[]> filesTask = Task.Run(() => Directory.GetFiles(localBasePath));
            while (!filesTask.IsCompleted)
                yield return null;

            if (filesTask.IsFaulted)
            {
                ShowStatus($"Could not scan folder:\n{filesTask.Exception?.GetBaseException().Message}");
                yield break;
            }

            string[] files = filesTask.Result;
            int added = 0;

            foreach (string file in files)
            {
                string ext   = Path.GetExtension(file).ToLowerInvariant();
                bool isSplat = System.Array.IndexOf(SplatExts, ext) >= 0;
                bool isPano  = System.Array.IndexOf(PanoExts, ext) >= 0;
                if (!isSplat && !isPano) continue;

                string captured   = file;
                string fileName   = Path.GetFileName(file);
                string badge      = isSplat ? "splat" : "pano";
                Color  badgeColor = isSplat ? SplatBadgeColor : PanoBadgeColor;

                var item = Instantiate(fileEntryPrefab, fileListContent);
                item.SetData(fileName, badge, badgeColor, () =>
                {
                    if (isSplat) splatLoader?.LoadAsync(captured);
                    else         panoLoader?.LoadAsync(captured);
                });
                added++;
                yield return null;
            }

            ShowStatus(added == 0 ? "No supported files found." : null);
            _refreshCoroutine = null;
        }

        void ShowStatus(string msg)
        {
            if (statusLabel == null) return;
            statusLabel.gameObject.SetActive(msg != null);
            if (msg != null) statusLabel.text = msg;
        }
    }
}
