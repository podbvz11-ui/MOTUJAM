using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace Motujam
{
    /// <summary>플레이 화면 뒤에 깔리는 배경 영상. 소리는 끄고 곡 시간에 맞춰 재생한다.</summary>
    public class BackgroundVideo : MonoBehaviour
    {
        const float Brightness = 0.5f;   // 노트가 잘 보이도록 영상을 어둡게

        VideoPlayer player;
        RenderTexture target;
        RawImage image;
        bool started;
        float checkTimer;

        public static BackgroundVideo Create(Transform parent, string path)
        {
            var rt = UI.Node("BackgroundVideo", parent);
            UI.Stretch(rt);
            var v = rt.gameObject.AddComponent<BackgroundVideo>();
            v.Build(path);
            return v;
        }

        void Build(string path)
        {
            target = new RenderTexture(1280, 720, 0);
            image = gameObject.AddComponent<RawImage>();
            image.texture = target;
            image.raycastTarget = false;
            image.color = new Color(0, 0, 0, 0);   // 첫 장면이 준비될 때까지 숨긴다

            player = gameObject.AddComponent<VideoPlayer>();
            player.playOnAwake = false;
            player.source = VideoSource.Url;
            player.url = path;
            player.renderMode = VideoRenderMode.RenderTexture;
            player.targetTexture = target;
            player.audioOutputMode = VideoAudioOutputMode.None;
            player.isLooping = false;
            player.skipOnDrop = true;
            player.errorReceived += (p, message) => { Debug.LogWarning("[Motujam] 배경 영상을 재생하지 못했습니다: " + message); enabled = false; };
            player.Prepare();
        }

        /// <summary>매 프레임 현재 곡 시간(초)을 넣어 준다.</summary>
        public void Sync(double songSeconds)
        {
            if (!enabled || !player.isPrepared || songSeconds < 0) return;

            if (!started)
            {
                started = true;
                player.time = songSeconds;
                player.Play();
                return;
            }

            // 영상이 밝아지며 나타난다
            Color c = image.color;
            if (c.a < 1f)
            {
                float a = Mathf.MoveTowards(c.a, 1f, Time.unscaledDeltaTime * 2f);
                image.color = new Color(Brightness, Brightness, Brightness, a);
            }

            // 1초마다 곡과 어긋났는지 보고, 많이 벌어졌으면 다시 맞춘다
            checkTimer += Time.unscaledDeltaTime;
            if (checkTimer >= 1f)
            {
                checkTimer = 0f;
                if (songSeconds >= player.length - 1) return;
                if (!player.isPlaying)
                {
                    // 창이 가려지는 등의 이유로 멈춰 있었다면 지금 위치에서 다시 재생한다
                    player.time = songSeconds;
                    player.Play();
                }
                else if (System.Math.Abs(player.time - songSeconds) > 0.2)
                {
                    player.time = songSeconds;
                }
            }
        }

        /// <summary>일시정지: 영상을 멈춘다. 다시 Sync 가 불리면 곡 위치에 맞춰 이어진다.</summary>
        public void Pause()
        {
            if (started && player.isPlaying) player.Pause();
            checkTimer = 1f;   // 재개하자마자 위치를 다시 맞춘다
        }

        void OnDestroy()
        {
            if (target != null)
            {
                target.Release();
                Destroy(target);
            }
        }
    }
}
