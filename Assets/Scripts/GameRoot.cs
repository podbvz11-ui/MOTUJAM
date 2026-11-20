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

        void Update()
        {
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
