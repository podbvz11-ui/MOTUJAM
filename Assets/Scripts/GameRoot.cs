using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace Motujam
{
    /// <summary>게임 전체 진입점. 캔버스를 만들고 곡 선택 → 플레이 → 결과 화면을 전환한다.</summary>
    public class GameRoot : MonoBehaviour
    {
        public GameSkin skin;

        [System.NonSerialized] public Conductor conductor;
        [System.NonSerialized] public List<SongData> songs;
        [System.NonSerialized] public SongPreview preview;

        Canvas canvas;
        GameObject current;
        GameSkin baseSkin;
        Image fade;

        void Awake()
        {
            Application.runInBackground = true;   // 창이 가려져도(방송 송출 중 등) 곡과 영상이 멈추지 않게
            Application.targetFrameRate = 240;
            QualitySettings.vSyncCount = 0;

            if (skin == null) skin = Resources.Load<GameSkin>("GameSkin");
            if (skin == null) skin = ScriptableObject.CreateInstance<GameSkin>();
            baseSkin = skin;
            ReloadSkin();

            // 오디오 버퍼를 줄여 소리 지연을 최소화한다 (리듬게임은 지연이 작을수록 좋다)
            var audioConfig = AudioSettings.GetConfiguration();
            if (audioConfig.dspBufferSize > 256)
            {
                audioConfig.dspBufferSize = 256;
                AudioSettings.Reset(audioConfig);
            }

            if (FindAnyObjectByType<AudioListener>() == null) gameObject.AddComponent<AudioListener>();
            conductor = gameObject.AddComponent<Conductor>();

            // 메뉴 효과음과 곡 미리듣기는 곡 재생과 다른 채널을 쓴다
            var sfxSource = gameObject.AddComponent<AudioSource>();
            sfxSource.playOnAwake = false;
            Sfx.Bind(sfxSource);
            preview = gameObject.AddComponent<SongPreview>();

            var canvasGo = new GameObject("Canvas", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);
            canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 1f;

            // 화면이 바뀔 때 잠깐 덮었다 걷히는 막 (항상 맨 위에 둔다)
            fade = UI.Img("Fade", canvas.transform, null, new Color(0.01f, 0.01f, 0.04f, 1f));
            UI.Stretch(fade.rectTransform);

            ReloadSongs();
            Show<NoticeScreen>("NoticeScreen").Init(this);   // 안내 문구 → 타이틀
        }

        // 창 모드에서 고를 수 있는 해상도 (모두 16:9). 모니터보다 큰 것은 목록에서 빠진다.
        static readonly Vector2Int[] Resolutions =
        {
            new Vector2Int(960, 540), new Vector2Int(1280, 720), new Vector2Int(1600, 900),
            new Vector2Int(1920, 1080), new Vector2Int(2560, 1440), new Vector2Int(3840, 2160),
        };

        static int WindowIndex
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt("windowRes", 1), 0, MaxWindowIndex);
            set => PlayerPrefs.SetInt("windowRes", Mathf.Clamp(value, 0, MaxWindowIndex));
        }

        /// <summary>이 모니터에 들어가는 가장 큰 해상도의 번호.</summary>
        static int MaxWindowIndex
        {
            get
            {
                int max = 0;
                for (int i = 0; i < Resolutions.Length; i++)
                    if (Resolutions[i].x <= Display.main.systemWidth && Resolutions[i].y <= Display.main.systemHeight) max = i;
                return max;
            }
        }

        /// <summary>지금 화면 상태를 글자로. 예: "전체 화면", "창 1280×720"</summary>
        public static string ScreenLabel
        {
            get
            {
                if (pendingLabel != null) return pendingLabel;
                return Screen.fullScreen ? "전체 화면" : $"창 {Screen.width}×{Screen.height}";
            }
        }

        static string pendingLabel;   // 해상도는 다음 프레임에 바뀌므로, 바꾼 직후에는 이 글자를 보여 준다
        static float pendingUntil;

        /// <summary>전체 화면 ↔ 창 모드. 창 모드는 마지막으로 고른 해상도로 열린다. 선택은 다음 실행 때도 유지된다.</summary>
        public static void ToggleWindowed()
        {
            if (Screen.fullScreen) ApplyWindow();
            else
            {
                Screen.SetResolution(Display.main.systemWidth, Display.main.systemHeight, FullScreenMode.FullScreenWindow);
                Pending("전체 화면");
            }
        }

        /// <summary>창 모드 해상도를 한 단계 키우거나(+1) 줄인다(-1). 전체 화면이었다면 창 모드로 바뀐다.</summary>
        public static void StepResolution(int step)
        {
            if (!Screen.fullScreen) WindowIndex += step;
            ApplyWindow();
        }

        static void ApplyWindow()
        {
            Vector2Int r = Resolutions[WindowIndex];
            Screen.SetResolution(r.x, r.y, FullScreenMode.Windowed);
            Pending($"창 {r.x}×{r.y}");
        }

        static void Pending(string label)
        {
            pendingLabel = label;
            pendingUntil = Time.unscaledTime + 0.5f;
        }

        Text toast;
        float toastT;

        void ShowToast(string text)
        {
            if (toast == null)
            {
                toast = UI.Txt("Toast", canvas.transform, "", 34, TextAnchor.MiddleCenter, Color.white, 0, 420, 900, 60);
                toast.fontStyle = FontStyle.Bold;
                Art.Neon(toast, new Color(0, 0, 0, 0.85f), 3f);
            }
            toast.transform.SetAsLastSibling();
            toast.text = text;
            toastT = 2f;
        }

        void Update()
        {
            // 어느 화면에서든: F11 전체 화면 ↔ 창 모드, F8 / F9 창 해상도 줄이기 / 키우기 (F10 은 윈도우가 메뉴 키로 쓰므로 피한다)
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null)
            {
                bool changed = true;
                if (kb.f11Key.wasPressedThisFrame) ToggleWindowed();
                else if (kb.f8Key.wasPressedThisFrame) StepResolution(-1);
                else if (kb.f9Key.wasPressedThisFrame) StepResolution(1);
                else changed = false;
                if (changed) { Sfx.Change(); ShowToast("화면: " + ScreenLabel); }
            }
            if (pendingLabel != null && Time.unscaledTime > pendingUntil) pendingLabel = null;
            if (toast != null && toastT > 0f)
            {
                toastT -= Time.unscaledDeltaTime;
                Color tc = toast.color;
                tc.a = Mathf.Clamp01(toastT * 2f);
                toast.color = tc;
            }

            Color c = fade.color;
            if (c.a <= 0f) return;
            c.a = Mathf.MoveTowards(c.a, 0f, Time.unscaledDeltaTime * 3.5f);
            fade.color = c;
        }

        public void ShowTitle()
        {
            StopAllCoroutines();
            conductor.Stop();
            preview.Stop();
            Show<TitleScreen>("TitleScreen").Init(this);
        }

        /// <summary>StreamingAssets/Skin 폴더의 이미지를 다시 읽는다.</summary>
        public void ReloadSkin()
        {
            Skin.Clear();
            skin = Skin.Fill(baseSkin);
        }

        public void ReloadSongs()
        {
            songs = SongLibrary.LoadAll();
        }

        T Show<T>(string name) where T : MonoBehaviour
        {
            if (current != null) Destroy(current);
            var rt = UI.Node(name, canvas.transform);
            UI.Stretch(rt);
            current = rt.gameObject;
            fade.transform.SetAsLastSibling();
            fade.color = new Color(0.01f, 0.01f, 0.04f, 1f);
            return current.AddComponent<T>();
        }

        public void ShowSelect()
        {
            StopAllCoroutines();
            conductor.Stop();
            Show<SelectScreen>("SelectScreen").Init(this);
        }

        public void ShowResult(PlayResult result)
        {
            conductor.Stop();
            Show<ResultScreen>("ResultScreen").Init(this, result);
        }

        public void StartGame(SongData song, ChartData chart)
        {
            StopAllCoroutines();
            preview.Stop(true);
            StartCoroutine(StartGameRoutine(song, chart));
        }

        IEnumerator StartGameRoutine(SongData song, ChartData chart)
        {
            AudioClip clip = null;

            if (!string.IsNullOrEmpty(song.audio) && !string.IsNullOrEmpty(song.folder))
            {
                string path = Path.Combine(song.folder, song.audio);
                string uri = new System.Uri(path).AbsoluteUri;
                using (var req = UnityWebRequestMultimedia.GetAudioClip(uri, AudioTypeOf(path)))
                {
                    yield return req.SendWebRequest();
                    if (req.result == UnityWebRequest.Result.Success) clip = DownloadHandlerAudioClip.GetContent(req);
                    else Debug.LogWarning($"[Motujam] 오디오를 불러오지 못했습니다: {path}\n{req.error}");
                }
            }

            if (clip == null)
            {
                // 음원이 없으면 채보 길이에 맞춘 데모 비트를 만들어 튼다
                float lastMs = 0;
                foreach (var n in chart.notes) lastMs = Mathf.Max(lastMs, n.t + n.len);
                clip = DemoSong.CreateClip(song.bpm > 0 ? song.bpm : 120f, lastMs / 1000f + 3f);
                yield return null;
            }

            Show<PlayScreen>("PlayScreen").Init(this, song, chart);
            conductor.Play(clip, 2.0);
        }

        static AudioType AudioTypeOf(string path)
        {
            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".ogg": return AudioType.OGGVORBIS;
                case ".mp3": return AudioType.MPEG;
                case ".wav": return AudioType.WAV;
                default: return AudioType.UNKNOWN;
            }
        }
    }
}
