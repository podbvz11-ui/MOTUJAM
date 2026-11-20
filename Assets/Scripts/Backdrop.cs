using UnityEngine;
using UnityEngine.UI;

namespace Motujam
{
    /// <summary>움직이는 배경: 그라데이션 위로 빛 방울이 천천히 떠오른다. 스킨 배경 이미지가 있으면 그 이미지만 보여 준다.</summary>
    public class Backdrop : MonoBehaviour
    {
        const int DotCount = 42;

        static readonly Color[] Palette =
        {
            new Color(0.2f, 0.8f, 1f), new Color(1f, 0.3f, 0.8f), new Color(0.6f, 0.4f, 1f), new Color(1f, 0.8f, 0.4f),
        };

        RectTransform[] dots;
        Image[] images;
        float[] speed, phase, alpha;

        /// <summary>박자에 맞춘 번쩍임 (0~1). 플레이 화면이 매 프레임 넣어 준다.</summary>
        [System.NonSerialized] public float pulse;

        public static Backdrop Create(Transform parent, Sprite skinSprite, Texture jacket)
        {
            var rt = UI.Node("Backdrop", parent);
            UI.Stretch(rt);
            var b = rt.gameObject.AddComponent<Backdrop>();
            b.Build(skinSprite, jacket);
            return b;
        }

        void Build(Sprite skinSprite, Texture jacket)
        {
            if (skinSprite != null)
            {
                UI.Stretch(UI.Img("Image", transform, skinSprite, Color.white).rectTransform);
                return;
            }

            UI.Stretch(UI.RawImg("Gradient", transform, Art.BgGradient, Color.white).rectTransform);
            if (jacket != null)
                UI.Stretch(UI.RawImg("Jacket", transform, jacket, new Color(1, 1, 1, 0.13f)).rectTransform);

            var rng = new System.Random(3);
            dots = new RectTransform[DotCount];
            images = new Image[DotCount];
            speed = new float[DotCount];
            phase = new float[DotCount];
            alpha = new float[DotCount];
            for (int i = 0; i < DotCount; i++)
            {
                float size = Mathf.Lerp(30f, 260f, (float)(rng.NextDouble() * rng.NextDouble()));
                images[i] = UI.Img("Dot", transform, Art.Glow, Palette[rng.Next(Palette.Length)]);
                dots[i] = images[i].rectTransform;
                UI.Place(dots[i], (float)(rng.NextDouble() * 2000 - 1000), (float)(rng.NextDouble() * 1200 - 600), size, size);
                speed[i] = Mathf.Lerp(50f, 8f, size / 260f);
                phase[i] = (float)(rng.NextDouble() * 6.28);
                alpha[i] = Mathf.Lerp(0.3f, 0.08f, size / 260f);
            }
        }

        void Update()
        {
            if (dots == null) return;
            float dt = Time.unscaledDeltaTime, t = Time.unscaledTime;
            for (int i = 0; i < dots.Length; i++)
            {
                Vector2 p = dots[i].anchoredPosition;
                p.y += speed[i] * dt;
                if (p.y > 700f) p.y = -700f;
                dots[i].anchoredPosition = p;
                Color c = images[i].color;
                c.a = alpha[i] * (0.65f + 0.35f * Mathf.Sin(t * 0.8f + phase[i])) * (1f + pulse * 0.6f);
                images[i].color = c;
            }
        }
    }
}
