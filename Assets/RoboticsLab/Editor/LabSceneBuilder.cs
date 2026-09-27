using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Video;
using UnityEngine.Rendering;

namespace RoboticsLab.Editor
{
    public static class LabSceneBuilder
    {
        public const string ScenePath="Assets/RoboticsLab/Scenes/RoboticsTrainingRoom.unity";
        [MenuItem("Robot Lab/Build Teaching Room")]
        public static void Build()
        {
            if(EditorApplication.isPlaying)throw new Exception("Stop Play Mode before generating the scene.");
            Directory.CreateDirectory("Assets/RoboticsLab/Scenes");
            Directory.CreateDirectory("Assets/RoboticsLab/Generated");
            Directory.CreateDirectory("Assets/StreamingAssets");
            LabRobotBuilder.ExtractOriginal();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            LabEnvironmentBuilder.Build();
            var stations=LabRobotBuilder.Build();
            var player=CreatePlayer();
            var app=new GameObject("TrainingSession").AddComponent<LabTrainingApp>();
            app.player=player;app.stations=stations;
            app.uiFont=AssetDatabase.LoadAssetAtPath<Font>("Assets/RoboticsLab/Fonts/NotoSansSC-Regular.ttf");
            var pickup=GameObject.Find("HelmetPickup");app.helmetPickup=pickup?pickup.transform:null;
            var teaching=new GameObject("TeachingInteractionPoint");teaching.transform.position=new Vector3(6.9f,0,10.5f);app.teachingPoint=teaching.transform;
            var video=new GameObject("TeachingVideoPlayer").AddComponent<VideoPlayer>();
            video.playOnAwake=false;video.waitForFirstFrame=true;video.isLooping=false;
            video.source=VideoSource.Url;video.url=Path.Combine(Application.streamingAssetsPath,"TrainingLesson.mp4");
            video.audioOutputMode=VideoAudioOutputMode.None;video.renderMode=VideoRenderMode.RenderTexture;
            var rt=AssetDatabase.LoadAssetAtPath<RenderTexture>("Assets/RoboticsLab/Generated/TeachingVideo.renderTexture");
            if(!rt){rt=new RenderTexture(1280,720,0);rt.name="TeachingVideo";AssetDatabase.CreateAsset(rt,"Assets/RoboticsLab/Generated/TeachingVideo.renderTexture");}
            video.targetTexture=rt;app.lessonVideo=video;
            const string videoMatPath="Assets/RoboticsLab/Generated/VideoScreen.mat";
            var videoMat=AssetDatabase.LoadAssetAtPath<Material>(videoMatPath);
            if(!videoMat){videoMat=new Material(Shader.Find("Unlit/Texture"));AssetDatabase.CreateAsset(videoMat,videoMatPath);}
            videoMat.mainTexture=rt;EditorUtility.SetDirty(videoMat);app.videoScreenMaterial=videoMat;
            const string json="{\"questions\":[{\"question\":\"在本实训室，进入机械臂操作前应该先做什么？\",\"options\":[\"直接操作机械臂\",\"戴好安全帽，完成学习和问答\",\"按住方向键让机械臂自动运行\"],\"correct\":1,\"explanation\":\"先完成安全准备与学习，再进入工位。\"},{\"question\":\"在机械臂操作模式中，Q / E 控制什么？\",\"options\":[\"末端工具升降\",\"人物前后走动\",\"切换安全帽颜色\"],\"correct\":0,\"explanation\":\"WASD 控制平移，Q / E 控制末端升降。\"},{\"question\":\"套圈练习中，发现操作不符合预期，应如何先暂停机械臂？\",\"options\":[\"加快移动速度\",\"继续移动并忽略提示\",\"按空格或点击界面的急停按钮\"],\"correct\":2,\"explanation\":\"本仿真通过空格与面板按钮切换急停状态。\"}]}";
            string qPath="Assets/RoboticsLab/Questions.json";
            if(!File.Exists(qPath)) File.WriteAllText(qPath,json,new System.Text.UTF8Encoding(false));
            if(!File.Exists("Assets/StreamingAssets/Questions.json"))File.WriteAllText("Assets/StreamingAssets/Questions.json",json,new System.Text.UTF8Encoding(false));
            AssetDatabase.ImportAsset(qPath);app.questionJson=AssetDatabase.LoadAssetAtPath<TextAsset>(qPath);
            app.wallLearningDisplay=LabWallLearningDisplayBuilder.Build();
            QualitySettings.antiAliasing=4;QualitySettings.vSyncCount=1;
            QualitySettings.shadowDistance=40;QualitySettings.shadowResolution=ShadowResolution.High;
            PlayerSettings.companyName="Robotics Training Team";PlayerSettings.productName="Robot Lab - Virtual Training";
            PlayerSettings.defaultScreenWidth=1600;PlayerSettings.defaultScreenHeight=900;
            PlayerSettings.fullScreenMode=FullScreenMode.FullScreenWindow;PlayerSettings.runInBackground=true;
            EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene(ScenePath,true)};
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(),ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log("ROBOT_LAB_SCENE_READY "+ScenePath+" stations="+stations.Length);
        }
        static LabThirdPerson CreatePlayer()
        {
            var root=new GameObject("Trainee");root.transform.position=new Vector3(0,.08f,2.1f);
            var cc=root.AddComponent<CharacterController>();cc.height=1.75f;cc.radius=.28f;cc.center=new Vector3(0,.88f,0);cc.stepOffset=.25f;
            var move=root.AddComponent<LabThirdPerson>();
            var camera=new GameObject("Main Camera").AddComponent<Camera>();camera.tag="MainCamera";
            camera.nearClipPlane=.08f;camera.farClipPlane=90;camera.fieldOfView=61;camera.allowHDR=true;camera.backgroundColor=new Color(.71f,.8f,.87f);
            camera.transform.position=new Vector3(0,2.5f,-1.25f);camera.transform.LookAt(root.transform.position+Vector3.up*1.25f);
            camera.gameObject.AddComponent<AudioListener>();move.viewCamera=camera;
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Jogging.fbx");
            if(prefab)
            {
                var avatar=(GameObject)PrefabUtility.InstantiatePrefab(prefab);avatar.name="TraineeVisual";
                avatar.transform.SetParent(root.transform,false);
                var bounds=new Bounds();bool first=true;
                foreach(var r in avatar.GetComponentsInChildren<Renderer>()){if(first){bounds=r.bounds;first=false;}else bounds.Encapsulate(r.bounds);}
                float factor=1.72f/Mathf.Max(.01f,bounds.size.y);avatar.transform.localScale*=factor;
                bounds=new Bounds();first=true;foreach(var r in avatar.GetComponentsInChildren<Renderer>()){if(first){bounds=r.bounds;first=false;}else bounds.Encapsulate(r.bounds);}
                avatar.transform.position+=new Vector3(root.transform.position.x-bounds.center.x,root.transform.position.y-bounds.min.y,root.transform.position.z-bounds.center.z);
                foreach(var c in avatar.GetComponentsInChildren<Collider>())UnityEngine.Object.DestroyImmediate(c);
                var anim=avatar.GetComponent<Animator>();if(!anim)anim=avatar.AddComponent<Animator>();
                var clip=AssetDatabase.LoadAllAssetsAtPath("Assets/Jogging.fbx").OfType<AnimationClip>().FirstOrDefault(c=>!c.name.StartsWith("__preview"));
                anim.runtimeAnimatorController=LabAvatarAnimationBuilder.Build(anim,clip);anim.applyRootMotion=false;anim.cullingMode=AnimatorCullingMode.AlwaysAnimate;
                move.animator=anim;move.visual=avatar.transform;
                var head=avatar.GetComponentsInChildren<Transform>().FirstOrDefault(t=>t.name.EndsWith("Head",StringComparison.OrdinalIgnoreCase));
                move.head=head;
                var original=GameObject.Find("HelmetPickup");
                if(original)
                {
                    var helmet=UnityEngine.Object.Instantiate(original);helmet.name="WornSafetyHelmet";
                    foreach(var c in helmet.GetComponentsInChildren<Collider>())UnityEngine.Object.DestroyImmediate(c);
                    // Normalize the cloned prop in world space before attaching to a potentially scaled skeleton.
                    var hr=helmet.GetComponentsInChildren<Renderer>();var hb=hr[0].bounds;foreach(var r in hr)hb.Encapsulate(r.bounds);
                    helmet.transform.localScale*=.31f/Mathf.Max(hb.size.x,hb.size.z);
                    var anchor=new GameObject("HelmetAnchor");anchor.transform.position=root.transform.position+Vector3.up*1.71f;
                    anchor.transform.SetParent(head?head:avatar.transform,true);
                    helmet.transform.SetParent(anchor.transform,true);helmet.transform.position=anchor.transform.position;
                    // Recenter using render bounds, not the shelf prop pivot.
                    hr=helmet.GetComponentsInChildren<Renderer>();hb=hr[0].bounds;foreach(var r in hr)hb.Encapsulate(r.bounds);
                    helmet.transform.position+=anchor.transform.position-new Vector3(hb.center.x,hb.min.y,hb.center.z);
                    move.wornHelmet=helmet;helmet.SetActive(false);
                    LabAvatarEquipmentAuthoring.Fit(move);
                }
            }
            return move;
        }
        [MenuItem("Robot Lab/Build Windows Demo")]
        public static void BuildPlayer()
        {
            Directory.CreateDirectory("Builds/Windows");
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{ScenePath},locationPathName="Builds/Windows/RobotLab.exe",target=BuildTarget.StandaloneWindows64,options=BuildOptions.None});
            File.WriteAllText("Documentation/BuildReport.txt",report.summary.result+"\nErrors: "+report.summary.totalErrors+"\nWarnings: "+report.summary.totalWarnings+"\nSize: "+report.summary.totalSize);
            Debug.Log("ROBOT_LAB_BUILD_"+report.summary.result);
        }
    }
}
