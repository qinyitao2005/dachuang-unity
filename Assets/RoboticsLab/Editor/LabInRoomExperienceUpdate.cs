using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RoboticsLab.Editor
{
    public static class LabInRoomExperienceUpdate
    {
        [MenuItem("Robot Lab/Update Character And In Room Learning")]
        public static void UpdateScene()
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play Mode first.");
            var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if(scene.path!=LabSceneBuilder.ScenePath)throw new InvalidOperationException("Open the teaching room before updating.");
            LabAvatarEquipmentAuthoring.UpdateScene();
            var app=UnityEngine.Object.FindObjectOfType<LabTrainingApp>();
            app.wallLearningDisplay=LabWallLearningDisplayBuilder.Build();
            app.helmetPickup=GameObject.Find("HelmetPickup").transform;
            app.panel="";app.HelmetWorn=false;app.LessonCompleted=false;app.QuizPassed=false;app.CorrectAnswers=0;app.questionIndex=0;
            app.player.learningFocus=false;app.player.focusMode=false;
            app.wallLearningDisplay.RefreshDisplay();
            EditorUtility.SetDirty(app);EditorUtility.SetDirty(app.player);
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
            Debug.Log("ROBOT_IN_ROOM_EXPERIENCE_UPDATED natural walk, slim body, nearest helmet, physical wall lessons");
        }
    }
}
