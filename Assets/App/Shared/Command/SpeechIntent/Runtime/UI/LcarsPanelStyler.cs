using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SpeechIntent
{
    public static class LcarsPanelStyler
    {
        public static readonly Color SpaceBlack = new Color(0.006f, 0.007f, 0.008f, 0.98f);
        public static readonly Color PanelBlack = new Color(0.018f, 0.018f, 0.024f, 0.96f);
        public static readonly Color DeepBlue = new Color(0.035f, 0.040f, 0.085f, 0.96f);
        public static readonly Color Orange = new Color(1.00f, 0.48f, 0.04f, 1f);
        public static readonly Color Gold = new Color(1.00f, 0.76f, 0.07f, 1f);
        public static readonly Color Blue = new Color(0.25f, 0.32f, 1.00f, 1f);
        public static readonly Color Violet = new Color(0.50f, 0.27f, 0.88f, 1f);
        public static readonly Color Text = new Color(0.94f, 0.90f, 0.82f, 1f);
        public static readonly Color DimText = new Color(0.58f, 0.60f, 0.68f, 1f);

        public static void StylePanel(GameObject panel)
        {
            if (panel == null)
                return;

            foreach (Image image in panel.GetComponentsInChildren<Image>(true))
            {
                if (image.GetComponentInParent<ModelModeRadioGroup>(true) != null)
                    continue;
                StyleImage(image);
            }

            foreach (RawImage raw in panel.GetComponentsInChildren<RawImage>(true))
            {
                if (raw.GetComponentInParent<CachedObjectCardUI>(true) != null)
                    continue;

                raw.color = raw.texture != null ? Color.white : new Color(0f, 0f, 0f, 0.78f);
            }

            foreach (Button button in panel.GetComponentsInChildren<Button>(true))
            {
                if (button.GetComponentInParent<ModelModeRadioGroup>(true) != null)
                    continue;
                StyleButton(button);
            }

            foreach (TMP_Text text in panel.GetComponentsInChildren<TMP_Text>(true))
            {
                if (text.GetComponentInParent<ModelModeRadioGroup>(true) != null)
                    continue;
                StyleTmpText(text);
            }

            foreach (Text text in panel.GetComponentsInChildren<Text>(true))
            {
                if (text.GetComponentInParent<ModelModeRadioGroup>(true) != null)
                    continue;
                StyleLegacyText(text);
            }

            foreach (TMP_InputField input in panel.GetComponentsInChildren<TMP_InputField>(true))
                StyleTmpInput(input);

            foreach (InputField input in panel.GetComponentsInChildren<InputField>(true))
                StyleLegacyInput(input);

            foreach (Scrollbar scrollbar in panel.GetComponentsInChildren<Scrollbar>(true))
                StyleScrollbar(scrollbar);
        }

        public static void StyleNavButton(Button button, bool active)
        {
            if (button == null)
                return;

            if (!button.interactable)
            {
                StyleDisabledButton(button);
                return;
            }

            Color color = active ? Gold : ButtonColor(button.name, ButtonLabel(button));
            ApplyButtonColors(button, color);
            if (button.targetGraphic is Image image)
                image.color = color;

            TMP_Text tmp = button.GetComponentInChildren<TMP_Text>(true);
            if (tmp != null)
            {
                tmp.color = Color.black;
                tmp.fontStyle = active ? FontStyles.Bold : FontStyles.Normal;
                tmp.characterSpacing = 0;
            }

            Text legacy = button.GetComponentInChildren<Text>(true);
            if (legacy != null)
            {
                legacy.color = Color.black;
                legacy.fontStyle = active ? FontStyle.Bold : FontStyle.Normal;
            }
        }

        static void StyleImage(Image image)
        {
            if (image == null)
                return;

            string name = image.gameObject.name.ToLowerInvariant();
            if (name.Contains("scrollbar"))
                image.color = new Color(0.045f, 0.047f, 0.055f, 1f);
            else if (name.Contains("handle"))
                image.color = Gold;
            else if (image.GetComponent<Button>() != null)
                image.color = ButtonColor(image.gameObject.name, ButtonLabel(image.GetComponent<Button>()));
            else if (name.Contains("header") || name.Contains("title") || name.Contains("tab"))
                image.color = DeepBlue;
            else if (name.Contains("viewport") || name.Contains("content") || name.Contains("list"))
                image.color = SpaceBlack;
            else
                image.color = PanelBlack;
        }

        static void StyleButton(Button button)
        {
            if (button == null)
                return;

            if (!button.interactable)
            {
                StyleDisabledButton(button);
                return;
            }

            Color color = ButtonColor(button.name, ButtonLabel(button));
            ApplyButtonColors(button, color);
            if (button.targetGraphic is Image image)
                image.color = color;
        }

        public static void StyleDisabledButton(Button button)
        {
            if (button == null)
                return;

            Color color = new Color(0.16f, 0.16f, 0.18f, 0.70f);
            ApplyButtonColors(button, color);
            if (button.targetGraphic is Image image)
                image.color = color;

            TMP_Text tmp = button.GetComponentInChildren<TMP_Text>(true);
            if (tmp != null)
            {
                tmp.color = DimText;
                tmp.fontStyle = FontStyles.Normal;
                tmp.characterSpacing = 0;
            }

            Text legacy = button.GetComponentInChildren<Text>(true);
            if (legacy != null)
            {
                legacy.color = DimText;
                legacy.fontStyle = FontStyle.Normal;
            }
        }

        static void ApplyButtonColors(Button button, Color color)
        {
            ColorBlock colors = button.colors;
            colors.normalColor = color;
            colors.highlightedColor = Color.Lerp(color, Color.white, 0.22f);
            colors.pressedColor = Color.Lerp(color, Color.black, 0.30f);
            colors.selectedColor = Color.Lerp(color, Color.white, 0.12f);
            colors.disabledColor = new Color(0.13f, 0.13f, 0.15f, 0.55f);
            colors.colorMultiplier = 1f;
            button.colors = colors;
        }

        static void StyleTmpText(TMP_Text text)
        {
            if (text == null)
                return;

            bool onButton = text.GetComponentInParent<Button>() != null;
            text.color = onButton ? Color.black : Text;
            text.characterSpacing = 0;
            text.enableWordWrapping = true;
            text.overflowMode = TextOverflowModes.Ellipsis;
            if (onButton)
                text.fontStyle = FontStyles.Bold;
        }

        static void StyleLegacyText(Text text)
        {
            if (text == null)
                return;

            bool onButton = text.GetComponentInParent<Button>() != null;
            text.color = onButton ? Color.black : Text;
            text.fontStyle = onButton ? FontStyle.Bold : FontStyle.Normal;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 10;
            text.resizeTextMaxSize = Mathf.Max(12, text.fontSize);
        }

        static void StyleTmpInput(TMP_InputField input)
        {
            Image image = input.GetComponent<Image>();
            if (image != null)
                image.color = new Color(0.07f, 0.08f, 0.11f, 1f);
            if (input.textComponent != null)
                input.textComponent.color = Text;
            if (input.placeholder is TMP_Text placeholder)
            {
                placeholder.color = DimText;
                placeholder.fontStyle = FontStyles.Italic;
            }
        }

        static void StyleLegacyInput(InputField input)
        {
            Image image = input.GetComponent<Image>();
            if (image != null)
                image.color = new Color(0.07f, 0.08f, 0.11f, 1f);
            if (input.textComponent != null)
                input.textComponent.color = Text;
            if (input.placeholder is Text placeholder)
            {
                placeholder.color = DimText;
                placeholder.fontStyle = FontStyle.Italic;
            }
        }

        static void StyleScrollbar(Scrollbar scrollbar)
        {
            if (scrollbar == null)
                return;

            if (scrollbar.targetGraphic is Image handle)
                handle.color = Gold;
            Image track = scrollbar.GetComponent<Image>();
            if (track != null)
                track.color = new Color(0.035f, 0.037f, 0.045f, 1f);
        }

        static Color ButtonColor(string name, string label)
        {
            string key = ((name ?? "") + " " + (label ?? "")).ToLowerInvariant();
            if (key.Contains("delete") || key.Contains("clear"))
                return new Color(0.80f, 0.08f, 0.00f, 1f);
            if (key.Contains("load") || key.Contains("next") || key.Contains("previous") || key.Contains("camera") || key.Contains("recap"))
                return Blue;
            if (key.Contains("object"))
                return Gold;
            if (key.Contains("file") || key.Contains("url"))
                return Violet;
            if (key.Contains("world") || key.Contains("create") || key.Contains("home"))
                return Orange;
            return new Color(0.78f, 0.72f, 0.88f, 1f);
        }

        static string ButtonLabel(Button button)
        {
            if (button == null)
                return "";
            TMP_Text tmp = button.GetComponentInChildren<TMP_Text>(true);
            if (tmp != null)
                return tmp.text;
            Text legacy = button.GetComponentInChildren<Text>(true);
            return legacy != null ? legacy.text : "";
        }
    }
}
