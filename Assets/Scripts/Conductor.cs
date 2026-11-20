using System;
using UnityEngine;

namespace Motujam
{
    /// <summary>오디오 재생과 곡 시간 계산. 오디오 하드웨어 시계(dspTime)에 맞춰 보정한다.</summary>
    public class Conductor : MonoBehaviour
    {
        AudioSource src;
        double startDsp;
        double time;
        double latency;
        bool running;

        /// <summary>지금 "귀에 들리고 있는" 곡 위치 (ms).</summary>
        public double TimeMs => (time - latency) * 1000.0;
        public bool Running => running;

        /// <summary>지금 틀고 있는 곡의 길이(ms). 곡이 없으면 0.</summary>
        public double LengthMs => src != null && src.clip != null ? src.clip.length * 1000.0 : 0.0;
        public double LatencyMs => latency * 1000.0;

        void Awake()
        {
            src = gameObject.AddComponent<AudioSource>();
            src.playOnAwake = false;

            // 오디오는 믹서 버퍼를 거쳐 스피커로 나가므로, 엔진 시계보다 소리가 조금 늦게 들린다.
            // 그만큼 곡 시간을 늦춰야 노트가 들리는 소리와 맞는다. (버퍼 지연 + 장치 지연 추정치)
            AudioSettings.GetDSPBufferSize(out int bufferLength, out int numBuffers);
            latency = (double)bufferLength * numBuffers / AudioSettings.outputSampleRate + 0.015;
        }

        public void Play(AudioClip clip, double leadInSeconds)
        {
            src.Stop();
            src.clip = clip;
            startDsp = AudioSettings.dspTime + leadInSeconds;
            src.PlayScheduled(startDsp);
            time = -leadInSeconds;
            running = true;
        }

        public void Stop()
        {
            src.Stop();
            running = false;
            Paused = false;
        }

        public bool Paused { get; private set; }

        /// <summary>곡을 멈춘다. 멈춘 위치는 기억해 둔다.</summary>
        public void Pause()
        {
            if (!running) return;
            src.Stop();
            running = false;
            Paused = true;
        }

        /// <summary>
        /// 멈춘 곳에서 다시 튼다. 곡 시간이 countdownSeconds 만큼 뒤로 감겼다가 흘러와서,
        /// 멈췄던 바로 그 순간에 소리가 다시 시작된다 (노트가 잠깐 위로 물러났다가 다시 내려온다).
        /// 돌려주는 값: 소리가 다시 시작되는 곡 시간(ms).
        /// </summary>
        public double Resume(double countdownSeconds)
        {
            if (!Paused) return TimeMs;
            double pausedAt = time;
            double audioPos = Math.Max(0.0, pausedAt);
            double startAt = AudioSettings.dspTime + countdownSeconds + Math.Max(0.0, -pausedAt);
            src.time = (float)Math.Min(audioPos, Math.Max(0.0, src.clip.length - 0.05));
            src.PlayScheduled(startAt);
            startDsp = startAt - audioPos;
            time = AudioSettings.dspTime - startDsp;
            running = true;
            Paused = false;
            return (pausedAt - latency) * 1000.0;
        }

        /// <summary>매 프레임 한 번, 판정 처리 전에 호출한다.</summary>
        public void Tick()
        {
            if (!running) return;
            time += Time.unscaledDeltaTime;
            double err = (AudioSettings.dspTime - startDsp) - time;
            if (Math.Abs(err) > 0.08) time += err;   // 크게 어긋나면(프레임 멈춤 등) 바로 맞춘다
            else time += err * 0.05;                 // 평소엔 조금씩 보정해 떨림을 없앤다
        }
    }
}
