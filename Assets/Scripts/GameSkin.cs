using UnityEngine;

namespace Motujam
{
    /// <summary>
    /// 게임에 쓰이는 이미지 슬롯 모음. 비워 둔 칸은 기본 도형으로 그려진다.
    /// Assets/Resources/GameSkin 에셋을 선택하고 인스펙터에서 이미지를 끌어다 놓으면 된다.
    /// </summary>
    [CreateAssetMenu(menuName = "Motujam/Game Skin", fileName = "GameSkin")]
    public class GameSkin : ScriptableObject
    {
        [Header("배경")]
        public Sprite menuBackground;      // 곡 선택 / 결과 화면 배경 (1920x1080)
        public Sprite playBackground;      // 플레이 화면 배경 (1920x1080). 곡에 자켓이 있으면 자켓이 우선
        public Sprite trackBackground;     // 노트가 내려오는 트랙 배경

        [Header("노트")]
        public Sprite noteWhite;           // 흰색 레인 노트
        public Sprite noteBlue;            // 파란색 레인 노트
        public Sprite longBodyWhite;       // 흰색 롱노트 몸통 (세로로 늘어남)
        public Sprite longBodyBlue;        // 파란색 롱노트 몸통

        [Header("트랙 장식")]
        public Sprite judgeLine;           // 판정선
        public Sprite keyGlow;             // 키를 누를 때 레인에 올라오는 빛
        public Sprite hitEffect;           // 노트를 맞췄을 때 판정선 위 이펙트

        [Header("판정 글자 (비우면 텍스트로 표시)")]
        public Sprite judgePerfect;
        public Sprite judgeGreat;
        public Sprite judgeGood;
        public Sprite judgeMiss;

        [Header("기본 색상 (이미지가 없을 때)")]
        public Color whiteNoteColor = new Color(0.95f, 0.95f, 1f);
        public Color blueNoteColor = new Color(0.3f, 0.7f, 1f);
        public Color judgeLineColor = new Color(1f, 0.35f, 0.55f);

        public Sprite JudgeSprite(Judge j)
        {
            switch (j)
            {
                case Judge.Perfect: return judgePerfect;
                case Judge.Great: return judgeGreat;
                case Judge.Good: return judgeGood;
                default: return judgeMiss;
            }
        }
    }
}
