using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace Motujam
{
    /// <summary>
    /// StreamingAssets/Skin 폴더의 PNG 를 이름으로 불러온다. 파일이 없으면 null 을 돌려주고,
    /// 그 자리는 코드로 그린 기본 모양이 대신한다. 이미지를 바꾸려면 같은 이름의 PNG 를 덮어쓰면 된다.
    /// </summary>
    public static class Skin
    {
        static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();

        // 에디터에서 도메인 리로드를 꺼 두면 이전 실행의 (이미 파괴된) 텍스처가 남으므로 Play 할 때마다 비운다
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Clear()
        {
            foreach (var s in cache.Values)
            {
                if (s == null) continue;
                Object.Destroy(s.texture);
                Object.Destroy(s);
            }
            cache.Clear();
        }

        public static Sprite Get(string name)
        {
            if (cache.TryGetValue(name, out Sprite sprite)) return sprite;
            string path = Path.Combine(Application.streamingAssetsPath, "Skin", name + ".png");
            if (File.Exists(path))
            {
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, name = name };
                if (tex.LoadImage(File.ReadAllBytes(path), true))
                    sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
                else
                    Object.Destroy(tex);
            }
            cache[name] = sprite;
            return sprite;
        }

        public static bool Has(string name) => Get(name) != null;

        /// <summary>가로 폭 w 에 맞췄을 때 이미지 비율대로의 높이.</summary>
        public static float HeightFor(Sprite s, float w) => w * s.rect.height / s.rect.width;

        /// <summary>세로 높이 h 에 맞췄을 때 이미지 비율대로의 폭.</summary>
        public static float WidthFor(Sprite s, float h) => h * s.rect.width / s.rect.height;

        /// <summary>스킨 이미지를 (x, y) 에 w×h 로 놓는다. 이미지가 없으면 아무것도 만들지 않고 null.</summary>
        public static Image Img(string name, Transform parent, float x, float y, float w, float h, float pivotX = 0.5f, float pivotY = 0.5f)
        {
            Sprite s = Get(name);
            if (s == null) return null;
            var img = UI.Img(name, parent, s, Color.white);
            UI.Place(img.rectTransform, x, y, w, h, pivotX, pivotY);
            return img;
        }

        /// <summary>스킨 이미지를 높이 h 에 맞춰(폭은 비율대로) 놓는다. 없으면 null.</summary>
        public static Image ImgH(string name, Transform parent, float x, float y, float h, float pivotX = 0.5f, float pivotY = 0.5f)
        {
            Sprite s = Get(name);
            return s == null ? null : Img(name, parent, x, y, WidthFor(s, h), h, pivotX, pivotY);
        }

        /// <summary>창 틀. 스킨 이미지가 있으면 그것을, 없으면 코드로 그린 기본 패널을 쓴다.</summary>
        public static Image Panel(string skinName, Transform parent, float x, float y, float w, float h, Color fill, Color frame)
        {
            return Img(skinName, parent, x, y, w, h) ?? Art.Panel(skinName, parent, x, y, w, h, fill, frame);
        }

        static Sprite Or(Sprite assigned, string name) => assigned != null ? assigned : Get(name);

        /// <summary>GameSkin 의 빈 칸을 폴더의 이미지로 채운 사본을 만든다. 인스펙터에서 직접 넣은 이미지가 우선이다.</summary>
        public static GameSkin Fill(GameSkin source)
        {
            GameSkin s = Object.Instantiate(source);
            s.menuBackground = Or(s.menuBackground, "bg_select");
            s.playBackground = Or(s.playBackground, "bg_play");
            s.trackBackground = Or(s.trackBackground, "track_bg");
            s.noteWhite = Or(s.noteWhite, "note_white");
            s.noteBlue = Or(s.noteBlue, "note_blue");
            s.longBodyWhite = Or(s.longBodyWhite, "long_body_white");
            s.longBodyBlue = Or(s.longBodyBlue, "long_body_blue");
            s.judgeLine = Or(s.judgeLine, "judge_line");
            s.keyGlow = Or(s.keyGlow, "key_beam");
            s.hitEffect = Or(s.hitEffect, "hit_effect");
            s.judgePerfect = Or(s.judgePerfect, "judge_perfect");
            s.judgeGreat = Or(s.judgeGreat, "judge_great");
            s.judgeGood = Or(s.judgeGood, "judge_good");
            s.judgeMiss = Or(s.judgeMiss, "judge_miss");
            return s;
        }
    }

    /// <summary>숫자 이미지(digit_0 ~ digit_9)로 수를 그린다. 가운데 정렬.</summary>
    public class DigitRow : MonoBehaviour
    {
        readonly List<Image> pool = new List<Image>();
        float height, advance;

        public static bool Available
        {
            get
            {
                for (int i = 0; i < 10; i++) if (!Skin.Has("digit_" + i)) return false;
                return true;
            }
        }

        public static DigitRow Create(Transform parent, float x, float y, float height)
        {
            var rt = UI.Node("Digits", parent);
            UI.Place(rt, x, y, 10, height);
            var row = rt.gameObject.AddComponent<DigitRow>();
            row.height = height;
            row.advance = Skin.WidthFor(Skin.Get("digit_0"), height) * 0.74f;   // 숫자 칸 양옆의 여백만큼 겹쳐 놓는다
            return row;
        }

        public void Set(string digits)
        {
            float cellW = Skin.WidthFor(Skin.Get("digit_0"), height);
            float x0 = -advance * (digits.Length - 1) / 2f;
            for (int i = 0; i < Mathf.Max(pool.Count, digits.Length); i++)
            {
                if (i >= pool.Count) pool.Add(UI.Img("Digit", transform, null, Color.white));
                bool on = i < digits.Length && digits[i] >= '0' && digits[i] <= '9';
                pool[i].enabled = on;
                if (!on) continue;
                pool[i].sprite = Skin.Get("digit_" + digits[i]);
                UI.Place(pool[i].rectTransform, x0 + advance * i, 0, cellW, height);
            }
        }
    }
}
