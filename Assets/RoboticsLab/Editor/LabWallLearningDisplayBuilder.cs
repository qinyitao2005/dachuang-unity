#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RoboticsLab.Editor
{
    public static class LabWallLearningDisplayBuilder
    {
        [MenuItem("Robot Lab/Update Wall Learning Display")]
        public static void UpdateScene()
        {
            Build();
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
        }

        public static LabWallLearningDisplay Build()
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play Mode before authoring the wall display.");
            var app=UnityEngine.Object.FindObjectOfType<LabTrainingApp>();
            var screen=GameObject.Find("TeachingDisplay");
            if(!app||!screen)throw new InvalidOperationException("Create TrainingSession and TeachingDisplay before the wall UI.");
            var display=screen.GetComponent<LabWallLearningDisplay>();
            if(!display)display=screen.AddComponent<LabWallLearningDisplay>();
            display.app=app;
            display.EnsureBuilt();display.RefreshDisplay();
            EditorUtility.SetDirty(display);
            EditorSceneManager.MarkSceneDirty(screen.scene);
            Debug.Log("ROBOT_WALL_LEARNING_DISPLAY_READY canvas=4.46x2.51m camera="+(app.player&&app.player.viewCamera?app.player.viewCamera.name:"unassigned"));
            return display;
        }
    }
}
#endif
