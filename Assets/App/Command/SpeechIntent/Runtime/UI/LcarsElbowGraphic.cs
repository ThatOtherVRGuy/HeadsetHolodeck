using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace SpeechIntent
{
    [ExecuteAlways]
    public sealed class LcarsElbowGraphic : MonoBehaviour
    {
        public enum ElbowCorner
        {
            TopLeft,
            TopRight,
            BottomLeft,
            BottomRight
        }

        public ElbowCorner corner = ElbowCorner.TopLeft;
        public Vector2 size = new Vector2(520f, 300f);
        [FormerlySerializedAs("thickness")]
        [Min(1f)] public float horizontalThickness = 92f;
        [Min(1f)] public float verticalThickness = 92f;
        [Min(1f)] public float cornerSize = 92f;
        [Min(1f)] public float innerCutoutSize = 148f;
        public Color accentColor = new Color(1f, 0.48f, 0.04f, 1f);
        public Color backgroundColor = new Color(0.006f, 0.007f, 0.008f, 1f);

        [Header("Sprites")]
        public Sprite outerTopLeft;
        public Sprite outerTopRight;
        public Sprite outerBottomLeft;
        public Sprite outerBottomRight;
        public Sprite inverseTopLeft;
        public Sprite inverseTopRight;
        public Sprite inverseBottomLeft;
        public Sprite inverseBottomRight;

        [Header("Parts")]
        public Image horizontalBar;
        public Image verticalBar;
        public Image outerCorner;
        public Image innerCutout;

        void OnEnable() => Rebuild();
        void OnValidate() => Rebuild();

        [ContextMenu("Rebuild")]
        public void Rebuild()
        {
            RectTransform root = GetComponent<RectTransform>();
            if (root == null)
                return;

            root.sizeDelta = size;
            EnsureParts();
            ApplySprites();
            ApplyColors();
            LayoutParts(root);
        }

        void EnsureParts()
        {
            horizontalBar = EnsureImage("HorizontalBar", horizontalBar);
            verticalBar = EnsureImage("VerticalBar", verticalBar);
            outerCorner = EnsureImage("OuterCorner", outerCorner);
            innerCutout = EnsureImage("InnerCutout", innerCutout);

            verticalBar.transform.SetSiblingIndex(0);
            horizontalBar.transform.SetSiblingIndex(1);
            outerCorner.transform.SetSiblingIndex(2);
            innerCutout.transform.SetSiblingIndex(3);
        }

        Image EnsureImage(string name, Image current)
        {
            if (current != null)
                return current;

            Transform found = transform.Find(name);
            GameObject go = found != null ? found.gameObject : new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(transform, false);
            Image image = go.GetComponent<Image>();
            if (image == null)
                image = go.AddComponent<Image>();
            image.raycastTarget = false;
            return image;
        }

        void ApplySprites()
        {
            if (outerCorner != null)
                outerCorner.sprite = corner switch
                {
                    ElbowCorner.TopRight => outerTopRight,
                    ElbowCorner.BottomLeft => outerBottomLeft,
                    ElbowCorner.BottomRight => outerBottomRight,
                    _ => outerTopLeft
                };

            if (innerCutout != null)
                innerCutout.sprite = corner switch
                {
                    ElbowCorner.TopRight => inverseTopRight,
                    ElbowCorner.BottomLeft => inverseBottomLeft,
                    ElbowCorner.BottomRight => inverseBottomRight,
                    _ => inverseTopLeft
                };
        }

        void ApplyColors()
        {
            if (horizontalBar != null) horizontalBar.color = accentColor;
            if (verticalBar != null) verticalBar.color = accentColor;
            if (outerCorner != null) outerCorner.color = accentColor;
            if (innerCutout != null) innerCutout.color = accentColor;
        }

        void LayoutParts(RectTransform root)
        {
            bool top = corner == ElbowCorner.TopLeft || corner == ElbowCorner.TopRight;
            bool left = corner == ElbowCorner.TopLeft || corner == ElbowCorner.BottomLeft;

            float horizontalLength = Mathf.Max(1f, size.x - cornerSize);
            float verticalLength = Mathf.Max(1f, size.y - cornerSize);
            Vector2 anchor = new Vector2(left ? 0f : 1f, top ? 1f : 0f);

            SetFixed(horizontalBar.rectTransform,
                anchor,
                new Vector2(left ? cornerSize + horizontalLength * 0.5f : -cornerSize - horizontalLength * 0.5f,
                    top ? -horizontalThickness * 0.5f : horizontalThickness * 0.5f),
                new Vector2(horizontalLength, horizontalThickness));

            SetFixed(verticalBar.rectTransform,
                anchor,
                new Vector2(left ? verticalThickness * 0.5f : -verticalThickness * 0.5f,
                    top ? -cornerSize - verticalLength * 0.5f : cornerSize + verticalLength * 0.5f),
                new Vector2(verticalThickness, verticalLength));

            SetFixed(outerCorner.rectTransform,
                anchor,
                new Vector2(left ? cornerSize * 0.5f : -cornerSize * 0.5f, top ? -cornerSize * 0.5f : cornerSize * 0.5f),
                new Vector2(cornerSize, cornerSize));

            SetFixed(innerCutout.rectTransform,
                anchor,
                new Vector2(left ? verticalThickness + innerCutoutSize * 0.5f : -verticalThickness - innerCutoutSize * 0.5f,
                    top ? -horizontalThickness - innerCutoutSize * 0.5f : horizontalThickness + innerCutoutSize * 0.5f),
                new Vector2(innerCutoutSize, innerCutoutSize));
        }

        static void SetFixed(RectTransform rt, Vector2 anchor, Vector2 anchoredPosition, Vector2 sizeDelta)
        {
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = anchoredPosition;
            rt.sizeDelta = sizeDelta;
            rt.localScale = Vector3.one;
            rt.localRotation = Quaternion.identity;
        }
    }
}
