using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Motujam
{
    /// <summary>곡 선택 화면. 키보드로만 조작한다.</summary>
    public class SelectScreen : MonoBehaviour
    {
        const int Rows = 9;
        const float RowH = 80f;

        static int songIndex, chartIndex;

        GameRoot root;
        List<SongData> songs;
        readonly Text[] rows = new Text[Rows];
        Backdrop backdrop;
        Image cursor, keyBadge, diffBadge, arrowL, arrowR;
        RawImage jacket;
        RectTransform rowsRoot, jacketRoot, badgeRoot;
        Text title, artist, info, chartText, levelText, settingText, loadingText;
        bool starting, cursorSkinned, badges;

        // 연출 값: 바꿀 때 1 로 올렸다가 매 프레임 0 으로 줄인다
        float rowSlide, jacketPop, badgePop, cursorPop, arrowKick, beatClock;
        int arrowSide;
        SongData shownSong;

        public void Init(GameRoot root)
        {
            this.root = root;
            songs = root.songs;
            var rt = (RectTransform)transform;

            backdrop = Backdrop.Create(rt, root.skin.menuBackground, null);

            RectTransform header;
            Image headerImg = Skin.ImgH("title_select", rt, -880, 470, 84, 0f);
            if (headerImg != null) header = headerImg.rectTransform;
            else
            {
                var h = UI.Txt("Header", rt, "SELECT MUSIC", 58, TextAnchor.MiddleLeft, Color.white, -560, 470);
                h.fontStyle = FontStyle.BoldAndItalic;
                Art.Neon(h, new Color(0.1f, 0.6f, 1f, 0.7f), 4f);
                header = h.rectTransform;
            }

            // ----- 왼쪽: 곡 목록 -----
            RectTransform list = Group("List", rt);
            Color panel = new Color(0.03f, 0.04f, 0.1f, 0.6f);
            Skin.Panel("panel_list", list, -480, 0, 850, 810, panel, new Color(0.2f, 0.85f, 1f, 0.7f));
            cursorSkinned = Skin.Has("row_selected");
            cursor = Skin.Panel("row_selected", list, -480, 0, 790, 92, new Color(1f, 0.8f, 0.3f, 0.22f), new Color(1f, 0.85f, 0.4f, 1f));
            rowsRoot = Group("Rows", list);
            for (int i = 0; i < Rows; i++)
                rows[i] = UI.Txt("Row" + i, rowsRoot, "", 38, TextAnchor.MiddleLeft, Color.white, -450, (Rows / 2 - i) * RowH, 660, 76);

            // ----- 오른쪽: 고른 곡 -----
            RectTransform infoGroup = Group("Info", rt);
            Skin.Panel("panel_info", infoGroup, 480, 20, 880, 900, panel, new Color(1f, 0.3f, 0.75f, 0.7f));
            jacketRoot = UI.Node("JacketRoot", infoGroup);
            UI.Place(jacketRoot, 480, 200, 10, 10);
            jacket = UI.RawImg("Jacket", jacketRoot, null, Color.white);
            UI.Place(jacket.rectTransform, 0, 0, 420, 420);
            Skin.Img("jacket_frame", jacketRoot, 0, 0, 468, 468);
            title = UI.Txt("Title", infoGroup, "", 52, TextAnchor.MiddleCenter, Color.white, 480, -60, 800, 70);
            title.fontStyle = FontStyle.Bold;
            artist = UI.Txt("Artist", infoGroup, "", 32, TextAnchor.MiddleCenter, new Color(1, 1, 1, 0.7f), 480, -115, 800, 50);
            chartText = UI.Txt("Chart", infoGroup, "", 44, TextAnchor.MiddleCenter, new Color(1f, 0.8f, 0.3f), 480, -200, 800, 60);
            badges = Skin.Has("badge_4k") && Skin.Has("badge_6k") && Skin.Has("badge_easy") && Skin.Has("badge_normal") && Skin.Has("badge_hard");
            if (badges)
            {
                badgeRoot = UI.Node("Badges", infoGroup);
                UI.Place(badgeRoot, 480, -200, 10, 10);
                keyBadge = Skin.ImgH("badge_4k", badgeRoot, -190, 0, 62);
                diffBadge = Skin.ImgH("badge_easy", badgeRoot, -20, 0, 62);
                diffBadge.preserveAspect = true;
                levelText = UI.Txt("Level", badgeRoot, "", 40, TextAnchor.MiddleLeft, new Color(1f, 0.8f, 0.3f), 190, 0, 180, 60);
                levelText.fontStyle = FontStyle.BoldAndItalic;
                arrowR = Skin.ImgH("arrow", infoGroup, 480 + 340, -200, 44);
                arrowL = Skin.ImgH("arrow", infoGroup, 480 - 340, -200, 44);
                if (arrowL != null) arrowL.rectTransform.localScale = new Vector3(-1, 1, 1);
            }
            info = UI.Txt("Info", infoGroup, "", 28, TextAnchor.MiddleCenter, new Color(1, 1, 1, 0.7f), 480, -260, 800, 50);
            settingText = UI.Txt("Settings", infoGroup, "", 28, TextAnchor.MiddleCenter, new Color(0.6f, 0.9f, 1f), 480, -350, 800, 50);
            loadingText = UI.Txt("Loading", infoGroup, "", 40, TextAnchor.MiddleCenter, Color.white, 480, -420, 800, 60);

            var help = UI.Txt("Help", rt, "↑↓ 곡 선택    ←→ 키 / 난이도    ENTER 시작    ESC 타이틀    F1/F2 노트 속도    F3/F4 판정 보정    F5 새로고침    F6 배경 영상",
                25, TextAnchor.MiddleCenter, new Color(1, 1, 1, 0.55f), 0, -500, 1880, 50);

            // 등장: 양쪽 창이 옆에서 미끄러져 들어온다
            Tween.SlideIn(this, header, new Vector2(0, 90), 0.35f);
            Tween.SlideIn(this, list, new Vector2(-260, 0), 0.4f, 0.05f);
            Tween.SlideIn(this, infoGroup, new Vector2(260, 0), 0.4f, 0.12f);
            Tween.SlideIn(this, help.rectTransform, new Vector2(0, -40), 0.35f, 0.3f);

            Refresh();
        }

        static RectTransform Group(string name, Transform parent)
        {
            var rt = UI.Node(name, parent);
            UI.Stretch(rt);
            return rt;
        }

        void OnDestroy()
        {
            if (root != null && root.preview != null) root.preview.Stop();
        }

        void Update()
        {
            Animate();

            var kb = Keyboard.current;
            if (kb == null || starting || songs.Count == 0) return;

            int songStep = 0, chartStep = 0;
            bool setting = false;
            if (kb.upArrowKey.wasPressedThisFrame) songStep = -1;
            if (kb.downArrowKey.wasPressedThisFrame) songStep = 1;
            if (kb.leftArrowKey.wasPressedThisFrame) chartStep = -1;
            if (kb.rightArrowKey.wasPressedThisFrame) chartStep = 1;
            if (kb.f1Key.wasPressedThisFrame) { Settings.Speed -= 0.1f; setting = true; }
            if (kb.f2Key.wasPressedThisFrame) { Settings.Speed += 0.1f; setting = true; }
            if (kb.f3Key.wasPressedThisFrame) { Settings.OffsetMs -= 5; setting = true; }
            if (kb.f4Key.wasPressedThisFrame) { Settings.OffsetMs += 5; setting = true; }
            if (kb.f5Key.wasPressedThisFrame) { Sfx.Confirm(); root.ReloadSongs(); root.ReloadSkin(); root.ShowSelect(); return; }
            if (kb.f6Key.wasPressedThisFrame) { Settings.Video = !Settings.Video; setting = true; }
            // 자동 연주는 안내에 적지 않는 관리자용 기능이다: Ctrl + Shift + F7
            if (kb.f7Key.wasPressedThisFrame && kb.ctrlKey.isPressed && kb.shiftKey.isPressed) { Settings.Auto = !Settings.Auto; setting = true; }
            if (kb.escapeKey.wasPressedThisFrame) { Sfx.Back(); root.ShowTitle(); return; }

            if (songStep != 0)
            {
                songIndex += songStep;
                rowSlide = songStep;      // 목록이 한 칸 밀려 들어온다
                jacketPop = cursorPop = 1f;
                Sfx.Move();
            }
            if (chartStep != 0)
            {
                chartIndex += chartStep;
                badgePop = arrowKick = 1f;
                arrowSide = chartStep;
                Sfx.Change();
            }
            if (setting) Sfx.Change();
            if (songStep != 0 || chartStep != 0 || setting) Refresh();

            if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)
            {
                starting = true;
                loadingText.text = "불러오는 중...";
                Sfx.Confirm();
                root.preview.Stop();
                jacketPop = cursorPop = 1.6f;
                Tween.Flash(this, transform, new Color(1, 1, 1, 0.55f), 0.45f);
                SongData song = songs[songIndex];
                ChartData chart = song.charts[chartIndex];
                Tween.After(this, 0.45f, () => root.StartGame(song, chart));
            }
        }

        void Animate()
        {
            float dt = Time.unscaledDeltaTime, t = Time.unscaledTime;
            rowSlide = Mathf.MoveTowards(rowSlide, 0f, dt * 9f);
            jacketPop = Mathf.MoveTowards(jacketPop, 0f, dt * 5f);
            badgePop = Mathf.MoveTowards(badgePop, 0f, dt * 6f);
            cursorPop = Mathf.MoveTowards(cursorPop, 0f, dt * 6f);
            arrowKick = Mathf.MoveTowards(arrowKick, 0f, dt * 5f);

            // 미리듣기가 나오는 동안 곡의 빠르기에 맞춰 화면이 뛴다
            float bpm = shownSong != null && shownSong.bpm > 0 ? shownSong.bpm : 120f;
            while (bpm > 150f) bpm *= 0.5f;
            beatClock += dt * bpm / 60f;
            float beat = root.preview.Audible ? Mathf.Exp(-Mathf.Repeat(beatClock, 1f) * 4.5f) : 0f;
            backdrop.pulse = beat;

            rowsRoot.anchoredPosition = new Vector2(0, -rowSlide * rowSlide * Mathf.Sign(rowSlide) * RowH * 0.6f);
            jacketRoot.localScale = Vector3.one * (1f - 0.08f * Mathf.Min(1f, jacketPop) + 0.12f * Mathf.Max(0f, jacketPop - 1f) + 0.02f * beat);
            jacket.color = new Color(1, 1, 1, jacket.texture != null ? 1f - 0.5f * Mathf.Min(1f, jacketPop) : 0.03f);
            if (badgeRoot != null) badgeRoot.localScale = Vector3.one * (1f + 0.18f * badgePop);

            // 선택 막대: 은은하게 깜빡이고, 곡을 바꾸면 살짝 커졌다 돌아온다
            Color cc = cursor.color;
            cc.a = cursorSkinned ? 0.85f + 0.15f * Mathf.Sin(t * 5f) : 0.2f + 0.1f * Mathf.Sin(t * 5f);
            cursor.color = cc;
            cursor.rectTransform.localScale = new Vector3(1f + 0.03f * cursorPop, 1f + 0.12f * cursorPop, 1f);

            // 좌우 화살표: 천천히 흔들리다가, 누른 쪽이 튕긴다
            float sway = 5f * Mathf.Sin(t * 4f);
            if (arrowR != null) arrowR.rectTransform.anchoredPosition = new Vector2(480 + 340 + sway + (arrowSide > 0 ? 16f * arrowKick : 0f), -200);
            if (arrowL != null) arrowL.rectTransform.anchoredPosition = new Vector2(480 - 340 - sway - (arrowSide < 0 ? 16f * arrowKick : 0f), -200);

            if (starting)
            {
                Color lc = loadingText.color;
                lc.a = 0.6f + 0.4f * Mathf.Sin(t * 10f);
                loadingText.color = lc;
            }
        }

        /// <summary>화면에 보여 줄 난이도 이름.</summary>
        public static string DifficultyName(string difficulty)
        {
            switch (difficulty)
            {
                case "VeryHard": return "VERY HARD";
                case "Extreme": return "EXTREME";
                default: return difficulty;
            }
        }

        static Color DifficultyColor(string difficulty)
        {
            switch (difficulty)
            {
                case "VeryHard": return new Color(0.75f, 0.5f, 1f);
                case "Extreme": return new Color(1f, 0.3f, 0.25f);
                default: return new Color(1f, 0.8f, 0.3f);
            }
        }

        void Refresh()
        {
            songIndex = (songIndex % songs.Count + songs.Count) % songs.Count;
            SongData song = songs[songIndex];
            chartIndex = (chartIndex % song.charts.Count + song.charts.Count) % song.charts.Count;
            ChartData chart = song.charts[chartIndex];

            for (int i = 0; i < Rows; i++)
            {
                int idx = songIndex + i - Rows / 2;
                bool valid = idx >= 0 && idx < songs.Count;
                int far = Mathf.Abs(i - Rows / 2);
                rows[i].text = valid ? songs[idx].title : "";
                rows[i].color = far == 0 ? Color.white : new Color(1, 1, 1, Mathf.Lerp(0.55f, 0.18f, (far - 1) / 3f));   // 멀수록 흐리게
                rows[i].fontStyle = far == 0 ? FontStyle.Bold : FontStyle.Normal;
            }

            Texture2D tex = song.GetJacket();
            Sprite fallback = Skin.Get("jacket_default");
            jacket.texture = tex != null ? tex : (fallback != null ? fallback.texture : null);
            title.text = song.title;
            artist.text = song.artist;
            Sprite diff = badges ? Skin.Get("badge_" + chart.difficulty.ToLowerInvariant()) : null;
            bool useBadges = diff != null && (chart.keys == 4 || chart.keys == 6);
            if (badges)
            {
                keyBadge.enabled = diffBadge.enabled = useBadges;
                levelText.text = useBadges ? $"Lv.{chart.level}" : "";
                if (useBadges)
                {
                    keyBadge.sprite = Skin.Get(chart.keys == 6 ? "badge_6k" : "badge_4k");
                    diffBadge.sprite = diff;
                }
            }
            chartText.text = useBadges ? "" : song.charts.Count > 1 && arrowL == null
                ? $"◀  {chart.keys}K  {DifficultyName(chart.difficulty)}  Lv.{chart.level}  ▶"
                : $"{chart.keys}K  {DifficultyName(chart.difficulty)}  Lv.{chart.level}";
            chartText.color = DifficultyColor(chart.difficulty);
            info.text = $"BPM {song.bpm:0.#}    노트 {chart.notes.Count}개";
            settingText.text = $"노트 속도 x{Settings.Speed:0.0}    판정 보정 {Settings.OffsetMs:+0;-0;0}ms    배경 영상 {(Settings.Video ? "켜짐" : "꺼짐")}{(Settings.Auto ? "    자동 연주 켜짐" : "")}";

            if (song != shownSong)
            {
                shownSong = song;
                beatClock = 0f;
                root.preview.Play(song);   // 잠시 머무르면 이 곡이 흘러나온다
            }
        }
    }
}
