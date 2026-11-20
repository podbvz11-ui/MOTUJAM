using UnityEngine;

namespace Motujam
{
    public enum Judge { Perfect = 0, Great = 1, Good = 2, Miss = 3 }

    public static class JudgeRule
    {
        // 판정 범위 (±ms)
        public const double PerfectMs = 45;
        public const double GreatMs = 90;
        public const double GoodMs = 135;

        public static Judge FromDiff(double absMs)
        {
            if (absMs <= PerfectMs) return Judge.Perfect;
            if (absMs <= GreatMs) return Judge.Great;
            if (absMs <= GoodMs) return Judge.Good;
            return Judge.Miss;
        }

        public static double Weight(Judge j)
        {
            switch (j)
            {
                case Judge.Perfect: return 1.0;
                case Judge.Great: return 0.7;
                case Judge.Good: return 0.4;
                default: return 0.0;
            }
        }

        public static string Name(Judge j)
        {
            switch (j)
            {
                case Judge.Perfect: return "PERFECT";
                case Judge.Great: return "GREAT";
                case Judge.Good: return "GOOD";
                default: return "MISS";
            }
        }

        public static Color ColorOf(Judge j)
        {
            switch (j)
            {
                case Judge.Perfect: return new Color(1f, 0.9f, 0.3f);
                case Judge.Great: return new Color(0.4f, 1f, 0.5f);
                case Judge.Good: return new Color(0.4f, 0.7f, 1f);
                default: return new Color(1f, 0.35f, 0.35f);
            }
        }

        public static string Rank(float accuracy)
        {
            if (accuracy >= 95f) return "S";
            if (accuracy >= 90f) return "A";
            if (accuracy >= 80f) return "B";
            if (accuracy >= 70f) return "C";
            return "D";
        }
    }

    public class PlayResult
    {
        public SongData song;
        public ChartData chart;
        public int[] counts = new int[4];
        public int score;
        public int maxCombo;
        public float accuracy;
        public bool FullCombo => counts[(int)Judge.Miss] == 0;
    }

    /// <summary>PlayerPrefs에 저장되는 플레이어 설정.</summary>
    public static class Settings
    {
        public static float Speed
        {
            get => PlayerPrefs.GetFloat("speed", 2.5f);
            set => PlayerPrefs.SetFloat("speed", Mathf.Clamp(Mathf.Round(value * 10f) / 10f, 0.5f, 8f));
        }

        /// <summary>자동 연주. 채보가 음악과 맞는지 눈으로 확인할 때 쓴다. 저장하지 않고 실행할 때마다 꺼진 상태로 시작한다.</summary>
        public static bool Auto;

        // Play를 누를 때마다 자동 연주를 끈다 (에디터에서 도메인 리로드를 꺼 두면 static 값이 남아 있기 때문)
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlay() => Auto = false;

        /// <summary>플레이 중 배경 영상을 틀지 여부.</summary>
        public static bool Video
        {
            get => PlayerPrefs.GetInt("video", 1) != 0;
            set => PlayerPrefs.SetInt("video", value ? 1 : 0);
        }

        /// <summary>판정 보정값(ms). 노트가 음악보다 빨리 온다고 느껴지면 +로 올린다.</summary>
        public static int OffsetMs
        {
            get => PlayerPrefs.GetInt("offset", 0);
            set => PlayerPrefs.SetInt("offset", Mathf.Clamp(value, -500, 500));
        }
    }
}
