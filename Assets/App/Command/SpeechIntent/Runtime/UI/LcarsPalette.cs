using UnityEngine;

namespace SpeechIntent
{
    [CreateAssetMenu(fileName = "DefaultLcarsPalette", menuName = "Holodeck/LCARS Palette")]
    public sealed class LcarsPalette : ScriptableObject
    {
        const string DefaultResourcePath = "LCARS/DefaultLcarsPalette";

        static LcarsPalette _default;
        static LcarsPalette _fallback;

        [Header("Surfaces")]
        public Color spaceBlack = new Color(0.006f, 0.007f, 0.008f, 0.98f);
        public Color panelBlack = new Color(0.018f, 0.018f, 0.024f, 0.96f);
        public Color panelBlue = new Color(0.035f, 0.040f, 0.085f, 0.96f);
        public Color inputBlack = new Color(0.07f, 0.08f, 0.11f, 1f);
        public Color disabled = new Color(0.16f, 0.16f, 0.18f, 0.70f);

        [Header("LCARS Accents")]
        public Color orange = new Color(1.00f, 0.48f, 0.04f, 1f);
        public Color amber = new Color(0.90f, 0.38f, 0.05f, 1f);
        public Color gold = new Color(1.00f, 0.76f, 0.07f, 1f);
        public Color redOrange = new Color(0.80f, 0.08f, 0.00f, 1f);
        public Color blue = new Color(0.25f, 0.32f, 1.00f, 1f);
        public Color lightBlue = new Color(0.48f, 0.55f, 1.00f, 1f);
        public Color violet = new Color(0.50f, 0.27f, 0.88f, 1f);
        public Color lavender = new Color(0.66f, 0.52f, 0.94f, 1f);
        public Color paleViolet = new Color(0.78f, 0.72f, 0.88f, 1f);

        [Header("Text")]
        public Color text = new Color(0.94f, 0.90f, 0.82f, 1f);
        public Color dimText = new Color(0.58f, 0.60f, 0.68f, 1f);
        public Color buttonText = Color.black;

        [Header("Status")]
        public Color info = new Color(1.00f, 0.78f, 0.08f, 1f);
        public Color success = new Color(0.48f, 0.55f, 1.00f, 1f);
        public Color warning = new Color(1.00f, 0.55f, 0.07f, 1f);
        public Color error = new Color(0.90f, 0.20f, 0.10f, 1f);

        [Header("Model Mode Radio Group")]
        public Color modelActive = new Color(0.25f, 0.34f, 1f, 1f);
        public Color modelDraft = new Color(1f, 0.55f, 0.03f, 1f);
        public Color modelFast = new Color(1f, 0.55f, 0.03f, 1f);
        public Color modelStandard = new Color(1f, 0.55f, 0.03f, 1f);
        public Color modelHigh = new Color(1f, 0.55f, 0.03f, 1f);

        public static LcarsPalette Default
        {
            get
            {
                if (_default == null)
                    _default = Resources.Load<LcarsPalette>(DefaultResourcePath);
                if (_default != null)
                    return _default;

                if (_fallback == null)
                {
                    _fallback = CreateInstance<LcarsPalette>();
                    _fallback.name = "Runtime LCARS Palette Fallback";
                }

                return _fallback;
            }
        }
    }
}
