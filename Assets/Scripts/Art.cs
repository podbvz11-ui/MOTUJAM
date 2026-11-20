using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Motujam
{
    /// <summary>스킨 이미지가 없을 때 쓰는 그래픽(글로우 노트, 빛, 링, 둥근 패널 등)을 코드로 그려 만든다.</summary>
    public static class Art
    {
        static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();
        static Texture2D rainbowH, bgGradient;

        static Texture2D Make(int w, int h, Func<int, int, Color> pixel, TextureWrapMode wrap = TextureWrapMode.Clamp)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = wrap, filterMode = FilterMode.Bilinear };
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    px[y * w + x] = pixel(x, y);
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        static Sprite Cached(string key, Func<Sprite> create)
        {
            if (!cache.TryGetValue(key, out Sprite s) || s == null)
            {
                s = create();
                cache[key] = s;
            }
            return s;
        }

        static Sprite ToSprite(Texture2D tex, float border = 0)
        {
            return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(border, border, border, border));
        }

        // 둥근 사각형까지의 거리 (안쪽은 음수)
        static float RoundBox(float px, float py, float hx, float hy, float r)
        {
            float qx = Mathf.Abs(px) - hx + r, qy = Mathf.Abs(py) - hy + r;
            float ox = Mathf.Max(qx, 0), oy = Mathf.Max(qy, 0);
            return Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0) - r;
        }

        const int NoteTexW = 160, NoteTexH = 64;
        const float NoteBodyW = 128f, NoteBodyH = 28f;

        /// <summary>노트 몸체가 (w, h) 크기로 보이도록 하는 이미지 크기 (주변 빛 여백 포함).</summary>
        public static Vector2 NoteSize(float w, float h) => new Vector2(w * NoteTexW / NoteBodyW, h * NoteTexH / NoteBodyH);

        /// <summary>광택과 테두리, 주변 빛이 들어간 노트.</summary>
        public static Sprite Note(Color c)
        {
            return Cached("note" + ColorUtility.ToHtmlStringRGB(c), () => ToSprite(Make(NoteTexW, NoteTexH, (x, y) =>
            {
                float px = x + 0.5f - NoteTexW / 2f, py = y + 0.5f - NoteTexH / 2f;
                float d = RoundBox(px, py, NoteBodyW / 2f, NoteBodyH / 2f, 8f);
                float t = Mathf.Clamp01((py + NoteBodyH / 2f) / NoteBodyH);
                Color body = Color.Lerp(c * 0.5f, c, t);
                if (t > 0.55f) body = Color.Lerp(body, Color.white, 0.55f * Mathf.SmoothStep(0f, 1f, (t - 0.55f) / 0.3f));
                body = Color.Lerp(body, Color.white, Mathf.Clamp01((d + 3f) / 2f) * 0.85f);   // 밝은 테두리
                body.a = 1f;
                Color glow = c;
                glow.a = Mathf.Exp(-Mathf.Max(d, 0f) / 5f) * 0.6f * Mathf.Clamp01(1f - d / 15f);   // 텍스처 끝에서 완전히 사라지게
                return Color.Lerp(glow, body, Mathf.Clamp01(0.5f - d));
            })));
        }

        /// <summary>롱노트 몸통. 가장자리가 밝고 가운데는 반투명.</summary>
        public static Sprite LongBody(Color c)
        {
            return Cached("long" + ColorUtility.ToHtmlStringRGB(c), () => ToSprite(Make(64, 4, (x, y) =>
            {
                float u = Mathf.Abs((x + 0.5f) / 32f - 1f);
                Color col = Color.Lerp(c, Color.white, Mathf.Pow(u, 5f));
                col.a = Mathf.Lerp(0.5f, 1f, Mathf.Pow(u, 4f));
                return col;
            })));
        }

        /// <summary>부드러운 원형 빛.</summary>
        public static Sprite Glow => Cached("glow", () => ToSprite(Make(128, 128, (x, y) =>
        {
            float r = Mathf.Sqrt((x - 63.5f) * (x - 63.5f) + (y - 63.5f) * (y - 63.5f)) / 64f;
            float a = Mathf.Clamp01(1f - r);
            return new Color(1, 1, 1, a * a);
        })));

        /// <summary>히트 이펙트용 링.</summary>
        public static Sprite Ring => Cached("ring", () => ToSprite(Make(128, 128, (x, y) =>
        {
            float r = Mathf.Sqrt((x - 63.5f) * (x - 63.5f) + (y - 63.5f) * (y - 63.5f)) / 64f;
            float k = (r - 0.8f) / 0.07f;
            return new Color(1, 1, 1, Mathf.Exp(-k * k));
        })));

        /// <summary>가운데가 밝은 가로 띠 (판정선 빛).</summary>
        public static Sprite BandH => Cached("bandH", () => ToSprite(Make(4, 64, (x, y) =>
        {
            float v = 1f - Mathf.Abs((y + 0.5f) / 32f - 1f);
            return new Color(1, 1, 1, v * v);
        })));

        /// <summary>가운데가 밝은 세로 띠 (트랙 옆 네온).</summary>
        public static Sprite BandV => Cached("bandV", () => ToSprite(Make(64, 4, (x, y) =>
        {
            float v = 1f - Mathf.Abs((x + 0.5f) / 32f - 1f);
            return new Color(1, 1, 1, v * v);
        })));

        public static Sprite Rounded => Cached("rounded", () => ToSprite(Make(64, 64, (x, y) =>
        {
            float d = RoundBox(x - 31.5f, y - 31.5f, 32f, 32f, 16f);
            return new Color(1, 1, 1, Mathf.Clamp01(0.5f - d));
        }), 24f));

        public static Sprite Frame => Cached("frame", () => ToSprite(Make(64, 64, (x, y) =>
        {
            float d = RoundBox(x - 31.5f, y - 31.5f, 32f, 32f, 16f);
            float line = Mathf.Clamp01(1.5f - Mathf.Abs(d + 2f));
            float inner = d < 0 ? Mathf.Exp(d / 5f) * 0.35f : 0f;
            return new Color(1, 1, 1, Mathf.Max(line, inner));
        }), 24f));

        public static Texture2D RainbowH
        {
            get
            {
                if (rainbowH == null) rainbowH = Make(256, 1, (x, y) => Color.HSVToRGB(x / 256f, 0.75f, 1f), TextureWrapMode.Repeat);
                return rainbowH;
            }
        }

        public static Texture2D BgGradient
        {
            get
            {
                if (bgGradient == null)
                    bgGradient = Make(1, 64, (x, y) => Color.Lerp(new Color(0.02f, 0.02f, 0.07f), new Color(0.13f, 0.04f, 0.22f), y / 63f));
                return bgGradient;
            }
        }

        // ---------- 자주 쓰는 조립 도우미 ----------

        /// <summary>반투명 둥근 패널 + 색 테두리.</summary>
        public static Image Panel(string name, Transform parent, float x, float y, float w, float h, Color fill, Color frame)
        {
            var bg = UI.Img(name, parent, Rounded, fill);
            bg.type = Image.Type.Sliced;
            UI.Place(bg.rectTransform, x, y, w, h);
            var fr = UI.Img("Frame", bg.transform, Frame, frame);
            fr.type = Image.Type.Sliced;
            UI.Stretch(fr.rectTransform);
            return bg;
        }

        /// <summary>글자 뒤에 색 번짐을 넣는다.</summary>
        public static Text Neon(Text t, Color glow, float distance = 3f)
        {
            var o = t.gameObject.AddComponent<Outline>();
            o.effectColor = glow;
            o.effectDistance = new Vector2(distance, distance);
            return t;
        }
    }
}
