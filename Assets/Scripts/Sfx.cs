using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace Motujam
{
    /// <summary>메뉴 효과음. 음원 파일 없이 코드로 합성한다.</summary>
    public static class Sfx
    {
        const int Rate = 44100;
        static readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        static AudioSource source;

        public static float Volume = 0.55f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { clips.Clear(); source = null; }

        public static void Bind(AudioSource s) { source = s; }

        static void Play(string name, Func<AudioClip> make, float volume = 1f)
        {
            if (source == null) return;
            if (!clips.TryGetValue(name, out AudioClip clip) || clip == null) clips[name] = clip = make();
            source.PlayOneShot(clip, Volume * volume);
        }

        /// <summary>곡 목록에서 위아래로 움직일 때: 짧고 또렷한 틱.</summary>
        public static void Move() => Play("move", () => Make("move", 0.07f, t =>
            Env(t, 0.002f, 0.05f) * (Sine(1480, t) * 0.6f + Sine(2960, t) * 0.25f)), 0.8f);

        /// <summary>난이도·설정을 바꿀 때: 두 음이 이어지는 블립.</summary>
        public static void Change() => Play("change", () => Make("change", 0.13f, t =>
            Env(t, 0.002f, 0.05f) * Sine(990, t) * 0.6f + Env(t - 0.05f, 0.002f, 0.07f) * Sine(1320, t) * 0.6f), 0.8f);

        /// <summary>결정: 위로 올라가는 밝은 화음.</summary>
        public static void Confirm() => Play("confirm", () => Make("confirm", 0.7f, t =>
        {
            float v = 0f;
            float[] notes = { 523.25f, 659.25f, 783.99f, 1046.5f, 1318.5f };
            for (int i = 0; i < notes.Length; i++)
            {
                float u = t - i * 0.045f;
                v += Env(u, 0.004f, 0.45f) * (Sine(notes[i], u) + 0.3f * Sine(notes[i] * 2, u)) * 0.22f;
            }
            return v + Env(t, 0.001f, 0.12f) * Noise(t) * 0.12f;
        }));

        /// <summary>뒤로: 아래로 떨어지는 음.</summary>
        public static void Back() => Play("back", () => Make("back", 0.2f, t =>
            Env(t, 0.003f, 0.15f) * Sweep(720, 300, 0.2f, t) * 0.6f), 0.8f);

        /// <summary>타이틀에서 시작: 휙 올라가는 소리 + 큰 화음.</summary>
        public static void Start() => Play("start", () => Make("start", 1.1f, t =>
        {
            float whoosh = Env(t, 0.12f, 0.2f) * Noise(t) * 0.18f * Mathf.Clamp01(t / 0.12f);
            float rise = Env(t, 0.01f, 0.22f) * Sweep(300, 1800, 0.22f, t) * 0.3f;
            float chord = 0f;
            float[] notes = { 261.63f, 392f, 523.25f, 659.25f, 783.99f, 1046.5f };
            foreach (float f in notes) chord += Sine(f, t - 0.16f) * 0.12f;
            chord *= Env(t - 0.16f, 0.005f, 0.8f);
            return whoosh + rise + chord + Env(t - 0.16f, 0.001f, 0.25f) * Sine(65, t - 0.16f) * 0.5f;
        }));

        /// <summary>결과 화면에서 숫자가 올라갈 때.</summary>
        public static void Count() => Play("count", () => Make("count", 0.04f, t =>
            Env(t, 0.001f, 0.03f) * Sine(1900, t) * 0.4f), 0.5f);

        /// <summary>등급 도장이 찍힐 때: 묵직한 타격 + 밝은 화음.</summary>
        public static void Rank() => Play("rank", () => Make("rank", 1.2f, t =>
        {
            float thump = Env(t, 0.001f, 0.3f) * Sweep(140, 45, 0.25f, t) * 0.9f;
            float hit = Env(t, 0.001f, 0.08f) * Noise(t) * 0.35f;
            float chord = 0f;
            float[] notes = { 523.25f, 659.25f, 783.99f, 1046.5f };
            foreach (float f in notes) chord += (Sine(f, t) + 0.4f * Sine(f * 2, t)) * 0.1f;
            return thump + hit + chord * Env(t, 0.004f, 0.9f);
        }));

        /// <summary>풀 콤보: 반짝이며 올라가는 음.</summary>
        public static void Sparkle() => Play("sparkle", () => Make("sparkle", 0.9f, t =>
        {
            float v = 0f;
            float[] notes = { 1046.5f, 1318.5f, 1568f, 2093f, 2637f, 3136f };
            for (int i = 0; i < notes.Length; i++)
            {
                float u = t - i * 0.06f;
                v += Env(u, 0.002f, 0.35f) * Sine(notes[i], u) * 0.2f;
            }
            return v;
        }));

        // ---------- 합성 도구 ----------

        static AudioClip Make(string name, float seconds, Func<float, float> wave)
        {
            int n = Mathf.CeilToInt(seconds * Rate);
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float fadeOut = Mathf.Clamp01((seconds - t) / 0.01f);   // 끝에서 '틱' 소리가 나지 않게
                data[i] = Mathf.Clamp(wave(t), -1f, 1f) * fadeOut;
            }
            var clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static float Sine(float freq, float t) => t < 0 ? 0f : Mathf.Sin(2f * Mathf.PI * freq * t);

        /// <summary>duration 동안 f0 에서 f1 로 미끄러지는 음.</summary>
        static float Sweep(float f0, float f1, float duration, float t)
        {
            if (t < 0) return 0f;
            float u = Mathf.Min(t, duration);
            float phase = f0 * u + (f1 - f0) * u * u / (2f * duration) + f1 * Mathf.Max(0f, t - duration);
            return Mathf.Sin(2f * Mathf.PI * phase);
        }

        /// <summary>attack 초 동안 커졌다가 decay 초에 걸쳐 사라지는 음량 곡선.</summary>
        static float Env(float t, float attack, float decay)
        {
            if (t < 0) return 0f;
            return t < attack ? t / attack : Mathf.Exp(-(t - attack) / (decay * 0.35f));
        }

        static float Noise(float t) => t < 0 ? 0f : Mathf.PerlinNoise(t * 9000f, 0.37f) * 2f - 1f + (Mathf.Repeat(Mathf.Sin(t * 91345.3f) * 43758.5f, 1f) - 0.5f);
    }

    /// <summary>화면 연출에 쓰는 간단한 애니메이션 도우미.</summary>
    public static class Tween
    {
        public static float OutCubic(float k) { k = 1f - k; return 1f - k * k * k; }
        public static float OutBack(float k) { k -= 1f; return 1f + k * k * (2.7f * k + 1.7f); }

        /// <summary>delay 뒤에 duration 초 동안 step(0→1) 을 매 프레임 부른다.</summary>
        public static Coroutine Run(MonoBehaviour host, float duration, Action<float> step, float delay = 0f)
        {
            return host.StartCoroutine(Routine(duration, step, delay));
        }

        public static Coroutine After(MonoBehaviour host, float delay, Action action)
        {
            return host.StartCoroutine(Routine(0f, _ => action(), delay));
        }

        static IEnumerator Routine(float duration, Action<float> step, float delay)
        {
            if (duration > 0f) step(0f);
            for (float t = 0f; t < delay; t += Time.unscaledDeltaTime) yield return null;
            for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
            {
                step(t / duration);
                yield return null;
            }
            step(1f);
        }

        /// <summary>대상을 CanvasGroup 으로 감싸 옆에서 미끄러져 들어오게 한다.</summary>
        public static void SlideIn(MonoBehaviour host, RectTransform rt, Vector2 from, float duration = 0.4f, float delay = 0f)
        {
            var group = rt.GetComponent<CanvasGroup>();
            if (group == null) group = rt.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            Vector2 home = rt.anchoredPosition;
            Run(host, duration, k =>
            {
                float e = OutCubic(k);
                rt.anchoredPosition = home + from * (1f - e);
                group.alpha = Mathf.Clamp01(k * 2f);
            }, delay);
        }

        /// <summary>화면 전체가 번쩍였다 사라진다.</summary>
        public static void Flash(MonoBehaviour host, Transform parent, Color color, float duration = 0.35f)
        {
            var img = UI.Img("Flash", parent, null, color);
            UI.Stretch(img.rectTransform);
            float a0 = color.a;
            Run(host, duration, k =>
            {
                if (img == null) return;
                Color c = img.color;
                c.a = a0 * (1f - k) * (1f - k);
                img.color = c;
                if (k >= 1f) UnityEngine.Object.Destroy(img.gameObject);
            });
        }
    }

    /// <summary>곡 선택 화면에서 고른 곡을 잠깐 들려준다.</summary>
    public class SongPreview : MonoBehaviour
    {
        const float Level = 0.55f;

        AudioSource src;
        Coroutine job;
        float target;

        /// <summary>미리듣기가 실제로 들리고 있는지.</summary>
        public bool Audible => src != null && src.isPlaying && src.volume > 0.05f;

        void Awake()
        {
            src = gameObject.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.loop = true;
            src.volume = 0f;
        }

        void Update()
        {
            src.volume = Mathf.MoveTowards(src.volume, target, Time.unscaledDeltaTime * 1.6f);
            if (target <= 0f && src.volume <= 0f && src.isPlaying) src.Stop();
        }

        public void Play(SongData song)
        {
            if (job != null) StopCoroutine(job);
            target = 0f;
            job = StartCoroutine(Load(song));
        }

        public void Stop(bool immediately = false)
        {
            if (job != null) StopCoroutine(job);
            job = null;
            target = 0f;
            if (immediately) { src.volume = 0f; src.Stop(); }
        }

        IEnumerator Load(SongData song)
        {
            // 목록을 빠르게 넘길 때 곡마다 불러오지 않도록 잠깐 기다린다
            yield return new WaitForSecondsRealtime(0.45f);
            if (string.IsNullOrEmpty(song.audio) || string.IsNullOrEmpty(song.folder)) yield break;
            string path = Path.Combine(song.folder, song.audio);
            if (!File.Exists(path)) yield break;

            AudioType type = path.EndsWith(".ogg") ? AudioType.OGGVORBIS : path.EndsWith(".mp3") ? AudioType.MPEG : AudioType.WAV;
            using (var req = UnityWebRequestMultimedia.GetAudioClip(new Uri(path).AbsoluteUri, type))
            {
                ((DownloadHandlerAudioClip)req.downloadHandler).streamAudio = true;   // 통째로 풀지 않고 흘려 듣는다
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success) yield break;
                AudioClip clip = DownloadHandlerAudioClip.GetContent(req);
                if (clip == null) yield break;

                while (src.volume > 0.01f) yield return null;   // 앞 곡이 다 사라진 뒤에 바꾼다
                src.Stop();
                AudioClip old = src.clip;
                src.clip = clip;
                if (old != null) Destroy(old);
                src.time = Mathf.Min(clip.length * 0.35f, Mathf.Max(0f, clip.length - 20f));   // 보통 후렴 근처
                src.Play();
                target = Level;
            }
            job = null;
        }
    }
}
