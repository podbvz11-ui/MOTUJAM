using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Motujam
{
    /// <summary>게임을 켜면 타이틀보다 먼저 나오는 안내 문구. ENTER 를 누르거나 잠시 기다리면 타이틀로 넘어간다.</summary>
    public class NoticeScreen : MonoBehaviour
    {
        const float MinSeconds = 1.2f;    // 실수로 바로 넘기지 않도록 이 시간 동안은 키를 받지 않는다
        const float AutoSeconds = 12f;    // 아무것도 누르지 않아도 이만큼 지나면 넘어간다

        const string Message =
            "SOOP 인터넷 방송 모치의 클립 영상을 따온 음악으로\n" +
            "노래 시작 전과 끝난 후에 추가적인 스트리머의 떠드는 소리가 나올 수 있습니다.\n\n" +
            "박자 또한 매우 불안정하므로\n" +
            "감안하고 게임을 즐겨주셨으면 합니다.";

        GameRoot root;
        CanvasGroup group;
        Text press;
        float born;
        bool leaving;

        public void Init(GameRoot root)
        {
            this.root = root;
            born = Time.unscaledTime;
            var rt = (RectTransform)transform;
            group = gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;

            UI.Stretch(UI.Img("Black", rt, null, new Color(0.01f, 0.01f, 0.04f, 1f)).rectTransform);

            var head = UI.Txt("Head", rt, "안내", 54, TextAnchor.MiddleCenter, new Color(1f, 0.82f, 0.3f), 0, 250, 1600, 80);
            head.fontStyle = FontStyle.Bold;
            var line = UI.Img("Line", rt, null, new Color(1f, 0.82f, 0.3f, 0.6f));
            UI.Place(line.rectTransform, 0, 195, 520, 3);

            var body = UI.Txt("Body", rt, Message, 40, TextAnchor.MiddleCenter, Color.white, 0, 10, 1700, 420);
            body.lineSpacing = 1.35f;

            press = UI.Txt("Press", rt, "ENTER 를 누르면 넘어갑니다", 30, TextAnchor.MiddleCenter, new Color(1, 1, 1, 0.6f), 0, -330, 1200, 50);
        }

        void Update()
        {
            float t = Time.unscaledTime - born;
            if (!leaving) group.alpha = Mathf.Clamp01(t / 0.6f);

            Color c = press.color;
            c.a = t < MinSeconds ? 0f : 0.35f + 0.3f * Mathf.Sin(t * 4f);
            press.color = c;

            if (leaving || t < MinSeconds) return;
            var kb = Keyboard.current;
            bool pressed = kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame || kb.escapeKey.wasPressedThisFrame);
            if (pressed || t >= AutoSeconds)
            {
                leaving = true;
                if (pressed) Sfx.Change();
                Tween.Run(this, 0.4f, k => group.alpha = 1f - k);
                Tween.After(this, 0.42f, () => root.ShowTitle());
            }
        }
    }
}
