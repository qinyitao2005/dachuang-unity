using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;

namespace RoboticsLab.Editor
{
    public static class LabPendantInstaller
    {
        public const string PrefabPath="Assets/RoboticsLab/Generated/TeachingPendant.prefab";
        [Serializable] class BuildEvidence
        {
            public string result,unityVersion;
            public int errors,warnings;
            public string[] warningMessages;
        }
        public static void BuildDemo()
        {
            LabSceneBuilder.BuildPlayer();
            var report=UnityEditor.Build.Reporting.BuildReport.GetLatestReport();
            var evidence=new BuildEvidence{result=report.summary.result.ToString(),unityVersion=Application.unityVersion,
                errors=(int)report.summary.totalErrors,warnings=(int)report.summary.totalWarnings,
                warningMessages=report.steps.SelectMany(step=>step.messages).Where(message=>message.type==LogType.Warning).Select(message=>message.content).Distinct().ToArray()};
            File.WriteAllText("Documentation/PendantBuildReport.json",JsonUtility.ToJson(evidence,true));
            if(report.summary.result!=UnityEditor.Build.Reporting.BuildResult.Succeeded)throw new InvalidOperationException("Pendant demo build failed");
        }
        [MenuItem("Robot Lab/Install Teaching Pendant")]
        public static void Install()
        {
            var main=EditorSceneManager.OpenScene(LabSceneBuilder.ScenePath,OpenSceneMode.Single);
            var sourceScene=EditorSceneManager.OpenScene("Assets/控制器.unity",OpenSceneMode.Additive);
            var preview=EditorSceneManager.NewPreviewScene();
            GameObject clone=null;
            try
            {
                var source=sourceScene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true))
                    .First(t=>t.name=="UIRoot");
                clone=Object.Instantiate(source.gameObject);clone.name="TeachingPendant";
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(clone,preview);
                foreach(var menu in clone.GetComponentsInChildren<ScreenMenuNavigator>(true))
                    if(menu)Object.DestroyImmediate(menu.gameObject);
                // The original deadman overlays are full-frame 4K images. They have no
                // recording action in this view; the printed key remains in the housing art.
                var unusedDeadman=clone.transform.Find("MENU/Deadman");
                if(unusedDeadman)Object.DestroyImmediate(unusedDeadman.gameObject);
                foreach(var image in clone.GetComponentsInChildren<UnityEngine.UI.Graphic>(true))image.raycastTarget=false;
                var prefab=PrefabUtility.SaveAsPrefabAsset(clone,PrefabPath);
                if(!prefab)throw new InvalidOperationException("Unable to save imported pendant prefab");
            }
            finally
            {
                if(clone)Object.DestroyImmediate(clone);
                EditorSceneManager.ClosePreviewScene(preview);
                EditorSceneManager.CloseScene(sourceScene,true);
            }
            UnityEngine.SceneManagement.SceneManager.SetActiveScene(main);
            var app=Object.FindObjectOfType<LabTrainingApp>();
            if(!app)throw new InvalidOperationException("Training app is missing");
            var controller=app.GetComponent<LabPendantController>()??app.gameObject.AddComponent<LabPendantController>();
            controller.app=app;controller.pendantPrefab=AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            app.pendantController=controller;
            EditorUtility.SetDirty(controller);EditorUtility.SetDirty(app);
            EditorSceneManager.MarkSceneDirty(main);EditorSceneManager.SaveScene(main);
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory("Documentation");
            File.WriteAllText("Documentation/PendantAssetValidation.txt",
                "Teaching pendant installed / "+DateTime.UtcNow.ToString("O")+"\n"+
                "Source: Assets/控制器.unity (from kongzhiqi controller project)\n"+
                "Prefab: "+PrefabPath+"\nScene: "+main.path+"\n"+
                "Imported sprites: "+AssetDatabase.FindAssets("t:Sprite",new[]{"Assets/ControllerUI/Sprites"}).Length+"\n"+
                "Controller model/view/jog are separate components.\n"+
                "Legacy menus omitted only from integrated prefab; original controller scene retained.\n");
            Debug.Log("PENDANT_INSTALL_SUCCESS");
        }
    }
}
