using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Motujam
{
    /// <summary>노트 하나. 시간 단위는 전부 밀리초(ms)이며 오디오 시작 지점이 0이다.</summary>
    [Serializable]
    public class NoteData
    {
        public int t;        // 눌러야 하는 시각 (ms)
        public int lane;     // 0부터 시작하는 레인 번호 (4키: 0~3, 6키: 0~5)
        public int len;      // 롱노트 길이 (ms). 0이면 일반 노트
    }

    [Serializable]
    public class ChartData
    {
        public int keys = 4;                 // 4 또는 6
        public string difficulty = "Normal";
        public int level = 1;
        public List<NoteData> notes = new List<NoteData>();

        /// <summary>채보를 정리한다: 잘못된 레인의 노트 제거, 시간순 정렬.</summary>
        public void Sanitize()
        {
            keys = keys == 6 ? 6 : 4;
            notes.RemoveAll(n => n.lane < 0 || n.lane >= keys);
            notes.Sort((a, b) => a.t.CompareTo(b.t));
        }
    }

    [Serializable]
    public class SongData
    {
        public string title = "Untitled";
        public string artist = "";
        public string audio = "";    // song.json과 같은 폴더의 오디오 파일명 (ogg / mp3 / wav). 비우면 데모 비트 재생
        public string jacket = "";   // 같은 폴더의 자켓 이미지 파일명 (png / jpg). 선택 사항
        public float bpm = 120f;
        public int offset = 0;       // 채보 전체를 밀거나 당기는 값 (ms). 양수면 노트가 늦게 온다
        public List<ChartData> charts = new List<ChartData>();

        [NonSerialized] public string folder;
        [NonSerialized] Texture2D jacketTex;

        /// <summary>곡 폴더에 video.mp4 가 있으면 그 경로 (플레이 배경 영상). 없으면 null.</summary>
        public string VideoPath
        {
            get
            {
                if (string.IsNullOrEmpty(folder)) return null;
                string p = Path.Combine(folder, "video.mp4");
                return File.Exists(p) ? p : null;
            }
        }

        public Texture2D GetJacket()
        {
            if (jacketTex == null && !string.IsNullOrEmpty(jacket) && !string.IsNullOrEmpty(folder))
            {
                string p = Path.Combine(folder, jacket);
                if (File.Exists(p))
                {
                    var tex = new Texture2D(2, 2);
                    if (tex.LoadImage(File.ReadAllBytes(p))) jacketTex = tex;
                }
            }
            return jacketTex;
        }
    }

    public static class SongLibrary
    {
        public static string SongsDir => Path.Combine(Application.streamingAssetsPath, "Songs");

        /// <summary>StreamingAssets/Songs/곡폴더/song.json 을 전부 읽는다.</summary>
        public static List<SongData> LoadAll()
        {
            var list = new List<SongData>();
            if (Directory.Exists(SongsDir))
            {
                foreach (string dir in Directory.GetDirectories(SongsDir))
                {
                    string file = Path.Combine(dir, "song.json");
                    if (!File.Exists(file)) continue;
                    try
                    {
                        var song = JsonUtility.FromJson<SongData>(File.ReadAllText(file));
                        if (song == null || song.charts == null || song.charts.Count == 0) continue;
                        song.folder = dir;
                        foreach (var c in song.charts)
                        {
                            c.Sanitize();
                        }
                        list.Add(song);
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[Motujam] 채보를 읽지 못했습니다: {file}\n{e.Message}");
                    }
                }
            }
            if (list.Count == 0) list.Add(DemoSong.Create());
            return list;
        }
    }
}
