using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Motujam
{
    public class ResultScreen : MonoBehaviour
    {
        GameRoot root;
        PlayResult result;
        RectTransform rankRt, charRt, fullRt;
        Graphic rankG, fullG;
        Text scoreValue;
        float born;
        bool done, leaving;

        public void Init(GameRoot root, PlayResult r)
        {
            this.root = root;
            result = r;
            born = Time.unscaledTime;
            var rt = (RectTransform)transform;

            Sprite bg = Skin.Get("bg_result");
            Backdrop.Create(rt, bg != null ? bg : root.skin.menuBackground, r.song.GetJacket());

            RectTransform left = Group("Left", rt), right = Group("Right", rt), top = Group("Top", rt);

            Color panel = new Color(0.03f, 0.04f, 0.1f, 0.6f);
            Skin.Panel("panel_rank", left, -420, -40, 580, 620, panel, new Color(1f, 0.82f, 0.3f, 0.8f));
            Skin.Panel("panel_result", right, 330, -40, 850, 624, panel, new Color(0.2f, 0.85f, 1f, 0.8f));

            if (Skin.ImgH("title_result", top, 0, 460, 84) == null)
            {
                var header = UI.Txt("Header", top, "RESULT", 58, TextAnchor.MiddleCenter, Color.white, 0, 460);
                header.fontStyle = FontStyle.BoldAndItalic;
                Art.Neon(header, new Color(0.1f, 0.6f, 1f, 0.7f), 4f);
            }
            UI.Txt("Title", top, r.song.title, 44, TextAnchor.MiddleCenter, Color.white, 0, 380, 1600, 60);
            UI.Txt("Chart", top, $"{r.chart.keys}K  {SelectScreen.DifficultyName(r.chart.difficulty)}  Lv.{r.chart.level}", 30, TextAnchor.MiddleCenter, new Color(1f, 0.8f, 0.3f), 0, 325);

            // 등급: 숫자가 다 올라간 뒤 도장처럼 찍힌다
            string grade = JudgeRule.Rank(r.accuracy);
            rankG = Skin.ImgH("rank_" + grade.ToLowerInvariant(), rt, -420, 20, 400);
            if (rankG == null)
            {
                var rank = UI.Txt("Rank", rt, grade, 300, TextAnchor.MiddleCenter, new Color(1f, 0.85f, 0.35f), -420, 20, 500, 400);
                rank.fontStyle = FontStyle.BoldAndItalic;
                Art.Neon(rank, new Color(1f, 0.4f, 0.1f, 0.6f), 8f);
                rankG = rank;
            }
            rankRt = rankG.rectTransform;
            SetAlpha(rankG, 0f);

            if (r.FullCombo)
            {
                fullG = Skin.ImgH("full_combo", rt, -420, -235, 96);
                if (fullG == null)
                {
                    var t = UI.Txt("FullCombo", rt, "FULL COMBO", 44, TextAnchor.MiddleCenter, new Color(1f, 0.6f, 0.9f), -420, -200);
                    t.fontStyle = FontStyle.Bold;
                    fullG = t;
                }
                fullRt = fullG.rectTransform;
                SetAlpha(fullG, 0f);
            }

            // 캐릭터 그림이 있으면 등급에 따라 보여 준다
            string charName = grade == "C" || grade == "D" ? "char_result_bad" : "char_result_good";
            Sprite ch = Skin.Get(charName);
            if (ch != null)
            {
                var img = Skin.Img(charName, rt, -822, -360, 256, Skin.HeightFor(ch, 256), 0.5f, 0f);
                charRt = img.rectTransform;
                charRt.localScale = Vector3.zero;
            }

            float x = 330, y = 200;
            var lines = new System.Collections.Generic.List<(Text label, Text value, string text)>();
            lines.Add(Row(right, "SCORE", "0", Color.white, x, y)); y -= 80;
            scoreValue = lines[0].value;
            lines.Add(Row(right, "ACCURACY", r.accuracy.ToString("0.00") + "%", Color.white, x, y)); y -= 80;
            lines.Add(Row(right, "MAX COMBO", r.maxCombo.ToString(), Color.white, x, y)); y -= 100;
            for (int i = 0; i < 4; i++)
            {
                var j = (Judge)i;
                lines.Add(Row(right, JudgeRule.Name(j), r.counts[i].ToString(), JudgeRule.ColorOf(j), x, y));
                y -= 64;
            }

            var help = UI.Txt("Help", rt, "ENTER 곡 선택으로    R 다시 하기", 28, TextAnchor.MiddleCenter, new Color(1, 1, 1, 0.55f), 0, -480, 1600, 50);
            SetAlpha(help, 0f);

            Tween.SlideIn(this, top, new Vector2(0, 90), 0.35f);
            Tween.SlideIn(this, left, new Vector2(-260, 0), 0.4f, 0.05f);
            Tween.SlideIn(this, right, new Vector2(260, 0), 0.4f, 0.12f);
            StartCoroutine(Reveal(lines, help));
        }

        IEnumerator Reveal(System.Collections.Generic.List<(Text label, Text value, string text)> lines, Text help)
        {
            foreach (var l in lines) { SetAlpha(l.label, 0f); SetAlpha(l.value, 0f); }
            yield return new WaitForSecondsRealtime(0.5f);

            // 1) 줄이 하나씩 나타난다
            for (int i = 0; i < lines.Count; i++)
            {
                var l = lines[i];
                Tween.Run(this, 0.18f, k => { SetAlpha(l.label, k); SetAlpha(l.value, k); l.value.rectTransform.localScale = Vector3.one * (1.08f - 0.08f * k); });
                Sfx.Count();
                if (i == 0)
                {
                    // 점수는 0 에서부터 올라간다
                    float dur = 0.9f, next = 0f;
                    for (float t = 0f; t < dur; t += Time.unscaledDeltaTime)
                    {
                        float k = Tween.OutCubic(t / dur);
                        scoreValue.text = Mathf.RoundToInt(result.score * k).ToString("N0");
                        if (t >= next) { Sfx.Count(); next = t + 0.05f; }
                        yield return null;
                    }
                    scoreValue.text = result.score.ToString("N0");
                }
                else yield return new WaitForSecondsRealtime(0.11f);
            }
            yield return new WaitForSecondsRealtime(0.25f);

            // 2) 등급이 크게 날아와 찍힌다
            Sfx.Rank();
            Tween.Flash(this, transform, new Color(1, 1, 1, 0.6f), 0.4f);
            Tween.Run(this, 0.28f, k =>
            {
                rankRt.localScale = Vector3.one * Mathf.Lerp(3.2f, 1f, Tween.OutCubic(k));
                SetAlpha(rankG, Mathf.Clamp01(k * 3f));
            });
            var self = (RectTransform)transform;
            Tween.Run(this, 0.3f, k => self.anchoredPosition = new Vector2(Mathf.Sin(k * 40f), Mathf.Cos(k * 33f)) * 10f * (1f - k), 0.26f);   // 화면이 살짝 흔들린다
            yield return new WaitForSecondsRealtime(0.45f);

            // 3) 풀 콤보와 캐릭터
            if (fullG != null)
            {
                Sfx.Sparkle();
                Tween.Run(this, 0.35f, k => { fullRt.localScale = Vector3.one * Tween.OutBack(k); SetAlpha(fullG, Mathf.Clamp01(k * 2f)); });
            }
            if (charRt != null) Tween.Run(this, 0.4f, k => charRt.localScale = Vector3.one * Tween.OutBack(k));
            Tween.Run(this, 0.3f, k => SetAlpha(help, 0.55f * k));
            done = true;
        }

        void Update()
        {
            float t = Time.unscaledTime - born;
            if (done)
            {
                // 등급이 천천히 숨 쉬고, 캐릭터가 통통 튄다
                rankRt.localScale = Vector3.one * (1f + 0.025f * Mathf.Sin(t * 3f));
                float hop = Mathf.Exp(-Mathf.Repeat(t / 0.5f, 1f) * 4f);
                if (charRt != null) charRt.localScale = new Vector3(1f - 0.03f * hop, 1f + 0.045f * hop, 1f);
            }

            var kb = Keyboard.current;
            if (kb == null || leaving) return;
            if (kb.rKey.wasPressedThisFrame)
            {
                leaving = true;
                Sfx.Confirm();
                root.StartGame(result.song, result.chart);
            }
            else if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.escapeKey.wasPressedThisFrame)
            {
                leaving = true;
                Sfx.Back();
                root.ShowSelect();
            }
        }

        static RectTransform Group(string name, Transform parent)
        {
            var rt = UI.Node(name, parent);
            UI.Stretch(rt);
            return rt;
        }

        static void SetAlpha(Graphic g, float a)
        {
            Color c = g.color;
            c.a = a;
            g.color = c;
        }

        static (Text label, Text value, string text) Row(Transform parent, string label, string value, Color color, float x, float y)
        {
            Text l = UI.Txt(label, parent, label, 36, TextAnchor.MiddleLeft, color, x, y, 700, 60);
            Text v = UI.Txt(label + "Value", parent, value, 44, TextAnchor.MiddleRight, Color.white, x, y, 700, 60);
            return (l, v, value);
        }
    }
}
