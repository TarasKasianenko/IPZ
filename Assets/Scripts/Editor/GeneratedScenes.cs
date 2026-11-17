using System.IO;
using IPZ.AirHockey;
using IPZ.Lobby;
using IPZ.Maze;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace IPZ.Style
{
    [InitializeOnLoad]
    public static class GeneratedScenes
    {
        const int Version = 9;
        const string StampPath = "Library/IPZGeneratedScenes.txt";

        static GeneratedScenes()
        {
            if (Application.isBatchMode)
                return;
            EditorApplication.delayCall += RebuildIfOutdated;
        }

        static void RebuildIfOutdated()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += RebuildIfOutdated;
                return;
            }
            if (File.Exists(StampPath) && File.ReadAllText(StampPath).Trim() == Version.ToString())
                return;

            RebuildAll();
        }

        [MenuItem("IPZ/Rebuild All Scenes")]
        public static void RebuildAll()
        {
            var active = SceneManager.GetActiveScene().path;
            GameSceneBuilder.Build();
            MazeSceneBuilder.Build();
            LobbySceneBuilder.Build();
            File.WriteAllText(StampPath, Version.ToString());
            if (!string.IsNullOrEmpty(active) && active != "Assets/Lobby/Lobby.unity" && File.Exists(active))
                EditorSceneManager.OpenScene(active);
            Debug.Log("IPZ: Lobby and Game scenes rebuilt.");
        }
    }
}
