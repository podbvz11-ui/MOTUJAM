using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Motujam
{
    /// <summary>게임을 켜면 처음 나오는 화면. ENTER 를 누르면 곡 선택으로 넘어간다.</summary>
    public class TitleScreen : MonoBehaviour
    {
        const float BeatSeconds = 0.5f;   // 로고가 뛰는 박자 (120 BPM)

        GameRoot root;
        Backdrop backdrop;
        RectTransform logo, character;
        Graphic logoGraphic;
        Text press;
        float born, logoIn;
        bool leaving, ready;

        public void Init(GameRoot root)
        {
            this.root = root;
            born = Time.unscaledTime;
            var rt = (RectTransform)transform;

            Sprite bg = Skin.Get("bg_title");
            backdrop = Backdrop.Create(rt, bg != null ? bg : root.skin.menuBackground, null);

            Sprite logoSprite = Skin.Get("logo");
            if (logoSprite != null)
            {
                logoGraphic = Skin.Img("logo", rt, 0, 110, 960, Skin.HeightFor(logoSprite, 960));
            }
            else
            {
                var t = UI.Txt("Logo", rt, "MOTUJAM", 190, TextAnchor.MiddleCenter, Color.white, 0, 110, 1600, 260);
                t.fontStyle = FontStyle.BoldAndItalic;
                Art.Neon(t, new Color(0.1f, 0.6f, 1f, 0.7f), 8f);
                logoGraphic = t;
            }
            logo = logoGraphic.rectTransform;

            Sprite ch = Skin.Get("char_title");
            if (ch != null) character = Skin.Img("char_title", rt, -690, -400, Skin.WidthFor(ch, 500), 500, 0.5f, 0f).rectTransform;

            press = UI.Txt("Press", rt, "PRESS ENTER", 44, TextAnchor.MiddleCenter, Color.white, 0, -300, 900, 70);
            press.fontStyle = FontStyle.BoldAndItalic;
            Art.Neon(press, new Color(0.1f, 0.6f, 1f, 0.7f), 3f);
            var keys = UI.Txt("Keys", rt, "4K  A S ; '        6K  A S D L ; '        F8/F9 해상도        F11 창 모드        ESC 게임 끝내기", 26, TextAnchor.MiddleCenter, new Color(1, 1, 1, 0.55f), 0, -480, 1600, 50);

            // 등장: 로고가 크게 나타나며 자리 잡고, 캐릭터가 아래에서 올라온다
            Tween.Run(this, 0.6f, k => logoIn = k, 0.15f);
            if (character != null) Tween.SlideIn(this, character, new Vector2(0, -260), 0.5f, 0.35f);
            Tween.SlideIn(this, keys.rectTransform, new Vector2(0, -40), 0.4f, 0.7f);
            Tween.After(this, 0.75f, () => { ready = true; Tween.Flash(this, transform, new Color(1, 1, 1, 0.35f), 0.4f); });
        }

        void Update()
        {
            float t = Time.unscaledTime - born;
            float beat = Mathf.Exp(-Mathf.Repeat(t / BeatSeconds, 1f) * 4f);   // 박자마다 1 에서 0 으로 줄어든다

            float appear = Tween.OutBack(Mathf.Clamp01(logoIn));
            logo.localScale = Vector3.one * (Mathf.Lerp(1.6f, 1f, appear) + (ready ? 0.03f * beat : 0f));
            Color lc = logoGraphic.color;
            lc.a = Mathf.Clamp01(logoIn * 2.5f);
            logoGraphic.color = lc;
            backdrop.pulse = ready ? beat : 0f;

            if (character != null)   // 캐릭터가 박자에 맞춰 통통 튄다
                character.localScale = new Vector3(1f - 0.03f * beat, 1f + 0.045f * beat, 1f);

            Color c = press.color;
            c.a = !ready ? 0f : leaving ? Mathf.Round(Mathf.Repeat(t * 14f, 1f)) : 0.55f + 0.45f * Mathf.Sin(t * 4f);
            press.color = c;

            var kb = Keyboard.current;
            if (kb == null || leaving || !ready) return;
            if (kb.escapeKey.wasPressedThisFrame) { Application.Quit(); return; }   // 타이틀에서 ESC: 게임 끝내기
            if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame)
            {
                leaving = true;
                Sfx.Start();
                Tween.Flash(this, transform, new Color(1, 1, 1, 0.8f), 0.5f);
                Tween.After(this, 0.55f, () => root.ShowSelect());
            }
        }
    }
}
