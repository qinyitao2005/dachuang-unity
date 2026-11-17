using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RoboticsLab.Editor
{
    /// <summary>Updates only room finishes in the existing team scene, then saves it.</summary>
    public static class LabRoomRefinishDelivery
    {
        public static void ApplyAndBuild()
        {
            Apply();
            LabSceneBuilder.BuildPlayer();
        }

        public static void Apply()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
            var scene = EditorSceneManager.OpenScene(LabSceneBuilder.ScenePath, OpenSceneMode.Single);
            if (!UnityEngine.Object.FindObjectOfType<LabTrainingApp>())
                throw new InvalidOperationException("Existing training application is missing; refusing to rebuild.");
            LabInteriorRefinishBuilder.ApplyToOpenScene();
            LabBackgroundMusicImporter.ConfigureImport();
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save the existing training scene.");
            AssetDatabase.SaveAssets();
            Debug.Log("ROBOT_ROOM_REFINISH_SAVED " + scene.path);
        }
    }
}
