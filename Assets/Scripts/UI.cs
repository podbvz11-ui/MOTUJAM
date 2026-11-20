using UnityEngine;
using UnityEngine.UI;

namespace Motujam
{
    /// <summary>코드로 UI를 만들 때 쓰는 도우미. 모든 좌표는 1920x1080 캔버스의 중앙 기준이다.</summary>
    public static class UI
    {
        static Font font;
        static Sprite gradient;
        static Texture2D rainbow;

        public static Font DefaultFont
        {
            get
            {
                if (font == null) font = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "Segoe UI", "Arial" }, 32);
                return font;
            }
        }

        /// <summary>아래쪽이 불투명하고 위로 갈수록 투명해지는 그라데이션.</summary>
        public static Sprite Gradient
        {
            get
            {
                if (gradient == null)
                {
                    var tex = new Texture2D(1, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                    for (int y = 0; y < 64; y++)
                    {
                        float a = 1f - y / 63f;
                        tex.SetPixel(0, y, new Color(1, 1, 1, a * a));
                    }
                    tex.Apply();
                    gradient = Sprite.Create(tex, new Rect(0, 0, 1, 64), new Vector2(0.5f, 0f));
                }
                return gradient;
            }
        }

        /// <summary>세로로 반복되는 무지개 텍스처.</summary>
        public static Texture2D Rainbow
        {
            get
            {
                if (rainbow == null)
                {
                    rainbow = new Texture2D(1, 256, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat };
                    for (int y = 0; y < 256; y++) rainbow.SetPixel(0, y, Color.HSVToRGB(y / 256f, 0.75f, 1f));
                    rainbow.Apply();
                }
                return rainbow;
            }
        }

        public static RectTransform Node(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        public static void Place(RectTransform rt, float x, float y, float w, float h, float pivotX = 0.5f, float pivotY = 0.5f)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(pivotX, pivotY);
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(w, h);
        }

        public static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        public static Image Img(string name, Transform parent, Sprite sprite, Color color)
        {
            var img = Node(name, parent).gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        /// <summary>스킨 이미지가 있으면 그 이미지를 원래 색으로, 없으면 단색 사각형으로 그린다.</summary>
        public static Image SkinImg(string name, Transform parent, Sprite sprite, Color fallbackColor)
        {
            return Img(name, parent, sprite, sprite != null ? Color.white : fallbackColor);
        }

        public static RawImage RawImg(string name, Transform parent, Texture texture, Color color)
        {
            var img = Node(name, parent).gameObject.AddComponent<RawImage>();
            img.texture = texture;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        public static Text Txt(string name, Transform parent, string text, int size, TextAnchor anchor, Color color)
        {
            var t = Node(name, parent).gameObject.AddComponent<Text>();
            t.font = DefaultFont;
            t.text = text;
            t.fontSize = size;
            t.alignment = anchor;
            t.color = color;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        public static Text Txt(string name, Transform parent, string text, int size, TextAnchor anchor, Color color, float x, float y, float w = 600, float h = 60)
        {
            var t = Txt(name, parent, text, size, anchor, color);
            Place(t.rectTransform, x, y, w, h);
            return t;
        }
    }
}
