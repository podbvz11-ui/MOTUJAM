using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Motujam.EditorTools
{
    public static class MotujamSetup
    {
        const string SkinPath = "Assets/Resources/GameSkin.asset";
        const string ScenePath = "Assets/Scenes/Game.unity";
        const string DemoJson = "Assets/StreamingAssets/Songs/Demo/song.json";

        /// <summary>게임 씬, 스킨 에셋, 예제 채보를 만든다. 이미 있는 스킨과 채보는 건드리지 않는다.</summary>
        [MenuItem("Motujam/게임 씬 만들기")]
        public static void Setup()
        {
            Directory.CreateDirectory("Assets/Resources");
            var skin = AssetDatabase.LoadAssetAtPath<GameSkin>(SkinPath);
            if (skin == null)
            {
                skin = ScriptableObject.CreateInstance<GameSkin>();
                AssetDatabase.CreateAsset(skin, SkinPath);
            }

            if (!File.Exists(DemoJson))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(DemoJson));
                File.WriteAllText(DemoJson, JsonUtility.ToJson(DemoSong.Create(), true));
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var cam = Camera.main;
            if (cam != null)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Color.black;
            }
            var go = new GameObject("GameRoot");
            go.AddComponent<GameRoot>().skin = skin;

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Motujam] 게임 씬 준비 완료: " + ScenePath);
        }
    }
}
