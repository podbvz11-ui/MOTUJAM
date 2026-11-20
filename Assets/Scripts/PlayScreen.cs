using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Motujam
{
    /// <summary>실제 플레이 화면: 입력, 노트 이동, 판정, 점수, 연출.</summary>
    public class PlayScreen : MonoBehaviour
    {
        const float JudgeY = -370f;   // 판정선 높이
        const float TopY = 560f;
        const float BottomY = -560f;
        const float KeyW = 124f;      // 레인 폭
        const float NoteH = 30f;

        const int Pending = 0, Holding = 1, Done = 2, Failed = 3;

        static readonly Color Cyan = new Color(0.2f, 0.85f, 1f);
        static readonly Color Pink = new Color(1f, 0.3f, 0.75f);
        static readonly Color Gold = new Color(1f, 0.82f, 0.3f);

        class Note
        {
            public NoteData d;
            public int state;
            public Lane lane;
            public RectTransform head, body;
            public Graphic headG, bodyG;
            public bool dimmed;
        }

        class Lane
        {
            public Key key;
            public float x, w;
            public bool blue;
            public Color color;
            public List<Note> notes = new List<Note>();
            public int next, spawn;
            public Image beam, flash, cap;
            public Sprite capUp, capDown;
            public Graphic capFill;
            public Text capLabel;
            public float flashT, holdFxT;
        }

        class Fx
        {
            public RectTransform rt;
            public Graphic g;
            public float t, life, size0, size1, gravity;
            public Vector2 vel;
        }

        GameRoot root;
        SongData song;
        ChartData chart;
        GameSkin skin;

        Lane[] lanes;
        readonly List<Note> active = new List<Note>();
        readonly List<Fx> effects = new List<Fx>();
        readonly System.Random rng = new System.Random();
        RectTransform bodyLayer, headLayer, fxLayer;

        Backdrop backdrop;
        BackgroundVideo video;
        Text judgeText, comboText, comboLabel, scoreText, accText, speedText, timeText;
        int shownSecond = -1;

        // 일시정지
        static readonly string[] PauseItems = { "계속하기", "처음부터 다시", "곡 선택으로" };
        const double ResumeCountdown = 3.0;
        bool paused;
        int pauseIndex;
        double resumeGateMs = double.NegativeInfinity;   // 재개 후 이 시각 전까지는 판정하지 않는다
        RectTransform pauseRoot;
        Image pauseCursor;
        Text[] pauseTexts;
        Text countdownText;
        Image judgeImage, progress, sideL, sideR, judgeGlow, comboLabelImage;
        DigitRow comboDigits;
        RectTransform comboRoot;
        bool progressFilled;
        float judgeT = 99f, trackW, beatMs;

        readonly PlayResult result = new PlayResult();
        int combo, judged, total;
        double weightSum, endMs;
        bool finished;

        public void Init(GameRoot root, SongData song, ChartData chart)
        {
            this.root = root;
            this.song = song;
            this.chart = chart;
            skin = root.skin;
            result.song = song;
            result.chart = chart;
            beatMs = 60000f / (song.bpm > 0 ? song.bpm : 120f);
            BuildLanes();
            BuildView();
            UpdateHud();
        }

        void BuildLanes()
        {
            Key[] keys = chart.keys == 6
                ? new[] { Key.A, Key.S, Key.D, Key.L, Key.Semicolon, Key.Quote }
                : new[] { Key.A, Key.S, Key.Semicolon, Key.Quote };
            bool[] blue = chart.keys == 6
                ? new[] { false, true, false, false, true, false }
                : new[] { false, true, true, false };

            int n = chart.keys;
            trackW = n * KeyW;
            lanes = new Lane[n];
            for (int i = 0; i < n; i++)
            {
                lanes[i] = new Lane
                {
                    key = keys[i], blue = blue[i], w = KeyW, x = -trackW / 2f + KeyW * (i + 0.5f),
                    color = blue[i] ? skin.blueNoteColor : skin.whiteNoteColor,
                };
            }

            foreach (var d in chart.notes)
            {
                var lane = lanes[Mathf.Clamp(d.lane, 0, n - 1)];
                lane.notes.Add(new Note { d = d, lane = lane });
                total += d.len > 0 ? 2 : 1;   // 롱노트는 시작 + 끝 두 번 판정
                endMs = Math.Max(endMs, d.t + d.len);
            }
        }

        // ---------- 화면 구성 ----------

        void BuildView()
        {
            var rt = (RectTransform)transform;
            int n = chart.keys;

            backdrop = Backdrop.Create(rt, skin.playBackground, song.GetJacket());
            if (Settings.Video && song.VideoPath != null) video = BackgroundVideo.Create(rt, song.VideoPath);

            // 트랙 양옆 네온
            sideL = UI.Img("SideGlowL", rt, Art.BandV, Cyan);
            UI.Place(sideL.rectTransform, -trackW / 2f, 0, 140, 1080);
            sideR = UI.Img("SideGlowR", rt, Art.BandV, Pink);
            UI.Place(sideR.rectTransform, trackW / 2f, 0, 140, 1080);

            var track = UI.SkinImg("Track", rt, skin.trackBackground, new Color(0.01f, 0.01f, 0.03f, 0.9f));
            UI.Place(track.rectTransform, 0, 0, trackW, 1080);
            if (skin.trackBackground != null) track.color = new Color(1, 1, 1, 0.93f);

            // 트랙 양옆 기둥 (오른쪽은 좌우를 뒤집는다)
            Sprite pillar = Skin.Get("track_side");
            if (pillar != null)
            {
                float pw = Skin.WidthFor(pillar, 1080);
                Skin.Img("track_side", rt, -trackW / 2f - pw / 2f + 8f, 0, pw, 1080);
                Skin.Img("track_side", rt, trackW / 2f + pw / 2f - 8f, 0, pw, 1080).rectTransform.localScale = new Vector3(-1, 1, 1);
            }

            for (int i = 0; i < n; i++)
            {
                Lane lane = lanes[i];
                if (lane.blue)
                {
                    var tint = UI.Img("LaneTint", rt, null, new Color(0.2f, 0.6f, 1f, 0.05f));
                    UI.Place(tint.rectTransform, lane.x, 0, lane.w, 1080);
                }
                if (i > 0)
                {
                    var line = UI.Img("LaneLine", rt, null, new Color(1, 1, 1, 0.1f));
                    UI.Place(line.rectTransform, lane.x - lane.w / 2f, 0, 2, 1080);
                }
            }

            // 키를 누를 때 올라오는 빛기둥
            foreach (var lane in lanes)
            {
                Color c = lane.color;
                c.a = 0.55f;
                if (skin.keyGlow != null) c.a = 0.9f;
                lane.beam = UI.Img("Beam", rt, skin.keyGlow != null ? skin.keyGlow : UI.Gradient, c);
                UI.Place(lane.beam.rectTransform, lane.x, JudgeY, lane.w, 520, 0.5f, 0f);
                lane.beam.enabled = false;
            }

            bodyLayer = Layer("NoteBodies", rt);

            // 판정선
            if (skin.judgeLine != null)
            {
                var judge = UI.Img("JudgeLine", rt, skin.judgeLine, Color.white);
                UI.Place(judge.rectTransform, 0, JudgeY, trackW, 44);
            }
            judgeGlow = UI.Img("JudgeGlow", rt, Art.BandH, skin.judgeLineColor);
            UI.Place(judgeGlow.rectTransform, 0, JudgeY, trackW, skin.judgeLine != null ? 60 : 90);
            if (skin.judgeLine == null)
            {
                var core = UI.Img("JudgeLine", rt, null, Color.white);
                UI.Place(core.rectTransform, 0, JudgeY, trackW, 5);
            }

            headLayer = Layer("NoteHeads", rt);

            foreach (var lane in lanes)
            {
                lane.flash = UI.Img("LaneFlash", rt, Art.BandH, lane.color);
                UI.Place(lane.flash.rectTransform, lane.x, JudgeY, lane.w, 130);
                lane.flash.enabled = false;
            }
            fxLayer = Layer("Effects", rt);

            // 판정선 아래: 키 버튼
            var cover = UI.Img("KeyPanel", rt, null, new Color(0.03f, 0.03f, 0.07f, 1f));
            UI.Place(cover.rectTransform, 0, (JudgeY - 4 + BottomY) / 2f, trackW, JudgeY - 4 - BottomY);
            for (int i = 0; i < n; i++)
            {
                Lane lane = lanes[i];
                Color frame = lane.color; frame.a = 0.9f;
                lane.capUp = Skin.Get(lane.blue ? "key_blue" : "key_white");
                lane.capDown = Skin.Get(lane.blue ? "key_blue_pressed" : "key_white_pressed");
                if (lane.capUp != null && lane.capDown != null)
                {
                    lane.cap = UI.Img("KeyCap", rt, lane.capUp, Color.white);
                    UI.Place(lane.cap.rectTransform, lane.x, JudgeY - 56, lane.w - 2, Skin.HeightFor(lane.capUp, lane.w - 2));
                }
                else
                {
                    lane.capUp = lane.capDown = null;
                    lane.cap = Art.Panel("KeyCap", rt, lane.x, JudgeY - 52, lane.w - 14, 70, new Color(0.08f, 0.09f, 0.16f), frame);
                }
                lane.capFill = lane.cap;
                lane.capLabel = UI.Txt("Label", rt, KeyLabel(lane.key), 38, TextAnchor.MiddleCenter, Color.white, lane.x, JudgeY - 50, lane.w, 60);
                lane.capLabel.fontStyle = FontStyle.Bold;
            }

            // 트랙 테두리 선
            if (pillar == null)
            {
                var edgeL = UI.Img("EdgeL", rt, null, new Color(0.7f, 0.95f, 1f));
                UI.Place(edgeL.rectTransform, -trackW / 2f, 0, 4, 1080);
                var edgeR = UI.Img("EdgeR", rt, null, new Color(1f, 0.75f, 0.92f));
                UI.Place(edgeR.rectTransform, trackW / 2f, 0, 4, 1080);
            }

            // 진행 바
            float gaugeX = trackW / 2f + 60f + 250f, gaugeY = 248f, gaugeW = 500f;
            if (Skin.Has("progress_bg") && Skin.Has("progress_fill"))
            {
                Skin.Img("progress_bg", rt, gaugeX, gaugeY, gaugeW, 40);
                progress = Skin.Img("progress_fill", rt, gaugeX, gaugeY, gaugeW, 56);
                progress.type = Image.Type.Filled;
                progress.fillMethod = Image.FillMethod.Horizontal;
                progress.fillOrigin = (int)Image.OriginHorizontal.Left;
                progress.fillAmount = 0f;
                progressFilled = true;
            }
            else
            {
                var barBg = UI.Img("ProgressBg", rt, null, new Color(1, 1, 1, 0.15f));
                UI.Place(barBg.rectTransform, gaugeX, gaugeY, gaugeW, 12);
                progress = UI.Img("Progress", rt, null, Gold);
                UI.Place(progress.rectTransform, gaugeX - gaugeW / 2f, gaugeY, 0, 12, 0f, 0.5f);
            }
            timeText = UI.Txt("Time", rt, "", 28, TextAnchor.MiddleRight, new Color(1, 1, 1, 0.85f), gaugeX, gaugeY - 36, gaugeW, 40);
            Art.Neon(timeText, new Color(0, 0, 0, 0.7f), 2f);

            // 판정 / 콤보
            judgeImage = UI.Img("JudgeImage", rt, null, Color.white);
            UI.Place(judgeImage.rectTransform, 0, 120, 300, 70);
            judgeImage.preserveAspect = true;
            judgeImage.enabled = false;
            judgeText = UI.Txt("JudgeText", rt, "", 46, TextAnchor.MiddleCenter, Color.white, 0, 120);
            judgeText.fontStyle = FontStyle.BoldAndItalic;
            Art.Neon(judgeText, new Color(0, 0, 0, 0.75f), 4f);
            comboLabel = UI.Txt("ComboLabel", rt, "", 30, TextAnchor.MiddleCenter, Gold, 0, 350);
            comboLabel.fontStyle = FontStyle.BoldAndItalic;
            Art.Neon(comboLabel, new Color(0, 0, 0, 0.7f), 2f);
            comboRoot = UI.Node("ComboRoot", rt);
            UI.Place(comboRoot, 0, 260, 10, 10);
            comboText = UI.Txt("Combo", comboRoot, "", 130, TextAnchor.MiddleCenter, Color.white, 0, 0);
            comboText.fontStyle = FontStyle.BoldAndItalic;
            Art.Neon(comboText, new Color(0.1f, 0.5f, 1f, 0.65f), 5f);
            if (DigitRow.Available) comboDigits = DigitRow.Create(comboRoot, 0, 0, 140);
            comboLabelImage = Skin.ImgH("combo_label", rt, 0, 360, 42);
            if (comboLabelImage != null) comboLabelImage.enabled = false;

            // 양옆 정보 패널
            float side = trackW / 2f + 60f;
            Color panel = new Color(0.03f, 0.04f, 0.1f, 0.65f);
            Skin.Panel("panel_score", rt, -side - 210, 385, 460, 210, panel, new Color(Cyan.r, Cyan.g, Cyan.b, 0.8f));
            UI.Txt("ScoreLabel", rt, "SCORE", 26, TextAnchor.MiddleRight, Cyan, -side - 330, 450).fontStyle = FontStyle.BoldAndItalic;
            scoreText = UI.Txt("Score", rt, "0", 68, TextAnchor.MiddleRight, Color.white, -side - 330, 390);
            scoreText.fontStyle = FontStyle.Bold;
            accText = UI.Txt("Accuracy", rt, "", 32, TextAnchor.MiddleRight, Gold, -side - 330, 325);

            Skin.Panel("panel_song", rt, side + 250, 385, 540, 210, panel, new Color(Pink.r, Pink.g, Pink.b, 0.8f));
            UI.Txt("Title", rt, song.title, 44, TextAnchor.MiddleLeft, Color.white, side + 330, 445).fontStyle = FontStyle.Bold;
            UI.Txt("Artist", rt, song.artist, 28, TextAnchor.MiddleLeft, new Color(1, 1, 1, 0.7f), side + 330, 392);
            UI.Txt("Chart", rt, $"{chart.keys}K  {SelectScreen.DifficultyName(chart.difficulty)}  Lv.{chart.level}", 30, TextAnchor.MiddleLeft, Gold, side + 330, 335).fontStyle = FontStyle.BoldAndItalic;

            speedText = UI.Txt("Speed", rt, "", 24, TextAnchor.MiddleRight, new Color(1, 1, 1, 0.6f), -side - 350, -490);
            UI.Txt("Help", rt, "ESC 일시정지   F1/F2 속도", 24, TextAnchor.MiddleLeft, new Color(1, 1, 1, 0.6f), side + 350, -490);
        }

        static RectTransform Layer(string name, Transform parent)
        {
            var rt = UI.Node(name, parent);
            UI.Stretch(rt);
            return rt;
        }

        static string KeyLabel(Key k)
        {
            switch (k)
            {
                case Key.Semicolon: return ";";
                case Key.Quote: return "'";
                default: return k.ToString();
            }
        }

        void Update()
        {
            if (finished || root == null || root.conductor == null) return;   // 플레이 중 스크립트가 다시 컴파일된 경우
            var kb = Keyboard.current;

            if (paused) { UpdatePause(kb); return; }

            if (kb != null)
            {
                if (kb.escapeKey.wasPressedThisFrame) { OpenPause(); return; }
                if (kb.f1Key.wasPressedThisFrame) { Settings.Speed -= 0.1f; UpdateHud(); }
                if (kb.f2Key.wasPressedThisFrame) { Settings.Speed += 0.1f; UpdateHud(); }
            }

            root.conductor.Tick();
            double now = root.conductor.TimeMs - song.offset - Settings.OffsetMs;
            float pxPerMs = Settings.Speed * 0.4f;
            if (video != null) video.Sync(root.conductor.TimeMs / 1000.0);

            // 일시정지에서 돌아오는 중: 곡이 다시 시작될 때까지 숫자를 세고, 그동안은 판정하지 않는다
            bool resuming = root.conductor.TimeMs < resumeGateMs;
            if (countdownText != null)
            {
                countdownText.enabled = resuming;
                if (resuming)
                {
                    double left = (resumeGateMs - root.conductor.TimeMs) / 1000.0;
                    countdownText.text = Mathf.CeilToInt((float)left).ToString();
                    float frac = (float)(left - System.Math.Floor(left));
                    countdownText.rectTransform.localScale = Vector3.one * (1f + 0.5f * frac * frac);
                }
            }

            Spawn(now, pxPerMs);
            if (!resuming) foreach (var lane in lanes) Process(lane, now, kb);
            MoveNotes(now, pxPerMs);
            Animate(now);

            if (now > endMs + 1500)
            {
                finished = true;
                FillResult();
                root.ShowResult(result);
            }
        }

        // ---------- 일시정지 ----------

        void OpenPause()
        {
            paused = true;
            pauseIndex = 0;
            root.conductor.Pause();
            if (video != null) video.Pause();
            foreach (var lane in lanes) SetPressed(lane, false);
            Sfx.Change();

            if (pauseRoot == null) BuildPauseMenu();
            pauseRoot.gameObject.SetActive(true);
            pauseRoot.SetAsLastSibling();
            if (countdownText != null) countdownText.enabled = false;
            RefreshPauseMenu();
        }

        void BuildPauseMenu()
        {
            pauseRoot = UI.Node("Pause", transform);
            UI.Stretch(pauseRoot);
            UI.Stretch(UI.Img("Dim", pauseRoot, null, new Color(0f, 0f, 0.03f, 0.72f)).rectTransform);
            UI.Place(UI.Img("Back", pauseRoot, null, new Color(0.02f, 0.03f, 0.09f, 0.94f)).rectTransform, 0, 0, 560, 500);   // 뒤의 콤보·판정 글자가 비치지 않게
            Skin.Panel("panel_rank", pauseRoot, 0, 0, 620, 560, new Color(0.03f, 0.04f, 0.1f, 0.9f), new Color(Gold.r, Gold.g, Gold.b, 0.9f));

            var head = UI.Txt("Head", pauseRoot, "PAUSE", 76, TextAnchor.MiddleCenter, Color.white, 0, 170, 560, 100);
            head.fontStyle = FontStyle.BoldAndItalic;
            Art.Neon(head, new Color(0.1f, 0.6f, 1f, 0.7f), 4f);

            pauseCursor = Skin.Panel("row_selected", pauseRoot, 0, 0, 480, 84, new Color(1f, 0.8f, 0.3f, 0.22f), new Color(1f, 0.85f, 0.4f, 1f));
            pauseTexts = new Text[PauseItems.Length];
            for (int i = 0; i < PauseItems.Length; i++)
                pauseTexts[i] = UI.Txt("Item" + i, pauseRoot, PauseItems[i], 40, TextAnchor.MiddleCenter, Color.white, 0, 50 - i * 92, 460, 70);
            UI.Txt("Help", pauseRoot, "↑↓ 선택    ENTER 결정    ESC 계속하기", 24, TextAnchor.MiddleCenter, new Color(1, 1, 1, 0.55f), 0, -235, 560, 40);

            countdownText = UI.Txt("Countdown", transform, "", 220, TextAnchor.MiddleCenter, Color.white, 0, -40, 400, 300);
            countdownText.fontStyle = FontStyle.BoldAndItalic;
            Art.Neon(countdownText, new Color(0.1f, 0.6f, 1f, 0.8f), 6f);
            countdownText.enabled = false;
        }

        void RefreshPauseMenu()
        {
            for (int i = 0; i < pauseTexts.Length; i++)
            {
                pauseTexts[i].color = i == pauseIndex ? Color.white : new Color(1, 1, 1, 0.5f);
                pauseTexts[i].fontStyle = i == pauseIndex ? FontStyle.Bold : FontStyle.Normal;
            }
            pauseCursor.rectTransform.anchoredPosition = new Vector2(0, 50 - pauseIndex * 92);
        }

        void ResumeFromPause()
        {
            Sfx.Confirm();
            paused = false;
            pauseRoot.gameObject.SetActive(false);
            resumeGateMs = root.conductor.Resume(ResumeCountdown);
            countdownText.rectTransform.SetAsLastSibling();
        }

        void UpdatePause(Keyboard kb)
        {
            Color cc = pauseCursor.color;
            cc.a = 0.8f + 0.2f * Mathf.Sin(Time.unscaledTime * 5f);
            pauseCursor.color = cc;
            if (kb == null) return;

            if (kb.upArrowKey.wasPressedThisFrame) { pauseIndex = (pauseIndex + PauseItems.Length - 1) % PauseItems.Length; Sfx.Move(); RefreshPauseMenu(); }
            if (kb.downArrowKey.wasPressedThisFrame) { pauseIndex = (pauseIndex + 1) % PauseItems.Length; Sfx.Move(); RefreshPauseMenu(); }

            bool enter = kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame;
            if (kb.escapeKey.wasPressedThisFrame || (enter && pauseIndex == 0))
            {
                // 계속하기: 3초를 세는 동안 노트가 다시 내려오고, 멈췄던 순간부터 곡이 이어진다
                ResumeFromPause();
            }
            else if (enter && pauseIndex == 1)
            {
                finished = true;
                Sfx.Confirm();
                root.StartGame(song, chart);
            }
            else if (enter && pauseIndex == 2)
            {
                finished = true;
                Sfx.Back();
                root.ShowSelect();
            }
        }

        // ---------- 판정 ----------

        void Process(Lane lane, double now, Keyboard kb)
        {
            bool pressed = kb != null && kb[lane.key].wasPressedThisFrame;
            bool held = kb != null && kb[lane.key].isPressed;
            if (Settings.Auto && lane.next < lane.notes.Count)
            {
                // 자동 연주: 노트 시각이 되면 누르고, 롱노트는 끝까지 누른다
                Note auto = lane.notes[lane.next];
                pressed = auto.state == Pending && now >= auto.d.t;
                held = auto.state == Holding || (now >= auto.d.t && now <= auto.d.t + auto.d.len + 50);
            }
            SetPressed(lane, held);

            while (lane.next < lane.notes.Count)
            {
                Note n = lane.notes[lane.next];
                bool isLong = n.d.len > 0;

                if (n.state == Holding)
                {
                    double end = n.d.t + n.d.len;
                    if (now >= end)
                    {
                        // 끝까지 누르고 있었으면 자동으로 성공
                        Apply(Judge.Perfect, lane);
                        n.state = Done;
                        lane.next++;
                        continue;
                    }
                    if (!held)
                    {
                        // 일찍 뗐다: 끝에 충분히 가까우면 인정, 아니면 미스
                        double early = end - now;
                        if (early <= JudgeRule.GoodMs) { Apply(JudgeRule.FromDiff(early), lane); n.state = Done; }
                        else { Apply(Judge.Miss, lane); n.state = Failed; }
                        lane.next++;
                        continue;
                    }
                    // 누르고 있는 동안 불꽃이 계속 튄다
                    lane.flashT = 1f;
                    lane.holdFxT -= Time.unscaledDeltaTime;
                    if (lane.holdFxT <= 0f)
                    {
                        lane.holdFxT = 0.06f;
                        Sparks(lane, 1);
                    }
                    break;
                }

                // 아직 안 친 노트가 판정 범위를 지나갔다
                if (now - n.d.t > JudgeRule.GoodMs)
                {
                    Apply(Judge.Miss, lane);
                    if (isLong) Apply(Judge.Miss, lane);
                    n.state = Failed;
                    lane.next++;
                    continue;
                }

                if (pressed)
                {
                    double diff = Math.Abs(n.d.t - now);
                    if (diff <= JudgeRule.GoodMs)
                    {
                        Apply(JudgeRule.FromDiff(diff), lane);
                        if (isLong) n.state = Holding;
                        else { n.state = Done; lane.next++; }
                    }
                    // 범위 밖에서 누른 건 무시 (빈 입력에 패널티 없음)
                }
                break;
            }
        }

        void SetPressed(Lane lane, bool held)
        {
            if (lane.beam.enabled == held) return;
            lane.beam.enabled = held;
            if (lane.capUp != null) lane.cap.sprite = held ? lane.capDown : lane.capUp;
            else lane.capFill.color = held ? lane.color : new Color(0.08f, 0.09f, 0.16f);
            lane.capLabel.color = held ? new Color(0.05f, 0.05f, 0.1f) : Color.white;
        }

        void Apply(Judge j, Lane lane)
        {
            result.counts[(int)j]++;
            judged++;
            weightSum += JudgeRule.Weight(j);
            if (j == Judge.Miss) combo = 0;
            else
            {
                combo++;
                if (combo > result.maxCombo) result.maxCombo = combo;
                lane.flashT = 1f;
                Burst(lane, j);
            }

            Sprite sprite = skin.JudgeSprite(j);
            judgeImage.enabled = sprite != null;
            judgeImage.sprite = sprite;
            judgeText.text = sprite != null ? "" : JudgeRule.Name(j);
            judgeText.color = JudgeRule.ColorOf(j);
            judgeT = 0f;
            UpdateHud();
        }

        void FillResult()
        {
            result.accuracy = total > 0 ? (float)(weightSum / total * 100.0) : 0f;
            result.score = total > 0 ? (int)Math.Round(weightSum / total * 1000000.0) : 0;
        }

        // ---------- 히트 이펙트 ----------

        Color FxColor(Lane lane) => Color.Lerp(lane.color, Color.white, 0.35f);

        Fx AddFx(Sprite sprite, Color color, float x, float y, float size0, float size1, float life)
        {
            var img = UI.Img("Fx", fxLayer, sprite, color);
            UI.Place(img.rectTransform, x, y, size0, size0);
            var fx = new Fx { rt = img.rectTransform, g = img, life = life, size0 = size0, size1 = size1 };
            effects.Add(fx);
            return fx;
        }

        void Burst(Lane lane, Judge j)
        {
            float w = lane.w;
            Sprite ring = skin.hitEffect != null ? skin.hitEffect : Art.Ring;
            AddFx(ring, skin.hitEffect != null ? Color.white : FxColor(lane), lane.x, JudgeY, w * 0.5f, w * (j == Judge.Perfect ? 2.0f : 1.5f), 0.32f);
            AddFx(Art.Glow, Color.white, lane.x, JudgeY, w * 1.6f, w * 0.6f, 0.2f);
            Sparks(lane, j == Judge.Perfect ? 8 : 5);
        }

        void Sparks(Lane lane, int count)
        {
            for (int i = 0; i < count; i++)
            {
                float x = lane.x + ((float)rng.NextDouble() - 0.5f) * lane.w * 0.8f;
                float size = 10f + (float)rng.NextDouble() * 16f;
                Sprite spark = Skin.Get("spark");
                if (spark != null) size *= 1.8f;
                Fx fx = AddFx(spark != null ? spark : Art.Glow, FxColor(lane), x, JudgeY, size, size * 0.3f, 0.3f + (float)rng.NextDouble() * 0.3f);
                fx.vel = new Vector2(((float)rng.NextDouble() - 0.5f) * 500f, 250f + (float)rng.NextDouble() * 550f);
                fx.gravity = 1400f;
            }
        }

        void UpdateEffects(float dt)
        {
            for (int i = effects.Count - 1; i >= 0; i--)
            {
                Fx fx = effects[i];
                fx.t += dt;
                float k = fx.t / fx.life;
                if (k >= 1f)
                {
                    Destroy(fx.rt.gameObject);
                    effects.RemoveAt(i);
                    continue;
                }
                float size = Mathf.Lerp(fx.size0, fx.size1, 1f - (1f - k) * (1f - k));
                fx.rt.sizeDelta = new Vector2(size, size);
                fx.vel.y -= fx.gravity * dt;
                fx.rt.anchoredPosition += fx.vel * dt;
                Color c = fx.g.color;
                c.a = (1f - k) * (1f - k);
                fx.g.color = c;
            }
        }

        // ---------- 노트 표시 ----------

        void Spawn(double now, float pxPerMs)
        {
            double lookAhead = (TopY - JudgeY) / pxPerMs + 100;
            foreach (var lane in lanes)
            {
                while (lane.spawn < lane.notes.Count && lane.notes[lane.spawn].d.t - now < lookAhead)
                {
                    CreateView(lane.notes[lane.spawn]);
                    lane.spawn++;
                }
            }
        }

        void CreateView(Note n)
        {
            Lane lane = n.lane;
            float w = lane.w - 10f;

            if (n.d.len > 0)
            {
                Sprite s = lane.blue ? skin.longBodyBlue : skin.longBodyWhite;
                n.bodyG = UI.Img("LongBody", bodyLayer, s != null ? s : Art.LongBody(lane.color), Color.white);
                n.body = n.bodyG.rectTransform;
                UI.Place(n.body, lane.x, TopY + 100, w * 0.8f, 0, 0.5f, 0f);
            }

            Sprite head = lane.blue ? skin.noteBlue : skin.noteWhite;
            Transform parent = headLayer;
            if (head != null)
            {
                n.headG = UI.Img("Note", parent, head, Color.white);
                UI.Place(n.headG.rectTransform, lane.x, TopY + 100, lane.w, Skin.HeightFor(head, lane.w));
            }
            else
            {
                n.headG = UI.Img("Note", parent, Art.Note(lane.color), Color.white);
                Vector2 size = Art.NoteSize(w, NoteH);
                UI.Place(n.headG.rectTransform, lane.x, TopY + 100, size.x, size.y);
            }
            n.head = n.headG.rectTransform;
            active.Add(n);
        }

        void MoveNotes(double now, float pxPerMs)
        {
            for (int i = active.Count - 1; i >= 0; i--)
            {
                Note n = active[i];
                float yHead = JudgeY + (float)(n.d.t - now) * pxPerMs;
                float yTail = JudgeY + (float)(n.d.t + n.d.len - now) * pxPerMs;

                if (n.state == Done || yTail < BottomY)
                {
                    Destroy(n.head.gameObject);
                    if (n.body != null) Destroy(n.body.gameObject);
                    active.RemoveAt(i);
                    continue;
                }

                if (n.state == Holding) yHead = JudgeY;   // 누르는 동안 머리는 판정선에 붙는다
                if (n.state == Failed && !n.dimmed)
                {
                    n.dimmed = true;
                    n.headG.color = new Color(0.35f, 0.35f, 0.35f, 1f);
                    if (n.bodyG != null) n.bodyG.color = new Color(0.35f, 0.35f, 0.35f, n.bodyG.color.a);
                }

                n.head.anchoredPosition = new Vector2(n.lane.x, yHead);
                if (n.body != null)
                {
                    float h = Mathf.Max(0f, yTail - yHead);
                    n.body.anchoredPosition = new Vector2(n.lane.x, yHead);
                    n.body.sizeDelta = new Vector2(n.body.sizeDelta.x, h);
                }
            }
        }

        // ---------- HUD ----------

        void Animate(double now)
        {
            float dt = Time.unscaledDeltaTime;
            UpdateEffects(dt);

            // 박자에 맞춰 트랙 조명이 번쩍인다
            float pulse = now > 0 ? Mathf.Exp(-Mathf.Repeat((float)(now / beatMs), 1f) * 5f) : 0f;
            backdrop.pulse = pulse;
            SetAlpha(sideL, 0.35f + 0.5f * pulse);
            SetAlpha(sideR, 0.35f + 0.5f * pulse);
            SetAlpha(judgeGlow, 0.55f + 0.4f * pulse);

            foreach (var lane in lanes)
            {
                if (lane.flashT > 0f)
                {
                    lane.flashT -= dt * 5f;
                    lane.flash.enabled = lane.flashT > 0f;
                    SetAlpha(lane.flash, Mathf.Clamp01(lane.flashT));
                }
            }

            judgeT += dt;
            float pop = Mathf.Clamp01(1f - judgeT * 9f);
            float alpha = Mathf.Clamp01(1.6f - judgeT * 2f);
            judgeText.rectTransform.localScale = judgeImage.rectTransform.localScale = Vector3.one * (1f + 0.4f * pop);
            SetAlpha(judgeText, alpha);
            judgeImage.color = new Color(1, 1, 1, alpha);
            comboRoot.localScale = Vector3.one * (1f + 0.22f * pop);

            // 곡 진행 게이지: 곡 전체 길이 기준 (곡 길이를 모르면 마지막 노트 기준)
            double lengthMs = root.conductor.LengthMs > 0 ? root.conductor.LengthMs : endMs;
            double songMs = System.Math.Max(0.0, System.Math.Min(root.conductor.TimeMs, lengthMs));
            float p = lengthMs > 0 ? (float)(songMs / lengthMs) : 0f;
            if (progressFilled) progress.fillAmount = p;
            else progress.rectTransform.sizeDelta = new Vector2(500f * p, 12);
            int sec = (int)(songMs / 1000.0);
            if (sec != shownSecond)
            {
                shownSecond = sec;
                int total = (int)(lengthMs / 1000.0);
                timeText.text = $"{sec / 60}:{sec % 60:00} / {total / 60}:{total % 60:00}    {Mathf.FloorToInt(p * 100f)}%";
            }
        }

        static void SetAlpha(Graphic g, float a)
        {
            Color c = g.color;
            c.a = a;
            g.color = c;
        }

        void UpdateHud()
        {
            string comboStr = combo >= 2 ? combo.ToString() : "";
            if (comboDigits != null) comboDigits.Set(comboStr);
            else comboText.text = comboStr;
            if (comboLabelImage != null) comboLabelImage.enabled = combo >= 2;
            else comboLabel.text = combo >= 2 ? "COMBO" : "";
            int score = total > 0 ? (int)Math.Round(weightSum / total * 1000000.0) : 0;
            scoreText.text = score.ToString("N0");
            accText.text = judged > 0 ? (weightSum / judged * 100.0).ToString("0.00") + "%" : "";
            speedText.text = (Settings.Auto ? "자동 연주   " : "") + $"속도 x{Settings.Speed:0.0}";
        }
    }
}
