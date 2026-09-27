using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace RoboticsLab.Editor
{
    public static class LabDiagnostics
    {
        [MenuItem("Robot Lab/Diagnostics/Write Scene Snapshot")]
        public static void Snapshot()
        {
            var stationList=UnityEngine.Object.FindObjectsOfType<LabRobotStation>();
            var text=new System.Text.StringBuilder();
            text.AppendLine("Scene: "+UnityEngine.SceneManagement.SceneManager.GetActiveScene().path);
            foreach(var s in stationList)
            {
                text.AppendLine("STATION "+s.name+" @ "+s.transform.position);
                foreach(var t in s.GetComponentsInChildren<Transform>())
                    if(t.GetComponent<Renderer>() || t.GetComponent<MonoBehaviour>())text.AppendLine(t.name+" @ "+t.position+" | "+t.localScale);
            }
            var app=UnityEngine.Object.FindObjectOfType<LabTrainingApp>();if(app)text.AppendLine(app.StateSummary);
            Directory.CreateDirectory("Documentation");File.WriteAllText("Documentation/SceneSnapshot.txt",text.ToString());
            Debug.Log("ROBOT_LAB_SNAPSHOT_READY");
        }
        [MenuItem("Robot Lab/Capture/Room Overview")]
        public static void CaptureOverview(){Capture("RoomOverview",new Vector3(6.1f,3.55f,1.3f),new Vector3(-2.2f,1.65f,12.1f),65);}
        [MenuItem("Robot Lab/Capture/Teaching Zone")]
        public static void CaptureTeaching(){Capture("TeachingZone",new Vector3(-.5f,2.9f,4.3f),new Vector3(6.6f,1.9f,10.2f),65);}
        [MenuItem("Robot Lab/Capture/Robot Workstations")]
        public static void CaptureStations(){Capture("RobotWorkstations",new Vector3(1.1f,2.65f,2.7f),new Vector3(-4.4f,1.4f,9.6f), sixty());}
        static float sixty(){return 60;}
        static void Capture(string name,Vector3 pos,Vector3 target,float fov)
        {
            Directory.CreateDirectory("Documentation/Screenshots");
            var cam=new GameObject("TemporaryCaptureCamera").AddComponent<Camera>();cam.transform.position=pos;cam.transform.LookAt(target);cam.fieldOfView=fov;cam.farClipPlane=90;
            cam.clearFlags=CameraClearFlags.Skybox;cam.allowHDR=false;
            var rt=new RenderTexture(1920,1080,24);rt.antiAliasing=4;cam.targetTexture=rt;cam.Render();
            var old=RenderTexture.active;RenderTexture.active=rt;var tex=new Texture2D(1920,1080,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,1920,1080),0,0);tex.Apply();
            File.WriteAllBytes("Documentation/Screenshots/"+name+".png",tex.EncodeToPNG());RenderTexture.active=old;cam.targetTexture=null;rt.Release();
            UnityEngine.Object.DestroyImmediate(tex);UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(cam.gameObject);
            Debug.Log("ROBOT_LAB_CAPTURE "+name);
        }
        static IEnumerator<int> validation;
        static List<string> results=new List<string>();
        [MenuItem("Robot Lab/Diagnostics/Validate Play Session")]
        public static void Validate()
        {
            if(!EditorApplication.isPlaying)throw new Exception("Enter Play Mode first.");
            results.Clear();validation=RunChecks();EditorApplication.update-=Tick;EditorApplication.update+=Tick;
        }
        static void Tick()
        {
            try { if(validation!=null&&validation.MoveNext())return; }
            catch(Exception e){results.Add("FAIL: "+e);Debug.LogException(e);}
            EditorApplication.update-=Tick;validation=null;
            Directory.CreateDirectory("Documentation");File.WriteAllLines("Documentation/Validation.txt",results);
            Debug.Log("ROBOT_LAB_VALIDATION_FINISHED "+string.Join(" | ",results));
        }
        static void Check(bool pass,string text){results.Add((pass?"PASS: ":"FAIL: ")+text);if(!pass)throw new Exception(text);}
        static IEnumerator<int> RunChecks()
        {
            var app=UnityEngine.Object.FindObjectOfType<LabTrainingApp>();
            Check(app && app.player && app.stations.Length==3,"Third person player and three stations exist");
            app.ClosePanel();
            Check(!app.TryStartStation(0),"Robot operation blocked before safety and quiz");
            var helmetPosition=app.helmetPickup.position;
            app.player.Teleport(new Vector3(helmetPosition.x-.85f,.08f,helmetPosition.z),90);
            app.EquipHelmet();
            double helmetUntil=EditorApplication.timeSinceStartup+5;
            while(app.player.IsEquippingHelmet && EditorApplication.timeSinceStartup<helmetUntil)yield return 0;
            Check(app.HelmetWorn && app.player.wornHelmet.activeInHierarchy,"Helmet is worn on the visible character after the equip animation");
            Check(!app.TryStartStation(0),"Helmet alone does not bypass quiz");
            app.player.Teleport(new Vector3(4.9f,.08f,10.5f),90);
            app.OpenLesson();
            double until=EditorApplication.timeSinceStartup+3;
            while(EditorApplication.timeSinceStartup<until)yield return 0;
            Check(app.lessonVideo && app.lessonVideo.isPrepared && app.lessonVideo.frame>0,"Example MP4 decodes and advances frames");
            // Check the independent text-course route and wrong-answer retry path.
            app.LessonCompleted=true;app.StartQuiz();
            Check(!app.SubmitAnswer(0) && app.CorrectAnswers==0,"Incorrect quiz answer does not unlock or increment score");
            Check(app.SubmitAnswer(1),"Safety question accepts correct answer");app.NextQuestion();
            Check(app.SubmitAnswer(0),"Control question accepts correct answer");app.NextQuestion();
            Check(app.SubmitAnswer(2),"Emergency stop question accepts correct answer");app.NextQuestion();
            Check(app.QuizPassed && app.CorrectAnswers==3,"All quiz questions unlock practice");
            for(int i=0;i<3;i++)
            {
                Check(app.TryStartStation(i),"Station "+(i+1)+" can be selected");
                var s=app.ActiveStation;s.SetMode(0);s.ResetExercise();
                var ts=(s.joints??new JointConfig[0]).Where(j=>j!=null&&j.joint).Select(j=>j.joint).Distinct().ToArray();var rotations=ts.Select(t=>t.localRotation).ToArray();
                until=EditorApplication.timeSinceStartup+1;
                while(EditorApplication.timeSinceStartup<until){s.MoveTool(new Vector3(.45f,0,.35f));yield return 0;}
                bool changed=false;for(int j=0;j<ts.Length;j++)if(Quaternion.Angle(rotations[j],ts[j].localRotation)>.2f)changed=true;
                Check(changed,"Station "+(i+1)+" original robot joint transforms respond to tool input");
                s.SetEmergencyStop(true);
                yield return 0;yield return 0;
                rotations=ts.Select(t=>t.localRotation).ToArray();
                until=EditorApplication.timeSinceStartup+.4;
                while(EditorApplication.timeSinceStartup<until){s.MoveTool(Vector3.one);yield return 0;}
                float max=0;for(int j=0;j<ts.Length;j++)max=Mathf.Max(max,Quaternion.Angle(rotations[j],ts[j].localRotation));
                Check(max<.1f,"Station "+(i+1)+" emergency stop freezes all joint motion");
                s.SetEmergencyStop(false);s.SetMode(1);s.ResetExercise();
                Check(s.Contacts==0 && s.Progress<=.01f,"Station "+(i+1)+" ring exercise resets contacts and progress");
                app.ClosePanel();
            }
            app.player.Teleport(new Vector3(0,.1f,2.1f),0);app.ClosePanel();
            Check(app.player && !app.ActiveStation,"Exiting station returns to roaming");
            results.Add("NOTE: This integration run uses the text-course completion state for quiz setup; video decoding was tested separately.");
        }
    }
}
