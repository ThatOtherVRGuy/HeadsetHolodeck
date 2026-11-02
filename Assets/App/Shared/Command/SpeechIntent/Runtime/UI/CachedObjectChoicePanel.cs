using System;
using System.Collections.Generic;
using Holodeck.Direct;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SpeechIntent
{
    public sealed class CachedObjectChoicePanel : MonoBehaviour
    {
        [Header("UI")]
        public GameObject root;
        public TextMeshProUGUI titleLabel;
        public TextMeshProUGUI detailLabel;
        public Button useSavedButton;
        public Button createNewButton;
        public Button cancelButton;

        List<CachedObjectRecord> _matches = new List<CachedObjectRecord>();
        Action<CachedObjectRecord> _onUse;
        Action _onCreateNew;
        Action _onCancel;

        void Awake()
        {
            WireButtons();
            Hide();
        }

        void OnEnable()
        {
            WireButtons();
        }

        void OnDisable()
        {
            UnwireButtons();
        }

        public void Show(IReadOnlyList<CachedObjectRecord> matches, Action<CachedObjectRecord> onUse, Action onCreateNew, Action onCancel)
        {
            _matches = matches != null ? new List<CachedObjectRecord>(matches) : new List<CachedObjectRecord>();
            _onUse = onUse;
            _onCreateNew = onCreateNew;
            _onCancel = onCancel;

            CachedObjectRecord first = _matches.Count > 0 ? _matches[0] : null;
            if (titleLabel != null)
                titleLabel.text = first != null
                    ? $"Saved {FirstNonEmpty(first.canonical_name, "object")}"
                    : "Saved object";

            if (detailLabel != null)
            {
                if (first == null)
                {
                    detailLabel.text = "No cached match.";
                }
                else
                {
                    string provider = FirstNonEmpty(first.provider, "unknown provider");
                    string created = FirstNonEmpty(first.created_at, "unknown date");
                    detailLabel.text = _matches.Count == 1
                        ? $"{provider}\n{created}"
                        : $"{_matches.Count} matches\nFirst: {provider}\n{created}";
                }
            }

            SetVisible(true);
        }

        public void Hide()
        {
            SetVisible(false);
        }

        public void UseSaved()
        {
            CachedObjectRecord first = _matches.Count > 0 ? _matches[0] : null;
            _onUse?.Invoke(first);
        }

        public void CreateNew()
        {
            _onCreateNew?.Invoke();
        }

        public void Cancel()
        {
            _onCancel?.Invoke();
        }

        void WireButtons()
        {
            UnwireButtons();
            if (useSavedButton != null) useSavedButton.onClick.AddListener(UseSaved);
            if (createNewButton != null) createNewButton.onClick.AddListener(CreateNew);
            if (cancelButton != null) cancelButton.onClick.AddListener(Cancel);
        }

        void UnwireButtons()
        {
            if (useSavedButton != null) useSavedButton.onClick.RemoveListener(UseSaved);
            if (createNewButton != null) createNewButton.onClick.RemoveListener(CreateNew);
            if (cancelButton != null) cancelButton.onClick.RemoveListener(Cancel);
        }

        void SetVisible(bool visible)
        {
            GameObject target = root != null ? root : gameObject;
            if (target != null && target.activeSelf != visible)
                target.SetActive(visible);
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
