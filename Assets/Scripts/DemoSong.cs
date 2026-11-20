using System.Collections.Generic;
using UnityEngine;

namespace Motujam
{
    /// <summary>음원 없이도 테스트할 수 있도록 코드로 만든 데모 곡(비트 + 채보).</summary>
    public static class DemoSong
    {
        const float Bpm = 120f;
        const int Beats = 96;

        public static SongData Create()
        {
            var song = new SongData { title = "데모 트랙", artist = "Motujam", bpm = Bpm, audio = "", offset = 0 };
            song.charts.Add(BuildChart(4, "Normal", 3, 11));
            song.charts.Add(BuildChart(6, "Normal", 5, 23));
            return song;
        }

        static ChartData BuildChart(int keys, string difficulty, int level, int seed)
        {
            var chart = new ChartData { keys = keys, difficulty = difficulty, level = level };
            var rng = new System.Random(seed);
            float beatMs = 60000f / Bpm;
            var busyUntil = new float[keys];
            int lastLane = -1;

            for (int b = 8; b < Beats - 4; b++)
            {
                float t = b * beatMs;

                int lane = PickLane(rng, keys, busyUntil, t, lastLane);
                if (lane >= 0)
                {
                    bool isLong = b % 8 == 4 && rng.Next(3) > 0;
                    int len = isLong ? Mathf.RoundToInt(beatMs * 1.5f) : 0;
                    chart.notes.Add(new NoteData { t = Mathf.RoundToInt(t), lane = lane, len = len });
                    busyUntil[lane] = t + len + 1;
                    lastLane = lane;
                }

                // 뒤쪽 절반부터 8분음표 추가
                if (b >= 40 && rng.Next(100) < (keys == 6 ? 65 : 45))
                {
                    float t2 = t + beatMs * 0.5f;
                    int lane2 = PickLane(rng, keys, busyUntil, t2, lastLane);
                    if (lane2 >= 0)
                    {
                        chart.notes.Add(new NoteData { t = Mathf.RoundToInt(t2), lane = lane2 });
                        busyUntil[lane2] = t2 + 1;
                        lastLane = lane2;
                    }
                }

                // 가끔 동시치기
                if (b >= 24 && b % 4 == 0 && rng.Next(100) < 50)
                {
                    int lane3 = PickLane(rng, keys, busyUntil, t, lastLane);
                    if (lane3 >= 0)
                    {
                        chart.notes.Add(new NoteData { t = Mathf.RoundToInt(t), lane = lane3 });
                        busyUntil[lane3] = t + 1;
                    }
                }
            }

            chart.Sanitize();
            return chart;
        }

        static int PickLane(System.Random rng, int keys, float[] busyUntil, float t, int avoid)
        {
            var free = new List<int>();
            for (int i = 0; i < keys; i++)
                {
                if (busyUntil[i] <= t && i != avoid) free.Add(i);
            }
            if (free.Count == 0) return -1;
            return free[rng.Next(free.Count)];
        }

        /// <summary>킥 / 스네어 / 하이햇 / 베이스 / 아르페지오로 된 간단한 반주를 합성한다.</summary>
        public static AudioClip CreateClip(float bpm, float seconds)
        {
            const int sr = 44100;
            int n = Mathf.CeilToInt(sr * seconds);
            var data = new float[n];
            float beat = 60f / bpm;
            int beats = Mathf.FloorToInt(seconds / beat);
            var rng = new System.Random(7);
            int[] roots = { 57, 53, 48, 55 };          // A F C G (MIDI)
            int[] arp = { 0, 7, 12, 16, 12, 7, 19, 12 };

            for (int b = 0; b < beats; b++)
            {
                int s0 = Mathf.RoundToInt(b * beat * sr);
                int root = roots[(b / 4) % roots.Length];

                // 킥
                double phase = 0;
                for (int i = 0; i < sr * 0.3f && s0 + i < n; i++)
                {
                    float tt = i / (float)sr;
                    float f = 45f + 110f * Mathf.Exp(-tt * 28f);
                    phase += 2 * Mathf.PI * f / sr;
                    data[s0 + i] += Mathf.Sin((float)phase) * Mathf.Exp(-tt * 14f) * 0.75f;
                }

                // 스네어 (2, 4박)
                if (b % 2 == 1)
                {
                    for (int i = 0; i < sr * 0.2f && s0 + i < n; i++)
                    {
                        float tt = i / (float)sr;
                        float noise = (float)(rng.NextDouble() * 2 - 1);
                        data[s0 + i] += (noise * 0.28f + Mathf.Sin(2 * Mathf.PI * 190f * tt) * 0.15f) * Mathf.Exp(-tt * 22f);
                    }
                }

                // 하이햇 (8분)
                for (int h = 0; h < 2; h++)
                {
                    int hs = s0 + Mathf.RoundToInt(h * beat * 0.5f * sr);
                    for (int i = 0; i < sr * 0.05f && hs + i < n; i++)
                    {
                        float tt = i / (float)sr;
                        data[hs + i] += (float)(rng.NextDouble() * 2 - 1) * Mathf.Exp(-tt * 90f) * (h == 0 ? 0.08f : 0.14f);
                    }
                }

                // 베이스 + 아르페지오 (8분)
                for (int h = 0; h < 2; h++)
                {
                    int hs = s0 + Mathf.RoundToInt(h * beat * 0.5f * sr);
                    float bassF = Midi(root - 24);
                    float leadF = Midi(root + arp[(b * 2 + h) % arp.Length]);
                    int len = Mathf.RoundToInt(beat * 0.5f * sr);
                    for (int i = 0; i < len && hs + i < n; i++)
                    {
                        float tt = i / (float)sr;
                        float env = Mathf.Exp(-tt * 6f);
                        data[hs + i] += Mathf.Sin(2 * Mathf.PI * bassF * tt) * env * 0.3f;
                        if (b >= 8)
                        {
                            float tri = Mathf.PingPong(tt * leadF * 2f, 1f) * 2f - 1f;
                            data[hs + i] += tri * Mathf.Exp(-tt * 9f) * 0.16f;
                        }
                    }
                }
            }

            for (int i = 0; i < n; i++) data[i] = Mathf.Clamp(data[i], -1f, 1f);
            var clip = AudioClip.Create("DemoTrack", n, 1, sr, false);
            clip.SetData(data, 0);
            return clip;
        }

        static float Midi(int note) => 440f * Mathf.Pow(2f, (note - 69) / 12f);
    }
}
