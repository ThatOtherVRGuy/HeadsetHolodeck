using System.Collections;
using System.Collections.Generic;
using System.IO;
using Holodeck.Direct;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;

namespace SpeechIntent
{
    public sealed class CachedObjectCatalogPanel : MonoBehaviour
    {
        [Header("Dependencies")]
        public CachedObjectStore cachedObjectStore;
        public CachedObjectChoiceController cachedObjectChoiceController;
        public WorldActionDispatcher dispatcher;

        [Header("UI References")]
        public RectTransform cardListContent;
        public CachedObjectCardUI cardPrefab;
        public TMP_Text statusLabel;

        [Header("Use Action")]
        public bool placeCatalogUseInFrontOfUser = true;
        public float defaultUseDistanceMeters = 2f;

        [Header("Refresh")]
        [Min(1)] public int cardsPerFrame = 2;

        Coroutine _refreshCoroutine;
        MonoBehaviour _refreshRunner;
        readonly List<Texture2D> _thumbnails = new List<Texture2D>();

        void OnEnable()
        {
            Refresh();
        }

        void OnDisable()
        {
            if (_refreshCoroutine != null && _refreshRunner == this)
            {
                StopCoroutine(_refreshCoroutine);
                _refreshCoroutine = null;
                _refreshRunner = null;
            }
        }

        void OnDestroy()
        {
            StopRefreshCoroutine();
            ClearOwnedThumbnails();
        }

        public void Refresh()
        {
            if (!isActiveAndEnabled)
                return;

            StartRefresh(this);
        }

        public void RefreshInBackground(MonoBehaviour runner)
        {
            StartRefresh(runner);
        }

        void StartRefresh(MonoBehaviour runner)
        {
            if (runner == null || !runner.isActiveAndEnabled)
                return;

            StopRefreshCoroutine();
            if (_refreshCoroutine != null)
                StopCoroutine(_refreshCoroutine);
            _refreshRunner = runner;
            _refreshCoroutine = runner.StartCoroutine(RefreshCoroutine());
        }

        IEnumerator RefreshCoroutine()
        {
            ResolveDependencies();
            ClearCards();
            ClearOwnedThumbnails();

            if (cardListContent == null || cardPrefab == null)
            {
                ShowStatus("Object catalog UI not assigned.");
                FinishRefresh();
                yield break;
            }

            if (cachedObjectStore == null)
            {
                ShowStatus("CachedObjectStore not assigned.");
                FinishRefresh();
                yield break;
            }

            List<CachedObjectRecord> records = cachedObjectStore.ListAll();
            if (records.Count == 0)
            {
                ShowStatus("No cached objects found.");
                FinishRefresh();
                yield break;
            }

            ShowStatus(null);
            int cardsThisFrame = 0;
            foreach (CachedObjectRecord record in records)
            {
                CachedObjectRecord captured = record;
                CachedObjectCardUI card = null;
                try
                {
                    card = Instantiate(cardPrefab, cardListContent);
                    card.gameObject.SetActive(true);
                    card.SetData(
                        captured,
                        null,
                        onUse: () => Use(captured),
                        onDelete: () => Delete(captured),
                        onRename: null);
                }
                catch (System.Exception ex)
                {
                    string label = captured != null && !string.IsNullOrWhiteSpace(captured.canonical_name)
                        ? captured.canonical_name
                        : "cached object";
                    string message = $"Could not create object catalog card for {label}: {ex.Message}";
                    Debug.LogWarning("[CachedObjectCatalogPanel] " + message, this);
                    ArchStatusBus.Warning(message, "OBJECT");
                    if (card != null)
                        Destroy(card.gameObject);
                    continue;
                }

                MonoBehaviour runner = _refreshRunner != null ? _refreshRunner : this;
                runner.StartCoroutine(LoadThumbnailCoroutine(captured, card));

                cardsThisFrame++;
                if (cardsThisFrame >= Mathf.Max(1, cardsPerFrame))
                {
                    cardsThisFrame = 0;
                    yield return null;
                }
            }

            FinishRefresh();
        }

        public void Use(CachedObjectRecord record)
        {
            if (record == null)
            {
                ShowStatus("Cached object missing.");
                return;
            }

            ResolveDependencies();
            if (cachedObjectChoiceController != null && cachedObjectChoiceController.HasPendingChoice)
            {
                if (dispatcher != null)
                {
                    dispatcher.UseSavedCachedObject(record);
                    return;
                }
            }

            if (!placeCatalogUseInFrontOfUser || cachedObjectChoiceController == null || dispatcher == null)
            {
                ShowStatus("Say where to place it, then use the saved object.");
                return;
            }

            var command = new VoiceIntentCommand
            {
                transcript = $"use cached object {record.canonical_name}",
                intent = VoiceIntentType.PlaceObject,
                should_execute = true,
                object_name = record.canonical_name,
                spatial_reference = SpatialReferenceMode.RelativeToMe,
                relative_direction = RelativeDirection.InFront,
                relative_distance_meters = Mathf.Max(0.1f, defaultUseDistanceMeters),
                spoken_response = $"Loading saved {record.canonical_name}."
            };

            cachedObjectChoiceController.BeginChoice(command, new SpatialSnapshot(), new List<CachedObjectRecord> { record });
            dispatcher.UseSavedCachedObject(record);
        }

        public void Delete(CachedObjectRecord record)
        {
            ResolveDependencies();
            if (record == null || cachedObjectStore == null)
                return;

            cachedObjectStore.Delete(record.object_id);
            Refresh();
        }

        IEnumerator LoadThumbnailCoroutine(CachedObjectRecord record, CachedObjectCardUI card)
        {
            string abs = ResolveThumbnailPath(record);
            if (string.IsNullOrEmpty(abs) || !File.Exists(abs))
            {
                Debug.LogWarning($"[CachedObjectCatalogPanel] No thumbnail found for '{record?.canonical_name}'.", this);
                yield break;
            }

            using UnityWebRequest request = UnityWebRequestTexture.GetTexture("file://" + abs);
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success)
            {
                string message = $"Could not load object thumbnail for {record?.canonical_name}: {request.error}";
                Debug.LogWarning("[CachedObjectCatalogPanel] " + message, this);
                ArchStatusBus.Warning(message, "OBJECT");
                yield break;
            }

            Texture2D texture = DownloadHandlerTexture.GetContent(request);
            if (texture == null || card == null)
            {
                string message = $"Object thumbnail load returned no texture for {record?.canonical_name}.";
                Debug.LogWarning("[CachedObjectCatalogPanel] " + message, this);
                ArchStatusBus.Warning(message, "OBJECT");
                yield break;
            }

            _thumbnails.Add(texture);
            card.SetThumbnail(texture);
        }

        string ResolveThumbnailPath(CachedObjectRecord record)
        {
            return record == null || cachedObjectStore == null
                ? null
                : cachedObjectStore.GetThumbnailAbsolutePath(record);
        }

        void ResolveDependencies()
        {
            if (cachedObjectStore == null)
                cachedObjectStore = CachedObjectStore.GetOrCreate();
            if (cachedObjectChoiceController == null)
                cachedObjectChoiceController = FindFirstObjectByType<CachedObjectChoiceController>(FindObjectsInactive.Include);
            if (dispatcher == null)
                dispatcher = FindFirstObjectByType<WorldActionDispatcher>(FindObjectsInactive.Include);
        }

        void ClearCards()
        {
            if (cardListContent == null)
                return;

            for (int i = cardListContent.childCount - 1; i >= 0; i--)
                Destroy(cardListContent.GetChild(i).gameObject);
        }

        void ClearOwnedThumbnails()
        {
            foreach (Texture2D texture in _thumbnails)
            {
                if (texture != null)
                    Destroy(texture);
            }

            _thumbnails.Clear();
        }

        void StopRefreshCoroutine()
        {
            if (_refreshCoroutine == null || _refreshRunner == null)
                return;

            _refreshRunner.StopCoroutine(_refreshCoroutine);
            FinishRefresh();
        }

        void FinishRefresh()
        {
            _refreshCoroutine = null;
            _refreshRunner = null;
        }

        void ShowStatus(string message)
        {
            if (statusLabel == null)
                return;

            statusLabel.gameObject.SetActive(message != null);
            if (message != null)
                statusLabel.text = message;
        }
    }
}
